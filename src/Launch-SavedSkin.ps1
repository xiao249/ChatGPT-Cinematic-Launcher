param([Parameter(Mandatory=$true)][string]$StatusPath,[switch]$ValidateOnly,[switch]$RestartExisting)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$entryRoot=$PSScriptRoot
$stateRoot=Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
$engine=Join-Path $stateRoot 'engine'
$codex=$null
function Test-SavedSkinHiddenOutput {
    param([AllowEmptyCollection()][object[]]$Output)
    try {
        $payload=($Output -join "`n")|ConvertFrom-Json -ErrorAction Stop
        $targets=@($payload.targets)
        if($targets.Count -eq 0){return $false}
        foreach($target in $targets){
            $result=$target.result
            if($result.documentHidden -isnot [bool] -or -not $result.documentHidden -or
                $result.documentVisibility -cne 'hidden' -or
                $result.readiness.documentPass -isnot [bool] -or $result.readiness.documentPass){return $false}
        }
        return $true
    } catch {return $false}
}
function Report([string]$phase,[string]$message,[hashtable]$extra=@{}) {
    $record=@{phase=$phase;message=$message;updatedAt=[DateTime]::UtcNow.ToString('o')}
    foreach($key in $extra.Keys){$record[$key]=$extra[$key]}
    $tmp=$StatusPath+'.tmp'
    [IO.File]::WriteAllText($tmp,($record|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tmp -Destination $StatusPath -Force
}
try {
    Report 'checking' '正在检查已保存的背景…'
    . (Join-Path $engine 'scripts\common-windows.ps1')
    . (Join-Path $engine 'scripts\theme-windows.ps1')
    $codex=Get-DreamSkinCodexInstall
    $paths=Get-DreamSkinThemePaths -StateRoot $stateRoot
    if($ValidateOnly){
        $null=Get-DreamSkinNodeRuntime
        $theme=Read-DreamSkinTheme -ThemeDirectory $paths.Active
        $hash=(Get-FileHash -LiteralPath $theme.ImagePath).Hash
        Report 'validated' '启动检查通过。' @{themeId=$theme.Theme.id;imageHash=$hash;version=$codex.Version}
        exit 0
    }
    $saved=Read-DreamSkinState -Path (Join-Path $stateRoot 'state.json')
    $port=if($saved -and $saved.port){[int]$saved.port}else{9335}
    $endpoint=Get-DreamSkinVerifiedCdpIdentity -Port $port -Codex $codex
    $clients=@(Get-DreamSkinCodexProcesses -Codex $codex)
    $restart=[bool]$RestartExisting
    $needsRestart=$null -eq $endpoint
    if($null -ne $endpoint){
        $node=Get-DreamSkinNodeRuntime
        $visibility=Invoke-DreamSkinNative -FilePath $node.Path -ArgumentList @((Join-Path $engine 'scripts\injector.mjs'),'--verify','--port',[string]$port,'--browser-id',$endpoint.BrowserId,'--theme-dir',$paths.Active,'--timeout-ms','4000') -SeparateStderr
        if(Test-SavedSkinHiddenOutput -Output $visibility.Output){$needsRestart=$true}
    }
    if($needsRestart -and $clients.Count -gt 0){
        if($RestartExisting){$restart=$true}
        else {
            Report 'confirming' '请确认是否重新打开 ChatGPT。'
            $shell=New-Object -ComObject WScript.Shell
            $choice=$shell.Popup("需要重新打开 ChatGPT 才能加载背景。现在重新打开吗？`n当前进行中的任务会中断。",0,'ChatGPT 背景启动器',52+262144)
            if($choice -ne 6){Report 'cancelled' '已取消重新打开。';exit 0}
            $restart=$true
        }
    }
    Report 'starting' '正在打开 ChatGPT 并加载背景…'
    $token=[guid]::NewGuid().ToString('N')
    $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $start=Join-Path $engine 'scripts\start-dream-skin.ps1'
    $arguments='-NoLogo -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File "'+$start+'" -ResultToken '+$token+' -OperationLockTimeoutMilliseconds 15000'
    if($restart){$arguments+=' -RestartExisting'}
    $logBase=Join-Path $entryRoot ('logs\launch-'+$token)
    $child=Start-Process -FilePath $powershell -ArgumentList $arguments -WorkingDirectory $engine -WindowStyle Hidden -PassThru -RedirectStandardOutput ($logBase+'.log') -RedirectStandardError ($logBase+'.error.log')
    $resultFile=Get-DreamSkinStartResultPath -StateRoot $stateRoot -Token $token
    $deadline=[DateTime]::UtcNow.AddSeconds(210)
    while([DateTime]::UtcNow -lt $deadline){
        if(Test-Path -LiteralPath $resultFile){
            $result=Read-DreamSkinStartResult -StateRoot $stateRoot -Token $token
            if($result.outcome -ne 'success'){throw ('背景没有通过启动检查（'+$result.category+'）。详细记录：'+$logBase+'.error.log')}
            # The upstream startup has already validated image metadata and Safe CSS.
            $theme=Read-DreamSkinTheme -ThemeDirectory $paths.Active -SkipImageMetadata
            $hash=(Get-FileHash -LiteralPath $theme.ImagePath).Hash
            Report 'complete' 'ChatGPT 和背景已打开。' @{themeId=$theme.Theme.id;imageHash=$hash;version=$codex.Version;token=$token}
            exit 0
        }
        if($child.HasExited){throw ('背景启动未完成。详细记录：'+$logBase+'.error.log')}
        Start-Sleep -Milliseconds 400
    }
    throw ('加载背景超过预期时间。详细记录：'+$logBase+'.error.log')
} catch {
    $message=$_.Exception.Message
    try{Report 'error' $message}catch{}
    if(-not $ValidateOnly -and $null -ne $codex){
        try{if(@(Get-DreamSkinCodexProcesses -Codex $codex).Count -eq 0){$null=Start-DreamSkinCodex -Codex $codex}}catch{}
    }
    exit 1
}
