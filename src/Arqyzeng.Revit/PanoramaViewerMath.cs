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
    internal static double WrapYaw(double yaw) => (yaw + 180 - Math.Floor((yaw + 180) / 360) * 360) - 180;
}