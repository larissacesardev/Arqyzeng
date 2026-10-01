using Arqyzeng.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;

namespace Arqyzeng.Revit;

internal sealed class PrintCaptureHandler : IExternalEventHandler
{
    // Referencia mantida somente enquanto o painel estiver aberto.
    // Validada no contexto da API e liberada ao fechar o painel.
    internal Document? ExpectedDocument { get; set; }
    internal PrintWindow? Window { get; set; }
    internal PrintRequest? Pending { get; set; }
    internal Func<PrintRequest, bool>? GuideActionRequested { get; set; }
    private ViewportGuide? _guide;
    private CaptureViewPreparation? _preparation;
    internal bool HasBackgroundWork => _guide is not null || _preparation is not null;

    internal void StopGuide(bool restore)
    {
        ViewportGuide? guide = _guide;
        _guide = null;
        guide?.Stop(restore);
    }

    internal void StopPreparation()
    {
        CaptureViewPreparation? preparation = _preparation;
        _preparation = null;
        preparation?.Stop();
    }

    public string GetName() => "Arqyzeng BIM - capturar vista 3D";

    public void Execute(UIApplication application)
    {
        PrintRequest? request = Pending;
        PrintWindow? window = Window;
        Pending = null;
        if (request is null) return;
        if (request.Action == PrintAction.Return) { StopGuide(true); StopPreparation(); return; }
        if (window is null) { StopGuide(false); StopPreparation(); return; }
        try
        {
            if (request.Action == PrintAction.Capture)
            {
                bool changed = _guide is not null && !_guide.MatchesActiveView(application);
                StopGuide(true);
                if (changed) throw new InvalidOperationException("A vista mudou. Abra a moldura novamente na vista desejada.");
            }
            Document? document = application.ActiveUIDocument?.Document;
            if (document is null || !document.IsValidObject)
                throw new InvalidOperationException("Abra um projeto e uma vista 3D para capturar.");
            Document? expected = ExpectedDocument;
            // Document.Equals identifica o documento nativo do Revit, mesmo com wrappers .NET diferentes.
            if (expected is null || !expected.IsValidObject || !document.Equals(expected))
                throw new InvalidOperationException("O projeto ativo mudou. Feche o painel e abra Print novamente no projeto desejado.");
            if (document.ActiveView is not View3D view || view.IsTemplate)
                throw new InvalidOperationException("Abra uma vista 3D do projeto antes de capturar.");
            if (request.Action == PrintAction.Panorama)
            {
                StopGuide(true); StopPreparation();
                window.ReceivePanorama(PanoramaCaptureService.Capture(document, view, request.PanoramaFacePixels, request.PanoramaSamplingMultiplier));
                return;
            }
            if (request.Action == PrintAction.Prepare)
            {
                StopGuide(true); StopPreparation();
                _preparation = new CaptureViewPreparation(application, window, view, (name, note) =>
                {
                    _preparation = null;
                    if (name is null) window.ReceiveFailure(note ?? "A preparacao foi interrompida.");
                    else window.ReceivePreparedView(name, note);
                });
                _preparation.Start();
                return;
            }
            CaptureSettings settings = request.Settings ?? throw new InvalidOperationException("Selecione o formato da captura.");
            if (settings.Width < 256 || settings.Height < 256 ||
                settings.Width > 4096 || settings.Height > 4096)
                throw new InvalidOperationException("Use dimensoes entre 256 e 4096 pixels.");

            if (request.Action == PrintAction.Guide)
            {
                if (settings.Mode != FrameMode.Crop)
                    throw new InvalidOperationException("A moldura da vista usa o modo Preencher com recorte.");
                StopGuide(false);
                _guide = new ViewportGuide(application, window, settings, capture =>
                    GuideActionRequested?.Invoke(new PrintRequest(capture ? PrintAction.Capture : PrintAction.Return, settings)) ?? false);
                _guide.Start();
                return;
            }

            string session = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArqyzengBIM", "captures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session);
            string basePath = Path.Combine(session, "revit-original");
            using (var options = new ImageExportOptions())
            {
                options.ExportRange = ExportRange.VisibleRegionOfCurrentView;
                options.FilePath = basePath;
                options.ZoomType = ZoomFitType.FitToPage;
                options.FitDirection = FitDirectionType.Horizontal;
                options.PixelSize = Math.Min(8192, Math.Max(settings.Width, settings.Height) * 2);
                options.HLRandWFViewsFileType = ImageFileType.PNG;
                options.ShadowViewsFileType = ImageFileType.PNG;
                options.ImageResolution = ImageResolution.DPI_150;
                document.ExportImage(options);
            }
            string[] images = Directory.GetFiles(session, "*.png");
            if (images.Length != 1)
                throw new InvalidOperationException("O Revit nao retornou uma unica imagem PNG para a vista ativa.");
            BimSceneSnapshot bim;
            try { bim = BimSceneReader.Read(document, view); }
            catch (Exception exception)
            {
                PluginLog.Write("Captura preservada; leitura BIM indisponivel.", exception);
                bim = BimSceneReader.Unavailable();
            }
            var result = new CaptureResult(images[0], view.Name, bim);
            PluginLog.Write("Captura concluida.");
            window.ReceiveCapture(result);
        }
        catch (Exception exception)
        {
            StopGuide(true); StopPreparation();
            PluginLog.Write("Falha no Print.", exception);
            if (request.Action == PrintAction.Panorama) window.ReceivePanoramaFailure(exception.Message);
            else window.ReceiveFailure(exception.Message);
        }
    }
}