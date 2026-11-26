param(
    [switch]$Test,
    [switch]$Package,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$compiler = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe')
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) { throw 'The .NET Framework C# compiler is unavailable.' }

[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$sources = @('AntigravityProxyLauncher.cs', 'Network.cs', 'BrowserProxy.cs', 'LauncherForm.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$compilerOptions = @('/nologo', '/codepage:65001', '/platform:anycpu', '/optimize+',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll')
$launcher = Join-Path $output 'AntigravityProxyLauncher.exe'
& $compiler @compilerOptions '/target:winexe' "/out:$launcher" @sources
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }

if ($Test) {
    $smokeTest = Join-Path $output 'SmokeTests.exe'
    $testSource = Join-Path $PSScriptRoot 'SmokeTests.cs'
    & $compiler @compilerOptions '/target:exe' '/main:SmokeTests' "/out:$smokeTest" @sources $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test compilation failed.' }
    & $smokeTest
    if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed.' }
}

if ($Package) {
    $archive = Join-Path $output 'Antigravity-Proxy-Launcher-Windows.zip'
    $license = Join-Path $PSScriptRoot 'LICENSE'
    $helpFile = Join-Path $PSScriptRoot 'README.md'
    Compress-Archive -LiteralPath @($launcher, $license, $helpFile) -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = $archive + '.sha256'
    [IO.File]::WriteAllText($manifest, $hash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine)
    Write-Output $archive
}

Write-Output $launcher
