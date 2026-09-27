# Builds the portable folder:
#
#   Caret-Portable\
#     Caret.exe     small launcher (src\Caret.Launcher) that starts app\Caret.exe
#     README.md     docs\user-guide.md
#     app\          the WinUI app, self-contained (.NET and the Windows App SDK included)
#     Data\         created on first run: settings, recent files, WebView2 profile
#
# Needs Visual Studio 2022 (or its Build Tools) with the .NET desktop workload, and the editor bundle
# built first (yarn build in src\Caret.Editor). MSBuild is found with vswhere, so a normal
# PowerShell works. Used by .github\workflows\portable.yml.
param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',
    [string] $Output = (Join-Path $PSScriptRoot '..\out')
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Output = [IO.Path]::GetFullPath($Output)
$dest = Join-Path $Output 'Caret-Portable'
$app = Join-Path $dest 'app'
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

$msbuild = (Get-Command msbuild -ErrorAction SilentlyContinue).Source
if (-not $msbuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }
}
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio 2022 or its Build Tools with the .NET desktop workload.' }

& $msbuild (Join-Path $root 'src\Caret.App\Caret.App.csproj') -restore -t:Publish -m -v:minimal `
    -p:Configuration=Release -p:Platform=$Platform "-p:PublishDir=$app\"
if ($LASTEXITCODE) { throw "Publishing the app failed ($LASTEXITCODE)" }

$launcherOut = Join-Path $Output 'launcher'
dotnet build (Join-Path $root 'src\Caret.Launcher\Caret.Launcher.csproj') -c Release -o $launcherOut -v:minimal
if ($LASTEXITCODE) { throw "Building the launcher failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $launcherOut 'Caret.exe') $dest
Copy-Item (Join-Path $root 'docs\user-guide.md') (Join-Path $dest 'README.md')

# The Windows App SDK ships its own UI text (text box menus, accessibility names) for about 90
# languages, one folder each. Caret itself is English, French and Spanish, so keep those; any other
# Windows language falls back to English, like the rest of Caret. Only folders that hold nothing but
# resource files (.mui, .resources.dll) are touched.
$keep = 'en', 'en-us', 'en-gb', 'fr', 'fr-fr', 'fr-ca', 'es', 'es-es', 'es-mx'
$removed = 0
foreach ($dir in Get-ChildItem $app -Directory) {
    if ($dir.Name -cnotmatch '^[a-z]{2,3}(-[A-Za-z0-9]+){0,2}$' -or $keep -contains $dir.Name.ToLowerInvariant()) { continue }
    $files = @(Get-ChildItem $dir.FullName -Recurse -File)
    if ($files.Count -eq 0 -or @($files | Where-Object { $_.Name -notlike '*.mui' -and $_.Name -notlike '*.resources.dll' }).Count -gt 0) { continue }
    Remove-Item $dir.FullName -Recurse -Force
    $removed++
}

foreach ($required in 'Caret.exe', 'README.md', 'app\Caret.exe', 'app\Resources\Statics\index.html', 'app\Strings\en\AppResources.resw', 'app\markitdown-plugins\markitdown_caret_email\__init__.py', 'app\markitdown-plugins\markitdown_caret_email\rules\en.json') {
    if (-not (Test-Path (Join-Path $dest $required))) { throw "$required is missing from the portable folder" }
}
$size = (Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Removed $removed unused language folders."
"Portable folder: {0:N0} MB. Top level: {1}" -f $size, ((Get-ChildItem $dest | ForEach-Object Name) -join ', ')
