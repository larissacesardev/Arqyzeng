using System.Collections.Immutable;
using Arqyzeng.SceneScan;
using Autodesk.Revit.DB;

namespace Arqyzeng.Revit;

internal static class SceneScanUnits
{
    internal static double Meters(double value) => UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Meters);
    internal static double InternalLength(double meters) => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
    internal static SceneVector Point(XYZ point) => new(Meters(point.X), Meters(point.Y), Meters(point.Z));
    internal static SceneVector Direction(XYZ vector) => new(vector.X, vector.Y, vector.Z);
    internal static XYZ Point(SceneVector point) => new(InternalLength(point.X), InternalLength(point.Y), InternalLength(point.Z));
    internal static XYZ Direction(SceneVector vector) => new(vector.X, vector.Y, vector.Z);
    internal static SceneOrientedBox Box(BoundingBoxXYZ box)
    {
        using var t = box.Transform;
        return new(Point(box.Min), Point(box.Max), new(Point(t.Origin), Direction(t.BasisX), Direction(t.BasisY), Direction(t.BasisZ)));
    }
}

internal static class SceneScanCameraReader
{
    internal static SceneCameraSnapshot Read(Document document, View3D view,
        ImmutableArray<SceneVector> viewportCorners, Func<bool> stop, List<string> notes)
    {
        using var orientation = view.GetOrientation();
        var basis = SceneCameraMath.Basis(SceneScanUnits.Direction(orientation.ForwardDirection), SceneScanUnits.Direction(orientation.UpDirection));
        SceneProjection? projection = null;
        try
        {
            var context = new CameraContext(stop);
            using var exporter = new CustomExporter(document, context)
            { IncludeGeometricObjects = false, ShouldStopOnError = true };
            exporter.Export(view);
            projection = context.Projection;
            if (projection is not null) SceneCameraMath.ValidateProjection(view.IsPerspective, projection);
        }
        catch (Exception) { notes.Add("CameraProjectionUnavailable: ray scan omitted; orientation remains available."); projection = null; }
        SceneOrientedBox? section = null, crop = null;
        try { if (view.IsSectionBoxActive) { using var box = view.GetSectionBox(); section = SceneScanUnits.Box(box); } }
        catch (Exception) { notes.Add("SectionBoxUnavailable"); }
        try { using var box = view.CropBox; if (box is not null) crop = SceneScanUnits.Box(box); }
        catch (Exception) { notes.Add("CropBoxUnavailable"); }
        return new(SceneScanUnits.Point(orientation.EyePosition), basis.Forward, basis.Up, basis.Right,
            view.IsPerspective, projection,
            view.IsPerspective && projection is not null ? SceneCameraMath.Fov(projection.WidthMeters, projection.TargetDistanceMeters!.Value, projection.RightOffsetMeters) : null,
            view.IsPerspective && projection is not null ? SceneCameraMath.Fov(projection.HeightMeters, projection.TargetDistanceMeters!.Value, projection.UpOffsetMeters) : null,
            section, crop, viewportCorners.IsDefault ? ImmutableArray<SceneVector>.Empty : viewportCorners,
            "View3D.GetOrientation", "ViewProjectionOnly_VisibleRegionAndFinalImageCropNotCalibrated");
    }

    // CustomExporter is used only to obtain public CameraInfo. No geometry export or mutation.
    private sealed class CameraContext(Func<bool> stop) : IExportContext
    {
        internal SceneProjection? Projection { get; private set; }
        public bool Start() => !stop();
        public void Finish() { }
        public bool IsCanceled() => stop();
        public RenderNodeAction OnViewBegin(ViewNode node)
        {
            using var camera = node.GetCameraInfo();
            if (camera is null) return RenderNodeAction.Skip;
            double near = Math.Max(0, SceneScanUnits.Meters(camera.NearDistance));
            double far = SceneScanUnits.Meters(camera.FarDistance);
            Projection = new(SceneScanUnits.Meters(camera.HorizontalExtent), SceneScanUnits.Meters(camera.VerticalExtent),
                camera.IsPerspective ? SceneScanUnits.Meters(camera.TargetDistance) : null,
                SceneScanUnits.Meters(camera.RightOffset), SceneScanUnits.Meters(camera.UpOffset),
                near, double.IsFinite(far) && far > near ? far : null, "CustomExporter.CameraInfo");
            return RenderNodeAction.Skip;
        }
        public void OnViewEnd(ElementId id) { }
        public RenderNodeAction OnElementBegin(ElementId id) => RenderNodeAction.Skip;
        public void OnElementEnd(ElementId id) { }
        public RenderNodeAction OnInstanceBegin(InstanceNode node) => RenderNodeAction.Skip;
        public void OnInstanceEnd(InstanceNode node) { }
        public RenderNodeAction OnLinkBegin(LinkNode node) => RenderNodeAction.Skip;
        public void OnLinkEnd(LinkNode node) { }
        public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Skip;
        public void OnFaceEnd(FaceNode node) { }
        public void OnMaterial(MaterialNode node) { }
        public void OnPolymesh(PolymeshTopology node) { }
        public void OnRPC(RPCNode node) { }
        public void OnLight(LightNode node) { }
    }
}
