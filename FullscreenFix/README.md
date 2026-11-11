# 主线 · 全屏修复（FullscreenFix）

> 本仓库有两个并行系列。这份文档只管**主线**（`dsh.FullscreenFix`）。
> K 系列走的是「启动后补一次请求」的路子，见 [`../FullscreenFix-K/README.md`](../FullscreenFix-K/README.md)；
> 两个系列的取舍见 [`../README.md`](../README.md)。

当前版本：**`2.1.0`**

**它做什么**：让「全屏」这个设置能正常保存下来、下次启动还能用。
**它不做什么**：不拦截游戏调用、不改你选的画面模式、不替你决定分辨率。

---

## 一、问题：设置自己会变成「无边框」

选的是「全屏」，`Graphics.ini` 里存的也是 `0`（= `ExclusiveFullScreen`），可进游戏却是无边框，而且**下一次启动照样无边框**。

`Player.log` 里是这样的：

```
dxgi: swapchain SetFullscreenState failed (887a0022).
Failed to apply requested ExclusiveFullScreen resolution (1680x1050)...will try again
Unable to apply requested ExclusiveFullScreen resolution again (1680x1050)...reverting to current display resolution: 2880x1800
Failed to change display to ExclusiveFullscreen...reverting to FullscreenWindow
```

`0x887A0022` = `DXGI_ERROR_NOT_CURRENTLY_AVAILABLE`。申请独占全屏被拒，Unity 自己降级成无边框。

## 二、为什么「下次启动还是无边框」——设置是这样被写坏的

这是主线要解决的那个点。反编译 `Assembly-CSharp.dll` 里的 `GraphicsSettingsController` 后确认：

`_ApplyDisplaySettings_d__29.MoveNext` 在收尾时，把**实际变成了什么模式**写回了设置结构：

```asm
0xc42b63  mov ebx, dword ptr [rdi + 0x24]   ; ebx = 你请求的模式（0 = 全屏）
0xc42b68  call Screen::get_fullScreenMode()  ; eax = 实际模式
0xc42b7d  cmp ebx, eax
0xc42b7f  je  0xc42d7a                      ; 一致就跳过
...
0xc4347a  mov dword ptr [rdi + 0x24], eax   ; ★ 把实际模式写回 settings
```

于是链条是：

1. 你选的是 `0`（全屏）
2. DXGI 建独占失败，Unity 回退成 `1`（无边框）
3. 上面那条 `0xc4347a` 把 `1` 写进 `EftDisplaySettings.FullScreenMode`
4. 这份设置被序列化进 `Graphics.ini` → `"DisplaySettings": { "FullScreenMode": 1 }`
5. 下次启动读到 `1`，于是**从一开始就是无边框**，连申请独占都不做了

`StoreDisplaySettings`（另一个看起来该负责保存的方法）反汇编后确认**根本不写 `FullScreenMode`**，它只按模式往 `Stored[]` 里写分辨率/宽高比 —— 这也是为什么以前那种「改保存值」的补丁全都没用。

## 三、修复：在落盘那一刻把这一项改回来

最终写完 `Graphics.ini` 的唯一出口是 `Bsg.GameSettings.Json.JsonFileSettingsProvider.SaveJson(JObject json)`。主线在这个方法上挂 Prefix，改的是**即将写出去的那份 JSON**：

- 内存里的运行时状态不动 → 不影响本次画面
- 只把 `DisplaySettings.FullScreenMode` 从非 0 改回 `0`
- 改了几次都记进日志

日志里会看到：

```
[全屏修复] 已挂钩 Bsg.GameSettings.Json.JsonFileSettingsProvider.SaveJson：写盘前把 FullScreenMode 改回你选的全屏。
[全屏修复] 写盘前拦下了一次降级：DisplaySettings.FullScreenMode 1 -> 0（已守住第 1 次）。
```

第二条出现 = 补丁真的拦下了一次降级。这是正常现象，不是报错。

## 四、它治不了什么（先说清楚）

**它不保证你这次进得去独占全屏。** 如果本机 DXGI 就是拒绝建立独占全屏，这一次的画面仍然是无边框 —— 主线不会假装成功，也不会偷偷把全屏改成无边框来「配合」现状。

它保证的是：**你的选择不会再被游戏偷偷改掉**。设置始终是「全屏」，下次启动游戏还会再试一次。至于那一次能不能成，取决于游戏启动时的时机与显示栈状态（K 系列那套「事后补发」就是专门解这个的）。

## 五、安装

1. 关掉游戏。
2. 把 `FullscreenFix.dll` 放进 `BepInEx\plugins\FullscreenFix\`（目录不存在就建）。
3. 启动一次游戏，看 `BepInEx\LogOutput.log` 里 `[全屏修复]` 开头的行。

## 六、配置（`BepInEx\config\dsh.FullscreenFix.cfg`）

| 键 | 默认 | 作用 |
|---|---|---|
| `General/Enabled` | `true` | 总开关 |
| `General/ReadOnlyMode` | `false` | 只读记录模式。开成 true 就只记日志、什么都不改，用于取证 |
| `Fix/ForceBorderless` | `false` | 把独占全屏改写成无边框（退路，一般别开） |
| `Fix/ForceNativeResolution` | `false` | 强制按原生分辨率渲染（**非必需**，会让像素涨到约 2.9 倍、掉帧） |
| `Fix/PinNativeResolutionInPlayerPrefs` | `false` | 改写 PlayerPrefs 里的分辨率（旧思路，已被实测否证：请求 1680x1050 ≠ 桌面 2880x1800 时也成功进过独占全屏） |
| `Fix/KeepStoredExclusive` | `false` | 旧版遗留项，**实际无效**（改的是装箱副本），保留只为兼容旧配置 |
| `Debug/Verbose` | `true` | 详细日志 |
| `General/ConfigRevision` | `4` | 内部迁移标记，请勿手改 |

从旧版升级上来时，插件会自动把 `ReadOnlyMode` 关掉并写一条迁移提示；旧配置里被误开的 `ForceBorderless` / `ForceNativeResolution` 请手动确认为 `false`。

## 七、怎么确认它在工作

看两个地方：

1. `BepInEx\LogOutput.log` 里搜 `写盘前拦下了一次降级`。有这条 = 成功拦住了一次被改写。
2. 退出游戏后打开 `SPT_Runtime\user\sptSettings\Graphics.ini`，看 `"DisplaySettings": { "FullScreenMode": ? }`。**应该是 `0`**。如果还是 `1`，说明补丁没挂上，把 `LogOutput.log` 发我。

## 八、已知限制

1. **只修「保存」，不修「这一次能不能进独占全屏」**。后者是 DXGI 层面的时序问题。
2. 只在作者本机（MECHREVO WUJIE 14 / AMD Radeon 780M / Win11 26300 / 内屏 2880x1800@120）验证过。
3. 如果游戏在**本进程之外**写这个 ini（例如启动器），钩子拦不到。目前观察到的写盘都发生在游戏进程内。

## 九、从源码构建

```
cd FullscreenFix
dotnet build -c Release
```

产物在 `bin\Release\FullscreenFix.dll`，需要手动复制到游戏目录。

**坑**：如果仓库根有 `work\`（Il2CppDumper 的 DummyDll），MSBuild 会把它当 `CandidateAssemblyFiles` 抢先解析，导致引用到假程序集。csproj 里保留：

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);bin\**;obj\**</DefaultItemExcludes>
```

## 十、许可

MIT。
