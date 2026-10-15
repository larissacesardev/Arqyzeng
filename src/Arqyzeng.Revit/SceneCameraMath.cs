using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Arqyzeng.SceneScan;

internal static class SceneCameraMath
{
    internal static SceneVector Add(SceneVector a, SceneVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    internal static SceneVector Subtract(SceneVector a, SceneVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    internal static SceneVector Scale(SceneVector a, double factor) => new(a.X * factor, a.Y * factor, a.Z * factor);
    internal static double Dot(SceneVector a, SceneVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static SceneVector Cross(SceneVector a, SceneVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    internal static double Length(SceneVector a) => Math.Sqrt(Dot(a, a));
    internal static bool Finite(SceneVector a) => double.IsFinite(a.X) && double.IsFinite(a.Y) && double.IsFinite(a.Z);
    internal static SceneVector Normalize(SceneVector a)
    {
        double length = Length(a);
        if (!Finite(a) || !double.IsFinite(length) || length < 1e-12) throw new ArgumentException("Invalid direction.");
        return Scale(a, 1 / length);
    }

    internal static (SceneVector Forward, SceneVector Up, SceneVector Right) Basis(SceneVector forward, SceneVector up)
    {
        forward = Normalize(forward);
        SceneVector right = Normalize(Cross(forward, up));
        return (forward, Normalize(Cross(right, forward)), right);
    }

    // Camera axes: X right, Y up, Z forward. This is explicitly not Revit's view transform basis.
    internal static SceneVector WorldToCamera(SceneCameraSnapshot camera, SceneVector world)
    {
        var offset = Subtract(world, camera.OriginWorldMeters);
        return new(Dot(offset, camera.RightWorld), Dot(offset, camera.UpWorld), Dot(offset, camera.ForwardWorld));
    }

    internal static ScenePoint2? Project(SceneCameraSnapshot camera, SceneVector world)
    {
        if (camera.Projection is not { } p) return null;
        var local = WorldToCamera(camera, world);
        if (local.Z <= 0) return null;
        double scale = camera.IsPerspective ? p.TargetDistanceMeters!.Value / local.Z : 1;
        return new(.5 + (local.X * scale - p.RightOffsetMeters) / p.WidthMeters,
            .5 - (local.Y * scale - p.UpOffsetMeters) / p.HeightMeters);
    }

    internal static (SceneVector Origin, SceneVector Direction) Ray(SceneCameraSnapshot camera, ScenePoint2 sample)
    {
        if (!double.IsFinite(sample.X) || !double.IsFinite(sample.Y) || sample.X < 0 || sample.X > 1 || sample.Y < 0 || sample.Y > 1)
            throw new ArgumentOutOfRangeException(nameof(sample));
        var p = camera.Projection ?? throw new InvalidOperationException("Projection unavailable.");
        ValidateProjection(camera.IsPerspective, p);
        double x = (sample.X - .5) * p.WidthMeters + p.RightOffsetMeters;
        double y = (.5 - sample.Y) * p.HeightMeters + p.UpOffsetMeters;
        var lateral = Add(Scale(camera.RightWorld, x), Scale(camera.UpWorld, y));
        if (!camera.IsPerspective)
            return (Add(camera.OriginWorldMeters, Add(lateral, Scale(camera.ForwardWorld, p.NearMeters))), camera.ForwardWorld);
        var direction = Normalize(Add(lateral, Scale(camera.ForwardWorld, p.TargetDistanceMeters!.Value)));
        // Start on the near plane so clipped foreground does not occlude the evaluated view.
        double alongRay = p.NearMeters / Dot(direction, camera.ForwardWorld);
        return (Add(camera.OriginWorldMeters, Scale(direction, alongRay)), direction);
    }

    internal static void ValidateProjection(bool perspective, SceneProjection p)
    {
        if (!double.IsFinite(p.WidthMeters) || p.WidthMeters <= 0 || !double.IsFinite(p.HeightMeters) || p.HeightMeters <= 0 ||
            !double.IsFinite(p.RightOffsetMeters) || !double.IsFinite(p.UpOffsetMeters) ||
            !double.IsFinite(p.NearMeters) || p.NearMeters < 0 ||
            (perspective && (p.TargetDistanceMeters is not double d || !double.IsFinite(d) || d <= 0)) ||
            (p.FarMeters is double far && (!double.IsFinite(far) || far <= p.NearMeters)))
            throw new ArgumentException("Invalid projection.");
    }

    internal static double Fov(double extent, double target, double offset) =>
        (Math.Atan((offset + extent / 2) / target) - Math.Atan((offset - extent / 2) / target)) * 180 / Math.PI;

    internal static ImmutableArray<ScenePoint2> Grid(SceneScanOptions options)
    {
        options.Validate();
        var samples = ImmutableArray.CreateBuilder<ScenePoint2>(options.RayGridWidth * options.RayGridHeight);
        for (int row = 0; row < options.RayGridHeight; row++)
            for (int column = 0; column < options.RayGridWidth; column++)
                samples.Add(new((column + .5) / options.RayGridWidth, (row + .5) / options.RayGridHeight));
        return samples.ToImmutable();
    }

    internal static ImmutableDictionary<string, SceneElementCoverage> Aggregate(IEnumerable<SceneRayHit> rays)
    {
        var samples = rays.ToArray();
        // Denominator includes misses and failed attempts; no extrapolation to unsampled rays.
        return samples.Where(r => r.State == SceneRayState.Hit && r.ElementKey is not null && r.DistanceFromCameraMeters is not null)
            .GroupBy(r => r.ElementKey!, StringComparer.Ordinal).ToImmutableDictionary(g => g.Key, g => new SceneElementCoverage(
                g.Count(), (double)g.Count() / samples.Length,
                g.Min(r => r.DistanceFromCameraMeters!.Value), g.Average(r => r.DistanceFromCameraMeters!.Value), g.Max(r => r.DistanceFromCameraMeters!.Value),
                new(g.Min(r => r.Sample.X), g.Min(r => r.Sample.Y)), new(g.Max(r => r.Sample.X), g.Max(r => r.Sample.Y)),
                new(g.Average(r => r.Sample.X), g.Average(r => r.Sample.Y)), "EvaluatedGridSamples_IncompletePossible_NotPixelSegmentation"), StringComparer.Ordinal);
    }

    internal static (int Hits, int Misses, int Failures, int UnsupportedLinks, int HitElements) Metrics(IEnumerable<SceneRayHit> rays)
    {
        var samples = rays.ToArray();
        return (samples.Count(r => r.State == SceneRayState.Hit), samples.Count(r => r.State == SceneRayState.Miss),
            samples.Count(r => r.State == SceneRayState.Unavailable), samples.Count(r => r.State == SceneRayState.UnsupportedLink),
            samples.Where(r => r.State == SceneRayState.Hit && r.ElementKey is not null).Select(r => r.ElementKey).Distinct(StringComparer.Ordinal).Count());
    }

    internal static string ElementKey(string documentKey, string uniqueId, string? linkInstanceUniqueId = null)
    {
        if (string.IsNullOrWhiteSpace(documentKey) || string.IsNullOrWhiteSpace(uniqueId)) throw new ArgumentException("Identity unavailable.");
        // Length prefixes prevent delimiter collisions; camera/view never participate in identity.
        string value = $"{documentKey.Length}:{documentKey}{uniqueId.Length}:{uniqueId}{linkInstanceUniqueId?.Length ?? 0}:{linkInstanceUniqueId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
