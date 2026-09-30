using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Arqyzeng.Revit;

internal static class ImagePipeline
{
    internal static BitmapImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static BitmapSource Render(BitmapSource source, CaptureSettings settings, bool preview = false)
    {
        if (settings.Width < 256 || settings.Height < 256 ||
            settings.Width > 4096 || settings.Height > 4096)
            throw new InvalidOperationException("Use dimensoes entre 256 e 4096 pixels.");
        if (!double.IsFinite(settings.Horizontal) || !double.IsFinite(settings.Vertical) ||
            !double.IsFinite(settings.Zoom) || settings.Horizontal < 0 || settings.Horizontal > 1 ||
            settings.Vertical < 0 || settings.Vertical > 1 || settings.Zoom < 1 || settings.Zoom > 3)
            throw new InvalidOperationException("Enquadramento invalido.");

        BitmapSource framed = source;
        if (settings.Mode == FrameMode.Crop)
        {
            double ratio = (double)settings.Width / settings.Height;
            double cropWidth = Math.Min(source.PixelWidth, source.PixelHeight * ratio) / settings.Zoom;
            double cropHeight = cropWidth / ratio;
            int width = Math.Clamp((int)Math.Round(cropWidth), 1, source.PixelWidth);
            int height = Math.Clamp((int)Math.Round(cropHeight), 1, source.PixelHeight);
            int x = (int)Math.Round((source.PixelWidth - width) * settings.Horizontal);
            int y = (int)Math.Round((source.PixelHeight - height) * settings.Vertical);
            var crop = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
            crop.Freeze();
            framed = crop;
        }
        else if (settings.Mode != FrameMode.Fit)
            throw new InvalidOperationException("Selecione um modo de enquadramento valido.");

        // A previa usa menos pixels; o PNG salvo usa a resolucao selecionada.
        double previewScale = preview ? Math.Min(1, 1200.0 / Math.Max(settings.Width, settings.Height)) : 1;
        int outputWidth = Math.Max(1, (int)Math.Round(settings.Width * previewScale));
        int outputHeight = Math.Max(1, (int)Math.Round(settings.Height * previewScale));
        double scale = Math.Min((double)outputWidth / framed.PixelWidth, (double)outputHeight / framed.PixelHeight);
        double drawWidth = framed.PixelWidth * scale;
        double drawHeight = framed.PixelHeight * scale;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, outputWidth, outputHeight));
            context.DrawImage(framed, new Rect((outputWidth - drawWidth) / 2,
                (outputHeight - drawHeight) / 2, drawWidth, drawHeight));
        }
        var result = new RenderTargetBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    internal static BitmapSource PrepareImageEditReference(BitmapSource source, CaptureSettings framing, int width, int height)
    {
        // Aplicar o recorte escolhido uma unica vez; depois apenas encaixar na resolucao da API.
        BitmapSource framed = Render(source, framing);
        return Render(framed, new CaptureSettings(width, height, FrameMode.Fit));
    }

    internal static void Save(BitmapSource source, string output, CaptureSettings settings)
    {
        BitmapSource result = Render(source, settings);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));
        using (var stream = File.Create(output)) encoder.Save(stream);
        BitmapImage verified = Load(output);
        if (verified.PixelWidth != settings.Width || verified.PixelHeight != settings.Height)
            throw new InvalidOperationException("O PNG nao possui as dimensoes solicitadas.");
    }
}