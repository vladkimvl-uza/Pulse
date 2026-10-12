# Builds Pulse into .\app using only what ships with Windows (.NET Framework 4.8) plus the
# Roslyn compiler and libraries restored into .\packages / .\lib.
param([switch]$Run, [string]$OutDir = 'app')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc  = Join-Path $root 'packages\Microsoft.Net.Compilers.Toolset.5.9.0\tasks\net472\csc.exe'
if (-not (Test-Path $csc)) { & "$root\tools\restore.ps1" }
$lib  = Join-Path $root 'lib'
$out  = Join-Path $root $OutDir
$obj  = Join-Path $root 'build'
New-Item -ItemType Directory -Force $out, $obj | Out-Null

# 1. Icon (drawn by the same code the app uses for its tray icon)
& $csc -nologo -langversion:latest -out:"$obj\IconGen.exe" -r:System.Drawing.dll "$root\src\IconFactory.cs" "$root\src\Native.cs" "$root\tools\IconGen.cs"
if ($LASTEXITCODE) { throw 'IconGen build failed' }
& "$obj\IconGen.exe" "$obj\pulse.ico" | Out-Null

# 2. Application
$managed = Get-ChildItem $lib -Filter *.dll | Where-Object { $_.Name -ne 'WebView2Loader.dll' }
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','Microsoft.CSharp.dll','System.Xml.dll','System.Management.dll') + ($managed | ForEach-Object { $_.FullName })
$refArgs = $refs | ForEach-Object { "-r:$_" }
# version.txt is the only place the version lives; the installer reads it too
$version = (Get-Content "$root\version.txt" -TotalCount 1).Trim()
@"
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
namespace Pulse { static class BuildInfo { public const string Version = "$version"; } }
"@ | Set-Content "$obj\BuildInfo.g.cs" -Encoding UTF8
$sources = @(Get-ChildItem "$root\src" -Filter *.cs | ForEach-Object { $_.FullName }) + "$obj\BuildInfo.g.cs"
& $csc -nologo -langversion:latest -platform:x64 -target:winexe -optimize+ -nowarn:CS1998 `
    "-win32icon:$obj\pulse.ico" "-win32manifest:$root\app.manifest" "-out:$out\Pulse.exe" @refArgs @sources
if ($LASTEXITCODE) { throw 'Pulse build failed' }

# 3. Runtime files
Copy-Item "$lib\*.dll" $out -Force
if (Test-Path "$out\ui") { Remove-Item "$out\ui" -Recurse -Force }
Copy-Item "$root\ui" "$out\ui" -Recurse -Force
Copy-Item "$obj\pulse.png" "$out\ui\icon.png" -Force
New-Item -ItemType Directory -Force "$out\tools" | Out-Null
Copy-Item "$root\third_party\presentmon\PresentMon.exe", "$root\third_party\presentmon\LICENSE.txt" "$out\tools" -Force

# 4. Config with binding redirects for the NuGet facades LibreHardwareMonitor depends on
$redirects = foreach ($f in $managed) {
    $an = [Reflection.AssemblyName]::GetAssemblyName($f.FullName)
    $tok = ($an.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join ''
    if (-not $tok) { continue }
    "      <dependentAssembly><assemblyIdentity name=`"$($an.Name)`" publicKeyToken=`"$tok`" culture=`"neutral`"/><bindingRedirect oldVersion=`"0.0.0.0-$($an.Version)`" newVersion=`"$($an.Version)`"/></dependentAssembly>"
}
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup>
  <System.Windows.Forms.ApplicationConfigurationSection>
    <add key="DpiAwareness" value="PerMonitorV2"/>
  </System.Windows.Forms.ApplicationConfigurationSection>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
$($redirects -join "`n")
    </assemblyBinding>
  </runtime>
</configuration>
"@ | Set-Content "$out\Pulse.exe.config" -Encoding UTF8

Write-Host "Built $out\Pulse.exe"
if ($Run) { Start-Process "$out\Pulse.exe" -ArgumentList '--dev' }
