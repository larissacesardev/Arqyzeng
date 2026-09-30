using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Arqyzeng.Panorama;

namespace Arqyzeng.Revit;

internal sealed class PanoramaViewerWindow : Window
{
    private readonly Viewport3D _viewport = new() { IsHitTestVisible = false };
    private readonly PerspectiveCamera _camera = new() {
        Position = new Point3D(0, 0, 0), LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0),
        FieldOfView = 75, NearPlaneDistance = .01, FarPlaneDistance = 50 };
    private readonly TextBlock _info = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8, 12, 8) };
    private readonly Border _interaction;
    private readonly Slider _fov = new() { Minimum = 35, Maximum = 110, Value = 75, Width = 170, Margin = new Thickness(8) };
    private readonly MeshGeometry3D _sphere;
    private double _yaw, _pitch;
    private bool _dragging, _fullscreen;
    private Point _lastPointer;
    private WindowState _previousState;
    private WindowStyle _previousStyle;
    private ResizeMode _previousResize;
    private string _filename = "";
    private string _imageSize = "";

    internal PanoramaViewerWindow(string path)
    {
        Title = "Arqyzeng BIM - Viewer 360 v8.2";
        Width = 1100; Height = 760; MinWidth = 640; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 30, 34));
        _sphere = CreateSphere();
        _viewport.Camera = _camera;
        _interaction = new Border { Background = Brushes.Black, Child = _viewport, Cursor = Cursors.Hand, Focusable = true, ClipToBounds = true };
        var root = new DockPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(8) };
        var open = Button("Abrir outro panorama PNG");
        open.Click += (_, _) => OpenAnotherPanorama();
        toolbar.Children.Add(open);
        var reset = Button("Voltar para frente");
        reset.Click += (_, _) => { _yaw = 0; _pitch = 0; _fov.Value = 75; UpdateCamera(); };
        toolbar.Children.Add(reset);
        foreach (var target in new[] { (Label: "Teto", Pitch: 89.9), (Label: "Piso", Pitch: -89.9) })
        {
            var look = Button(target.Label);
            look.Click += (_, _) => { _pitch = target.Pitch; UpdateCamera(); };
            toolbar.Children.Add(look);
        }
        var fullscreen = Button("Tela cheia (F11)"); fullscreen.Click += (_, _) => ToggleFullscreen(); toolbar.Children.Add(fullscreen);
        toolbar.Children.Add(new TextBlock { Text = "Campo de visao", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) });
        toolbar.Children.Add(_fov);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_info, Dock.Bottom); root.Children.Add(_info);
        root.Children.Add(_interaction); Content = root;
        _fov.ValueChanged += (_, _) => UpdateCamera();
        _interaction.MouseLeftButtonDown += (_, e) => {
            _dragging = true; _lastPointer = e.GetPosition(_interaction); _interaction.CaptureMouse(); _interaction.Focus(); e.Handled = true;
        };
        _interaction.MouseMove += (_, e) => {
            if (!_dragging) return;
            Point current = e.GetPosition(_interaction);
            double sensitivity = _camera.FieldOfView / Math.Max(300, _interaction.ActualWidth);
            // Arrastar a imagem para a direita revela o que esta a esquerda, como um panorama segurado pela mao.
            _yaw -= (current.X - _lastPointer.X) * sensitivity;
            _pitch += (current.Y - _lastPointer.Y) * sensitivity;
            _lastPointer = current; UpdateCamera(); e.Handled = true;
        };
        _interaction.MouseLeftButtonUp += (_, e) => { StopDrag(); e.Handled = true; };
        _interaction.LostMouseCapture += (_, _) => _dragging = false;
        _interaction.MouseWheel += (_, e) => { _fov.Value = Math.Clamp(_fov.Value - e.Delta / 120d * 5, 35, 110); e.Handled = true; };
        Deactivated += (_, _) => StopDrag();
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == Key.Escape && _fullscreen) { ToggleFullscreen(); e.Handled = true; }
            else if (e.Key == Key.Left) { _yaw -= 5; UpdateCamera(); e.Handled = true; }
            else if (e.Key == Key.Right) { _yaw += 5; UpdateCamera(); e.Handled = true; }
            else if (e.Key == Key.Up) { _pitch += 5; UpdateCamera(); e.Handled = true; }
            else if (e.Key == Key.Down) { _pitch -= 5; UpdateCamera(); e.Handled = true; }
        };
        Closed += (_, _) => { StopDrag(); _viewport.Children.Clear(); _viewport.Camera = null; };
        LoadPanorama(path);
    }
    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4) };
    private void LoadPanorama(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("PNG nao encontrado.");
        var image = ImagePipeline.Load(path);
        if (image.PixelWidth != 2 * image.PixelHeight || image.PixelHeight < 256 || image.PixelWidth > 8192)
            throw new InvalidOperationException("Use um panorama PNG 2:1, com largura entre 512 e 8192 pixels.");
        var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
        brush.Freeze();
        // Material emissivo evita aplicar outra iluminacao sobre a imagem pronta.
        var material = new EmissiveMaterial(brush); material.Freeze();
        var model = new GeometryModel3D(_sphere, material); model.Freeze();
        _viewport.Children.Clear(); _viewport.Children.Add(new ModelVisual3D { Content = model });
        _filename = System.IO.Path.GetFileName(path); _imageSize = $"{image.PixelWidth} x {image.PixelHeight}";
        _yaw = 0; _pitch = 0; _fov.Value = 75; UpdateCamera();
    }
    private void OpenAnotherPanorama()
    {
        var dialog = new OpenFileDialog { Title = "Abrir panorama equiretangular PNG 2:1", Filter = "Panorama PNG (*.png)|*.png" };
        if (dialog.ShowDialog(this) != true) return;
        try { LoadPanorama(dialog.FileName); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Panorama 360", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void UpdateCamera()
    {
        _yaw = PanoramaViewerMath.WrapYaw(_yaw); _pitch = Math.Clamp(_pitch, -89.9, 89.9);
        var look = PanoramaViewerMath.Direction(_yaw, _pitch);
        _camera.LookDirection = new Vector3D(look.X, look.Y, look.Z);
        _camera.FieldOfView = _fov.Value;
        _info.Text = $"{_filename} | {_imageSize} px | Direcao: {_yaw:F1} graus | Altura: {_pitch:F1} graus | FOV: {_fov.Value:F0} graus\nArraste para olhar ao redor. Roda do mouse: zoom. Setas: direcao. F11: tela cheia. Esc: sair da tela cheia.";
    }
    private void StopDrag() { _dragging = false; if (_interaction.IsMouseCaptured) _interaction.ReleaseMouseCapture(); }
    private void ToggleFullscreen()
    {
        StopDrag();
        if (!_fullscreen)
        {
            _previousState = WindowState; _previousStyle = WindowStyle; _previousResize = ResizeMode;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        }
        else { WindowState = WindowState.Normal; WindowStyle = _previousStyle; ResizeMode = _previousResize; WindowState = _previousState; }
        _fullscreen = !_fullscreen;
    }
    private static MeshGeometry3D CreateSphere()
    {
        const int columns = 128, rows = 64; const double radius = 10;
        var mesh = new MeshGeometry3D();
        for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                double u = (double)x / columns, v = (double)y / rows;
                var direction = PanoramaViewerMath.SpherePoint(u, v);
                mesh.Positions.Add(new Point3D(direction.X * radius, direction.Y * radius, direction.Z * radius));
                mesh.Normals.Add(new Vector3D(-direction.X, -direction.Y, -direction.Z));
                mesh.TextureCoordinates.Add(new Point(u, v));
            }
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int a = y * (columns + 1) + x, b = a + columns + 1, c = a + 1, d = b + 1;
                // Faces orientadas para o observador no centro da esfera.
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c);
                mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(d);
            }
        mesh.Freeze(); return mesh;
    }
}