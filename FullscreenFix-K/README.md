# K 系列 · 全屏修复 K1

> 本仓库有两个并行系列。这份文档只管 **K 系列**（`dsh.k1.fullscreen`）。
> 主线见 [`../FullscreenFix/README.md`](../FullscreenFix/README.md)，两个系列的取舍见 [`../README.md`](../README.md)。

SPT 5.0 BE 的客户端插件（BepInEx 5 / IL2CPP），解决「每次启动都变成无边框全屏」。

当前版本：**v0.4.0**

---

## 这个插件到底在解决什么

`Player.log` 第 18 行：

```
dxgi: swapchain SetFullscreenState failed (887a0022).
```

第 299 行 / 331-334 行 / 422-426 行：

```
Failed to apply requested ExclusiveFullScreen resolution (1680x1050)...will try again
Unable to apply requested ExclusiveFullScreen resolution again (1680x1050)...reverting to current display resolution: 2880x1800
Failed to change display to ExclusiveFullscreen...reverting to FullscreenWindow
```

三串关键字符串在 `UnityPlayer.dll` 里各命中 1 次，在 `Assembly-CSharp.dll`、`GameAssembly.dll`、
`global-metadata.dat` 里全部 0 命中 —— 说明「切独占全屏失败 → 回退无边框」是 **Unity 原生 C++** 的行为，
**托管侧没有任何方法可以打补丁拦住它**。所以旧版（`dsh.FullscreenFix` 1.x）试图用 Harmony 拦截，
方向本身就是错的。

决定性的反证：**玩家手动按 F9 能成功进独占全屏**（2026-11-10 实测；`Player.log` 第 426 行之后
到文件末尾 38 万行不再有任何 dxgi 失败日志）。同一台机器、同一个分辨率、同一个 API ——
早失败、晚成功，说明这是**纯粹的时机问题**，不是硬件不支持。

本插件因此只做一件事：**等时机对了，重新请求一次，然后如实报告结果。**

---

## 它做什么

0. **感知「模式选择」界面**（v0.4.0 新增）。5.0 BE 模仿线上的流程是
   `登录 → 设备校验 → 侧别选择 → 模式选择（PVE/PVP/PVPS） → 载入存档 → 主菜单`。
   那个选择页停留多久由玩家决定，而它正好卡在「引擎正在切场景」的中间——
   这时候请求独占全屏会被立刻回退（实测只撑 0.58s）。所以：
   - 挂着 `CharacterSelectionScreen.Show/Close` 与
     `CharacterSelectionScreenController.SubmitAsync/Submit`；
   - 玩家还停在那页时**不发请求**，并把超时计时往后推；
   - 玩家选完模式后给 8s 宽限（主菜单信号优先），不再干等 45s。
1. 挂 `EFT.UI.MenuScreen.Show` 的**全部重载**（两个），主菜单一出现就得到确切信号。
   等不到就按超时退化（`SettleTimeoutSeconds`，默认 45s）。
2. 信号到 → 等 `InitialDelaySeconds` → 调
   `EFTDisplayHelper.SetResolution(w, h, ExclusiveFullScreen)` 重新请求。
3. **验证**：要求 `Screen.fullScreenMode == ExclusiveFullScreen` **连续保持** `StableSeconds`（默认 3s）
   才算成功。这一点是 v0.1.0 吃过亏的地方——请求发出瞬间 Unity 会先把期望模式置上，
   swapchain 失败才回退，单次采样读到的只是回退前的**瞬态值**。
4. 失败 → 指数退避（4→6→9→13.5→20→30→45s 封顶）再试，最多 `MaxAttempts` 次（默认 10，累计覆盖启动后约 2-3 分钟）。
5. 热键（默认 F9）随时可手动再触发一次。

## 它不做什么

- 不改 `Graphics.ini`、不改 `PlayerPrefs` / 注册表、不改任何存档。
- 不把「全屏」偷偷改成「无边框」来让设置看起来一致（旧版的 `KeepStoredExclusive` 就是这么干的）。
- 不在失败时假装成功。日志里写了什么，实际就是什么。

---

## 独立日志文件

从 v0.3.0 开始，本插件自己的日志单独写一份：

```
<BepInEx 根目录>\K1Logs\K1FullscreenFix.log
```

即 `D:\EFT-5.0 BE\BepInEx\K1Logs\K1FullscreenFix.log`。

- 上一次运行的日志会被改名成 `K1FullscreenFix.prev.log`，方便对照「这次和上次差在哪」。
- 每行带 `HH:mm:ss.fff` 时间戳和级别。
- 高频细节（每次 0.5s 采样的模式变化）**只进这个文件**，不进 `LogOutput.log`，
  避免像 v0.2.0 那样把主日志刷出几千行。
- 关键行（加载、配置快照、钩子结果、每次尝试、成功/失败结论）同时进 BepInEx 主日志。

排查时**只看这一个文件就够**，不用在 6900 行别的插件日志里翻。

---

## 编译

```powershell
cd D:\EFT-WORKS\full\FullscreenFix-K
dotnet build -v q --nologo
```

产物：`bin\Debug\K1FullscreenFix.dll` + `K1FullscreenFix.pdb`，
并自动复制到 `dist\BepInEx\plugins\K1FullscreenFix\`。

> `.pdb` 一定要一起复制。没有它的话 IL2CPP trampoline 抛出的堆栈**没有行号**
> （v0.2.0 排查 `NullReferenceException` 时就吃了这个亏，只看到
> `at K1FullscreenFix.Runtime.FullscreenRetrier.Start()`，不知道是第几行）。

游戏目录不在默认位置时：

```powershell
dotnet build -v q --nologo /p:SptRoot="D:\EFT-5.0 BE"
```

## 安装

把 `K1FullscreenFix.dll` 和 `K1FullscreenFix.pdb` 一起放进：

```
D:\EFT-5.0 BE\BepInEx\plugins\K1FullscreenFix\
```

首次启动会在 `BepInEx\config\dsh.k1.fullscreen.cfg` 生成配置。

---

## 配置（`BepInEx\config\dsh.k1.fullscreen.cfg`）

### `[General]`

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `Enabled` | `true` | 总开关。关掉后只记录，不发任何显示切换请求。 |
| `Verbose` | `true` | 详细日志。只影响独立日志文件的详细程度。 |

### `[Retry]`

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `AutoRetryOnStartup` | `true` | 启动后自动尝试。 |
| `MaxAttempts` | `10` | 最多自动尝试几次。累计覆盖到启动后约 2-3 分钟。 |
| `SettleTimeoutSeconds` | `45` | 最多等多久主菜单信号；超时则直接开始尝试。冷启动慢的机器可调大。 |
| `StableSeconds` | `3` | 独占模式要**连续保持**多少秒才算真成功。 |
| `InitialDelaySeconds` | `1` | 信号到之后再等几秒发请求。 |
| `ResultTimeoutSeconds` | `6` | 一次尝试最多观察多久（进独占后改按 `StableSeconds` 判定）。 |
| `BackoffSeconds` | `4` | 首次失败后的等待间隔。 |
| `BackoffGrowth` | `1.5` | 每次失败间隔乘以这个系数。设 `1.0` 即固定间隔。 |
| `MaxBackoffSeconds` | `45` | 退避间隔上限。 |
| `Resolution` | `Current` | `Current` = 用当前 `Screen.width/height`；`Native` = 用 `Display.main.systemWidth/Height`。 |
| `ManualHotkeyEnabled` | `true` | 允许热键手动触发。 |
| `ManualHotkey` | `F9` | 热键，写法同 `UnityEngine.KeyCode` 枚举名。 |

---

## 用的四个反射入口

全部走字符串反射（`AccessTools.TypeByName`）。原因：`EFT.Settings.Graphics.EFTDisplayHelper`
是 Il2CppInterop 运行时生成的类型，编译期 `typeof` 直接 CS0122。

| 入口 | 签名（ilspycmd 核对） | 用途 |
| --- | --- | --- |
| `SetResolution`（3 参） | `SetResolution(int, int, FullScreenMode) -> Task`，`[CallerCount(8)]`，未内联 | 唯一可靠的重发请求入口 |
| `SetResolution`（2 参） | `SetResolution(int, int) -> Task`，`[CallerCount(6)]` | 备用（本版未用） |
| `IsScreenReady` | `IsScreenReady() -> bool`，`[CallerCount(1)]` | 判断「现在切换是否安全」 |
| `ApplyDelay` | `ApplyDelay { get; } -> Il2CppSystem.TimeSpan`，`[CallerCount(0)]` + **已内联** | 只读值做诊断（内联属性打不了补丁） |

---

## 看日志判断结果

打开 `BepInEx\K1Logs\K1FullscreenFix.log`，按顺序看这几行：

```
[INFO] 全屏修复 K1 v0.3.0
[INFO] 配置快照 → Enabled=True, ..., MaxAttempts=10, Stable=3.0s, ...
[INFO] 已挂钩 EFT.UI.MenuScreen 的 2 个显示入口
[INFO] 检测到主菜单已显示 —— 这是「可以安全切独占全屏」的确切信号。
[INFO] 主菜单已出现：等 1.0s 后发请求。
[INFO] 第 1/10 次尝试：请求 1680x1050 独占全屏。 之前 → mode=FullScreenWindow, ...
[INFO] ✔ 第 1 次尝试成功：ExclusiveFullScreen 已连续保持 3.0s。
```

**成功判据**：看到 `✔ ... 已连续保持 3.0s`，并且在这之后日志里**不再出现**
`显示模式变化（后台监视）：ExclusiveFullScreen → FullScreenWindow` 这类回退。

**失败长这样**：

```
[WARN] ✘ 第 1/10 次尝试未成功：观察 6s 后是 FullScreenWindow，不是 ExclusiveFullScreen。
[INFO] 第 2 次尝试前退避：等 6.0s 后发请求。
```

---

## 排障

| 现象 | 看哪里 / 怎么办 |
| --- | --- |
| 日志文件根本没生成 | `LogOutput.log` 里搜 `[K1] 独立日志文件`，会打出实际路径；搜 `[K1] 初始化失败` 看异常。 |
| `已挂钩 ... 0 个显示入口` | `EFT.UI.MenuScreen` 方法名变了。定时逻辑仍会兜底（等 `SettleTimeoutSeconds` 后开试）。 |
| 一直是 `✘` 失败 | 说明显示栈在这个会话里就是不放行。试：进图后再按 F9、Alt+Tab 出去再回来按 F9、或用 `Resolution=Native`。 |
| `请求根本没发出去` | 反射入口失效，通常是游戏更新改了 `EFTDisplayHelper`。日志里有具体异常。 |
| 日志里 `配置项读取失败` | 某个键的值解析不了，已用默认值。按提示的键名检查 `.cfg`。 |

---

## 文件结构

```
FullscreenFix-K/
├─ FullscreenFix.csproj
├─ README.md
└─ src/
   ├─ Plugin.cs                     插件入口：初始化日志 → 快照配置 → 注册组件 → 挂菜单钩子
   ├─ Config/
   │  ├─ ModConfig.cs               配置定义（只负责 Bind，运行时不读）
   │  └─ RetrySettings.cs           配置的纯值快照（IL2CPP 路径只碰这个）
   ├─ Diagnostics/
   │  └─ K1Log.cs                   独立日志文件 + 关键行同步进 BepInEx 主日志
   ├─ Display/
   │  └─ DisplayApi.cs              EFTDisplayHelper 的反射封装
   ├─ Hooks/
   │  ├─ MenuHook.cs                挂 MenuScreen.Show 的全部重载
   │  └─ ScreenHooks.cs             挂「模式选择」界面：开着就别动手，选完再动
   └─ Runtime/
      └─ FullscreenRetrier.cs       状态机：等信号 → 重试 → 观察 → 退避
```

---

## 版本历史

### v0.4.0（2026-11-10）

修掉「玩家停在模式选择界面时白试一次」的设计错误。

实测日志（v0.3.1，2026-11-10 23:03）：

```
23:03:49.638 等待信号超时，按定时判断：等 1.0s 后发请求。   ← 45s 兜底，此刻人还在选择页
23:03:50.670 [观察] FullScreenWindow → ExclusiveFullScreen
23:03:51.247 [观察] ExclusiveFullScreen → FullScreenWindow  ← 只撑了 0.58s
23:03:56.834 ✘ 第 1 次尝试未成功
23:04:00.839 第 2/10 次尝试                                  ← 已选完模式、进了主菜单流程
23:04:04.262 ✔ 第 2 次尝试成功：已连续保持 3.0s
23:04:07.884 检测到主菜单已显示                               ← 真正的信号比第 1 次晚 17s
```

问题不是「时机没等够」，而是**把「等信号的上限」当成了「可以动手了」**。
选择界面停留时长不由插件控制，45s 只是个魔法数，这次刚好落在选择页里。

改动：新增 `ScreenHooks`，三段信号（选择页打开 / 选完模式 / 主菜单出现）；
`TickWaitSignal` 在「选择页开着且未选定模式」时不发请求并把超时往后推；
选定模式后 8s 宽限；另加 180s 硬上限，万一 `Close` 钩子没挂上也不会彻底罢工。

### v0.3.0（2026-11-10）

修掉 v0.2.0 的三个致命问题：

1. **`NullReferenceException` 崩在 `Start()`**（`LogOutput.log:412-415`，IL2CPP trampoline 抛出的）。
   根因：`Start()` / `EnterSettle()` / `EnterDelay()` 在 IL2CPP 调用路径上直接读
   `ConfigEntry<T>.Value`（BepInEx 对象），那条路径是 IL2CPP → 托管 trampoline，容错为零。
   修法：配置在纯托管的 `Plugin.Load()` 里一次性快照成 `RetrySettings` 纯值对象，
   IL2CPP 路径只碰 `bool`/`int`/`float`/`KeyCode`，一个 BepInEx 对象都不再接触。
2. **状态机卡死 + 日志刷屏**。因为 `EnterSettle()` 抛异常，`_phase` 永远停在 `WaitReady`，
   于是「屏幕已就绪」每 0.5s 打一次（数千行）、`✘ 失败` 打了 24 次。
   修法：所有状态转移都改成**先改状态、再写日志**，日志抛异常也不会卡住状态机；
   高频细节走 `K1Log.Detail` 只进独立文件。
3. **`AmbiguousMatchException`**（`LogOutput.log:233`）。`EFT.UI.MenuScreen` 上确实有两个 `Show` 重载：
   - `Show(MainMenuBaseScreenController)` — public virtual, CallerCount(0)
   - `Show(MatchmakerPlayersController, ExpansionsPlayerInfo, GameModeDescriptor, Profile, SeasonalRewardController)` — private, CallerCount(1)

   `AccessTools.Method(type, "Show")` 因此报歧义，钩子完全没挂上。
   修法：枚举全部名为 `Show`/`Open` 的实例方法，**逐个** patch，任一触发即视为菜单已出现。
4. 新增独立日志文件（用户要求）。
5. 构建时一并复制 `.pdb`，让 IL2CPP 堆栈带行号。
6. 退避改为指数增长（4→6→9→13.5→20→30→45s 封顶），`MaxAttempts` 3→10，
   让重试覆盖到启动后约 2-3 分钟，越过引擎自己的折腾期。

### v0.2.0（2026-11-10，实测无效）

加了 `WaitSettle` 阶段和「连续保持才判成功」，但 `Start()` 抛 NRE 导致整个状态机空转，
实际只跑了 F9 手动路径。见上面 v0.3.0 第 1、2 条。

### v0.1.0（2026-11-10，实测失败）

首次实现。两个错误（`LogOutput.log:439` 与 `:441-444`）：
1. 在「屏幕就绪 + 6s」就发请求，撞在引擎自己 `will try again` → `again` → `reverting` 流程中间，必然再失败。
2. `TickObserve` 只采样一次就判成功，读到的是回退前的**瞬态值** → 假阳性。

---

## 版本回滚

两个系列共用一个仓库，所以 **K 系列的 tag 带 `k-` 前缀**（主线的带 `mainline-` 前缀），
避免 `v0.4.0` 这种名字分不清是哪个系列。

```powershell
cd D:\EFT-WORKSull

git log --oneline            # 看有哪些提交
git tag                      # 看有哪些 tag

# 回到 K 系列某个版本（只影响全仓库的检出状态，但你要的代码在 FullscreenFix-K/）
git checkout k-v0.4.0

# 丢弃全部未提交改动
git checkout -- .
```

> 注意：`git checkout k-v0.4.0` 之后处于 detached HEAD 状态。
> 想在那个版本上继续改，用 `git switch -c 从k0.4.0继续`。
> 想回到最新代码，用 `git switch main`。

## 本仓库的 git 本地配置

> 这些配置是**仓库级**的（存在仓库根的 `.git\config`），对两个系列都生效。

这台机器上访问 GitHub 走的是 SteamTools（Watt Toolkit）的本地代理，
它用一个自签根证书做中间人，而 Git 的 OpenSSL 后端只认 Mozilla 证书包，
所以仓库局部配置了两个东西（**只影响本仓库，没动你的全局 git 配置**）：

| 配置 | 值 | 作用 |
| --- | --- | --- |
| `http.sslBackend` | `openssl` | 用 OpenSSL 而不是 Windows Schannel |
| `http.sslCAInfo` | `.git/k1-ca-bundle.pem` | Mozilla 证书包 + SteamTools 根证书合并 |
| `credential.helper` | `!gh auth git-credential` | 复用 `gh` 的 token，避免弹 Credential Manager 选单 |

换到别的机器或网络环境时，如果推送报证书错误，可以先清掉这两项试：

```powershell
git config --local --unset http.sslCAInfo
git config --local --unset http.sslBackend
```
