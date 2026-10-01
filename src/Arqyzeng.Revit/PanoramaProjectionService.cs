using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arqyzeng.Panorama;
using Arqyzeng.Core;

namespace Arqyzeng.Revit;

internal sealed record PanoramaProjectionResult(string Path, PanoramaResult Capture, int Width, int Height);
internal static class PanoramaProjectionService
{
    private static readonly string[] Names = ["front", "right", "back", "left", "up", "down"];
    private sealed record Raster(byte[] Pixels, int Size);
    private sealed record Metadata(int SchemaVersion, string Version, string SourceView, bool Validated,
        double[] Eye, FaceData[] Faces);
    private sealed record FaceData(string Name, string File, int Width, int Height, double[] Eye,
        double[] Forward, double[] Up, double HorizontalFov, double VerticalFov,
        double RightOffsetRatio, double UpOffsetRatio, bool Validated,
        int RawWidth, int RawHeight, bool RasterNormalized, BimSceneSnapshot? Bim = null);

    internal static PanoramaProjectionResult Build(string metadataPath, CancellationToken cancellation)
    {
        string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(metadataPath))!;
        using var input = File.OpenRead(metadataPath);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Metadados maiores que o limite permitido.");
        var data = JsonSerializer.Deserialize<Metadata>(input, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Metadados vazios.");
        if (data.SchemaVersion != 2 || !data.Validated || data.Faces is not { Length: 6 } || !VectorValid(data.Eye))
            throw new InvalidOperationException("Selecione panorama.json de uma captura validada da v8.0.1 ou posterior, com seis faces.");
        if (data.Faces.Any(f => f is null) || data.Faces.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != 6)
            throw new InvalidOperationException("Faces ausentes ou repetidas.");
        FaceData front = data.Faces.SingleOrDefault(f => f.Name == "front") ?? throw new InvalidOperationException("Face front ausente.");
        if (!Unit(front.Forward) || !Unit(front.Up) || Math.Abs(Dot(front.Forward, front.Up)) > 1e-6)
            throw new InvalidOperationException("Orientacao da camera invalida.");
        double[] fwd = front.Forward, up = front.Up, right = Cross(fwd, up);
        double[][] forwards = [fwd, right, Negate(fwd), Negate(right), up, Negate(up)];
        double[][] ups = [up, up, up, up, Negate(fwd), fwd];
        int facePixels = front.Width;
        var outputSize = CubeProjection.OutputSize(facePixels);
        var rasters = new List<Raster>(); var faces = new List<PanoramaFace>();
        for (int i = 0; i < Names.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = data.Faces.SingleOrDefault(v => v.Name == Names[i]) ?? throw new InvalidOperationException("Face " + Names[i] + " ausente.");
            if (!face.Validated || face.Width != facePixels || face.Height != facePixels || !VectorValid(face.Eye) ||
                Distance(face.Eye, data.Eye) > 1e-7 || !Unit(face.Forward) || !Unit(face.Up) ||
                Dot(face.Forward, forwards[i]) < 1 - 1e-6 || Dot(face.Up, ups[i]) < 1 - 1e-6 ||
                !Near(face.HorizontalFov, 90, .05) || !Near(face.VerticalFov, 90, .05) ||
                !Near(face.RightOffsetRatio, 0, 1e-5) || !Near(face.UpOffsetRatio, 0, 1e-5))
                throw new InvalidOperationException("A face " + Names[i] + " nao passou na verificacao de camera e projecao.");
            // Somente os PNGs normalizados da pasta principal, nunca raw nem caminhos externos.
            if (face.File != Names[i] + ".png") throw new InvalidOperationException("Nome de arquivo da face invalido.");
            string path = System.IO.Path.Combine(folder, face.File);
            var image = ImagePipeline.Load(path);
            if (image.PixelWidth != facePixels || image.PixelHeight != facePixels)
                throw new InvalidOperationException("O arquivo " + face.File + $" deve ter {facePixels} x {facePixels} px. Use os arquivos da pasta principal da captura.");
            var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[facePixels * facePixels * 4]; rgb.CopyPixels(pixels, facePixels * 4, 0);
            rasters.Add(new Raster(pixels, facePixels));
            faces.Add(new PanoramaFace(face.Name, path, face.Width, face.Height, face.Eye, face.Forward, face.Up,
                face.HorizontalFov, face.VerticalFov, face.RightOffsetRatio, face.UpOffsetRatio, true,
                face.RawWidth, face.RawHeight, face.RasterNormalized, face.Bim));
        }
        int width = outputSize.Width, height = outputSize.Height;
        byte[] output = new byte[width * height * 4];
        double[] sinLongitude = new double[width], cosLongitude = new double[width];
        for (int x = 0; x < width; x++)
        {
            double longitude = ((x + .5) / width - .5) * 2 * Math.PI;
            sinLongitude[x] = Math.Sin(longitude); cosLongitude[x] = Math.Cos(longitude);
        }
        for (int y = 0; y < height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            double latitude = (.5 - (y + .5) / height) * Math.PI;
            double horizontal = Math.Cos(latitude), vertical = Math.Sin(latitude);
            for (int x = 0; x < width; x++)
            {
                var mapped = CubeProjection.Map(horizontal * sinLongitude[x], vertical, horizontal * cosLongitude[x]);
                Sample(rasters[mapped.Face], mapped.U, mapped.V, output, (y * width + x) * 4);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, output, width * 4);
        bitmap.Freeze();
        string finalPath = System.IO.Path.Combine(folder, $"Panorama_{width}x{height}_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8] + ".png");
        string temporaryPath = finalPath + ".pending";
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(temporaryPath)) encoder.Save(stream);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        return new PanoramaProjectionResult(finalPath, new PanoramaResult(folder, data.SourceView, faces.ToArray(), true), width, height);
    }
    private static void Sample(Raster raster, double u, double v, byte[] output, int offset)
    {
        double x = Math.Clamp(u * raster.Size - .5, 0, raster.Size - 1);
        double y = Math.Clamp(v * raster.Size - .5, 0, raster.Size - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, raster.Size - 1), y1 = Math.Min(y0 + 1, raster.Size - 1);
        double tx = x - x0, ty = y - y0;
        int a = (y0 * raster.Size + x0) * 4, b = (y0 * raster.Size + x1) * 4;
        int c = (y1 * raster.Size + x0) * 4, d = (y1 * raster.Size + x1) * 4;
        for (int channel = 0; channel < 3; channel++)
            output[offset + channel] = (byte)Math.Clamp(Math.Round(
                (raster.Pixels[a + channel] * (1 - tx) + raster.Pixels[b + channel] * tx) * (1 - ty) +
                (raster.Pixels[c + channel] * (1 - tx) + raster.Pixels[d + channel] * tx) * ty), 0, 255);
        output[offset + 3] = 255;
    }
    private static bool Near(double a, double b, double tolerance) => double.IsFinite(a) && Math.Abs(a - b) <= tolerance;
    private static bool VectorValid(double[]? v) => v is { Length: 3 } && v.All(double.IsFinite);
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static bool Unit(double[]? v) => VectorValid(v) && Math.Abs(Dot(v!, v!) - 1) < 1e-6;
    private static double Distance(double[] a, double[] b) => Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());
    private static double[] Negate(double[] a) => a.Select(v => -v).ToArray();
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}