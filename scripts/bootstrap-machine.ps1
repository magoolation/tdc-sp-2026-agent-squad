#Requires -Version 7.0
<#
.SYNOPSIS
    Põe uma máquina nova em condições de rodar o Agent Squad.

.DESCRIPTION
    Não há segredo para copiar. O projeto não guarda chave: o Foundry usa Entra ID
    (`disableLocalAuth`), o GitHub usa o token do `gh`, e o Copilot usa a conta do GitHub
    logada. Os três valores em user-secrets são configuração, não credencial — endpoint e
    nomes de repositório.

    O que NÃO se transporta são justamente os tokens: os do `gh`, do `az` e do Copilot são
    presos à máquina e ao usuário, e a forma certa de tê-los lá é autenticar lá. Este script
    conduz isso e verifica o resultado.

.PARAMETER FoundryEndpoint
    O endpoint do projeto Foundry. O padrão é o que já está em uso.

.PARAMETER Owner
    Dono do repositório alvo da demo.

.PARAMETER Repository
    Nome do repositório alvo da demo.

.PARAMETER WorkRoot
    Raiz de trabalho dos worktrees. Curta de propósito: caminho longo quebra o build.

.EXAMPLE
    ./scripts/bootstrap-machine.ps1
    Roda tudo com os padrões e termina no doctor.
#>
[CmdletBinding()]
param(
    [string] $FoundryEndpoint = 'https://fdyvmwze6tbmkbki.services.ai.azure.com/api/projects/agent-squad',
    [string] $Owner = 'magoolation',
    [string] $Repository = 'squad-demo-catalogo',
    [string] $WorkRoot = 'C:\squad'
)

$ErrorActionPreference = 'Stop'

function Write-Step { param([string] $Text) Write-Host "`n=== $Text ===" -ForegroundColor Cyan }
function Write-Ok { param([string] $Text) Write-Host "  [ok]  $Text" -ForegroundColor Green }
function Write-Gap { param([string] $Text) Write-Host "  [!!]  $Text" -ForegroundColor Yellow }

$cli = Join-Path $PSScriptRoot '..' 'src' 'AgentSquad.Cli'

# --- 1. ferramentas -------------------------------------------------------
# Só relata. Instalar por conta própria, sem a pessoa ver, é exatamente o tipo de
# surpresa que não se quer numa máquina emprestada horas antes de uma palestra.
Write-Step '1. Ferramentas'

$required = @(
    @{ Name = 'dotnet'; Hint = 'winget install Microsoft.DotNet.SDK.10'; Expect = '10.0' }
    @{ Name = 'git'; Hint = 'winget install Git.Git'; Expect = '2.' }
    @{ Name = 'gh'; Hint = 'winget install GitHub.cli'; Expect = '2.' }
    @{ Name = 'az'; Hint = 'winget install Microsoft.AzureCLI'; Expect = '2.' }
    @{ Name = 'copilot'; Hint = 'npm install -g @github/copilot'; Expect = '1.' }
)

$missing = @()

foreach ($tool in $required) {
    $found = Get-Command $tool.Name -ErrorAction SilentlyContinue

    if ($found) {
        Write-Ok "$($tool.Name) — $($found.Source)"
    }
    else {
        Write-Gap "$($tool.Name) não encontrado → $($tool.Hint)"
        $missing += $tool.Name
    }
}

if ($missing.Count -gt 0) {
    throw "Instale primeiro: $($missing -join ', '). Depois rode este script de novo."
}

# --- 2. Smart App Control -------------------------------------------------
# A razão de existir deste script. Bloqueia todo assembly recém-compilado, e o sintoma
# é uma FileLoadException apontando para o seu próprio dll.
Write-Step '2. Smart App Control'

$sac = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' `
        -Name VerifiedAndReputablePolicyState -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState

switch ($sac) {
    1 {
        Write-Gap 'LIGADO e bloqueando. O projeto NÃO vai rodar nesta máquina.'
        Write-Host '        Segurança do Windows → Controle de aplicativos e navegador →' -ForegroundColor Yellow
        Write-Host '        Configurações do Controle Inteligente de Aplicativos → Desativado' -ForegroundColor Yellow
        Write-Host '        Atenção: desligar é IRREVERSÍVEL sem reinstalar o Windows.' -ForegroundColor Yellow
        throw 'Smart App Control está bloqueando binários locais.'
    }
    2 { Write-Gap 'Em avaliação. Pode virar bloqueio sozinho — considere desligar antes da palestra.' }
    default { Write-Ok 'Desligado ou ausente.' }
}

# --- 3. autenticações -----------------------------------------------------
Write-Step '3. Autenticações (é aqui que moram os tokens — por isso se faz nesta máquina)'

if (gh auth status 2>&1 | Select-String -Quiet 'Logged in') {
    Write-Ok "GitHub: $(gh api user --jq '.login' 2>$null)"
}
else {
    Write-Gap 'GitHub não autenticado. Rodando gh auth login…'
    gh auth login --hostname github.com --git-protocol https --scopes 'repo,read:org,workflow' --web
}

$account = az account show --query 'user.name' -o tsv 2>$null

if ($LASTEXITCODE -eq 0 -and $account) {
    Write-Ok "Azure: $account"
}
else {
    Write-Gap 'Azure não autenticado. Rodando az login…'
    az login --use-device-code | Out-Null
}

# O Copilot CLI usa a conta do GitHub já logada; o diretório é isolado por execução.
$copilotHome = Join-Path $WorkRoot 'copilot-home'
New-Item -ItemType Directory -Force -Path $copilotHome | Out-Null
Write-Ok "COPILOT_HOME preparado em $copilotHome"

# --- 4. configuração ------------------------------------------------------
# Os três valores que as pessoas chamam de "meus secrets". Nenhum é credencial.
Write-Step '4. Configuração (user-secrets)'

dotnet user-secrets --project $cli set 'Foundry:ProjectEndpoint' $FoundryEndpoint | Out-Null
dotnet user-secrets --project $cli set 'GitHub:Owner' $Owner | Out-Null
dotnet user-secrets --project $cli set 'GitHub:Repository' $Repository | Out-Null

Write-Ok "Foundry:ProjectEndpoint = $FoundryEndpoint"
Write-Ok "GitHub:Owner            = $Owner"
Write-Ok "GitHub:Repository       = $Repository"

New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null
Write-Ok "Raiz de trabalho em $WorkRoot"

# --- 5. build -------------------------------------------------------------
# Compilar AGORA, não no palco: as demos usam --no-build e não podem esperar compilação.
Write-Step '5. Build em Release (para as demos rodarem com --no-build)'

dotnet build (Join-Path $PSScriptRoot '..' 'AgentSquad.slnx') -c Release --nologo

if ($LASTEXITCODE -ne 0) {
    throw 'O build falhou. Sem isso não adianta seguir.'
}

Write-Ok 'Build concluído.'

# --- 6. prova ------------------------------------------------------------
Write-Step '6. Prova de ponta a ponta (chamada real a cada modelo)'

dotnet run --project $cli -c Release --no-build -- doctor --probe-models

Write-Host "`nSe o doctor passou, a máquina está pronta." -ForegroundColor Green
Write-Host 'Vale rodar também: dotnet run --project src/AgentSquad.Cli -c Release --no-build -- doctor --probe-copilot'
