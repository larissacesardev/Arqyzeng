# Gargalos, causas e plano de evolução

A prioridade separa sintomas visuais de causa técnica. Um PNG borrado pode nascer no Revit, no crop, na projeção, na redução para IA, na própria IA ou no viewer; nenhum desses culpados pode ser decretado sem medir estágios.

| Prioridade | Sintoma/risco | Causa sustentada | Experimento discriminante | Ação proposta |
|---|---|---|---|---|
| P0 | Materiais inventados/substituídos | Sem material↔pixel; prompt Panorama recebe só contagem e descrição manual (BimSceneReader.cs:10-11; PanoramaRenderContract.cs:54-62) | RVT com materiais únicos por superfície e máscara ground truth | Render pass de IDs + mapa material/elemento por direção |
| P0 | Perda de detalhe no render IA | 8K reduzido para 2K antes do envio (PanoramaAi.cs:199-203) | Comparar recortes de alta frequência em original, referência e saída | Definir estratégia de edição por tiles ou pós-processo validado, conforme capacidade real do serviço |
| P0 | Confiabilidade Revit incerta | Checks exercem matemática, não export/rollback/viewer real (tests/...csproj:8-18) | Matriz RVT×vista×hardware/versão | Automação de captura diagnóstica + ensaio manual registrado |
| P1 | Textura física sem fidelidade | AppearanceAsset, bitmap e UV não são lidos (BimSceneReader.cs:63-66,82) | Material com mapa conhecido e escala conhecida | Extrator de appearance asset com proveniência e fallback |
| P1 | Instruções ricas não chegam ao Panorama | rulesDirectory ignorado em PanoramaRenderContract.cs:20-64 | Comparar prompts com regras editadas em fixture | Contrato Panorama próprio com regras esféricas materiais condicionais |
| P1 | Costura face-face | Sample bilinear faz clamp dentro de uma face (PanoramaProjectionService.cs:100-113) | Cubemap sintético com gradiente contínuo | Amostragem com vizinho transfronteira/edge-aware; medir antes |
| P1 | Seam IA ou polos defeituosos | Prompt pede continuidade, mas não há detector ou métrica (PanoramaAi.cs:218-230) | Métrica Δ cor/gradiente 0°/360° e poles antes/depois | QC automático com limiares; manter revisão humana |
| P1 | Custos de memória/tempo | 4393² bruto no modo 2048/2× e output 8192×4096 (CubeProjection.cs:25-30; PanoramaProjectionService.cs:66-87) | Perfmon/GC/tempo por face em máquina alvo | Pipeline por blocos, limitação de cópias, progresso/cancelamento |
| P2 | Rastreabilidade Print incompleta | Histórico não guarda prompt/modelo/snapshot completo (PrintWindow.cs:107-112,812-822) | Salvar e tentar reproduzir com JSON alterado | Histórico versionado com hash de referência e regras |
| P2 | Versões conflitantes | Atualizador v9.1 e fontes v9.0.2 (PanoramaRenderContract.cs:12) | Inventário de hash fonte/binário implantado | Manifesto de versão/hash gerado em build, retirar instaladores de uso |

## Plano de maior impacto

**Etapa 1 — medição e referência.** Construir RVT sintético autorizado, com seis paredes/cores/materiais, objetos conhecidos, mapa de textura e bordas nos meridianos. Registrar fonte, seis brutos, seis faces finais, panorama, referência IA e render, com hashes, dimensões, FOV medido, tempo e memória. Uma rotina de QC deve detectar largura de seam, diferenças de cor e deslocamentos de linhas. Critério: métricas reproduzíveis em dois runs; nenhum “passou” sem inspeção Revit.

**Etapa 2 — semântica por pixel.** Em export auxiliar, codificar ElementId/superfície/material em mapa raster alinhado ao mesmo eye/orientação/crop. Resolver faces→longitude/latitude reutilizando CubeProjection, lidar com múltiplos materiais e oclusão, e comparar máscaras com ground truth. O JSON deve conter proveniência e incerteza; não converter automaticamente um nome genérico em acabamento. Critério: IoU por superfície e erro angular medidos; link/camadas com estado explícito.

**Etapa 3 — prompt e textura.** Acrescentar leitura controlada de AppearanceAsset e texturas com resolução de paths, escala/rotação/UV, sem enviar binários ou caminhos inadvertidamente. Inserir apenas materiais cuja superfície esteja mapeada, usando regras da biblioteca adaptadas à geometria esférica. Critério: testes que confirmem “material X apenas na região Y” e que material incerto permaneça incerto.

**Etapa 4 — IA em resolução e continuidade.** Verificar capacidades atuais do serviço em ambiente autorizado; explorar tile editing/overlap ou super-resolução apenas com critérios de fidelidade. Medir 8K→referência→saída e custo, não chamar qualidade high de resolução high. Automatizar seam/pole QC e preservar imagem antes/depois. Critério: melhoria quantitativa e revisão visual, sem mudança geométrica indevida.

**Riscos de proposta:** mapas de IDs via Revit podem exigir API/renderer adicional; alinhamento perfeito e leitura de AppearanceAsset dependem de versão Revit. Custos, recursos de API e qualidade de modelo são **não é possível determinar** no repositório.
