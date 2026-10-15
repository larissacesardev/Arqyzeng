# Arquitetura, bootstrap e ciclo de vida

## Estado do repositório

| Item | Evidência | Conclusão |
|---|---|---|
| Solution | ArqyzengBIM.sln | Um projeto de plugin; configurações Debug/Release; checks fora da solution |
| Plugin | src/Arqyzeng.Revit/Arqyzeng.Revit.csproj:1-31 | net8.0-windows, x64, WPF, referências RevitAPI/RevitAPIUI 2026, quatro JSON copiados ao output |
| Checks | tests/Arqyzeng.Rendering.Checks/Arqyzeng.Rendering.Checks.csproj:1-21 | Executável net8.0 que vincula fontes sem dependência Revit |
| SDK | global.json:1-6 | 8.0.425, latestPatch |
| Artefatos | Directory.Build.props:1-6 | bin/obj sob .artifacts |
| Instalação | scripts/dev.ps1:1-165 | Checks→Build→Install; manifesto .addin criado na instalação, sem manifesto fonte no clone |
| Compatibilidade | scripts/dev.ps1:49-64 | Revit 2026 anterior a 2026.5, condicionado à versão local |
| Guias | src/Arqyzeng.Revit/data/*.json; BUMP *.docx | JSON consumidos pelo Print; DOCX indicados como origem editorial, não lidos em runtime |
| Legado | README.md:74-76; Instalar_*.ps1 | Instaladores embutem fontes antigas e podem sobrescrever src |
| Atualização | Atualizar_Arqyzeng_Panorama_v9.1.ps1 | Script de atualização não aplicado aos arquivos src auditados |

Não havia diretório .artifacts nem DLL/manifesto compilado no clone inspecionado; a versão do binário instalado é **não é possível determinar** por esta leitura. A informação sobre Revit 2023 no README.md:14 é histórica; o projeto aponta para Revit 2026. Não foi inspecionado binário instalado. Não há prova estática de que o ambiente de produção execute estes mesmos fontes.

## Componentes

~~~mermaid
flowchart LR
  R[Revit 2026] --> A[App / Ribbon]
  A --> C[PrintCommand]
  C --> H[PrintHost + ExternalEvent]
  H --> W[PrintWindow WPF]
  H --> E[PrintCaptureHandler]
  E --> P[Print PNG e BIM]
  E --> PC[PanoramaCaptureService]
  PC --> F[seis faces + panorama.json]
  F --> PP[PanoramaProjectionService]
  PP --> V[PanoramaViewerWindow WPF]
  W --> PB[PromptBuilder Print]
  W --> PR[PanoramaRenderContract]
  PB --> O[OpenAiImageProvider]
  PR --> O
  O --> HI[Histórico local]
  W --> SR[PanoramaSeamMath + reparo opcional]
~~~

**Confirmado pelo código.** App.OnStartup registra aba Arqyzeng BIM, painel MVP e botões “Testar plugin” e “Abrir Print”; OnShutdown chama PrintHost.Shutdown (App.cs:5-41). Não há botão Panorama separado: a aba fica dentro de PrintWindow (PrintWindow.cs:141-145). PrintCommand valida projeto/vista 3D e abre janela modeless; PrintHost cria ExternalEvent e liga ações de captura, moldura, preparo e Panorama (PrintCommand.cs:8-102). PrintCaptureHandler.Execute valida o documento, a vista e despacha a ação no contexto da API (PrintCaptureHandler.cs:36-83). Rede e prompt atuam na WPF fora da captura API (PrintWindow.PanoramaAi.cs:19,186-247).

~~~mermaid
sequenceDiagram
  participant Rev as Revit
  participant App as App
  participant Cmd as PrintCommand
  participant UI as PrintWindow
  participant Ev as ExternalEvent
  participant H as PrintCaptureHandler
  Rev->>App: OnStartup
  App->>Rev: criar ribbon e botões
  Rev->>Cmd: Execute Abrir Print
  Cmd->>UI: PrintHost.Show
  UI->>Ev: Raise pedido
  Ev->>H: Execute no contexto API
  H->>Rev: ler vista/exportar
  H-->>UI: resultado
  Rev->>App: OnShutdown
  App->>UI: PrintHost.Shutdown
~~~

## Fronteiras e estado

- **UI:** PrintWindow.cs e duas classes partial de Panorama; bitmaps e senhas vivem em campos da janela. Fechamento cancela esperas, limpa PasswordBox e referências de imagem (PrintWindow.cs:175; PrintWindow.PanoramaAi.cs:320-324).
- **Revit:** BimSceneReader e serviços de captura usam Autodesk.Revit.DB; operações mutáveis em cópia de vista/TransactionGroup. É preservação pretendida da vista original, sujeita a validação real (PanoramaCaptureService.cs:70-87,276).
- **Núcleo testável:** RenderSettings, PromptBuilder, CubeProjection, PanoramaViewerMath, PanoramaSeamMath, PanoramaRenderContract e provedor HTTP têm partes sem Revit/WPF. O projeto de checks vincula essas fontes (tests/...csproj).
- **Dados:** Print usa BimSceneSnapshot agregado e JSONs de regras; Panorama guarda snapshots por face e prompt próprio. Ver 06_BIM_MATERIAIS_TEXTURAS.md.
- **Persistência:** Print/Historico, Panorama/Capturas e Panorama/Renders sob LocalApplicationData; bootstrap.log separado. Ver 07_DADOS_HISTORICO_SEGURANCA.md.
- **Não identificado:** autenticação de usuário, cloud, publicação, fila de jobs, serviço backend e módulo de documentação BIM operacional. Aba Documentacao é placeholder (PrintWindow.cs:144).
