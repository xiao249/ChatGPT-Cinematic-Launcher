[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$framework=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw 'Install the 64-bit .NET Framework 4.x desktop runtime/compiler on Windows first.'}
$destination=Join-Path $PSScriptRoot 'dist'
$null=New-Item -ItemType Directory -Path $destination,(Join-Path $destination 'assets') -Force
$arguments=@('/nologo','/optimize+','/target:winexe','/platform:x64','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Management.dll','/reference:System.Xaml.dll',('/reference:'+(Join-Path $framework 'WPF\PresentationFramework.dll')),('/reference:'+(Join-Path $framework 'WPF\PresentationCore.dll')),('/reference:'+(Join-Path $framework 'WPF\WindowsBase.dll')),('/out:'+(Join-Path $destination 'ChatGPT.exe')),(Join-Path $PSScriptRoot 'src\Launcher.cs'),(Join-Path $PSScriptRoot 'src\CinematicSplash.cs'))
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Launch-SavedSkin.ps1') -Destination $destination -Force
if(-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'assets\artwork-v2.jpg'))){& (Join-Path $PSScriptRoot 'scripts\Generate-DefaultArtwork.ps1')}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\Splash.xaml'),(Join-Path $PSScriptRoot 'assets\artwork-v2.jpg') -Destination (Join-Path $destination 'assets') -Force
Write-Output ('Built: '+(Join-Path $destination 'ChatGPT.exe'))