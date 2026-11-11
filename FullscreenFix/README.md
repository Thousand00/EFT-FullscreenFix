# 主线 · 全屏修复（FullscreenFix）

> 本仓库有两个并行系列。这份文档只管**主线**（`dsh.FullscreenFix`）。
> K 系列走的是「启动后补一次请求」的路子，见 [`../FullscreenFix-K/README.md`](../FullscreenFix-K/README.md)；
> 两个系列的取舍见 [`../README.md`](../README.md)。

当前版本：**`2.2.0`**

**它做什么**：让「全屏」这个设置不再被游戏在运行中偷偷改成「无边框」，下次启动还能重新去申请独占全屏。
**它不做什么**：不拦截游戏调用、不改你选的画面模式、不替你决定分辨率、不保证这一次一定进得去。

---

## 一、问题：选的是全屏，进来却是无边框，而且下次还是

`Player.log` 里是这样的：

```
dxgi: swapchain SetFullscreenState failed (887a0022).
Failed to apply requested ExclusiveFullScreen resolution (1680x1050)...will try again
Unable to apply requested ExclusiveFullScreen resolution again (1680x1050)...reverting to current display resolution: 2880x1800
Failed to change display to ExclusiveFullscreen...reverting to FullscreenWindow
```

`0x887A0022` = `DXGI_ERROR_NOT_CURRENTLY_AVAILABLE`。申请独占全屏被拒，Unity 自己降级成无边框。

而**下一次启动连这条 dxgi 失败都不会出现**——引擎直接建了个无边框窗口，压根没申请。

## 二、真正被改坏的是 PlayerPrefs，不是 Graphics.ini

这一点我在 v2.1.0 说错过，这里更正。

`SPT_Runtime\user\sptSettings\Graphics.ini` 里的 `DisplaySettings.FullScreenMode` **长期都是 `0`**，20 次启动快照里只有 2 次被跟写成 `1`。它**不是**驱动启动模式的来源。

真正决定「这次启动要不要去申请独占全屏」的是注册表：

```
HKCU\Software\Battlestate Games\EscapeFromTarkov
  Screenmanager Fullscreen mode_h3630240806 = 0   ← 0=全屏 1=无边框 2=最大化 3=窗口
```

20 次快照里，这个键与「Unity 启动瞬间的 `Screen.fullScreenMode`」**100% 一一对应**（0↔Exclusive 13 次 / 1↔FullScreenWindow 6 次 / 3↔Windowed 1 次）。

它由 `UnityPlayer.dll` 在**运行中**写入，不是退出时才写：实测 16/16 次都是在「显示模式发生变化」之后约 **2.2 秒**（1.82–2.61s）把变化后的**实际**模式写进去。

于是链条是：

1. 你选的是「全屏」，键是 `0`
2. 启动时引擎去申请独占全屏，被 DXGI 拒（见第三节）
3. Unity 回退成 `1`（无边框）
4. 约 2.2 秒后，引擎把「实际模式 = 1」写回那个注册表键
5. 下次启动读到 `1`，**连申请都不做**，直接无边框

这就是「一次失败被固化成永久失效」。

**推论**：只在退出后钉这个键的外部工具没用——运行中任何一次模式抖动都会在约 2 秒内把它覆盖回去。

## 三、为什么 5.0 才出现这个问题

4.1.6 时代不会这样。差别不在游戏本身，在 **BepInXDX**：

| | 4.1.6 | 5.0 BE |
|---|---|---|
| BepInXDX | 5.4.23.5 | 6.0.0-be.35 |
| `Logging.Console` / `Enabled` 出厂默认 | `false` | **`true`** |

控制台一开，`ConsoleManager.CreateConsole()` 会在进程启动早期调 `AllocConsole()` 凭空建一个控制台窗口。这个窗口**会抢走前台**，然后 `ConsoleWindow.Attach()` 里的 `setForeground(currWnd)` 把前台**还给 AllocConsole 之前那个窗口**（通常是你的浏览器或启动器）——对游戏进程来说，等于把自己永久锁在后台。

`SetFullscreenState` 要求调用瞬间本进程持有前台上下文，否则就是 `0x887A0022`。

这一条已经用探针在**单个进程内**闭环验证：同一条 swapchain、同一个窗口，只把前台抢回本进程，返回值就从 `0x887A0022` 变成 `S_OK`。

> 注意 `setForeground` 那次调用**返回的是 `True`**，它成功执行了——只是执行的结果是把前台给了别人。所以补返回值判断没用，问题在这段逻辑的意图本身。

完整取证过程见 [`docs/为什么-5.0-才有这个问题.md`](docs/为什么-5.0-才有这个问题.md)
与 [`docs/控制台抢前台-实测闭环.md`](docs/控制台抢前台-实测闭环.md)。

### 你可以怎么绕开

在 `BepInEx\config\BepInEx.cfg` 里关掉控制台：

```ini
[Logging.Console]
Enabled = false
```

关掉后 `AllocConsole` 不会调，前台就不会被抢走。（日志照样写 `LogOutput.log`，只是没有那个黑窗口。）

## 四、这版改了什么

**启动时检查并把 PlayerPrefs 的全屏键钉回 `0`。**

- 读 `Graphics.ini` 的 `DisplaySettings.FullScreenMode` 作为「你要的模式」
- 读注册表键作为「上次退出时实际停在哪」
- 只有当前者 `= 0`（你要全屏）、后者 `≠ 0`（上次被改坏了）时才写回 `0`
- 写的是**所有** `Screenmanager Fullscreen mode_h*` 开头的值，兼容 Unity 不同版本的 hash 后缀

日志里会看到：

```
[全屏修复] 全屏键检查：你要的模式=全屏(0)，上次退出时实际模式=无边框(1)。
[全屏修复] 已把 PlayerPrefs 的全屏键从「无边框(1)」改回「全屏」（第 1 次）。
  → 这个改动【下次启动才生效】：下次启动引擎会重新去申请独占全屏。
```

**注意是「下次启动生效」**——引擎读这个键发生在 BepInEx 加载之前（约 2.2 秒 vs 插件加载时已经 1.6 秒的逻辑时间之后），插件没法拦住当次。

## 五、它治不了什么（先说清楚）

1. **不保证这一次进得去独占全屏。** 这一版的定位就是把设置守住，让每次启动都重新去试。
2. 当次能不能成功，取决于启动那一刻前台是否就绪。要「当次就进」请用 K 系列（启动后补发一次请求，已实测成功）。
3. 如果前台被抢是必然发生的，那么即使键是 `0`，这次启动仍会失败——失败后键又会被写回 `1`，插件下次启动再钉回来。**这是一个拉锯，不是根治。**

## 六、安装

1. 关掉游戏。
2. 把 `FullscreenFix.dll` 放进 `BepInEx\plugins\FullscreenFix\`（目录不存在就建）。
3. **把 `BepInEx\config\BepInEx.cfg` 里的 `[Logging.Console] Enabled` 改成 `false`**（第三节的理由）。
4. 启动一次游戏，看 `BepInEx\LogOutput.log` 里 `[全屏修复]` 开头的行。

## 七、配置（`BepInEx\config\dsh.FullscreenFix.cfg`）

| 键 | 默认 | 作用 |
|---|---|---|
| `General/Enabled` | `true` | 总开关 |
| `General/ReadOnlyMode` | `false` | 只读模式。开成 true 就只记日志、什么都不改，用于取证 |
| `Fix/PinFullscreenKeyInPlayerPrefs` | `true` | **本版核心**：启动时把 PlayerPrefs 的全屏键钉回 `0` |
| `Fix/ForceBorderless` | `false` | 把独占全屏改写成无边框（退路，一般别开） |
| `Fix/ForceNativeResolution` | `false` | 强制按原生分辨率渲染（**非必需**，会让像素涨到约 2.9 倍、掉帧） |
| `Fix/PinNativeResolutionInPlayerPrefs` | `false` | 改写 PlayerPrefs 里的分辨率（旧思路，已被实测否证） |
| `Fix/KeepStoredExclusive` | `false` | 旧版遗留项，**实际无效**（改的是装箱副本），保留只为兼容旧配置 |
| `Debug/Verbose` | `true` | 详细日志 |
| `General/ConfigRevision` | `5` | 内部迁移标记，请勿手改 |

## 八、怎么确认它在工作

1. `LogOutput.log` 里搜 `全屏键检查`。能看到「你要的模式=全屏(0)，上次退出时实际模式=无边框(1)」说明读到了。
2. 如果紧跟一条 `已把 PlayerPrefs 的全屏键从「无边框(1)」改回「全屏」`，说明确实修正了。
3. 用 `reg query "HKCU\Software\Battlestate Games\EscapeFromTarkov"` 看 `Screenmanager Fullscreen mode_h3630240806`，现在应该是 `0x0`。

**别看 `Graphics.ini`**——它长期恒 `0`，不能用来判断（这是我 v2.1.0 的错误，已在第二节更正）。

## 九、已知限制

1. **只守住设置，不保证当次进得去独占全屏。**
2. 只在作者本机（MECHREVO WUJIE 14 / AMD Radeon 780M / Win11 / 内屏 2880x1800@120）验证过。
3. 插件加载晚于引擎读注册表，所以修正只对**下一次**启动生效。
4. 如果游戏在**本进程之外**写这个键，钩子拦不到。

## 十、从源码构建

```
cd FullscreenFix
dotnet build -c Release
```

产物在 `bin\Release\FullscreenFix.dll`，需要手动复制到游戏目录。

**坑**：如果仓库根有 `work\`（Il2CppDumper 的 DummyDll），MSBuild 会把它当 `CandidateAssemblyFiles` 抢先解析，导致引用到假程序集。csproj 里保留：

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);bin\**;obj\**</DefaultItemExcludes>
```

## 十一、许可

MIT。
