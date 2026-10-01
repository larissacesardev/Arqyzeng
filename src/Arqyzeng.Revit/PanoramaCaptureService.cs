using Autodesk.Revit.DB;
using Arqyzeng.Panorama;
using Arqyzeng.Core;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Arqyzeng.Revit;

internal sealed record PanoramaFace(string Name, string Path, int Width, int Height,
    double[] Eye, double[] Forward, double[] Up, double HorizontalFov, double VerticalFov,
    double RightOffsetRatio, double UpOffsetRatio, bool Validated,
    int RawWidth, int RawHeight, bool RasterNormalized, BimSceneSnapshot? Bim = null);
internal sealed record PanoramaResult(string Folder, string SourceView, PanoramaFace[] Faces, bool Validated, string? AppearanceNote = null);

internal static class PanoramaCaptureService
{
    internal static PanoramaResult Capture(Document document, View3D source, int facePixels = 1024, int samplingMultiplier = 1)
    {
        CubeProjection.OutputSize(facePixels);
        int exportPixels = CubeProjection.OverscanPixels(facePixels, samplingMultiplier);
        if (!source.IsPerspective)
            throw new InvalidOperationException("Panorama precisa de uma camera em perspectiva. Abra uma vista criada com Vista 3D > Camera, posicionada dentro do ambiente.");
        if (document.IsReadOnly || document.IsModifiable)
            throw new InvalidOperationException("Termine o comando ativo e tente novamente em um projeto editavel.");
        if (source.IsTemporaryHideIsolateActive() || source.GetTemporaryViewPropertiesId() != ElementId.InvalidElementId)
            throw new InvalidOperationException("Desative Ocultar/Isolar temporariamente e Propriedades temporarias da vista antes de capturar. A origem nao sera modificada pelo plugin.");
        if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            throw new InvalidOperationException("Esta camera nao permite duplicacao no Revit. Crie outra vista 3D em perspectiva para capturar preservando a aparencia.");
        DisplayStyle sourceStyle = source.DisplayStyle;
        if (sourceStyle is not (DisplayStyle.Shading or DisplayStyle.ShadingWithEdges or
            DisplayStyle.FlatColors or DisplayStyle.Realistic or DisplayStyle.RealisticWithEdges or DisplayStyle.Textures))
            throw new InvalidOperationException("Use uma vista em Realista, Texturas, Sombreado ou Cores consistentes. Para capturar texturas, prepare a origem em Realista. O plugin nao troca seu estilo automaticamente.");
        string sourceName = source.Name;
        long sourceId = source.Id.Value;
        long templateId = source.ViewTemplateId.Value;
        bool sourceSectionBox = source.IsSectionBoxActive;
        bool sectionBoxOutlineHidden = false;
        var sourceSun = source.SunAndShadowSettings;
        string sourceSunMode = sourceSun.SunAndShadowType.ToString();
        bool sourceRelativeLight = sourceSun.SunAndShadowType == SunAndShadowType.Lighting && sourceSun.RelativeToView;
        var appearanceNotes = new List<string>();
        if (sourceStyle is not (DisplayStyle.Realistic or DisplayStyle.RealisticWithEdges or DisplayStyle.Textures))
            appearanceNotes.Add("A origem nao esta em Realista/Texturas: o panorama preserva o estilo escolhido, sem criar texturas ou realismo ausentes.");
        if (sourceSectionBox)
            appearanceNotes.Add("A caixa de corte da origem foi preservada. Geometria cortada pode deixar vazios no panorama.");
        string lightingPolicy = "Source world lighting preserved through view duplication.";
        string actualSunMode = sourceSunMode;
        bool actualRelativeLight = false;
        double? actualAzimuth = null, actualAltitude = null;
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
        var exportVisibility = new List<object>();
        var rasterConversions = new List<object>();
        using var group = new TransactionGroup(document, "Arqyzeng - camera temporaria Panorama");
        bool rolledBack = false;
        try
        {
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("Nao foi possivel iniciar a captura temporaria.");
            View3D temporary;
            using (var transaction = new Transaction(document, "Criar camera temporaria"))
            {
                transaction.Start();
                // Duplicar a vista preserva as configuracoes nativas em vez de reconstruir
                // uma camera sombreada sem o contexto visual da origem.
                temporary = document.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate)) as View3D
                    ?? throw new InvalidOperationException("O Revit nao devolveu uma copia 3D da camera.");
                if (!temporary.IsPerspective)
                    throw new InvalidOperationException("A copia da vista nao permaneceu em perspectiva.");
                temporary.Name = "Arqyzeng_Panorama_Temporario_" + Guid.NewGuid().ToString("N");
                // Apenas a copia perde o vinculo ao template, para permitir calibrar o crop.
                temporary.ViewTemplateId = ElementId.InvalidElementId;
                temporary.Unlock();
                temporary.DisplayStyle = sourceStyle;
                temporary.DetailLevel = source.DetailLevel;
                temporary.IsSectionBoxActive = sourceSectionBox;
                if (sourceSectionBox)
                {
                    temporary.SetSectionBox(source.GetSectionBox());
                    // O corte continua ativo; esconder apenas o contorno na copia temporaria.
                    var sectionBoxCategory = new ElementId(BuiltInCategory.OST_SectionBox);
                    if (!temporary.GetCategoryHidden(sectionBoxCategory))
                    {
                        if (!temporary.CanCategoryBeHidden(sectionBoxCategory))
                            throw new InvalidOperationException("O Revit nao permitiu ocultar o contorno da caixa de corte na camera temporaria. Nenhuma vista original foi alterada.");
                        temporary.SetCategoryHidden(sectionBoxCategory, true);
                    }
                    sectionBoxOutlineHidden = temporary.GetCategoryHidden(sectionBoxCategory);
                    if (!sectionBoxOutlineHidden)
                        throw new InvalidOperationException("Nao foi possivel confirmar a ocultacao do contorno da caixa de corte.");
                }
                temporary.CropBoxActive = true;
                temporary.CropBoxVisible = false;
                var far = temporary.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                if (far is not null && !far.IsReadOnly)
                {
                    if (far.AsInteger() != 0)
                        appearanceNotes.Add("O corte distante foi desativado apenas na copia para permitir a captura em 360 graus.");
                    far.Set(0);
                }
                var sun = temporary.SunAndShadowSettings;
                if (sun.Id == sourceSun.Id)
                    throw new InvalidOperationException("A copia compartilha o mesmo elemento solar da origem. Captura interrompida para preservar a vista original.");
                sun.SharesSettings = false;
                if (sourceRelativeLight)
                {
                    // Uma luz relativa giraria com cada face, criando mudancas nas emendas.
                    // Nesta primeira revisao a adaptacao e explicita, nao uma promessa de
                    // equivalencia com a luz original: use luz nao relativa para comparacao fiel.
                    sun.SunAndShadowType = SunAndShadowType.Lighting;
                    sun.RelativeToView = false;
                    sun.Azimuth = Math.PI / 4;
                    sun.Altitude = Math.PI / 4;
                    lightingPolicy = "Camera-relative source light replaced by fixed world lighting at azimuth/altitude 45 degrees.";
                    appearanceNotes.Add("A luz relativa a vista foi adaptada para luz fixa de 45 graus no modelo, para evitar emendas. Pode mudar o brilho das cores. Para comparar a luz original, desative Relativo a vista na origem antes de capturar.");
                }
                actualSunMode = sun.SunAndShadowType.ToString();
                if (sun.SunAndShadowType == SunAndShadowType.Lighting)
                {
                    actualRelativeLight = sun.RelativeToView;
                    actualAzimuth = sun.Azimuth * 180 / Math.PI;
                    actualAltitude = sun.Altitude * 180 / Math.PI;
                }
                // Recusar uma copia que perdeu a ordem de filtros; nao publicar captura
                // aparentemente fiel se a configuracao nativa nao foi conservada.
                if (!source.GetOrderedFilters().SequenceEqual(temporary.GetOrderedFilters()))
                    throw new InvalidOperationException("A copia nao preservou os filtros da origem. A captura foi interrompida.");
                foreach (ElementId filter in source.GetOrderedFilters())
                {
                    temporary.SetFilterOverrides(filter, source.GetFilterOverrides(filter));
                    temporary.SetFilterVisibility(filter, source.GetFilterVisibility(filter));
                    temporary.SetIsFilterEnabled(filter, source.GetIsFilterEnabled(filter));
                }
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
                for (int attempt = 0; attempt < 5 && !measure.ValidFor(CubeProjection.CaptureFovDegrees); attempt++)
                {
                    using var transaction = new Transaction(document, "Calibrar campo de visao Panorama");
                    transaction.Start();
                    var crop = temporary.CropBox;
                    double halfWidth = (crop.Max.X - crop.Min.X) * measure.TargetDistance / measure.HorizontalExtent *
                        Math.Tan(CubeProjection.CaptureFovDegrees * Math.PI / 360);
                    double halfHeight = (crop.Max.Y - crop.Min.Y) * measure.TargetDistance / measure.VerticalExtent *
                        Math.Tan(CubeProjection.CaptureFovDegrees * Math.PI / 360);
                    if (!double.IsFinite(halfWidth) || !double.IsFinite(halfHeight) || halfWidth <= 0 || halfHeight <= 0)
                        throw new InvalidOperationException("Limites de camera invalidos.");
                    crop.Min = new XYZ(-halfWidth, -halfHeight, crop.Min.Z);
                    crop.Max = new XYZ(halfWidth, halfHeight, crop.Max.Z);
                    temporary.CropBox = crop;
                    document.Regenerate();
                    if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Falha ao calibrar campo de visao.");
                    measure = Measure(document, temporary);
                }
                if (!measure.ValidFor(CubeProjection.CaptureFovDegrees))
                    throw new InvalidOperationException("Nao foi possivel calibrar os 94 graus de captura com margem.");
                // Calibrar CropBox pode reativar seu contorno: ocultar DEPOIS da ultima calibracao.
                bool cropVisibleBeforeCleanup = temporary.CropBoxVisible;
                bool cropCategoryHidden = false, specialCropCategoryHidden = false;
                using (var transaction = new Transaction(document, "Ocultar contornos da camera Panorama"))
                {
                    transaction.Start();
                    var cropCategory = new ElementId(BuiltInCategory.OST_CropBoundary);
                    var specialCropCategory = new ElementId(BuiltInCategory.OST_CropBoundarySpecial);
                    if (temporary.CanCategoryBeHidden(cropCategory))
                    {
                        temporary.SetCategoryHidden(cropCategory, true);
                        cropCategoryHidden = temporary.GetCategoryHidden(cropCategory);
                    }
                    if (temporary.CanCategoryBeHidden(specialCropCategory))
                    {
                        temporary.SetCategoryHidden(specialCropCategory, true);
                        specialCropCategoryHidden = temporary.GetCategoryHidden(specialCropCategory);
                    }
                    temporary.CropBoxVisible = false;
                    document.Regenerate();
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Falha ao ocultar o contorno do recorte da camera.");
                }
                bool cropVisibleAtExport = temporary.CropBoxVisible;
                if (cropVisibleAtExport)
                    throw new InvalidOperationException("O contorno do recorte da camera continua visivel; captura interrompida para evitar linhas nas emendas.");
                exportVisibility.Add(new { Name = direction.Name, CropVisibleBeforeCleanup = cropVisibleBeforeCleanup,
                    CropVisibleAtExport = cropVisibleAtExport, CropCategoryHidden = cropCategoryHidden,
                    SpecialCropCategoryHidden = specialCropCategoryHidden });
                // Ler candidatos BIM de cada orientacao calibrada na thread da API.
                // Falha de leitura nao invalida a captura nem inventa especificacoes.
                BimSceneSnapshot bim;
                try { bim = BimSceneReader.Read(document, temporary); }
                catch (Exception exception)
                {
                    PluginLog.Write("Falha ao ler materiais BIM da face Panorama " + direction.Name, exception);
                    bim = BimSceneReader.Unavailable();
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
                    options.PixelSize = exportPixels;
                    options.HLRandWFViewsFileType = ImageFileType.PNG;
                    options.ShadowViewsFileType = ImageFileType.PNG;
                    options.ImageResolution = ImageResolution.DPI_150;
                    document.ExportImage(options);
                }
                var files = Directory.GetFiles(faceDirectory, "*.png");
                if (files.Length != 1) throw new InvalidOperationException("A face " + direction.Name + " nao produziu um PNG unico.");
                // Preservar o raster bruto de 94 graus; extrair a regiao central exata de 90 graus.
                var bitmap = ImagePipeline.Load(files[0]);
                string filename = direction.Name + ".png";
                string rawDirectory = Path.Combine(pending, "raw");
                Directory.CreateDirectory(rawDirectory);
                string rawPath = Path.Combine(rawDirectory, filename);
                File.Move(files[0], rawPath);
                Directory.Delete(faceDirectory);
                int rawWidth = bitmap.PixelWidth, rawHeight = bitmap.PixelHeight;
                if (Math.Abs(rawWidth - exportPixels) > 1 || Math.Abs(rawHeight - exportPixels) > 1)
                    throw new InvalidOperationException("O raster exportado nao corresponde ao overscan esperado.");
                var region = CubeProjection.CentralFaceRegion(rawWidth, rawHeight,
                    measure.HorizontalFov, measure.VerticalFov);
                WriteCentralFaceRaster(bitmap, Path.Combine(pending, filename), facePixels, region);
                int width = facePixels, height = facePixels;
                bool normalize = true;
                double finalHorizontalFov = 2 * Math.Atan(Math.Tan(measure.HorizontalFov * Math.PI / 360) *
                    region.Width / rawWidth) * 180 / Math.PI;
                double finalVerticalFov = 2 * Math.Atan(Math.Tan(measure.VerticalFov * Math.PI / 360) *
                    region.Height / rawHeight) * 180 / Math.PI;
                rasterConversions.Add(new { Name = direction.Name, RawHorizontalFov = measure.HorizontalFov,
                    RawVerticalFov = measure.VerticalFov, RawWidth = rawWidth, RawHeight = rawHeight,
                    SourceX = region.X, SourceY = region.Y, SourceWidth = region.Width, SourceHeight = region.Height,
                    FinalHorizontalFov = finalHorizontalFov, FinalVerticalFov = finalVerticalFov,
                    Policy = "Exact central 90-degree region from measured 94-degree camera; raw PNG preserved." });
                var actual = temporary.GetOrientation();
                bool sameEye = actual.EyePosition.DistanceTo(eye) < 1e-7;
                bool sameOrientation = actual.ForwardDirection.Normalize().DotProduct(direction.Forward) > 1 - 1e-8 &&
                    actual.UpDirection.Normalize().DotProduct(direction.Up) > 1 - 1e-8;
                faces.Add(new PanoramaFace(direction.Name, Path.Combine(folder, filename), width, height,
                    Vector(actual.EyePosition), Vector(actual.ForwardDirection), Vector(actual.UpDirection),
                    finalHorizontalFov, finalVerticalFov, measure.RightOffsetRatio, measure.UpOffsetRatio,
                    measure.ValidFor(CubeProjection.CaptureFovDegrees) &&
                    Math.Abs(finalHorizontalFov - 90) < 1e-8 && Math.Abs(finalVerticalFov - 90) < 1e-8 && width == facePixels && height == facePixels && sameEye && sameOrientation,
                    rawWidth, rawHeight, normalize, bim));
            }
            if (group.RollBack() != TransactionStatus.RolledBack) throw new InvalidOperationException("Nao foi possivel remover a camera temporaria.");
            rolledBack = true;
            bool valid = faces.Count == 6 && faces.All(f => f.Validated);
            var metadata = new {
                SchemaVersion = 2, Version = "8.2.8-PoC", CreatedAt = DateTimeOffset.Now,
                SourceView = sourceName, SourceViewId = sourceId, CameraUnits = "Revit internal feet",
                Eye = Vector(eye), ExpectedFacePixels = facePixels, SamplingMultiplier = samplingMultiplier, ExpectedRawPixels = exportPixels,
                ExpectedFovDegrees = 90, FovToleranceDegrees = 0.05,
                Validated = valid, Projection = "cubemap-faces", SourceViewPreserved = true,
                Lighting = new { SourceMode = sourceSunMode, SourceRelativeToView = sourceRelativeLight,
                    Mode = actualSunMode, RelativeToView = actualRelativeLight,
                    AzimuthDegrees = actualAzimuth, AltitudeDegrees = actualAltitude, Policy = lightingPolicy },
                AppearanceSettings = new { SourceStyle = sourceStyle.ToString(), EffectiveStyle = sourceStyle.ToString(),
                    Strategy = "Native View.Duplicate; template detached only on copy; native graphics retained.",
                    SourceTemplateId = templateId, SectionBoxPreserved = sourceSectionBox,
                    SectionBoxOutlineHidden = sectionBoxOutlineHidden,
                    AnnotationsHidden = true, FacePixels = facePixels, Notes = appearanceNotes.ToArray() },
                RasterPolicy = "Raw PNGs contain measured 94-degree overscan at increased pixel resolution. Optional 2x sampling exports twice the linear native resolution before high-quality reduction to the final face size. Main faces contain the exact central 90-degree region, resampled without integer crop rounding. No seam blur or border repainting.",
                RasterConversions = rasterConversions.ToArray(),
                Appearance = "Duplicated source view with original visual style. Native visibility and graphics retained; filter overrides reapplied. Camera crop/far clipping and annotations adapted only on temporary copy. Visual fidelity still requires testing in Revit.",
                ExportVisibility = exportVisibility.ToArray(),
                BimScope = "Independent candidate snapshot for each calibrated 94-degree raw camera face, including overscan margin, host document only. Not pixel correspondence; do not sum counts across faces. No appearance asset or texture file analysis; names/classes are Revit metadata, not verified material specifications.",
                Faces = faces.Select(f => new { f.Name, File = f.Name + ".png", f.Width, f.Height, f.Eye, f.Forward, f.Up,
                    f.HorizontalFov, f.VerticalFov, f.RightOffsetRatio, f.UpOffsetRatio, f.Validated,
                    RawFile = "raw/" + f.Name + ".png", f.RawWidth, f.RawHeight, f.RasterNormalized, f.Bim }).ToArray()
            };
            File.WriteAllText(Path.Combine(pending, "panorama.json"), JsonSerializer.Serialize(metadata,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Directory.Move(pending, folder);
            return new PanoramaResult(folder, sourceName, faces.ToArray(), valid,
                "Estilo: " + sourceStyle + ". Amostragem " + samplingMultiplier + "x; raster bruto de " +
                exportPixels + " px no lado maior. " + string.Join(" ", appearanceNotes));
        }
        catch
        {
            if (!rolledBack && group.GetStatus() == TransactionStatus.Started) group.RollBack();
            if (Directory.Exists(pending)) Directory.Delete(pending, true);
            throw;
        }
    }
    internal static bool CanNormalizeRaster(int width, int height, int facePixels = 1024) =>
        CubeProjection.CanNormalizeRaster(width, height, facePixels);

    private static void WriteCentralFaceRaster(BitmapSource source, string path, int facePixels,
        (double X, double Y, double Width, double Height) region)
    {
        // Coordenadas continuas: arredondar um CroppedBitmap mudaria o FOV e a emenda.
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        double sx = facePixels / region.Width, sy = facePixels / region.Height;
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(source, new System.Windows.Rect(-region.X * sx, -region.Y * sy,
                source.PixelWidth * sx, source.PixelHeight * sy));
        var target = new RenderTargetBitmap(facePixels, facePixels, 96, 96, PixelFormats.Pbgra32);
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
    internal bool Valid => ValidFor(90);
    internal bool ValidFor(double fov) => Math.Abs(HorizontalFov - fov) <= 0.05 && Math.Abs(VerticalFov - fov) <= 0.05 &&
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