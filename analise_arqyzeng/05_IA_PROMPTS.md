# IA, prompts, resolução e contrato HTTP

## Contrato único de provedor

**Confirmado pelo código.** OpenAiImageProvider.cs:27-29 fixa o modelo “gpt-image-2.5-sunburst”; :82 usa POST para /v1/images/edits; :66-83 envia model, prompt, size, quality, n=1, output_format=png, image[] PNG e mask opcional, Bearer em header. O código aceita qualidades low, medium e high, não xhigh/max (:49-55). Valida referência exatamente no tamanho solicitado, PNG <50 MiB, máscara RGBA no mesmo tamanho, proporção 1:3–3:1, múltiplos de 16, limites 16–3840 por eixo e 655360–8294400 pixels. Lê JSON Base64 até 80 MiB de resposta e confere dimensões PNG (:43-125). Não há fallback de modelo nem retry; isso evita duplicar cobrança automática, mas deixa falhas transitórias para decisão humana. A presença da string de modelo **não prova** sua disponibilidade real na API em 2026; nenhum serviço foi chamado.

| Pipeline | Original | Referência enviada | Saída pedida/validada | Qualidade |
|---|---|---|---|---|
| Print | PNG Revit, conforme vista e export | Fit/crop local → maior eixo 1536, múltiplos de 16 | mesmo tamanho da referência | low/medium/high |
| Panorama | equiretangular 4096×2048 ou 8192×4096, ou PNG 2:1 escolhido 512–8192 | resize WPF da esfera toda a 1536×768 ou 2048×1024 | exatamente 1536×768 ou 2048×1024 | low/medium/high; UI padrão high 2048×1024 |
| Seam repair | render IA atual | imagem deslocada 180° + máscara | mesmo tamanho, high | high |

Evidência: OpenAiImageProvider.cs:33-58; PrintWindow.cs:708-753; PrintWindow.PanoramaAi.cs:103-129,186-229; PrintWindow.PanoramaSeam.cs:107-139. “Input fidelity” não é enviado como parâmetro HTTP; fidelidade máxima do Panorama é linguagem de prompt e metadado, não garantia de fidelidade da API. Não há tamanho customizado de IA na UI Panorama; Print admite dimensão de captura customizada, não saída arbitrária de IA. Contrato genérico do provider aceitar outros tamanhos não os torna selecionáveis no Panorama.

~~~mermaid
sequenceDiagram
  participant U as Usuário
  participant UI as PrintWindow Panorama
  participant P as PanoramaRenderContract
  participant R as ImagePipeline
  participant H as OpenAiImageProvider
  participant API as API de imagens
  U->>UI: escolhe PNG 2:1, perfil, qualidade, tamanho
  UI->>P: Build com descrições e candidatos BIM
  UI->>R: reduzir panorama inteiro
  UI->>H: EditAsync PNG + prompt + chave
  H->>API: POST uma tentativa
  API-->>H: PNG em JSON Base64
  H-->>UI: dimensões verificadas
  UI-->>U: conferir viewer, salvar manualmente
~~~

## Panorama: conteúdo efetivo do prompt

PanoramaRenderContract.Build (src/Arqyzeng.Revit/PanoramaRenderContract.cs:20-64) valida 1536×768/2048×1024, perfil Dia/Noite, descrições até 2000 caracteres e 12 linhas no formato local da superfície => acabamento. O texto preserva projeção equiretangular 360×180, razão 2:1, longitude periódica, fechamento 0°/360°, zenit/nadir, posição angular de portas/janelas/paredes, aparência de materiais e ausência de elementos inventados. Exige retorno em dimensão exata e pede checagem da seam; Dia permite céu somente exterior já visível, Noite mantém luminárias existentes. Há cláusulas de textura, roughness, transparência e ausência de oversharpening (linhas 36-51).

As descrições manuais são serializadas como string JSON e chamadas de dados, não comandos (:52-53). Se snapshots existem, o código conta candidatos distintos por nome/classe/cor, mas deliberadamente **não envia seus nomes** no prompt (:54-62). Portanto BIM não especifica acabamento localizado à IA Panorama. O argumento rulesDirectory é recebido, mas não lido; nenhum JSON de MaterialLibrary/RenderGeneralRules/Day/Night entra aqui (:20-64). O helper Safe em :66-67 não tem chamada identificada. A UI prepara rulesDirectory apesar disso (PrintWindow.PanoramaAi.cs:170-174). Isto é código existente mas desconectado, não “biblioteca parcialmente aplicada” no sentido de instruções JSON.

## Print: conteúdo efetivo do prompt

PromptBuilder.Build (src/Arqyzeng.Revit/PromptBuilder.cs:17-100) carrega dois guias selecionados, geral e MaterialLibrary, valida ids/versões/instruções, concatena restrições arquitetônicas e liberdade Criativo/Controlado/Máxima. BimContextFormatter.Format acrescenta snapshot agregado da vista como JSON de materiais e categorias, inclusive nome/classe/cor/transparência/uso e notas de incerteza (:77; BimContextFormatter.cs:17-39). Confirmações humanas entram como registros JSON com material, classe, categorias e descrição (PromptBuilder.cs:148-165). Não há segmentação por pixel. BuildForImageEdit acrescenta dimensão final e proibição de reenquadrar, com exceção textual de céu no Bump Dia/Máxima (:103-120). A ordem e a presença textual são cobertas por checks existentes (tests/.../Program.cs:18-120), sem comprovar obediência do modelo.

## Seam repair

Prompt independente de PanoramaSeamMath.Prompt pede reparar apenas faixa transparente de 30° após rotação cíclica, preservando arquitetura e material fora dela (PanoramaSeamMath.cs:50-57). O provider recebe máscara e imagem deslocada. Depois, LimitToBand garante cópia exata fora da faixa, com taper de até 16 px nas bordas; isso é proteção de pixels programática, não validação semântica dentro da faixa (PanoramaSeamMath.cs:35-48; PrintWindow.PanoramaSeam.cs:117-140).

## Falhas, custo e segurança

Timeout de cinco minutos e cancelamento local são empregados em Print/Panorama/reparo (PrintWindow.cs:735-762; PrintWindow.PanoramaAi.cs:210-245; PrintWindow.PanoramaSeam.cs:127-150). Cancelar espera não garante cancelamento de cobrança remota. Resposta de erro HTTP não expõe body remoto (OpenAiImageProvider.cs:86-99). PasswordBox não é persistido em JSON; log grava apenas tipo de exceção (PluginLog.cs:7-18). O envio de imagem e prompt pode conter projeto, materiais e descrições: uma chamada real exige decisão de uso pelo operador. Nenhuma chamada real foi feita nesta auditoria.
