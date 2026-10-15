using System;
using System.Collections.Immutable;

namespace Arqyzeng.SceneScan;

// This file and the math/serializer deliberately have no Revit or WPF dependency.
internal enum SceneReadState { Read, Partial, Unavailable, NotRequested }
internal enum SceneRayState { Hit, Miss, Unavailable, UnsupportedLink }
[Flags]
internal enum SceneVisibilityEvidence { Unknown = 0, ViewCollector = 1, RayHit = 2 }

internal readonly record struct SceneVector(double X, double Y, double Z);
internal readonly record struct ScenePoint2(double X, double Y);
internal sealed record SceneBounds(SceneVector Minimum, SceneVector Maximum);
internal sealed record SceneTransform(SceneVector OriginMeters, SceneVector BasisX, SceneVector BasisY, SceneVector BasisZ);
internal sealed record SceneOrientedBox(SceneVector MinimumLocalMeters, SceneVector MaximumLocalMeters, SceneTransform LocalToHost);
internal sealed record SceneDocumentIdentity(string Key, string Source);
internal sealed record SceneElementIdentity(string Key, string DocumentKey, long ElementId, string UniqueId,
    bool IsLinked = false, string? LinkInstanceUniqueId = null);
internal sealed record SceneViewSnapshot(long ElementId, string UniqueId, string Name, string Type,
    bool CropActive, bool SectionBoxActive, string DisplayStyle);
internal sealed record SceneProjection(double WidthMeters, double HeightMeters, double? TargetDistanceMeters,
    double RightOffsetMeters, double UpOffsetMeters, double NearMeters, double? FarMeters, string Source);
internal sealed record SceneCameraSnapshot(SceneVector OriginWorldMeters, SceneVector ForwardWorld,
    SceneVector UpWorld, SceneVector RightWorld, bool IsPerspective, SceneProjection? Projection,
    double? HorizontalFovDegrees, double? VerticalFovDegrees, SceneOrientedBox? SectionBox,
    SceneOrientedBox? CropBox, ImmutableArray<SceneVector> ViewportCornersWorldMeters,
    string OrientationSource, string RasterAlignment);
internal sealed record SceneGeometrySummary(SceneReadState State, int SolidCount, int FaceCount,
    int VisitedObjects, bool Limited, string Source);
internal sealed record SceneSemanticParameter(string Name, string Value, string Source);
internal sealed record SceneElementCoverage(int HitCount, double SampleFraction,
    double MinimumDistanceMeters, double MeanDistanceMeters, double MaximumDistanceMeters,
    ScenePoint2 MinimumSample, ScenePoint2 MaximumSample, ScenePoint2 MeanSample, string Source);
internal sealed record SceneElementSnapshot(SceneElementIdentity Identity, string? Name, string? Category,
    long? CategoryId, string ApiClass, string? Family, long? TypeId, string? TypeName, string? TypeUniqueId,
    string? HostUniqueId, SceneBounds? BoundsWorldMeters, SceneVector? CenterWorldMeters,
    SceneVector? CenterCameraMeters, double? CenterDistanceMeters, ScenePoint2? ProjectedCenter,
    SceneVisibilityEvidence VisibilityEvidence, SceneReadState State, SceneGeometrySummary? Geometry,
    SceneElementCoverage? Coverage, ImmutableArray<SceneSemanticParameter> Parameters,
    string SemanticSource = "RevitCategory/Family/ElementType", string PositionSource = "ViewBoundingBoxCenter",
    SceneTransform? LinkToHost = null);
internal sealed record SceneAppearanceSnapshot(SceneReadState State, long? AssetId, string? AssetName,
    ImmutableArray<double> GenericDiffuseRgba, double? GenericTransparency, double? GenericGlossiness,
    bool? TextureReferencePresent, string TextureResolution, string Source);
internal sealed record SceneMaterialSnapshot(string Key, string DocumentKey, long ElementId, string UniqueId,
    string? Name, string? MaterialClass, string? GraphicsColorRgb, int? GraphicsTransparencyPercent,
    bool? UseRenderAppearanceForShading, SceneAppearanceSnapshot Appearance, SceneReadState State,
    string GraphicsSource = "RevitMaterialGraphics_NotViewOverrides");
internal sealed record SceneSurfaceSnapshot(string Key, string ElementKey, string FaceClass,
    double? AreaSquareMeters, long? BaseMaterialId, long? PaintedMaterialId,
    string? MaterialKey, string MaterialSource, SceneReadState State);
internal sealed record SceneRayHit(int Index, ScenePoint2 Sample, SceneVector OriginWorldMeters,
    SceneVector DirectionWorld, SceneRayState State, string? ElementKey, string? SurfaceKey,
    string? MaterialKey, SceneVector? PointWorldMeters, SceneVector? NormalWorld,
    double? DistanceFromRayOriginMeters, double? DistanceFromCameraMeters, double? CameraDepthMeters,
    string DistanceSource = "ReferenceIntersector", string NormalSource = "Unavailable");
internal sealed record SceneScanStatistics(int CandidateCount, int ElementCount, int HitElementCount,
    int MaterialCount, int PlannedRayCount, int RayCount, int RayHits, int RayMisses, int RayFailures,
    int UnsupportedLinkHits, int SkippedLinkCandidates, int FailedReads, int GeometryReads,
    bool CandidateLimitReached, bool TimeBudgetReached, bool Cancelled,
    double CameraMilliseconds, double InventoryMilliseconds, double RayMilliseconds,
    double AggregationMilliseconds, double TotalMilliseconds);
internal sealed record SceneScanCapabilities(bool LinksSupported = false, bool AdaptiveSamplingSupported = false,
    bool PixelSegmentation = false, bool RasterAlignmentVerified = false);
internal sealed record SceneScanSnapshot(int SchemaVersion, DateTimeOffset CreatedAt,
    SceneDocumentIdentity Document, SceneViewSnapshot View, SceneCameraSnapshot Camera,
    string Units, string WorldSpace, string CameraSpace, string SampleSpace,
    SceneScanOptions Options, SceneScanCapabilities Capabilities, SceneReadState State,
    ImmutableArray<SceneElementSnapshot> Elements, ImmutableArray<SceneSurfaceSnapshot> Surfaces,
    ImmutableArray<SceneMaterialSnapshot> Materials, ImmutableArray<SceneRayHit> RayHits,
    SceneScanStatistics Statistics, ImmutableArray<string> Notes);

internal sealed record SceneScanOptions
{
    public const int HardMaxRayCount = 1024;
    public int RayGridWidth { get; init; } = 32;
    public int RayGridHeight { get; init; } = 18;
    public int MaxRayCount { get; init; } = HardMaxRayCount;
    public int MaxCandidateElements { get; init; } = 500;
    public int MaxMaterials { get; init; } = 128;
    public int MaxGeometryElements { get; init; } = 32;
    public int MaxGeometryObjectsPerElement { get; init; } = 256;
    public int MaxGeometryDepth { get; init; } = 4;
    public int TimeBudgetMilliseconds { get; init; } = 3000;
    public bool IncludeMaterials { get; init; } = true;
    public bool IncludeAppearanceAssets { get; init; } = true;
    public bool IncludeGeometrySummary { get; init; } = false;
    public bool IncludeSemanticParameters { get; init; } = false;
    public bool IncludeLinkedModels { get; init; } = false;
    public bool EnableAdaptiveSampling { get; init; } = false;

    public void Validate()
    {
        if (RayGridWidth < 1 || RayGridWidth > 64 || RayGridHeight < 1 || RayGridHeight > 64 ||
            MaxRayCount < 1 || MaxRayCount > HardMaxRayCount || (long)RayGridWidth * RayGridHeight > MaxRayCount ||
            MaxCandidateElements < 1 || MaxCandidateElements > 1000 || MaxMaterials < 1 || MaxMaterials > 256 ||
            MaxGeometryElements < 0 || MaxGeometryElements > 64 ||
            MaxGeometryObjectsPerElement < 1 || MaxGeometryObjectsPerElement > 512 ||
            MaxGeometryDepth < 0 || MaxGeometryDepth > 8 || TimeBudgetMilliseconds < 1 || TimeBudgetMilliseconds > 10000)
            throw new ArgumentOutOfRangeException(nameof(SceneScanOptions), "Scene Scan limits are outside the supported range.");
        if (IncludeLinkedModels || EnableAdaptiveSampling)
            throw new NotSupportedException("Linked models and adaptive sampling are reserved for a later phase.");
    }
}
