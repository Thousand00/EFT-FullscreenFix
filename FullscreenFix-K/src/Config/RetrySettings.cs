using System;
using UnityEngine;

namespace K1FullscreenFix.Config;

/// <summary>
/// 配置的一份「纯值快照」。
///
/// 为什么要有它（v0.2.0 崩掉的直接原因就在这里）：
///   之前 <see cref="Runtime.FullscreenRetrier"/> 在 IL2CPP 调用的 Start() / Update() 里
///   直接读 <c>ConfigEntry&lt;T&gt;.Value</c>。那条路径是 IL2CPP → 托管 trampoline，
///   任何一点意外（入口还没完全就绪、绑定对象被 GC、属性 getter 内部状态不对）
///   都会变成 NullReferenceException，而且日志里连行号都没有。
///
///   现在的做法：在 Plugin.Load()（纯托管、最安全的地方）把所有配置读成普通值，
///   存进这个类的实例。之后 IL2CPP 路径只碰 bool / int / float / KeyCode 这些纯值字段，
///   不再触碰任何 BepInEx 对象。
///
/// 即使真的读失败也不会崩：<see cref="From"/> 对每个键单独 try/catch，失败就用默认值，
/// 并把失败的键名记进 <see cref="ReadFailures"/> 写进日志。
/// </summary>
internal sealed class RetrySettings
{
    // ---- 总开关 ----
    internal bool Enabled = true;
    internal bool Verbose = true;

    // ---- 时机 ----
    internal bool AutoRetryOnStartup = true;
    internal int MaxAttempts = 10;
    internal float SettleTimeoutSeconds = 45f;
    internal float StableSeconds = 3f;
    internal float InitialDelaySeconds = 1f;
    internal float ResultTimeoutSeconds = 6f;
    internal float BackoffSeconds = 4f;
    internal float BackoffGrowth = 1.5f;
    internal float MaxBackoffSeconds = 45f;

    // ---- 分辨率 ----
    internal bool UseNativeResolution;

    // ---- 热键 ----
    internal bool ManualHotkeyEnabled = true;
    internal KeyCode ManualHotkey = KeyCode.F9;

    /// <summary>读失败的键名（逗号分隔），用于写日志自证。</summary>
    internal string ReadFailures = "";

    internal static RetrySettings From(ModConfig c)
    {
        var s = new RetrySettings();

        if (c == null)
        {
            s.ReadFailures = "ModConfig 为 null，全部使用默认值";
            return s;
        }

        var failures = new System.Text.StringBuilder();

        s.Enabled = Read(() => c.Enabled.Value, s.Enabled, "General/Enabled", failures);
        s.Verbose = Read(() => c.Verbose.Value, s.Verbose, "General/Verbose", failures);

        s.AutoRetryOnStartup = Read(() => c.AutoRetryOnStartup.Value, s.AutoRetryOnStartup, "Retry/AutoRetryOnStartup", failures);
        s.MaxAttempts = Read(() => c.MaxAttempts.Value, s.MaxAttempts, "Retry/MaxAttempts", failures);
        s.SettleTimeoutSeconds = Read(() => c.SettleTimeoutSeconds.Value, s.SettleTimeoutSeconds, "Retry/SettleTimeoutSeconds", failures);
        s.StableSeconds = Read(() => c.StableSeconds.Value, s.StableSeconds, "Retry/StableSeconds", failures);
        s.InitialDelaySeconds = Read(() => c.InitialDelaySeconds.Value, s.InitialDelaySeconds, "Retry/InitialDelaySeconds", failures);
        s.ResultTimeoutSeconds = Read(() => c.ResultTimeoutSeconds.Value, s.ResultTimeoutSeconds, "Retry/ResultTimeoutSeconds", failures);
        s.BackoffSeconds = Read(() => c.BackoffSeconds.Value, s.BackoffSeconds, "Retry/BackoffSeconds", failures);
        s.BackoffGrowth = Read(() => c.BackoffGrowth.Value, s.BackoffGrowth, "Retry/BackoffGrowth", failures);
        s.MaxBackoffSeconds = Read(() => c.MaxBackoffSeconds.Value, s.MaxBackoffSeconds, "Retry/MaxBackoffSeconds", failures);

        s.ManualHotkeyEnabled = Read(() => c.ManualHotkeyEnabled.Value, s.ManualHotkeyEnabled, "Retry/ManualHotkeyEnabled", failures);

        string res = Read(() => c.Resolution.Value, "Current", "Retry/Resolution", failures);
        s.UseNativeResolution = !string.IsNullOrEmpty(res)
            && res.Trim().Equals("Native", StringComparison.OrdinalIgnoreCase);

        string hk = Read(() => c.ManualHotkey.Value, "F9", "Retry/ManualHotkey", failures);
        if (!string.IsNullOrEmpty(hk))
        {
            try
            {
                if (Enum.TryParse<KeyCode>(hk.Trim(), true, out var key)) s.ManualHotkey = key;
                else failures.Append("Retry/ManualHotkey(无法解析) ");
            }
            catch
            {
                failures.Append("Retry/ManualHotkey(解析抛异常) ");
            }
        }

        // 兜底钳制，避免配置写成 0 或负数把状态机卡死。
        s.MaxAttempts = Math.Max(1, s.MaxAttempts);
        s.StableSeconds = Math.Max(0.5f, s.StableSeconds);
        s.ResultTimeoutSeconds = Math.Max(1f, s.ResultTimeoutSeconds);
        s.InitialDelaySeconds = Math.Max(0f, s.InitialDelaySeconds);
        s.BackoffSeconds = Math.Max(0f, s.BackoffSeconds);
        s.BackoffGrowth = Math.Max(1f, s.BackoffGrowth);
        s.MaxBackoffSeconds = Math.Max(s.BackoffSeconds, s.MaxBackoffSeconds);
        s.SettleTimeoutSeconds = Math.Max(1f, s.SettleTimeoutSeconds);

        s.ReadFailures = failures.ToString().Trim();
        return s;
    }

    private static T Read<T>(Func<T> getter, T fallback, string name, System.Text.StringBuilder failures)
    {
        try
        {
            return getter();
        }
        catch (Exception ex)
        {
            failures.Append(name).Append('(').Append(ex.GetType().Name).Append(") ");
            return fallback;
        }
    }

    internal string Describe()
    {
        return $"Enabled={Enabled}, Verbose={Verbose}, AutoRetryOnStartup={AutoRetryOnStartup}, "
             + $"MaxAttempts={MaxAttempts}, SettleTimeout={SettleTimeoutSeconds:F0}s, "
             + $"Stable={StableSeconds:F1}s, InitialDelay={InitialDelaySeconds:F1}s, "
             + $"ResultTimeout={ResultTimeoutSeconds:F1}s, Backoff={BackoffSeconds:F1}s "
             + $"x{BackoffGrowth:F1} (max {MaxBackoffSeconds:F0}s), "
             + $"Resolution={(UseNativeResolution ? "Native" : "Current")}, "
             + $"Hotkey={(ManualHotkeyEnabled ? ManualHotkey.ToString() : "关闭")}";
    }
}
