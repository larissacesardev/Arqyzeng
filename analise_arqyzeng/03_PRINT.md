# Print: captura, enquadramento, IA e histórico

## Fluxo

~~~mermaid
flowchart LR
  A[Vista 3D ativa] --> B[PrintCommand valida]
  B --> C[PrintWindow: formato, resolução e moldura]
  C --> D[ExternalEvent]
  D --> E[ImageExportOptions PNG]
  D --> F[BimSceneReader]
  E --> G[ImagePipeline: fit/crop]
  F --> H[PromptBuilder + JSON]
  G --> I[referência na resolução da IA]
  H --> J[OpenAiImageProvider]
  I --> J
  J --> K[comparar, salvar PNG ou histórico local]
~~~

**Confirmado pelo código.** O comando exige projeto e View3D não template (PrintCommand.cs:11-25). A UI dispõe de proporções 1:1, 4:5, 3:2, 16:9, 9:16, 21:9 e dimensões livres; presets de lado maior 1024, 2048, 4096; cada eixo limitado a 256–4096 (PrintWindow.cs:99-105,377-475,492-515). A moldura usa modo crop e acompanha vista via Idling; seu comportamento visual exato carece de Revit (ViewportGuide.cs:62-71,131-229).

A opção “Preparar vista limpa” duplica uma vista, solta template na cópia, muda para ShadingWithEdges, fundo branco, oculta níveis, grids e caixa de corte; a cópia é mantida para o usuário capturar, diferente do Panorama que faz rollback da cópia temporária. A origem não é alterada por este método (CaptureViewPreparation.cs:26-76). A aparência efetiva e a limpeza completa são **não é possível determinar** sem teste em modelo.

PrintCaptureHandler exporta VisibleRegionOfCurrentView em PNG, FitToPage horizontal, 150 DPI e PixelSize = min(8192, 2 × maior dimensão pedida); exige um único PNG e lê BIM da vista ativa (PrintCaptureHandler.cs:95-121). O PNG bruto fica sob LocalApplicationData/ArqyzengBIM/captures/<GUID>; o usuário pode salvar a captura final por diálogo (PrintWindow.cs:1175-1205). Não há evidência de rotina geral de expurgo desse cache.

ImagePipeline aplica crop localizado por horizontal/vertical/zoom 1–3 ou fit com margens brancas; RenderTargetBitmap WPF usa BitmapScalingMode.HighQuality e PNG. A prévia pode ter máximo de 1200 px, mas salvamento usa resolução selecionada (ImagePipeline.cs:22-85). O algoritmo interno exato de HighQuality depende de WPF, portanto não o chamar de bicúbico sem medição.

## IA do Print

OpenAiImageProvider.OutputSize normaliza o lado maior a 1536 px, com arredondamento para múltiplos de 16 e proporção 1:3–3:1 (OpenAiImageProvider.cs:33-41). PrintWindow prepara a referência nessa dimensão *antes* da chamada; RenderScene informa a dimensão da referência enviada e inclui snapshot BIM + confirmações. O provedor exige que PNG de entrada e saída solicitada tenham o mesmo tamanho; resposta é validada por cabeçalho e decodificação WPF (PrintWindow.cs:708-753; OpenAiImageProvider.cs:43-58,110-120). Se o Print capturado for 4096, o render IA não conserva os 4096 pixels. Não há upscaling após a IA (PrintWindow.cs:753).

Perfis Bump Dia/Noite, fidelidade Criativo/Controlado/Máxima e qualidade low/medium/high são expostos (PrintWindow.cs:427-445). PromptBuilder.BuildForImageEdit lê BumpDay/NightRules, RenderGeneralRules e MaterialLibrary (20 categorias), inclui restrições de geometria, classe e dados BIM. O modo Máxima adiciona travas de composição/entorno; no Bump Dia autoriza céu azul apenas no fundo externo vazio conforme exceção implementada (PromptBuilder.cs:23-120). Confirmar material é uma descrição por material candidato da captura; vale no prompt e histórico, não altera Revit nem vincula pixel (PrintWindow.cs:1101-1153; PromptBuilder.cs:148-165). Mudanças de vista/captura limpam confirmações em pontos explícitos; mudanças de enquadramento invalidam render, mas não convertem BIM em mapa de visibilidade (PrintWindow.cs:517-570,630-645).

A chamada é POST de edição com modelo constante em OpenAiImageProvider.cs:29, endpoint em :82, qualidade no multipart e uma tentativa. Timeout de cinco minutos, cancelamento e mensagens sem corpo remoto (PrintWindow.cs:735-762; OpenAiImageProvider.cs:82-125). Disponibilidade real do modelo e qualidade visual são **não é possível determinar** sem serviço externo, que não foi chamado.

## Histórico e riscos

Salvar no histórico é opcional, após render. A pasta contém referencia.png, render.png e generation.json com horário, vista, perfil, fidelidade, qualidade, dimensões, enquadramento e materiais manuais (PrintWindow.cs:107-120,774-856). Prompt textual e snapshot BIM bruto não são gravados no histórico Print; podem ser reconstruíveis apenas se regras/dados não mudarem, o que não é garantido. Salvar render e referência separadamente usa diálogos (PrintWindow.cs:1037-1073). Chave de API fica em PasswordBox e é apagada ao fechar (PrintWindow.cs:53,175); não é campo do JSON.

**Pontos de perda de informação:** rasterização da vista Revit; crop manual possivelmente corta elementos; resize/fit HighQuality até 4096 para captura e até ~1536 para IA; modelo generativo pode alterar pixels mesmo com prompt restritivo. Nenhuma dessas etapas demonstra fidelity geométrica automática. Ver 09_GARGALOS_E_OPORTUNIDADES.md.
