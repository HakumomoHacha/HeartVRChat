# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 HakuChya (HakumomoHacha)

param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $appVersion = ([xml](Get-Content PulsoidVRC/PulsoidVRC.csproj -Raw)).Project.PropertyGroup.Version
    $coreVersion = ([xml](Get-Content PulsoidVRC.Core/PulsoidVRC.Core.csproj -Raw)).Project.PropertyGroup.Version
    if ($appVersion -ne $coreVersion) { throw 'Application and NuGet package versions must match.' }
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
    if (!(Test-Path .git)) { throw 'Package from a Git checkout so the matching source archive can be included.' }
    if (!$SkipBuild) { & ./build.ps1 }
    if (!(Test-Path release/PulsoidVRC.exe)) { throw 'Portable application has not been built.' }
    foreach ($file in @('LICENSE', 'NOTICE', 'README.md')) {
        Copy-Item -LiteralPath $file -Destination (Join-Path release $file) -Force
    }
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    dotnet pack PulsoidVRC.Core/PulsoidVRC.Core.csproj -c Release -p:ContinuousIntegrationBuild=true "-p:RepositoryCommit=$commit" -o artifacts --nologo
    if ($LASTEXITCODE -ne 0) { throw 'NuGet packing failed.' }
    $portable = "artifacts/HeartVRChat-$appVersion-Windows-x64.zip"
    $source = "artifacts/HeartVRChat-$appVersion-Source.zip"
    $package = "artifacts/HeartVRChat.Core.$coreVersion.nupkg"
    Compress-Archive -Path release/* -DestinationPath $portable -Force
    git archive --format=zip --prefix=HeartVRChat-Source/ -o $source HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    $checksums = @($portable, $source, $package) | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $(Split-Path -Leaf $_)"
    }
    [IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'artifacts/SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
    Write-Host "Packaged HeartVRChat $appVersion from $commit"
}
finally { Pop-Location }
