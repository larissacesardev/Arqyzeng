using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arqyzeng.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class StartupTestCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            TaskDialog.Show("Arqyzeng BIM",
                "O plugin carregou e o comando foi executado com sucesso.\n\n" +
                "Revit: " + commandData.Application.Application.VersionNumber + "\n" +
                "Primeira etapa: Ribbon e integracao nativa.\n" +
                "Proxima etapa: interface Print e captura da vista 3D.");
            PluginLog.Write("Comando de teste executado.");
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha no comando de teste.", exception);
            message = "Nao foi possivel executar o teste. Consulte o log local.";
            return Result.Failed;
        }
    }
}

public sealed class StartupTestAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}