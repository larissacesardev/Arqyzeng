# Mapa de evidências e respostas verificáveis

**Classificações exigidas:** CONFIRMADO PELO CÓDIGO (C), CONFIRMADO POR TESTE EXISTENTE (T: teste explícito, não executado nesta auditoria), FORTEMENTE INFERIDO (I), HIPÓTESE (H) e NÃO É POSSÍVEL DETERMINAR (ND). Arquivos relativos à raiz; src abreviado como src/Arqyzeng.Revit. Toda conclusão importante remete ao método e linhas.

## Evidências centrais

| Tema | Namespace / classe / método | Arquivo e linhas | Evidência → conclusão | Classe |
|---|---|---|---|---|
| Ribbon | Arqyzeng.Revit.App.OnStartup | App.cs:5-25 | Cria tab/painel e dois botões | C |
| Ponte Revit | Arqyzeng.Revit.PrintHost.Show / PrintCaptureHandler.Execute | PrintCommand.cs:43-79; PrintCaptureHandler.cs:36-79 | ExternalEvent executa captura em contexto Revit | C |
| Captura Print | PrintCaptureHandler.Execute | PrintCaptureHandler.cs:95-121 | VisibleRegionOfCurrentView PNG, PixelSize até 8192, BIM | C |
| Prompt Print | Arqyzeng.Rendering.PromptBuilder.Build | PromptBuilder.cs:17-100 | Lê três JSONs aplicáveis + biblioteca | C/T texto |
| Seis faces | Arqyzeng.Revit.PanoramaCaptureService.Capture | PanoramaCaptureService.cs:52-60,154-274 | Mesmo eye, direções ortogonais, câmera calibrada | C/T matemática; ND visual |
| Overscan | Arqyzeng.Panorama.CubeProjection.OverscanPixels / CentralFaceRegion | CubeProjection.cs:22-45 | 94° bruto, centro 90°, s=1/2 | C/T |
| Projeção | Arqyzeng.Revit.PanoramaProjectionService.Build / Sample | PanoramaProjectionService.cs:23-113 | Direção equiretangular→face; bilinear local | C/T só mapa |
| Viewer | Arqyzeng.Revit.PanoramaViewerWindow.LoadPanorama | PanoramaViewerWindow.cs:95-203 | Esfera WPF segmentada | C/T só UV |
| IA Panorama | Arqyzeng.Panorama.PanoramaRenderContract.Build | PanoramaRenderContract.cs:20-64 | Prompt e contagem BIM, sem JSONs | C/T texto |
| Serviço IA | Arqyzeng.Rendering.OpenAiImageProvider.EditAsync | OpenAiImageProvider.cs:43-125 | Multipart de edição, validações e uma tentativa | C/T HTTP simulado |
| BIM | Arqyzeng.Revit.BimSceneReader.Read | BimSceneReader.cs:24-99 | Material agregado, sem pixel/asset | C |
| Seam | Arqyzeng.Panorama.PanoramaSeamMath.RotateHalf/Mask/LimitToBand | PanoramaSeamMath.cs:8-57 | Faixa 30°, cópia externa exata | C/T matemática |
| Histórico | Arqyzeng.Revit.PrintWindow.SaveGenerationToHistory / SavePanoramaAiGeneration | PrintWindow.cs:795-856; PrintWindow.PanoramaAi.cs:259-299 | Pastas locais e schemas diferentes | C |
| Visual real | export e IA em execução | não há artefato de ensaio no clone | Alinhamento, nitidez e custo não demonstrados | ND |

~~~mermaid
flowchart LR
  A[render IA com seam] --> B[RotateHalf 180 graus]
  B --> C[Mask 30 graus no centro]
  C --> D[EditAsync high: chamada paga]
  D --> E[LimitToBand: fora da faixa copia original]
  E --> F[RotateHalf de volta]
  F --> G[usuário vê candidato 360]
  G -->|aplica| H[salvar nova geração + evidências]
  G -->|descarta| I[render anterior preservado]
~~~

Diagrama baseado em PrintWindow.PanoramaSeam.cs:32-70,107-140,189-197 e PanoramaSeamMath.cs:8-57. Não existe detector automático de seam ou prova de que o resultado reparado seja geometricamente correto.

## 33 respostas solicitadas

| Nº | Resposta objetiva | Evidência e classe |
|---:|---|---|
| 1 | Plugin C#/WPF para Revit 2026, UI + ExternalEvent + serviços de captura/projeção + provider HTTP + arquivos locais. | App.cs:5-41; PrintCommand.cs:43-79; C |
| 2 | Print exporta vista ativa, faz fit/crop, constrói prompt JSON/BIM e envia referência à IA opcional. | PrintCaptureHandler.cs:95-121; PrintWindow.cs:708-753; C |
| 3 | Panorama captura seis faces, projeta 2:1, visualiza e opcionalmente edita por IA. | PanoramaCaptureService.cs:154-305; PanoramaProjectionService.cs:23-99; C |
| 4 | Direções/UV são matematicamente consistentes nos métodos e checks; correção da exportação real permanece indeterminada. | CubeProjection.cs:48-70; Program.cs:123-152; C/T/ND |
| 5 | 94° horizontal/vertical medidos no bruto, 90° final calculado. | PanoramaCaptureService.cs:163-184,251-274; C |
| 6 | Sim. | CubeProjection.cs:22-30; C |
| 7 | ceil(N·s·tan47°) bruto, região central contínua de 90°, reamostra N×N. | CubeProjection.cs:25-45; PanoramaCaptureService.cs:319-335; C/T |
| 8 | Sim, opção “Detalhes finos”. | PrintWindow.cs:27-30,201-203,237-238; C |
| 9 | s=2 por eixo, ~4× pixels brutos, mesmo FOV e mesmo tamanho final; WPF HighQuality reduz. | CubeProjection.cs:25-45; PanoramaCaptureService.cs:319-335; C/T |
| 10 | Modo alto: face final 2048²; bruto até 4393² no 2×, sujeito à tolerância de export. | CubeProjection.cs:25-30; C |
| 11 | 8192×4096. | CubeProjection.cs:9-14; C/T |
| 12 | 1536×768 ou 2048×1024 para Panorama. | PanoramaRenderContract.cs:13-18; PanoramaAi.cs:124-129,199-203; C |
| 13 | Exige retorno exatamente no tamanho solicitado, 1536×768 ou 2048×1024. | PanoramaAi.cs:218-222; C |
| 14 | Rasterização/crop, bilinear e sobretudo redução 8K→2K podem tirar detalhe; contribuição real de cada etapa requer medição. | ImagePipeline.cs:22-68; PanoramaProjectionService.cs:100-113; PanoramaAi.cs:199-203; C/I/ND |
| 15 | Constante gpt-image-2.5-sunburst; disponibilidade remota não verificada. | OpenAiImageProvider.cs:27-29; C/ND |
| 16 | low/medium/high; não xhigh/max. | OpenAiImageProvider.cs:49-55; PanoramaAi.cs:65-67; C |
| 17 | Sim, texto das 20 categorias inserido no prompt Print. | PromptBuilder.cs:23-34,82-86; C/T |
| 18 | Não; rulesDirectory recebido mas ignorado. | PanoramaRenderContract.cs:20-64; C |
| 19 | Contagens de candidatos por categoria/material, nome, classe, cor gráfica, transparência, flag de shading, paint e notas. | RenderSettings.cs:9-16; BimSceneReader.cs:47-99; C |
| 20 | Print: snapshot formatado + confirmações; Panorama: só contagem no prompt + descrições humanas e PNG. | BimContextFormatter.cs:17-39; PanoramaRenderContract.cs:52-62; C |
| 21 | Não no snapshot. | BimSceneReader.cs:62-66,82; C |
| 22 | Arquivos/paths/UV de textura não lidos; textura raster pode aparecer no PNG Revit, a depender da vista. | BimSceneReader.cs:82; PanoramaCaptureService.cs:31-45; C/I |
| 23 | Não. | BimSceneReader.cs:10-11; PanoramaProjectionService.cs:100-113; C |
| 24 | Não. | BimSceneReader.cs:47-75 e RenderSettings.cs:11-16 agregam sem manter IDs; C |
| 25 | Não há identificação estruturada de objetos no Panorama v9.0.2; prompt menciona objetos genericamente. | PanoramaRenderContract.cs:42-43; PanoramaAi.cs:22-50; C |
| 26 | Rotação 180°, máscara central 30°, IA high, composição limitada, aceitação humana e evidências. | PrintWindow.PanoramaSeam.cs:107-140; PanoramaSeamMath.cs:8-57; C/T matemática |
| 27 | Print e Panorama salvam manualmente em pastas locais com JSON; arquivos e esquemas diferem. | PrintWindow.cs:774-856; PanoramaAi.cs:259-299; C |
| 28 | Código não persiste a API key; PasswordBox é limpo ao fechar. | PrintWindow.cs:175,716-740; PanoramaAi.cs:320-324; C |
| 29 | Testes de prompts, provider simulado, cubemap, FOV/overscan, viewer UV, seam math; sem integração Revit/WPF visual. | tests/.../Program.cs:1-355; tests/...csproj:8-18; T/ND |
| 30 | Dez gargalos ranqueados em 01_RESUMO_EXECUTIVO.md e 09_GARGALOS_E_OPORTUNIDADES.md. | Evidências por item; C/I |
| 31 | Dez forças ranqueadas em 01_RESUMO_EXECUTIVO.md. | Evidências por item; C |
| 32 | Print tem biblioteca JSON aplicada, prompt BIM detalhado, confirmação por material, três fidelidades e histórico de navegação; Panorama tem texto próprio/contagem e descrições livres, sem equivalentes completos. | PromptBuilder.cs:23-100; PanoramaRenderContract.cs:20-64; PrintWindow.cs:917-1030; C |
| 33 | Maior impacto: correspondência material/superfície↔pixel validada em RVT, seguida de prompt localizado e QC visual. É recomendação, não implementação. | BimSceneReader.cs:10-11; PanoramaRenderContract.cs:54-62; proposta |

## Evidência negativa e cautela

“Nenhuma chamada encontrada” foi verificada no conjunto de arquivos src listado em 10_MAPA_COMPLETO_CODIGO.md; não prova ausência em um binário externo ou versão futura. A qualidade visual, input fidelity efetivo da API, sucesso de build, comportamento da câmera Revit e custo são **não é possível determinar** neste escopo.
