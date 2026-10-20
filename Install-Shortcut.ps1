[CmdletBinding()]
param([string]$Name='ChatGPT Cinematic Launcher')
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($Name) -or $Name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0){throw 'Choose a valid shortcut name.'}
$target=Join-Path $PSScriptRoot 'dist\ChatGPT.exe'
if(-not (Test-Path -LiteralPath $target)){throw 'Run Build.ps1 first.'}
$desktop=[Environment]::GetFolderPath('DesktopDirectory')
$link=Join-Path $desktop ($Name+'.lnk')
if(Test-Path -LiteralPath $link){throw ('Shortcut already exists; choose another -Name. Nothing was replaced: '+$link)}
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($link)
$shortcut.TargetPath=$target
$shortcut.WorkingDirectory=Split-Path $target -Parent
$shortcut.Description='Community cinematic launcher; requires official desktop client and Codex Dream Skin.'
$shortcut.IconLocation=$target+',0'
$shortcut.Save()
Write-Output ('Created shortcut: '+$link)