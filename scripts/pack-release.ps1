$ErrorActionPreference = "Stop"
$Version = "1.0.2"
$env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $ProjectRoot "publish"
$ReleaseDir = Join-Path $ProjectRoot "releases"

Push-Location $ProjectRoot
try {
    dotnet publish GestionCommerciale.csproj `
        -c Release `
        --self-contained `
        -r win-x64 `
        -o $PublishDir `
        /p:Version=$Version

    $IconPath = Join-Path $ProjectRoot "Assets\faturati.ico"

    vpk pack `
        --packId Sonlighting.GestionCommerciale `
        --packTitle "SAHRAOUI" `
        --packVersion $Version `
        --packDir $PublishDir `
        --mainExe GestionCommerciale.exe `
        --icon $IconPath `
        --outputDir $ReleaseDir
}
finally {
    Pop-Location
}

Write-Host "Release artifacts written to $ReleaseDir"
