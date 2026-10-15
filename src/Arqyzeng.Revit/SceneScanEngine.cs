using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using Arqyzeng.SceneScan;
using Autodesk.Revit.DB;

namespace Arqyzeng.Revit;

// Synchronous API boundary. Call ONLY from an IExternalCommand/ExternalEvent/Idling callback.
// STA is a fail-fast worker-thread guard, not a substitute for Revit's API context requirement.
internal static class SceneScanEngine
{
    private const double SurfaceValidationToleranceMeters = .001;

    internal static SceneScanSnapshot Scan(Document document, View3D view, SceneScanOptions? configuration = null,
        ImmutableArray<SceneVector> viewportCorners = default, CancellationToken cancellation = default)
    {
        if (Thread.CurrentThread.IsThreadPoolThread || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Scene Scan must run synchronously in the Revit API context.");
        var options = configuration ?? new SceneScanOptions();
        options.Validate();
        if (!document.IsValidObject || !view.IsValidObject || !view.Document.Equals(document) || view.IsTemplate ||
            !FilteredElementCollector.IsViewValidForElementIteration(document, view.Id))
            throw new ArgumentException("A valid 3D view of the supplied document is required.");
        var total = Stopwatch.StartNew();
        bool Stop() => cancellation.IsCancellationRequested || total.ElapsedMilliseconds >= options.TimeBudgetMilliseconds;
        var notes = new List<string>
        {
            "Candidate != visible. RayHit means first geometric host-model intersection, not pixel segmentation or photometric visibility.",
            "LinksSupported=false: linked geometry may occlude host hits; linked elements/materials/transforms are not scanned.",
            "Camera samples use the exported view projection. Viewport zoom/pan, output Fit/Crop and raster alignment are not calibrated.",
            "Orthographic distances are relative to View3D orientation origin, not a physical eye position.",
            "Model semantics are copied verbatim; generic categories/names do not establish real-world object identity.",
            "Graphics/face materials do not include view overrides, section-cut graphics or guaranteed rendered texture appearance."
        };
        string documentKey = document.CreationGUID.ToString("D");
        var documentIdentity = new SceneDocumentIdentity(documentKey, "Document.CreationGUID_NoPath");
        var viewData = new SceneViewSnapshot(view.Id.Value, view.UniqueId, view.Name, view.ViewType.ToString(),
            view.CropBoxActive, view.IsSectionBoxActive, view.DisplayStyle.ToString());
        var stage = Stopwatch.StartNew();
        int baseNoteCount = notes.Count;
        var camera = SceneScanCameraReader.Read(document, view, viewportCorners, Stop, notes);
        double cameraMs = stage.Elapsed.TotalMilliseconds;
        stage.Restart();
        var elements = new Dictionary<string, SceneElementSnapshot>(StringComparer.Ordinal);
        var surfaces = new Dictionary<string, SceneSurfaceSnapshot>(StringComparer.Ordinal);
        var materials = new SceneScanMaterialReader(document, documentKey, options);
        int candidates = 0, skippedLinks = 0, failures = notes.Count - baseNoteCount, geometryReads = 0;
        bool candidateLimit = false;
        using (var collector = new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType())
        {
            foreach (Element element in collector)
            {
                if (Stop()) break;
                if (candidates >= options.MaxCandidateElements) { candidateLimit = true; break; }
                try
                {
                    if (element is RevitLinkInstance) { skippedLinks++; continue; }
                    if (element.IsTransient || element.Category?.CategoryType != CategoryType.Model || element.IsHidden(view)) continue;
                    candidates++;
                    var data = SceneScanElementReader.Read(element, view, documentKey, camera, SceneVisibilityEvidence.ViewCollector, options.IncludeSemanticParameters);
                    elements[data.Identity.Key] = data;
                    if (data.State == SceneReadState.Partial) failures++;
                }
                catch (Exception) { failures++; }
            }
        }
        double inventoryMs = stage.Elapsed.TotalMilliseconds;
        stage.Restart();
        var grid = SceneCameraMath.Grid(options);
        var rays = ImmutableArray.CreateBuilder<SceneRayHit>();
        // Do not restrict targets to capped collector results: an omitted object may be the nearest occluder.
        if (camera.Projection is not null && !Stop())
        {
            try
            {
                using var intersector = new ReferenceIntersector(view)
                { TargetType = FindReferenceTarget.Face, FindReferencesInRevitLinks = false };
                for (int index = 0; index < grid.Length && !Stop(); index++)
                {
                    var sample = grid[index];
                    var ray = SceneCameraMath.Ray(camera, sample);
                    SceneRayHit Empty(SceneRayState state) => new(index, sample, ray.Origin, ray.Direction, state,
                        null, null, null, null, null, null, null, null);
                    try
                    {
                        using var hit = intersector.FindNearest(SceneScanUnits.Point(ray.Origin), SceneScanUnits.Direction(ray.Direction));
                        if (hit is null) { rays.Add(Empty(SceneRayState.Miss)); continue; }
                        using var reference = hit.GetReference();
                        if (reference.LinkedElementId != ElementId.InvalidElementId)
                        { rays.Add(Empty(SceneRayState.UnsupportedLink)); continue; }
                        double proximity = SceneScanUnits.Meters(hit.Proximity);
                        if (!double.IsFinite(proximity) || proximity < 0) { rays.Add(Empty(SceneRayState.Unavailable)); continue; }
                        // Ray equation is authoritative; GlobalPoint may be unavailable on some references.
                        var point = SceneCameraMath.Add(ray.Origin, SceneCameraMath.Scale(ray.Direction, proximity));
                        if (!SceneCameraMath.Finite(point)) { rays.Add(Empty(SceneRayState.Unavailable)); continue; }
                        var local = SceneCameraMath.WorldToCamera(camera, point);
                        if (camera.Projection.FarMeters is double far && local.Z > far)
                        { rays.Add(Empty(SceneRayState.Miss)); continue; }
                        Element? element = document.GetElement(reference.ElementId);
                        if (element is null) { rays.Add(Empty(SceneRayState.Unavailable)); continue; }
                        string key = SceneCameraMath.ElementKey(documentKey, element.UniqueId);
                        if (!elements.TryGetValue(key, out var data))
                        {
                            data = SceneScanElementReader.Read(element, view, documentKey, camera, SceneVisibilityEvidence.RayHit, options.IncludeSemanticParameters);
                            if (data.State == SceneReadState.Partial) failures++;
                        }
                        data = data with { VisibilityEvidence = data.VisibilityEvidence | SceneVisibilityEvidence.RayHit };
                        if (options.IncludeGeometrySummary && data.Geometry is null && geometryReads < options.MaxGeometryElements && !Stop())
                        {
                            geometryReads++;
                            data = data with { Geometry = SceneScanElementReader.Geometry(element, view, options, Stop) };
                            if (data.Geometry.State != SceneReadState.Read) failures++;
                        }
                        elements[key] = data;
                        string? surfaceKey = null, materialKey = null;
                        SceneVector? normal = null;
                        try
                        {
                            using var geometry = element.GetGeometryObjectFromReference(reference);
                            if (geometry is Face face && !Stop())
                            {
                                using var transform = hit.GetInstanceTransform();
                                // Some API references expose symbol-local faces. Validate the transformed UV point
                                // before publishing a normal; ambiguous references retain a null normal.
                                var uv = reference.UVPoint;
                                if (uv is not null && transform.IsConformal)
                                {
                                    var evaluated = SceneScanUnits.Point(transform.OfPoint(face.Evaluate(uv)));
                                    if (SceneCameraMath.Length(SceneCameraMath.Subtract(evaluated, point)) <= SurfaceValidationToleranceMeters)
                                        normal = SceneCameraMath.Normalize(SceneScanUnits.Direction(transform.OfVector(face.ComputeNormal(uv))));
                                }
                                surfaceKey = SceneCameraMath.ElementKey(key, reference.ConvertToStableRepresentation(document));
                                if (!surfaces.TryGetValue(surfaceKey, out var surface))
                                {
                                    var materialId = face.MaterialElementId;
                                    ElementId painted = ElementId.InvalidElementId;
                                    bool paintFailed = false;
                                    if (options.IncludeMaterials)
                                    {
                                        try { painted = document.GetPaintedMaterial(element.Id, face); }
                                        catch (Exception) { paintFailed = true; }
                                    }
                                    var effective = painted != ElementId.InvalidElementId ? painted : materialId;
                                    materialKey = materials.Read(effective);
                                    double? area = transform.IsConformal
                                        ? UnitUtils.ConvertFromInternalUnits(face.Area * transform.Scale * transform.Scale, UnitTypeId.SquareMeters) : null;
                                    if (area is double areaValue && (!double.IsFinite(areaValue) || areaValue < 0)) area = null;
                                    surface = new(surfaceKey, key, face.GetType().Name, area,
                                        materialId != ElementId.InvalidElementId ? materialId.Value : null,
                                        painted != ElementId.InvalidElementId ? painted.Value : null, materialKey,
                                        !options.IncludeMaterials ? "NotRequested" : painted != ElementId.InvalidElementId ? "FacePaint" : paintFailed ? "FaceMaterial_PaintUnavailable" : "Face.MaterialElementId",
                                        paintFailed || (options.IncludeMaterials && materialKey is null) ? SceneReadState.Partial : SceneReadState.Read);
                                    surfaces[surfaceKey] = surface;
                                    if (paintFailed) failures++;
                                }
                                materialKey = surface.MaterialKey;
                            }
                        }
                        catch (Exception)
                        {
                            failures++;
                            if (surfaceKey is not null && !surfaces.ContainsKey(surfaceKey)) surfaceKey = null;
                        } // Keep valid depth/identity even if surface enrichment fails.
                        rays.Add(new(index, sample, ray.Origin, ray.Direction, SceneRayState.Hit, key, surfaceKey,
                            materialKey, point, normal, proximity, SceneCameraMath.Length(SceneCameraMath.Subtract(point, camera.OriginWorldMeters)), local.Z,
                            NormalSource: normal is null ? "Unavailable" : "Face.ComputeNormal_ValidatedInstanceTransform"));
                    }
                    catch (Exception) { failures++; rays.Add(Empty(SceneRayState.Unavailable)); }
                }
            }
            catch (Exception) { failures++; notes.Add("ReferenceIntersectorUnavailableOrInterrupted"); }
        }
        double rayMs = stage.Elapsed.TotalMilliseconds;
        stage.Restart();
        var samples = rays.ToImmutable();
        var coverage = SceneCameraMath.Aggregate(samples);
        var elementData = elements.Values.Select(e => coverage.TryGetValue(e.Identity.Key, out var c) ? e with { Coverage = c } : e)
            .OrderBy(e => e.Identity.Key, StringComparer.Ordinal).ToImmutableArray();
        var materialData = materials.Materials;
        failures += materials.FailedReads;
        bool timeLimited = total.ElapsedMilliseconds >= options.TimeBudgetMilliseconds;
        if (candidateLimit) notes.Add("CandidateLimitReached: candidate list incomplete; ray targets were not limited to this list.");
        if (materials.Limited) notes.Add("MaterialLimitReached");
        if (timeLimited) notes.Add("TimeBudgetReached: cooperative checks cannot interrupt an individual Revit API call.");
        if (cancellation.IsCancellationRequested) notes.Add("CancelledBetweenApiCalls");
        if (camera.Projection is null) notes.Add("ProjectionUnavailable_NoRaySamples");
        if (options.IncludeGeometrySummary && elements.Values.Count(e => e.VisibilityEvidence.HasFlag(SceneVisibilityEvidence.RayHit)) > geometryReads)
            notes.Add("GeometrySummariesPartial: only bounded hit elements were traversed.");
        var metrics = SceneCameraMath.Metrics(samples);
        var statistics = new SceneScanStatistics(candidates, elementData.Length, metrics.HitElements, materialData.Length,
            grid.Length, samples.Length, metrics.Hits, metrics.Misses, metrics.Failures,
            metrics.UnsupportedLinks, skippedLinks, failures, geometryReads,
            candidateLimit, timeLimited, cancellation.IsCancellationRequested, cameraMs, inventoryMs, rayMs,
            stage.Elapsed.TotalMilliseconds, total.Elapsed.TotalMilliseconds);
        bool partial = camera.Projection is null || candidateLimit || timeLimited || cancellation.IsCancellationRequested ||
            failures > 0 || materials.Limited || samples.Length < grid.Length || statistics.RayFailures > 0 || statistics.UnsupportedLinkHits > 0;
        return new(1, DateTimeOffset.UtcNow, documentIdentity, viewData, camera,
            "m; m²; degrees; unitless directions", "Revit host internal origin, converted to meters",
            "X=right; Y=up; Z=forward; relative to orientation eye", "[0,1]; origin top-left; X right; Y down; cell centers",
            options, new SceneScanCapabilities(), partial ? SceneReadState.Partial : SceneReadState.Read,
            elementData, surfaces.Values.OrderBy(s => s.Key, StringComparer.Ordinal).ToImmutableArray(), materialData,
            samples, statistics, notes.ToImmutableArray());
    }
}
