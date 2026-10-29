# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 HakuChya (HakumomoHacha)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project PulsoidVRC.Tests/PulsoidVRC.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish PulsoidVRC/PulsoidVRC.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o release
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination release/README.md
    Copy-Item -LiteralPath LICENSE -Destination release/LICENSE
    Copy-Item -LiteralPath NOTICE -Destination release/NOTICE
    if (Test-Path -LiteralPath docs) {
        New-Item -ItemType Directory -Path release/docs -Force | Out-Null
        Copy-Item -Path docs/* -Destination release/docs -Recurse -Force
    }
    if (Test-Path -LiteralPath '快速开始.html') { Copy-Item -LiteralPath '快速开始.html' -Destination release/快速开始.html }
    $nugetCache = ((dotnet nuget locals global-packages --list) -replace '^[^:]+:\s*', '').Trim()
    $runtimeVersion = ([xml](Get-Content -LiteralPath PulsoidVRC/PulsoidVRC.csproj -Raw)).Project.PropertyGroup.RuntimeFrameworkVersion
    New-Item -ItemType Directory -Path release/licenses -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $nugetCache "microsoft.netcore.app.runtime.win-x64/$runtimeVersion/LICENSE.TXT") -Destination release/licenses/DOTNET-LICENSE.txt
    Copy-Item -LiteralPath (Join-Path $nugetCache "microsoft.netcore.app.runtime.win-x64/$runtimeVersion/THIRD-PARTY-NOTICES.TXT") -Destination release/licenses/DOTNET-THIRD-PARTY-NOTICES.txt
    Copy-Item -LiteralPath (Join-Path $nugetCache "microsoft.windowsdesktop.app.runtime.win-x64/$runtimeVersion/LICENSE") -Destination release/licenses/WINDOWSDESKTOP-LICENSE.txt
    Write-Host 'Done: release/PulsoidVRC.exe'
}
finally { Pop-Location }
