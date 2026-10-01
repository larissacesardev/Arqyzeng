using System;

namespace Arqyzeng.Panorama;

// Coordenadas relativas: X = direita da camera, Y = cima, Z = frente.
// Ordem das faces: front, right, back, left, up, down.
internal static class CubeProjection
{
    internal static (int Width, int Height) OutputSize(int facePixels)
    {
        if (facePixels is not (1024 or 2048))
            throw new ArgumentOutOfRangeException(nameof(facePixels), "Use faces de 1024 ou 2048 pixels.");
        return (facePixels * 4, facePixels * 2);
    }

    internal static bool CanNormalizeRaster(int width, int height, int facePixels)
    {
        OutputSize(facePixels);
        return Math.Abs((long)width - facePixels) <= 1 && Math.Abs((long)height - facePixels) <= 1;
    }

    // Exportar 94 graus e extrair exatamente 90 graus: margem real, sem
    // reduzir um cubemap ja capturado a 90 graus ou deslocar suas emendas.
    internal const double CaptureFovDegrees = 94;
    internal static int OverscanPixels(int facePixels, int samplingMultiplier = 1)
    {
        OutputSize(facePixels);
        if (samplingMultiplier is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(samplingMultiplier), "Use amostragem 1x ou 2x.");
        return (int)Math.Ceiling(facePixels * samplingMultiplier * Math.Tan(CaptureFovDegrees * Math.PI / 360));
    }

    internal static (double X, double Y, double Width, double Height) CentralFaceRegion(
        int rawWidth, int rawHeight, double horizontalFov, double verticalFov)
    {
        if (rawWidth <= 0 || rawHeight <= 0 || !double.IsFinite(horizontalFov) ||
            !double.IsFinite(verticalFov) || horizontalFov <= 90 || verticalFov <= 90 ||
            horizontalFov >= 100 || verticalFov >= 100)
            throw new ArgumentException("Raster ou campo de visao do overscan invalido.");
        double width = rawWidth / Math.Tan(horizontalFov * Math.PI / 360);
        double height = rawHeight / Math.Tan(verticalFov * Math.PI / 360);
        double x = (rawWidth - width) / 2, y = (rawHeight - height) / 2;
        if (x < 8 || y < 8)
            throw new ArgumentException("Margem de overscan insuficiente para separar as bordas da exportacao.");
        return (x, y, width, height);
    }

    internal static (int Face, double U, double V) Map(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
            Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) < 1e-15)
            throw new ArgumentException("Direcao de projecao invalida.");
        double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
        int face; double right, up, depth;
        if (az >= ax && az >= ay)
        {
            face = z >= 0 ? 0 : 2; depth = az;
            right = z >= 0 ? x : -x; up = y;
        }
        else if (ax >= ay)
        {
            face = x >= 0 ? 1 : 3; depth = ax;
            right = x >= 0 ? -z : z; up = y;
        }
        else
        {
            face = y >= 0 ? 4 : 5; depth = ay;
            right = x; up = y >= 0 ? -z : z;
        }
        return (face, (right / depth + 1) / 2, (1 - up / depth) / 2);
    }
}