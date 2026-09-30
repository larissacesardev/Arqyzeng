using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Arqyzeng.Core;
using Autodesk.Revit.DB;

namespace Arqyzeng.Revit;

// Consulta somente leitura, executada no ExternalEvent da captura.
// O collector fornece candidatos da vista, nao correspondencia por pixel ou face visivel.
internal static class BimSceneReader
{
    private const int MaxElements = 2000;
    private const int MaxMaterials = 200;
    private sealed class MaterialUsage
    {
        internal required BimMaterial Data;
        internal int Count;
        internal bool Paint;
        internal Dictionary<string, int> Categories = new(StringComparer.OrdinalIgnoreCase);
    }

    internal static BimSceneSnapshot Read(Document document, View3D view)
    {
        var timer = Stopwatch.StartNew();
        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var materials = new Dictionary<ElementId, MaterialUsage>();
        var notes = new List<string>();
        int scanned = 0, noIds = 0, failures = 0, links = 0, imports = 0, omitted = 0;
        bool limited = false;
        using var collector = new FilteredElementCollector(document, view.Id);
        collector.WhereElementIsNotElementType();
        foreach (Element element in collector)
        {
            if (scanned >= MaxElements || timer.Elapsed > TimeSpan.FromSeconds(5))
            { limited = true; break; }
            try
            {
                Category? category = element.Category;
                if (category is null || category.CategoryType != CategoryType.Model) continue;
                scanned++;
                string categoryName = category.Name;
                Increment(categories, categoryName);
                if (element is RevitLinkInstance) { links++; continue; }
                if (element is ImportInstance) { imports++; continue; }
                var ids = new HashSet<ElementId>();
                var paintIds = new HashSet<ElementId>();
                try { ids.UnionWith(element.GetMaterialIds(false)); }
                catch (Exception) { failures++; }
                try { paintIds.UnionWith(element.GetMaterialIds(true)); ids.UnionWith(paintIds); }
                catch (Exception) { failures++; }
                if (ids.Count == 0) noIds++;
                foreach (ElementId id in ids)
                {
                    if (id.Equals(ElementId.InvalidElementId)) continue;
                    try
                    {
                        if (!materials.TryGetValue(id, out MaterialUsage? usage))
                        {
                            if (materials.Count >= MaxMaterials) { omitted++; continue; }
                            if (document.GetElement(id) is not Material material) { failures++; continue; }
                            Color color = material.Color;
                            var data = new BimMaterial(material.Name, material.MaterialClass ?? "",
                                $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}", material.Transparency,
                                material.UseRenderAppearanceForShading, 0, false, Array.Empty<BimCategory>());
                            usage = new MaterialUsage { Data = data };
                            materials.Add(id, usage);
                        }
                        usage.Count++;
                        usage.Paint |= paintIds.Contains(id);
                        Increment(usage.Categories, categoryName);
                    }
                    catch (Exception) { failures++; }
                }
            }
            catch (Exception) { failures++; }
        }
        notes.Add("Escopo: candidatos de categorias de modelo da vista capturada, no documento principal. Nao e uma identificacao de superficies por pixel.");
        notes.Add("Materiais internos de camadas e objetos encobertos podem aparecer. Recortes locais do PNG nao filtram este levantamento.");
        notes.Add("Nome e classe sao metadados cadastrados no Revit, nao certificacao fisica. Classe vazia ou generica continua incerta; nao classifique apenas pela cor.");
        notes.Add("Cor e transparencia sao propriedades graficas do material. Overrides da vista, filtros, texturas e propriedades dos assets de aparencia nao foram analisados.");
        notes.Add("Familias aninhadas e materiais definidos por categoria podem nao ser retornados por GetMaterialIds. Ausencia de ids nao confirma ausencia de material.");
        notes.Add("Dados congelados no momento da captura; capture novamente apos alterar o modelo ou a vista.");
        if (limited) notes.Add("Leitura parcial: limite de 2000 elementos ou orcamento de 5 segundos atingido. Uma chamada individual da API pode exceder esse tempo.");
        if (omitted > 0) notes.Add($"Leitura parcial: limite de {MaxMaterials} materiais distintos; {omitted} associacoes adicionais omitidas.");
        if (failures > 0) notes.Add($"Leitura parcial: {failures} consultas de elementos ou materiais nao puderam ser concluidas.");
        if (links > 0) notes.Add($"Materiais de {links} vinculos Revit nao foram lidos nesta etapa.");
        if (imports > 0) notes.Add($"Materiais de {imports} importacoes CAD nao foram lidos nesta etapa.");
        BimReadState state = limited || omitted > 0 || failures > 0 || links > 0 || imports > 0
            ? BimReadState.Partial : BimReadState.Read;
        return new BimSceneSnapshot(state, scanned, noIds, failures, links, imports,
            categories.OrderBy(p => p.Key).Select(p => new BimCategory(p.Key, p.Value)).ToArray(),
            materials.Values.Select(u => u.Data with
            {
                ElementCount = u.Count, HasPaintAssignment = u.Paint,
                UsesByCategory = u.Categories.OrderBy(p => p.Key).Select(p => new BimCategory(p.Key, p.Value)).ToArray()
            }).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.MaterialClass).ToArray(),
            notes.ToArray());
    }

    internal static BimSceneSnapshot Unavailable() => new(BimReadState.Unavailable, 0, 0, 0, 0, 0,
        Array.Empty<BimCategory>(), Array.Empty<BimMaterial>(),
        new[] { "A imagem foi capturada, mas a consulta BIM nao foi concluida. Nao assuma materiais confirmados; tente capturar novamente." });

    private static void Increment(Dictionary<string, int> values, string key) =>
        values[key] = values.TryGetValue(key, out int count) ? count + 1 : 1;
}