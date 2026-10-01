using Arqyzeng.Core;

namespace Arqyzeng.Revit;

internal enum FrameMode { Fit, Crop }
internal sealed record CaptureSettings(int Width, int Height,
    FrameMode Mode = FrameMode.Fit, double Horizontal = 0.5,
    double Vertical = 0.5, double Zoom = 1);
internal sealed record CaptureResult(string OriginalPath, string ViewName, BimSceneSnapshot Bim);
internal enum PrintAction { Capture, Guide, Prepare, Return, Panorama }
internal sealed record PrintRequest(PrintAction Action, CaptureSettings? Settings = null, int PanoramaFacePixels = 1024,
    int PanoramaSamplingMultiplier = 1);