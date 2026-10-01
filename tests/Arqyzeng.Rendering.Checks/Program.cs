using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Arqyzeng.Core;
using Arqyzeng.Rendering;

int checkedCases = 0;
string rules = Path.Combine(AppContext.BaseDirectory, "data");
var scene = new RenderScene("Vista de teste", 1920, 1080);
void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
foreach (RenderProfile profile in Enum.GetValues<RenderProfile>())
foreach (FidelityMode fidelity in Enum.GetValues<FidelityMode>())
{
    string prompt = PromptBuilder.Build(new(profile, fidelity), scene, rules);
    Require(prompt.Contains("Preserve a geometria") && prompt.Contains("portas, janelas e vaos") &&
        prompt.Contains("identidade dos materiais") && prompt.Contains("fonte existente"), "Restricoes obrigatorias ausentes.");
    Require(prompt.Contains("1920 x 1080") && prompt.Contains("Sem dados BIM para esta captura"), "Contexto incorreto.");
    Require(prompt.Contains(PromptBuilder.FidelityLabel(fidelity)), "Modo incorreto.");
    Require(prompt.Contains("REGRAS GERAIS DOS GUIAS - 1.1") && prompt.Contains("MATERIALIDADE CONDICIONAL - 1.1"), "Guias compartilhados ausentes.");
    Require(prompt.Contains("Vidro reflecta:") && prompt.Contains("Silestone") && prompt.Contains("veludo") &&
        prompt.Contains("Nas superficies brancas") && prompt.Contains("valores universais de IOR"), "Materialidade incompleta.");
    Require(prompt.Contains("hipoteses, nunca confirmacao automatica") && prompt.Contains("sem remodelar o projeto"), "Protecao de incerteza ou geometria ausente.");
    Require(prompt.IndexOf("RESTRICOES OBRIGATORIAS", StringComparison.Ordinal) < prompt.IndexOf("MATERIALIDADE CONDICIONAL", StringComparison.Ordinal), "Prioridade de restricoes incorreta.");
    if (profile == RenderProfile.BumpDay)
        Require(prompt.Contains("Bump Dia") && prompt.Contains("luz natural") && !prompt.Contains("3000-3500"), "Perfil Dia incorreto.");
    else
        Require(prompt.Contains("Bump Noite") && prompt.Contains("3000-3500") && prompt.Contains("subexposicao"), "Perfil Noite incorreto.");
    if (fidelity != FidelityMode.Creative)
        Require(prompt.Contains("Preserve a camera, a perspectiva"), "Preservacao de camera ausente.");
    if (fidelity == FidelityMode.Maximum)
        Require(prompt.Contains("Bloqueie alteracoes semanticas"), "Bloqueio semantico ausente.");
    if (fidelity == FidelityMode.Controlled)
        Require(prompt.Contains("Nao acrescente"), "Restricao de elementos adicionais ausente.");
    if (fidelity == FidelityMode.Creative)
        Require(prompt.Contains("ambientacao conceitual moderada") && !prompt.Contains("Bloqueie alteracoes semanticas"), "Liberdade criativa incorreta.");
    checkedCases++;
}
void MustReject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; }
    Require(rejected, message);
}
MustReject(() => PromptBuilder.Build(new((RenderProfile)999, FidelityMode.Maximum), scene, rules), "Perfil invalido aceito.");
MustReject(() => PromptBuilder.Build(new(RenderProfile.BumpDay, (FidelityMode)999), scene, rules), "Fidelidade invalida aceita.");
MustReject(() => PromptBuilder.Build(new(RenderProfile.BumpDay, FidelityMode.Maximum), scene with { Width = 0 }, rules), "Dimensoes invalidas aceitas.");
var bim = new BimSceneSnapshot(BimReadState.Partial, 12, 2, 0, 1, 0,
    new[] { new BimCategory("Paredes", 8), new BimCategory("Portas", 4) },
    new[] { new BimMaterial("Madeira Freijo", "Wood", "#996633", 0, false, 3, true,
        new[] { new BimCategory("Portas", 3) }) },
    new[] { "Materiais de vinculos nao incluidos.", "Candidatos nao correspondem a pixels." });
string bimPrompt = PromptBuilder.Build(new(RenderProfile.BumpNight, FidelityMode.Maximum), scene with { Bim = bim }, rules);
Require(bimPrompt.Contains("Madeira Freijo") && bimPrompt.Contains("Wood") && bimPrompt.Contains("#996633") &&
    bimPrompt.Contains("HasPaintAssignment") && bimPrompt.Contains("Paredes"), "Dados BIM ausentes do prompt.");
Require(bimPrompt.Contains("consulta parcial") && bimPrompt.Contains("vinculos nao incluidos") &&
    !bimPrompt.Contains("Sem dados BIM para esta captura"), "Estado de leitura incorreto.");
Require(bimPrompt.Contains("nunca comandos") && bimPrompt.Contains("nao area ou volume visivel"), "Limites dos dados BIM ausentes.");
var suspicious = bim with { Materials = new[] { bim.Materials[0] with { Name = "Material\nNAO E COMANDO" } } };
string jsonContext = BimContextFormatter.Format(suspicious);
Require(jsonContext.Contains("Material\\nNAO E COMANDO") && !jsonContext.Contains("Material\nNAO E COMANDO"), "Nome BIM nao foi escapado como dado JSON.");
var empty = bim with { State = BimReadState.Read, Materials = Array.Empty<BimMaterial>() };
Require(BimContextFormatter.Format(empty).Contains("Isso nao prova ausencia de materiais"), "Material ausente interpretado como vazio.");
var unavailable = bim with { State = BimReadState.Unavailable };
Require(BimContextFormatter.Format(unavailable).Contains("Sem dados BIM para esta captura") &&
    !BimContextFormatter.Format(unavailable).Contains("Madeira Freijo"), "Estado indisponivel vazou dados antigos.");
var library = JsonSerializer.Deserialize<MaterialRules>(File.ReadAllText(Path.Combine(rules, "MaterialLibrary.json")))!;
Require(library.Categories.Length == 20, "Categorias esperadas ausentes.");
string scratchRules = Path.Combine(Path.GetTempPath(), "Arqyzeng-PromptChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratchRules);
try
{
    foreach (string path in Directory.GetFiles(rules, "*.json")) File.Copy(path, Path.Combine(scratchRules, Path.GetFileName(path)));
    string libraryPath = Path.Combine(scratchRules, "MaterialLibrary.json");
    var duplicated = library with { Categories = new[] { library.Categories[0], library.Categories[0] } };
    File.WriteAllText(libraryPath, JsonSerializer.Serialize(duplicated));
    bool rejected = false;
    try { PromptBuilder.Build(new(RenderProfile.BumpDay, FidelityMode.Maximum), scene, scratchRules); }
    catch (InvalidOperationException) { rejected = true; }
    Require(rejected, "Biblioteca com categorias duplicadas aceita.");
    File.Copy(Path.Combine(rules, "MaterialLibrary.json"), libraryPath, true);
    File.Delete(Path.Combine(scratchRules, "RenderGeneralRules.json"));
    rejected = false;
    try { PromptBuilder.Build(new(RenderProfile.BumpDay, FidelityMode.Maximum), scene, scratchRules); }
    catch (FileNotFoundException) { rejected = true; }
    Require(rejected, "Prompt gerado sem regras gerais obrigatorias.");
}
finally { Directory.Delete(scratchRules, true); }
Console.WriteLine($"PromptBuilder: {checkedCases} combinacoes Dia/Noite e fidelidade + validacoes de regras, 20 categorias e contexto BIM aprovadas.");

var confirmedScene = scene with { MaterialConfirmations = new[] { new MaterialConfirmation("k", "Vidro Fachada", "Glass", new[] { "Janelas" }, "vidro incolor") } };
string confirmedPrompt = PromptBuilder.Build(new(RenderProfile.BumpDay, FidelityMode.Maximum), confirmedScene, rules);
Require(confirmedPrompt.Contains("CONFIRMACOES MANUAIS DE MATERIAIS") && confirmedPrompt.Contains("vidro incolor") && confirmedPrompt.Contains("nao provam correspondencia exata"), "Confirmacoes manuais nao chegaram ao prompt.");

var editScene = scene with { Width = 1536, Height = 864 };
string maximumEdit = PromptBuilder.BuildForImageEdit(new(RenderProfile.BumpDay, FidelityMode.Maximum), editScene, rules);
Require(maximumEdit.Contains("Imagem de referencia: 1536 x 864") && maximumEdit.Contains("devolva 1536 x 864") &&
    !maximumEdit.Contains("1920 x 1080"), "Prompt nao corresponde a referencia enviada.");
Require(maximumEdit.Contains("ENQUADRAMENTO VINCULANTE") && maximumEdit.Contains("Nao aplique zoom, recorte") &&
    maximumEdit.Contains("espacos vazios e as margens") && maximumEdit.Contains("extremidade da rua") &&
    maximumEdit.Contains("VERIFICACAO FINAL"), "Protecoes contra reenquadramento ausentes.");
string controlledEdit = PromptBuilder.BuildForImageEdit(new(RenderProfile.BumpDay, FidelityMode.Controlled), editScene, rules);
Require(!controlledEdit.Contains("ENQUADRAMENTO VINCULANTE - FIDELIDADE MAXIMA") && controlledEdit.Contains("Preserve a camera"), "Modo controlado incorreto.");
Console.WriteLine("Prompt de edicao: dimensoes da referencia e restricoes contra reenquadramento conferidas.");

foreach (RenderProfile profile in Enum.GetValues<RenderProfile>())
{
    string strict = PromptBuilder.BuildForImageEdit(new(profile, FidelityMode.Maximum), editScene, rules);
    Require(strict.Contains("ENTORNO E MATERIAIS VINCULANTES") && strict.Contains("Nenhuma arvore nova") &&
        strict.Contains(profile == RenderProfile.BumpDay ? "CEU AZUL AUTORIZADO" : "nenhuma troca de fundo neutro por ceu") && strict.Contains("Nao crie rodapes, juntas") &&
        strict.Contains("Generic, cor de sombreamento") && strict.Contains("correspondencia confiavel"), "Restricoes de entorno ou material incerto ausentes.");
    Require(strict.IndexOf("ENTORNO E MATERIAIS VINCULANTES", StringComparison.Ordinal) < strict.IndexOf("REGRAS GERAIS DOS GUIAS", StringComparison.Ordinal) &&
        strict.IndexOf("PRIORIDADE FINAL DO MODO MAXIMO", StringComparison.Ordinal) > strict.IndexOf("PERFIL DE ILUMINACAO E APARENCIA", StringComparison.Ordinal), "Prioridade do modo maximo incorreta.");
    string creative = PromptBuilder.BuildForImageEdit(new(profile, FidelityMode.Creative), editScene, rules);
    Require(!creative.Contains("ENTORNO E MATERIAIS VINCULANTES") && creative.Contains("ambientacao conceitual moderada"), "Restricao maxima imposta ao criativo.");
}
Console.WriteLine("Fidelidade Maxima: protecoes de entorno, materiais incertos e prioridade sobre guias conferidas para Dia e Noite.");
await ImageProviderChecks.RunAsync();
// Invariantes da projecao: seis eixos, orientacao dos polos e continuidade angular.
var axes = new (double X, double Y, double Z, int Face)[] {
    (0, 0, 1, 0), (1, 0, 0, 1), (0, 0, -1, 2), (-1, 0, 0, 3), (0, 1, 0, 4), (0, -1, 0, 5) };
foreach (var axis in axes)
{
    var mapped = Arqyzeng.Panorama.CubeProjection.Map(axis.X, axis.Y, axis.Z);
    Require(mapped.Face == axis.Face && Math.Abs(mapped.U - .5) < 1e-12 && Math.Abs(mapped.V - .5) < 1e-12, "Eixo cubemap incorreto.");
}
var northFront = Arqyzeng.Panorama.CubeProjection.Map(0, 1, .5);
var southFront = Arqyzeng.Panorama.CubeProjection.Map(0, -1, .5);
Require(northFront.Face == 4 && northFront.V > .5 && southFront.Face == 5 && southFront.V < .5, "Orientacao do teto/piso invertida.");
var random = new Random(42);
for (int sample = 0; sample < 2000; sample++)
{
    double x = random.NextDouble() * 2 - 1, y = random.NextDouble() * 2 - 1, z = random.NextDouble() * 2 - 1;
    var m = Arqyzeng.Panorama.CubeProjection.Map(x, y, z);
    Require(m.U >= 0 && m.U <= 1 && m.V >= 0 && m.V <= 1, "Coordenada fora da face.");
    double a = 2 * m.U - 1, b = 1 - 2 * m.V;
    (double X, double Y, double Z) reconstructed = m.Face switch {
        0 => (a, b, 1), 1 => (1, b, -a), 2 => (-a, b, -1),
        3 => (-1, b, a), 4 => (a, 1, -b), 5 => (a, -1, b), _ => throw new InvalidOperationException() };
    double originalLength = Math.Sqrt(x * x + y * y + z * z);
    double reconstructedLength = Math.Sqrt(reconstructed.X * reconstructed.X + reconstructed.Y * reconstructed.Y + reconstructed.Z * reconstructed.Z);
    double agreement = (x * reconstructed.X + y * reconstructed.Y + z * reconstructed.Z) / (originalLength * reconstructedLength);
    Require(agreement > 1 - 1e-12, "Raio cubemap mudou de direcao.");
}
bool invalidDirectionRejected = false;
try { Arqyzeng.Panorama.CubeProjection.Map(0, 0, 0); }
catch (ArgumentException) { invalidDirectionRejected = true; }
Require(invalidDirectionRejected, "Direcao nula aceita.");
Console.WriteLine("Panorama: seis eixos, orientacao dos polos e 2000 raios conferidos.");

var viewerAxes = new (double U, double V, double X, double Y, double Z)[] {
    (.5, .5, 0, 0, -1), (.75, .5, 1, 0, 0), (.25, .5, -1, 0, 0),
    (0, .5, 0, 0, 1), (.5, 0, 0, 1, 0), (.5, 1, 0, -1, 0) };
foreach (var expected in viewerAxes)
{
    var d = Arqyzeng.Panorama.PanoramaViewerMath.SpherePoint(expected.U, expected.V);
    Require(Math.Abs(d.X - expected.X) < 1e-12 && Math.Abs(d.Y - expected.Y) < 1e-12 && Math.Abs(d.Z - expected.Z) < 1e-12,
        "Viewer: textura espelhada ou eixo invertido.");
}
for (int sample = 0; sample < 1000; sample++)
{
    double u = random.NextDouble() * .998 + .001, v = random.NextDouble() * .998 + .001;
    var d = Arqyzeng.Panorama.PanoramaViewerMath.SpherePoint(u, v);
    double recoveredU = Math.Atan2(d.X, -d.Z) / (2 * Math.PI) + .5;
    double recoveredV = .5 - Math.Asin(Math.Clamp(d.Y, -1, 1)) / Math.PI;
    Require(Math.Abs(u - recoveredU) < 1e-10 && Math.Abs(v - recoveredV) < 1e-10, "Viewer: coordenada de textura nao corresponde ao raio.");
}
Require(Arqyzeng.Panorama.PanoramaViewerMath.WrapYaw(360) == 0 &&
    Arqyzeng.Panorama.PanoramaViewerMath.WrapYaw(-360) == 0 &&
    Arqyzeng.Panorama.PanoramaViewerMath.WrapYaw(181) == -179, "Viewer: fechamento de longitude incorreto.");
Console.WriteLine("Viewer 360: frente, laterais, polos, fechamento e 1000 coordenadas de textura conferidos.");

// Limites de resolucao: aceitar somente os dois modos; nunca normalizar um erro maior que um pixel.
foreach (int edge in new[] { 1024, 2048 })
{
    var size = Arqyzeng.Panorama.CubeProjection.OutputSize(edge);
    Require(size.Width == (edge == 1024 ? 4096 : 8192) && size.Width == size.Height * 2,
        "Panorama: resolucao final ou proporcao incorreta.");
    for (int dx = -2; dx <= 2; dx++)
        for (int dy = -2; dy <= 2; dy++)
            Require(Arqyzeng.Panorama.CubeProjection.CanNormalizeRaster(edge + dx, edge + dy, edge) ==
                (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1), "Panorama: tolerancia de raster incorreta.");
    Require(!Arqyzeng.Panorama.CubeProjection.CanNormalizeRaster(int.MinValue, int.MaxValue, edge),
        "Panorama: raster extremo aceito.");
}
foreach (int invalidSize in new[] { -1, 0, 512, 1536, 4096, int.MaxValue })
{
    bool rejected = false;
    try { Arqyzeng.Panorama.CubeProjection.OutputSize(invalidSize); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Require(rejected, "Panorama: tamanho de face nao suportado aceito.");
}
Console.WriteLine("Panorama: modos 4K/8K, proporcao 2:1, tolerancia de um pixel e limites de resolucao conferidos.");

// Overscan: preservar o mesmo raio de 90 graus, com bordas fora da regiao util.
foreach (int faceSize in new[] { 1024, 2048 })
{
    int rawSize = Arqyzeng.Panorama.CubeProjection.OverscanPixels(faceSize);
    Require(rawSize > faceSize, "Overscan: resolucao bruta nao aumentou.");
    foreach (int delta in new[] { -1, 0, 1 })
    {
        double hfov = 93.98, vfov = 94.02;
        var region = Arqyzeng.Panorama.CubeProjection.CentralFaceRegion(rawSize, rawSize + delta, hfov, vfov);
        Require(region.X >= 8 && region.Y >= 8, "Overscan: margem insuficiente.");
        Require(Math.Abs(region.X * 2 + region.Width - rawSize) < 1e-9 &&
            Math.Abs(region.Y * 2 + region.Height - rawSize - delta) < 1e-9, "Overscan: centro da camera mudou.");
        foreach (double coordinate in new[] { 0.0, .125, .5, .875, 1.0 })
        {
            double sourceX = region.X + coordinate * region.Width;
            double sourceY = region.Y + coordinate * region.Height;
            double rayX = (2 * sourceX / rawSize - 1) * Math.Tan(hfov * Math.PI / 360);
            double rayY = (2 * sourceY / (rawSize + delta) - 1) * Math.Tan(vfov * Math.PI / 360);
            Require(Math.Abs(rayX - (2 * coordinate - 1)) < 1e-12 &&
                Math.Abs(rayY - (2 * coordinate - 1)) < 1e-12,
                "Overscan: raio final nao corresponde ao cubemap de 90 graus.");
        }
    }
}
foreach (double invalidFov in new[] { 90.0, 89.0, 100.0, double.NaN, double.PositiveInfinity })
{
    bool rejected = false;
    try { Arqyzeng.Panorama.CubeProjection.CentralFaceRegion(2048, 2048, invalidFov, 94); }
    catch (ArgumentException) { rejected = true; }
    Require(rejected, "Overscan: FOV invalido aceito.");
}
Console.WriteLine("Panorama overscan: centro, margem e raios exatos de 90 graus conferidos nos dois modos.");

// Particionamento: cada pixel pertence a uma unica parte, UV exata e margens circulares.
foreach (int panoramaWidth in new[] { 512, 4096, 8192 })
{
    int panoramaHeight = panoramaWidth / 2;
    var tiles = Arqyzeng.Panorama.PanoramaViewerMath.TextureTiles(panoramaWidth, panoramaHeight);
    long area = 0;
    foreach (var tile in tiles)
    {
        area += (long)tile.Width * tile.Height;
        Require(tile.Width <= 1024 && tile.Height <= 1024 && tile.X >= 0 && tile.Y >= 0 &&
            tile.X + tile.Width <= panoramaWidth && tile.Y + tile.Height <= panoramaHeight,
            "Viewer: parte fora do raster ou textura muito grande.");
        foreach (double fraction in new[] { 0.0, .25, .5, .75, 1.0 })
        {
            double recoveredX = tile.TextureU(fraction) * tile.PaddedWidth - Arqyzeng.Panorama.PanoramaTextureTile.Gutter;
            double recoveredY = tile.TextureV(fraction) * tile.PaddedHeight - Arqyzeng.Panorama.PanoramaTextureTile.Gutter;
            Require(Math.Abs(recoveredX - fraction * tile.Width) < 1e-10 &&
                Math.Abs(recoveredY - fraction * tile.Height) < 1e-10, "Viewer: margem deslocou coordenadas.");
        }
    }
    Require(area == (long)panoramaWidth * panoramaHeight, "Viewer: partes nao cobrem todos os pixels.");
    Require(Arqyzeng.Panorama.PanoramaViewerMath.WrapPixelX(-1, panoramaWidth) == panoramaWidth - 1 &&
        Arqyzeng.Panorama.PanoramaViewerMath.WrapPixelX(panoramaWidth, panoramaWidth) == 0,
        "Viewer: margem nao fecha a longitude.");
    for (int i = 0; i < tiles.Length; i++)
        for (int j = i + 1; j < tiles.Length; j++)
            Require(tiles[i].X + tiles[i].Width <= tiles[j].X || tiles[j].X + tiles[j].Width <= tiles[i].X ||
                tiles[i].Y + tiles[i].Height <= tiles[j].Y || tiles[j].Y + tiles[j].Height <= tiles[i].Y,
                "Viewer: partes se sobrepoem.");
}
Console.WriteLine("Viewer: partes nativas, cobertura completa, UV e fechamento das margens conferidos.");

// Mais amostras nativas, mesmo quadro e mesmas dimensoes finais; nenhuma ampliacao de PNG antigo.
foreach (int faceSize in new[] { 1024, 2048 })
{
    int standardRaw = Arqyzeng.Panorama.CubeProjection.OverscanPixels(faceSize, 1);
    int fineRaw = Arqyzeng.Panorama.CubeProjection.OverscanPixels(faceSize, 2);
    Require(Math.Abs(fineRaw - 2 * standardRaw) <= 1 && fineRaw < 5000,
        "Panorama: resolucao bruta de detalhes finos incorreta.");
    foreach (int sampling in new[] { 1, 2 })
    {
        int raw = Arqyzeng.Panorama.CubeProjection.OverscanPixels(faceSize, sampling);
        var region = Arqyzeng.Panorama.CubeProjection.CentralFaceRegion(raw, raw - 1, 94, 94);
        Require(Math.Abs(region.Width - faceSize * sampling) < 1 &&
            Math.Abs(region.Height - faceSize * sampling) < 1,
            "Panorama: area central nao tem as amostras esperadas.");
        double tangent = Math.Tan(94 * Math.PI / 360);
        foreach (double coordinate in new[] { 0.0, .1, .5, .9, 1.0 })
        {
            double direction = (2 * (region.X + coordinate * region.Width) / raw - 1) * tangent;
            Require(Math.Abs(direction - (2 * coordinate - 1)) < 1e-12,
                "Panorama: detalhes finos deslocaram a camera ou mudaram o FOV.");
        }
        var output = Arqyzeng.Panorama.CubeProjection.OutputSize(faceSize);
        Require(output.Width == 4 * faceSize && output.Height == 2 * faceSize,
            "Panorama: modo de amostragem mudou a resolucao final.");
    }
}
foreach (int invalidSampling in new[] { -1, 0, 3, int.MaxValue })
{
    bool rejected = false;
    try { Arqyzeng.Panorama.CubeProjection.OverscanPixels(2048, invalidSampling); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Require(rejected, "Panorama: fator de amostragem invalido aceito.");
}
Console.WriteLine("Panorama: amostragem 1x/2x, limite de raster e invariancia de camera/dimensoes conferidos.");

// Contrato IA 360 separado: dimensoes reais, preservacao da projecao e fechamento.
foreach (RenderProfile profile in Enum.GetValues<RenderProfile>())
foreach (int width in new[] { 1536, 2048 })
{
    string contract = Arqyzeng.Panorama.PanoramaRenderContract.Build(profile, width, width / 2, rules, null);
    Require(contract.Contains("360 x 180") && contract.Contains("equirectangular") &&
        contract.Contains("SAME meridian") && contract.Contains("zenith and nadir") &&
        contract.Contains($"{width} x {width / 2} pixels"), "Panorama IA: projecao, polos ou tamanho ausentes.");
    Require(contract.Contains("Fidelidade Maxima") && contract.Length <= 30000,
        "Panorama IA: fidelidade ou limite do prompt incorretos.");
}
foreach (var invalid in new[] { (1536, 1024), (2048, 2048), (8192, 4096), (0, 0) })
{
    bool rejected = false;
    try { Arqyzeng.Panorama.PanoramaRenderContract.Validate(invalid.Item1, invalid.Item2, "low"); }
    catch (ArgumentException) { rejected = true; }
    Require(rejected, "Panorama IA: dimensoes fora do contrato aceitas.");
}
bool invalidQualityRejected = false;
try { Arqyzeng.Panorama.PanoramaRenderContract.Validate(1536, 768, "invalid"); }
catch (ArgumentException) { invalidQualityRejected = true; }
Require(invalidQualityRejected, "Panorama IA: qualidade invalida aceita.");
Console.WriteLine("Panorama IA: Dia/Noite, contrato 360, polos, fechamento e resolucoes conferidos; nenhuma chamada paga.");

// Regressao: Panorama nao pode herdar instrucoes de perspectiva do Print.
string panoramaOnly = Arqyzeng.Panorama.PanoramaRenderContract.Build(RenderProfile.BumpDay, 2048, 1024, rules, null, "Porta ao lado do sofa => carvalho fosco.");
Require(!panoramaOnly.Contains("ENQUADRAMENTO VINCULANTE") && !panoramaOnly.Contains("Evite fisheye") &&
    panoramaOnly.Contains("BIM: unavailable") && panoramaOnly.Contains("carvalho fosco") &&
    panoramaOnly.Contains("Do not invent tile joints"), "Panorama: regras de perspectiva ou material incerto incorretas.");
bool descriptionsRejected = false;
try { Arqyzeng.Panorama.PanoramaRenderContract.Build(RenderProfile.BumpDay, 2048, 1024, rules, null, new string('x', 2001)); }
catch (ArgumentException) { descriptionsRejected = true; }
Require(descriptionsRejected, "Panorama: descricao acima do limite aceita.");

// Reparo 360: deslocamento reversivel, mascara central, pixels exteriores preservados.
{
    const int w = 512, h = 256;
    byte[] raster = Enumerable.Range(0, w * h * 4).Select(i => (byte)(i % 251)).ToArray();
    byte[] moved = Arqyzeng.Panorama.PanoramaSeamMath.RotateHalf(raster, w, h);
    Require(Arqyzeng.Panorama.PanoramaSeamMath.RotateHalf(moved, w, h).SequenceEqual(raster), "Reparo: deslocamento nao reversivel.");
    byte[] mask = Arqyzeng.Panorama.PanoramaSeamMath.Mask(w, h);
    byte[] changed = Enumerable.Repeat((byte)255, raster.Length).ToArray();
    byte[] limited = Arqyzeng.Panorama.PanoramaSeamMath.LimitToBand(raster, changed, w, h);
    int half = Arqyzeng.Panorama.PanoramaSeamMath.HalfBand(w);
    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        int offset = (y * w + x) * 4;
        bool inside = x >= w / 2 - half && x < w / 2 + half;
        Require(mask[offset + 3] == (inside ? 0 : 255), "Reparo: mascara invertida.");
        if (!inside) Require(limited.AsSpan(offset, 4).SequenceEqual(raster.AsSpan(offset, 4)), "Reparo: pixel externo alterado.");
    }
    Require(limited[(h / 2 * w + w / 2) * 4] == 255, "Reparo: centro editavel nao aplicado.");
}
bool locationRejected = false;
try { Arqyzeng.Panorama.PanoramaRenderContract.Build(RenderProfile.BumpDay, 2048, 1024, rules, null, "madeira marrom"); }
catch (ArgumentException) { locationRejected = true; }
Require(locationRejected, "Material sem localizacao aceito.");
