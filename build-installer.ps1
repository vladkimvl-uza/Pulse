# Builds the single-file installer dist\Pulse-Setup-<version>.exe:
#   the app (built fresh into build\stage) -> payload.zip, the setup pages -> setup-ui.zip,
#   both embedded with the WebView2 assemblies into one exe (setup\*.cs).
# -Preview builds build\Pulse-Setup-preview.exe without the admin manifest, for design checks with --demo.
param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc  = Join-Path $root 'packages\Microsoft.Net.Compilers.Toolset.5.9.0\tasks\net472\csc.exe'
$lib  = Join-Path $root 'lib'
$obj  = Join-Path $root 'build'
$version = (Get-Content "$root\version.txt" -TotalCount 1).Trim()
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function New-Zip($from, $to) {
    if (Test-Path $to) { Remove-Item $to -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($from, $to, [IO.Compression.CompressionLevel]::Optimal, $false)
}

# 1. The application, into a staging folder (the running copy in .\app may be locked)
$stage = Join-Path $obj 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
& "$root\build.ps1" -OutDir 'build\stage' | Out-Host
New-Zip $stage "$obj\payload.zip"

# 2. Setup pages
New-Zip "$root\setup\ui" "$obj\setup-ui.zip"

# 3. Setup.exe
if ($Preview) {
    (Get-Content "$root\setup\app.manifest" -Raw).Replace('requireAdministrator', 'asInvoker') | Set-Content "$obj\setup-preview.manifest" -Encoding UTF8
    $manifest = "$obj\setup-preview.manifest"; $exe = "$obj\Pulse-Setup-preview.exe"
} else {
    New-Item -ItemType Directory -Force "$root\dist" | Out-Null
    $manifest = "$root\setup\app.manifest"; $exe = "$root\dist\Pulse-Setup-$version.exe"
}
$refs = 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','Microsoft.CSharp.dll',
        'System.IO.Compression.dll','System.IO.Compression.FileSystem.dll',"$lib\Microsoft.Web.WebView2.Core.dll","$lib\Microsoft.Web.WebView2.WinForms.dll"
$res = @("$obj\payload.zip,payload.zip", "$obj\setup-ui.zip,setup-ui.zip", "$lib\WebView2Loader.dll,WebView2Loader.dll",
         "$lib\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll", "$lib\Microsoft.Web.WebView2.WinForms.dll,Microsoft.Web.WebView2.WinForms.dll")
@"
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
namespace PulseSetup { static class BuildInfo { public const string Version = "$version"; } }
"@ | Set-Content "$obj\SetupInfo.g.cs" -Encoding UTF8
$sources = @((Get-ChildItem "$root\setup" -Filter *.cs).FullName) + "$root\src\Native.cs" + "$root\src\Device.cs" + "$obj\SetupInfo.g.cs"
& $csc -nologo -langversion:latest -platform:x64 -target:winexe -optimize+ -nowarn:CS1998 `
    "-win32icon:$obj\pulse.ico" "-win32manifest:$manifest" "-out:$exe" `
    @($refs | ForEach-Object { "-r:$_" }) @($res | ForEach-Object { "-resource:$_" }) @sources
if ($LASTEXITCODE) { throw 'Setup build failed' }
"Built {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)
