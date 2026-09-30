namespace Arqyzeng.Core;

internal enum RenderProfile { BumpDay, BumpNight }
internal enum FidelityMode { Creative, Controlled, Maximum }
internal sealed record RenderSettings(RenderProfile Profile, FidelityMode Fidelity);
internal sealed record MaterialConfirmation(string MaterialKey, string Name, string MaterialClass, string[] Categories, string Description);
internal sealed record RenderScene(string ViewName, int Width, int Height, BimSceneSnapshot? Bim = null, MaterialConfirmation[]? MaterialConfirmations = null);

internal enum BimReadState { Read, Partial, Unavailable }
internal sealed record BimCategory(string Name, int ElementCount);
internal sealed record BimMaterial(string Name, string MaterialClass, string ColorRgb,
    int Transparency, bool UseRenderAppearanceForShading, int ElementCount,
    bool HasPaintAssignment, BimCategory[] UsesByCategory);
internal sealed record BimSceneSnapshot(BimReadState State, int ScannedElements,
    int ElementsWithoutMaterialIds, int FailedReads, int SkippedLinks, int SkippedImports,
    BimCategory[] Categories, BimMaterial[] Materials, string[] Notes);