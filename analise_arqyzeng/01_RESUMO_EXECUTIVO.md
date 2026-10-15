# Auditoria técnica Arqyzeng BIM — resumo executivo

**Objeto:** fontes de C:/Users/BatComputador/Desktop/Arqyzeng-git, examinados em 01/10/2026. Panorama tem prioridade. A pasta vizinha Arqyzeng parece um workspace anterior; a escolha do clone é sustentada pela presença da implementação Panorama IA v9.0.2 e seam repair. A versão de um script de atualização não é versão do código carregado.

**Método:** leitura estática. “Confirmado pelo código” descreve implementação; “confirmado por teste” significa apenas que existe check explícito, não que ele foi executado agora; “fortemente inferido” depende de execução; “hipótese” requer experimento; “não é possível determinar” carece de Revit/RVT/API/medição. Não houve execução de testes, build, instalação, serviço externo ou Git. Todos os caminhos e linhas abaixo são relativos ao clone.

## Estado atual

- **Confirmado pelo código:** ArqyzengBIM.sln inclui o projeto Revit, enquanto tests/Arqyzeng.Rendering.Checks é executável separado. O plugin é net8.0-windows/x64/WPF com referências Revit 2026; SDK 8.0.425 em global.json. Ver 02_ARQUITETURA.md.
- **Confirmado pelo código:** PrintWindow.cs:124 exibe “Panorama IA v9.0.2 | Print v7.5.2”; PanoramaRenderContract.cs:12 marca “9.0.2-PoC”; PanoramaCaptureService.cs:280 grava “8.2.8-PoC”. São rótulos de componentes, não comprovação do binário instalado.
- **Confirmado pelo código:** Atualizar_Arqyzeng_Panorama_v9.1.ps1 contém alterações futuras encapsuladas e pode reescrever fontes; não foi executado. README.md:49-76 descreve outro estado de workspace e classifica Instalar_* como legados. A análise considera os arquivos em src, sem presumir v9.1 instalada.

## Conclusões

**Print.** Captura a vista 3D, permite formato, crop/fit e edição por IA. PromptBuilder lê RenderGeneralRules, perfil Dia/Noite e MaterialLibrary (20 categorias); inclui snapshot BIM agregado e confirmações manuais. A IA recebe a referência enquadrada na dimensão de saída normalizada, não o PNG Revit bruto. Evidência: PrintWindow.cs:377-475,708-753; PromptBuilder.cs:17-100.

**Panorama.** Captura seis direções em câmera em perspectiva copiada temporariamente. Mede FOV de 94°, faz crop central de 90° e monta 4N×2N, N=1024/2048; oferece viewer WPF. A IA opcional usa referência 1536×768 ou 2048×1024, com uma chamada para a esfera inteira; reparo de emenda é chamada adicional opcional. Evidência: PanoramaCaptureService.cs:19-305; CubeProjection.cs:9-70; PanoramaProjectionService.cs:23-113; PrintWindow.PanoramaAi.cs:186-299.

**Limite dominante.** BimSceneReader coleta candidatos da vista, não superfícies visíveis por pixel. Não lê AppearanceAsset, mapas de textura ou UV. PanoramaRenderContract omite nomes dos candidatos do prompt e ignora o parâmetro rulesDirectory; só transmite contagem e descrições localizadas escritas pelo usuário. Assim, a IA vê a textura rasterizada se houver, mas não recebe uma especificação BIM da textura. Evidência: BimSceneReader.cs:10-16,63-84; PanoramaRenderContract.cs:20-64.

**Qualidade.** Panorama 8K vira no máximo 2K para IA: 4× menos amostras lineares e 16× menos pixels, antes de qualquer alteração generativa. Supersampling melhora potencialmente o raster da face, sem elevar a resolução final. A projeção usa bilinear apenas dentro da face; continuidade visual entre faces e resultado IA dependem de revisão. Evidência: CubeProjection.cs:25-45; PanoramaProjectionService.cs:100-113; PrintWindow.PanoramaAi.cs:199-229.

## Dez forças

1. ExternalEvent separa UI e contexto Revit (PrintCommand.cs:43-79).
2. Cópia temporária e rollback protegem a vista de origem (PanoramaCaptureService.cs:70-87,276).
3. FOV medido via CustomExporter (PanoramaCaptureService.cs:339-356).
4. Overscan e crop central contínuo (CubeProjection.cs:22-45).
5. Validação numérica de faces antes da projeção (PanoramaProjectionService.cs:30-58).
6. PNG bruto e metadados de diagnóstico são guardados (PanoramaCaptureService.cs:240-303).
7. Viewer segmenta texturas em tiles (PanoramaViewerWindow.cs:95-120).
8. Prompt 360 explicita longitude periódica e polos (PanoramaRenderContract.cs:36-51).
9. Provedor valida dimensões, máscara, erro e cancelamento (OpenAiImageProvider.cs:43-125).
10. Reparo local mantém pixels fora da faixa e exige aceite (PanoramaSeamMath.cs:35-48; PrintWindow.PanoramaSeam.cs:58-70).

## Dez gargalos, por prioridade

1. P0: sem material/elemento por pixel (BimSceneReader.cs:10-11).
2. P0: 8K→2K na referência e saída IA (PrintWindow.PanoramaAi.cs:124-129,199-203).
3. P0: sem validação integrada nesta auditoria; checks não cobrem Revit real (README.md:11-14,47).
4. P1: sem AppearanceAsset, bitmap ou escala UV no snapshot (BimSceneReader.cs:63-66,82).
5. P1: biblioteca JSON desconectada do Panorama (PanoramaRenderContract.cs:20-64).
6. P1: bilinear por face, sem mistura transfronteira (PanoramaProjectionService.cs:100-113).
7. P1: sem validação automática de geometria/seam do render IA (PrintWindow.PanoramaAi.cs:218-230).
8. P1: modo 2048/2× exporta seis PNGs brutos de 4393², com custo de memória e tempo potencial (CubeProjection.cs:25-30).
9. P2: histórico Print omite prompt e snapshot; Panorama salva manualmente sem índice central (PrintWindow.cs:107-112,795-856; PrintWindow.PanoramaAi.cs:259-299).
10. P2: coexistência de atualizador e instaladores legados torna o estado implantado incerto (README.md:74-76; PanoramaRenderContract.cs:12).

**Próxima evolução recomendada:** obter mapa de superfície/elemento/material visível por direção, alinhado à mesma câmera/FOV/crop e à projeção equiretangular; validar esse vínculo num RVT de referência. Só depois usar material BIM como instrução localizada automática. Em paralelo, registrar ensaios visuais e métricas de borda, polos, resolução e memória. Ver 09_GARGALOS_E_OPORTUNIDADES.md.

**Limite:** funcionamento real no Revit, qualidade visual, custo/aceitação do modelo de IA e fidelidade do resultado são **não é possível determinar** sem execução externa.
