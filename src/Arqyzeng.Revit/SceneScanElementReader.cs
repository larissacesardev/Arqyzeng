using System.Collections.Immutable;
using Arqyzeng.SceneScan;
using Autodesk.Revit.DB;

namespace Arqyzeng.Revit;

internal static class SceneScanElementReader
{
    internal static SceneElementSnapshot Read(Element element, View3D view, string documentKey,
        SceneCameraSnapshot camera, SceneVisibilityEvidence evidence, bool parameters)
    {
        bool failed = false;
        T? Optional<T>(Func<T> read)
        {
            try { return read(); }
            catch (Exception) { failed = true; return default; }
        }
        var identity = new SceneElementIdentity(SceneCameraMath.ElementKey(documentKey, element.UniqueId), documentKey, element.Id.Value, element.UniqueId);
        Category? category = Optional(() => element.Category);
        ElementType? type = Optional(() => element.Document.GetElement(element.GetTypeId()) as ElementType);
        string? family = Optional(() => element is FamilyInstance instance ? instance.Symbol?.FamilyName : type?.FamilyName);
        string? host = Optional(() => element is FamilyInstance instance ? instance.Host?.UniqueId : null);
        SceneBounds? bounds = Optional(() => Bounds(element, view));
        SceneVector? center = bounds is null ? null : SceneCameraMath.Scale(SceneCameraMath.Add(bounds.Minimum, bounds.Maximum), .5);
        var values = ImmutableArray.CreateBuilder<SceneSemanticParameter>();
        if (parameters && type is not null)
        {
            // Deliberately exclude Mark/Comments/project names and arbitrary parameter enumeration.
            foreach (var p in new[] { BuiltInParameter.ALL_MODEL_MANUFACTURER, BuiltInParameter.ALL_MODEL_MODEL })
            {
                string? value = Optional(() => type.get_Parameter(p)?.AsString());
                if (!string.IsNullOrWhiteSpace(value)) values.Add(new(p.ToString(), value.Length > 256 ? value[..256] : value, "RevitElementTypeParameter"));
            }
        }
        string? name = Optional(() => element.Name);
        string? categoryName = Optional(() => category?.Name);
        long? categoryId = Optional(() => category?.Id.Value);
        long? typeId = Optional(() => type?.Id.Value);
        string? typeName = Optional(() => type?.Name);
        string? typeUniqueId = Optional(() => type?.UniqueId);
        return new(identity, name, categoryName, categoryId, element.GetType().Name, family, typeId, typeName, typeUniqueId,
            host, bounds, center, center is { } c ? SceneCameraMath.WorldToCamera(camera, c) : null,
            center is { } d ? SceneCameraMath.Length(SceneCameraMath.Subtract(d, camera.OriginWorldMeters)) : null,
            center is { } point ? SceneCameraMath.Project(camera, point) : null, evidence,
            failed ? SceneReadState.Partial : SceneReadState.Read, null, null, values.ToImmutable());
    }

    private static SceneBounds? Bounds(Element element, View3D view)
    {
        using var box = element.get_BoundingBox(view);
        if (box is null) return null;
        using var transform = box.Transform;
        var points = new List<SceneVector>(8);
        for (int i = 0; i < 8; i++)
        {
            var local = new XYZ((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z);
            points.Add(SceneScanUnits.Point(transform.OfPoint(local)));
        }
        if (points.Any(p => !SceneCameraMath.Finite(p))) return null;
        return new(new(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
            new(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
    }

    internal static SceneGeometrySummary Geometry(Element element, View3D view, SceneScanOptions options, Func<bool> stop)
    {
        int objects = 0, solids = 0, faces = 0;
        bool limited = false, failed = false;
        void Visit(GeometryElement geometry, int depth)
        {
            foreach (GeometryObject item in geometry)
            {
                if (stop() || objects >= options.MaxGeometryObjectsPerElement) { limited = true; break; }
                objects++;
                if (item is Solid solid) { solids++; faces += solid.Faces.Size; }
                else if (item is GeometryInstance instance)
                {
                    if (depth >= options.MaxGeometryDepth) { limited = true; continue; }
                    try { using var nested = instance.GetInstanceGeometry(); if (nested is not null) Visit(nested, depth + 1); }
                    catch (Exception) { failed = true; }
                }
            }
        }
        try
        {
            using var settings = new Options { View = view, IncludeNonVisibleObjects = false, ComputeReferences = false };
            using var geometry = element.get_Geometry(settings);
            if (geometry is null) return new(SceneReadState.Unavailable, 0, 0, 0, false, "Element.Geometry(View)");
            Visit(geometry, 0);
        }
        catch (Exception) { failed = true; }
        return new(failed || limited ? SceneReadState.Partial : SceneReadState.Read, solids, faces, objects, limited, "Element.Geometry(View)_NoMeshes");
    }
}
