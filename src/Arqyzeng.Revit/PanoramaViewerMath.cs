using System;
namespace Arqyzeng.Panorama;

internal static class PanoramaViewerMath
{
    // WPF: frente = -Z. Longitude cresce para a direita; latitude cresce para cima.
    internal static (double X, double Y, double Z) Direction(double yawDegrees, double pitchDegrees)
    {
        double yaw = yawDegrees * Math.PI / 180, pitch = pitchDegrees * Math.PI / 180;
        double horizontal = Math.Cos(pitch);
        return (Math.Sin(yaw) * horizontal, Math.Sin(pitch), -Math.Cos(yaw) * horizontal);
    }
    internal static (double X, double Y, double Z) SpherePoint(double u, double v) =>
        Direction((u - .5) * 360, (.5 - v) * 180);
    // Partes pequenas, sem reduzir a resolucao total. A margem e preenchida
    // com pixels vizinhos reais, com fechamento horizontal e limite nos polos.
    internal static PanoramaTextureTile[] TextureTiles(int width, int height, int tileSize = 1024)
    {
        if (width < 512 || width > 8192 || height * 2L != width || tileSize < 64 || tileSize > 1024)
            throw new ArgumentException("Dimensoes ou tamanho de textura invalidos.");
        int columns = (width + tileSize - 1) / tileSize, rows = (height + tileSize - 1) / tileSize;
        var tiles = new PanoramaTextureTile[columns * rows];
        int index = 0;
        for (int y = 0; y < height; y += tileSize)
            for (int x = 0; x < width; x += tileSize)
                tiles[index++] = new PanoramaTextureTile(x, y, Math.Min(tileSize, width - x),
                    Math.Min(tileSize, height - y));
        return tiles;
    }
    internal static int WrapPixelX(int x, int width) => (x % width + width) % width;
    internal static double WrapYaw(double yaw) => (yaw + 180 - Math.Floor((yaw + 180) / 360) * 360) - 180;
}
internal sealed record PanoramaTextureTile(int X, int Y, int Width, int Height)
{
    internal const int Gutter = 2;
    internal int PaddedWidth => Width + Gutter * 2;
    internal int PaddedHeight => Height + Gutter * 2;
    internal double TextureU(double fraction) => (Gutter + fraction * Width) / PaddedWidth;
    internal double TextureV(double fraction) => (Gutter + fraction * Height) / PaddedHeight;
}
