using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arqyzeng.Panorama;
using Arqyzeng.Rendering;

namespace Arqyzeng.Revit;

internal sealed partial class PrintWindow
{
    private readonly Button _importPanoramaGeneration = new() { Content = "Abrir geração IA salva", Height = 38 };
    private readonly Button _inspectMaterialCandidates = new() { Content = "Consultar materiais candidatos", Height = 38 };
    private readonly Button _repairPanoramaSeam = new() { Content = "Propor reparo da emenda (nova chamada paga)", Height = 42, IsEnabled = false };
    private readonly Button _viewSeamCandidate = new() { Content = "Ver candidato de reparo em 360", Height = 38, IsEnabled = false };
    private readonly Button _applySeamCandidate = new() { Content = "Aplicar candidato após conferir", Height = 38, IsEnabled = false };
    private readonly Button _discardSeamCandidate = new() { Content = "Descartar candidato", Height = 32, IsEnabled = false };
    private readonly Button _viewBeforeRepair = new() { Content = "Ver render anterior ao reparo", Height = 38, IsEnabled = false };
    private BitmapSource? _seamCandidateImage;
    private byte[]? _seamCandidatePng;
    private SeamEvidence? _pendingSeam, _acceptedSeam;
    private sealed record SeamEvidence(DateTimeOffset CreatedAt, string Prompt, byte[] BeforePng,
        byte[] RequestPng, byte[] MaskPng, byte[] ResponsePng);

    private UIElement CreatePanoramaSeamPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(_inspectMaterialCandidates);
        panel.Children.Add(Text("Reparo local da emenda - experimental", 16, true));
        panel.Children.Add(_importPanoramaGeneration); panel.Children.Add(_repairPanoramaSeam);
        panel.Children.Add(_viewSeamCandidate); panel.Children.Add(_viewBeforeRepair);
        panel.Children.Add(_applySeamCandidate); panel.Children.Add(_discardSeamCandidate);
        _importPanoramaGeneration.Click += (_, _) => ImportPanoramaGeneration();
        _inspectMaterialCandidates.Click += (_, _) => {
            if (_busy) return;
            var snapshots = PanoramaAiSnapshots();
            string summary = snapshots is null ? "Sem dados BIM carregados." : string.Join("\n", snapshots
                .Where(s => s is not null).SelectMany(s => s.Materials ?? []).Where(m => m is not null)
                .GroupBy(m => (m.Name, m.MaterialClass, m.ColorRgb)).Select(g => g.First()).Take(200)
                .Select(m => m.Name + " | " + m.MaterialClass + " | cor gráfica: " + m.ColorRgb));
            ShowText("Materiais candidatos", "Inclui camadas ocultas e outros ambientes. Consulte o Revit para confirmar. Informe local => acabamento no campo de descrições; esta lista não entra como especificação no prompt.", summary);
        };
        _repairPanoramaSeam.Click += async (_, _) => await RepairPanoramaSeamAsync();
        _viewSeamCandidate.Click += (_, _) => OpenPanoramaAiViewer(_seamCandidateImage, "Candidato: conferir emenda, teto, piso e geometria", true);
        _viewBeforeRepair.Click += (_, _) => {
            if (_busy) return;
            var evidence = _pendingSeam ?? _acceptedSeam;
            if (evidence is not null) OpenPanoramaAiViewer(DecodeSeamPng(evidence.BeforePng), "Render antes do reparo", true);
        };
        _applySeamCandidate.Click += (_, _) => {
            if (_busy || _seamCandidateImage is null || _seamCandidatePng is null || _pendingSeam is null || _panoramaAiGeneration is null) return;
            _panoramaAiRender = _seamCandidateImage; _panoramaAiRenderPng = _seamCandidatePng;
            _acceptedSeam = _pendingSeam; _pendingSeam = null;
            _seamCandidateImage = null; _seamCandidatePng = null;
            _panoramaAiGeneration = _panoramaAiGeneration with { CreatedAt = DateTimeOffset.Now };
            _panoramaAiSavedFolder = null; SetPanoramaAiBusy(false);
            _panoramaAiInfo.Text = "Candidato aplicado por você. Salve para criar uma nova pasta com o render anterior e evidências do reparo. Fidelidade visual não foi validada automaticamente.";
        };
        _discardSeamCandidate.Click += (_, _) => {
            if (_busy) return;
            _pendingSeam = null; _seamCandidateImage = null; _seamCandidatePng = null;
            SetPanoramaAiBusy(false); _panoramaAiInfo.Text = "Candidato descartado. Render anterior mantido.";
        };
        return panel;
    }
    private void ImportPanoramaGeneration()
    {
        if (_busy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Selecione generation.json da geração salva", Filter = "Geração IA (generation.json)|generation.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(dialog.FileName))!;
            using var input = File.OpenRead(dialog.FileName);
            if (input.Length > 16 * 1024 * 1024) throw new IOException();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using var document = JsonDocument.Parse(input);
            if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
                document.RootElement.GetProperty("projection").GetString() != "equirectangular-360x180") throw new IOException();
            var generation = document.RootElement.Deserialize<PanoramaAiGeneration>(options) ?? throw new IOException();
            PanoramaRenderContract.Validate(generation.Width, generation.Height, generation.Quality);
            // Nomes locais fixos; nunca seguir caminhos fornecidos pelos metadados.
            string renderPath = Path.Combine(folder, "render.png"), referencePath = Path.Combine(folder, "referencia.png"), promptPath = Path.Combine(folder, "prompt.txt");
            if (new FileInfo(renderPath).Length >= 50 * 1024 * 1024 || new FileInfo(referencePath).Length >= 50 * 1024 * 1024 || new FileInfo(promptPath).Length > 128 * 1024) throw new IOException();
            byte[] renderPng = File.ReadAllBytes(renderPath), referencePng = File.ReadAllBytes(referencePath);
            BitmapSource render = DecodeSeamPng(renderPng), reference = DecodeSeamPng(referencePng);
            if (render.PixelWidth != generation.Width || render.PixelHeight != generation.Height || reference.PixelWidth != generation.Width || reference.PixelHeight != generation.Height) throw new IOException();
            string prompt = File.ReadAllText(promptPath);
            ClearPanoramaAiResult(); _panoramaAiOriginal = null; _panoramaAiSourcePath = null;
            _panoramaMaterialDescriptions.Clear(); _panoramaLoadedSnapshots = generation.CandidateSnapshots;
            _panoramaAiRender = render; _panoramaAiRenderPng = renderPng;
            _panoramaAiReference = reference; _panoramaAiReferencePng = referencePng;
            _panoramaAiGeneration = generation with { Prompt = prompt, MaterialDescriptions = generation.MaterialDescriptions ?? "" };
            _panoramaAiSavedFolder = folder; UpdatePanoramaMaterialInfo(); SetPanoramaAiBusy(false);
            _panoramaAiInfo.Text = "Geração carregada para comparar ou propor reparo. Nenhuma API chamada. Para gerar outro render completo, selecione o panorama original.";
        }
        catch { _panoramaAiInfo.Text = "Não foi possível carregar uma geração 360 válida com referência, render, prompt e metadados. O resultado anterior foi mantido."; }
    }
    private async Task RepairPanoramaSeamAsync()
    {
        if (_busy || _panoramaAiRender is null || _panoramaAiRenderPng is null || _panoramaAiGeneration is null || _acceptedSeam is not null) return;
        CancellationTokenSource? cancellation = null;
        try
        {
            string key = _panoramaAiKey.Password.Trim();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Insira sua chave API no campo protegido do Panorama.");
            int w = _panoramaAiRender.PixelWidth, h = _panoramaAiRender.PixelHeight;
            PanoramaRenderContract.Validate(w, h, "high");
            byte[] beforePng = _panoramaAiRenderPng;
            byte[] shifted = PanoramaSeamMath.RotateHalf(SeamPixels(_panoramaAiRender), w, h);
            byte[] requestPng = EncodeSeamPng(SeamBitmap(shifted, w, h));
            byte[] maskPng = EncodeSeamPng(SeamBitmap(PanoramaSeamMath.Mask(w, h), w, h));
            string prompt = PanoramaSeamMath.Prompt(w, h);
            var request = new ImageEditRequest(requestPng, prompt, w, h, "high", maskPng);
            cancellation = new CancellationTokenSource(); _panoramaAiCancellation = cancellation;
            SetBusy(true); _cancelPanoramaAi.IsEnabled = true;
            _panoramaAiInfo.Text = "Propondo reparo local. Uma nova chamada paga em qualidade Alta; resultado será candidato, sem aplicação automática.";
            _status.Text = _panoramaAiInfo.Text;
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            ImageEditResult result;
            try { result = await new OpenAiImageProvider(http).EditAsync(request, key, timeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new ImageProviderException("Tempo esgotado. A API pode cobrar; confira o uso antes de tentar novamente."); }
            if (_closed || cancellation.IsCancellationRequested) return;
            var edited = DecodeSeamPng(result.Png);
            if (edited.PixelWidth != w || edited.PixelHeight != h) throw new ImageProviderException("Dimensões do reparo incorretas.");
            byte[] limited = PanoramaSeamMath.LimitToBand(shifted, SeamPixels(edited), w, h);
            _seamCandidateImage = SeamBitmap(PanoramaSeamMath.RotateHalf(limited, w, h), w, h);
            _seamCandidatePng = EncodeSeamPng(_seamCandidateImage);
            _pendingSeam = new SeamEvidence(DateTimeOffset.Now, prompt, beforePng, requestPng, maskPng, result.Png);
            _panoramaAiInfo.Text = "Candidato recebido; render anterior mantido. Compare teto, piso, paredes e as bordas da faixa. Aplique somente se estiver correto; caso contrário, descarte.";
        }
        catch (OperationCanceledException) { if (!_closed) _panoramaAiInfo.Text = "Espera cancelada. Render anterior mantido; solicitação recebida pode ser cobrada."; }
        catch (ImageProviderException error) { if (!_closed) _panoramaAiInfo.Text = error.Message; }
        catch (ArgumentException error) { if (!_closed) _panoramaAiInfo.Text = error.Message; }
        catch { if (!_closed) _panoramaAiInfo.Text = "Reparo não concluído. Render anterior mantido. Sem repetição automática; confira o uso da API antes de reenviar."; }
        finally
        {
            if (!_closed) { SetBusy(false); if (_original is not null) RefreshPreview(); _cancelPanoramaAi.IsEnabled = false; _status.Text = _panoramaAiInfo.Text; }
            if (ReferenceEquals(_panoramaAiCancellation, cancellation)) _panoramaAiCancellation = null;
            cancellation?.Dispose();
        }
    }
    private static byte[] SeamPixels(BitmapSource source)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }
    private static BitmapSource SeamBitmap(byte[] pixels, int w, int h)
    {
        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        image.Freeze(); return image;
    }
    private static byte[] EncodeSeamPng(BitmapSource image)
    {
        using var output = new MemoryStream(); var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(output); return output.ToArray();
    }
    private static BitmapSource DecodeSeamPng(byte[] png)
    {
        var size = OpenAiImageProvider.ReadPngSize(png); PanoramaRenderContract.Validate(size.Width, size.Height, "high");
        using var input = new MemoryStream(png); var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = input; image.EndInit(); image.Freeze(); return image;
    }
    private void SetPanoramaSeamBusy(bool busy)
    {
        _importPanoramaGeneration.IsEnabled = !busy;
        _inspectMaterialCandidates.IsEnabled = !busy;
        _repairPanoramaSeam.IsEnabled = !busy && _panoramaAiRender is not null && _acceptedSeam is null;
        _viewSeamCandidate.IsEnabled = !busy && _seamCandidateImage is not null;
        _applySeamCandidate.IsEnabled = !busy && _pendingSeam is not null;
        _discardSeamCandidate.IsEnabled = !busy && _pendingSeam is not null;
        _viewBeforeRepair.IsEnabled = !busy && (_pendingSeam is not null || _acceptedSeam is not null);
    }
    private void ClearPanoramaSeamState()
    {
        _seamCandidateImage = null; _seamCandidatePng = null; _pendingSeam = null; _acceptedSeam = null;
    }
    private void SavePanoramaSeamEvidence(string folder)
    {
        if (_acceptedSeam is null) return;
        File.WriteAllBytes(Path.Combine(folder, "render_antes_reparo.png"), _acceptedSeam.BeforePng);
        File.WriteAllBytes(Path.Combine(folder, "reparo_referencia.png"), _acceptedSeam.RequestPng);
        File.WriteAllBytes(Path.Combine(folder, "reparo_mascara.png"), _acceptedSeam.MaskPng);
        File.WriteAllBytes(Path.Combine(folder, "reparo_resposta_ia.png"), _acceptedSeam.ResponsePng);
        File.WriteAllText(Path.Combine(folder, "reparo_prompt.txt"), _acceptedSeam.Prompt);
    }
}
