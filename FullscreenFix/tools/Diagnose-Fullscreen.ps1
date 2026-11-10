#Requires -Version 5.1
<#
.SYNOPSIS
  EFT 独占全屏诊断 / 隔离工具（需要管理员时才用 Stop*/Full/Restore）

.DESCRIPTION
  已证实：在本机，任何普通进程调用 IDXGISwapChain::SetFullscreenState 都会失败
  （0x887A0022 DXGI_ERROR_NOT_CURRENTLY_AVAILABLE 或 0x80004005 E_FAIL），
  ChangeDisplaySettingsEx 连"什么都不改"的空操作都被拒（ERROR_ACCESS_DENIED）。
  游戏因此每次启动都从"全屏"掉回"无边框"。

  本脚本用来隔离"到底是谁在挡路"，按嫌疑排序：
    1) 网易 GameViewer / UU 远程串流的虚拟显示驱动（IndirectKmd + Root\GameViewerIddDriver）
    2) RTSS（RivaTuner Statistics Server，钩 DXGI）
  关掉它们之后跑一次独立探针，看 SetFullscreenState 是否变成成功。

.PARAMETER Action
  Report          只报告当前状态，不改任何东西（默认）
  StopGameViewer  停止 GameViewer 服务与进程，并禁用其虚拟显示适配器，然后跑探针
  StopRTSS        结束 RTSS 与 RTSSHooksLoader64，然后跑探针
  Test            只跑一次独立探针，报告 SetFullscreenState 的真实结果
  Full            StopGameViewer + StopRTSS + 探针
  Restore         把 StopGameViewer 的改动全部还原（重新启用适配器、启动服务）

.EXAMPLE
  # 普通权限就能跑
  .\Diagnose-Fullscreen.ps1 -Action Report
  .\Diagnose-Fullscreen.ps1 -Action Test

.EXAMPLE
  # 需要管理员
  .\Diagnose-Fullscreen.ps1 -Action Full
  .\Diagnose-Fullscreen.ps1 -Action Restore
#>
param(
    [ValidateSet('Report', 'StopGameViewer', 'StopRTSS', 'Test', 'Full', 'Restore')]
    [string]$Action = 'Report'
)

$ErrorActionPreference = 'Continue'

$Root     = Split-Path -Parent $PSScriptRoot
$ProbeDir = Join-Path $Root 'work\fse11'
$ProbeExe = Join-Path $ProbeDir 'bin\Release\net8.0\fse11.exe'
$ProbeTxt = Join-Path $ProbeDir 'fse11.txt'
$GvId     = 'ROOT\DISPLAY\0000'
$StateFile = Join-Path $env:TEMP 'eft-fullscreen-diag-state.txt'

function Section([string]$t) { Write-Host ""; Write-Host "===== $t =====" -ForegroundColor Cyan }
function Ok([string]$t)      { Write-Host "  [OK]   $t" -ForegroundColor Green }
function Warn([string]$t)    { Write-Host "  [注意] $t" -ForegroundColor Yellow }
function Bad([string]$t)     { Write-Host "  [失败] $t" -ForegroundColor Red }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Show-Report {
    Section '身份与权限'
    if (Test-Admin) { Ok '当前是管理员' }
    else { Warn '当前不是管理员 —— Stop*/Full/Restore 会失败，但 Report/Test 不受影响' }
    Write-Host "  用户: $env:USERNAME   会话: $([System.Diagnostics.Process]::GetCurrentProcess().SessionId)"

    Section '显示适配器'
    try {
        Get-PnpDevice -Class Display -ErrorAction Stop | ForEach-Object {
            if ($_.FriendlyName -match 'GameViewer|Virtual|IDD|Basic') {
                Write-Host ("  {0,-8} {1}" -f $_.Status, $_.FriendlyName) -ForegroundColor Yellow
                Write-Host "           InstanceId = $($_.InstanceId)"
            }
            else {
                Write-Host ("  {0,-8} {1}" -f $_.Status, $_.FriendlyName)
            }
        }
    }
    catch { Warn "Get-PnpDevice 失败: $($_.Exception.Message)" }

    Section '服务'
    foreach ($n in @('GameViewerService', 'IndirectKmd')) {
        $s = Get-Service -Name $n -ErrorAction SilentlyContinue
        if ($s) { Write-Host ("  {0,-18} {1,-10} {2}" -f $s.Name, $s.Status, $s.StartType) }
        else    { Write-Host ("  {0,-18} (不存在)" -f $n) }
    }

    Section '相关进程'
    $procs = @(Get-Process -ErrorAction SilentlyContinue |
               Where-Object { $_.ProcessName -match 'GameViewer|RTSS|Afterburner' })
    if ($procs.Count -gt 0) {
        foreach ($p in $procs) { Write-Host ("  {0,-24} PID={1}" -f $p.ProcessName, $p.Id) }
    }
    else { Ok '没有 GameViewer / RTSS 进程在跑' }

    Section '当前显示模式'
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $scr = [System.Windows.Forms.Screen]::PrimaryScreen
        Write-Host "  逻辑分辨率（含 DPI 缩放）: $($scr.Bounds.Width)x$($scr.Bounds.Height)"
    }
    catch { }
    try {
        $vc = @(Get-CimInstance Win32_VideoController -ErrorAction Stop |
                Where-Object { $_.Name -notmatch 'Virtual|Basic' })
        foreach ($v in $vc) {
            Write-Host "  $($v.Name)   驱动 $($v.DriverVersion)"
            Write-Host "    物理分辨率: $($v.CurrentHorizontalResolution)x$($v.CurrentVerticalResolution) @ $($v.CurrentRefreshRate)Hz"
        }
    }
    catch { Warn "读取显卡信息失败: $($_.Exception.Message)" }

    Section '注册表：GameDVR / 全屏优化'
    try {
        $g = Get-ItemProperty 'HKCU:\System\GameConfigStore' -ErrorAction Stop
        Write-Host "  GameDVR_Enabled                       = $($g.GameDVR_Enabled)"
        Write-Host "  GameDVR_FSEBehaviorMode               = $($g.GameDVR_FSEBehaviorMode)"
        Write-Host "  GameDVR_HonorUserFSEBehaviorMode      = $($g.GameDVR_HonorUserFSEBehaviorMode)"
        Write-Host "  GameDVR_DXGIHonorFSEWindowsCompatible = $($g.GameDVR_DXGIHonorFSEWindowsCompatible)"
    }
    catch { Warn '读取 GameConfigStore 失败' }

    Section '游戏侧设置'
    $ini = 'D:\EFT-5.0 BE\SPT_Runtime\user\sptSettings\Graphics.ini'
    if (Test-Path $ini) {
        try {
            $j = Get-Content $ini -Raw -Encoding UTF8 | ConvertFrom-Json
            Write-Host "  Graphics.ini  FullScreenMode = $($j.DisplaySettings.FullScreenMode)  (0=独占全屏 1=无边框)"
            Write-Host "  Graphics.ini  分辨率 = $($j.DisplaySettings.Resolution.Width)x$($j.DisplaySettings.Resolution.Height)"
        }
        catch { Warn "解析 Graphics.ini 失败: $($_.Exception.Message)" }
    }
    else { Warn "找不到 $ini" }

    try {
        $k = Get-ItemProperty 'HKCU:\Software\Battlestate Games\EscapeFromTarkov' -ErrorAction Stop
        $modeProp = $k.PSObject.Properties | Where-Object { $_.Name -like 'Screenmanager Fullscreen mode_h*' } | Select-Object -First 1
        if ($modeProp) {
            Write-Host "  PlayerPrefs    $($modeProp.Name) = $($modeProp.Value)  (0=独占全屏 1=无边框)"
        }
        foreach ($pfx in @('Screenmanager Resolution Width_h', 'Screenmanager Resolution Height_h')) {
            $p = $k.PSObject.Properties | Where-Object { $_.Name -like "$pfx*" } | Select-Object -First 1
            if ($p) { Write-Host "  PlayerPrefs    $($p.Name) = $($p.Value)" }
        }
    }
    catch { Warn '读取 PlayerPrefs 失败' }

    Section '下一步'
    if (Test-Admin) {
        Write-Host '  管理员已就绪。建议按顺序：' -ForegroundColor Green
        Write-Host '    .\tools\Diagnose-Fullscreen.ps1 -Action Test'          -ForegroundColor Green
        Write-Host '    .\tools\Diagnose-Fullscreen.ps1 -Action Full'          -ForegroundColor Green
        Write-Host '    .\tools\Diagnose-Fullscreen.ps1 -Action Restore'       -ForegroundColor Green
    }
    else {
        Write-Host '  以管理员身份重开 PowerShell（右键 → 以管理员身份运行），再执行：' -ForegroundColor Yellow
        Write-Host '    cd D:\EFT-WORKS\full'                                  -ForegroundColor Yellow
        Write-Host '    .\tools\Diagnose-Fullscreen.ps1 -Action Full'           -ForegroundColor Yellow
        Write-Host '  它会：停掉 GameViewer 与 RTSS → 跑独立探针 → 告诉你独占全屏能不能成立。' -ForegroundColor Yellow
        Write-Host '  测完用 -Action Restore 还原显示适配器与服务。'            -ForegroundColor Yellow
    }
}

function Save-State {
    if (Test-Path $StateFile) { return }
    $lines = @()
    $dev = Get-PnpDevice -InstanceId $GvId -ErrorAction SilentlyContinue
    if ($dev) { $lines += "GV_DEV_STATUS=$($dev.Status)" }
    $svc = Get-Service GameViewerService -ErrorAction SilentlyContinue
    if ($svc) {
        $lines += "GV_SVC_STATUS=$($svc.Status)"
        $lines += "GV_SVC_START=$($svc.StartType)"
    }
    $lines | Set-Content -Path $StateFile -Encoding UTF8
    Write-Host "  已保存还原状态到 $StateFile"
}

function Stop-GameViewer {
    Section '停止 GameViewer（网易 UU 远程串流）'
    Save-State

    $procs = @(Get-Process -ErrorAction SilentlyContinue |
               Where-Object { $_.ProcessName -match '^GameViewer' })
    foreach ($p in $procs) {
        try {
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
            Ok "已结束 $($p.ProcessName) (PID $($p.Id))"
        }
        catch { Bad "结束 $($p.ProcessName) 失败：$($_.Exception.Message)" }
    }

    $svc = Get-Service GameViewerService -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        try {
            Stop-Service -Name GameViewerService -Force -ErrorAction Stop
            Ok '已停止服务 GameViewerService'
        }
        catch { Bad "停止服务 GameViewerService 失败：$($_.Exception.Message)" }
    }

    $dev = Get-PnpDevice -InstanceId $GvId -ErrorAction SilentlyContinue
    if ($dev) {
        if ($dev.Status -eq 'OK') {
            try {
                Disable-PnpDevice -InstanceId $GvId -Confirm:$false -ErrorAction Stop
                Ok '已禁用 GameViewer Virtual Display Adapter'
            }
            catch { Bad "禁用虚拟显示适配器失败：$($_.Exception.Message)" }
        }
        else { Ok "虚拟显示适配器已是 $($dev.Status) 状态，无需禁用" }
    }
    else { Warn '没找到 GameViewer 虚拟显示适配器' }

    Warn '若刚才正在远程串流，画面可能中断 —— 这是预期行为，用 -Action Restore 还原。'
}

function Stop-Rtss {
    Section '结束 RTSS'
    $procs = @(Get-Process -ErrorAction SilentlyContinue |
               Where-Object { $_.ProcessName -match 'RTSS|Afterburner' })
    if ($procs.Count -eq 0) { Ok 'RTSS 未运行'; return }
    foreach ($p in $procs) {
        try {
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
            Ok "已结束 $($p.ProcessName) (PID $($p.Id))"
        }
        catch { Bad "结束 $($p.ProcessName) 失败：$($_.Exception.Message)" }
    }
}

function Invoke-Probe {
    Section '运行独立 DXGI 探针（work\fse11）'
    if (-not (Test-Path $ProbeExe)) {
        Bad "找不到探针：$ProbeExe"
        Warn '先在 D:\EFT-WORKS\full\work\fse11 执行 dotnet build -c Release'
        return
    }

    if (Test-Path $ProbeTxt) { Remove-Item $ProbeTxt -Force -ErrorAction SilentlyContinue }

    try {
        $p = Start-Process -FilePath $ProbeExe -PassThru -Wait -WindowStyle Hidden
        Write-Host "  探针退出码: $($p.ExitCode)"
    }
    catch { Bad "启动探针失败：$($_.Exception.Message)"; return }

    if (-not (Test-Path $ProbeTxt)) { Bad "探针没有写出结果文件：$ProbeTxt"; return }

    Write-Host ''
    Write-Host '----- 探针原始结果 -----' -ForegroundColor DarkGray
    Get-Content $ProbeTxt -Encoding UTF8 | ForEach-Object { Write-Host "  $_" }
    Write-Host '----- 结果结束 -----' -ForegroundColor DarkGray

    $txt = Get-Content $ProbeTxt -Raw -Encoding UTF8

    Section '判定'
    $m = [regex]::Match($txt, '成功\s*(\d+)\s*/\s*(\d+)')
    if ($m.Success -and [int]$m.Groups[1].Value -gt 0) {
        Ok "SetFullscreenState 成功 $($m.Groups[1].Value)/$($m.Groups[2].Value) 次"
        Write-Host '  → 刚才关掉的东西就是元凶。现在启动游戏，看 BepInEx\LogOutput.log 里' -ForegroundColor Green
        Write-Host '    "[全屏修复] ... 实际显示模式 = ExclusiveFullScreen" 是否出现。' -ForegroundColor Green
        return
    }
    if ($txt -match '0x887A0022|0x80004005|887a0022') {
        Bad 'SetFullscreenState 依旧失败 —— 关掉 GameViewer / RTSS 不是充分条件。'
        Write-Host '  下一步建议（按顺序）：' -ForegroundColor Yellow
        Write-Host '    1) 用 DDU（Display Driver Uninstaller）在安全模式下彻底卸载 AMD 驱动再重装；' -ForegroundColor Yellow
        Write-Host '    2) 检查是否还有别的显示过滤驱动（带 IDD 或 virtual 字样的显示适配器）；' -ForegroundColor Yellow
        Write-Host '    3) 到 AMD 驱动控制面板里关掉任何"全屏优化/Radeon Anti-Lag/Chill/Boost"类选项。' -ForegroundColor Yellow
        return
    }
    Warn '没能从输出里识别出明确结论，请把上面的原始结果发我。'
}

function Restore-All {
    Section '还原'
    if (-not (Test-Path $StateFile)) {
        Warn '没找到之前保存的状态（可能没跑过 StopGameViewer），仍会尝试把适配器恢复为启用'
    }

    $dev = Get-PnpDevice -InstanceId $GvId -ErrorAction SilentlyContinue
    if ($dev -and $dev.Status -ne 'OK') {
        try {
            Enable-PnpDevice -InstanceId $GvId -Confirm:$false -ErrorAction Stop
            Ok '已重新启用 GameViewer Virtual Display Adapter'
        }
        catch { Bad "重新启用虚拟显示适配器失败：$($_.Exception.Message)" }
    }
    elseif ($dev) { Ok '虚拟显示适配器本来就是启用的' }
    else { Warn '没找到 GameViewer 虚拟显示适配器' }

    $svc = Get-Service GameViewerService -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Running') {
        try {
            Start-Service -Name GameViewerService -ErrorAction Stop
            Ok '已启动服务 GameViewerService'
        }
        catch { Bad "启动服务 GameViewerService 失败：$($_.Exception.Message)" }
    }
    elseif ($svc) { Ok 'GameViewerService 已在运行' }

    Remove-Item $StateFile -Force -ErrorAction SilentlyContinue
    Write-Host ''
    Write-Host '  RTSS 需要你自己从开始菜单启动（它本来就是你手动开的）。' -ForegroundColor Yellow
}

# ------------------------------------------------------------------ main
Write-Host "EFT 独占全屏诊断工具    Action = $Action" -ForegroundColor Cyan

$needAdmin = @('StopGameViewer', 'StopRTSS', 'Full', 'Restore')
if ($needAdmin -contains $Action -and -not (Test-Admin)) {
    Bad "Action = $Action 需要管理员权限。请以管理员身份重开 PowerShell 再跑。"
    Write-Host ''
    Write-Host '  普通权限下可先跑：' -ForegroundColor Yellow
    Write-Host '    .\tools\Diagnose-Fullscreen.ps1 -Action Report' -ForegroundColor Yellow
    Write-Host '    .\tools\Diagnose-Fullscreen.ps1 -Action Test'   -ForegroundColor Yellow
    exit 1
}

switch ($Action) {
    'Report'         { Show-Report }
    'StopGameViewer' { Stop-GameViewer; Invoke-Probe }
    'StopRTSS'       { Stop-Rtss; Invoke-Probe }
    'Full'           { Stop-GameViewer; Stop-Rtss; Invoke-Probe }
    'Test'           { Invoke-Probe }
    'Restore'        { Restore-All }
}
