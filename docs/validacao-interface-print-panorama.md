# Interface Print e Panorama

## Segunda rodada — polimento visual

- Tema centralizado em `PrintWindow.Theme.cs`: botões primários, secundários e
  compactos, inputs, itens selecionáveis, abas, tooltips, cards e rolagem.
- ComboBox permanece nativo na seleção e no teclado, com template programático.
  O chevron vetorial usa RotateTransform de 0° a 180° em 150 ms, com EaseOut.
  Se o Windows desabilitar animações, a orientação muda sem transição.
- Handle de 28 × 56 px em coluna própria de 40 px, fora do ScrollViewer.
  Aponta para a direita quando fechado e para a esquerda quando aberto;
  tooltip e nome acessível acompanham o estado. A comparação começa recolhida.
- Scrollbars de 9 unidades WPF, thumb arredondado, track discreto, sem setas.
  Aplicadas às sidebars, listas, dropdowns, campos de texto roláveis e modais
  com o tema, incluindo histórico e ampliação das imagens.
- Histórico com lista estilizada, detalhes em Segoe UI, cards das imagens e
  estado vazio. Acentuação revisada nos arquivos de UI; nomes de arquivos,
  pasta `Historico`, JSON, prompts e valores internos permanecem estáveis.
- `PanoramaViewerWindow.cs` e `ViewportGuide.cs` receberam somente correções
  em textos visíveis. Nenhuma alteração funcional além de apresentação/UX.

### Validação em 06/10/2026

- `dotnet build src/Arqyzeng.Revit/Arqyzeng.Revit.csproj -c Release --no-restore`:
  **0 erros**, os mesmos **2 avisos MSB3277** de referências Autodesk.
- Checks existentes executados em Release: aprovados, sem chamada paga.
- Smoke WPF local ampliado: templates, estado inicial recolhido, handle fixo
  durante rolagem, mouse wheel, arraste do thumb, abrir/fechar dropdown,
  rotação 0°/180°, seleção por teclado e modal somente leitura aprovados.
  Teste executado em janela WPF fora da tela, sem iniciar o Revit.
- Revisão das strings da UI e dos nomes estáveis de arquivos realizada.
- `global.json` e o `.csproj` não foram alterados nesta rodada; preservado o
  alvo atual da branch para Revit 2026.5 / .NET 10.

A integração dentro do Revit e escalas de tela diferentes ainda exigem o
roteiro manual abaixo. Os testes WPF não representam uma validação no Revit.

## Primeira rodada

Interface criada em C#, sem XAML. Tema em `PrintWindow.Theme.cs`: Segoe UI,
azul, inputs brancos, cartões e estados de foco/desabilitado. A comparação
começa recolhida; expandir/recolher mantém a imagem e a seleção existentes.
O painel principal tem largura limitada para manter os campos legíveis.

O recorte mantém os valores padrão (centro 50%/50%, zoom 1x), com os controles
avançados fora do layout. A moldura continua em **Ferramentas da vista**.
Largura e altura aparecem ao selecionar **Personalizado**. O reparo de emenda
continua disponível em uma seção recolhível no Panorama IA. Informações
complementares de fidelidade, BIM e cancelamento ficam em dicas de ferramenta;
status, erros e a indicação de chamadas pagas permanecem disponíveis.

## Verificações executadas

- Build do projeto com a configuração local existente: zero erros, dois
  avisos MSB3277 de conflito de referências de assemblies.
- Checks existentes de prompts, HTTP simulado, projeção e Panorama: aprovados,
  sem chamadas pagas.
- Verificação WPF local, sem abrir Revit: construção da janela e templates,
  comparação inicialmente recolhida, expandir/recolher, mudança de formato,
  controles de recorte fora da árvore visual e layout do Panorama.

Essas verificações não comprovam integração ou aparência dentro do Revit.
As alterações locais de SDK/framework em `global.json` e no `.csproj` foram
preservadas; a compatibilidade da instalação de destino deve ser confirmada
no ambiente usado para distribuir o plugin.

## Roteiro no Revit

1. Abrir Print em uma vista 3D. Conferir painel de comparação recolhido,
   fonte, campos, foco por Tab e seleção dos ComboBoxes por mouse e teclado.
2. Selecionar Personalizado, preencher dimensões e voltar a um formato fixo.
   Selecionar os dois modos de enquadramento: apenas o select deve aparecer.
3. Preparar vista/capturar. Abrir comparação e conferir Original. Recolher,
   capturar novamente e reabrir: a imagem deve refletir a captura atual.
4. Abrir prompt de render: conferir leitura, seleção de texto, Copiar, Fechar
   e Escape; a janela principal deve ficar bloqueada enquanto o modal estiver
   aberto. Repetir no Panorama IA após selecionar um PNG 2:1.
5. Com uma geração autorizada, conferir Original/Renderizado, salvar PNG e
   referência, salvar/abrir histórico. Recolher/reabrir não deve mudar a seleção.
6. No Panorama, capturar e trocar faces, montar/visualizar 360 e conferir
   materiais. Abrir a seção de reparo e confirmar que as ações permanecem
   acessíveis. Não é necessário executar chamadas pagas para conferir layout.
7. Redimensionar até o tamanho mínimo e testar escala Windows 100%/150%:
   conferir rolagem, leitura dos botões, preview e rodapé.

Registrar versão do Revit, escala de tela e capturas de evidência antes de
marcar a integração como validada.
