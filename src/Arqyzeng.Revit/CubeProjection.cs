using System;

namespace Arqyzeng.Panorama;

// Coordenadas relativas: X = direita da camera, Y = cima, Z = frente.
// Ordem das faces: front, right, back, left, up, down.
internal static class CubeProjection
{
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