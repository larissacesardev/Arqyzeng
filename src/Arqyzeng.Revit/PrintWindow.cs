using Arqyzeng.Core;
using Arqyzeng.Rendering;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Arqyzeng.Revit;

internal sealed partial class PrintWindow : Window
{
    internal event Action<CaptureSettings>? CaptureRequested;
    internal event Action<CaptureSettings>? GuideRequested;
    internal event Action? PrepareRequested;
    internal event Action<int, int>? PanoramaRequested;
    private readonly Button _showPanoramaBim = new() { Content = "Consultar materiais BIM do Panorama", Height = 38, IsEnabled = false };
    private readonly ComboBox _panoramaResolution = new() { Height = 32 };
    private readonly CheckBox _panoramaFineDetails = new() {
        Content = "Detalhes finos - captura 2x", IsChecked = false,
        Margin = new Thickness(0, 10, 0, 4) };
    private readonly Button _capturePanorama = new() { Content = "Capturar seis faces (1024 px)", Height = 42 };
    private readonly Button _openPanoramaFolder = new() { Content = "Abrir pasta da captura Panorama", Height = 38, IsEnabled = false };
    private readonly ComboBox _panoramaFace = new() { Height = 32, IsEnabled = false };
    private readonly Image _panoramaPreview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _panoramaInfo = new() { TextWrapping = TextWrapping.Wrap };
    private PanoramaResult? _panoramaResult;
    private readonly Button _buildPanorama = new() { Content = "Montar panorama de captura salva", Height = 42 };
    private readonly Button _cancelPanorama = new() { Content = "Cancelar montagem", Height = 32, IsEnabled = false };
    private CancellationTokenSource? _panoramaCancellation;
    private string? _panoramaProjectionPath;
    private readonly Button _viewPanorama = new() { Content = "Visualizar panorama 360", Height = 42 };
    private readonly ComboBox _ratio = new();
    private readonly ComboBox _resolution = new();
    private readonly TextBox _width = new() { Text = "1920" };
    private readonly TextBox _height = new() { Text = "1080" };
    private readonly Button _capture = new() { Content = "Capturar vista 3D", Height = 42 };
    private readonly Button _prepareButton = new() { Content = "Preparar vista limpa (cópia 3D)", Height = 38 };
    private readonly Button _guideButton = new() { Content = "Mostrar moldura no Revit", Height = 38 };
    private readonly Button _save = new() { Content = "Salvar captura PNG", Height = 38, IsEnabled = false };
    private readonly ComboBox _profile = new() { Height = 32 };
    private readonly ComboBox _fidelity = new() { Height = 32 };
    private readonly TextBlock _renderNote = new() { TextWrapping = TextWrapping.Wrap };
    private readonly PasswordBox _apiKey = new() { Height = 30 };
    private readonly ComboBox _quality = new() { Height = 32 };
    private readonly Button _generate = new() { Content = "Gerar render com IA (API paga)", Height = 42, IsEnabled = false };
    private readonly Button _cancel = new() { Content = "Cancelar espera", Height = 32, IsEnabled = false };
    private readonly Button _saveRender = new() { Content = "Salvar render PNG", Height = 38, IsEnabled = false };
    private readonly ComboBox _comparison = new() { Height = 32 };
    private readonly TextBlock _renderInfo = new() { TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? _renderCancellation;
    private BitmapSource? _rendered;
    private BitmapSource? _reference;
    private byte[]? _renderPng;
    private byte[]? _referencePng;
    private CaptureSettings? _lastRenderCapture;
    private RenderSettings? _lastRenderSettings;
    private string? _lastRenderQuality;
    private int _lastRenderWidth;
    private int _lastRenderHeight;
    private bool _currentRenderSavedToHistory;
    private readonly Button _saveReference = new() { Content = "Salvar referência enviada à IA", Height = 38, IsEnabled = false };
    private readonly Button _saveToHistory = new() { Content = "Salvar no histórico", Height = 38, IsEnabled = false };
    private readonly Button _history = new() { Content = "Abrir histórico", Height = 38 };
    private readonly Button _openHistoryFolder = new() { Content = "Abrir pasta do histórico", Height = 38 };
    private bool _closed;
    private BimSceneSnapshot? _bim;
    private readonly TextBlock _bimSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _showBim = new() { Content = "Visualizar dados BIM da captura", Height = 38, IsEnabled = false };
    private readonly Button _confirmMaterials = new() { Content = "Confirmar materiais", Height = 38, IsEnabled = false };
    private readonly Dictionary<string, string> _materialConfirmations = new(StringComparer.Ordinal);
    private readonly Button _showPrompt = new() { Content = "Abrir prompt de render", Height = 38, IsEnabled = false };
    private readonly StackPanel _renderControls = new();
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _dimensions = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _settings = new();
    private readonly StackPanel _customDimensions = new();
    private readonly ComboBox _frameMode = new();
    private readonly Slider _horizontal = new() { Minimum = 0, Maximum = 100, Value = 50 };
    private readonly Slider _vertical = new() { Minimum = 0, Maximum = 100, Value = 50 };
    private readonly Slider _zoom = new() { Minimum = 1, Maximum = 3, Value = 1 };
    private readonly StackPanel _cropControls = new();
    private readonly TextBlock _frameNote = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _adjustmentValues = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private BitmapSource? _original;
    private string? _originalPath;
    private string _viewName = "";
    private bool _busy;
    private readonly (string Label, int Width, int Height)[] _formats =
    [
        ("Quadrado - 1:1", 1, 1), ("Vertical - 4:5", 4, 5),
        ("Fotográfico - 3:2", 3, 2), ("Widescreen - 16:9", 16, 9),
        ("Story - 9:16", 9, 16), ("Cinema - 21:9", 21, 9),
        ("Personalizado", 0, 0)
    ];

    private sealed record HistoryMaterial(string Key, string Name, string MaterialClass, string[] Categories, string Description);
    private sealed record HistoryCapture(int Width, int Height, string FrameMode, double Horizontal, double Vertical, double Zoom);
    private sealed record HistoryMetadata(
        int SchemaVersion, string Id, DateTimeOffset CreatedAt, string ViewName,
        string Profile, string Fidelity, string Quality, int Width, int Height,
        HistoryCapture Capture, HistoryMaterial[] Materials, string ReferenceFile, string RenderFile);
    private sealed record HistoryItem(string Folder, HistoryMetadata Metadata)
    {
        public override string ToString() => $"{Metadata.CreatedAt:dd/MM/yyyy HH:mm:ss zzz} | {Metadata.Profile} | {Metadata.Fidelity} | {Metadata.Width}x{Metadata.Height}";
    }

    private static string HistoryRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArqyzengBIM", "Print", "Historico");

    internal PrintWindow()
    {
        Title = "Arqyzeng BIM";
        Width = 1080; Height = 700; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplyWindowTheme(this);
        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock
        {
            Text = "Arqyzeng BIM", FontSize = 26, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new Border
        {
            Background = Brushes.White, Padding = new Thickness(12),
            BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 12, 0, 0), MinHeight = 54, Child = _status
        };
        _status.FontSize = 12;
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Print", Content = CreatePrint() });
        tabs.Items.Add(new TabItem { Header = "Panorama", Content = CreatePanorama() });
        tabs.Items.Add(new TabItem { Header = "Documentação", Content = Placeholder("Documentação BIM", "Em desenvolvimento. A criação de vistas será uma etapa posterior.") });
        root.Children.Add(tabs); Content = root;
        foreach (var button in new[] { _capture, _generate, _capturePanorama, _generatePanoramaAi })
            ApplyPrimaryButtonStyle(button);
        ConfigureUiToolTips();
        _ratio.SelectionChanged += (_, _) => UpdateDimensions();
        _resolution.SelectionChanged += (_, _) => UpdateDimensions();
        _width.TextChanged += (_, _) => UpdateDimensions();
        _height.TextChanged += (_, _) => UpdateDimensions();
        _capture.Click += (_, _) => RequestCapture();
        _guideButton.Click += (_, _) => RequestGuide();
        _prepareButton.Click += (_, _) => RequestPreparation();
        _save.Click += (_, _) => SaveCapture();
        _showPrompt.Click += (_, _) => ShowRenderPrompt();
        _showBim.Click += (_, _) => ShowBimContext();
        _confirmMaterials.Click += (_, _) => ShowMaterialConfirmations();
        _generate.Click += async (_, _) => await GenerateRenderAsync();
        _cancel.Click += (_, _) => { _renderCancellation?.Cancel(); _cancel.IsEnabled = false; };
        _saveRender.Click += (_, _) => SaveRender();
        _saveReference.Click += (_, _) => SaveReference();
        _saveToHistory.Click += (_, _) => SaveCurrentGenerationToHistory();
        _history.Click += (_, _) => ShowHistory();
        _openHistoryFolder.Click += (_, _) => OpenHistoryFolder();
        _comparison.SelectionChanged += (_, _) => { if (_comparison.SelectedIndex == 1 && _rendered is null) _comparison.SelectedIndex = 0; RefreshPreview(); };
        _quality.SelectedIndex = 0;
        _comparison.SelectedIndex = 0;
        _profile.SelectionChanged += (_, _) => UpdateRenderDescription();
        _fidelity.SelectionChanged += (_, _) => UpdateRenderDescription();
        _profile.SelectedIndex = 0; _fidelity.SelectedIndex = 2;
        _frameMode.SelectionChanged += (_, _) => UpdateDimensions();
        _horizontal.ValueChanged += (_, _) => UpdateDimensions();
        _vertical.ValueChanged += (_, _) => UpdateDimensions();
        _zoom.ValueChanged += (_, _) => UpdateDimensions();
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RefreshPreview(); };
        Closed += (_, _) => { _closed = true; ClosePanoramaAi(); _panoramaCancellation?.Cancel(); _renderCancellation?.Cancel(); _apiKey.Clear(); _previewTimer.Stop(); _original = null; _bim = null; _rendered = null; _reference = null; _renderPng = null; _referencePng = null; _lastRenderCapture = null; _lastRenderSettings = null; _lastRenderQuality = null; _preview.Source = null; };
        _frameMode.SelectedIndex = 0;
        _ratio.SelectedIndex = 3; _resolution.SelectedIndex = 1;
        _status.Text = "Abra uma vista 3D e ajuste o enquadramento no Revit. Depois clique em Capturar.";
    }

    private UIElement CreatePanorama()
    {
        var grid = CreateTabGrid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var controls = new StackPanel();
        controls.Children.Add(CreateSectionTitle("Panorama 360"));
        controls.Children.Add(Text("Qualidade da captura"));
        _panoramaResolution.Items.Add("Rápida: faces 1024 - panorama 4096 x 2048");
        _panoramaResolution.Items.Add("Alta: faces 2048 - panorama 8192 x 4096");
        _panoramaResolution.SelectionChanged += (_, _) =>
            _capturePanorama.Content = _panoramaResolution.SelectedIndex == 1
                ? "Capturar seis faces (2048 px)" : "Capturar seis faces (1024 px)";
        _panoramaResolution.SelectedIndex = 0;
        controls.Children.Add(_panoramaResolution);
        controls.Children.Add(_panoramaFineDetails);
        controls.Children.Add(_capturePanorama);
        controls.Children.Add(_openPanoramaFolder);
        controls.Children.Add(_showPanoramaBim);
        _showPanoramaBim.Click += (_, _) => ShowPanoramaBim();
        controls.Children.Add(_buildPanorama);
        controls.Children.Add(_viewPanorama);
        _viewPanorama.Click += (_, _) => OpenPanoramaViewer();
        controls.Children.Add(_cancelPanorama);
        _buildPanorama.Click += async (_, _) => await BuildPanoramaAsync();
        _cancelPanorama.Click += (_, _) => { _panoramaCancellation?.Cancel(); _cancelPanorama.IsEnabled = false; };
        controls.Children.Add(_panoramaInfo);
        controls.Children.Add(CreatePanoramaAiPanel());
        grid.Children.Add(new ScrollViewer { Content = CreateCard(controls), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var preview = new DockPanel();
        var label = Text("Conferir as seis faces", 16, true);
        DockPanel.SetDock(label, Dock.Top); preview.Children.Add(label);
        DockPanel.SetDock(_panoramaFace, Dock.Top); preview.Children.Add(_panoramaFace);
        preview.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = _panoramaPreview });
        var previewCard = CreateCard(preview);
        Grid.SetColumn(previewCard, 2); grid.Children.Add(previewCard);
        _capturePanorama.Click += (_, _) =>
        {
            if (_busy) return;
            try
            {
                if (PanoramaRequested is null) throw new InvalidOperationException("O painel não está conectado ao Revit.");
                SetBusy(true);
                _panoramaInfo.Text = _panoramaFineDetails.IsChecked == true
                    ? "Capturando seis direções com amostragem 2x para detalhes finos. A exportação pode demorar mais; aguarde o Revit concluir."
                    : "Capturando seis direções e medindo o campo de visão. Aguarde o Revit concluir.";
                _status.Text = _panoramaInfo.Text;
                PanoramaRequested.Invoke(_panoramaResolution.SelectedIndex == 1 ? 2048 : 1024,
                    _panoramaFineDetails.IsChecked == true ? 2 : 1);
            }
            catch (Exception exception) { ReceivePanoramaFailure(exception.Message); }
        };
        _panoramaFace.SelectionChanged += (_, _) => ShowPanoramaFace();
        _openPanoramaFolder.Click += (_, _) =>
        {
            if (_panoramaResult is null) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_panoramaResult.Folder) { UseShellExecute = true }); }
            catch (Exception exception) { _panoramaInfo.Text = "Não foi possível abrir a pasta: " + exception.Message; }
        };
        _panoramaInfo.Text = "Nenhuma captura Panorama nesta sessão.";
        return grid;
    }

    internal void ReceivePanorama(PanoramaResult result)
    {
        if (_closed) return;
        SetBusy(false);
        if (_original is not null) RefreshPreview();
        _panoramaResult = result;
        _panoramaProjectionPath = null;
        _panoramaFace.Items.Clear();
        foreach (var face in result.Faces) _panoramaFace.Items.Add(face.Name);
        _panoramaFace.IsEnabled = true;
        _openPanoramaFolder.IsEnabled = true;
        _showPanoramaBim.IsEnabled = true;
        _panoramaFace.SelectedIndex = 0;
        _status.Text = result.Validated
            ? "Panorama: seis faces salvas; câmera, dimensões e FOV passaram na verificação numérica. Confira visualmente as bordas."
            : "Panorama: seis imagens diagnósticas salvas. Há medidas fora do alvo; ainda não são um cubemap validado. Abra a pasta e envie panorama.json.";
        if (!string.IsNullOrWhiteSpace(result.AppearanceNote)) _status.Text += "\n" + result.AppearanceNote;
        _panoramaInfo.Text = _status.Text + "\n\nVista: " + result.SourceView + "\nPasta: " + result.Folder;
    }

    internal void ReceivePanoramaFailure(string message)
    {
        if (_closed) return;
        SetBusy(false);
        if (_original is not null) RefreshPreview();
        _panoramaInfo.Text = "Não foi possível capturar Panorama: " + message;
        _status.Text = _panoramaInfo.Text;
    }

    private void ShowPanoramaBim()
    {
        if (_busy || _panoramaResult is null) return;
        var text = new System.Text.StringBuilder();
        text.AppendLine("MATERIAIS BIM DO PANORAMA - " + _panoramaResult.SourceView);
        text.AppendLine("Consulta independente por orientação da câmera. Um mesmo elemento/material pode aparecer em várias faces. Não some as contagens; não há associação com pixels nem garantia de visibilidade.");
        text.AppendLine("Nome e classe são informações cadastradas. Dados genéricos não confirmam madeira, espécie, acabamento ou fabricante. A cor gráfica pode diferir da textura e da iluminação da imagem. Assets e arquivos de textura não foram analisados.");
        foreach (var face in _panoramaResult.Faces)
        {
            text.AppendLine();
            text.AppendLine("FACE: " + face.Name);
            text.AppendLine(BimContextFormatter.Format(face.Bim));
        }
        ShowText("Materiais BIM do Panorama", "Dados salvos no instante da captura. Capture novamente depois de mudar o modelo. Esta consulta não altera os materiais nem gera render com IA.", text.ToString());
    }

    private void OpenPanoramaViewer()
    {
        if (_busy) return;
        string? path = _panoramaProjectionPath;
        if (path is null || !File.Exists(path))
        {
            var dialog = new OpenFileDialog { Title = "Selecione o panorama PNG 2:1", Filter = "Panorama PNG (*.png)|*.png",
                InitialDirectory = _panoramaResult?.Folder ?? "" };
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }
        try
        {
            var viewer = new PanoramaViewerWindow(path) { Owner = this };
            viewer.Show();
            _status.Text = "Viewer 360 aberto. Arraste para olhar ao redor; use a roda do mouse para zoom.";
        }
        catch (Exception exception)
        {
            _status.Text = "Não foi possível abrir o viewer 360: " + exception.Message;
        }
    }

    private async Task BuildPanoramaAsync()
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Title = "Selecione panorama.json da captura validada", Filter = "Metadados de Panorama (*.json)|*.json",
            FileName = "panorama.json", InitialDirectory = _panoramaResult?.Folder ?? "" };
        if (dialog.ShowDialog(this) != true) return;
        using var cancellation = new CancellationTokenSource();
        _panoramaCancellation = cancellation;
        try
        {
            SetBusy(true); _cancelPanorama.IsEnabled = true;
            _status.Text = "Montando panorama na resolução das faces salvas. Aguarde; o Revit pode continuar aberto.";
            _panoramaInfo.Text = _status.Text;
            var result = await Task.Run(() => PanoramaProjectionService.Build(dialog.FileName, cancellation.Token), cancellation.Token);
            if (_closed) return;
            ReceivePanorama(result.Capture);
            _panoramaProjectionPath = result.Path;
            _panoramaFace.Items.Add($"Panorama 2:1 - {result.Width} x {result.Height}");
            _panoramaFace.SelectedIndex = result.Capture.Faces.Length;
            _status.Text = $"Panorama {result.Width} x {result.Height} salvo. Confira teto, piso, portas e continuidade entre as extremidades.";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) { SetBusy(false); if (_original is not null) RefreshPreview(); _status.Text = "Montagem cancelada. As seis faces continuam preservadas."; _panoramaInfo.Text = _status.Text; }
        }
        catch (Exception exception)
        {
            if (!_closed) ReceivePanoramaFailure("Falha ao montar: " + exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_panoramaCancellation, cancellation)) _panoramaCancellation = null;
            if (!_closed) _cancelPanorama.IsEnabled = false;
        }
    }

    private void ShowPanoramaFace()
    {
        if (_panoramaResult is null || _panoramaFace.SelectedIndex < 0) return;
        try
        {
            if (_panoramaFace.SelectedIndex == _panoramaResult.Faces.Length && _panoramaProjectionPath is not null)
            {
                _panoramaPreview.Source = ImagePipeline.Load(_panoramaProjectionPath);
                _panoramaInfo.Text = "Panorama equiretangular 2:1 (360 x 180 graus). Frente no centro; a direção de trás fecha nas duas bordas.\n\n" + _panoramaProjectionPath;
                return;
            }
            var face = _panoramaResult.Faces[_panoramaFace.SelectedIndex];
            _panoramaPreview.Source = ImagePipeline.Load(face.Path);
            _panoramaInfo.Text = $"{face.Name}: {face.Width} x {face.Height} px\nFOV horizontal: {face.HorizontalFov:F4} graus\nFOV vertical: {face.VerticalFov:F4} graus\n" +
                (face.Validated ? "Verificação numérica aprovada." : "Medidas fora do alvo: face apenas diagnóstica.") +
                (face.RasterNormalized ? $"\nRaster bruto: {face.RawWidth} x {face.RawHeight}; convertido para a face final de 90 graus." : "") + "\n\n" + _panoramaResult.Folder;
        }
        catch (Exception exception) { _panoramaInfo.Text = "Não foi possível abrir a face: " + exception.Message; }
    }

    private UIElement CreatePrint()
    {
        var grid = CreateTabGrid();
        var settingsColumn = new ColumnDefinition { Width = new GridLength(520) };
        var gapColumn = new ColumnDefinition { Width = new GridLength(40) };
        var comparisonColumn = new ColumnDefinition { Width = new GridLength(0) };
        grid.ColumnDefinitions.Add(settingsColumn);
        grid.ColumnDefinitions.Add(gapColumn);
        grid.ColumnDefinitions.Add(comparisonColumn);
        var toggleComparison = CreateComparisonHandle();
        Grid.SetColumn(toggleComparison, 1); grid.Children.Add(toggleComparison);
        var left = new StackPanel();
        left.Children.Add(CreateSectionTitle("Captura da vista ativa"));
        left.Children.Add(_prepareButton);
        _settings.Children.Add(Text("Formato"));
        foreach (var format in _formats) _ratio.Items.Add(format.Label);
        _ratio.Height = 32; _settings.Children.Add(_ratio);
        _settings.Children.Add(Text("Resolução — lado maior"));
        foreach (int value in new[] { 1024, 2048, 4096 }) _resolution.Items.Add(value);
        _resolution.Height = 32; _settings.Children.Add(_resolution);
        _customDimensions.Children.Add(Text("Largura personalizada (px)"));
        _width.Height = 30; _customDimensions.Children.Add(_width);
        _customDimensions.Children.Add(Text("Altura personalizada (px)"));
        _height.Height = 30; _customDimensions.Children.Add(_height);
        _settings.Children.Add(_customDimensions);
        _dimensions.Margin = new Thickness(0, 12, 0, 6); _settings.Children.Add(_dimensions);
        _settings.Children.Add(Text("Enquadramento"));
        _frameMode.Items.Add("Encaixar imagem inteira");
        _frameMode.Items.Add("Preencher com recorte");
        _frameMode.Height = 32; _settings.Children.Add(_frameMode);
        _cropControls.Children.Add(Text("Posição horizontal: esquerda / direita", 12));
        _cropControls.Children.Add(_horizontal);
        _cropControls.Children.Add(Text("Posição vertical: topo / base", 12));
        _cropControls.Children.Add(_vertical);
        _cropControls.Children.Add(Text("Ampliação do recorte: 1x a 3x", 12));
        _cropControls.Children.Add(_zoom);
        _cropControls.Children.Add(_adjustmentValues);
        var reset = new Button { Content = "Centralizar recorte e voltar a 1x", Margin = new Thickness(0, 8, 0, 0) };
        reset.Click += (_, _) => { _horizontal.Value = 50; _vertical.Value = 50; _zoom.Value = 1; };
        _cropControls.Children.Add(reset);
        // Keep the crop state for the capture pipeline, outside the visible layout.
        left.Children.Add(_settings);
        left.Children.Add(new Expander
        {
            Header = "Ferramentas da vista", Margin = new Thickness(0, 12, 0, 4),
            Content = _guideButton
        });
        _capture.Margin = new Thickness(0, 12, 0, 8); left.Children.Add(_capture);
        left.Children.Add(_save);
        _bimSummary.Text = "Capture uma vista para consultar os materiais e categorias BIM.";
        _showBim.ToolTip = _bimSummary;
        _showBim.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(CreateSectionTitle("Materiais BIM"));
        left.Children.Add(_showBim);
        _confirmMaterials.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(_confirmMaterials);
        _renderControls.Children.Add(CreateSectionTitle("Render com IA"));
        _renderControls.Children.Add(Text("Perfil"));
        _profile.Items.Add("Bump Dia");
        _profile.Items.Add("Bump Noite - Comercial Premium");
        _renderControls.Children.Add(_profile);
        _renderControls.Children.Add(Text("Fidelidade"));
        foreach (string name in new[] { "Criativo", "Controlado", "Fidelidade máxima" }) _fidelity.Items.Add(name);
        _renderControls.Children.Add(_fidelity);
        _fidelity.ToolTip = _renderNote;
        _showPrompt.Margin = new Thickness(0, 10, 0, 0);
        _renderControls.Children.Add(_showPrompt);
        _renderControls.Children.Add(Text("Chave API OpenAI"));
        _renderControls.Children.Add(_apiKey);
        _apiKey.ToolTip = "A chave fica apenas nesta janela e é apagada ao fechar.";
        _renderControls.Children.Add(Text("Qualidade da IA"));
        foreach (string quality in new[] { "Baixa - primeiro teste", "Média", "Alta" }) _quality.Items.Add(quality);
        _renderControls.Children.Add(_quality);
        _renderControls.Children.Add(_generate);
        left.Children.Add(_renderControls);
        left.Children.Add(_cancel);
        _cancel.ToolTip = "Cancelar interrompe a espera; a API pode continuar e cobrar a solicitação recebida.";
        left.Children.Add(_saveRender);
        left.Children.Add(_saveReference);
        _saveToHistory.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(_saveToHistory);
        _history.Margin = new Thickness(0, 12, 0, 0);
        left.Children.Add(CreateSectionTitle("Histórico"));
        left.Children.Add(_history);
        _openHistoryFolder.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(_openHistoryFolder);
        var scroll = new ScrollViewer
        {
            Content = CreateCard(left), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left
        };
        scroll.HorizontalAlignment = HorizontalAlignment.Stretch;
        grid.Children.Add(scroll);
        var right = new DockPanel();
        var label = Text("Comparar captura e render", 16, true);
        DockPanel.SetDock(label, Dock.Top); right.Children.Add(label);
        _comparison.Items.Add("Original"); _comparison.Items.Add("Renderizado");
        DockPanel.SetDock(_comparison, Dock.Top); right.Children.Add(_comparison);
        _renderInfo.Text = "Capture uma vista e gere um render para comparar.";
        var note = _renderInfo;
        DockPanel.SetDock(note, Dock.Bottom); right.Children.Add(note);
        right.Children.Add(new Border
        {
            Background = Brushes.White, BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = _preview
        });
        var comparisonCard = CreateCard(right);
        comparisonCard.Visibility = Visibility.Collapsed;
        Grid.SetColumn(comparisonCard, 2); grid.Children.Add(comparisonCard);
        toggleComparison.Click += (_, _) =>
        {
            bool expanded = toggleComparison.IsChecked == true;
            comparisonCard.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            settingsColumn.Width = new GridLength(expanded ? 360 : 520);
            comparisonColumn.Width = expanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            scroll.MaxWidth = expanded ? 360 : 520;
        };
        return grid;
    }

    private static TextBlock Text(string value, double size = 13, bool bold = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(0, 12, 0, 8)
    };

    private static UIElement Placeholder(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Text(title, 22, true)); panel.Children.Add(Text(description));
        return panel;
    }

    private CaptureSettings ReadSettings()
    {
        if (_ratio.SelectedIndex < 0) throw new InvalidOperationException("Selecione um formato.");
        var format = _formats[_ratio.SelectedIndex];
        int width, height;
        if (format.Width == 0)
        {
            if (!int.TryParse(_width.Text, out width) || !int.TryParse(_height.Text, out height))
                throw new InvalidOperationException("Informe largura e altura como números inteiros.");
        }
        else
        {
            int edge = _resolution.SelectedItem is int selected ? selected : 2048;
            if (format.Width >= format.Height)
            { width = edge; height = (int)Math.Round((double)edge * format.Height / format.Width); }
            else
            { height = edge; width = (int)Math.Round((double)edge * format.Width / format.Height); }
        }
        if (width < 256 || height < 256 || width > 4096 || height > 4096)
            throw new InvalidOperationException("Use dimensões entre 256 e 4096 pixels.");
        return new CaptureSettings(width, height,
            _frameMode.SelectedIndex == 1 ? FrameMode.Crop : FrameMode.Fit,
            _horizontal.Value / 100, _vertical.Value / 100, _zoom.Value);
    }

    private void UpdateDimensions()
    {
        InvalidateRender();
        bool custom = _ratio.SelectedIndex == _formats.Length - 1;
        _customDimensions.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        _width.IsEnabled = custom; _height.IsEnabled = custom; _resolution.IsEnabled = !custom;
        bool crop = _frameMode.SelectedIndex == 1;
        _cropControls.IsEnabled = crop;
        _adjustmentValues.Text = $"Horizontal: {_horizontal.Value:F0}% | Vertical: {_vertical.Value:F0}% | Zoom: {_zoom.Value:F2}x";
        _frameNote.Text = crop
            ? "Preenche o formato. Ajuste a posição e a ampliação para escolher a região visível. Pode cortar elementos."
            : "Preserva toda a imagem exportada e adiciona margens brancas quando necessário. Mantém os espaços vazios da vista original.";
        _previewTimer.Stop();
        try
        {
            CaptureSettings settings = ReadSettings();
            _dimensions.Text = $"Saída: {settings.Width} x {settings.Height} pixels";
            if (_original is not null && !_busy)
            {
                _save.IsEnabled = false;
                _status.Text = "Atualizando a prévia do enquadramento...";
                _previewTimer.Start();
            }
        }
        catch (Exception exception)
        {
            _dimensions.Text = exception.Message;
            _save.IsEnabled = false; _generate.IsEnabled = false;
            if (_original is not null) _status.Text = "Ajuste as dimensões para atualizar a prévia e salvar.";
        }
    }

    private void RequestPreparation()
    {
        if (_busy) return;
        try
        {
            if (PrepareRequested is null) throw new InvalidOperationException("O painel não está conectado ao Revit.");
            ClearMaterialConfirmations();
            InvalidateRender();
            SetBusy(true);
            _status.Text = "Preparando uma nova vista 3D para captura. Aguarde a abertura da cópia.";
            PrepareRequested.Invoke();
        }
        catch (Exception exception) { ReceiveFailure(exception.Message); }
    }

    internal void ReceivePreparedView(string name, string? note)
    {
        if (!IsVisible) return;
        InvalidateRender();
        ClearMaterialConfirmations();
        _original = null; _originalPath = null; _bim = null; _preview.Source = null; _viewName = name;
        _bimSummary.Text = "Capture a nova vista para atualizar os dados BIM.";
        SetBusy(false);
        _status.Text = $"Vista limpa aberta: {name}. Ajuste a moldura e capture novamente." +
            (note is null ? "" : " " + note);
    }

    private void RequestGuide()
    {
        if (_busy) return;
        try
        {
            _frameMode.SelectedIndex = 1;
            CaptureSettings settings = ReadSettings();
            if (GuideRequested is null) throw new InvalidOperationException("O painel não está conectado ao Revit.");
            _status.Text = "Abrindo a moldura na vista do Revit...";
            GuideRequested.Invoke(settings);
        }
        catch (Exception exception) { ReceiveFailure(exception.Message); }
    }

    private void RequestCapture()
    {
        if (_busy) return;
        try
        {
            CaptureSettings settings = ReadSettings();
            if (CaptureRequested is null) throw new InvalidOperationException("O painel não está conectado ao Revit.");
            SetBusy(true);
            _status.Text = "Captura e leitura BIM solicitadas. Aguarde o Revit concluir. Se houver um comando ativo, finalize-o ou pressione Esc.";
            CaptureRequested.Invoke(settings);
        }
        catch (Exception exception) { ReceiveFailure(exception.Message); }
    }

    private void SetBusy(bool busy)
    {
        _showPanoramaBim.IsEnabled = !busy && _panoramaResult is not null;
        _panoramaResolution.IsEnabled = !busy;
        _panoramaFineDetails.IsEnabled = !busy;
        _viewPanorama.IsEnabled = !busy;
        _buildPanorama.IsEnabled = !busy;
        _capturePanorama.IsEnabled = !busy;
        _openPanoramaFolder.IsEnabled = !busy && _panoramaResult is not null;
        _panoramaFace.IsEnabled = !busy && _panoramaResult is not null;
        _busy = busy;
        SetPanoramaAiBusy(busy); _settings.IsEnabled = !busy; _capture.IsEnabled = !busy; _guideButton.IsEnabled = !busy; _prepareButton.IsEnabled = !busy;
        if (busy) _previewTimer.Stop();
        _renderControls.IsEnabled = !busy;
        _showPrompt.IsEnabled = !busy && _original is not null;
        _showBim.IsEnabled = !busy && _original is not null;
        _confirmMaterials.IsEnabled = !busy && _original is not null && _bim is not null && _bim.State != BimReadState.Unavailable && _bim.Materials.Length > 0;
        _save.IsEnabled = false;
        _generate.IsEnabled = !busy && _original is not null;
        _comparison.IsEnabled = !busy;
        _saveRender.IsEnabled = !busy && _renderPng is not null;
        _saveReference.IsEnabled = !busy && _referencePng is not null;
        _saveToHistory.IsEnabled = !busy && _renderPng is not null && _referencePng is not null && _lastRenderCapture is not null && _lastRenderSettings is not null && !_currentRenderSavedToHistory;
        _history.IsEnabled = !busy;
        _openHistoryFolder.IsEnabled = !busy;
    }

    internal void ReceiveCapture(CaptureResult result)
    {
        if (!IsVisible) return;
        InvalidateRender();
        ClearMaterialConfirmations();
        BitmapSource original = ImagePipeline.Load(result.OriginalPath);
        _original = original;
        _originalPath = result.OriginalPath;
        _viewName = result.ViewName;
        _bim = result.Bim;
        _bimSummary.Text = BimContextFormatter.Summary(_bim);
        SetBusy(false);
        RefreshPreview();
    }

    internal void ReceiveFailure(string message)
    {
        if (!IsVisible) return;
        SetBusy(false);
        if (_original is not null) RefreshPreview();
        _status.Text = "Não foi possível capturar: " + message +
            (_original is not null ? " A prévia mantida pertence a captura anterior." : "");
    }

    private void RefreshPreview()
    {
        if (_busy || _original is null) return;
        try
        {
            CaptureSettings settings = ReadSettings();
            if (_comparison.SelectedIndex == 1 && _rendered is not null)
                _preview.Source = _rendered;
            else
                _preview.Source = _reference ?? ImagePipeline.Render(_original, settings, preview: true);
            _generate.IsEnabled = true;
            _saveRender.IsEnabled = _renderPng is not null;
            _save.IsEnabled = true;
            _showPrompt.IsEnabled = true;
            string mode = settings.Mode == FrameMode.Fit ? "imagem inteira" : "recorte";
            _status.Text = _comparison.SelectedIndex == 1 && _rendered is not null
                ? $"Renderizado: {_rendered.PixelWidth} x {_rendered.PixelHeight} px. Use Salvar render PNG para salvar o resultado da IA."
                : _reference is not null
                    ? $"Original enviado a IA: {_reference.PixelWidth} x {_reference.PixelHeight} px. Use Salvar referência enviada à IA para comparar os arquivos."
                    : $"Original: {settings.Width} x {settings.Height} px ({mode}). Vista capturada: {_viewName}. Use Salvar captura PNG para salvar a captura.";
        }
        catch (Exception exception)
        {
            _save.IsEnabled = false; _generate.IsEnabled = false;
            _status.Text = "Não foi possível atualizar a prévia: " + exception.Message;
        }
    }

    private void UpdateRenderDescription()
    {
        InvalidateRender();
        string profile = _profile.SelectedIndex == 1
            ? "Noite: arquitetura legível, luzes existentes e reflexos plausíveis."
            : "Dia: luz natural, materiais preservados e sombras plausíveis.";
        string fidelity = _fidelity.SelectedIndex switch
        {
            0 => "Criativo: permite ambientação conceitual moderada sem alterar a arquitetura.",
            1 => "Controlado: preserva câmera e arquitetura; permite ajustes moderados no contexto existente.",
            _ => "Máxima: solicita preservar câmera, fundo, entorno e elementos existentes. Materiais incertos mantêm sua aparência. Confira o resultado; a IA pode descumprir as instruções."
        };
        _renderNote.Text = profile + "\n\n" + fidelity;
    }

    private void InvalidateRender()
    {
        _rendered = null; _renderPng = null; _reference = null; _referencePng = null;
        _lastRenderCapture = null; _lastRenderSettings = null; _lastRenderQuality = null;
        _lastRenderWidth = 0; _lastRenderHeight = 0; _currentRenderSavedToHistory = false;
        _saveToHistory.Content = "Salvar no histórico";
        _saveRender.IsEnabled = false; _saveReference.IsEnabled = false; _saveToHistory.IsEnabled = false;
        _renderInfo.Text = "Gere novamente após alterar enquadramento, perfil ou fidelidade.";
        _comparison.SelectedIndex = 0;
    }

    private async Task GenerateRenderAsync()
    {
        if (_busy || _original is null) return;
        CancellationTokenSource? cancellation = null;
        try
        {
            CaptureSettings capture = ReadSettings();
            var size = OpenAiImageProvider.OutputSize(capture.Width, capture.Height);
            string key = _apiKey.Password.Trim();
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Insira sua chave API OpenAI no campo protegido. Não envie a chave pelo chat.");
            var settings = new RenderSettings(
                _profile.SelectedIndex == 1 ? RenderProfile.BumpNight : RenderProfile.BumpDay,
                _fidelity.SelectedIndex switch { 0 => FidelityMode.Creative, 1 => FidelityMode.Controlled, _ => FidelityMode.Maximum });
            string rules = Path.Combine(Path.GetDirectoryName(typeof(PromptBuilder).Assembly.Location)!, "data");
            string prompt = PromptBuilder.BuildForImageEdit(settings, new RenderScene(_viewName, size.Width, size.Height, _bim, CurrentMaterialConfirmations()), rules);
            string quality = _quality.SelectedIndex switch { 1 => "medium", 2 => "high", _ => "low" };
            // Enquadrar e codificar na thread WPF, antes da chamada de rede.
            BitmapSource reference = ImagePipeline.PrepareImageEditReference(_original, capture, size.Width, size.Height);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(reference));
            using var bytes = new MemoryStream(); encoder.Save(bytes);
            var request = new ImageEditRequest(bytes.ToArray(), prompt, size.Width, size.Height, quality);
            cancellation = new CancellationTokenSource();
            _renderCancellation = cancellation;
            SetBusy(true); _cancel.IsEnabled = true;
            _status.Text = $"Gerando render {size.Width} x {size.Height} px. Uma solicitação paga enviada para a API; aguarde.";
            // HttpClient e provedor nao acessam objetos da API do Revit.
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            ImageEditResult result;
            try { result = await new OpenAiImageProvider(http).EditAsync(request, key, timeout.Token); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new ImageProviderException("Tempo de espera esgotado. A API pode concluir e cobrar a solicitação. Confira o uso antes de tentar novamente."); }
            if (_closed || cancellation.IsCancellationRequested) return;
            using var imageBytes = new MemoryStream(result.Png);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = imageBytes; image.EndInit(); image.Freeze();
            if (image.PixelWidth != result.Width || image.PixelHeight != result.Height)
                throw new ImageProviderException("Não foi possível validar as dimensões do render.");
            _rendered = image; _renderPng = result.Png; _reference = reference; _referencePng = request.Png;
            _lastRenderCapture = capture; _lastRenderSettings = settings; _lastRenderQuality = quality;
            _lastRenderWidth = result.Width; _lastRenderHeight = result.Height; _currentRenderSavedToHistory = false;
            _saveToHistory.Content = "Salvar no histórico";
            _renderInfo.Text = $"Render: {result.Width} x {result.Height} px | {settings.Profile} | {PromptBuilder.FidelityLabel(settings.Fidelity)} | qualidade {quality}. Salvamento na resolução nativa, sem ampliação.";
            SetBusy(false); _comparison.SelectedIndex = 1; RefreshPreview();
            _status.Text = "Render recebido. Ele não foi salvo automaticamente. Se quiser guardar esta geração, clique em Salvar no histórico.";
        }
        catch (OperationCanceledException)
        { if (!_closed) _status.Text = "Espera cancelada. A captura foi preservada. A API pode cobrar a solicitação já recebida."; }
        catch (ImageProviderException exception) { if (!_closed) _status.Text = exception.Message; }
        catch (ArgumentException exception) { if (!_closed) _status.Text = exception.Message; }
        catch (HttpRequestException) { if (!_closed) _status.Text = "Falha de conexão. Nenhuma repetição automática foi enviada; confira o uso da API antes de tentar novamente."; }
        catch (Exception) { if (!_closed) _status.Text = "Não foi possível preparar ou ler o render. A captura original foi preservada."; }
        finally
        {
            if (!_closed)
            {
                string status = _status.Text;
                SetBusy(false); RefreshPreview(); _status.Text = status; _cancel.IsEnabled = false;
            }
            _renderCancellation = null; cancellation?.Dispose();
        }
    }

    private void SaveCurrentGenerationToHistory()
    {
        if (_busy || _currentRenderSavedToHistory || _renderPng is null || _referencePng is null ||
            _lastRenderCapture is null || _lastRenderSettings is null || string.IsNullOrWhiteSpace(_lastRenderQuality)) return;
        try
        {
            SaveGenerationToHistory(_lastRenderCapture, _lastRenderSettings, _lastRenderQuality,
                _lastRenderWidth, _lastRenderHeight, _referencePng, _renderPng);
            _currentRenderSavedToHistory = true;
            _saveToHistory.Content = "Salvo no histórico";
            _saveToHistory.IsEnabled = false;
            _status.Text = "Geração salva no histórico local com referência, render e configurações.";
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha ao salvar geração no histórico local.", exception);
            _status.Text = "Não foi possível salvar no histórico. O render continua disponível no painel; você pode tentar novamente ou usar os botões de salvamento manual.";
            _saveToHistory.IsEnabled = true;
        }
    }

    private void SaveGenerationToHistory(CaptureSettings capture, RenderSettings settings, string quality, int width, int height, byte[] referencePng, byte[] renderPng)
    {
        Directory.CreateDirectory(HistoryRoot);
        DateTimeOffset createdAt = DateTimeOffset.Now;
        string id = Guid.NewGuid().ToString("N");
        string offset = (createdAt.Offset < TimeSpan.Zero ? "m" : "p") + createdAt.ToString("zzz").TrimStart('+', '-').Replace(":", "");
        string folderName = createdAt.ToString("yyyyMMdd_HHmmss_fff") + "_" + offset + "_" + id[..8];
        string pending = Path.Combine(HistoryRoot, ".pending-" + id);
        string destination = Path.Combine(HistoryRoot, folderName);
        Directory.CreateDirectory(pending);
        try
        {
            const string referenceFile = "referencia.png";
            const string renderFile = "render.png";
            const string metadataFile = "generation.json";
            File.WriteAllBytes(Path.Combine(pending, referenceFile), referencePng);
            File.WriteAllBytes(Path.Combine(pending, renderFile), renderPng);
            MaterialConfirmation[] confirmed = CurrentMaterialConfirmations();
            HistoryMaterial[] materials = confirmed.Select(c => new HistoryMaterial(
                c.MaterialKey, c.Name, c.MaterialClass, c.Categories, c.Description)).ToArray();
            var metadata = new HistoryMetadata(
                1, id, createdAt, _viewName,
                settings.Profile == RenderProfile.BumpNight ? "Bump Noite - Comercial Premium" : "Bump Dia",
                PromptBuilder.FidelityLabel(settings.Fidelity), quality, width, height,
                new HistoryCapture(capture.Width, capture.Height, capture.Mode.ToString(), capture.Horizontal, capture.Vertical, capture.Zoom),
                materials, referenceFile, renderFile);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            File.WriteAllText(Path.Combine(pending, metadataFile), JsonSerializer.Serialize(metadata, jsonOptions));
            if (!File.Exists(Path.Combine(pending, referenceFile)) || !File.Exists(Path.Combine(pending, renderFile)) || !File.Exists(Path.Combine(pending, metadataFile)))
                throw new IOException("Os arquivos do histórico não foram gravados integralmente.");
            Directory.Move(pending, destination);
        }
        catch
        {
            try { if (Directory.Exists(pending)) Directory.Delete(pending, true); } catch { }
            throw;
        }
    }

    private static List<HistoryItem> LoadHistory()
    {
        var items = new List<HistoryItem>();
        Directory.CreateDirectory(HistoryRoot);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (string folder in Directory.GetDirectories(HistoryRoot))
        {
            if (Path.GetFileName(folder).StartsWith(".pending-", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                string metadataPath = Path.Combine(folder, "generation.json");
                if (!File.Exists(metadataPath)) continue;
                HistoryMetadata? metadata = JsonSerializer.Deserialize<HistoryMetadata>(File.ReadAllText(metadataPath), jsonOptions);
                if (metadata is null || metadata.SchemaVersion != 1) continue;
                if (!File.Exists(Path.Combine(folder, metadata.ReferenceFile)) || !File.Exists(Path.Combine(folder, metadata.RenderFile))) continue;
                items.Add(new HistoryItem(folder, metadata));
            }
            catch
            {
                // Uma entrada corrompida ou incompleta nao deve impedir o restante do historico.
            }
        }
        return items.OrderByDescending(i => i.Metadata.CreatedAt).ToList();
    }

    private void OpenHistoryFolder()
    {
        try
        {
            Directory.CreateDirectory(HistoryRoot);
            Process.Start(new ProcessStartInfo { FileName = HistoryRoot, UseShellExecute = true });
        }
        catch (Exception exception) { _status.Text = "Não foi possível abrir a pasta do histórico: " + exception.Message; }
    }

    private static void ShowHistoryImage(Window owner, BitmapSource source, string title)
    {
        var image = new Image
        {
            Source = source, Width = source.PixelWidth, Height = source.PixelHeight,
            Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var scale = new ScaleTransform(1, 1);
        image.LayoutTransform = scale;
        var scroll = new ScrollViewer
        {
            Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White
        };
        var zoom = new Slider { Minimum = 0.05, Maximum = 4, Value = 1, Width = 220 };
        var value = new TextBlock { Text = "100%", Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var fit = CreateCompactButton("Encaixar");
        var actual = CreateCompactButton("100%");
        actual.Margin = new Thickness(8, 0, 0, 0);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        controls.Children.Add(new TextBlock { Text = "Ampliação", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        controls.Children.Add(zoom); controls.Children.Add(value); controls.Children.Add(fit); controls.Children.Add(actual);
        var root = new DockPanel();
        DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls); root.Children.Add(scroll);
        var dialog = new Window
        {
            Title = title, Owner = owner, Width = 1000, Height = 720,
            MinWidth = 600, MinHeight = 400, Content = root,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        ApplyWindowTheme(dialog);
        void FitImage()
        {
            double width = Math.Max(1, scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth - 20);
            double height = Math.Max(1, scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight - 20);
            zoom.Value = Math.Clamp(Math.Min(width / source.PixelWidth, height / source.PixelHeight), zoom.Minimum, zoom.Maximum);
        }
        zoom.ValueChanged += (_, _) =>
        {
            scale.ScaleX = zoom.Value; scale.ScaleY = zoom.Value;
            value.Text = $"{zoom.Value * 100:F0}%";
        };
        fit.Click += (_, _) => FitImage();
        actual.Click += (_, _) => zoom.Value = 1;
        dialog.Loaded += (_, _) => FitImage();
        dialog.ShowDialog();
    }

    private void ShowHistory()
    {
        if (_busy) return;
        try
        {
            List<HistoryItem> items = LoadHistory();
            var list = new ListBox { MinWidth = 230, Margin = new Thickness(0, 0, 12, 0) };
            foreach (HistoryItem item in items) list.Items.Add(item);
            var referenceImage = new Image { Stretch = Stretch.Uniform };
            var renderImage = new Image { Stretch = Stretch.Uniform };
            var enlargeReference = CreateCompactButton("Ampliar referência");
            var enlargeRender = CreateCompactButton("Ampliar render");
            enlargeReference.IsEnabled = false; enlargeRender.IsEnabled = false;
            var details = new TextBox
            {
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 160,
                FontSize = 13, Margin = new Thickness(0, 8, 0, 8), VerticalContentAlignment = VerticalAlignment.Top
            };
            var openSelected = new Button { Content = "Abrir pasta desta geração", Height = 36, IsEnabled = false };
            var comparison = new Grid();
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DockPanel CreatePane(string title, Image image, Button enlarge)
            {
                var pane = new DockPanel();
                var heading = Text(title, 16, true);
                DockPanel.SetDock(heading, Dock.Top); pane.Children.Add(heading);
                DockPanel.SetDock(enlarge, Dock.Bottom); pane.Children.Add(enlarge);
                pane.Children.Add(CreateCard(image));
                return pane;
            }
            var referencePane = CreatePane("Referência enviada à IA", referenceImage, enlargeReference);
            var renderPane = CreatePane("Render gerado", renderImage, enlargeRender);
            comparison.Children.Add(referencePane);
            Grid.SetColumn(renderPane, 2); comparison.Children.Add(renderPane);
            var right = new DockPanel();
            var note = Text("Comparação lado a lado: mesmo enquadramento e escala visual. Consultar o histórico não altera a captura atual.", 12);
            DockPanel.SetDock(note, Dock.Top); right.Children.Add(note);
            DockPanel.SetDock(openSelected, Dock.Bottom); right.Children.Add(openSelected);
            DockPanel.SetDock(details, Dock.Bottom); right.Children.Add(details);
            right.Children.Add(comparison);
            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(list); Grid.SetColumn(right, 1); grid.Children.Add(right);
            var dialog = new Window
            {
                Title = "Arqyzeng BIM — Histórico", Owner = this,
                Width = 1240, Height = 800, MinWidth = 940, MinHeight = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = grid
            };

            ApplyWindowTheme(dialog);
            var itemText = new FrameworkElementFactory(typeof(TextBlock));
            itemText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            itemText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            itemText.SetValue(TextBlock.FontSizeProperty, 13d);
            list.ItemTemplate = new DataTemplate { VisualTree = itemText };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            if (items.Count == 0)
            {
                list.Visibility = Visibility.Collapsed;
                right.Visibility = Visibility.Collapsed;
                var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
                empty.Children.Add(CreateSectionTitle("Nenhuma geração salva no histórico."));
                empty.Children.Add(Text("Suas gerações aparecem aqui ao usar Salvar no histórico.", 13));
                var emptyCard = CreateCard(empty);
                Grid.SetColumnSpan(emptyCard, 2); grid.Children.Add(emptyCard);
            }

            void RefreshHistoryPreview()
            {
                referenceImage.Source = null; renderImage.Source = null;
                enlargeReference.IsEnabled = false; enlargeRender.IsEnabled = false;
                openSelected.IsEnabled = false;
                if (list.SelectedItem is not HistoryItem selected)
                {
                    details.Text = items.Count == 0 ? "Nenhuma geração salva no histórico." : "Selecione uma geração.";
                    return;
                }
                openSelected.IsEnabled = true;
                try
                {
                    // Carregar as duas imagens antes de exibir, evitando comparar entradas diferentes.
                    BitmapSource reference = ImagePipeline.Load(Path.Combine(selected.Folder, selected.Metadata.ReferenceFile));
                    BitmapSource render = ImagePipeline.Load(Path.Combine(selected.Folder, selected.Metadata.RenderFile));
                    if (reference.PixelWidth != render.PixelWidth || reference.PixelHeight != render.PixelHeight)
                        throw new InvalidOperationException("Referência e render possuem dimensões diferentes. Consulte os arquivos na pasta desta geração.");
                    string materials = selected.Metadata.Materials.Length == 0
                        ? "Nenhum material confirmado manualmente."
                        : string.Join(Environment.NewLine, selected.Metadata.Materials.Select(m => $"- {m.Name}: {m.Description}"));
                    details.Text =
                        $"Data: {selected.Metadata.CreatedAt:O}{Environment.NewLine}" +
                        $"Vista: {selected.Metadata.ViewName}{Environment.NewLine}" +
                        $"Perfil: {selected.Metadata.Profile}{Environment.NewLine}" +
                        $"Fidelidade: {selected.Metadata.Fidelity}{Environment.NewLine}" +
                        $"Qualidade: {selected.Metadata.Quality}{Environment.NewLine}" +
                        $"Resolução IA: {selected.Metadata.Width} x {selected.Metadata.Height}{Environment.NewLine}" +
                        $"Captura: {selected.Metadata.Capture.Width} x {selected.Metadata.Capture.Height} | {selected.Metadata.Capture.FrameMode} | zoom {selected.Metadata.Capture.Zoom:F2}x{Environment.NewLine}{Environment.NewLine}" +
                        "Materiais confirmados:" + Environment.NewLine + materials;
                    referenceImage.Source = reference; renderImage.Source = render;
                    enlargeReference.IsEnabled = true; enlargeRender.IsEnabled = true;
                }
                catch (Exception exception)
                {
                    details.Text = "Não foi possível visualizar esta geração: " + exception.Message +
                        Environment.NewLine + "Selecione outra entrada ou abra a pasta para conferir os arquivos.";
                }
            }

            list.SelectionChanged += (_, _) => RefreshHistoryPreview();
            enlargeReference.Click += (_, _) =>
            {
                if (referenceImage.Source is BitmapSource source)
                    ShowHistoryImage(dialog, source, "Arqyzeng BIM - Referência enviada à IA");
            };
            enlargeRender.Click += (_, _) =>
            {
                if (renderImage.Source is BitmapSource source)
                    ShowHistoryImage(dialog, source, "Arqyzeng BIM - Render gerado");
            };
            openSelected.Click += (_, _) =>
            {
                if (list.SelectedItem is not HistoryItem selected) return;
                try { Process.Start(new ProcessStartInfo { FileName = selected.Folder, UseShellExecute = true }); }
                catch (Exception exception) { details.Text = "Não foi possível abrir a pasta: " + exception.Message; }
            };
            if (items.Count > 0) list.SelectedIndex = 0; else RefreshHistoryPreview();
            dialog.ShowDialog();
        }
        catch (Exception exception) { _status.Text = "Não foi possível abrir o histórico: " + exception.Message; }
    }

    private void SaveRender()
    {
        if (_busy || _renderPng is null || _rendered is null) return;
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salvar render Arqyzeng BIM", Filter = "Imagem PNG (*.png)|*.png",
                DefaultExt = ".png", AddExtension = true, OverwritePrompt = true,
                FileName = $"Arqyzeng_Render_{_rendered.PixelWidth}x{_rendered.PixelHeight}_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (_originalPath is not null && string.Equals(Path.GetFullPath(_originalPath), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Escolha outro nome para preservar a captura original.");
            File.WriteAllBytes(dialog.FileName, _renderPng);
            _status.Text = $"Render salvo em {_rendered.PixelWidth} x {_rendered.PixelHeight} px: {dialog.FileName}";
        }
        catch (Exception exception) { _status.Text = "Não foi possível salvar o render: " + exception.Message; }
    }

    private void SaveReference()
    {
        if (_busy || _referencePng is null || _reference is null) return;
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Salvar referência enviada à IA", Filter = "Imagem PNG (*.png)|*.png",
                DefaultExt = ".png", AddExtension = true, OverwritePrompt = true,
                FileName = $"Arqyzeng_ReferenciaIA_{_reference.PixelWidth}x{_reference.PixelHeight}_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (_originalPath is not null && string.Equals(Path.GetFullPath(_originalPath), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Escolha outro nome para preservar a captura original.");
            File.WriteAllBytes(dialog.FileName, _referencePng);
            _status.Text = "Referência enviada à IA salva. Compare este arquivo com o render na mesma resolução.";
        }
        catch (Exception exception) { _status.Text = "Não foi possível salvar a referência: " + exception.Message; }
    }

    private void ShowRenderPrompt()
    {
        try
        {
            if (_busy || _original is null) throw new InvalidOperationException("Capture uma vista primeiro.");
            CaptureSettings capture = ReadSettings();
            var settings = new RenderSettings(
                _profile.SelectedIndex == 1 ? RenderProfile.BumpNight : RenderProfile.BumpDay,
                _fidelity.SelectedIndex switch
                { 0 => FidelityMode.Creative, 1 => FidelityMode.Controlled, _ => FidelityMode.Maximum });
            string rules = Path.Combine(Path.GetDirectoryName(typeof(PromptBuilder).Assembly.Location)!, "data");
            var size = OpenAiImageProvider.OutputSize(capture.Width, capture.Height);
            string prompt = PromptBuilder.BuildForImageEdit(settings, new RenderScene(_viewName, size.Width, size.Height, _bim, CurrentMaterialConfirmations()), rules);
            ShowText("Prompt de render", $"Instruções para a referência que será enviada em {size.Width} x {size.Height} px. Visualizar este prompt não envia uma solicitação.", prompt);
        }
        catch (Exception exception) { _status.Text = "Não foi possível gerar o prompt: " + exception.Message; }
    }

    private void ShowBimContext()
    {
        if (_busy || _original is null) return;
        ShowText("Dados BIM da captura", "Levantamento de candidatos da vista no instante da captura. Capture novamente após alterar o modelo. Não corresponde a uma seleção por pixel.", BimContextFormatter.Format(_bim));
    }

    private MaterialConfirmation[] CurrentMaterialConfirmations()
    {
        if (_bim is null) return Array.Empty<MaterialConfirmation>();
        return _bim.Materials
            .Select(m => new MaterialConfirmation(MaterialKey(m), m.Name, m.MaterialClass,
                m.UsesByCategory.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                _materialConfirmations.TryGetValue(MaterialKey(m), out string? description) ? description.Trim() : ""))
            .Where(c => !string.IsNullOrWhiteSpace(c.Description))
            .ToArray();
    }

    private static string MaterialKey(BimMaterial material) =>
        material.Name + "\u001f" + material.MaterialClass + "\u001f" + material.ColorRgb;

    private void ClearMaterialConfirmations()
    {
        _materialConfirmations.Clear();
    }

    private void ShowMaterialConfirmations()
    {
        if (_busy || _original is null || _bim is null || _bim.State == BimReadState.Unavailable || _bim.Materials.Length == 0) return;
        var editors = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        var rows = new StackPanel();
        foreach (BimMaterial material in _bim.Materials)
        {
            string key = MaterialKey(material);
            var card = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) };
            var content = new StackPanel();
            content.Children.Add(Text(material.Name, 14, true));
            string categories = material.UsesByCategory.Length == 0 ? "sem categoria associada retornada" : string.Join(", ", material.UsesByCategory.Select(c => c.Name));
            content.Children.Add(Text("Classe: " + (string.IsNullOrWhiteSpace(material.MaterialClass) ? "não informada" : material.MaterialClass) + " | Categorias: " + categories, 12));
            content.Children.Add(Text("Descrição conhecida (deixe em branco se incerto):", 12));
            var editor = new TextBox { Text = _materialConfirmations.TryGetValue(key, out string? value) ? value : "", MinHeight = 30, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
            editor.TextChanged += (_, _) => InvalidateRender();
            editors[key] = editor; content.Children.Add(editor); card.Child = content; rows.Children.Add(card);
        }
        var save = new Button { Content = "Aplicar confirmações", Height = 38, Margin = new Thickness(0, 8, 0, 0) };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var header = Text("Descreva somente materiais que você reconhece com segurança. Ex.: telha metálica cinza, vidro incolor, concreto aparente. Campos vazios permanecem incertos.", 13);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        DockPanel.SetDock(save, Dock.Bottom); panel.Children.Add(save);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows, Margin = new Thickness(0, 10, 0, 10) });
        var dialog = new Window { Title = "Arqyzeng BIM - Confirmar materiais", Owner = this, Width = 760, Height = 650, MinWidth = 560, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        ApplyWindowTheme(dialog);
        ApplyPrimaryButtonStyle(save);
        save.Click += (_, _) =>
        {
            _materialConfirmations.Clear();
            foreach (var pair in editors) if (!string.IsNullOrWhiteSpace(pair.Value.Text)) _materialConfirmations[pair.Key] = pair.Value.Text.Trim();
            InvalidateRender();
            _status.Text = _materialConfirmations.Count == 0 ? "Confirmações de materiais limpas. Materiais incertos permanecem sem descrição manual." : $"{_materialConfirmations.Count} material(is) confirmado(s). Gere novamente para usar as novas informações.";
            dialog.DialogResult = true;
        };
        dialog.ShowDialog();
    }

    private void ShowText(string title, string description, string text)
    {
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = CreateSectionTitle(title);
        heading.FontSize = 24;
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var info = Text(description, 13);
        info.Margin = new Thickness(0, 0, 0, 16);
        DockPanel.SetDock(info, Dock.Top); panel.Children.Add(info);
        var dialog = new Window
        {
            Title = "Arqyzeng BIM — " + title, Owner = this,
            Width = 800, Height = 650, MinWidth = 500, MinHeight = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel
        };
        ApplyWindowTheme(dialog);
        var actions = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var close = new Button { Content = "Fechar", Width = 100, Height = 38, IsCancel = true };
        ApplyPrimaryButtonStyle(close);
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Right); actions.Children.Add(close);
        var copy = new Button { Content = "Copiar texto", Width = 120, Height = 38, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(copy, Dock.Left); actions.Children.Add(copy);
        var feedback = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        actions.Children.Add(feedback);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(text); feedback.Text = "Texto copiado."; }
            catch (System.Runtime.InteropServices.ExternalException)
            { feedback.Text = "Não foi possível copiar. Selecione o texto e use Ctrl+C."; }
        };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        panel.Children.Add(new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            FontSize = 14, Padding = new Thickness(16)
        });
        dialog.ShowDialog();
    }

    private void SaveCapture()
    {
        string? temporary = null;
        try
        {
            if (_busy || _original is null || _originalPath is null)
                throw new InvalidOperationException("Capture uma vista primeiro.");
            _previewTimer.Stop();
            CaptureSettings settings = ReadSettings();
            RefreshPreview();
            if (!_save.IsEnabled) return;
            var dialog = new SaveFileDialog
            {
                Title = "Salvar captura Arqyzeng BIM", Filter = "Imagem PNG (*.png)|*.png",
                DefaultExt = ".png", AddExtension = true, OverwritePrompt = true,
                FileName = $"Arqyzeng_{settings.Width}x{settings.Height}_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (string.Equals(Path.GetFullPath(_originalPath), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Escolha outro nome para preservar a imagem original.");
            temporary = Path.Combine(Path.GetDirectoryName(_originalPath)!, "saida-" + Guid.NewGuid().ToString("N") + ".png");
            ImagePipeline.Save(_original, temporary, settings);
            File.Copy(temporary, dialog.FileName, true);
            _status.Text = $"PNG salvo: {settings.Width} x {settings.Height} px. Arquivo: {dialog.FileName}";
        }
        catch (Exception exception) { _status.Text = "Não foi possível salvar: " + exception.Message; }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { /* A captura original permanece preservada. */ }
                catch (UnauthorizedAccessException) { /* Limpeza podera ser feita posteriormente. */ }
            }
        }
    }
}
