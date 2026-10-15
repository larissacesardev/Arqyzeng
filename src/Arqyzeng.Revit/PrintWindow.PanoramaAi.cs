using Arqyzeng.Core;
using Arqyzeng.Panorama;
using Arqyzeng.Rendering;
using Microsoft.Win32;
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

namespace Arqyzeng.Revit;

// UI/rede separadas da captura: nenhuma chamada a API Revit durante a geracao IA.
internal sealed partial class PrintWindow
{
    private readonly StackPanel _panoramaAiSettings = new();
    private readonly TextBox _panoramaMaterialDescriptions = new() { Height = 90, MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _panoramaMaterialInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _loadPanoramaMaterials = new() { Content = "Carregar dados BIM da pasta da captura", Height = 38 };
    private BimSceneSnapshot[]? _panoramaLoadedSnapshots;
    private sealed record AiCaptureMetadata(int SchemaVersion, bool Validated, AiCaptureFace[] Faces);
    private sealed record AiCaptureFace(string Name, int Width, int Height, bool Validated, BimSceneSnapshot? Bim);
    private readonly PasswordBox _panoramaAiKey = new() { Height = 30 };
    private readonly ComboBox _panoramaAiProfile = new() { Height = 32 };
    private readonly ComboBox _panoramaAiQuality = new() { Height = 32 };
    private readonly ComboBox _panoramaAiSize = new() { Height = 32 };
    private readonly TextBlock _panoramaAiInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _choosePanoramaAi = new() { Content = "Selecionar panorama original PNG", Height = 38 };
    private readonly Button _showPanoramaAiPrompt = new() { Content = "Abrir prompt Panorama IA", Height = 38, IsEnabled = false };
    private readonly Button _generatePanoramaAi = new() { Content = "Gerar Panorama IA (API paga)", Height = 42, IsEnabled = false };
    private readonly Button _cancelPanoramaAi = new() { Content = "Cancelar espera da IA", Height = 32, IsEnabled = false };
    private readonly Button _viewPanoramaAiReference = new() { Content = "Ver referência enviada em 360", Height = 38, IsEnabled = false };
    private readonly Button _viewPanoramaAiRender = new() { Content = "Ver render IA em 360", Height = 38, IsEnabled = false };
    private readonly Button _checkPanoramaAiSeam = new() { Content = "Conferir fechamento 360 do render", Height = 38, IsEnabled = false };
    private readonly Button _savePanoramaAi = new() { Content = "Salvar geração Panorama IA", Height = 38, IsEnabled = false };
    private readonly Button _openPanoramaAiFolder = new() { Content = "Abrir pasta da geração IA", Height = 38, IsEnabled = false };
    private string? _panoramaAiSourcePath, _panoramaAiSavedFolder;
    private BitmapSource? _panoramaAiOriginal, _panoramaAiReference, _panoramaAiRender;
    private byte[]? _panoramaAiReferencePng, _panoramaAiRenderPng;
    private PanoramaAiGeneration? _panoramaAiGeneration;
    private CancellationTokenSource? _panoramaAiCancellation;
    private sealed record PanoramaAiGeneration(DateTimeOffset CreatedAt, string SourcePath,
        int SourceWidth, int SourceHeight, int Width, int Height, string Profile, string Quality,
        string Model, string Prompt, BimSceneSnapshot[]? CandidateSnapshots, string MaterialDescriptions);

    private UIElement CreatePanoramaAiPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateSectionTitle("Panorama com IA · experimental"));
        _panoramaAiSettings.Children.Add(_choosePanoramaAi);
        _panoramaAiSettings.Children.Add(Text("Perfil de iluminação"));
        _panoramaAiProfile.Items.Add("Bump Dia"); _panoramaAiProfile.Items.Add("Bump Noite");
        _panoramaAiProfile.SelectedIndex = 0; _panoramaAiSettings.Children.Add(_panoramaAiProfile);
        _panoramaAiSettings.Children.Add(Text("Resolução nativa do render IA"));
        _panoramaAiSize.Items.Add("Primeiro teste: 1536 x 768");
        _panoramaAiSize.Items.Add("Maior: 2048 x 1024");
        _panoramaAiSize.SelectedIndex = 1; _panoramaAiSettings.Children.Add(_panoramaAiSize);
        _panoramaAiSettings.Children.Add(Text("Qualidade da IA"));
        foreach (string name in new[] { "Baixa - primeiro teste", "Média", "Alta" }) _panoramaAiQuality.Items.Add(name);
        _panoramaAiQuality.SelectedIndex = 2; _panoramaAiSettings.Children.Add(_panoramaAiQuality);
        _panoramaAiSettings.Children.Add(_loadPanoramaMaterials);
        _panoramaAiSettings.Children.Add(_panoramaMaterialInfo);
        _panoramaAiSettings.Children.Add(Text("Materiais e localização (opcional)"));
        _panoramaMaterialDescriptions.ToolTip = "Até 2000 caracteres. Ex.: porta ao lado do sofá => carvalho fosco. Informe apenas acabamentos conhecidos.";
        _panoramaAiSettings.Children.Add(_panoramaMaterialDescriptions);
        _panoramaAiSettings.Children.Add(_showPanoramaAiPrompt);
        _panoramaAiSettings.Children.Add(Text("Chave API OpenAI - Panorama"));
        _panoramaAiSettings.Children.Add(_panoramaAiKey);
        _panoramaAiSettings.Children.Add(_generatePanoramaAi);
        panel.Children.Add(_panoramaAiSettings); panel.Children.Add(_cancelPanoramaAi);
        _cancelPanoramaAi.ToolTip = "Cancelar interrompe a espera; a API pode continuar e cobrar a solicitação recebida.";
        panel.Children.Add(_viewPanoramaAiReference); panel.Children.Add(_viewPanoramaAiRender);
        panel.Children.Add(_checkPanoramaAiSeam); panel.Children.Add(_savePanoramaAi);
        panel.Children.Add(new Expander
        {
            Header = "Reparo da emenda · experimental", Margin = new Thickness(0, 12, 0, 8),
            Content = CreatePanoramaSeamPanel()
        });
        panel.Children.Add(_openPanoramaAiFolder); panel.Children.Add(_panoramaAiInfo);
        _loadPanoramaMaterials.Click += (_, _) => LoadPanoramaMaterialMetadata();
        _choosePanoramaAi.Click += (_, _) => ChoosePanoramaAiSource();
        _showPanoramaAiPrompt.Click += (_, _) => ShowPanoramaAiPrompt();
        _generatePanoramaAi.Click += async (_, _) => await GeneratePanoramaAiAsync();
        _cancelPanoramaAi.Click += (_, _) => { _panoramaAiCancellation?.Cancel(); _cancelPanoramaAi.IsEnabled = false; };
        _viewPanoramaAiReference.Click += (_, _) => OpenPanoramaAiViewer(_panoramaAiReference, "Referência enviada à IA", false);
        _viewPanoramaAiRender.Click += (_, _) => OpenPanoramaAiViewer(_panoramaAiRender, "Render Panorama IA - conferir fidelidade", false);
        _checkPanoramaAiSeam.Click += (_, _) => OpenPanoramaAiViewer(_panoramaAiRender, "Fechamento do render IA - conferir continuidade", true);
        _savePanoramaAi.Click += (_, _) => SavePanoramaAiGeneration();
        _openPanoramaAiFolder.Click += (_, _) => {
            if (_busy || _panoramaAiSavedFolder is null) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_panoramaAiSavedFolder) { UseShellExecute = true }); }
            catch { _panoramaAiInfo.Text = "Não foi possível abrir a pasta da geração IA."; }
        };
        _panoramaAiInfo.Text = "Selecione um panorama original PNG 2:1.";
        UpdatePanoramaMaterialInfo();
        return panel;
    }
    private void ChoosePanoramaAiSource()
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "Selecione o panorama original equiretangular 2:1",
            Filter = "Panorama PNG (*.png)|*.png", InitialDirectory = _panoramaResult?.Folder ?? "" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            BitmapSource image = ImagePipeline.Load(dialog.FileName);
            if (image.PixelWidth != 2L * image.PixelHeight || image.PixelWidth < 512 || image.PixelWidth > 8192)
                throw new ArgumentException("Selecione um panorama 2:1 de até 8192 pixels de largura.");
            ClearPanoramaAiResult();
            _panoramaLoadedSnapshots = null; _panoramaMaterialDescriptions.Clear();
            _panoramaAiOriginal = image; _panoramaAiSourcePath = Path.GetFullPath(dialog.FileName);
            UpdatePanoramaMaterialInfo();
            SetPanoramaAiBusy(false);
            _panoramaAiInfo.Text = $"Original: {image.PixelWidth} x {image.PixelHeight} px.\n" + Path.GetFileName(dialog.FileName) +
                "\nA geração será separada; confira o prompt antes de enviar.";
        }
        catch (Exception) { _panoramaAiInfo.Text = "Não foi possível abrir um panorama original PNG 2:1 válido. A seleção anterior foi mantida."; }
    }
    private (int Width, int Height, string Quality, RenderProfile Profile) ReadPanoramaAiSettings()
    {
        int width = _panoramaAiSize.SelectedIndex == 1 ? 2048 : 1536;
        string quality = _panoramaAiQuality.SelectedIndex switch { 1 => "medium", 2 => "high", _ => "low" };
        PanoramaRenderContract.Validate(width, width / 2, quality);
        return (width, width / 2, quality, _panoramaAiProfile.SelectedIndex == 1 ? RenderProfile.BumpNight : RenderProfile.BumpDay);
    }
    private BimSceneSnapshot[]? PanoramaAiSnapshots()
    {
        if (_panoramaLoadedSnapshots is { Length: > 0 }) return _panoramaLoadedSnapshots;
        // Apenas candidatos da captura atualmente carregada; nunca dados da vista ativa.
        if (_panoramaAiSourcePath is null || _panoramaResult is null ||
            !string.Equals(Path.GetDirectoryName(_panoramaAiSourcePath), Path.GetFullPath(_panoramaResult.Folder), StringComparison.OrdinalIgnoreCase)) return null;
        return _panoramaResult.Faces.Where(f => f.Bim is not null).Select(f => f.Bim!).ToArray();
    }
    private void UpdatePanoramaMaterialInfo()
    {
        var snapshots = PanoramaAiSnapshots();
        int count = snapshots?.Where(v => v is not null).SelectMany(v => v.Materials ?? [])
            .Where(v => v is not null).Select(v => (v.Name, v.MaterialClass, v.ColorRgb)).Distinct().Count() ?? 0;
        _panoramaMaterialInfo.Text = snapshots is { Length: > 0 }
            ? $"BIM: {count} materiais candidatos."
            : "BIM indisponível. Use as descrições de materiais.";
    }
    private void LoadPanoramaMaterialMetadata()
    {
        if (_busy || _panoramaAiOriginal is null || _panoramaAiSourcePath is null) return;
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(_panoramaAiSourcePath)!, "panorama.json");
            using var input = File.OpenRead(path);
            if (input.Length > 16 * 1024 * 1024) throw new IOException("Metadados excedem o limite.");
            var data = JsonSerializer.Deserialize<AiCaptureMetadata>(input, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            string[] names = ["front", "right", "back", "left", "up", "down"];
            if (data is null || data.SchemaVersion != 2 || !data.Validated || data.Faces is not { Length: 6 } ||
                data.Faces.Any(f => f is null || !f.Validated || !names.Contains(f.Name) ||
                    f.Width != f.Height || f.Width * 4L != _panoramaAiOriginal.PixelWidth) ||
                data.Faces.Select(f => f.Name).Distinct().Count() != 6)
                throw new IOException("Metadados de captura incompatíveis.");
            var snapshots = data.Faces.Where(f => f.Bim is not null).Select(f => f.Bim!).ToArray();
            if (snapshots.Length == 0) throw new IOException("Sem dados BIM.");
            _panoramaLoadedSnapshots = snapshots; UpdatePanoramaMaterialInfo();
            _panoramaAiInfo.Text = "Candidatos carregados de panorama.json da mesma pasta. Confirme que este PNG pertence a essa captura. Metadados não identificam o material de cada pixel.";
        }
        catch { _panoramaAiInfo.Text = "Não foi possível carregar dados BIM compatíveis desta pasta. Os dados anteriores foram mantidos. Use as descrições de materiais e confira o prompt."; }
    }
    private string PanoramaAiPrompt(int width, int height, RenderProfile profile, BimSceneSnapshot[]? snapshots)
    {
        string rules = Path.Combine(Path.GetDirectoryName(typeof(PromptBuilder).Assembly.Location)!, "data");
        return PanoramaRenderContract.Build(profile, width, height, rules, snapshots, _panoramaMaterialDescriptions.Text);
    }
    private void ShowPanoramaAiPrompt()
    {
        if (_busy || _panoramaAiOriginal is null) return;
        try
        {
            var settings = ReadPanoramaAiSettings();
            ShowText("Prompt Panorama IA", "Contrato 360 experimental. Conferir o prompt não envia imagens nem chama a API.",
                PanoramaAiPrompt(settings.Width, settings.Height, settings.Profile, PanoramaAiSnapshots()));
        }
        catch { _panoramaAiInfo.Text = "Não foi possível preparar o prompt. Confira os arquivos de regras da instalação."; }
    }
    private async Task GeneratePanoramaAiAsync()
    {
        if (_busy || _panoramaAiOriginal is null || _panoramaAiSourcePath is null) return;
        CancellationTokenSource? cancellation = null;
        try
        {
            var settings = ReadPanoramaAiSettings();
            string key = _panoramaAiKey.Password.Trim();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Insira a chave API no campo protegido do Panorama. Não envie a chave pelo chat.");
            var snapshots = PanoramaAiSnapshots();
            string materialDescriptions = _panoramaMaterialDescriptions.Text.Trim();
            UpdatePanoramaMaterialInfo();
            string prompt = PanoramaAiPrompt(settings.Width, settings.Height, settings.Profile, snapshots);
            // Reduzir a esfera inteira, mantendo exatamente 2:1: nao aplicar recorte do Print.
            BitmapSource reference = ImagePipeline.Render(_panoramaAiOriginal, new CaptureSettings(settings.Width, settings.Height));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(reference));
            using var buffer = new MemoryStream(); encoder.Save(buffer);
            var request = new ImageEditRequest(buffer.ToArray(), prompt, settings.Width, settings.Height, settings.Quality);
            int sourceWidth = _panoramaAiOriginal.PixelWidth, sourceHeight = _panoramaAiOriginal.PixelHeight;
            string sourcePath = _panoramaAiSourcePath;
            cancellation = new CancellationTokenSource(); _panoramaAiCancellation = cancellation;
            SetBusy(true); _cancelPanoramaAi.IsEnabled = true;
            _panoramaAiInfo.Text = $"Gerando Panorama IA {settings.Width} x {settings.Height}. Solicitação paga, sem repetição automática.";
            _status.Text = _panoramaAiInfo.Text;
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            ImageEditResult result;
            try { result = await new OpenAiImageProvider(http).EditAsync(request, key, timeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new ImageProviderException("Tempo de espera esgotado. A API pode concluir e cobrar. Confira o uso antes de tentar novamente."); }
            if (_closed || cancellation.IsCancellationRequested) return;
            using var bytes = new MemoryStream(result.Png);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = bytes; image.EndInit(); image.Freeze();
            if (image.PixelWidth != settings.Width || image.PixelHeight != settings.Height || image.PixelWidth != image.PixelHeight * 2)
                throw new ImageProviderException("O resultado não respeita as dimensões 2:1 solicitadas. O original foi preservado.");
            ClearPanoramaSeamState();
            _panoramaAiReference = reference; _panoramaAiReferencePng = request.Png;
            _panoramaAiRender = image; _panoramaAiRenderPng = result.Png; _panoramaAiSavedFolder = null;
            _panoramaAiGeneration = new PanoramaAiGeneration(DateTimeOffset.Now, sourcePath, sourceWidth, sourceHeight,
                settings.Width, settings.Height, settings.Profile.ToString(), settings.Quality, OpenAiImageProvider.Model, prompt, snapshots, materialDescriptions);
            _panoramaAiInfo.Text = $"Render recebido: {result.Width} x {result.Height} px, qualidade {settings.Quality}.\n" +
                "Não foi salvo automaticamente. Compare com a referência enviada e confira arquitetura, polos e fechamento. Continuidade visual ainda não validada.";
            _status.Text = "Panorama IA recebido. Confira em 360 antes de salvar ou usar.";
        }
        catch (OperationCanceledException) { if (!_closed) _panoramaAiInfo.Text = "Espera cancelada. O original foi preservado; a API pode cobrar a solicitação já recebida."; }
        catch (ImageProviderException error) { if (!_closed) _panoramaAiInfo.Text = error.Message; }
        catch (ArgumentException error) { if (!_closed) _panoramaAiInfo.Text = error.Message; }
        catch (HttpRequestException) { if (!_closed) _panoramaAiInfo.Text = "Falha de conexão. Sem repetição automática; confira o uso da API antes de tentar novamente."; }
        catch (Exception) { if (!_closed) _panoramaAiInfo.Text = "Não foi possível preparar ou ler o panorama IA. O original e o resultado anterior foram preservados."; }
        finally
        {
            if (!_closed)
            {
                SetBusy(false); if (_original is not null) RefreshPreview();
                _status.Text = _panoramaAiInfo.Text; _cancelPanoramaAi.IsEnabled = false;
            }
            if (ReferenceEquals(_panoramaAiCancellation, cancellation)) _panoramaAiCancellation = null;
            cancellation?.Dispose();
        }
    }
    private void OpenPanoramaAiViewer(BitmapSource? image, string label, bool seam)
    {
        if (_busy || image is null) return;
        try
        {
            var viewer = new PanoramaViewerWindow(image, label) { Owner = this };
            if (seam) viewer.LookAtLongitude(180);
            viewer.Show();
        }
        catch { _panoramaAiInfo.Text = "Não foi possível abrir o visualizador. O resultado continua no painel."; }
    }
    private void SavePanoramaAiGeneration()
    {
        if (_busy || _panoramaAiGeneration is null || _panoramaAiReferencePng is null || _panoramaAiRenderPng is null || _panoramaAiSavedFolder is not null) return;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArqyzengBIM", "Panorama", "Renders");
        string id = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..8];
        string pending = Path.Combine(root, ".pending-" + id), destination = Path.Combine(root, id);
        try
        {
            Directory.CreateDirectory(pending);
            File.WriteAllBytes(Path.Combine(pending, "referencia.png"), _panoramaAiReferencePng);
            File.WriteAllBytes(Path.Combine(pending, "render.png"), _panoramaAiRenderPng);
            var metadata = new { SchemaVersion = 1, Version = PanoramaRenderContract.Version,
                _panoramaAiGeneration.CreatedAt, _panoramaAiGeneration.SourcePath, _panoramaAiGeneration.SourceWidth,
                _panoramaAiGeneration.SourceHeight, _panoramaAiGeneration.Width, _panoramaAiGeneration.Height,
                _panoramaAiGeneration.Profile, _panoramaAiGeneration.Quality, _panoramaAiGeneration.Model,
                Projection = "equirectangular-360x180", Fidelity = "Maximum",
                ContinuityStatus = "Not automatically validated; inspect seam, poles and geometry in viewer.",
                CandidateSnapshots = _panoramaAiGeneration.CandidateSnapshots,
                MaterialDescriptions = _panoramaAiGeneration.MaterialDescriptions,
                SeamRepair = _acceptedSeam is null ? null : new { _acceptedSeam.CreatedAt, BandDegrees = 30, Approval = "User applied candidate; geometry and materials not automatically validated.", BeforeFile = "render_antes_reparo.png", RequestFile = "reparo_referencia.png", MaskFile = "reparo_mascara.png", ApiResponseFile = "reparo_resposta_ia.png", PromptFile = "reparo_prompt.txt" },
                MaterialStatus = "User descriptions and BIM candidates; surface correspondence not automatically verified.",
                ReferenceFile = "referencia.png", RenderFile = "render.png", PromptFile = "prompt.txt" };
            SavePanoramaSeamEvidence(pending);
            File.WriteAllText(Path.Combine(pending, "generation.json"), JsonSerializer.Serialize(metadata,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            File.WriteAllText(Path.Combine(pending, "prompt.txt"), _panoramaAiGeneration.Prompt);
            foreach (string name in new[] { "referencia.png", "render.png" })
            {
                var image = ImagePipeline.Load(Path.Combine(pending, name));
                if (image.PixelWidth != _panoramaAiGeneration.Width || image.PixelHeight != _panoramaAiGeneration.Height)
                    throw new IOException("Falha ao validar a imagem salva.");
            }
            Directory.Move(pending, destination); _panoramaAiSavedFolder = destination;
            SetPanoramaAiBusy(false); _panoramaAiInfo.Text = "Geração salva separadamente: referência, render, prompt e configurações.\n" + destination;
        }
        catch
        {
            try { if (Directory.Exists(pending)) Directory.Delete(pending, true); } catch { }
            _panoramaAiInfo.Text = "Não foi possível salvar. O render continua na memória desta janela; tente novamente antes de fechar.";
        }
    }
    private void SetPanoramaAiBusy(bool busy)
    {
        SetPanoramaSeamBusy(busy);
        _panoramaAiSettings.IsEnabled = !busy;
        _loadPanoramaMaterials.IsEnabled = !busy && _panoramaAiOriginal is not null;
        _generatePanoramaAi.IsEnabled = !busy && _panoramaAiOriginal is not null;
        _showPanoramaAiPrompt.IsEnabled = !busy && _panoramaAiOriginal is not null;
        _viewPanoramaAiReference.IsEnabled = !busy && _panoramaAiReference is not null;
        _viewPanoramaAiRender.IsEnabled = !busy && _panoramaAiRender is not null;
        _checkPanoramaAiSeam.IsEnabled = !busy && _panoramaAiRender is not null;
        _savePanoramaAi.IsEnabled = !busy && _panoramaAiRenderPng is not null && _panoramaAiSavedFolder is null;
        _openPanoramaAiFolder.IsEnabled = !busy && _panoramaAiSavedFolder is not null;
    }
    private void ClearPanoramaAiResult()
    {
        ClearPanoramaSeamState();
        _panoramaAiReference = null; _panoramaAiRender = null;
        _panoramaAiReferencePng = null; _panoramaAiRenderPng = null;
        _panoramaAiGeneration = null; _panoramaAiSavedFolder = null;
    }
    private void ClosePanoramaAi()
    {
        _panoramaAiCancellation?.Cancel(); _panoramaAiKey.Clear(); ClearPanoramaAiResult();
        _panoramaAiOriginal = null; _panoramaAiSourcePath = null; _panoramaLoadedSnapshots = null; _panoramaMaterialDescriptions.Clear();
    }
}
