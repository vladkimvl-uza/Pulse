# Fetches the one build tool that is not in the repository: the Roslyn C# compiler (MIT) from nuget.org,
# unpacked to packages\Microsoft.Net.Compilers.Toolset.<version>. Runtime libraries are already in lib\.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$id = 'Microsoft.Net.Compilers.Toolset'; $ver = '5.9.0'
$dir = Join-Path $root "packages\$id.$ver"
if (Test-Path "$dir\tasks\net472\csc.exe") { "compiler already restored"; return }
New-Item -ItemType Directory -Force $dir | Out-Null
$nupkg = Join-Path $env:TEMP "$id.$ver.nupkg"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/$($id.ToLower())/$ver/$($id.ToLower()).$ver.nupkg" -OutFile $nupkg
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $dir)
Remove-Item $nupkg
"restored $id $ver"
