param(
    [ValidateSet('Build', 'Package')]
    [string]$Mode = 'Build'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$nativeBuild = Join-Path $root 'artifacts\native-build'
$project = Join-Path $root 'src\ReviewApp\ReviewApp.csproj'
$nativeDll = Join-Path $nativeBuild 'src\ReviewCore\Release\ReviewCore.dll'

# Some launch environments provide both Path and PATH. MSBuild's C++ task rejects
# that duplicate, so normalize the process environment before invoking CMake.
$processPath = $env:Path
[System.Environment]::SetEnvironmentVariable('PATH', $null, 'Process')
[System.Environment]::SetEnvironmentVariable('Path', $processPath, 'Process')

function Invoke-Checked([string]$program, [string[]]$arguments) {
    & $program @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$program failed with exit code $LASTEXITCODE"
    }
}

try {
    Write-Host 'Building native C++ core...' -ForegroundColor Cyan
    Invoke-Checked 'cmake' @('-S', $root, '-B', $nativeBuild,
        '-G', 'Visual Studio 17 2022', '-A', 'x64', '-DBUILD_NATIVE_CORE=ON', '-DBUILD_WPF_APP=OFF')
    Invoke-Checked 'cmake' @('--build', $nativeBuild, '--config', 'Release')

    if ($Mode -eq 'Build') {
        $output = Join-Path $root 'artifacts\app'
        Write-Host 'Building WPF application...' -ForegroundColor Cyan
        Invoke-Checked 'dotnet' @('build', $project, '-c', 'Release', "-p:OutputPath=$output")
        Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $output 'ReviewCore.dll') -Force
        Write-Host "Ready: $(Join-Path $output 'ImageReviewTool.exe')" -ForegroundColor Green
        exit 0
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
    $releaseName = "ImageReviewTool-win-x64-$stamp"
    $releaseRoot = Join-Path $root 'artifacts\releases'
    $releaseFolder = Join-Path $releaseRoot $releaseName
    $zipPath = Join-Path $releaseRoot "$releaseName.zip"
    [System.IO.Directory]::CreateDirectory($releaseFolder) | Out-Null

    Write-Host 'Publishing self-contained Windows x64 application...' -ForegroundColor Cyan
    Invoke-Checked 'dotnet' @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishTrimmed=false',
        '-o', $releaseFolder)
    Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $releaseFolder 'ReviewCore.dll') -Force
    Compress-Archive -Path (Join-Path $releaseFolder '*') -DestinationPath $zipPath
    Write-Host "Release folder: $releaseFolder" -ForegroundColor Green
    Write-Host "Release ZIP: $zipPath" -ForegroundColor Green
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
