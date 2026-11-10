using BepInEx.Configuration;

namespace K1FullscreenFix.Config;

/// <summary>
/// 配置定义。注意：这里只负责「绑定」，运行时不直接读这些对象。
///
/// 真正的读取发生在 <see cref="Plugin.Load"/> 里，通过
/// <see cref="RetrySettings.From"/> 一次性快照成纯值。
/// 原因见 <see cref="RetrySettings"/> 的注释——IL2CPP 的 Start/Update 路径上
/// 碰 BepInEx 对象是 v0.2.0 抛 NullReferenceException 的根源。
/// </summary>
internal sealed class ModConfig
{
    public readonly ConfigEntry<bool> Enabled;
    public readonly ConfigEntry<bool> Verbose;

    public readonly ConfigEntry<bool> AutoRetryOnStartup;
    public readonly ConfigEntry<int> MaxAttempts;
    public readonly ConfigEntry<float> SettleTimeoutSeconds;
    public readonly ConfigEntry<float> StableSeconds;
    public readonly ConfigEntry<float> InitialDelaySeconds;
    public readonly ConfigEntry<float> ResultTimeoutSeconds;
    public readonly ConfigEntry<float> BackoffSeconds;
    public readonly ConfigEntry<float> BackoffGrowth;
    public readonly ConfigEntry<float> MaxBackoffSeconds;
    public readonly ConfigEntry<string> Resolution;

    public readonly ConfigEntry<bool> ManualHotkeyEnabled;
    public readonly ConfigEntry<string> ManualHotkey;

    public ModConfig(ConfigFile cfg)
    {
        Enabled = cfg.Bind(
            "General", "Enabled", true,
            "总开关。关掉后本插件不会发起任何显示切换，只剩日志。");

        Verbose = cfg.Bind(
            "General", "Verbose", true,
            "详细日志：每次采样到的显示模式变化都写进独立日志文件（K1Logs/K1FullscreenFix.log）。\n"
            + "这个开关只影响独立日志文件的详细程度，BepInEx 主日志只收关键行。");

        AutoRetryOnStartup = cfg.Bind(
            "Retry", "AutoRetryOnStartup", true,
            "启动后自动尝试切独占全屏。\n"
            + "\n"
            + "为什么值得试：引擎启动时那次请求注定失败（Player.log 第 18 行\n"
            + "  `dxgi: swapchain SetFullscreenState failed (887a0022)`，那时 BepInEx 还没加载），\n"
            + "  然后它自己把运行时降级成无边框。但你手动按 F9 确实能成功（2026-11-10 实测，\n"
            + "  Player.log 第 422-426 行之后再无任何 dxgi 失败），说明纯粹是时机问题。");

        MaxAttempts = cfg.Bind(
            "Retry", "MaxAttempts", 10,
            "最多自动尝试几次。\n"
            + "\n"
            + "旧版默认 3 次、间隔 3s：三次都挤在启动后十几秒内，全部撞在引擎自己的\n"
            + "  『失败 → will try again → again → reverting』流程里。\n"
            + "  新版配合退避增长，6 次能覆盖到启动后约 1 分钟，足够越过引擎的折腾期。");

        SettleTimeoutSeconds = cfg.Bind(
            "Retry", "SettleTimeoutSeconds", 45f,
            "最多等多久「主菜单出现」这个信号；超时就直接开始尝试。\n"
            + "冷启动、机械硬盘、装了很多 mod 的机器可以调大。");

        StableSeconds = cfg.Bind(
            "Retry", "StableSeconds", 3f,
            "判定『真的成功了』要求 ExclusiveFullScreen 连续保持多少秒。\n"
            + "\n"
            + "为什么不能看一眼就信：请求发出瞬间 Unity 会先把期望模式置上，swapchain 失败后\n"
            + "  才回退。v0.1.0 就是只采样一次、读到回退前的瞬态值而误报成功\n"
            + "  （BepInEx 日志第 439 行 ✔ 紧跟第 441-444 行的回退）。\n"
            + "  3s ≈ 6 次 0.5s 采样都还留在独占模式。");

        InitialDelaySeconds = cfg.Bind(
            "Retry", "InitialDelaySeconds", 1f,
            "确认可以动手之后，再等几秒发第一次请求（给引擎一点缓冲）。");

        ResultTimeoutSeconds = cfg.Bind(
            "Retry", "ResultTimeoutSeconds", 6f,
            "一次尝试最多观察多久。观察到独占模式后改为按 StableSeconds 判定，不受这个限制。");

        BackoffSeconds = cfg.Bind(
            "Retry", "BackoffSeconds", 4f,
            "第一次失败后等多久再试。");

        BackoffGrowth = cfg.Bind(
            "Retry", "BackoffGrowth", 1.5f,
            "每次失败后间隔乘以这个系数（指数退避）。\n"
            + "  5s → 8s → 12.8s → 20.5s → 25s(封顶)，累计约 1 分钟覆盖启动后的折腾期。\n"
            + "  设定为 1.0 则退化成固定间隔。");

        MaxBackoffSeconds = cfg.Bind(
            "Retry", "MaxBackoffSeconds", 45f,
            "退避间隔的上限，避免长时间挂机后间隔变得离谱。");

        Resolution = cfg.Bind(
            "Retry", "Resolution", "Current",
            "请求独占全屏时用哪个分辨率。\n"
            + "  Current = 用当前 Screen.width/height（实测可行，保持原样最不容易出意外）\n"
            + "  Native  = 用显示器物理分辨率 Display.main.systemWidth/Height\n"
            + "\n"
            + "补充：旧文档说『请求分辨率必须等于桌面当前模式』，这条已被实测推翻\n"
            + "  （2026-11-10 14:47 请求 1680x1050 ≠ 桌面 2880x1800 仍成功），不必强求一致。");

        ManualHotkeyEnabled = cfg.Bind(
            "Retry", "ManualHotkeyEnabled", true,
            "允许用热键手动触发一次尝试（在你想要的时候，比如进图后、Alt+Tab 回来之后）。");

        ManualHotkey = cfg.Bind(
            "Retry", "ManualHotkey", "F9",
            "手动触发的按键。写法同 UnityEngine.KeyCode 的枚举名：F9、F10、Insert、Home 等。");
    }
}
