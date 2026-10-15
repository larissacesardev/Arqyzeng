# Dados, armazenamento, histórico e segurança

## Diretórios e ciclo de vida

~~~mermaid
flowchart TB
  LA[LocalApplicationData/ArqyzengBIM] --> CP[captures/GUID: PNG bruto Print]
  LA --> PH[Print/Historico/id: referência, render, generation.json]
  LA --> PA[Panorama/Capturas/id: seis faces, raw, panorama.json, PNG 2:1]
  LA --> PR[Panorama/Renders/id: referência, render, prompt, generation.json, reparo opcional]
  LA --> LG[logs/bootstrap.log]
  AP[AppData/Autodesk/Revit/Addins/2026] --> AD[ArqyzengBIM.addin gerado no Install]
~~~

**Confirmado pelo código.** Print exporta PNG bruto para captures/<GUID> (PrintCaptureHandler.cs:95-121), permite salvamento manual da captura e do render via diálogo (PrintWindow.cs:1037-1073,1175-1205). Print/Historico guarda referencia.png, render.png e generation.json apenas após clique; usa pasta .pending, move para nome final com timestamp local, offset e GUID parcial; ignora entradas pendentes/corrompidas na listagem (PrintWindow.cs:107-120,774-856). O JSON contém schema 1, ID, data com offset, nome da vista, perfil, fidelidade, qualidade, dimensões, crop e confirmações manuais. Não guarda prompt, snapshot BIM completo, modelo de IA ou chave (PrintWindow.cs:107-112,812-822).

PanoramaCaptureService.cs:61-69,240-305 cria Panorama/Capturas/<data+GUID>, seis faces normalizadas na pasta, seis PNGs brutos em raw/, panorama.json schema 2 com geometria/visual/BIM por face; usa .pending e rollback da vista. PanoramaProjectionService.cs:85-98 salva PNG equiretangular na mesma pasta. Panorama IA, após clique explícito, salva Panorama/Renders/<id>/referencia.png, render.png, prompt.txt e generation.json schema 1 com caminho completo da origem, tamanhos, perfil, quality, model, candidatos BIM e descrições; reparo aceito acrescenta cinco arquivos de evidência e seção SeamRepair (PrintWindow.PanoramaAi.cs:259-299; PrintWindow.PanoramaSeam.cs:189-197). A UI pode reabrir generation.json Panorama e arquivos fixos, com limites de 16 MiB para JSON e 50 MiB para PNG (PrintWindow.PanoramaSeam.cs:74-105). Não existe índice central/retention policy do Panorama identificado.

**Dados pessoais ou de projeto:** SourcePath pode conter nome de usuário/projeto e é gravado no generation.json Panorama; ViewName, imagens, prompts, descrições e candidatos BIM podem revelar projeto. No relatório, valores concretos devem ser representados por <REDACTED>. Os IDs de pasta são GUID/data gerados, não credenciais. A análise não abriu arquivos de histórico locais.

## Segurança e validação

- Chave de API: PasswordBox da janela, lida para a chamada, limpa ao fechar, não serializada em histórico ou logs (PrintWindow.cs:53,175,716-740; PrintWindow.PanoramaAi.cs:29,193-214,320-324). Ela permanece em string temporária gerenciada durante a chamada; limpeza de memória não é demonstrável.
- Transporte: HttpClient envia Bearer somente no header, redirecionamento desabilitado nas chamadas UI, timeout cinco minutos, sem retry. Corpo de resposta HTTP de erro não é exibido nem logado (OpenAiImageProvider.cs:82-99; PrintWindow.PanoramaAi.cs:210-216).
- Entrada de imagem: dimensões/PNG header e máscara RGBA são validados, mas ReadPngSize só verifica assinatura, IHDR e dimensões; decodificação posterior pela WPF valida conteúdo em fluxos UI. Limite de saída JSON 80 MiB e input PNG <50 MiB (OpenAiImageProvider.cs:43-64,100-136). PNG escolhido pode conter metadados ou dados sensíveis; não há sanitização de metadados explícita.
- Metadados importados: projeção limita schema, câmera, nomes fixos de face e tamanho; importação de geração usa nomes locais fixos para evitar seguir caminhos no JSON (PanoramaProjectionService.cs:23-58; PrintWindow.PanoramaSeam.cs:79-96). O carregamento de panorama.json para candidatos BIM não verifica hash do PNG, apenas diretório/dimensões (PrintWindow.PanoramaAi.cs:148-168).
- Logs: PluginLog escreve mensagem técnica e **tipo** da exceção, sem mensagem/stack trace; bootstrap.log em LocalApplicationData (PluginLog.cs:7-18). Há mensagens UI de erro em algumas rotas que usam exception.Message, por exemplo PrintWindow.cs:247; não se observou persistência de chave por essa via.
- Arquivos temporários: .pending são removidos nos catch/finally dos fluxos principais de captura/projeção/histórico. Falha abrupta do processo pode deixar resíduos; não há rotina geral de limpeza visível (PanoramaCaptureService.cs:309-313; PanoramaProjectionService.cs:89-97; PrintWindow.cs:827-831).
- Instalação: scripts/dev.ps1:86-165 faz staging/backup da instalação de usuário e manifesto. Este script não foi executado; scripts legados podem sobrescrever fontes e ficam fora do fluxo recomendado.

## Riscos classificados

| Risco | Evidência | Gravidade | Estado |
|---|---|---|---|
| Exposição de projeto em arquivos locais sem criptografia de aplicação | PrintWindow.PanoramaAi.cs:267-284 | Média; depende da máquina | Confirmado pelo código |
| SourcePath absoluto em JSON | PrintWindow.PanoramaAi.cs:270-272 | Média | Confirmado pelo código |
| Metadados BIM da mesma pasta sem hash do PNG | PrintWindow.PanoramaAi.cs:153-166 | Média | Confirmado pelo código; impacto é hipótese |
| Persistência de cache de capturas brutas | PrintCaptureHandler.cs:95-121 | Média | Confirmado pelo código; política externa indeterminada |
| Segurança/adequação do serviço remoto | OpenAiImageProvider.cs:82-125 | Não avaliada | Não é possível determinar |
| Credencial salva pelo aplicativo | UI e serializers examinados | Não identificada | Confirmado pelo código no escopo auditado |

Nenhuma credencial real foi reproduzida ou usada nesta auditoria.
