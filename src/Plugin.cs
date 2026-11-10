using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using K1FullscreenFix.Config;
using K1FullscreenFix.Diagnostics;
using K1FullscreenFix.Display;
using K1FullscreenFix.Hooks;
using K1FullscreenFix.Runtime;

namespace K1FullscreenFix;

/// <summary>
/// 全屏修复 K1 —— 「等到时机对了再补一次」版。
///
/// 与旧版（dsh.FullscreenFix 1.x）的区别：
///   旧版一直在「拦」。拦 Unity 原生层的回退（拦不住——那段逻辑在 UnityPlayer.dll 的
///   C++ 里，托管侧没有任何方法可打补丁），或者改写玩家的选择（那等于骗人）。
///
/// 本版改成「事后补一次」：
///   引擎启动时那次独占全屏请求注定失败（Player.log 第 18 行就报
///   `dxgi: swapchain SetFullscreenState failed (887a0022)`，那时 BepInEx 都还没加载），
///   然后它自己把运行时降级成无边框。但玩家手动按 F9 确实能成功
///   （2026-11-10 实测；Player.log 第 422-426 行之后再无任何 dxgi 失败），
///   说明这不是硬件不支持，纯粹是时机问题。
///
/// 所以本插件：等主菜单出现 → 主动重新请求一次
///   `EFTDisplayHelper.SetResolution(w, h, ExclusiveFullScreen)` →
///   验证独占模式是否**连续保持**够久 → 失败就退避再试。
///
/// 它不改 Graphics.ini、不改 PlayerPrefs、不把「全屏」偷偷改成「无边框」。
/// 成功失败都原样写进日志（含独立的 K1Logs\K1FullscreenFix.log），不假装解决问题。
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "dsh.k1.fullscreen";
    public const string PluginName = "全屏修复 K1";
    public const string PluginVersion = "0.4.0";

    internal static new ManualLogSource Log;
    internal static Plugin Instance;
    internal static ModConfig Configs;
    internal static RetrySettings Settings;

    public override void Load()
    {
        Instance = this;
        Log = ((BasePlugin)this).Log;

        // 日志文件最先初始化——后面任何一步出事都要能落到文件里。
        K1Log.Init();

        K1Log.Banner($"全屏修复 K1 v{PluginVersion}");
        K1Log.Info($"独立日志文件：{K1Log.FilePath}");
        K1Log.Info($"游戏目录：{Safe(() => BepInEx.Paths.GameRootPath)}");
        K1Log.Info($"BepInEx 根：{Safe(() => BepInEx.Paths.BepInExRootPath)}");
        K1Log.Info($"插件 dll：{Safe(() => typeof(Plugin).Assembly.Location)}");
        K1Log.Info($"插件 guid：dsh.k1.fullscreen");

        try
        {
            Configs = new ModConfig(((BasePlugin)this).Config);

            // 关键：在纯托管的 Load() 里一次性把配置读成纯值。
            // IL2CPP 的 Start/Update 里绝不再碰 ConfigEntry（v0.2.0 就是在这里崩的）。
            Settings = RetrySettings.From(Configs);
            FullscreenRetrier.PendingSettings = Settings;

            // 让 General/Verbose 真正生效（v0.3.0 只读进了快照却没接上）。
            K1Log.SetVerbose(Settings.Verbose);

            K1Log.Info($"配置快照 → {Settings.Describe()}");

            if (!string.IsNullOrEmpty(Settings.ReadFailures))
            {
                K1Log.Warn($"以下配置项读取失败、已用默认值：{Settings.ReadFailures}");
            }

            DisplayApi.Resolve();
            DisplayApi.LogEnvironment();

            ClassInjector.RegisterTypeInIl2Cpp<FullscreenRetrier>();
            K1Log.Info("已向 IL2CPP 注册 FullscreenRetrier 类型。");

            AddComponent<FullscreenRetrier>();
            K1Log.Info("已添加 FullscreenRetrier 组件。");

            MenuHook.Apply(new Harmony(PluginGuid));
            ScreenHooks.Apply(new Harmony(PluginGuid));
        }
        catch (Exception ex)
        {
            K1Log.Error("初始化失败（插件不会生效）", ex);
        }

        K1Log.Info($"初始化流程结束。日志文件：{K1Log.FilePath}");
    }

    private static string Safe(Func<string> f)
    {
        try
        {
            return f() ?? "(null)";
        }
        catch (Exception ex)
        {
            return $"(读取失败 {ex.GetType().Name})";
        }
    }
}
