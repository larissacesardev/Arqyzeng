using System.Collections.Immutable;
using Arqyzeng.SceneScan;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

namespace Arqyzeng.Revit;

// Instance and caches live for one synchronous scan only. Only immutable DTOs leave this reader.
internal sealed class SceneScanMaterialReader(Document document, string documentKey, SceneScanOptions options)
{
    private readonly Dictionary<long, SceneMaterialSnapshot> _materials = new();
    private readonly HashSet<long> _attempted = new();
    internal int FailedReads { get; private set; }
    internal bool Limited { get; private set; }
    internal ImmutableArray<SceneMaterialSnapshot> Materials => _materials.Values.OrderBy(m => m.ElementId).ToImmutableArray();

    internal string? Read(ElementId id)
    {
        if (!options.IncludeMaterials || id == ElementId.InvalidElementId) return null;
        if (_materials.TryGetValue(id.Value, out var cached)) return cached.Key;
        if (!_attempted.Add(id.Value)) return null;
        if (_materials.Count >= options.MaxMaterials) { Limited = true; return null; }
        try
        {
            if (document.GetElement(id) is not Material material) { FailedReads++; return null; }
            string key = SceneCameraMath.ElementKey(documentKey, material.UniqueId);
            string? rgb = null, name = null, materialClass = null;
            int? transparency = null; bool? renderShading = null;
            bool failed = false;
            try { name = material.Name; materialClass = material.MaterialClass; }
            catch (Exception) { failed = true; }
            try
            {
                using var color = material.Color;
                if (color.IsValid) rgb = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
                transparency = material.Transparency;
                renderShading = material.UseRenderAppearanceForShading;
            }
            catch (Exception) { failed = true; }
            var appearance = Appearance(material);
            failed |= appearance.State == SceneReadState.Partial;
            if (failed) FailedReads++;
            _materials[id.Value] = new(key, documentKey, id.Value, material.UniqueId, name, materialClass,
                rgb, transparency, renderShading, appearance, failed ? SceneReadState.Partial : SceneReadState.Read);
            return key;
        }
        catch (Exception) { FailedReads++; return null; }
    }

    private SceneAppearanceSnapshot Appearance(Material material)
    {
        var empty = new SceneAppearanceSnapshot(SceneReadState.NotRequested, null, null, ImmutableArray<double>.Empty,
            null, null, null, "NotResolved_NoTextureFilesRead", "AppearanceAssetElement.GetRenderingAsset");
        if (!options.IncludeAppearanceAssets) return empty;
        long? id = null; string? name = null;
        try
        {
            var assetId = material.AppearanceAssetId;
            if (assetId == ElementId.InvalidElementId) return empty with { State = SceneReadState.Unavailable };
            id = assetId.Value;
            if (document.GetElement(assetId) is not AppearanceAssetElement element)
                return empty with { State = SceneReadState.Unavailable, AssetId = id };
            name = element.Name;
            using var asset = element.GetRenderingAsset();
            if (asset.Size == 0) return empty with { State = SceneReadState.Unavailable, AssetId = id, AssetName = name };
            // Only documented public Generic/UnifiedBitmap property names; other schemas remain unknown.
            var diffuse = asset.FindByName(Generic.GenericDiffuse);
            var channels = diffuse is AssetPropertyDoubleArray4d rgba ? rgba.GetValueAsDoubles().ToArray() : [];
            var color = channels.Length == 4 && channels.All(double.IsFinite)
                ? channels.ToImmutableArray() : ImmutableArray<double>.Empty;
            double? Value(string property) => asset.FindByName(property) is AssetPropertyDouble p && double.IsFinite(p.Value) ? p.Value : null;
            bool? texture = null;
            if (diffuse is not null)
            {
                if (diffuse.NumberOfConnectedProperties == 0) texture = false;
                else if (diffuse.NumberOfConnectedProperties == 1)
                {
                    using var connected = diffuse.GetSingleConnectedAsset();
                    if (connected?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) is AssetPropertyString bitmap)
                        texture = !string.IsNullOrWhiteSpace(bitmap.Value);
                }
            }
            return new(SceneReadState.Read, id, name, color, Value(Generic.GenericTransparency), Value(Generic.GenericGlossiness),
                texture, "NotResolved_NoTextureFilesRead", "PublicGenericProperties_WhenPresent");
        }
        catch (Exception) { return empty with { State = SceneReadState.Partial, AssetId = id, AssetName = name }; }
    }
}
