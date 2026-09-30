using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Arqyzeng.Core;

namespace Arqyzeng.Rendering;

internal sealed record ProfileRules(string Id, string Name, string Version, string[] Instructions);
internal sealed record MaterialCategory(string Id, string Name, string[] Instructions);
internal sealed record MaterialRules(string Id, string Version, MaterialCategory[] Categories);

// Nucleo sem dependencia da API Revit, da UI ou de um provedor de IA.
internal static class PromptBuilder
{
    internal static string Build(RenderSettings settings, RenderScene scene, string rulesDirectory)
    {
        if (!Enum.IsDefined(settings.Profile) || !Enum.IsDefined(settings.Fidelity))
            throw new ArgumentException("Perfil ou fidelidade invalido.");
        if (scene.Width < 256 || scene.Height < 256 || scene.Width > 4096 || scene.Height > 4096)
            throw new ArgumentException("Dimensoes fora do intervalo permitido.");
        string file = settings.Profile == RenderProfile.BumpDay ? "BumpDayRules.json" : "BumpNightRules.json";
        string expectedId = settings.Profile == RenderProfile.BumpDay ? "bump-day" : "bump-night";
        ProfileRules rules = LoadRules(Path.Combine(rulesDirectory, file), expectedId);
        ProfileRules general = LoadRules(Path.Combine(rulesDirectory, "RenderGeneralRules.json"), "render-general");
        var materials = JsonSerializer.Deserialize<MaterialRules>(File.ReadAllText(Path.Combine(rulesDirectory, "MaterialLibrary.json")))
            ?? throw new InvalidOperationException("Biblioteca de materialidade vazia.");
        if (materials.Id != "material-library" || string.IsNullOrWhiteSpace(materials.Version) ||
            materials.Categories is not { Length: > 0 } ||
            materials.Categories.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) ||
                c.Instructions is not { Length: > 0 } || c.Instructions.Any(string.IsNullOrWhiteSpace)) ||
            materials.Categories.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != materials.Categories.Length)
            throw new InvalidOperationException("Biblioteca de materialidade invalida.");
        var prompt = new StringBuilder();
        prompt.AppendLine("ARQYZENG BIM - INSTRUCOES DE RENDER");
        prompt.AppendLine($"Perfil: {rules.Name} | Regras: {rules.Version}");
        prompt.AppendLine($"Fidelidade: {FidelityLabel(settings.Fidelity)}");
        prompt.AppendLine($"Imagem de referencia: {scene.Width} x {scene.Height} px.");
        prompt.AppendLine("Use a captura anexada como referencia vinculante do projeto.");
        prompt.AppendLine();
        prompt.AppendLine("RESTRICOES OBRIGATORIAS");
        prompt.AppendLine("- Preserve a geometria e os volumes arquitetonicos. Nao redesenhe o projeto.");
        prompt.AppendLine("- Preserve quantidade, posicao e dimensoes aparentes de portas, janelas e vaos.");
        prompt.AppendLine("- Nao adicione nem remova pavimentos, coberturas ou marquises.");
        prompt.AppendLine("- Preserve a identidade dos materiais especificados; melhore sua leitura fisica.");
        prompt.AppendLine("- Quando um material for incerto, nao invente uma especificacao nem troque sua identidade.");
        prompt.AppendLine("- Vidro e material, nao vazio: preserve esquadrias, transparencia e reflexos plausiveis.");
        prompt.AppendLine("- Luz artificial forte deve corresponder a uma fonte existente ou explicitamente autorizada.");
        prompt.AppendLine("- LEDs devem respeitar fonte, emissao, superficie iluminada, falloff e contribuicao indireta.");
        prompt.AppendLine("- Fidelidade arquitetonica tem prioridade sobre impacto visual.");
        prompt.AppendLine();
        if (settings.Fidelity == FidelityMode.Maximum)
        {
            AppendFrameLock(prompt);
            AppendSceneLock(prompt);
        }
        prompt.AppendLine("LIBERDADE DO MODO");
        switch (settings.Fidelity)
        {
            case FidelityMode.Creative:
                prompt.AppendLine("- Permita ambientacao conceitual moderada, com vegetacao e decoracao no contexto, sem alterar a arquitetura nem obstruir suas aberturas.");
                prompt.AppendLine("- Preserve o ponto de vista da captura para permitir comparacao. Nao crie fontes luminosas fortes sem autorizacao.");
                break;
            case FidelityMode.Controlled:
                prompt.AppendLine("- Preserve a camera, a perspectiva e a composicao da captura.");
                prompt.AppendLine("- Permita ajustes moderados no contexto existente e na leitura dos acabamentos, sem trocar materiais especificados.");
                prompt.AppendLine("- Nao acrescente pessoas, carros, vegetacao, mobiliario ou decoracao novos sem autorizacao.");
                break;
            case FidelityMode.Maximum:
                prompt.AppendLine("- Preserve a camera, a perspectiva e a composicao da captura.");
                prompt.AppendLine("- Bloqueie alteracoes semanticas: nao acrescente, remova ou substitua elementos da cena.");
                prompt.AppendLine("- Melhore apenas luz, resposta fisica dos materiais, sombras, reflexos e tratamento tonal, sem mover os contornos do projeto.");
                break;
        }
        prompt.AppendLine();
        prompt.AppendLine(BimContextFormatter.Format(scene.Bim));
        AppendMaterialConfirmations(prompt, scene.MaterialConfirmations);
        prompt.AppendLine($"REGRAS GERAIS DOS GUIAS - {general.Version}");
        foreach (string instruction in general.Instructions) prompt.AppendLine("- " + instruction);
        prompt.AppendLine();
        prompt.AppendLine($"MATERIALIDADE CONDICIONAL - {materials.Version}");
        prompt.AppendLine("Estas regras sao orientacoes qualitativas. Consulte os dados BIM disponiveis, aplique cada categoria somente quando identificada com confianca e preserve a incerteza nas demais.");
        foreach (MaterialCategory category in materials.Categories)
            foreach (string instruction in category.Instructions)
                prompt.AppendLine($"- {category.Name}: {instruction}");
        prompt.AppendLine();
        prompt.AppendLine("PERFIL DE ILUMINACAO E APARENCIA");
        foreach (string instruction in rules.Instructions) prompt.AppendLine("- " + instruction);
        prompt.AppendLine();
        prompt.AppendLine("CONTEXTO DISPONIVEL");
        prompt.AppendLine("- A leitura BIM, quando disponivel, corresponde a captura original; mudar o recorte local nao identifica quais materiais permanecem visiveis.");
        prompt.AppendLine("- Dados BIM explicitos e confiaveis tem precedencia sobre inferencias visuais; metadados genericos continuam incertos.");
        prompt.AppendLine("- Entregue uma imagem na proporcao da referencia. Estas instrucoes nao garantem, por si so, a fidelidade do resultado de uma IA.");
        if (settings.Fidelity == FidelityMode.Maximum)
        {
            prompt.AppendLine("VERIFICACAO FINAL: mantenha todos os elementos nos mesmos lugares relativos ao quadro, incluindo espacos vazios e bordas. Nao aproxime nem recentralize a casa para melhorar a apresentacao.");
            prompt.AppendLine("PRIORIDADE FINAL DO MODO MAXIMO: as regras de entorno e material incerto prevalecem sobre ambientacao, paisagismo ou tratamento estetico sugeridos nos guias. Nenhuma arvore nova, nenhum entorno inventado, nenhuma troca de fundo neutro por ceu e nenhum revestimento reinterpretado sem identificacao confiavel.");
        }
        return prompt.ToString();
    }

    internal static string BuildForImageEdit(RenderSettings settings, RenderScene reference, string rulesDirectory)
    {
        // A referencia e o PNG efetivamente anexado, ja nas dimensoes de saida.
        string prompt = Build(settings, reference, rulesDirectory);
        if (settings.Profile == RenderProfile.BumpDay &&
            settings.Fidelity == FidelityMode.Maximum)
        {
            prompt = prompt.Replace(
                "- Areas vazias, brancas ou cinzas da captura sao fundo neutro a preservar. Nao complete essas areas com ceu, nuvens, horizonte, vizinhos, arvores ou jardins.",
                "- CEU AZUL AUTORIZADO: no Bump Dia, substitua somente o fundo externo vazio visivel por ceu azul natural. Preserve as margens, a escala e a posicao do projeto. Nao substitua paredes, pisos, coberturas ou outras superficies por ceu. Nao acrescente arvores, jardins, vizinhos ou construcoes. Em cenas internas, preserve as superficies e limite o ceu ao exterior visivel pelas aberturas.");
            prompt = prompt.Replace(
                "nenhuma troca de fundo neutro por ceu",
                "CEU AZUL AUTORIZADO somente nas regioes de fundo externo vazio, como excecao as restricoes de fundo");
        }
        return prompt +
            $"\nEDICAO DA IMAGEM ANEXADA: devolva {reference.Width} x {reference.Height} px. " +
            "A captura inteira e o quadro final: preserve suas bordas e nao crie outro enquadramento.";
    }

    private static void AppendFrameLock(StringBuilder prompt)
    {
        prompt.AppendLine("ENQUADRAMENTO VINCULANTE - FIDELIDADE MAXIMA");
        prompt.AppendLine("- Edite a aparencia da imagem anexada no mesmo quadro. Nao recrie a cena a partir de uma nova camera.");
        prompt.AppendLine("- Nao aplique zoom, recorte, ampliacao da casa, deslocamento, recentralizacao, rotacao nem correcao de perspectiva.");
        prompt.AppendLine("- Preserve a escala e as posicoes relativas ao quadro de cada volume, janela, muro, pergolado, jardim, calcada e rua.");
        prompt.AppendLine("- Preserve os espacos vazios e as margens acima, abaixo e nas laterais. Fundo branco e margem branca nao autorizam aproximar o modelo.");
        prompt.AppendLine("- Elementos junto as bordas devem permanecer visiveis na mesma extensao; nao corte a extremidade da rua nem dos muros.");
        prompt.AppendLine("- Preserve inclinacoes, linhas de cobertura, proporcoes e alinhamentos; melhora de luz e textura deve respeitar esses contornos.");
    }

    private static void AppendSceneLock(StringBuilder prompt)
    {
        prompt.AppendLine("ENTORNO E MATERIAIS VINCULANTES - FIDELIDADE MAXIMA");
        prompt.AppendLine("- Areas vazias, brancas ou cinzas da captura sao fundo neutro a preservar. Nao complete essas areas com ceu, nuvens, horizonte, vizinhos, arvores ou jardins.");
        prompt.AppendLine("- Nao acrescente vegetacao nas laterais, atras dos muros, sobre a cobertura ou no interior. Preserve quantidade, localizacao e porte aproximado das plantas ja visiveis.");
        prompt.AppendLine("- Melhore a aparencia das plantas existentes sem ampliar sua copa nem criar vegetacao fora de seus locais originais.");
        prompt.AppendLine("- Nao invente elementos ocultos ou externos para justificar reflexos ou sombras. Preserve leitura das aberturas sem inserir decoracao, arvores ou mobiliario em reflexos.");
        prompt.AppendLine("- Material incerto: mantenha cor, padrao e acabamento aparentes da captura. Generic, cor de sombreamento e nomes ambiguos nao comprovam uma especificacao fisica.");
        prompt.AppendLine("- Nao transforme cobertura incerta em outro tipo de telha; preserve direcao, ritmo e escala do padrao existente sem criar perfil ou relevo diferente.");
        prompt.AppendLine("- Nao crie rodapes, juntas de revestimento, paginação, placas, chanfros, ferragens ou detalhes construtivos ausentes. Preserve os detalhes existentes na mesma distribuicao.");
        prompt.AppendLine("- As categorias da biblioteca sao condicionais, nao uma lista de materiais a introduzir. Dados BIM agregados da vista nao autorizam aplicar um material a uma superficie sem correspondencia confiavel.");
        prompt.AppendLine("- O perfil Dia ou Noite muda a iluminacao da cena existente; nao autoriza reconstruir o entorno nem adicionar fontes de luz.");
        prompt.AppendLine();
    }

    private static void AppendMaterialConfirmations(StringBuilder prompt, MaterialConfirmation[]? confirmations)
    {
        prompt.AppendLine("CONFIRMACOES MANUAIS DE MATERIAIS");
        prompt.AppendLine("As descricoes abaixo foram fornecidas manualmente para orientar a aparencia. Elas nao alteram o Revit e nao provam correspondencia exata entre um material BIM agregado e uma superficie ou pixel da imagem.");
        if (confirmations is not { Length: > 0 })
        {
            prompt.AppendLine("- Nenhuma descricao manual confirmada para esta captura. Preserve a incerteza dos materiais nao confirmados.");
            prompt.AppendLine();
            return;
        }
        foreach (MaterialConfirmation confirmation in confirmations)
        {
            var data = new { confirmation.Name, confirmation.MaterialClass, confirmation.Categories, confirmation.Description };
            prompt.AppendLine(JsonSerializer.Serialize(data));
        }
        prompt.AppendLine("Use cada descricao somente como orientacao para o material identificado; nao a propague para outras superficies sem evidencia suficiente.");
        prompt.AppendLine();
    }

    private static ProfileRules LoadRules(string path, string expectedId)
    {
        var rules = JsonSerializer.Deserialize<ProfileRules>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Arquivo de regras vazio.");
        if (rules.Id != expectedId || string.IsNullOrWhiteSpace(rules.Name) ||
            string.IsNullOrWhiteSpace(rules.Version) || rules.Instructions is not { Length: > 0 } ||
            rules.Instructions.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Arquivo de regras invalido: " + Path.GetFileName(path));
        return rules;
    }

    internal static string FidelityLabel(FidelityMode mode) => mode switch
    {
        FidelityMode.Creative => "Criativo",
        FidelityMode.Controlled => "Controlado",
        FidelityMode.Maximum => "Fidelidade Maxima",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}