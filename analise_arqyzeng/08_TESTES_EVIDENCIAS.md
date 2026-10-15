# Testes existentes, maturidade e limites da evidência

## Método desta auditoria

A análise foi **estática**. Nenhum check foi executado: dotnet run/build geraria .artifacts e testes criam scratch em diretório temporário (tests/Arqyzeng.Rendering.Checks/Program.cs:72-90), enquanto a instrução permite criar somente analise_arqyzeng. Não houve Revit, RVT, API real, benchmark ou comparação visual. “Confirmado por teste” nesta documentação significa *existe teste automatizado explícito*, sem declarar resultado desta execução. README.md:11-14 registra um bloqueio histórico por SDK 8/Revit 2026 no computador de desenvolvimento; esse registro não substitui inspeção atual do ambiente.

## Inventário de checks em fonte

| Tema | Evidência | O que verifica | O que não verifica |
|---|---|---|---|
| Prompt Print | tests/.../Program.cs:18-120 | Perfis Dia/Noite × três fidelidades, regras JSON, 20 categorias, BIM, confirmações, ordem/restrições | Obediência da IA, aparência em cena |
| Provider HTTP | ImageProviderChecks.cs:14-151 | Form multipart, endpoint, Bearer, tamanhos, máscara, erro sem body, tentativa única, cancelamento; handler falso | API real, modelo existente, custo, latência |
| Cubemap | Program.cs:123-152 | Seis eixos, up/down, 2000 raios aleatórios, inversão angular | Export Revit e pixels |
| Viewer | Program.cs:154-174,231-261 | Direção, UV, 1000 coordenadas, yaw, tiles, wrapping | Render WPF/hardware, inversão perceptiva |
| Resoluções/overscan | Program.cs:177-229,263-296 | N=1024/2048, 4N×2N, tolerância raster, 94→90, 1×/2× | Kernel WPF, antialias Revit, nitidez percebida |
| IA Panorama | Program.cs:298-330 | Contrato 360, dimensões, qualidade, exclusão de regras perspectiva, descrições | Fidelidade do modelo real |
| Seam math | Program.cs:332-355 | Rotação reversível, máscara, cópia exata fora da faixa | Qualidade da correção IA, emendas reais |

O projeto de checks vincula diretamente classes puras e JSON (tests/...csproj:8-18), portanto não compila nem exercita PanoramaCaptureService, PanoramaProjectionService, PrintWindow, WPF real ou Autodesk.Revit.DB. ImageProviderChecks usa PNG sintético de cabeçalho, não uma imagem completa no trecho principal (:26-34), de modo que não prova decodificação por WPF.

## Matriz de maturidade

| Capacidade | Código | Check específico | Validação integrada nesta auditoria | Classificação |
|---|---|---|---|---|
| Bootstrap/ribbon | App.cs:5-41 | Não | Não | Implementado sem teste suficiente |
| Print captura/crop | PrintCaptureHandler.cs:95-121; ImagePipeline.cs:22-85 | Prompt/imagem parcial | Não | Implementado sem teste suficiente |
| Print IA/prompt | PrintWindow.cs:708-753; PromptBuilder.cs:17-120 | Sim, provider simulado e texto | Não | Implementado e testado parcialmente |
| Panorama câmera/export | PanoramaCaptureService.cs:19-305 | Matemática pura | Não | Implementado sem teste integrado |
| Projeção equiretangular | PanoramaProjectionService.cs:23-113 | Mapeamento puro | Não | Implementado sem teste de raster real |
| Viewer 360 | PanoramaViewerWindow.cs:95-203 | Matemática/tiles | Não | Implementado sem teste visual |
| IA Panorama | PrintWindow.PanoramaAi.cs:186-299 | Contrato/HTTP simulado | Não | Implementado parcialmente, experimental |
| BIM candidato | BimSceneReader.cs:24-99 | Formatter com fixture | Não | Implementado sem teste Revit |
| Biblioteca materiais Print | PromptBuilder.cs:23-89 | Sim, texto/JSON | Não | Implementado e testado no nível textual |
| Biblioteca materiais Panorama | PanoramaRenderContract.cs:20-64 | Não aplicável | Não | Não utilizada |
| Texturas/AppearanceAsset BIM | BimSceneReader.cs:63-66 | Não | Não | Não existe |
| Material/elemento ↔ pixel | BimSceneReader.cs:10-11 | Não | Não | Não existe |
| Confirmação de material Print | PrintWindow.cs:1101-1153 | Prompt com fixture | Não | Implementado parcialmente |
| Identificação de objetos Panorama | PanoramaRenderContract.cs:42-43 | Não | Não | Não existe como recurso estruturado |
| Seam repair | PrintWindow.PanoramaSeam.cs:107-140 | Matemática da faixa | Não | Implementado sem validação visual |
| Histórico Print/Panorama | PrintWindow.cs:795-856; PanoramaAi.cs:259-299 | Não | Não | Implementado sem teste suficiente |
| Documentação BIM | PrintWindow.cs:144 | Não | Não | Placeholder |
| Autenticação/cloud/publicação | Inventário de fontes | Não | Não | Não identificado |

“Implementado e testado” nesta matriz refere-se apenas à parte pura/textual coberta por checks existentes; não afirma execução dos checks neste trabalho.

## Casos adicionais recomendados

1. RVT controlado com 6 padrões coloridos que cruzem bordas de faces, teto/piso e longitude 0°/360°; comparar FOV medido, eye, orientação e pixel de borda.
2. Vistas com template, filtros, section box, clipping e luz relativa; verificar rollback e fidelidade de estilo em cada face.
3. Export 1024/2048 e 1×/2×; medir MTF/nitidez de linhas finas e tempo/memória, sem inferir kernel da WPF.
4. PNGs sintéticos de cubemap com rampas lineares; testar PanoramaProjectionService.Build e amostragem bilinear inclusive face boundary/polos.
5. Viewer WPF com imagem de coordenadas rotuladas; conferir front/right/back/left/up/down e seam no hardware alvo.
6. Mock da IA que altere pixels fora da máscara: garantir composição fora da faixa idêntica, persistência de evidências e importação posterior.
7. Teste de histórico para corrupção, limites de tamanho, path absolutos, dados BIM, reversão .pending e schema.
8. Teste de prompt Panorama verificando explicitamente que regras JSON não entram, para tornar deliberada a decisão ou detectar reconexão futura.
9. Teste de consentimento e privacidade com modelo RVT fictício; nenhuma chave real ou arquivo de cliente em fixture.

## Limites

Sem Revit real, não é possível confirmar compatibilidade binária, FOV efetivamente exportado, visual das seis faces, seam, aparência de materiais, performance nem a política de armazenamento do serviço externo. Um teste matemático passing em outra máquina não transforma esses pontos em observações reais.
