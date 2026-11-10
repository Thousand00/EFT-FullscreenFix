# 主线 · 全屏修复（FullscreenFix）

> 本仓库有两个并行系列。这份文档只管**主线**（`dsh.FullscreenFix`）。
> K 系列见 [`../FullscreenFix-K/README.md`](../FullscreenFix-K/README.md)，两个系列的取舍见 [`../README.md`](../README.md)。

当前版本：**`2.0.0-readonly`（只读记录模式，不修 bug）**

> **状态说明**：主线目前停在「只读取证」档位 —— 它只把启动那一瞬的显示状态原样落盘，
> 不写注册表、不改设置、不注入任何 Harmony 补丁。
> 下面第七节描述的那套「拦截+改写」能力仍然在代码里，但要手动把 `General/ReadOnlyMode` 改成 `false` 才会启用。
> **它现在不会让你的游戏变成全屏。**

《逃离塔科夫》离线版 5.0BE 上「屏幕模式选『全屏』，实际却是『无边框』」的诊断与修复插件。

**不修改游戏目录里的任何原始文件**，只在 `BepInEx\plugins\` 放一个 DLL；需要写设置时只写 `HKCU` 注册表。

---

## 一、先纠正一个误解：你的设置从来没被改回去

| 位置 | 键 | 排查时的值 |
|---|---|---|
| `SPT_Runtime\user\sptSettings\Graphics.ini` | `DisplaySettings.FullScreenMode` | `0` = 独占全屏 |
| `HKCU\Software\Battlestate Games\EscapeFromTarkov` | `Screenmanager Fullscreen mode_h3630240806` | `0` = 独占全屏 |

Unity 的 `FullScreenMode`：`0=ExclusiveFullScreen`、`1=FullScreenWindow`、`2=MaximizedWindow`、`3=Windowed`。

两处存的**都是 0**，也就是「全屏」。游戏每次启动都确实在申请独占全屏。

## 二、真正发生的事

`Player.log`（`%LOCALAPPDATA%Low\Battlestate Games\EscapeFromTarkov\Player.log`）里有这几行：

```
dxgi: swapchain SetFullscreenState failed (887a0022).
Failed to apply requested ExclusiveFullScreen resolution (1680x1050)...will try again
Unable to apply requested ExclusiveFullScreen resolution again (1680x1050)...reverting to current display resolution: 2880x1800
Failed to change display to ExclusiveFullscreen...reverting to FullscreenWindow
```

顺序是：**申请独占全屏 → DXGI 拒绝 → Unity 自己在运行时降级成无边框**。所谓「变回无边框」是运行时降级的结果，不是配置被改写。

`0x887A0022` = `DXGI_ERROR_NOT_CURRENTLY_AVAILABLE`。

## 三、为什么进不了独占全屏（本机实测结论）

用一个和游戏完全无关的独立程序（纯 P/Invoke `CreateDXGIFactory1` + `D3D11CreateDeviceAndSwapChain` / `CreateSwapChainForHwnd` + `SetFullscreenState`）在本机复现，结果：

| 尝试 | 结果 |
|---|---|
| 隐藏窗口 + FLIP_DISCARD | `0x887A0022` |
| 可见窗口 + FLIP_DISCARD | `0x887A0022` |
| 可见窗口 + bitblt | `0x887A0022` |
| 强行 `AttachThreadInput` 抢到前台 | `0x80004005` E_FAIL |
| 缓冲区 = 桌面原生 2880x1800 + 显式 output0 | `0x887A0022` |
| 带 `ALLOW_MODE_SWITCH` | `0x80004005` E_FAIL |

同时：

- `ChangeDisplaySettingsEx` **连「什么都不改」的空操作都返回 `-1`，`GetLastError()=5 ERROR_ACCESS_DENIED`**（Unicode 版 220 字节和 ANSI 版 156 字节两种结构体都测过，排除了结构体尺寸问题）
- `EnumDisplaySettings(NULL, ENUM_CURRENT_SETTINGS)` 返回的 `dmDeviceName` 是 **`CDD`**（Canonical Display Driver），而不是真实适配器名
- 进程在正常交互桌面（`SessionId=1`、`WinSta0`、`Default`、`UserInteractive=True`、`SM_REMOTESESSION=0`），完整性级别是 `Medium`（普通标准用户，非 AppContainer、非受限 Job）
- 进程内没有加载任何第三方注入模块（排除了 RTSS 注入导致的假阴性）

上面的探针看起来得出「这台机器进不了 DXGI 独占全屏」，但 **2026-11-08 22:36 的实机验证推翻了「无解」这个判断**——真相是**分两条路径**：

| 路径 | 本机结果 |
|---|---|
| 先建窗口化 swapchain，**事后**调 `SetFullscreenState(TRUE, …)` 切过去 | ❌ 恒失败（上表全部如此） |
| 引擎启动时**一次成型**：请求分辨率 == 桌面当前模式，直接以独占全屏创建 swapchain | ✅ 成功 |

之前每次启动走的都是第一条路：游戏请求 `1680x1050`，桌面是 `2880x1800@120`，分辨率不一致 → Unity 先建普通 swapchain，再靠 `SetFullscreenState` 去切模式和全屏 → 被拒 → 回退无边框。

**只要让请求分辨率等于桌面当前模式，Unity 就改走第二条路，一次成型，全程不碰那个会失败的调用。**

所以能修，而且只靠改分辨率就够了。上方所有探针程序无一例外都在测「路径一」，这就是它们全部失败的原因——我当初据此下「无解」的结论是错的。

顺带解释了你遇到的第二个现象：给 exe 勾「禁用全屏优化」会启动失败——那个兼容性层同样依赖被拒的那套显示模式切换路径。

## 四、v1.2.0 做什么

### 1. 不再违背你的选择

上一版默认把「独占全屏」改写成「无边框」——那不是你要的。v1.1.0 默认**不改写屏幕模式**（`Fix/ForceBorderless = false`），只做下面这些。

### 2. 钉住分辨率，让引擎的请求不再需要切换显示模式（这就是全部关键）

`Player.log` 里那句 `requested ExclusiveFullScreen resolution (1680x1050)` 说明：游戏是带着 **1680x1050** 去申请独占全屏的，而桌面当前是 **2880x1800@120**。分辨率不一致就必须做 **显示模式切换（mode switch）**，而本机的模式切换被系统拒绝（`ERROR_ACCESS_DENIED`）。

**如果请求的分辨率正好等于桌面当前模式，就不需要 mode switch**，Unity 会直接以独占全屏创建 swapchain。所以 v1.1.0 默认开启：

- `Fix/PinNativeResolutionInPlayerPrefs`（**默认开，这一项就是全部必需的改动**）：把 PlayerPrefs 里记录的分辨率改成原生 2880x1800，**下次启动生效**。引擎启动期只读 PlayerPrefs，所以它决定了引擎那一次请求用哪个分辨率。
- `Fix/ForceNativeResolution`（**默认关**）：强制把游戏运行时另外发起的分辨率请求也钉到 2880x1800。实测证明它**不是**让全屏成立的必要条件，只会把渲染像素拉到 1680x1050 的 2.9 倍、白白掉帧。

所以默认只开第一项：**独占全屏成立，渲染仍按你自己在游戏里选的分辨率**。

#### 1.2.0 新增：让这个钉子活下来

v1.1.0 只在**启动那一刻**钉一次。实测发现游戏在会话过程中会把这两个键写回它正在用的分辨率（`Screenmanager Resolution Width/Height` 又变回 1680x1050，因为 `Graphics.ini` 里存的就是 1680x1050）。而下一局引擎依然是在**任何托管代码之前**读 PlayerPrefs 的——只要退出时注册表里是 1680，下一局就又要 mode switch、又要退回无边框。

写这些键的是 Unity 引擎原生侧（`ScreenManager` C++），托管代码里没有对应字符串，**Harmony 拦不住**。所以 v1.2.0 改成事后反复纠正：

- 启动时钉一次（原行为）
- **运行期每 5 秒检查一次**，发现被改回去就立刻钉回 2880x1800（10 秒启动缓冲，避开加载瞬间）
- **退出前（`OnApplicationQuit`）再钉一次**——这一次最关键，它决定下一局引擎读到的初始值

日志里会看到：

```
[全屏修复] 游戏把 PlayerPrefs 里的分辨率改回了非原生值，已重新钉回 2880x1800（改了 2 个键，累计第 3 次）。
[全屏修复] 退出前已把 PlayerPrefs 分辨率钉回原生值（决定下一局是否还需 mode switch）。
```

这两行出现说明重钉在工作，属于正常现象，不是报错。

> 实测确认（2026-11-08 22:36 那一局）：启动日志里 `Screen.fullScreenMode : ExclusiveFullScreen`、`Player.log` 中 `SetFullscreenState failed` 与 `reverting to FullscreenWindow` **均为 0 次**，`Graphics.ini` 与 PlayerPrefs 的 `FullScreenMode` 都稳定为 `0`（独占全屏）。UU/GameViewer 和 RTSS 当时都还在跑，所以它们并不是元凶。

### 3. 把真实结果写进日志（不再靠猜）

每次显示切换后都会打印实际模式，例如：

```
[全屏修复] SetResolution 返回后 → 实际显示模式 = FullScreenWindow ✘ 无边框全屏（独占全屏失败后 Unity 的回退结果）
[全屏修复] SetResolution 返回后 → 实际显示模式 = ExclusiveFullScreen ✔ 真·独占全屏（成功）
```

看 `BepInEx\LogOutput.log` 里 `[全屏修复]` 开头的行即可。

### 4. 启动自检

会枚举显示设备，如果发现 `GameViewer` / IDD / 虚拟屏，会打一条警告。

## 五、安装

1. 关掉游戏。
2. 把 `bin\Release\FullscreenFix.dll` 复制到游戏目录的 `BepInEx\plugins\FullscreenFix\`。
3. 启动一次游戏，然后看 `BepInEx\LogOutput.log` 里 `[全屏修复]` 的行。

### 从 1.0.0 升级上来的注意

1.0.0 的默认行为是**把全屏改成无边框**（`ForceBorderless = true`），这正是你不想要的那个。BepInEx 不会覆盖已存在的配置项，所以光换 DLL 会继续沿用旧值，等于白升级。

本版内置**一次性配置迁移**：启动时若发现 `ConfigRevision < 4`，会
- 把 `ForceBorderless` 改回 `false`（它是把全屏变成无边框的，正是你不想要的）
- 把 `ForceNativeResolution` 改回 `false`（实测非必需，只会掉帧）
- rev 3 → rev 4 只打一行提示：v1.2.0 起会周期重钉，无需改任何配置

并写进 cfg，日志里打印

```
[全屏修复] 检测到 1.0.0 的旧配置：ForceBorderless 曾是 true（会把全屏改成无边框）。
[全屏修复] 已自动改为 false —— 你要的是『全屏』，不是『无边框』。
[全屏修复] 已关闭 ForceNativeResolution（原为 true）。
[全屏修复] 实测证明：钉住 PlayerPrefs 里的分辨率就足以让独占全屏成立，
[全屏修复] 没必要再逼游戏按 2880x1800 渲染 —— 那是 2.9 倍像素，白掉帧。
```

想手动确认：打开 `BepInEx\config\dsh.FullscreenFix.cfg`，看 `ForceBorderless` 与 `ForceNativeResolution` 是否都为 `false`。


## 六、配置（`BepInEx\config\dsh.FullscreenFix.cfg`）

| 键 | 默认 | 作用 |
|---|---|---|
| `General/Enabled` | `true` | 总开关 |
| `Fix/PinNativeResolutionInPlayerPrefs` | `true` | 把 PlayerPrefs 记录的分辨率改成 2880x1800（下次启动生效） |
| `Fix/ForceNativeResolution` | `false` | 强制运行时按 2880x1800 渲染（**非必需，默认关**） |
| `Fix/ForceBorderless` | `false` | 把独占全屏改写成无边框（**退路，默认关**） |
| `Fix/KeepStoredExclusive` | `true` | 让设置界面/保存值保持显示「全屏」 |
| `Debug/Verbose` | `true` | 详细日志 |
| `General/ConfigRevision` | `4` | 内部用的配置迁移标记，请勿手改 |

## 七、如果日志显示仍然是 `FullScreenWindow`

先按**顺序**查这两条，绝大多数情况第 1 条就能解决：

1. **确认分辨率已经等于桌面当前模式。** 看日志里这两行：
   ```
   判定原生分辨率  : 2880x1800 (刷新 120Hz)
   游戏请求 SetResolution(2880x1800, ExclusiveFullScreen) —— 原样放行
   ```
   如果游戏请求的分辨率和桌面模式不一致（比如请求 1680x1050、桌面 2880x1800），就会走「路径一」，必然失败。此时确认 `PinNativeResolutionInPlayerPrefs = true`，然后**再重启一次游戏**（这个改动下次启动才生效）。
2. **确认桌面模式本身没被人为改掉。** 如果你在 Windows 显示设置里把桌面改成了别的分辨率/刷新率，`判定原生分辨率` 会跟着变，那没关系——只要游戏请求的等于它即可。

上面两条都对了还是 `FullScreenWindow`，才说明本机显示栈连「一次成型」都拒，需要动系统环境（都要管理员权限，我没有）：

3. 退出 **RTSS**（RivaTuner Statistics Server），它会钩 DXGI。
4. 退出 **UU / GameViewer 远程串流**，或到「设备管理器 → 显示适配器」禁用 **GameViewer Virtual Display Adapter** 后重启再试。
5. **关闭 Windows 全屏优化 / GameDVR**：设置 → 游戏 → 游戏模式 / 捕获，全部关掉。
6. **更新或重装显卡驱动**（本机 AMD Radeon 780M，驱动 `32.0.32015.12012`），装的时候勾「恢复出厂设置」。

> 注意：本机实测证明 **第 3、4 条并不是元凶**——那次成功进入独占全屏时，UU/GameViewer 与 RTSS 全都还在跑。所以这两条只是「排除干扰」的备选，不要当成首选。

### 自动隔离（推荐，省掉手动折腾）

我写了一个诊断脚本 `tools/Diagnose-Fullscreen.ps1`，它会**自动停掉嫌疑进程 → 跑独立探针 → 直接告诉你独占全屏能不能成立**，测完还能一键还原：

```powershell
cd D:\EFT-WORKS\full

# 普通权限就能跑：只报告现状
.\tools\Diagnose-Fullscreen.ps1 -Action Report

# 普通权限就能跑：只跑探针，看当前环境下独占全屏是否可行
.\tools\Diagnose-Fullscreen.ps1 -Action Test

# 需要管理员：停 GameViewer + RTSS，然后跑探针
.\tools\Diagnose-Fullscreen.ps1 -Action Full

# 需要管理员：还原（重新启用虚拟显示适配器、启动服务）
.\tools\Diagnose-Fullscreen.ps1 -Action Restore
```

脚本会给出三种结论之一：**探针成功 → 那两样东西就是元凶**；**探针仍失败 → 需要动显卡驱动**；**没识别出结论 → 把原始输出发我**。
`-Action StopGameViewer` / `-Action StopRTSS` 可以单独测，用来区分到底是哪一个在挡。

模块化排查用：`work\fse2`、`work\fse9`、`work\fse11`、`work\fse12`、`work\fse13`、`work\fse16` 下都是独立探针工程（`dotnet build -c Release` 即可），不依赖游戏。

## 八、卸载

删掉 `BepInEx\plugins\FullscreenFix\` 与 `BepInEx\config\dsh.FullscreenFix.cfg` 即可。若开过 `PinNativeResolutionInPlayerPrefs`，进游戏设置里重新选一次分辨率恢复原值。

## 九、已知限制

1. **独占全屏是否成立，取决于「请求分辨率是否等于桌面当前模式」**。这一点已在本机实测通过（2026-11-08 22:36 那一局真正进入了 `ExclusiveFullScreen`）。插件本身不强制任何屏幕模式，它做的是「免掉 mode switch + 如实报告」。
   - 反过来说：如果你把 `PinNativeResolutionInPlayerPrefs` 关掉、又在游戏里选了与桌面不同的分辨率，就会退回「路径一」，重新出现无边框。
2. `KeepStoredExclusive` 只保证**保存值/界面显示**为「全屏」，它不改变渲染实际模式。日志里的 `实际显示模式` 才是真相。
3. 只在作者本机（MECHREVO WUJIE 14 / AMD Radeon 780M / Win11 26300 / 内屏 2880x1800@120）验证过，且**只有一次成功记录**。其它机器上若桌面模式本身不可切换（`ChangeDisplaySettingsEx` 被拒），同理需要让游戏分辨率等于桌面当前模式。
4. 本机 `SetFullscreenState(TRUE, …)` 事后切换**始终**失败（`work\fse11` 复跑仍是 0/4），所以这条修复走的是「一次成型」路径，而不是把事后切换修好了。

## 十、从源码构建

```
dotnet build -c Release
```

产物在 `bin\Release\FullscreenFix.dll`，需要手动复制到游戏目录。

**坑**：`work\` 下有 Il2CppDumper 生成的 `DummyDll`（含同名 `UnityEngine.CoreModule.dll`），MSBuild 会把它当作 `CandidateAssemblyFiles` 抢先解析，导致引用到假程序集、报一堆莫名其妙的类型错误。csproj 里必须保留：

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);bin\**;obj\**</DefaultItemExcludes>
```
