# Builds the portable folder:
#
#   Caret-Portable\
#     Caret.exe     small launcher (Dev\Caret.Launcher) that starts app\Caret.exe
#     README.md     PORTABLE.md, the user guide
#     app\          the WinUI app, self-contained (.NET and the Windows App SDK included)
#     Data\         created on first run: settings, recent files, WebView2 profile
#
# Needs Visual Studio 2022's MSBuild on PATH (a Developer PowerShell) and the editor bundle built
# first (yarn build in Dev\Typedown.Editor). Used by .github\workflows\portable.yml.
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

msbuild (Join-Path $root 'Dev\Typedown.WinUI\Typedown.WinUI.csproj') -restore -t:Publish -m -v:minimal `
    -p:Configuration=Release -p:Platform=$Platform "-p:PublishDir=$app\"
if ($LASTEXITCODE) { throw "Publishing the app failed ($LASTEXITCODE)" }

$launcherOut = Join-Path $Output 'launcher'
dotnet build (Join-Path $root 'Dev\Caret.Launcher\Caret.Launcher.csproj') -c Release -o $launcherOut -v:minimal
if ($LASTEXITCODE) { throw "Building the launcher failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $launcherOut 'Caret.exe') $dest
Copy-Item (Join-Path $root 'PORTABLE.md') (Join-Path $dest 'README.md')

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

foreach ($required in 'Caret.exe', 'README.md', 'app\Caret.exe', 'app\Resources\Statics\index.html', 'app\Strings\en\AppResources.resw') {
    if (-not (Test-Path (Join-Path $dest $required))) { throw "$required is missing from the portable folder" }
}
$size = (Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Removed $removed unused language folders."
"Portable folder: {0:N0} MB. Top level: {1}" -f $size, ((Get-ChildItem $dest | ForEach-Object Name) -join ', ')
