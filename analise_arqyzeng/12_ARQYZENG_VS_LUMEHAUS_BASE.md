# Base de comparação futura: Arqyzeng × Lumehaus

**Regra de leitura:** esta matriz só preenche Arqyzeng a partir do clone auditado. A coluna Lumehaus fica “a levantar”; nenhum dado de Lumehaus é inferido, importado de memória ou usado para afirmar superioridade. As categorias e medidas permitem comparação posterior com a mesma metodologia, versões e cenários. C = confirmado pelo código, T = check existente (não executado aqui), ND = não determinável sem execução.

| Eixo | Arqyzeng observado | Fonte/estado | Lumehaus |
|---|---|---|---|
| Plataforma | Plugin C#/WPF dentro do Revit 2026, net8.0-windows x64 | Arqyzeng.Revit.csproj:3-25; C | A levantar |
| Arquitetura | Ribbon→PrintCommand→PrintWindow→ExternalEvent; captura e IA separadas | App.cs:11-25; PrintCommand.cs:43-79; C | A levantar |
| Print | Vista 3D, PNG, fit/crop, prompt/IA opcional | PrintWindow.cs:377-475,708-753; C | A levantar |
| Print saída local | 256–4096 px por eixo, presets 1024/2048/4096 | PrintWindow.cs:390-396,492-515; C | A levantar |
| Print IA | Lado maior normalizado a 1536, múltiplos 16 | OpenAiImageProvider.cs:33-41; C | A levantar |
| Panorama captura | Câmera perspectiva, seis faces | PanoramaCaptureService.cs:19-60; C | A levantar |
| Faces/eixos | +Z frente, +X direita, -Z trás, -X esquerda, +Y teto, -Y piso | CubeProjection.cs:5-70; C/T | A levantar |
| Eye/orientação | Uma eye, eixos derivados da orientação da fonte, sem nivelamento explícito | PanoramaCaptureService.cs:52-60; C | A levantar |
| FOV bruto/útil | 94° medido → 90° central | PanoramaCaptureService.cs:163-184,251-274; C/T matemática | A levantar |
| Overscan | ceil(N·s·tan47°), crop central contínuo | CubeProjection.cs:22-45; C/T | A levantar |
| Supersampling | 1× ou 2× por eixo bruto; final N×N | CubeProjection.cs:25-30; PanoramaCaptureService.cs:319-335; C/T | A levantar |
| Resolução face | 1024²/2048²; brutos até 4393² | CubeProjection.cs:9-30; C | A levantar |
| Resolução equiretangular | 4096×2048 ou 8192×4096 | CubeProjection.cs:9-14; C/T | A levantar |
| Projeção | Direção esférica→face dominante, bilinear local com clamp | PanoramaProjectionService.cs:66-113; C | A levantar |
| Tratamento de seam de captura | Sem blend entre faces; overscan/crop e luz fixa mitigam causas | PanoramaCaptureService.cs:120-130; PanoramaProjectionService.cs:100-113; C/I | A levantar |
| Viewer | WPF Viewport3D, esfera interna, tiles ≤1024 + gutter | PanoramaViewerWindow.cs:95-203; C/T UV | A levantar |
| Viewer controles | yaw/pitch, FOV, drag, setas, fullscreen | PanoramaViewerWindow.cs:47-85; C | A levantar |
| IA Panorama modelo | String constante gpt-image-2.5-sunburst | OpenAiImageProvider.cs:27-29; C; disponibilidade ND | A levantar |
| Endpoint | POST images/edits, uma imagem e máscara opcional | OpenAiImageProvider.cs:66-85; C/T simulado | A levantar |
| Input fidelity parâmetro | Não enviado como parâmetro HTTP | OpenAiImageProvider.cs:66-83; C | A levantar |
| Qualidade IA | low/medium/high, padrão Panorama high | PanoramaAi.cs:65-69; OpenAiImageProvider.cs:49-55; C | A levantar |
| Tamanho referência/saída IA | 1536×768 ou 2048×1024, sem output 8K | PanoramaRenderContract.cs:13-18; PanoramaAi.cs:199-222; C | A levantar |
| Prompt Panorama | Próprio para 360, polos, longitude periódica e restrição semântica | PanoramaRenderContract.cs:36-51; C/T texto | A levantar |
| Prompt Print | Perfis/fidelidades e regras JSON | PromptBuilder.cs:17-120; C/T texto | A levantar |
| MaterialLibrary | 20 categorias no Print; não lida pelo Panorama | PromptBuilder.cs:23-34,82-86; PanoramaRenderContract.cs:20-64; C | A levantar |
| Dados BIM | Candidatos agregados da vista/face: nome/classe/cor/transparência/categorias/paint | BimSceneReader.cs:24-99; C | A levantar |
| Texturas | Imagem Revit pode rasterizar textura; path/mapa/UV não extraídos | BimSceneReader.cs:82; PanoramaCaptureService.cs:31-45; C/I | A levantar |
| AppearanceAsset | Não lido pelo snapshot | BimSceneReader.cs:62-66; C | A levantar |
| Material↔pixel | Não implementado | BimSceneReader.cs:10-11; C | A levantar |
| Elemento↔pixel | Não implementado | RenderSettings.cs:11-16; PanoramaProjectionService.cs:100-113; C | A levantar |
| Confirmações humanas | Print por material; Panorama “local=>acabamento” até 12 linhas/2000 chars | PrintWindow.cs:1101-1153; PanoramaRenderContract.cs:25-33; C | A levantar |
| Identificação de objetos | Sem recurso estruturado atual no Panorama; instrução genérica no prompt | PanoramaRenderContract.cs:42-43; C | A levantar |
| Seam IA | Reparo opcional pago, máscara 30°, composição local, aplicação manual | PanoramaSeamMath.cs:8-57; PanoramaSeam.cs:107-140; C/T matemática | A levantar |
| Histórico | Pastas locais separadas Print/Panorama, clique explícito, schemas 1; captura schema 2 | PrintWindow.cs:795-856; PanoramaAi.cs:259-299; C | A levantar |
| Storage | LocalApplicationData/ArqyzengBIM; manifesto instalado em AppData pelo script | PrintCaptureHandler.cs:95-99; scripts/dev.ps1:86-125; C | A levantar |
| Segurança | PasswordBox, Bearer, sem chave no JSON, erro sem corpo remoto; projetos e SourcePath locais | OpenAiImageProvider.cs:82-99; PanoramaAi.cs:270-284; C | A levantar |
| Documentação BIM | Aba placeholder | PrintWindow.cs:144,485-490; C | A levantar |
| Cloud/publicação | Não identificado nos fontes | inventário 10_MAPA_COMPLETO_CODIGO.md; escopo C | A levantar |
| Testes | Checks puros e HTTP simulado; sem evidência de integração Revit nesta auditoria | tests/.../Program.cs; README.md:47; T/ND | A levantar |

## Protocolo de comparação justa

1. Fixar commit ou hash dos fontes, versão do binário instalado, Revit, RVT, câmera, estilo, hardware e tamanho de saída em ambos os produtos.
2. Capturar cada etapa: seis brutos, faces úteis, panorama, referência enviada à IA, resposta nativa, seam reparado; medir hashes, dimensões e tempo/memória.
3. Usar cenários de materiais conhecidos com ground truth de IDs, texturas UV e oclusões. Não confundir nome agregado no BIM com acabamento do pixel.
4. Comparar pixels por grau e resolução nativa antes de avaliar nitidez; qualidade “high” da API não é contagem de pixels.
5. Medir continuidade 0°/360°, entre faces e polos, alinhamento angular de objetos, cor/padrão de material e custo por tentativa.
6. Marcar separadamente implementado, check automatizado, observado em Revit e observado em API externa. Qualquer campo não medido fica ND.
7. Registrar consentimento e privacidade de modelos antes de chamadas reais.

Esta base não conclui qual produto é melhor. A coluna Lumehaus e os indicadores de execução aguardam auditoria própria e comparação com cenário comum.
