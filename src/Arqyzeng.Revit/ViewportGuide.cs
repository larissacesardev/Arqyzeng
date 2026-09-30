using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ShapeRectangle = System.Windows.Shapes.Rectangle;

namespace Arqyzeng.Revit;

// Criado e encerrado no contexto do ExternalEvent. O timer usa somente Win32/WPF.
internal sealed class ViewportGuide
{
    private readonly UIApplication _application;
    private readonly PrintWindow _print;
    private readonly CaptureSettings _settings;
    private readonly Autodesk.Revit.DB.Document _document;
    private readonly Autodesk.Revit.DB.ElementId _viewId;
    private readonly Func<bool, bool> _finish;
    private readonly Window _overlay;
    private readonly Window _toolbar;
    private readonly GuideSurface _surface;
    private readonly DispatcherTimer _visibilityTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly IntPtr _revitHandle;
    private bool _stopped;
    private bool _finishing;
    private bool _hasRectangle;
    private bool _loggedLayout;
    private DateTime _lastUpdate;

    internal ViewportGuide(UIApplication application, PrintWindow print, CaptureSettings settings, Func<bool, bool> finish)
    {
        _application = application; _print = print; _settings = settings; _finish = finish;
        _document = application.ActiveUIDocument.Document;
        _viewId = _document.ActiveView.Id;
        _revitHandle = application.MainWindowHandle;
        _surface = new GuideSurface(settings);
        _overlay = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, ResizeMode = ResizeMode.NoResize,
            Width = 100, Height = 100, Content = _surface,
            Topmost = true, SizeToContent = SizeToContent.Manual
        };
        new WindowInteropHelper(_overlay).Owner = _revitHandle;
        _overlay.SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(_overlay).Handle;
            long style = NativeGuide.GetWindowLongPtr(handle, -20).ToInt64();
            // Janela em camadas: deixa o mouse passar para o Revit e nao recebe foco.
            NativeGuide.SetWindowLongPtr(handle, -20, new IntPtr(style | 0x20 | 0x08000000 | 0x80));
        };
        _toolbar = CreateToolbar();
        new WindowInteropHelper(_toolbar).Owner = _revitHandle;
        _toolbar.Closed += (_, _) => { if (!_stopped) Finish(false); };
        _visibilityTimer.Tick += (_, _) => UpdateVisibility();
    }

    internal void Start()
    {
        try
        {
            _print.Hide();
            _overlay.Show();
            UpdateRectangle();
            _toolbar.Show();
            _application.Idling += OnIdling;
            _visibilityTimer.Start();
        }
        catch { Stop(true); throw; }
    }

    private Window CreateToolbar()
    {
        var window = new Window
        {
            Title = "Arqyzeng - Moldura v3.1", Topmost = true, Width = 320, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248))
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Area da captura: {_settings.Width} x {_settings.Height} px",
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Ajuste o modelo diretamente no Revit. A area clara entra no PNG; a area cinza fica fora. Arraste este painel se ele cobrir a vista.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8)
        });
        var capture = new Button { Content = "Capturar este enquadramento", Height = 34 };
        capture.Click += (_, _) => Finish(true);
        panel.Children.Add(capture);
        var back = new Button { Content = "Voltar ao Print", Height = 30, Margin = new Thickness(0, 6, 0, 0) };
        back.Click += (_, _) => Finish(false);
        panel.Children.Add(back);
        window.Content = panel;
        return window;
    }

    private void Finish(bool capture)
    {
        if (_stopped || _finishing) return;
        _finishing = true;
        _toolbar.IsEnabled = false;
        _overlay.Hide();
        try
        {
            if (!_finish(capture))
            {
                _finishing = false; _toolbar.IsEnabled = true;
                _toolbar.Title = "Moldura - finalize o comando no Revit";
                UpdateVisibility();
            }
        }
        catch { _finishing = false; _toolbar.IsEnabled = true; throw; }
    }

    internal bool MatchesActiveView(UIApplication application)
    {
        var document = application.ActiveUIDocument?.Document;
        return document is not null && document.IsValidObject && _document.IsValidObject &&
            document.Equals(_document) && document.ActiveView.Id.Equals(_viewId);
    }

    private void OnIdling(object? sender, IdlingEventArgs args)
    {
        if (_stopped || _finishing || DateTime.UtcNow - _lastUpdate < TimeSpan.FromMilliseconds(200)) return;
        _lastUpdate = DateTime.UtcNow;
        try
        {
            if (!_print.IsLoaded) { Stop(false); return; }
            if (!MatchesActiveView(_application))
            { Finish(false); return; }
            UpdateRectangle();
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha ao atualizar moldura.", exception);
            Finish(false);
        }
    }

    private void UpdateRectangle()
    {
        var uiView = _application.ActiveUIDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId.Equals(_viewId));
        if (uiView is null) throw new InvalidOperationException("A vista 3D nao esta aberta na tela.");
        using var rectangle = uiView.GetWindowRectangle();
        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        if (width <= 0 || height <= 0) { _hasRectangle = false; _overlay.Hide(); return; }
        IntPtr handle = new WindowInteropHelper(_overlay).Handle;
        // Move primeiro para o monitor da vista e consulta a escala WPF desse monitor.
        if (!NativeGuide.SetWindowPos(handle, new IntPtr(-1), rectangle.Left, rectangle.Top, width, height, 0x10))
            throw new InvalidOperationException("Nao foi possivel posicionar a moldura na vista.");
        Matrix fromDevice = PresentationSource.FromVisual(_overlay)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Vector logicalSize = fromDevice.Transform(new Vector(width, height));
        _overlay.Width = logicalSize.X;
        _overlay.Height = logicalSize.Y;
        _surface.SetViewportSize(logicalSize.X, logicalSize.Y);
        _overlay.UpdateLayout();
        // Sincroniza o HWND fisico com o tamanho logico, mantendo a camada acima da vista sem dar foco.
        if (!NativeGuide.SetWindowPos(handle, new IntPtr(-1), rectangle.Left, rectangle.Top, width, height, 0x10))
            throw new InvalidOperationException("Nao foi possivel sincronizar a moldura na vista.");
        if (!_hasRectangle)
        {
            var transform = PresentationSource.FromVisual(_overlay)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point position = transform.Transform(new Point(rectangle.Left + 12, rectangle.Top + 12));
            _toolbar.Left = position.X; _toolbar.Top = position.Y;
        }
        _hasRectangle = true;
        if (!_loggedLayout)
        {
            PluginLog.Write($"Moldura: vista {width}x{height} px; camada {logicalSize.X:F0}x{logicalSize.Y:F0} DIP.");
            _loggedLayout = true;
        }
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_stopped || _finishing) return;
        IntPtr foreground = NativeGuide.GetForegroundWindow();
        IntPtr toolbarHandle = new WindowInteropHelper(_toolbar).Handle;
        bool visible = _hasRectangle && !NativeGuide.IsIconic(_revitHandle) &&
            (foreground == _revitHandle || foreground == toolbarHandle);
        if (visible)
        {
            if (!_overlay.IsVisible) _overlay.Show();
            if (!_toolbar.IsVisible && toolbarHandle != IntPtr.Zero) _toolbar.Show();
            // O painel de botoes fica acima da mascara, sem interromper pan/zoom no Revit.
            if (toolbarHandle != IntPtr.Zero)
                NativeGuide.SetWindowPos(toolbarHandle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        }
        else
        {
            if (_overlay.IsVisible) _overlay.Hide();
            if (_toolbar.IsVisible) _toolbar.Hide();
        }
    }

    // Chamado pelo handler ou pelo OnShutdown, sempre no contexto da API do Revit.
    internal void Stop(bool restore)
    {
        if (_stopped) return;
        _stopped = true;
        _visibilityTimer.Stop();
        _application.Idling -= OnIdling;
        _overlay.Close(); _toolbar.Close();
        if (restore && _print.IsLoaded) { _print.Show(); _print.Activate(); }
    }
}

internal sealed class GuideSurface : Canvas
{
    private readonly CaptureSettings _settings;
    private readonly ShapeRectangle[] _masks = new ShapeRectangle[4];
    private readonly ShapeRectangle _darkBorder = new() { Stroke = Brushes.Black, StrokeThickness = 5 };
    private readonly ShapeRectangle _border = new() { Stroke = Brushes.LimeGreen, StrokeThickness = 3 };

    internal GuideSurface(CaptureSettings settings)
    {
        _settings = settings;
        Background = Brushes.Transparent;
        IsHitTestVisible = false;
        ClipToBounds = true;
        var gray = new SolidColorBrush(Color.FromArgb(165, 60, 60, 60));
        gray.Freeze();
        for (int i = 0; i < _masks.Length; i++)
        {
            _masks[i] = new ShapeRectangle { Fill = gray };
            Children.Add(_masks[i]);
        }
        Children.Add(_darkBorder); Children.Add(_border);
    }

    internal void SetViewportSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        Width = width; Height = height;
        double ratio = (double)_settings.Width / _settings.Height;
        double frameWidth = Math.Min(width, height * ratio) / _settings.Zoom;
        double frameHeight = Math.Min(height, frameWidth / ratio);
        double x = Math.Max(0, width - frameWidth) * _settings.Horizontal;
        double y = Math.Max(0, height - frameHeight) * _settings.Vertical;
        Place(_masks[0], 0, 0, width, y);
        Place(_masks[1], 0, y + frameHeight, width, Math.Max(0, height - y - frameHeight));
        Place(_masks[2], 0, y, x, frameHeight);
        Place(_masks[3], x + frameWidth, y, Math.Max(0, width - x - frameWidth), frameHeight);
        Place(_darkBorder, x + 2, y + 2, Math.Max(0, frameWidth - 4), Math.Max(0, frameHeight - 4));
        Place(_border, x + 2, y + 2, Math.Max(0, frameWidth - 4), Math.Max(0, frameHeight - 4));
    }

    private static void Place(ShapeRectangle rectangle, double x, double y, double width, double height)
    {
        SetLeft(rectangle, x); SetTop(rectangle, y);
        rectangle.Width = width; rectangle.Height = height;
    }
}

internal static class NativeGuide
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr handle);
}