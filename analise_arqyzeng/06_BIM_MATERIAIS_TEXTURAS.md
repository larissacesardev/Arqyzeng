# BIM, MaterialLibrary, texturas e correspondência espacial

## Mapa do fluxo

~~~mermaid
flowchart LR
  M[Documento e vista Revit] --> B[BimSceneReader]
  B --> S[BimSceneSnapshot: categorias e materiais candidatos]
  S --> PF[PromptBuilder Print: detalhes agregados]
  S --> PJ[panorama.json: por face]
  PJ --> PC[PanoramaRenderContract: contagem apenas]
  ML[MaterialLibrary 20 categorias] --> PF
  GR[RenderGeneralRules + Day/Night] --> PF
  U[Descrições humanas localizadas] --> PF
  U --> PC
  PC --> IA[IA recebe PNG + prompt]
  PF --> IA
~~~

**Confirmado pelo código.** BimSceneReader.Read usa FilteredElementCollector(document, view.Id) no documento principal, só elementos de categoria Model, limite de 2000 elementos/5 s e 200 materiais distintos; ignora leitura de links e CAD, marca Partial em limites/falhas (BimSceneReader.cs:14-37,40-46,85-99). Para cada elemento consulta GetMaterialIds(false) e GetMaterialIds(true), resolve Material, agrega por id e categoria, conta elementos e marca paint (linhas 47-75). Snapshot tem State, contadores, categorias e BimMaterial com Name, MaterialClass, ColorRgb, Transparency, UseRenderAppearanceForShading, ElementCount, HasPaintAssignment e UsesByCategory (RenderSettings.cs:9-16). O snapshot não guarda ElementId ou MaterialId, apesar de usá-los durante a consulta; não guarda família, tipo, área visível, face, UV ou geometria. A contagem é de candidatos, não de pixels nem superfícies.

**Qualidade dos dados.** BimSceneReader.cs:79-84 diz explicitamente que camadas encobertas podem aparecer, crop local não filtra a consulta, classe/nome são metadados não certificação, cor e transparência são propriedades gráficas, filtros/texturas/assets não analisados, materiais de famílias aninhadas/categoria podem faltar. No Panorama, há snapshot por face de câmera bruta 94°, com margens que não estão na face final (PanoramaCaptureService.cs:214-221,297-300). Os seis snapshots não devem ser somados como inventário único. BimContextFormatter reitera o limite no prompt Print (BimContextFormatter.cs:17-39). 

## MaterialLibrary e regras

MaterialLibrary.json:1-150, versão 1.1, define 20 categorias com nome/id/instruções: madeira, metais, três vidros, acrílico, pedra, pedra composta, concreto, cerâmica, tecido, couro, tapete, água, vegetação, terra, parede/forro, rodapé, plástico e papel. São orientações qualitativas condicionais, não detecção automática. Não há tabela de propriedades físicas calibradas, IOR por material, PBR maps ou fallback de texture lookup. RenderGeneralRules.json:1-28 e BumpDayRules/BumpNightRules são guias textuais. PromptBuilder.cs:23-34 valida os arquivos; :79-89 insere todas as categorias como condicionais e perfil de luz no Print. **PanoramaRenderContract.cs:20-64 não lê nenhum desses JSONs** apesar de receber rulesDirectory; o perfil Dia/Noite é próprio e mais curto. Portanto, “MaterialLibrary usada plenamente” vale para montagem textual do Print, não para reconhecimento de cada material nem para Panorama.

## AppearanceAsset e texturas

A leitura de Material em BimSceneReader.cs:62-66 acessa Name, MaterialClass, Color, Transparency e UseRenderAppearanceForShading. Não há chamada a AppearanceAssetId, AppearanceAssetElement, Asset, UnifiedBitmap, caminho de imagem, scale/offset/rotation UV, bump/normal, tint, glossiness, reflectivity ou physical asset no snapshot. A busca estática dessas APIs nos fontes de runtime não encontrou implementações. **Confirmado pelo código:** o plugin não extrai nem envia bitmap de textura BIM para IA. **Distinção:** quando a vista origem está em Realistic/Textures, o PNG exportado pelo Revit pode conter aparência de textura rasterizada (PanoramaCaptureService.cs:31-45); isso não é leitura semântica do arquivo da textura pelo plugin. A aparência final depende de Revit e modelo, portanto é **não é possível determinar** aqui.

## Material e objeto ↔ pixel

Não há ID render pass, máscara de material, ID de elemento por pixel, mapa de profundidade, ray cast de imagem nem projeção de superfícies BIM implementados. CubeProjection.Map transforma direção→face/UV, mas não informa o elemento que gerou o pixel (CubeProjection.cs:48-70). PanoramaProjectionService.Sample trabalha só BGRA (PanoramaProjectionService.cs:100-113). Assim, nem Print nem Panorama podem afirmar automaticamente que “este pixel é carvalho” ou “esta cadeira corresponde ao elemento X”. Uma descrição humana “local => acabamento” em PanoramaRenderContract.cs:25-53 é localização em linguagem natural, não identificação automática. A seção de objetos do atualizador v9.1 não consta dos fontes atuais; no Panorama v9.0.2 objetos são apenas mencionados genericamente no prompt e não há formulário de identificação obrigatória.

## Confirmações manuais

No Print, a UI lista BimMaterial.Name, classe e categorias e aceita descrição livre não vazia; a chave em memória combina nome, classe e cor. A lista é refeita para a captura; mudanças relevantes invalidam render. O prompt incorpora as descrições e o histórico salva materiais confirmados (PrintWindow.cs:1101-1153; :812-822; PromptBuilder.cs:148-165). Não prova associação visual exata.

No Panorama, a UI aceita até 12 linhas “local da superfície => acabamento”, 2000 caracteres; oferece consulta de candidatos BIM e opcionalmente carrega panorama.json da mesma pasta do PNG, validando schema e dimensões, mas diz ao operador para confirmar associação. Os candidatos são guardados em generation.json quando o usuário salva, enquanto o prompt só leva contagem e descrições humanas (PrintWindow.PanoramaAi.cs:22-28,139-173,259-280; PanoramaRenderContract.cs:25-62). Não há invalidação vinculada a alteração do RVT depois da captura: o snapshot é congelado, e a UI pode importar um PNG distinto na mesma pasta. **Hipótese de risco:** metadados podem ser associados a uma imagem incompatível sem verificação de hash; testar com arquivos controlados.

## Evolução proposta

Construir mapa de IDs de superfície/elemento por face no mesmo frustum exportado; incluir profundidade, material de aparência e atribuições paint/camadas, resolver o pixel após crop/projeção e marcar confiança/oclusão. Validar com um RVT controlado e máscaras de referência; só então incluir no prompt materiais localizados. Essa proposta não é uma função existente.
