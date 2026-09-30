using Autodesk.Revit.DB;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Arqyzeng.Revit;

internal sealed record PanoramaFace(string Name, string Path, int Width, int Height,
    double[] Eye, double[] Forward, double[] Up, double HorizontalFov, double VerticalFov,
    double RightOffsetRatio, double UpOffsetRatio, bool Validated,
    int RawWidth, int RawHeight, bool RasterNormalized);
internal sealed record PanoramaResult(string Folder, string SourceView, PanoramaFace[] Faces, bool Validated);

internal static class PanoramaCaptureService
{
    internal static PanoramaResult Capture(Document document, View3D source)
    {
        if (!source.IsPerspective)
            throw new InvalidOperationException("Panorama precisa de uma camera em perspectiva. Abra uma vista criada com Vista 3D > Camera, posicionada dentro do ambiente.");
        if (document.IsReadOnly || document.IsModifiable)
            throw new InvalidOperationException("Termine o comando ativo e tente novamente em um projeto editavel.");
        var orientation = source.GetOrientation();
        XYZ eye = orientation.EyePosition;
        XYZ front = orientation.ForwardDirection.Normalize();
        XYZ right = front.CrossProduct(orientation.UpDirection).Normalize();
        XYZ up = right.CrossProduct(front).Normalize();
        var directions = new[] {
            (Name: "front", Forward: front, Up: up), (Name: "right", Forward: right, Up: up),
            (Name: "back", Forward: front.Negate(), Up: up), (Name: "left", Forward: right.Negate(), Up: up),
            (Name: "up", Forward: up, Up: front.Negate()), (Name: "down", Forward: up.Negate(), Up: front) };
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArqyzengBIM", "Panorama", "Capturas");
        Directory.CreateDirectory(root);
        string id = DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
        string pending = Path.Combine(root, ".pending-" + id);
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(pending);
        var faces = new List<PanoramaFace>();
        using var group = new TransactionGroup(document, "Arqyzeng - camera temporaria Panorama");
        bool rolledBack = false;
        try
        {
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Nao foi possivel iniciar a captura temporaria.");
            View3D temporary;
            using (var transaction = new Transaction(document, "Criar camera temporaria"))
            {
                transaction.Start();
                temporary = View3D.CreatePerspective(document, source.GetTypeId());
                temporary.Name = "Arqyzeng_Panorama_Temporario_" + Guid.NewGuid().ToString("N");
                temporary.ViewTemplateId = ElementId.InvalidElementId;
                temporary.DisplayStyle = DisplayStyle.Shading;
                // Independente da vista original: a luz permanece fixa no modelo quando a camera gira.
                var sun = temporary.SunAndShadowSettings;
                sun.SharesSettings = false;
                sun.SunAndShadowType = SunAndShadowType.Lighting;
                sun.RelativeToView = false;
                sun.Azimuth = Math.PI / 4;
                sun.Altitude = Math.PI / 4;
                temporary.DetailLevel = source.DetailLevel;
                temporary.IsSectionBoxActive = false;
                temporary.CropBoxActive = true;
                temporary.CropBoxVisible = false;
                var far = temporary.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                if (far is not null && !far.IsReadOnly) far.Set(0);
                var phase = source.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
                var phaseParameter = temporary.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (phase is not null && phaseParameter is not null && !phaseParameter.IsReadOnly) phaseParameter.Set(phase);
                var phaseFilter = source.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER)?.AsElementId();
                var filterParameter = temporary.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
                if (phaseFilter is not null && filterParameter is not null && !filterParameter.IsReadOnly) filterParameter.Set(phaseFilter);
                foreach (Category category in document.Settings.Categories)
                    if (category.CategoryType == CategoryType.Annotation && temporary.CanCategoryBeHidden(category.Id))
                        temporary.SetCategoryHidden(category.Id, true);
                if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("A camera temporaria nao foi criada.");
            }
            foreach (var direction in directions)
            {
                using (var transaction = new Transaction(document, "Orientar camera Panorama"))
                {
                    transaction.Start();
                    temporary.SetOrientation(new ViewOrientation3D(eye, direction.Up, direction.Forward));
                    document.Regenerate();
                    if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Falha ao orientar camera.");
                }
                // Calibrar os limites do crop pela medida real da camera. Nao confundir pixels quadrados com FOV de 90 graus.
                CameraMeasure measure = Measure(document, temporary);
                for (int attempt = 0; attempt < 5 && !measure.Valid; attempt++)
                {
                    using var transaction = new Transaction(document, "Calibrar campo de visao Panorama");
                    transaction.Start();
                    var crop = temporary.CropBox;
                    double halfWidth = (crop.Max.X - crop.Min.X) * measure.TargetDistance / measure.HorizontalExtent;
                    double halfHeight = (crop.Max.Y - crop.Min.Y) * measure.TargetDistance / measure.VerticalExtent;
                    if (!double.IsFinite(halfWidth) || !double.IsFinite(halfHeight) || halfWidth <= 0 || halfHeight <= 0)
                        throw new InvalidOperationException("Limites de camera invalidos.");
                    crop.Min = new XYZ(-halfWidth, -halfHeight, crop.Min.Z);
                    crop.Max = new XYZ(halfWidth, halfHeight, crop.Max.Z);
                    temporary.CropBox = crop;
                    document.Regenerate();
                    if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Falha ao calibrar campo de visao.");
                    measure = Measure(document, temporary);
                }
                string faceDirectory = Path.Combine(pending, direction.Name);
                Directory.CreateDirectory(faceDirectory);
                using (var options = new ImageExportOptions())
                {
                    options.ExportRange = ExportRange.SetOfViews;
                    options.SetViewsAndSheets(new List<ElementId> { temporary.Id });
                    options.FilePath = Path.Combine(faceDirectory, "face");
                    options.ZoomType = ZoomFitType.FitToPage;
                    options.FitDirection = FitDirectionType.Horizontal;
                    options.PixelSize = 1024;
                    options.HLRandWFViewsFileType = ImageFileType.PNG;
                    options.ShadowViewsFileType = ImageFileType.PNG;
                    options.ImageResolution = ImageResolution.DPI_150;
                    document.ExportImage(options);
                }
                var files = Directory.GetFiles(faceDirectory, "*.png");
                if (files.Length != 1) throw new InvalidOperationException("A face " + direction.Name + " nao produziu um PNG unico.");
                // Preservar o raster bruto. Normalizar apenas um pixel de arredondamento quando a camera ja esta validada.
                var bitmap = ImagePipeline.Load(files[0]);
                string filename = direction.Name + ".png";
                string rawDirectory = Path.Combine(pending, "raw");
                Directory.CreateDirectory(rawDirectory);
                string rawPath = Path.Combine(rawDirectory, filename);
                File.Move(files[0], rawPath);
                Directory.Delete(faceDirectory);
                int rawWidth = bitmap.PixelWidth, rawHeight = bitmap.PixelHeight;
                bool normalize = measure.Valid && CanNormalizeRaster(rawWidth, rawHeight) &&
                    (rawWidth != 1024 || rawHeight != 1024);
                if (normalize) WriteSquareRaster(bitmap, Path.Combine(pending, filename));
                else File.Copy(rawPath, Path.Combine(pending, filename));
                int width = normalize ? 1024 : rawWidth, height = normalize ? 1024 : rawHeight;
                var actual = temporary.GetOrientation();
                bool sameEye = actual.EyePosition.DistanceTo(eye) < 1e-7;
                bool sameOrientation = actual.ForwardDirection.Normalize().DotProduct(direction.Forward) > 1 - 1e-8 &&
                    actual.UpDirection.Normalize().DotProduct(direction.Up) > 1 - 1e-8;
                faces.Add(new PanoramaFace(direction.Name, Path.Combine(folder, filename), width, height,
                    Vector(actual.EyePosition), Vector(actual.ForwardDirection), Vector(actual.UpDirection),
                    measure.HorizontalFov, measure.VerticalFov, measure.RightOffsetRatio, measure.UpOffsetRatio,
                    measure.Valid && width == 1024 && height == 1024 && sameEye && sameOrientation,
                    rawWidth, rawHeight, normalize));
            }
            if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Nao foi possivel remover a camera temporaria.");
            rolledBack = true;
            bool valid = faces.Count == 6 && faces.All(f => f.Validated);
            var metadata = new {
                SchemaVersion = 2, Version = "8.0.1-PoC", CreatedAt = DateTimeOffset.Now,
                SourceView = source.Name, SourceViewId = source.Id.Value, CameraUnits = "Revit internal feet",
                Eye = Vector(eye), ExpectedFacePixels = 1024, ExpectedFovDegrees = 90, FovToleranceDegrees = 0.05,
                Validated = valid, Projection = "cubemap-faces", SourceViewPreserved = true,
                Lighting = new { Mode = "Lighting", RelativeToView = false, AzimuthDegrees = 45, AltitudeDegrees = 45 },
                RasterPolicy = "Original PNGs preserved under raw/. Only verified 90-degree centered cameras with raster dimensions within one pixel of 1024 may be resampled across the full frame; no cropping or padding.",
                Appearance = "Independent shaded camera with fixed world lighting; source phase and detail level; section box disabled. Source filters, hidden elements and template are not copied.",
                Faces = faces.Select(f => new { f.Name, File = f.Name + ".png", f.Width, f.Height, f.Eye, f.Forward, f.Up,
                    f.HorizontalFov, f.VerticalFov, f.RightOffsetRatio, f.UpOffsetRatio, f.Validated,
                    RawFile = "raw/" + f.Name + ".png", f.RawWidth, f.RawHeight, f.RasterNormalized }).ToArray()
            };
            File.WriteAllText(Path.Combine(pending, "panorama.json"), JsonSerializer.Serialize(metadata,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Directory.Move(pending, folder);
            return new PanoramaResult(folder, source.Name, faces.ToArray(), valid);
        }
        catch
        {
            if (!rolledBack && group.GetStatus() == TransactionStatus.Started) group.RollBack();
            if (Directory.Exists(pending)) Directory.Delete(pending, true);
            throw;
        }
    }
    internal static bool CanNormalizeRaster(int width, int height) =>
        Math.Abs(width - 1024) <= 1 && Math.Abs(height - 1024) <= 1;

    private static void WriteSquareRaster(BitmapSource source, string path)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(source, new System.Windows.Rect(0, 0, 1024, 1024));
        var target = new RenderTargetBitmap(1024, 1024, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static double[] Vector(XYZ value) => new[] { value.X, value.Y, value.Z };
    private static CameraMeasure Measure(Document document, View3D view)
    {
        var context = new PanoramaCameraContext();
        using var exporter = new CustomExporter(document, context);
        exporter.IncludeGeometricObjects = false;
        exporter.ShouldStopOnError = true;
        exporter.Export(view);
        return context.Measurement ?? throw new InvalidOperationException("O Revit nao forneceu os parametros de campo de visao. Nenhum cubemap foi confirmado.");
    }
}

internal sealed record CameraMeasure(double HorizontalExtent, double VerticalExtent, double TargetDistance,
    double RightOffsetRatio, double UpOffsetRatio)
{
    internal double HorizontalFov => 2 * Math.Atan(HorizontalExtent / (2 * TargetDistance)) * 180 / Math.PI;
    internal double VerticalFov => 2 * Math.Atan(VerticalExtent / (2 * TargetDistance)) * 180 / Math.PI;
    internal bool Valid => Math.Abs(HorizontalFov - 90) <= 0.05 && Math.Abs(VerticalFov - 90) <= 0.05 &&
        Math.Abs(RightOffsetRatio) <= 1e-5 && Math.Abs(UpOffsetRatio) <= 1e-5;
}

internal sealed class PanoramaCameraContext : IExportContext
{
    internal CameraMeasure? Measurement { get; private set; }
    public bool Start() => true;
    public void Finish() { }
    public bool IsCanceled() => false;
    public RenderNodeAction OnViewBegin(ViewNode node)
    {
        using var camera = node.GetCameraInfo();
        if (!camera.IsPerspective || camera.TargetDistance <= 0 || camera.HorizontalExtent <= 0 || camera.VerticalExtent <= 0)
            throw new InvalidOperationException("Camera de exportacao invalida.");
        Measurement = new CameraMeasure(camera.HorizontalExtent, camera.VerticalExtent, camera.TargetDistance,
            camera.RightOffset / camera.TargetDistance, camera.UpOffset / camera.TargetDistance);
        return RenderNodeAction.Skip;
    }
    public void OnViewEnd(ElementId id) { }
    public RenderNodeAction OnElementBegin(ElementId id) => RenderNodeAction.Skip;
    public void OnElementEnd(ElementId id) { }
    public RenderNodeAction OnInstanceBegin(InstanceNode node) => RenderNodeAction.Skip;
    public void OnInstanceEnd(InstanceNode node) { }
    public RenderNodeAction OnLinkBegin(LinkNode node) => RenderNodeAction.Skip;
    public void OnLinkEnd(LinkNode node) { }
    public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Skip;
    public void OnFaceEnd(FaceNode node) { }
    public void OnMaterial(MaterialNode node) { }
    public void OnPolymesh(PolymeshTopology node) { }
    public void OnRPC(RPCNode node) { }
    public void OnLight(LightNode node) { }
}