using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Arqyzeng.SceneScan;

int assertions = 0;
void Check(bool valid, string name) { assertions++; if (!valid) throw new InvalidOperationException(name); }
void Near(double a, double b, string name) => Check(Math.Abs(a - b) < 1e-9, name + $": {a} vs {b}");
void Reject(Action action, string name)
{
    bool failed = false;
    try { action(); } catch (ArgumentException) { failed = true; } catch (NotSupportedException) { failed = true; }
    Check(failed, name);
}

var basis = SceneCameraMath.Basis(new(0, 3, 0), new(0, 1, 2));
Check(basis.Right == new SceneVector(1, 0, 0), "Right = +X");
Check(basis.Up == new SceneVector(0, 0, 1), "Up orthogonalized to +Z");
Near(SceneCameraMath.Dot(basis.Up, basis.Forward), 0, "Orthonormal basis");
Reject(() => SceneCameraMath.Basis(new(0, 1, 0), new(0, 2, 0)), "Parallel axes rejected");
Reject(() => SceneCameraMath.Normalize(new(double.NaN, 0, 1)), "NaN direction rejected");
var projection = new SceneProjection(2, 2, 1, 0, 0, 0, 100, "Synthetic");
var camera = new SceneCameraSnapshot(new(0, 0, 0), basis.Forward, basis.Up, basis.Right, true, projection,
    90, 90, null, null, ImmutableArray<SceneVector>.Empty, "Synthetic", "Synthetic");
var a = new SceneVector(-1, 2, 0);
var b = new SceneVector(1, 5, 1);
Check(SceneCameraMath.WorldToCamera(camera, a) == new SceneVector(-1, 0, 2), "A left, depth 2 m");
Check(SceneCameraMath.WorldToCamera(camera, b) == new SceneVector(1, 1, 5), "B right/up, depth 5 m");
Near(SceneCameraMath.Length(a), Math.Sqrt(5), "Euclidean distance distinguished from depth");
Near(SceneCameraMath.Project(camera, a)!.Value.X, .25, "A image X");
Near(SceneCameraMath.Project(camera, b)!.Value.X, .6, "B image X");
Near(SceneCameraMath.Project(camera, b)!.Value.Y, .4, "Top-left image convention");
Check(SceneCameraMath.Project(camera, new(0, -1, 0)) is null, "Behind camera has no projection");
var translated = camera with { OriginWorldMeters = new(100, 200, 300) };
Check(SceneCameraMath.WorldToCamera(translated, new(99, 202, 300)) == new SceneVector(-1, 0, 2), "Translation handled");
var rotatedBasis = SceneCameraMath.Basis(new(1, 0, 0), new(0, 0, 1));
var rotated = camera with { ForwardWorld = rotatedBasis.Forward, RightWorld = rotatedBasis.Right, UpWorld = rotatedBasis.Up };
Check(SceneCameraMath.WorldToCamera(rotated, new(5, 1, 0)) == new SceneVector(-1, 0, 5), "Rotated camera");
Near(SceneCameraMath.Fov(2, 1, 0), 90, "FOV in degrees");
Check(SceneCameraMath.Fov(2, 1, .5) < 90, "Asymmetric frustum FOV includes offsets");

var options = new SceneScanOptions();
var grid = SceneCameraMath.Grid(options);
Check(grid.Length == 576, "Default 32 x 18");
Check(grid.All(p => p.X > 0 && p.X < 1 && p.Y > 0 && p.Y < 1), "Cell centers exclude edges");
Check(grid.Distinct().Count() == grid.Length, "No duplicate samples");
Check(SceneCameraMath.Grid(options with { RayGridWidth = 32, RayGridHeight = 32 }).Length == 1024, "Hard limit allowed");
foreach (var invalid in new[] { options with { RayGridWidth = 0 }, options with { RayGridHeight = -1 },
    options with { RayGridWidth = int.MaxValue }, options with { RayGridWidth = 64, RayGridHeight = 64 },
    options with { MaxRayCount = 575 }, options with { MaxRayCount = 1025 }, options with { MaxCandidateElements = 1001 },
    options with { TimeBudgetMilliseconds = 10001 }, options with { MaxGeometryDepth = 9 },
    options with { IncludeLinkedModels = true }, options with { EnableAdaptiveSampling = true } })
    Reject(invalid.Validate, "Unsafe/unimplemented option rejected");

var shifted = camera with { Projection = projection with { RightOffsetMeters = .2, UpOffsetMeters = -.3, NearMeters = .1 } };
foreach (var c in new[] { camera, shifted, camera with { IsPerspective = false, Projection = projection with { TargetDistanceMeters = null, NearMeters = .3 } } })
foreach (var sample in grid)
{
    var ray = SceneCameraMath.Ray(c, sample);
    Near(SceneCameraMath.Length(ray.Direction), 1, "Unit ray direction");
    var point = SceneCameraMath.Add(ray.Origin, SceneCameraMath.Scale(ray.Direction, 3));
    var projected = SceneCameraMath.Project(c, point)!.Value;
    Near(projected.X, sample.X, "Ray/project X round trip");
    Near(projected.Y, sample.Y, "Ray/project Y round trip");
}
var ortho = camera with { IsPerspective = false, Projection = projection with { TargetDistanceMeters = null } };
Check(SceneCameraMath.Ray(ortho, new(.1, .1)).Direction == SceneCameraMath.Ray(ortho, new(.9, .9)).Direction, "Orthographic rays parallel");
Check(SceneCameraMath.Ray(ortho, new(.1, .1)).Origin != SceneCameraMath.Ray(ortho, new(.9, .9)).Origin, "Orthographic ray origins differ");
Near(SceneCameraMath.WorldToCamera(shifted, SceneCameraMath.Ray(shifted, new(.9, .9)).Origin).Z, .1, "Perspective origin on near plane");
Reject(() => SceneCameraMath.Ray(camera, new(double.NaN, .5)), "Invalid sample rejected");
Reject(() => SceneCameraMath.ValidateProjection(true, projection with { TargetDistanceMeters = 0 }), "Invalid target distance rejected");
Reject(() => SceneCameraMath.ValidateProjection(false, projection with { FarMeters = -1 }), "Invalid depth range rejected");

var doc = new SceneDocumentIdentity("document-a", "Synthetic");
string keyA = SceneCameraMath.ElementKey(doc.Key, "element-a");
Check(keyA == SceneCameraMath.ElementKey(doc.Key, "element-a"), "Identity stable between views/scans");
Check(keyA != SceneCameraMath.ElementKey("document-b", "element-a"), "Document identity participates");
Check(keyA != SceneCameraMath.ElementKey(doc.Key, "element-a", "link-1"), "Link instance identity reserved");
Check(SceneCameraMath.ElementKey("a:b", "c") != SceneCameraMath.ElementKey("a", "b:c"), "Identity delimiters cannot collide");
SceneRayHit Hit(int i, string key, double distance, double x, double y) => new(i, new(x, y), camera.OriginWorldMeters,
    camera.ForwardWorld, SceneRayState.Hit, key, null, null, new(0, distance, 0), null, distance, distance, distance);
var hits = ImmutableArray.Create(Hit(0, keyA, 2, .2, .3), Hit(1, keyA, 4, .4, .7), Hit(2, "B", 5, .8, .5),
    Hit(3, "ignored", 1, .5, .5) with { State = SceneRayState.Miss, ElementKey = null, PointWorldMeters = null, DistanceFromCameraMeters = null });
var coverage = SceneCameraMath.Aggregate(hits);
Check(coverage.Count == 2 && coverage[keyA].HitCount == 2, "Hits grouped by identity");
Near(coverage[keyA].SampleFraction, .5, "Misses included in coverage denominator");
Near(coverage[keyA].MinimumDistanceMeters, 2, "Minimum distance");
Near(coverage[keyA].MeanDistanceMeters, 3, "Mean distance");
Near(coverage[keyA].MaximumDistanceMeters, 4, "Maximum distance");
Check(coverage[keyA].MinimumSample == new ScenePoint2(.2, .3), "Sample bounding minimum");
Check(coverage[keyA].MaximumSample == new ScenePoint2(.4, .7), "Sample bounding maximum");
Near(coverage[keyA].MeanSample.Y, .5, "Predominant region centroid");
Check(SceneCameraMath.Aggregate([]).Count == 0, "Empty grid produces empty metrics");
Check(SceneCameraMath.Aggregate(hits.Select(h => h with { State = SceneRayState.Unavailable })).Count == 0, "Failures are never visible hits");
var metrics = SceneCameraMath.Metrics(hits.Add(hits[3] with { State = SceneRayState.Unavailable })
    .Add(hits[3] with { State = SceneRayState.UnsupportedLink }));
Check(metrics.Hits == 3 && metrics.Misses == 1 && metrics.Failures == 1 && metrics.UnsupportedLinks == 1 && metrics.HitElements == 2,
    "Miss/failure/unsupported metrics remain distinct");

var element = new SceneElementSnapshot(new(keyA, doc.Key, 12, "element-a"), "Generic Model", "Generic Models", -2000151,
    "FamilyInstance", "Unclassified family", 20, "Type A", "type-a", null, null, null, null, null, null,
    SceneVisibilityEvidence.ViewCollector | SceneVisibilityEvidence.RayHit, SceneReadState.Read, null, coverage[keyA], ImmutableArray<SceneSemanticParameter>.Empty);
var stats = new SceneScanStatistics(1, 2, 2, 0, 576, 4, 3, 1, 0, 0, 0, 0, 0, false, true, false, 1, 2, 3, 1, 7);
var snapshot = new SceneScanSnapshot(1, DateTimeOffset.UnixEpoch, doc, new(1, "view", "Synthetic view", "ThreeD", true, false, "Shading"),
    camera, "m", "host", "right/up/forward", "top-left [0,1]", options, new(), SceneReadState.Partial,
    ImmutableArray.Create(element, element with { Identity = new("B", doc.Key, 13, "element-b"), Coverage = coverage["B"] }), ImmutableArray<SceneSurfaceSnapshot>.Empty, ImmutableArray<SceneMaterialSnapshot>.Empty,
    hits, stats, ImmutableArray.Create("Synthetic fixture; not a Revit validation."));
string json = SceneScanSerializer.Serialize(snapshot);
var copy = SceneScanSerializer.Deserialize(json);
Check(copy.SchemaVersion == 1 && copy.Elements[0].Identity.Key == keyA, "JSON identity/schema round trip");
Check(copy.Elements[0].Category == "Generic Models", "No invented semantics");
Check(copy.RayHits[3].State == SceneRayState.Miss && copy.RayHits[3].PointWorldMeters is null, "Null miss round trip");
Check(copy.Statistics.RayCount == 4 && copy.Statistics.TotalMilliseconds == 7, "Metrics serialized");
Check(!copy.Capabilities.LinksSupported && !copy.Capabilities.RasterAlignmentVerified, "Capabilities explicit");
var bim = new Arqyzeng.Core.BimSceneSnapshot(Arqyzeng.Core.BimReadState.Unavailable, 0, 0, 0, 0, 0, [], [], []);
var capture = new Arqyzeng.Revit.CaptureResult("unchanged.png", "View", bim);
var (originalPath, viewName, originalBim) = capture;
Check(capture.SceneScan is null && originalPath == "unchanged.png" && viewName == "View" && ReferenceEquals(bim, originalBim), "Capture constructor/deconstruction compatibility");
var enriched = capture with { SceneScan = snapshot };
Check(ReferenceEquals(enriched.Bim, bim) && enriched.OriginalPath == capture.OriginalPath, "Scan enrichment does not replace existing BIM/image");
Check(json.Contains('\n') && !json.Contains("OpenAI", StringComparison.OrdinalIgnoreCase), "Readable diagnostic, no provider data");
string? previous = Environment.GetEnvironmentVariable("ARQYZENG_SCENE_SCAN_DEBUG");
try
{
    Environment.SetEnvironmentVariable("ARQYZENG_SCENE_SCAN_DEBUG", null);
    Check(SceneScanSerializer.WriteDiagnostic(snapshot) is null, "No diagnostic without opt-in");
    Environment.SetEnvironmentVariable("ARQYZENG_SCENE_SCAN_DEBUG", "true");
    Check(SceneScanSerializer.WriteDiagnostic(snapshot) is null, "Only explicit value 1 enables diagnostic");
}
finally { Environment.SetEnvironmentVariable("ARQYZENG_SCENE_SCAN_DEBUG", previous); }
var output = Path.Combine(".artifacts", "scene-scan-checks", "synthetic-scene-scan.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, json);

// Compile-time independence is reinforced by a recursive DTO type audit.
var seen = new HashSet<Type>();
void Audit(Type type)
{
    if (!seen.Add(type)) return;
    Check(!(type.Namespace?.StartsWith("Autodesk", StringComparison.Ordinal) ?? false), "No Autodesk type in DTO graph");
    Check(type != typeof(object) && !typeof(Delegate).IsAssignableFrom(type), "No opaque objects/delegates in DTO graph");
    if (type.IsGenericType) foreach (var arg in type.GetGenericArguments()) Audit(arg);
    if (type.Namespace == "Arqyzeng.SceneScan")
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) Audit(property.PropertyType);
}
Audit(typeof(SceneScanSnapshot));
Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a => a.Name?.StartsWith("Revit") == true), "Checks run without Revit assemblies");
Console.WriteLine($"Scene Scan: {assertions} assertions passed — camera, perspective/orthographic rays, offsets, limits, identity, coverage, JSON and pure DTO boundary.");
