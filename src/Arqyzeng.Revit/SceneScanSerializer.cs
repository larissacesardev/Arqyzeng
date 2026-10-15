using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arqyzeng.SceneScan;

internal static class SceneScanSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static string Serialize(SceneScanSnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);
    internal static SceneScanSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<SceneScanSnapshot>(json, JsonOptions) ?? throw new InvalidDataException("Empty snapshot.");

    // No filesystem operation at all unless explicitly opted in. Called with pure DTOs only.
    internal static string? WriteDiagnostic(SceneScanSnapshot snapshot)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ARQYZENG_SCENE_SCAN_DEBUG"), "1", StringComparison.Ordinal)) return null;
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArqyzengBIM", "Diagnostics", "SceneScan");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "scene-scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        string pending = path + ".pending";
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, snapshot, JsonOptions);
            File.Move(pending, path);
            return path;
        }
        finally
        {
            try { if (File.Exists(pending)) File.Delete(pending); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
