using System;
using K1FullscreenFix.Config;
using K1FullscreenFix.Display;
using K1FullscreenFix.Diagnostics;
using UnityEngine;

namespace K1FullscreenFix.Runtime;

/// <summary>
/// 重试器：等到「时机对了」再重新请求一次独占全屏，然后验证它是不是真的稳住了。
///
/// 为什么用 Update + 状态机，而不是 Coroutine：
///   IL2CPP 下 MonoBehaviour 协程要额外注入 IEnumerator 包装类型，
///   从注入组件发起 StartCoroutine 在加载早期容易出问题。
///   Update 轮询没有这些依赖（每帧只判断状态，不做重活）。
///
/// v0.3.0 相对 v0.2.0 的三个修正：
///   1. 【崩溃】v0.2.0 的 Start()/EnterSettle()/EnterDelay() 抛 NullReferenceException。
///      共同点是都在 IL2CPP 调用路径上直接读 <c>ConfigEntry&lt;T&gt;.Value</c>（BepInEx 对象）。
///      现在改成：配置在 Plugin.Load() 里一次性快照成 <see cref="RetrySettings"/> 纯值，
///      本类只碰 bool/int/float/KeyCode，一个 BepInEx 对象都不再接触。
///      并且快照是「先赋值、再调用」的静态字段，Start() 永远拿得到非 null 的配置。
///   2. 【刷屏】v0.2.0 因为 EnterSettle() 抛异常，_phase 卡在 WaitReady，
///      导致「屏幕已就绪」每 0.5s 打一次、`✘ 失败` 打了 24 次。
///      现在所有状态转移都是「先改状态、再写日志」，日志抛异常也不会卡住状态机；
///      高频细节走 <see cref="K1Log.Detail"/> 只进独立文件，不进 BepInEx 主日志。
///   3. 【时机】不再靠猜秒数，而是优先等「主菜单已显示」这个确定的事件
///      （<see cref="Hooks.MenuHook"/>），超时才退回定时判断。
/// </summary>
public sealed class FullscreenRetrier : MonoBehaviour
{
    private enum Phase
    {
        WaitSignal,
        DelayBeforeTry,
        ObserveResult,
        Done,
    }

    private const float PollInterval = 0.5f;
    private const float MonitorInterval = 10f;

    /// <summary>
    /// 配置快照。由 <see cref="Plugin"/> 在 AddComponent 之前赋好，
    /// 所以 Start() 里它一定不是 null（即使真是 null 也会兜底成默认值）。
    /// </summary>
    internal static RetrySettings PendingSettings;

    /// <summary>主菜单是否已经显示过。由 MenuHook 写入。</summary>
    internal static bool MenuSeen;

    private static FullscreenRetrier _current;

    private RetrySettings _cfg;

    private Phase _phase = Phase.WaitSignal;
    private float _startedAt;
    private float _phaseStartedAt;
    private float _phaseEndsAt;

    private int _attempt;
    private int _successes;
    private float _nextBackoff = -1f;

    private int _requestW;
    private int _requestH;
    private string _lastError;

    private float _nextPoll;
    private float _nextMonitor;

    private FullScreenMode _lastMode;
    private bool _hasLastMode;

    private FullScreenMode _observedMode;
    private bool _hasObservedMode;
    private float _observedSince;

    private bool _started;
    private bool _finished;

    // ---- 供外部（Plugin / MenuHook）使用 ----

    internal static void SignalMenuReady()
    {
        MenuSeen = true;

        var inst = _current;
        if (inst == null) return;

        try
        {
            inst.OnMenuReady();
        }
        catch (Exception ex)
        {
            K1Log.Warn($"处理主菜单信号失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    public FullscreenRetrier(IntPtr ptr) : base(ptr) { }

    private void Start()
    {
        try
        {
            _current = this;

            // 拷一份，之后再不碰 BepInEx 的任何对象。
            _cfg = PendingSettings ?? new RetrySettings();

            float now = SafeNow();
            _startedAt = now;
            _phase = Phase.WaitSignal;
            _phaseStartedAt = now;
            _phaseEndsAt = now + _cfg.SettleTimeoutSeconds;
            _nextPoll = now;
            _started = true;

            K1Log.Info($"重试器已启动。配置 → {_cfg.Describe()}");

            if (!string.IsNullOrEmpty(_cfg.ReadFailures))
            {
                K1Log.Warn($"以下配置项读取失败、已用默认值：{_cfg.ReadFailures}");
            }

            if (!_cfg.Enabled)
            {
                K1Log.Info("总开关 Enabled=false：只记录，不做任何显示切换。");
                _phase = Phase.Done;
                _finished = true;
                _nextMonitor = now + MonitorInterval;
                return;
            }

            if (!_cfg.AutoRetryOnStartup)
            {
                K1Log.Info("AutoRetryOnStartup=false：不自动尝试，只在后台记录。按热键可手动触发。");
                _phase = Phase.Done;
                _finished = true;
                _nextMonitor = now + MonitorInterval;
                return;
            }

            K1Log.Info(
                $"等待信号：优先等主菜单出现（最多 {_cfg.SettleTimeoutSeconds:F0}s），"
                + "超时就按定时判断。到点后请求独占全屏，并验证它能否连续保持 "
                + $"{_cfg.StableSeconds:F1}s。");
        }
        catch (Exception ex)
        {
            // 这里绝不能抛：IL2CPP 的 Start 抛出去就是 trampoline 异常 + 状态机卡死。
            K1Log.Error("Start() 内部异常（已吞掉，插件仍然可用）", ex);
        }
    }

    private void Update()
    {
        if (!_started) return;

        try
        {
            float now = SafeNow();

            if (_cfg == null) _cfg = new RetrySettings();

            if (_cfg.ManualHotkeyEnabled)
            {
                HandleHotkey(now);
            }

            if (!_cfg.Enabled) return;

            switch (_phase)
            {
                case Phase.WaitSignal:
                    TickWaitSignal(now);
                    break;
                case Phase.DelayBeforeTry:
                    TickDelayBeforeTry(now);
                    break;
                case Phase.ObserveResult:
                    TickObserve(now);
                    break;
                case Phase.Done:
                    TickMonitor(now);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 状态先推进再日志，所以即使日志抛异常也不会把状态机卡住。
            // 但仍然只报第一次，避免刷屏。
            if (!_loggedUpdateFailure)
            {
                _loggedUpdateFailure = true;
                K1Log.Error("Update() 内部异常（已吞掉）", ex);
            }
        }
    }

    private bool _loggedUpdateFailure;

    // ------------------------------------------------------------- phases

    /// <summary>
    /// 等一个「现在可以动手了」的信号。
    /// 优先用主菜单出现这个确定事件；等不到就按超时退化为定时判断。
    /// </summary>
    private void TickWaitSignal(float now)
    {
        if (!MenuSeen && now < _phaseEndsAt) return;

        // 先转移状态，再写日志——顺序不能反。
        if (MenuSeen)
        {
            EnterDelay(now, _cfg.InitialDelaySeconds, "主菜单已出现");
        }
        else
        {
            EnterDelay(now, _cfg.InitialDelaySeconds, "等待信号超时，按定时判断");
        }
    }

    private void TickDelayBeforeTry(float now)
    {
        if (now < _phaseEndsAt) return;

        if (_attempt >= _cfg.MaxAttempts)
        {
            Finish("已达最大尝试次数");
            return;
        }

        _attempt++;

        ResolveRequestedResolution(out _requestW, out _requestH);

        K1Log.Info(
            $"第 {_attempt}/{_cfg.MaxAttempts} 次尝试：请求 {_requestW}x{_requestH} 独占全屏。"
            + $" 之前 → {DisplayApi.Snapshot()}");

        bool sent = DisplayApi.RequestExclusive(_requestW, _requestH, out _lastError);

        if (!sent)
        {
            K1Log.Error($"第 {_attempt} 次请求根本没发出去（{_lastError}）。这通常是反射入口失效，不是显示栈问题。");
            Finish("请求无法发出");
            return;
        }

        // 先转移状态，再初始化观察窗口。
        _phase = Phase.ObserveResult;
        _phaseStartedAt = now;
        _phaseEndsAt = now + _cfg.ResultTimeoutSeconds;

        _hasObservedMode = false;
        _observedMode = SafeMode();
        _hasObservedMode = true;
        _observedSince = now;

        K1Log.Detail($"[观察开始] 初始 mode={_observedMode}  {DisplayApi.Snapshot()}");
    }

    private void TickObserve(float now)
    {
        if (now < _nextPoll) return;
        _nextPoll = now + PollInterval;

        var mode = SafeMode();

        if (!_hasObservedMode || mode != _observedMode)
        {
            K1Log.Detail($"[观察] {_observedMode} → {mode}  {DisplayApi.Snapshot()}");

            _hasObservedMode = true;
            _observedMode = mode;
            _observedSince = now;
        }

        float heldFor = now - _observedSince;

        if (mode == FullScreenMode.ExclusiveFullScreen)
        {
            if (heldFor < _cfg.StableSeconds) return;

            _successes++;
            K1Log.Info(
                $"✔ 第 {_attempt} 次尝试成功：ExclusiveFullScreen 已连续保持 {heldFor:F1}s。"
                + $" 请求 {_requestW}x{_requestH}，{DisplayApi.Snapshot()}");

            Finish("成功进入独占全屏");
            return;
        }

        // 没进独占，或进了又掉出来：等到观察窗口结束再判失败，别过早打断引擎。
        if (now < _phaseEndsAt) return;

        K1Log.Warn(
            $"✘ 第 {_attempt} 次尝试未成功：观察 {_cfg.ResultTimeoutSeconds:F0}s 后是 {mode}，"
            + $"不是 ExclusiveFullScreen。{DisplayApi.Snapshot()}");

        if (_attempt >= _cfg.MaxAttempts)
        {
            Finish("次数用尽仍未成功");
            return;
        }

        // 指数退避：5s → 8s → 12.8s → 20.5s → 25s(封顶)。
        _nextBackoff = _nextBackoff < 0f
            ? _cfg.BackoffSeconds
            : Math.Min(_cfg.MaxBackoffSeconds, _nextBackoff * _cfg.BackoffGrowth);

        EnterDelay(now, _nextBackoff, $"第 {_attempt + 1} 次尝试前退避");
    }

    private void TickMonitor(float now)
    {
        if (now < _nextMonitor) return;
        _nextMonitor = now + MonitorInterval;

        var mode = SafeMode();

        if (!_hasLastMode)
        {
            _hasLastMode = true;
            _lastMode = mode;
            return;
        }

        if (mode == _lastMode) return;

        K1Log.Info($"显示模式变化（后台监视）：{_lastMode} → {mode}  {DisplayApi.Snapshot()}");
        _lastMode = mode;
    }

    // ------------------------------------------------------------ helpers

    private void OnMenuReady()
    {
        if (!_started || _cfg == null || !_cfg.Enabled) return;
        if (_phase != Phase.WaitSignal) return;

        // 主菜单出现时立刻动手。
        EnterDelay(SafeNow(), Math.Max(_cfg.InitialDelaySeconds, 0.5f), "主菜单已出现");
    }

    private void EnterDelay(float now, float seconds, string reason)
    {
        float wait = Math.Max(0f, seconds);

        _phase = Phase.DelayBeforeTry;
        _phaseStartedAt = now;
        _phaseEndsAt = now + wait;
        _nextPoll = now;

        K1Log.Info($"{reason}：等 {wait:F1}s 后发请求。");
    }

    private void Finish(string reason)
    {
        _phase = Phase.Done;
        _finished = true;
        _nextMonitor = SafeNow() + MonitorInterval;
        _hasLastMode = false;

        if (_successes > 0)
        {
            K1Log.Info($"结束（{reason}）：成功 {_successes} 次。按热键可再试。");
        }
        else
        {
            K1Log.Warn(
                $"结束（{reason}）：本次会话仍未能进入独占全屏。"
                + " 这不是插件『没生效』——每次请求都真的发出去了，是显示栈拒绝了它。"
                + " 可在进图后、或 Alt+Tab 回来之后按热键手动再试。");
        }
    }

    private void ResolveRequestedResolution(out int w, out int h)
    {
        if (_cfg.UseNativeResolution)
        {
            try
            {
                var main = UnityEngine.Display.main;
                if (main.systemWidth > 0 && main.systemHeight > 0)
                {
                    w = main.systemWidth;
                    h = main.systemHeight;
                    return;
                }
            }
            catch
            {
                // 落到下面用当前尺寸
            }
        }

        try
        {
            w = Screen.width;
            h = Screen.height;
        }
        catch
        {
            w = 0;
            h = 0;
        }
    }

    private void HandleHotkey(float now)
    {
        if (Input.GetKeyDown(_cfg.ManualHotkey))
        {
            K1Log.Info($"热键 {_cfg.ManualHotkey} 手动触发。之前 → {DisplayApi.Snapshot()}");

            _attempt = 0;
            _nextBackoff = -1f;
            ResolveRequestedResolution(out _requestW, out _requestH);

            bool sent = DisplayApi.RequestExclusive(_requestW, _requestH, out _lastError);

            if (!sent)
            {
                K1Log.Error($"手动请求没发出去（{_lastError}）。");
                return;
            }

            _attempt = 1;
            _phase = Phase.ObserveResult;
            _phaseStartedAt = now;
            _phaseEndsAt = now + Math.Max(1f, _cfg.ResultTimeoutSeconds);

            _hasObservedMode = true;
            _observedMode = SafeMode();
            _observedSince = now;

            K1Log.Detail($"[观察开始/手动] 初始 mode={_observedMode}  {DisplayApi.Snapshot()}");
        }

        // 允许手动把已经结束的重试器重新武装起来。
        if (_finished && _successes == 0 && _phase == Phase.Done)
        {
            // 什么都不做：Done 状态下只监视，热键随时可以再发请求（上面已处理）。
        }
    }

    private static float SafeNow()
    {
        try
        {
            return Time.unscaledTime;
        }
        catch
        {
            return 0f;
        }
    }

    private static FullScreenMode SafeMode()
    {
        try
        {
            return Screen.fullScreenMode;
        }
        catch
        {
            return FullScreenMode.Windowed;
        }
    }
}
