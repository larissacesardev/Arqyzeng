using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
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
    private double _yaw, _pitch;
    private bool _dragging, _fullscreen;
    private Point _lastPointer;
    private WindowState _previousState;
    private WindowStyle _previousStyle;
    private ResizeMode _previousResize;
    private string _filename = "";
    private string _imageSize = "";
    private string _textureInfo = "";

    internal PanoramaViewerWindow(string path) : this() { LoadPanorama(path); }
    internal PanoramaViewerWindow(BitmapSource image, string label) : this() { LoadPanorama(image, label); }
    private PanoramaViewerWindow()
    {
        Title = "Arqyzeng BIM - Viewer 360 v8.2.7";
        Width = 1100; Height = 760; MinWidth = 640; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 30, 34));
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
        toolbar.Children.Add(new TextBlock { Text = "Campo de visão", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) });
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
    }
    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4) };
    private void LoadPanorama(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("PNG não encontrado.");
        LoadPanorama(ImagePipeline.Load(path), System.IO.Path.GetFileName(path));
    }
    private void LoadPanorama(BitmapSource image, string label)
    {
        if (image.PixelWidth != 2 * image.PixelHeight || image.PixelHeight < 256 || image.PixelWidth > 8192)
            throw new InvalidOperationException("Use um panorama PNG 2:1, com largura entre 512 e 8192 pixels.");
        var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        rgb.Freeze();
        var group = new Model3DGroup();
        // Evitar uma unica textura de 8192 px: carregar partes em resolucao nativa.
        int textureLimit = (int)Math.Min(RenderCapability.MaxHardwareTextureSize.Width,
            RenderCapability.MaxHardwareTextureSize.Height);
        int tileSize = textureLimit >= 68 ? Math.Min(1024, textureLimit - 4) : 1024;
        var tiles = PanoramaViewerMath.TextureTiles(image.PixelWidth, image.PixelHeight, tileSize);
        foreach (var tile in tiles)
        {
            var texture = CreateTexture(rgb, tile);
            var brush = new ImageBrush(texture) { Stretch = Stretch.Fill, TileMode = TileMode.None };
            brush.Freeze();
            // Emissivo conserva os pixels: o viewer nao aplica iluminacao adicional.
            var material = new EmissiveMaterial(brush); material.Freeze();
            var model = new GeometryModel3D(CreateSpherePatch(tile, image.PixelWidth, image.PixelHeight), material);
            model.Freeze(); group.Children.Add(model);
        }
        group.Freeze();
        // Trocar apenas depois de montar tudo: manter a imagem anterior se a abertura falhar.
        _viewport.Children.Clear(); _viewport.Children.Add(new ModelVisual3D { Content = group });
        _textureInfo = $"{tiles.Length} partes em resolução nativa | WPF tier {RenderCapability.Tier >> 16}";
        _filename = label; _imageSize = $"{image.PixelWidth} x {image.PixelHeight}";
        _yaw = 0; _pitch = 0; _fov.Value = 75; UpdateCamera();
    }
    internal void LookAtLongitude(double yaw)
    {
        if (!double.IsFinite(yaw)) throw new ArgumentException("Direção inválida.");
        _yaw = yaw; _pitch = 0; UpdateCamera();
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
        _info.Text = $"{_filename} | {_imageSize} px | {_textureInfo} | Direção: {_yaw:F1} graus | Altura: {_pitch:F1} graus | FOV: {_fov.Value:F0} graus\nArraste para olhar ao redor. Roda do mouse: zoom. Setas: direção. F11: tela cheia. Esc: sair da tela cheia.";
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
    private static BitmapSource CreateTexture(BitmapSource source, PanoramaTextureTile tile)
    {
        int stride = tile.PaddedWidth * 4;
        byte[] pixels = new byte[stride * tile.PaddedHeight];
        for (int y = 0; y < tile.PaddedHeight; y++)
        {
            int sourceY = Math.Clamp(tile.Y + y - PanoramaTextureTile.Gutter, 0, source.PixelHeight - 1);
            source.CopyPixels(new Int32Rect(tile.X, sourceY, tile.Width, 1), pixels, stride,
                y * stride + PanoramaTextureTile.Gutter * 4);
            for (int margin = 0; margin < PanoramaTextureTile.Gutter; margin++)
            {
                int left = PanoramaViewerMath.WrapPixelX(tile.X + margin - PanoramaTextureTile.Gutter, source.PixelWidth);
                int right = PanoramaViewerMath.WrapPixelX(tile.X + tile.Width + margin, source.PixelWidth);
                source.CopyPixels(new Int32Rect(left, sourceY, 1, 1), pixels, stride, y * stride + margin * 4);
                source.CopyPixels(new Int32Rect(right, sourceY, 1, 1), pixels, stride,
                    y * stride + (PanoramaTextureTile.Gutter + tile.Width + margin) * 4);
            }
        }
        var bitmap = BitmapSource.Create(tile.PaddedWidth, tile.PaddedHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze(); return bitmap;
    }
    private static MeshGeometry3D CreateSpherePatch(PanoramaTextureTile tile, int width, int height)
    {
        // Cerca de 256 x 128 segmentos na esfera inteira; vertices comuns nas juncoes.
        int columns = Math.Max(1, (int)Math.Ceiling(256d * tile.Width / width));
        int rows = Math.Max(1, (int)Math.Ceiling(128d * tile.Height / height));
        const double radius = 10;
        var mesh = new MeshGeometry3D();
        for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                double localU = (double)x / columns, localV = (double)y / rows;
                double u = (tile.X + localU * tile.Width) / width;
                double v = (tile.Y + localV * tile.Height) / height;
                var direction = PanoramaViewerMath.SpherePoint(u, v);
                mesh.Positions.Add(new Point3D(direction.X * radius, direction.Y * radius, direction.Z * radius));
                mesh.Normals.Add(new Vector3D(-direction.X, -direction.Y, -direction.Z));
                mesh.TextureCoordinates.Add(new Point(tile.TextureU(localU), tile.TextureV(localV)));
            }
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int a = y * (columns + 1) + x, b = a + columns + 1, c = a + 1, d = b + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c);
                mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(d);
            }
        mesh.Freeze(); return mesh;
    }
}
