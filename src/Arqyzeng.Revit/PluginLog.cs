using System.IO;

namespace Arqyzeng.Revit;

internal static class PluginLog
{
    internal static void Write(string text, Exception? exception = null)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArqyzengBIM", "logs");
            Directory.CreateDirectory(directory);
            // Somente mensagens tecnicas: sem dados do projeto, chaves ou tokens.
            string detail = exception is null ? string.Empty : " | " + exception.GetType().FullName;
            File.AppendAllText(Path.Combine(directory, "bootstrap.log"),
                DateTimeOffset.Now.ToString("O") + " | " + text + detail + Environment.NewLine);
        }
        catch
        {
            // Falhas no log nao devem impedir o carregamento do plugin.
        }
    }
}