# Panorama 360°: câmera, cubemap, projeção, viewer e orçamento de qualidade

## Captura e validação de câmera

~~~mermaid
flowchart LR
  A[Vista 3D perspectiva] --> B[Validar estado, estilo e duplicação]
  B --> C[Duplicar em TransactionGroup]
  C --> D[front/right/back/left/up/down]
  D --> E[Orientar + medir câmera CustomExporter]
  E --> F[Calibrar FOV horizontal/vertical 94 graus]
  F --> G[Exportar PNG bruto overscan]
  G --> H[Recortar região central exata 90 graus]
  H --> I[faces N×N + raw + BIM por face]
  I --> J[Rollback e panorama.json schema 2]
  J --> K[Projetar equiretangular 4N×2N]
  K --> L[Viewer WPF ou IA opcional]
~~~

**Confirmado pelo código.** Panorama exige View3D em perspectiva, projeto editável, sem hide/isolate ou propriedades temporárias, duplicação permitida e estilo visual Realistic/Textures/Shading/FlatColors compatível (PanoramaCaptureService.cs:19-34). A orientação da fonte fornece eye, front, right=front×up e up=right×front. A origem pode ter pitch/roll; não há nivelamento forçado ao mundo. As faces, na ordem, são front=(F,U), right=(R,U), back=(-F,U), left=(-R,U), up=(U,-F), down=(-U,F) (PanoramaCaptureService.cs:52-60). A convenção local é +X direita, +Y cima, +Z frente (CubeProjection.cs:5-6).

A vista é duplicada e o template é solto só na cópia. Estilo, detail level, caixa de corte, filtros e overrides são conservados; contornos e anotações são ocultos na cópia; far clipping é desativado. Luz relativa à vista é substituída por luz fixa de azimute/altitude 45° para evitar variação entre faces. Isso muda potencialmente a aparência frente à origem. A cópia e suas transações sofrem rollback após as seis faces (PanoramaCaptureService.cs:70-152,276-307). **Fortemente inferido:** a vista original permanece visualmente intacta se todas as operações Revit obedecerem o contrato; confirmação exige ensaio Revit.

CustomExporter mede HorizontalExtent, VerticalExtent e TargetDistance. FOV = 2 atan(extent/(2·distance)); calibração iterativa procura 94° nos dois eixos, tolerância 0,05° e offsets ≤1e-5; falha aborta a captura (PanoramaCaptureService.cs:163-184,339-356). ImageExportOptions exporta um único PNG por face, PNG, FitToPage horizontal, PixelSize calculado e 150 DPI; raster e orientação são validados (PanoramaCaptureService.cs:214-274). Dados BIM são lidos na face de 94°, portanto incluem margem de overscan e candidatos ocultos; não são pixels da face final de 90° (PanoramaCaptureService.cs:214-221,297-300).

## Overscan e detalhes finos

CubeProjection.CaptureFovDegrees=94; o crop central extrai 90°. A razão de amostras na área útil é 1/tan(47°) ≈ 0,9325 do lado bruto. Para N pixels finais e fator s=1 ou 2, lado bruto = ceil(N·s·tan47°); a região central tem ≈N·s amostras e é reamostrada para N×N. CentralFaceRegion usa coordenadas contínuas; WriteCentralFaceRaster desenha com WPF HighQuality, sem arredondar um crop inteiro (CubeProjection.cs:22-45; PanoramaCaptureService.cs:319-335). O FOV não muda com 2×; muda densidade do raster bruto. Não há prova do kernel interno exato da WPF ou de antialiasing nativo Revit.

| N final por face | 1× bruto | 2× bruto | Panorama final | Pixels horizontais/grau |
|---:|---:|---:|---:|---:|
| 1024 | 1099×1099 | 2197×2197 | 4096×2048 | 11,38 |
| 2048 | 2197×2197 | 4393×4393 | 8192×4096 | 22,76 |

Os números brutos são derivados da fórmula em CubeProjection.cs:25-30; o export real admite tolerância de 1 px e é verificado em PanoramaCaptureService.cs:248-250. Cada face final tem 90° úteis, portanto 1024/90=11,38 ou 2048/90=22,76 px/grau no equador da face; a equiretangular conserva 4N/360. Em latitude alta, amostras representam área esférica diferente; “pixels por grau” não equivale a detalhe homogêneo nos polos.

~~~mermaid
flowchart TB
  F[+Z front] --- R[+X right]
  R --- B[-Z back]
  B --- L[-X left]
  L --- F
  U[+Y up] --> F
  D[-Y down] --> F
~~~

**Confirmado por teste existente, sem execução nesta auditoria:** Program.cs:123-152 confere eixos, polos e 2000 raios aleatórios; :198-229 confere crop/FOV; :263-296 confere invariância do 2×. Isso testa funções puras, não a câmera real, imagem exportada ou costura visual.

## Equiretangular e amostragem

PanoramaProjectionService.Build lê panorama.json de até 16 MiB, exige schema 2, Validated, seis nomes únicos, mesma eye, direções e up ortonormais esperadas, 90° ±0,05°, offset ~0, arquivos locais de nomes fixos e faces quadradas N=1024/2048 (PanoramaProjectionService.cs:23-64). Para cada pixel do panorama: longitude=((x+0,5)/W−0,5)·2π; latitude=(0,5−(y+0,5)/H)·π; raio=(coslat·sinlon, sinlat, coslat·coslon). CubeProjection.Map escolhe eixo dominante e face e fornece U/V. Sample interpola bilinearmente os quatro vizinhos dentro da face, com clamp na borda. Não amostra face vizinha nem aplica blur/feather de seam (PanoramaProjectionService.cs:66-113; CubeProjection.cs:48-70). O fluxo escreve PNG por arquivo .pending renomeado, preservando os arquivos de captura (PanoramaProjectionService.cs:85-98).

Matematicamente, +Z está no centro da imagem, -Z nas bordas periódicas, +X a 90° à direita e -X à esquerda; +Y e -Y são topo/base. Os testes de raios e do viewer sustentam a convenção, mas igualdade visual nas bordas depende de exportação/iluminação/raster. O último e o primeiro pixel horizontal são direções próximas do mesmo meridiano, sem identidade de pixels garantida. Nos polos, muitas longitudes convergem, e os pixels de última linha aproximam os polos sem amostrar exatamente ±90°. Não há algoritmo explícito para harmonização de cor entre faces.

## Viewer

PanoramaViewerWindow.cs:34-87,95-203 cria Viewport3D WPF com esfera vista por dentro, câmera na origem, FOV inicial 75°, yaw/pitch, mouse, roda, setas, F11, botões teto/piso. Aceita PNG 2:1 entre 512 e 8192 px de largura. Segmenta textura em patches nativos até 1024 px e usa gutter de 2 px; longitudes envolvem, latitudes são limitadas nos polos (PanoramaViewerMath.cs; PanoramaViewerWindow.cs:95-120,156-203). Não há HTML, JS, WebGL, WebView ou canvas nesta implementação. Os checks cobrem mapeamento de eixos, UV, tiles e wrapping (tests/.../Program.cs:154-174,231-261); não medem deformação perceptiva da malha nem hardware GPU.

## Orçamento de qualidade e memória

| Etapa | 4K | 8K | Risco |
|---|---:|---:|---|
| Export bruto 1×/2× | 1099²/2197² por face | 2197²/4393² por face | Tempo, aliasing, memória Revit |
| Face final | 1024² | 2048² | Downsample 2× melhora potencial de linhas finas sem novo tamanho |
| Equiretangular | 4096×2048 (~32 MiB BGRA) | 8192×4096 (~128 MiB BGRA) | Bilinear e clamp nas bordas; várias cópias em memória |
| Referência/saída IA | 1536×768 ou 2048×1024 | mesmos limites | 4K→2K: 2×/4× menos linear; 8K→2K: 4× menos linear, 16× menos pixels |
| Viewer | arquivo até 8192×4096 | arquivo até 8192×4096 | tiles mantêm resolução nominal, mas tela/GPU impõem limite visual |

Valores BGRA são 4 bytes/pixel, excluem bitmaps originais, PNG comprimidos, strings Base64, buffers do decoder, texturas WPF e seis faces. Modo alto 2× gera ~73,6 MiB BGRA por face bruta (4393²×4); seis simultâneas equivaleriam a ~442 MiB, mas o código processa sequencialmente, com retenções/cópias variáveis. Não houve benchmark. Qualidade “high” da API não eleva a resolução pedida; nitidez final permanece não determinada sem comparação visual.

## Limites e verificação necessária

Não foi executado Revit, RVT, exportação real, panorama de referência ou teste visual. Logo: **confirmado pelo código** é a matemática/validação pretendida; **confirmado por teste** é a existência de checks puros; **não é possível determinar** que as seis imagens reais casem perfeitamente, que o sol/crop exportem como esperado ou que não haja costura visível. O ensaio prioritário deve usar RVT com marcador geométrico/colorido atravessando todas as emendas, teto e piso, e medir desvio angular e de cor.
