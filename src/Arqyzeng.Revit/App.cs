using Autodesk.Revit.UI;

namespace Arqyzeng.Revit;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            const string tab = "Arqyzeng BIM";
            application.CreateRibbonTab(tab);
            RibbonPanel panel = application.CreateRibbonPanel(tab, "MVP");
            var buttonData = new PushButtonData(
                "Arqyzeng.StartupTest", "Testar\nplugin",
                typeof(App).Assembly.Location,
                typeof(StartupTestCommand).FullName!);
            buttonData.AvailabilityClassName = typeof(StartupTestAvailability).FullName!;
            var button = (PushButton)panel.AddItem(buttonData);
            button.ToolTip = "Verifica o carregamento do primeiro plugin Arqyzeng BIM.";
            var printData = new PushButtonData(
                "Arqyzeng.Print", "Abrir\nPrint",
                typeof(App).Assembly.Location, typeof(PrintCommand).FullName!);
            var printButton = (PushButton)panel.AddItem(printData);
            printButton.ToolTip = "Abre o painel Print para capturar a vista 3D ativa.";
            PluginLog.Write("Plugin iniciado.");
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha no startup.", exception);
            TaskDialog.Show("Arqyzeng BIM", "Nao foi possivel iniciar o plugin. Consulte o log local.");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        PrintHost.Shutdown();
        PluginLog.Write("Plugin encerrado.");
        return Result.Succeeded;
    }
}