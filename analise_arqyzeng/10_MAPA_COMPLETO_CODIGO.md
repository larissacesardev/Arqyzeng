# Mapa completo do código auditado

Cada entrada identifica o papel no **estado atual** dos arquivos. Caminhos sob src/Arqyzeng.Revit/ salvo indicação; linhas aproximadas. “Ativo” quer dizer referenciado no fluxo fonte, não binário comprovado em produção.

| Arquivo / namespace / classe | Método/linhas principais | Papel e estado |
|---|---|---|
| App.cs / Arqyzeng.Revit / App | OnStartup/OnShutdown 5-41 | Ribbon, comandos, fechamento; ativo |
| StartupTestCommand.cs / Arqyzeng.Revit / StartupTestCommand, Availability | Execute 8-33 | Botão de teste de carga; ativo |
| PrintCommand.cs / Arqyzeng.Revit / PrintCommand, PrintHost | Execute 11-34; Show/Shutdown 43-101 | Valida vista, WPF modeless, ExternalEvent, roteamento; ativo |
| PrintCaptureHandler.cs / Arqyzeng.Revit / PrintCaptureHandler | Execute 36-128 | Contexto Revit: Print, Panorama, moldura, preparo; ativo |
| CaptureModels.cs / Arqyzeng.Revit / CaptureSettings, PrintRequest | 5-12 | Tipos de ações e enquadramento; ativo |
| PrintWindow.cs / Arqyzeng.Revit / PrintWindow | construtor 122-179; CreatePanorama 181-251; CreatePrint 377-475; GenerateRenderAsync 708-772; histórico 774-856; materiais 1101-1153 | UI principal Print/Panorama, estado e persistência; ativo |
| PrintWindow.PanoramaAi.cs / Arqyzeng.Revit / PrintWindow partial | CreatePanoramaAiPanel 52-101; Generate 186-247; Save 259-299 | Seleção, prompt, chamada IA Panorama, histórico; ativo PoC |
| PrintWindow.PanoramaSeam.cs / Arqyzeng.Revit / PrintWindow partial | CreatePanel 32-73; Import 74-106; Repair 107-152; SaveEvidence 189-197 | Reparo opcional e importação de geração; ativo experimental |
| CaptureViewPreparation.cs / Arqyzeng.Revit / CaptureViewPreparation | Start 26-83; Stop 145-154 | Cria vista limpa persistente para Print; ativo |
| ViewportGuide.cs / Arqyzeng.Revit / ViewportGuide, GuideSurface, NativeGuide | Start 62-73; OnIdling 131-208; Stop 208-217; Win32 266-279 | Overlay de moldura/interop; ativo |
| ImagePipeline.cs / Arqyzeng.Revit / ImagePipeline | Load 10-20; Render 22-68; PrepareReference 70-75; Save 77-86 | PNG WPF, crop, fit e resize; ativo |
| BimSceneReader.cs / Arqyzeng.Revit / BimSceneReader | Read 24-100; Unavailable 102-104 | Consulta de candidatos BIM da vista, agregados; ativo |
| RenderSettings.cs / Arqyzeng.Core / enums e records | 3-16 | Perfis, fidelidade, snapshot BIM, materiais; ativo |
| BimContextFormatter.cs / Arqyzeng.Rendering / BimContextFormatter | Summary 10-15; Format 17-39 | Serializa contexto BIM no prompt Print; ativo |
| PromptBuilder.cs / Arqyzeng.Rendering / PromptBuilder | Build 17-101; BuildForImageEdit 103-120; confirmações 148-165 | Regras JSON e prompt Print; ativo |
| OpenAiImageProvider.cs / Arqyzeng.Rendering / OpenAiImageProvider | OutputSize 33-41; EditAsync 43-126; ReadPngSize 128-137 | Contrato HTTP compartilhado Print/Panorama/reparo; ativo |
| ImageProviderChecks.cs / Arqyzeng.Rendering / ImageProviderChecks | RunAsync 14-151 | Checks HTTP simulados, não runtime do plugin |
| CubeProjection.cs / Arqyzeng.Panorama / CubeProjection | OutputSize 9-14; Overscan 25-31; CentralFaceRegion 33-45; Map 48-70 | Matemática cubemap; ativo |
| PanoramaCaptureService.cs / Arqyzeng.Revit / PanoramaCaptureService, CameraMeasure, PanoramaCameraContext | Capture 19-315; raster 319-335; Measure 339-356; export context 360-390 | Câmera, seis PNG, metadata e rollback; ativo |
| PanoramaProjectionService.cs / Arqyzeng.Revit / PanoramaProjectionService | Build 23-99; Sample 100-113 | Validação e projeção 2:1; ativo |
| PanoramaViewerMath.cs / Arqyzeng.Panorama / PanoramaViewerMath, PanoramaTextureTile | Direction 7-12; SpherePoint 13-16; TextureTiles 17-30 | Direções/UV/tiling; ativo |
| PanoramaViewerWindow.cs / Arqyzeng.Revit / PanoramaViewerWindow | Load 90-123; UpdateCamera 136-143; textura/malha 156-203 | Viewer esfera WPF; ativo |
| PanoramaRenderContract.cs / Arqyzeng.Panorama / PanoramaRenderContract | Validate 13-19; Build 20-64; Safe 66-67 | Prompt Panorama 360; ativo, rulesDirectory e Safe sem uso interno |
| PanoramaSeamMath.cs / Arqyzeng.Panorama / PanoramaSeamMath | HalfBand/Rotate/Mask/Limit/Prompt 8-57 | Reparo limitado; ativo |
| PluginLog.cs / Arqyzeng.Revit / PluginLog | Write 7-24 | Log técnico local; ativo |
| Class1.cs / Arqyzeng.Revit / Class1 | 1-6 | Classe vazia, sem chamada identificada; resíduo |
| data/MaterialLibrary.json / JSON | 1-150 | 20 categorias, usado por PromptBuilder Print |
| data/RenderGeneralRules.json / JSON | 1-28 | Guia geral Print |
| data/BumpDayRules.json / JSON | todo arquivo | Perfil textual Print Dia |
| data/BumpNightRules.json / JSON | todo arquivo | Perfil textual Print Noite |
| tests/Arqyzeng.Rendering.Checks/Program.cs / top-level | 1-355+ | Checks puros/simulados; não UI/Revit |
| tests/Arqyzeng.Rendering.Checks/*.csproj | 1-20 | Vincula fontes e JSON para checks |
| ArqyzengBIM.sln, global.json, Directory.Build.props | arquivos inteiros | Solution, SDK, outputs |
| scripts/dev.ps1 | 1-165 | Checks/build/install atual; não executado |
| Atualizar_Arqyzeng_Panorama_v9.1.ps1 | cabeçalho e bloco de atualização | Atualizador com payloads; **não aplicado** aos fontes auditados |
| Instalar_Arqyzeng_Panorama_v8.2.ps1, Instalar_Arqyzeng_Primeiro_Plugin.ps1 | scripts inteiros | Legado com cópias de fontes; não executado |
| README.md, .vscode/tasks.json, .gitignore | arquivos inteiros | Documentação/tarefas/ignores; README contém histórico de outro workspace |
| BUMP RENDER.docx, BUMP DIAA.docx, BUMP NOITE.docx | binários não analisados semanticamente | Citados como fontes editoriais dos JSON; não carregados em runtime |

**Artefatos compilados:** a pasta .artifacts não existia na raiz inspecionada. Nenhuma DLL ou manifesto .addin fonte foi encontrada no inventário da raiz; o manifesto é gerado por scripts/dev.ps1:110-125 durante Install. Não foi feita comparação binário↔fonte.

## Conexões ausentes ou latentes

- PanoramaRenderContract.Build recebe rulesDirectory, mas não chama File.ReadAllText nem PromptBuilder; MaterialLibrary não chega ao prompt Panorama (PanoramaRenderContract.cs:20-64).
- PanoramaRenderContract.Safe não é chamado no arquivo (:66-67).
- Class1 não contém função e não aparece no fluxo de botões (Class1.cs:1-6; App.cs:11-25).
- A aba Documentacao só chama Placeholder (PrintWindow.cs:144,485-490).
- O script v9.1 não altera o código em runtime sem execução explícita; não confundir seu payload com classe compilada.

## Entradas de execução e limites

App.OnStartup cria a ribbon; PrintCommand.Execute abre a UI; PrintCaptureHandler.Execute é a ponte API; PrintWindow.GenerateRenderAsync e GeneratePanoramaAiAsync são rede; PanoramaProjectionService.Build processa arquivo local; scripts/dev.ps1 é operação de desenvolvimento/instalação. Sem Revit real ou binário implantado, a cadeia de execução é conclusão sobre fontes, não telemetria.
