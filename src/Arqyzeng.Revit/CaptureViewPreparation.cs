using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Arqyzeng.Revit;

// As alteracoes ficam exclusivamente na nova vista; nao modifica a vista-base nem materiais.
internal sealed class CaptureViewPreparation
{
    private readonly UIApplication _application;
    private readonly PrintWindow _window;
    private readonly View3D _source;
    private readonly Action<string?, string?> _completed;
    private View3D? _copy;
    private IList<XYZ>? _zoomCorners;
    private DateTime _deadline;
    private bool _subscribed;
    private bool _stopped;

    internal CaptureViewPreparation(UIApplication application, PrintWindow window, View3D source,
        Action<string?, string?> completed)
    {
        _application = application; _window = window; _source = source; _completed = completed;
    }

    internal void Start()
    {
        Document document = _source.Document;
        UIDocument? uiDocument = _application.ActiveUIDocument;
        if (uiDocument is null || !uiDocument.Document.Equals(document))
            throw new InvalidOperationException("Abra a vista 3D no projeto ativo antes de preparar a captura.");
        if (document.IsFamilyDocument || document.IsReadOnly || document.IsModifiable)
            throw new InvalidOperationException("Use um projeto RVT editavel e termine o comando ativo antes de preparar a vista.");
        if (!_source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
            throw new InvalidOperationException("Esta vista nao pode ser duplicada. Abra uma vista 3D que permita duplicacao.");
        if (!_source.IsPerspective)
        {
            var sourceWindow = uiDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId.Equals(_source.Id));
            _zoomCorners = sourceWindow?.GetZoomCorners();
        }
        string name = UniqueName(document);
        using var orientation = _source.GetOrientation();
        using (var transaction = new Transaction(document, "Arqyzeng BIM - preparar vista de captura"))
        {
            if (transaction.Start() != TransactionStatus.Started)
                throw new InvalidOperationException("Nao foi possivel iniciar a preparacao da vista.");
            _copy = document.GetElement(_source.Duplicate(ViewDuplicateOption.Duplicate)) as View3D
                ?? throw new InvalidOperationException("A duplicacao nao retornou uma vista 3D.");
            _copy.Name = name;
            _copy.ViewTemplateId = ElementId.InvalidElementId;
            _copy.Unlock();
            _copy.SetOrientation(orientation);
            _copy.SaveOrientation();
            if (!_copy.CanModifyDisplayStyle())
                throw new InvalidOperationException("Nao foi possivel configurar o estilo visual da nova vista.");
            _copy.DisplayStyle = DisplayStyle.ShadingWithEdges;
            if (_copy.CanModifyDetailLevel()) _copy.DetailLevel = ViewDetailLevel.Fine;
            _copy.AreAnnotationCategoriesHidden = true;
            _copy.CropBoxVisible = false;
            foreach (BuiltInCategory item in new[]
                { BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids,
                  BuiltInCategory.OST_SectionBox })
            {
                Category? category = Category.GetCategory(document, item);
                if (category is not null && _copy.CanCategoryBeHidden(category.Id))
                    _copy.SetCategoryHidden(category.Id, true);
            }
            var white = new Color(255, 255, 255);
            using var background = ViewDisplayBackground.CreateGradient(white, white, white);
            _copy.SetBackground(background);
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("A preparacao nao foi confirmada pelo Revit.");
        }
        // A troca de vista so e solicitada depois de encerrar a transacao.
        _deadline = DateTime.UtcNow.AddSeconds(15);
        _application.Idling += OnIdling;
        _subscribed = true;
        try { uiDocument.RequestViewChange(_copy); }
        catch
        {
            Stop();
            throw new InvalidOperationException($"A vista {name} foi criada, mas nao foi aberta automaticamente. Abra-a no Navegador de projeto.");
        }
    }

    private static string UniqueName(Document document)
    {
        var names = new HashSet<string>(new FilteredElementCollector(document).OfClass(typeof(View))
            .Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        for (int number = 1; ; number++)
        {
            string name = $"ARQYZENG_Print_{number:000}";
            if (!names.Contains(name)) return name;
        }
    }

    private void OnIdling(object? sender, IdlingEventArgs args)
    {
        if (_stopped) return;
        try
        {
            if (!_window.IsLoaded) { Stop(); _completed(null, null); return; }
            UIDocument? uiDocument = _application.ActiveUIDocument;
            var document = uiDocument?.Document;
            if (uiDocument is null || document is null || !document.IsValidObject || _copy is null || !_copy.IsValidObject ||
                !document.Equals(_copy.Document))
            { Complete(null, "O projeto ativo mudou. Abra Print no projeto desejado."); return; }
            if (!document.ActiveView.Id.Equals(_copy.Id))
            {
                if (DateTime.UtcNow > _deadline)
                    Complete(null, $"A vista {_copy.Name} foi criada. Abra-a no Navegador de projeto antes de capturar.");
                return;
            }
            var uiView = uiDocument.GetOpenUIViews().FirstOrDefault(v => v.ViewId.Equals(_copy.Id));
            if (uiView is null)
            {
                if (DateTime.UtcNow > _deadline) Complete(_copy.Name, "Confira o enquadramento na nova vista antes de capturar.");
                return;
            }
            string? note = null;
            if (_zoomCorners is { Count: 2 })
            {
                try { uiView.ZoomAndCenterRectangle(_zoomCorners[0], _zoomCorners[1]); }
                catch (Exception exception)
                {
                    PluginLog.Write("Nao foi possivel restaurar o zoom da vista-base.", exception);
                    note = "Ajuste o zoom na nova vista antes de capturar.";
                }
            }
            Complete(_copy.Name, note);
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha ao abrir a vista preparada.", exception);
            Complete(null, "Nao foi possivel concluir a abertura da vista preparada. Confira o Navegador de projeto.");
        }
    }

    private void Complete(string? name, string? note)
    {
        Stop();
        _completed(name, note);
    }

    // Chamado apenas de ExternalEvent, Idling ou OnShutdown.
    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        if (_subscribed) { _application.Idling -= OnIdling; _subscribed = false; }
    }
}