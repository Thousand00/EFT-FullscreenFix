# EFT 5.0 BE 全屏修复 · 双系列仓库

《逃离塔科夫》离线版（SPT 5.0 BE）上「屏幕模式选『全屏』，实际却是『无边框』」的排查与修复。
本仓库并排放着**两个独立迭代的插件系列**，它们解决同一个问题，但走的是完全不同的路。

| | **主线 · FullscreenFix** | **K 系列 · FullscreenFix-K** |
|---|---|---|
| 目录 | `FullscreenFix/` | `FullscreenFix-K/` |
| 插件 GUID | `dsh.FullscreenFix` | `dsh.k1.fullscreen` |
| 当前版本 | `2.0.0-readonly` | `0.4.0` |
| 思路 | 拦截 + 改写：Harmony 打补丁改写入参、钉住 PlayerPrefs，另配「只读记录模式」取证 | 只重发：不做任何拦截，等信号对了再请求一次独占全屏，然后如实报告 |
| 是否改玩家设置 | 会（关掉只读模式时） | 从不 |
| 状态 | 只读取证模式，**不修 bug** | 公测中，v0.4.0 待实测 |
| 文档 | `FullscreenFix/README.md` | `FullscreenFix-K/README.md` |

**两个系列会一直各自往下迭代**，互不合并。选哪个装：

- 想**先看清楚这台机器到底什么状态** → 主线（只读模式，只落盘不修改）。
- 想**让它现在就试着进独占全屏** → K 系列。

> 两者**不要同时装**。它们都会去动显示模式，装在一起日志会互相污染，出问题也没法判断是谁干的。

---

## 一、这个问题到底是什么

`Graphics.ini` 和注册表里存的**都是 `0`（= 独占全屏）**，设置从来没被改回去。

真正发生的是：游戏申请独占全屏 → DXGI 拒绝（`0x887A0022`）→ **Unity 自己在运行时降级**成无边框。

`Player.log` 里就是这四行：

```
dxgi: swapchain SetFullscreenState failed (887a0022).
Failed to apply requested ExclusiveFullScreen resolution (1680x1050)...will try again
Unable to apply requested ExclusiveFullScreen resolution again (1680x1050)...reverting to current display resolution: 2880x1800
Failed to change display to ExclusiveFullscreen...reverting to FullscreenWindow
```

这三串字符串在 `UnityPlayer.dll` 里各命中 1 次，在 `Assembly-CSharp.dll`、`GameAssembly.dll`、
`global-metadata.dat` 里**全部 0 命中** —— 说明「切换失败 → 回退无边框」是 **Unity 原生 C++** 的行为，
托管侧没有任何方法能打补丁拦住它。

决定性的反证：**手动切换能成功**（按热键或进设置切一下就行，成功后几十万行日志里再无 dxgi 失败）。
同一台机器、同一个分辨率、同一个 API —— 早失败、晚成功，所以这是**纯粹的时机问题**。

两个系列就是对「时机」这件事的两种回答。

---

## 二、目录结构

```
full/
├─ README.md                   ← 你正在看的这份
├─ FullscreenFix/              主线系列
│  ├─ FullscreenFix.csproj
│  ├─ README.md                主线自己的文档（思路、配置、排障）
│  ├─ docs/                    交接文档、发布帖、诊断报告
│  └─ src/
│     ├─ FullscreenFix/        入口与运行时组件
│     ├─ FullscreenFix.Config/ 配置定义
│     └─ FullscreenFix.Patches/ 三个 Harmony 补丁
├─ FullscreenFix-K/            K 系列
│  ├─ FullscreenFix.csproj
│  ├─ README.md                K 系列自己的文档
│  ├─ docs/                    发布帖
│  └─ src/
│     ├─ Config/               配置与纯值快照
│     ├─ Diagnostics/          独立日志文件
│     ├─ Display/              EFTDisplayHelper 反射封装
│     ├─ Hooks/                菜单 / 模式选择界面钩子
│     └─ Runtime/              重试状态机
├─ work/                       抓包、反编译、探针的历史工作区（**不入库**）
└─ _archive/                   已停用的旧工程与备份（**不入库**）
```

`work/` 里有 Il2CppDumper 的产物、DXGI 探针工程、反编译代码，体积约 750 MB；
`_archive/` 里是旧的顶层工程、多份 `_backup_*`、中文汉化插件（`cmcn/`）等。
两处都只留在本地，**不推送**。

---

## 三、标签规则

两个系列各有各的版本号，所以标签**必须带前缀**，否则 `v0.4.0` 到底是哪个系列的说不清。

| 标签 | 指向 |
|---|---|
| `mainline-v2.0.0-readonly` | 主线 2.0.0 只读取证版 |
| `k-v0.4.0` | K 系列 0.4.0（模式选择界面感知） |
| `k-v0.3.1` | K 系列 0.3.1 |
| `k-v0.3.0` | K 系列 0.3.0（首个带独立日志的版本） |

```bash
git tag                              # 列出全部
git checkout k-v0.4.0                # 回到某个版本
git switch -c 从k0.4.0继续            # 在那个版本上继续改
```

**也可以直接下载成品**，不用编译：

- [K 系列 v0.4.0](https://github.com/Thousand00/EFT-FullscreenFix/releases/tag/k-v0.4.0) —— 解压到游戏目录即可
- [主线 v2.0.0-readonly](https://github.com/Thousand00/EFT-FullscreenFix/releases/tag/mainline-v2.0.0-readonly) —— 同样解压即用

两个 Release 的 zip 里已经放好 `BepInEx/plugins/<名字>/` 这层目录，直接解压到游戏根目录就能用。

早期用过不带前缀的 `v0.3.0` / `v0.4.0`，已重命名为 `k-v0.3.0` / `k-v0.4.0`，内容与原来一致。

> **`k-v0.4.0` 指向的是目录重组后的提交**，检出后代码在 `FullscreenFix-K/` 下，进去编译即可。
> 代码与重组前那一提交完全一致（8 个源文件 + csproj 的 blob hash 相同），只是路径变了。
>
> **`k-v0.3.0` / `k-v0.3.1` 是重组之前的布局**：那时 K 系列还在仓库根目录（`src/`、`FullscreenFix.csproj`），
> 没有 `FullscreenFix-K/` 这层。检出这两个 tag 后直接在仓库根 `dotnet build -c Release` 即可。
> 它们代表真实的旧代码，没有重新指向，否则就分不清版本了。

---

## 四、编译

两个系列都用 .NET 10 SDK，且都默认从 `D:\EFT-5.0 BE` 取 BepInEx 引用（可用 `/p:SptRoot=...` 覆盖）。

```bash
cd FullscreenFix    && dotnet build -c Release
cd FullscreenFix-K  && dotnet build -c Release
```

产物：

- 主线 → `FullscreenFix/bin/Release/FullscreenFix.dll`
- K 系列 → `FullscreenFix-K/bin/Release/K1FullscreenFix.dll`（同时复制到 `dist/`）

装到 `BepInEx\plugins\<插件名>\` 即可。

> **踩过的坑**：`work/` 里有 Il2CppDumper 生成的 `DummyDll`（含同名 `UnityEngine.CoreModule.dll`），
> MSBuild 会把它当 `CandidateAssemblyFiles` 抢先解析，导致引用到假程序集、报一堆莫名其妙的类型错误。
> 两个 csproj 里都因此保留了 `DefaultItemExcludes`，别删。

---

## 五、日志在哪

| 系列 | 位置 |
|---|---|
| 主线 | 写 BepInEx 主日志 `BepInEx\LogOutput.log`；启动快照单独落盘 |
| K 系列 | 独立文件 `BepInEx\K1Logs\K1FullscreenFix.log`（关键行同时也进主日志） |

给别人排查时，主线要 `LogOutput.log`，K 系列只要 `K1Logs\K1FullscreenFix.log`。
K 系列每次启动会把上一份改名成 `K1FullscreenFix.prev.log`，只保留两份。

---

## 六、许可

MIT。
