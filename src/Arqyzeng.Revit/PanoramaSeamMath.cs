using System;

namespace Arqyzeng.Panorama;

// Operacoes em BGRA: deslocamento ciclico sem interpolacao e faixa de edicao limitada.
internal static class PanoramaSeamMath
{
    internal static int HalfBand(int width) => width / 24; // 30 graus no total.
    private static void Validate(byte[] pixels, int width, int height)
    {
        if (width < 512 || width > 2048 || width != height * 2L || pixels.Length != (long)width * height * 4)
            throw new ArgumentException("Raster de reparo 360 invalido.");
    }
    internal static byte[] RotateHalf(byte[] pixels, int width, int height)
    {
        Validate(pixels, width, height);
        byte[] result = new byte[pixels.Length]; int halfRow = width * 2;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            Buffer.BlockCopy(pixels, row + halfRow, result, row, halfRow);
            Buffer.BlockCopy(pixels, row, result, row + halfRow, halfRow);
        }
        return result;
    }
    internal static byte[] Mask(int width, int height)
    {
        byte[] pixels = new byte[checked(width * height * 4)]; Validate(pixels, width, height);
        int half = HalfBand(width), center = width / 2;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            pixels[(y * width + x) * 4 + 3] = (byte)(x >= center - half && x < center + half ? 0 : 255);
        return pixels;
    }
    internal static byte[] LimitToBand(byte[] before, byte[] edited, int width, int height)
    {
        Validate(before, width, height); Validate(edited, width, height);
        byte[] output = (byte[])before.Clone(); int half = HalfBand(width), start = width / 2 - half, end = width / 2 + half;
        int taper = Math.Min(16, half / 4);
        for (int y = 0; y < height; y++)
        for (int x = start; x < end; x++)
        {
            double weight = Math.Min(1, Math.Min(x - start, end - 1 - x) / (double)taper);
            int offset = (y * width + x) * 4;
            for (int c = 0; c < 3; c++) output[offset + c] = (byte)Math.Round(before[offset + c] * (1 - weight) + edited[offset + c] * weight);
            output[offset + 3] = before[offset + 3];
        }
        return output; // Fora da faixa: copia exata do render anterior, mesmo se a API mudar tudo.
    }
    internal static string Prompt(int width, int height) =>
        $"Repair only the transparent mask band in this {width} x {height} full 360x180 equirectangular panorama. " +
        "The panorama was cyclically shifted by 180 degrees so its damaged left-right join is now at the center of the image. " +
        "Join the existing ceiling, wall and floor contours continuously across that center, using both sides as constraints. " +
        "Preserve their angular positions, materials, color and illumination. Reconstruct only the local discontinuity; do not redesign architecture or move furniture. " +
        "Keep the equirectangular projection, including curved contours in the flat image and correct zenith/nadir geometry. " +
        "Do not straighten into a perspective photo, add objects, invent finishes, change exposure globally or create another camera. " +
        "Leave all opaque-mask pixels unchanged. Return the entire image at the exact requested dimensions.";
}
