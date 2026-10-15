# Scene Scan Engine — Fase 1

## A. Auditoria antes da implementação

`PrintCommand` abre uma janela modeless. `PrintHost` guarda um handler, um
ExternalEvent e o documento esperado; os cliques enfileiram DTOs `PrintRequest`.
`PrintCaptureHandler.Execute` valida documento e View3D no contexto da API,
exporta a região visível em PNG, chama `BimSceneReader` e entrega `CaptureResult`.
Falha BIM já é isolada da captura. Não existe leitura de geometria por face.

`BimSceneReader` faz um collector limitado à view (2.000 elementos/5 segundos),
agrega categorias, materiais e pintura por `GetMaterialIds`, até 200 materiais.
`BimMaterial` contém nome/classe, RGB gráfico, transparência, uso da aparência
para sombreamento e contagens. Não contém IDs, famílias, câmera, profundidade,
superfícies ou assets. Candidatos não são uma prova de visibilidade.

`BimSceneSnapshot`, em `RenderSettings.cs`, contém apenas tipos .NET, mas usa
arrays mutáveis. `BimContextFormatter` é usado pelo PromptBuilder de produção;
alterá-lo afetaria a IA. Estes contratos permanecem intactos nesta fase.

`PanoramaCaptureService` executa sincronamente no mesmo ExternalEvent, duplica
temporariamente a vista, orienta seis faces e consulta BIM em cada orientação.
Já existe um CustomExporter que lê CameraInfo e retorna Skip no OnViewBegin;
ele calibra FOV/offsets, sem exportar meshes. Não será refatorado nesta fase para
não alterar o Panorama. A mesma técnica de leitura da câmera poderá ser usada
isoladamente pelo Scene Scan, incluindo projeção ortográfica e offsets.

Objetos Revit persistentes encontrados: ExpectedDocument no handler; UIApplication,
Document, View3D/UIView e XYZ na preparação/guia, utilizados em ExternalEvent,
Idling e encerramento. Esses objetos ficam na infraestrutura Revit; não foram
encontrados no RenderScene, HTTP ou DTOs entregues à UI. O Task.Run atual monta
panorama a partir de arquivos e matemática pura. A UI/HTTP trabalham com dados
BIM puros e imagens WPF. Nenhum desses mecanismos será reestruturado aqui.

Ponto de integração escolhido: depois do PNG e do levantamento BIM existente,
antes de construir CaptureResult. Uma propriedade opcional adicional preserva
o construtor posicional e os consumidores atuais. O scan não alimenta RenderScene,
PromptBuilder ou Panorama. O novo núcleo matemático não depende da UI Print.

## Decisões de projeto

- DTOs próprios para dados espaciais, usando records e ImmutableArray. Reutilizar
  o DTO agregado atual faria a fronteira da IA mudar; manter os dois explícitos.
- Grade regular 32 × 18 (576 raios), máximo configurável até 1.024. Refinamento
  adaptativo fica desabilitado e reservado para uma subfase, sem amostras extras.
- Collector da view uma vez por scan; inventário limitado e enriquecimento dos
  elementos efetivamente atingidos. Nunca filtrar o intersector só pelos primeiros
  candidatos, pois isso permitiria atravessar oclusores omitidos do inventário.
- Links não suportados nesta fase, declarados nos capabilities. Referências de
  links e colisões com geometria vinculada não devem virar semântica inventada.
- Somente resumos de geometria, sem triangulação. Cache local ao scan, sem cache
  global de Document/Element/Face. Falhas parciais têm estado e contadores próprios.
- CameraInfo representa a projeção da View3D; VisibleRegionOfCurrentView pode
  incluir zoom/pan e a imagem ainda sofre Fit/Crop localmente. Guardar esse limite
  explicitamente: coordenadas da grade não são segmentação nem alinhamento
  garantido ao PNG final. UIView.GetZoomCorners será registrado quando disponível.
- API local auditada: RevitAPI.xml/RevitAPIUI.xml da instalação 2026, incluindo
  CreationGUID, CameraInfo, ReferenceIntersector, Face, GetPaintedMaterial,
  GetRenderingAsset, Generic e UnifiedBitmap. Valores não disponíveis ficam null;
  nenhum schema privado ou nome de propriedade de asset será adivinhado.

Referências consultadas: [CameraInfo](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/facf52cc-bc82-0008-9e4c-60e6a335ef40.htm),
[ray projection](https://help.autodesk.com/cloudhelp/2025/DEU/Revit-API/files/Revit_API_Developers_Guide/Revit_Geometric_Elements/Geometry/Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Geometry_Finding_geometry_by_ray_projection_html.html)
e [câmera versus viewport](https://aps.autodesk.com/blog/camera-mapping-between-aps-viewer-and-revit-part-ii-restore-revit-camera-viewer).

## B. Arquitetura implementada

| Componente | Responsabilidade |
| --- | --- |
| `SceneScanModels.cs` | Records, enums, configuração e coleções imutáveis, sem Revit/WPF |
| `SceneCameraMath.cs` | Bases, world/camera, projeção, raios, grade, identidade, cobertura e métricas puras |
| `SceneScanSerializer.cs` | JSON indentado e diagnóstico opt-in com publicação após escrita completa |
| `SceneScanCameraReader.cs` | Orientação, projeção pública CameraInfo, crop/section box e conversão central de unidades |
| `SceneScanElementReader.cs` | Categoria/família/tipo/host, bounding box transformado, parâmetros opcionais e resumo de geometria limitado |
| `SceneScanMaterialReader.cs` | Materiais e pequeno conjunto público de propriedades de aparência, cache por scan |
| `SceneScanEngine.cs` | Orquestração síncrona, collector, intersector, superfícies, limites e estatísticas |

Dois fontes existentes foram alterados nesta fase: `CaptureModels.cs` recebe a
propriedade opcional `SceneScan` (construtor e deconstruction preservados) e
`PrintCaptureHandler.cs` executa o enriquecimento isolado. Nenhum outro fonte
existente mudou em relação ao início desta tarefa, conforme comparação SHA-256.
Os demais arquivos modificados no Git pertencem às rodadas anteriores.

```text
ExternalEvent.Execute
  → exportação PNG existente
  → BimSceneReader existente
  → SceneScanEngine.Scan (síncrono; somente leitura)
      → SceneScanSnapshot puro/imutável
      → diagnóstico somente se habilitado
  → CaptureResult { Bim existente, SceneScan opcional }
  → ReceiveCapture existente
```

O consumidor atual não usa SceneScan para render, UI ou histórico. A próxima fase
pode consumir a propriedade entregue no CaptureResult sem reter objetos Revit.
Não existe cache global da cena. Panorama poderá reutilizar câmera/configuração
e identidades por documento, mas inventário global compartilhado entre seis scans
fica para a próxima fase; não há promessa de cache multivista já implementado.

## C. Scan e agregação

O inventário usa um único `FilteredElementCollector(document, view.Id)`, sem tipos,
apenas categoria Model, sem elementos transitórios/ocultos explicitamente. Limite
padrão de 500 candidatos (configurável até 1.000). Categorias nulas não entram no
inventário, mas um elemento atingido pode ser registrado com categoria null.
IDs de elementos atingidos não presentes no inventário são enriquecidos também.
Bounding boxes ausentes não eliminam um hit nem inventam posições.

`ReferenceIntersector(view)` usa alvo Face, links desabilitados e `FindNearest`.
Um intersector serve toda a grade. O scan não é restringido aos primeiros IDs do
collector. A grade amostra centros de células, padrão 32 × 18 = 576 raios. Cada
eixo aceita 1–64, com produto limitado por MaxRayCount e teto absoluto de 1.024;
configurações inválidas são rejeitadas. AdaptiveSampling=true é rejeitado
explicitamente, evitando uma opção aparentemente funcional.

Miss é uma amostra executada sem superfície válida no intervalo de profundidade;
não contém elemento/ponto/distância inventados. Falha da API é `Unavailable`,
separada de Miss. Amostras não executadas por orçamento/cancelamento não são
registradas como misses: PlannedRayCount e RayCount permitem diferenciá-las.

Por elemento: contagem de hits, fração dos raios **executados**, mínimo/média/máximo
da distância ao olho, limites e centro médio das coordenadas amostradas. Isso não
é área projetada exata. Um scan parcial interrompido por tempo pode ter viés
espacial pela ordem da grade; não extrapolar sua cobertura para a tela inteira.
Não há labels inferidos como “geladeira”: Category/Family/Type são copiados do BIM.

## D. Câmera, espaços e unidades

Origem: `View3D.GetOrientation().EyePosition`. Forward e Up são normalizados;
Right = Forward × Up e Up é reortogonalizado. Camera space explícito: X direita,
Y cima, Z para frente. Não confundir com o eixo Z do transform nativo de uma vista.
World space: coordenadas internas do documento host convertidas para metros,
sem transformação para coordenadas compartilhadas/topográficas.

CameraInfo fornece largura/altura do plano, offsets, near/far e distância do
plano alvo. FOV só existe para perspectiva; leva em conta offsets assimétricos.
Raios de perspectiva divergem do olho e começam no plano near. Raios ortográficos
são paralelos, com origens distribuídas nesse plano; TargetDistance ortográfico
é ignorado. Profundidade camera-Z, distância euclidiana ao olho e distância da
origem do raio são campos diferentes. O “olho” ortográfico não é uma câmera física.

Distâncias usam `UnitUtils`/`UnitTypeId.Meters`; áreas usam SquareMeters. Vetores
direção são adimensionais, FOV em graus. Boxes de corte preservam min/max locais
e transform local→host. Bounding boxes de elementos viram AABB host pelos oito
cantos transformados, com centro apenas aproximado. As coordenadas normalizadas
têm origem no topo esquerdo, X para direita e Y para baixo.

`UIView.GetZoomCorners` é registrado como metadado opcional. **Não foi implementada
calibração entre ViewProjection, VisibleRegionOfCurrentView e o PNG Fit/Crop final.**
`RasterAlignmentVerified=false` e Camera.RasterAlignment documentam isso. O scan
é espacial da vista, não uma máscara pronta para anexar ao render. Se a projeção
não puder ser obtida, o snapshot fica parcial, sem raios e com FOV null.

## E. Superfícies, geometria e materiais

A referência do hit é resolvida por `GetGeometryObjectFromReference`. A superfície
tem chave derivada da referência estável, tratada como string opaca, sem interpretar
sua estrutura privada. Material base vem de `Face.MaterialElementId`; pintura é
consultada via `Document.GetPaintedMaterial` e prevalece quando disponível. Falha
de pintura é declarada (`FaceMaterial_PaintUnavailable`), não tratada como prova
de ausência. Material inválido, ausente ou limitado permanece null/parcial.

A normal usa `Face.ComputeNormal` e `ReferenceWithContext.GetInstanceTransform`.
Antes de publicar, o ponto UV transformado é confrontado com o ponto do raio
(tolerância de 1 mm). Se houver ambiguidade, inclusive em famílias aninhadas,
a normal permanece null. Isso evita transformar cegamente uma face local como
se estivesse em world space. Áreas consideram a escala conformal da instância.

`Material` fornece ID/UniqueId/nome/classe, RGB gráfico, transparência e política
de sombreamento. Esses dados não representam overrides nem textura renderizada.
O cache de materiais guarda DTOs e dura apenas o scan, com máximo padrão 128.

AppearanceAssetId/AppearanceAssetElement/GetRenderingAsset foram conferidos na
API local. São lidas apenas propriedades públicas documentadas de Generic:
GenericDiffuse (RGBA, quando quatro canais finitos), GenericTransparency e
GenericGlossiness. Uma conexão UnifiedBitmap permite registrar a **presença**
de referência de textura; nenhum caminho ou arquivo de textura é exportado/lido.
Assets vazios ficam Unavailable; shaders diferentes ou propriedades ausentes têm
campos null. Não se carrega toda a biblioteca de assets para preencher lacunas.
Não existe AppearanceAssetEditScope, transação ou alteração de material.

Resumo geométrico completo é opcional e desligado por padrão. Quando ativado,
apenas elementos atingidos são percorridos, uma vez por elemento, no máximo 32
(teto 64), com 256 objetos por elemento (teto 512) e profundidade 4 (teto 8).
GeometryInstance é percorrida por GetInstanceGeometry, sem meshes serializados
ou triangulação. Boxes e áreas de faces continuam disponíveis no modo padrão.
Parâmetros opcionais estão desligados: somente Manufacturer e Model do tipo,
limitados a 256 caracteres; Mark, Comments e enumeração indiscriminada não existem.

## F. Revit Links

**Não implementados.** `LinksSupported=false`; IncludeLinkedModels=true é
rejeitado. DTOs reservam documento de origem, UniqueId da instância e LinkToHost.
Element keys incluem documento + UniqueId, com campo de instância previsto para
links. O documento host usa CreationGUID, não caminho/nome. Uma cópia de arquivo
que conserve CreationGUID não ganha identidade distinta automaticamente: futura
federação de cópias precisa definir sua política de identidade.

Como links não participam do intersector, um hit host pode estar visualmente
encoberto por um link. O snapshot declara essa limitação; não o utilizar como
prova de oclusão completa numa cena federada. Subfase sugerida: referências de
links, documentos descarregados, transforms aninhados e validação de oclusão.

## G. Performance, métricas e robustez

Stopwatch mede câmera, inventário, raios (incluindo leitura de superfície/material
e geometria opcional), agregação e total. A gravação JSON fica fora do total.
Há contagens de candidatos processados, elementos/hits/misses/falhas, links
ignorados, materiais, resumos geométricos e limites atingidos. PluginLog recebe
um resumo numérico sem nomes de projeto/vista/material ou linhas por raio.

O trabalho gerenciado cresce com candidatos limitados + grade limitada + geometria
opcional limitada. O custo interno de collector, CustomExporter e intersector
depende do Revit/modelo e não tem garantia linear ou tempo fixo. O orçamento
padrão é 3.000 ms (configurável 1–10.000 ms), consultado entre etapas/elementos/
raios. **Uma chamada nativa não é interrompida e pode ultrapassar o orçamento.**
O custo existente de exportação PNG e BIM agregado continua separado.

CancellationToken é cooperativo; não usa outra thread para chamar a API nem
interrompe uma consulta nativa. Erros individuais produzem dados parciais; falha
global retorna ao handler, que mantém a captura e SceneScan=null. Falha de gravação
do diagnóstico também não elimina o snapshot. Tempos reais em RVT ainda não
foram medidos nesta entrega; devem ser registrados no roteiro abaixo.

## H. Thread safety

Toda leitura Revit nova ocorre sincronamente dentro de PrintCaptureHandler.Execute.
O engine tem guarda contra thread pool/MTA antes de consultar a API; ela não
detecta sozinha um contexto Revit válido em qualquer thread STA. O contrato
interno exige ExternalEvent/ExternalCommand/Idling e há somente um call site de
produção nesta fase, no ExternalEvent. Não há Task.Run, async, Dispatcher ou HTTP
no scan. Helpers retornam DTOs; objetos nativos de câmera/geometria/intersector
ficam no escopo síncrono. Nenhum Document, Element, XYZ, Face, Reference, Solid,
Material ou View3D integra o grafo serializável.

Records e ImmutableArray fazem o snapshot independente do documento aberto.
Um teste recursivo do grafo de propriedades rejeita tipos Autodesk e objetos
opacos/delegates; os testes puros compilam sem referências Revit ou WPF.

## I. Diagnóstico

Fechar o Revit antes de habilitar a variável, para o processo herdá-la:

```powershell
$env:ARQYZENG_SCENE_SCAN_DEBUG = '1'
Start-Process 'C:\Program Files\Autodesk\Revit 2026\Revit.exe'
```

Somente o valor exato `1` habilita gravação. Sem ele, nem a pasta é criada pelo
serializer. O engine ainda executa como enriquecimento da captura normal.

```text
%LOCALAPPDATA%\ArqyzengBIM\Diagnostics\SceneScan\
scene-scan-AAAAMMDD-HHMMSS-mmm-identificador.json
```

JSON indentado, sem imagens, binários, chave OpenAI, caminhos do projeto/texturas
ou todos os parâmetros. Contém nomes/identificadores BIM solicitados. Primeiro
é escrito um arquivo `.pending`; só uma escrita completa publica o `.json`.
Para desabilitar, remover a variável e reiniciar o Revit. Nenhuma variável do
usuário foi configurada automaticamente por esta implementação.

Estrutura resumida (não é resultado medido no Revit):

```json
{
  "schemaVersion": 1,
  "document": { "key": "creation-guid", "source": "Document.CreationGUID_NoPath" },
  "view": { "elementId": 123, "name": "Vista de teste" },
  "camera": {
    "isPerspective": true,
    "originWorldMeters": { "x": 0, "y": 0, "z": 1.6 },
    "rasterAlignment": "ViewProjectionOnly_VisibleRegionAndFinalImageCropNotCalibrated"
  },
  "elements": [],
  "surfaces": [],
  "materials": [],
  "rayHits": [],
  "statistics": { "plannedRayCount": 576, "rayCount": 0 },
  "capabilities": { "linksSupported": false, "rasterAlignmentVerified": false }
}
```

Uma fixture completa e sintética é gerada pelos testes em
`.artifacts/scene-scan-checks/synthetic-scene-scan.json`.

## J. Testes e roteiro dentro do Revit

Teste puro reproduzível, sem dependências externas e sem modificar os csproj:

```powershell
.\scripts\scene-scan-checks.ps1
```

O script gera apenas um projeto temporário em `.artifacts`, compila os fontes
puros atuais e `tests/SceneScanChecks.cs`, tratando warnings como erros.
Resultado: **5.337 asserções aprovadas**. Cobertura: bases ortonormais, translação/
rotação, objetos à esquerda/direita a profundidades 2/5 m, perspectiva/ortográfica,
round trips de 1.728 amostras, offsets, near plane, FOV, limites, IDs, agregação,
miss versus falha, JSON/nulls/capabilities, grafo puro e compatibilidade CaptureResult.

Checks existentes de prompts/HTTP simulado/Panorama: aprovados em Release.
Smoke WPF existente: aprovado (handle, scroll, selects, teclado, modal e abas),
sem iniciar Revit. Nenhuma solicitação paga foi feita.

**Validação real no Revit ainda pendente.** Usar um modelo controlado com quatro
paredes, piso, teto, porta, janela, mesa, cadeira, armário e geladeira/equipamento;
atribuir materiais distintos e nomes de família/tipo conhecidos. Registrar versão
Revit 2026.5, dimensões do ambiente, posição da câmera e tempo de cada etapa.

1. Habilitar o diagnóstico, abrir uma perspectiva interna e capturar Print.
   Confirmar que a imagem e o BIM antigos continuam disponíveis mesmo sem gerar IA.
2. Abrir o JSON. Conferir view/document identity e orientação com a vista; medir
   distâncias conhecidas no Revit e confrontar depth versus distância euclidiana.
3. Conferir CandidateCount, RayCount, RayHits, RayMisses e HitElementCount.
   RayCount ≤ 576 no padrão, e hit+miss+failure+unsupported = RayCount.
4. Confrontar Category/Family/Type/UniqueId com Propriedades. Um Generic Model
   deve continuar genérico, mesmo que visualmente pareça geladeira.
5. Conferir a normal/material de uma parede plana e de uma família rotacionada.
   Testar material base, face pintada, face sem material e asset vazio. Normais
   não verificadas devem ser null; textura referenciada não significa arquivo lido.
6. Colocar um objeto atrás de uma parede: pode continuar candidato, mas não deve
   ganhar RayHit nas amostras bloqueadas. Ocultar o objeto e ativar section box;
   comparar com o estado anterior. Testar a opção de projeto visível na vista.
7. Repetir em ortográfica, perspectiva com crop deslocado, zoom/pan da viewport
   e Fit/Crop do Print. O limite RasterAlignmentVerified=false deve permanecer;
   não interpretar a grade como correspondência pixel a pixel ao PNG.
8. Testar vista vazia, categoria/bounding box ausente e famílias aninhadas.
   Esperar campos null/misses/Partial, sem impedir a captura. Os casos sem material
   não podem ser preenchidos por palpite de cor/nome.
9. Inserir um link: verificar capabilities=false e notas de oclusão incompleta.
   Não esperar materiais/elementos do link ou transforms preenchidos nesta fase.
10. Em cena grande, registrar totais e flags de limite. Tempo pode ultrapassar
    3 s por chamada nativa; medir antes de aumentar limites. Para testar geometria
    detalhada/cancelamento, um comando de desenvolvimento deve passar opções/token
    ao engine dentro de um ExternalEvent; não chamar Scan pelo console/thread pool.
11. Desabilitar a variável/reiniciar: uma nova captura não deve criar JSON.
    Com diagnóstico habilitado e pasta sem permissão de escrita, a captura deve
    continuar. Falha global de scan deve deixar SceneScan=null e preservar o PNG.
12. Reabrir a mesma vista/outra direção sem modificar elementos: comparar keys.
    Ler o JSON após fechar o documento para confirmar que é autocontido.

Guardar JSONs, screenshots da vista e tabela de tempos como evidência; não
marcar integrações/câmera/materiais como validados em RVT só pelo build.

## K. Build

`dotnet build .\ArqyzengBIM.sln -c Release`: **0 erros, 2 warnings de referências
Autodesk/.NET já conhecidos**. Nenhum warning novo do Scene Scan. global.json,
todos os csproj existentes e o target Revit 2026.5/.NET 10 foram preservados.

## L. Alterações funcionais

- A IA e o comportamento atual de render foram alterados? **NÃO.**
- O Panorama atual foi alterado? **NÃO.**
- A UI foi alterada? **NÃO.**
- Mudança funcional desta fase: captura Print ganha um scan síncrono limitado,
  snapshot opcional e diagnóstico explicitamente habilitado. Há custo adicional
  de leitura, instrumentado. Nenhuma escrita no modelo, novo painel ou envio do
  snapshot para OpenAI. Nenhum push ou operação Git destrutiva.
