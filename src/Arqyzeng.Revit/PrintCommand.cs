using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;

namespace Arqyzeng.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class PrintCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            if (commandData.Application.ActiveUIDocument is null)
            {
                TaskDialog.Show("Arqyzeng BIM", "Abra um projeto e uma vista 3D para usar Print.");
                return Result.Cancelled;
            }
            if (commandData.Application.ActiveUIDocument.Document.ActiveView is not View3D view || view.IsTemplate)
            {
                TaskDialog.Show("Arqyzeng BIM", "Print captura vistas 3D nesta etapa. Abra a vista {3D} ou uma camera e clique em Abrir Print novamente.");
                return Result.Cancelled;
            }
            PrintHost.Show(commandData.Application);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha ao abrir Print.", exception);
            message = "Nao foi possivel abrir Print: " + exception.Message;
            return Result.Failed;
        }
    }
}

internal static class PrintHost
{
    private static PrintWindow? _window;
    private static PrintCaptureHandler? _handler;
    private static ExternalEvent? _event;

    internal static void Show(UIApplication application)
    {
        if (_window is not null) { _handler?.StopGuide(true); _window.Show(); _window.Activate(); return; }
        var handler = _handler ??= new PrintCaptureHandler();
        var externalEvent = _event ??= ExternalEvent.Create(handler);
        handler.ExpectedDocument = application.ActiveUIDocument.Document;
        var window = new PrintWindow();
        _window = window;
        handler.Window = window;
        new WindowInteropHelper(window).Owner = application.MainWindowHandle;
        bool QueueRequest(PrintRequest request)
        {
            try
            {
                handler.Pending = request;
                ExternalEventRequest status = externalEvent.Raise();
                if (status == ExternalEventRequest.Accepted || status == ExternalEventRequest.Pending) return true;
                handler.Pending = null;
                if (request.Action == PrintAction.Panorama) window.ReceivePanoramaFailure("O Revit esta ocupado. Termine o comando ativo e tente novamente.");
                else window.ReceiveFailure("O Revit esta ocupado. Termine o comando ativo e tente novamente.");
                return false;
            }
            catch (Exception exception)
            {
                handler.Pending = null;
                if (request.Action == PrintAction.Panorama) window.ReceivePanoramaFailure(exception.Message);
                else window.ReceiveFailure(exception.Message);
                PluginLog.Write("Falha ao solicitar captura ou moldura.", exception);
                return false;
            }
        }
        window.CaptureRequested += settings => QueueRequest(new PrintRequest(PrintAction.Capture, settings));
        window.GuideRequested += settings => QueueRequest(new PrintRequest(PrintAction.Guide, settings));
        window.PanoramaRequested += (pixels, sampling) => QueueRequest(new PrintRequest(PrintAction.Panorama,
            PanoramaFacePixels: pixels, PanoramaSamplingMultiplier: sampling));
        window.PrepareRequested += () => QueueRequest(new PrintRequest(PrintAction.Prepare));
        handler.GuideActionRequested = QueueRequest;
        window.Closed += (_, _) =>
        {
            if (handler.HasBackgroundWork) QueueRequest(new PrintRequest(PrintAction.Return));
            else handler.Pending = null;
            _window = null;
            handler.Window = null;
            handler.GuideActionRequested = null;
            handler.ExpectedDocument = null;
        };
        window.Show();
    }

    internal static void Shutdown()
    {
        _handler?.StopGuide(false);
        _handler?.StopPreparation();
        _window?.Close();
        if (_handler is not null) _handler.GuideActionRequested = null;
        _event?.Dispose();
        _event = null;
        _handler = null;
    }
}