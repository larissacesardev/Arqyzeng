# Arqyzeng BIM — desenvolvimento e validação

Plugin C#/WPF para Revit 2026. `src/Arqyzeng.Revit` e seus JSON em `data` são as fontes do plugin; `tests/Arqyzeng.Rendering.Checks` contém as verificações atuais. O novo fluxo compila diretamente esses arquivos. A solução `ArqyzengBIM.sln` contém o plugin, enquanto os checks são executados separadamente.

## Responsabilidades e pré-requisitos

| Etapa | Computador de desenvolvimento (VS Code) | Computador da Larissa |
|---|---|---|
| Implementação | Editar e revisar `src`, `tests` e `data`; preparar cada mudança com um roteiro curto de teste. | Receber os fontes por `git pull` depois que as mudanças forem integradas ao repositório remoto. |
| Checks independentes | Executar com Windows, PowerShell e SDK 8 compatível com [global.json](global.json) (`8.0.425` ou patch posterior selecionado por `latestPatch`). Não exigem Revit nem DLLs. | Repetir os mesmos checks antes do build. |
| Compilação e instalação | Não fazem parte da validação neste computador; sem DLLs 2026, o script informa bloqueio de ambiente. | Usar o mesmo SDK 8 e Revit 2026 anterior a 2026.5, com `Revit.exe`, `RevitAPI.dll` e `RevitAPIUI.dll`; instalar com Revit fechado. |
| Validação no Revit | Não é realizada aqui. | Abrir Revit após instalar e executar o roteiro manual da mudança. |

O alvo permanece `net8.0-windows`, x64 e WPF. O script preserva a restrição de versão dos instaladores existentes e nunca seleciona as DLLs do Revit 2023 automaticamente. Neste computador, a última inspeção encontrou apenas o SDK 10 e o Revit 2023. Os checks estão bloqueados **pela ausência do SDK 8**; isso não é resultado de teste do código. A compilação do plugin fica bloqueada **pela ausência das referências 2026**, o que também não indica defeito do código. Não é necessário instalar Revit neste computador.

## Configurar a pasta do Revit

No computador da Larissa, o projeto usa `C:\Program Files\Autodesk\Revit 2026` como fallback. Defina `REVIT_2026_DIR` para outra instalação do Revit 2026 ou passe `-RevitRoot` ao script. Um build direto também aceita a propriedade MSBuild `RevitInstallDir`:

```powershell
$env:REVIT_2026_DIR = 'D:\Autodesk\Revit 2026'
dotnet build .\src\Arqyzeng.Revit\Arqyzeng.Revit.csproj --configuration Debug
dotnet build .\src\Arqyzeng.Revit\Arqyzeng.Revit.csproj --configuration Debug '-p:RevitInstallDir=D:\Autodesk\Revit 2026'
```

Se a pasta ou as referências estiverem ausentes, o projeto e o script informam o arquivo esperado. O modo `Checks` não faz essa validação. Para `Build` e `Install`, o script também verifica a versão de `Revit.exe`. `Directory.Build.props` direciona `bin` e `obj` para `.artifacts/`, sem gerar artefatos de build em `src` ou `tests`.

## Checks, build e instalação

Execute na raiz do clone em PowerShell. O script descobre a raiz pela própria localização; `-RevitRoot` substitui o fallback e a variável de ambiente. `Checks` depende apenas do SDK 8. `Build` e `Install` executam os checks primeiro, depois validam as referências 2026 e compilam o plugin.

```powershell
.\scripts\dev.ps1 -Action Checks
.\scripts\dev.ps1 -Action Build
.\scripts\dev.ps1 -Action Install
.\scripts\dev.ps1 -Action Install -Configuration Release -RevitRoot 'D:\Autodesk\Revit 2026'
```

No computador de desenvolvimento, o comando principal é `-Action Checks`. O comando direto equivalente, quando o SDK 8 estiver disponível, é:

```powershell
dotnet run --project .\tests\Arqyzeng.Rendering.Checks\Arqyzeng.Rendering.Checks.csproj --configuration Debug --no-launch-profile
```

No modo `Install`, após checks e build aprovados, o script exige Revit fechado, copia toda a saída compilada (inclusive `data` e seus quatro JSON) para uma pasta temporária, confere os arquivos essenciais e só então substitui `%LOCALAPPDATA%\ArqyzengBIM\Revit2026`. O manifesto fica em `%APPDATA%\Autodesk\Revit\Addins\2026\ArqyzengBIM.addin`. A instalação anterior e o manifesto anterior são movidos para `%LOCALAPPDATA%\ArqyzengBIM\install-backups\<identificador>` antes da substituição. Em caso de falha na troca, o script tenta restaurá-los. Ele não cria nem reescreve arquivos de `src`, `tests` ou `data`.

O VS Code oferece as tarefas **Arqyzeng: checks**, **Arqyzeng: build** e **Arqyzeng: instalar** em *Terminal > Run Task*. Use **checks** neste computador; Larissa pode usar as três tarefas no computador com Revit 2026. Os checks cobrem prompts, regras, provedor HTTP simulado e matemática de Panorama; não substituem teste visual ou de integração no Revit.

## Trabalho com Git e em outro computador

Esta pasta foi recebida sem `.git`. Não há branch, commit ou remoto verificável aqui. Para trabalhar com o repositório remoto, obtenha a URL com o responsável e clone **em outra pasta**, sem usar esta como destino:

```powershell
cd C:\caminho\para\projetos
git clone <URL_DO_REPOSITORIO> ArqyzengBIM
cd .\ArqyzengBIM
git status -sb
```

Compare o clone com este workspace antes de transferir as mudanças de infraestrutura; preserve alterações que já existam no remoto. Depois de integrar e publicar as mudanças no repositório remoto pelo fluxo normal da equipe, Larissa pode executar `git pull --ff-only` no clone dela. Ela executa `Checks`, `Build` e `Install` nessa ordem; então abre o Revit para a validação manual. Nenhum script deste projeto faz commit, push ou pull. Não há suporte implementado ou validado para Revit 2023.

## Roteiro manual por alteração

Para cada mudança, registre o comportamento esperado, um modelo/vista de referência, os passos de execução e uma evidência do resultado. Separe sempre **implementado** (código alterado e revisado), **compilado** (checks e build aprovados no computador da Larissa) e **validado no Revit** (observação manual registrada); uma etapa não comprova a seguinte.

Para **esta alteração de infraestrutura**, o roteiro curto no computador da Larissa é:

1. Fazer pull do commit revisado, fechar o Revit e executar `-Action Checks`, `-Action Build` e `-Action Install`. Confirmar saída em `.artifacts/`, DLL, quatro JSON e manifesto em `Addins\2026`.
2. Se havia instalação, confirmar que plugin e manifesto anteriores foram guardados em `install-backups`. Confirmar que os arquivos de `src`, `tests` e `data` do clone não foram reescritos pelo script.
3. Abrir o Revit 2026, acionar **Testar plugin** e abrir as abas Print e Panorama com um RVT de teste. Registrar se carregam; comparar a aparência com a versão anterior, sem interpretar esta etapa como correção visual.

Se checks, build ou instalação falharem, parar e registrar comando, mensagem e etapa. Não marcar como validado no Revit até concluir o passo 3.

## Instaladores legados

`Instalar_Arqyzeng_Panorama_v8.2.ps1` e `Instalar_Arqyzeng_Primeiro_Plugin.ps1` são legados. Eles contêm cópias embutidas de fontes e podem sobrescrever correções em `src`, `tests`, `data` e configurações. O instalador Primeiro Plugin contém uma versão anterior ao Print atual. Use `scripts/dev.ps1` para checks, build e instalação do código presente no workspace; não execute os instaladores legados para manter este código.
