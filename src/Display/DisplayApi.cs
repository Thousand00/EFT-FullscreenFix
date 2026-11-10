using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace K1FullscreenFix.Display;

/// <summary>
/// 对 EFT 自带显示接口的反射封装。
///
/// 为什么全部走反射：<c>EFT.Settings.Graphics.EFTDisplayHelper</c> 是 Il2CppInterop
/// 运行时生成的类型，编译期引用它会直接 CS0122（不可访问）。字符串反射既能绕开，
/// 也能在游戏更新改名时优雅降级为「找不到 → 只记录不动作」，而不是整个插件崩掉。
///
/// 用到的三个入口（签名已用 ilspycmd 对 Assembly-CSharp.dll 核对过）：
///   EFTDisplayHelper.SetResolution(int width, int height, FullScreenMode mode) -> Task
///       [CallerCount(8)]，未内联，public static —— 这是唯一可靠的重发请求入口。
///   EFTDisplayHelper.IsScreenReady() -> bool
///       [CallerCount(1)]，用来判断「现在切换是否安全」。
///   EFTDisplayHelper.ApplyDelay { get; } -> Il2CppSystem.TimeSpan
///       这是【内联属性】（InlinedInto _SetResolution_d__19.MoveNext），打不了补丁，
///       但可以读它的值，用来对齐官方自己等待的时长——只作诊断参考。
/// </summary>
internal static class DisplayApi
{
    private const string HelperTypeName = "EFT.Settings.Graphics.EFTDisplayHelper";

    internal static bool Available { get; private set; }

    private static MethodInfo _setResolution3;
    private static MethodInfo _setResolution2;
    private static MethodInfo _isScreenReady;
    private static PropertyInfo _applyDelay;
    private static bool _resolved;

    internal static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        Type type = null;
        try
        {
            type = AccessTools.TypeByName(HelperTypeName);
        }
        catch (Exception ex)
        {
            Warn($"AccessTools.TypeByName 失败：{ex.GetType().Name}: {ex.Message}");
        }

        if (type == null)
        {
            // 退一步：直接扫已加载程序集，避免 AccessTools 在某些加载时序下漏掉。
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetType(HelperTypeName, false);
                }
                catch
                {
                    // 某些 interop 程序集在 GetType 时会抛，忽略继续找。
                }
                if (type != null) break;
            }
        }

        if (type == null)
        {
            Warn($"找不到类型 {HelperTypeName}，本插件不会做任何显示操作。");
            return;
        }

        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static;

        try
        {
            _setResolution3 = type.GetMethod(
                "SetResolution",
                Flags,
                null,
                new[] { typeof(int), typeof(int), typeof(FullScreenMode) },
                null);
        }
        catch (Exception ex)
        {
            Warn($"解析 SetResolution(int,int,FullScreenMode) 失败：{ex.Message}");
        }

        try
        {
            _setResolution2 = type.GetMethod(
                "SetResolution",
                Flags,
                null,
                new[] { typeof(int), typeof(int) },
                null);
        }
        catch
        {
            // 两参重载只是备用，拿不到不影响主路径。
        }

        try
        {
            _isScreenReady = type.GetMethod("IsScreenReady", Flags, null, Type.EmptyTypes, null);
        }
        catch (Exception ex)
        {
            Warn($"解析 IsScreenReady() 失败：{ex.Message}");
        }

        try
        {
            _applyDelay = type.GetProperty("ApplyDelay", Flags);
        }
        catch
        {
            // 诊断用，拿不到就算了。
        }

        Available = _setResolution3 != null;

        Info(
            $"[K1] 显示接口解析结果：SetResolution(3参)={Yn(_setResolution3 != null)}"
            + $"，SetResolution(2参)={Yn(_setResolution2 != null)}"
            + $"，IsScreenReady={Yn(_isScreenReady != null)}"
            + $"，ApplyDelay={Yn(_applyDelay != null)}");

        if (!Available)
        {
            Warn("[K1] 拿不到 SetResolution(int,int,FullScreenMode)，重试功能不可用。");
        }
    }

    /// <summary>读官方自己的等待时长，仅用于日志对照。</summary>
    internal static string DescribeApplyDelay()
    {
        if (_applyDelay == null) return "未知（属性未解析到）";

        try
        {
            object boxed = _applyDelay.GetValue(null);
            if (boxed == null) return "未知（返回 null）";

            var ms = boxed.GetType().GetProperty("TotalMilliseconds");
            if (ms != null)
            {
                object v = ms.GetValue(boxed);
                if (v != null) return $"{Convert.ToDouble(v):F0} ms";
            }

            return boxed.ToString();
        }
        catch (Exception ex)
        {
            return $"读取失败（{ex.GetType().Name}: {ex.Message}）";
        }
    }

    internal static bool IsScreenReady()
    {
        if (_isScreenReady == null) return true;   // 拿不到就认为可以试，由调用方自己靠超时兜底

        try
        {
            object r = _isScreenReady.Invoke(null, null);
            return r is bool b && b;
        }
        catch (Exception ex)
        {
            WarnOnce(ref _readyWarned, $"IsScreenReady() 调用失败：{ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }

    private static bool _readyWarned;

    /// <summary>
    /// 请求把窗口切到独占全屏。返回 false 表示「请求没发出去」，
    /// 注意这不等于「切成功了」——成功与否要事后读 Screen.fullScreenMode。
    /// </summary>
    internal static bool RequestExclusive(int width, int height, out string error)
    {
        error = null;

        if (_setResolution3 == null)
        {
            error = "SetResolution(int,int,FullScreenMode) 未解析到";
            return false;
        }

        try
        {
            _setResolution3.Invoke(null, new object[] { width, height, FullScreenMode.ExclusiveFullScreen });
            return true;
        }
        catch (TargetInvocationException tie)
        {
            error = $"{tie.InnerException?.GetType().Name ?? "TargetInvocationException"}: {tie.InnerException?.Message ?? tie.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>把当下能读到的显示状态打一遍，失败时尤其需要它。</summary>
    internal static void LogEnvironment()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[K1] ==== 启动时显示环境 ====");
        try
        {
            var cur = Screen.currentResolution;
            sb.AppendLine($"  Screen.width/height        : {Screen.width}x{Screen.height}");
            sb.AppendLine($"  Screen.currentResolution   : {cur.width}x{cur.height}");
            sb.AppendLine($"  Screen.fullScreenMode      : {Screen.fullScreenMode}");
            sb.AppendLine($"  Screen.fullScreen          : {Screen.fullScreen}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  读取 Screen 失败           : {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var main = UnityEngine.Display.main;
            sb.AppendLine($"  Display.main 物理像素      : {main.systemWidth}x{main.systemHeight}");
            sb.AppendLine($"  Display.displays.Length    : {UnityEngine.Display.displays.Length}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  读取 Display 失败          : {ex.GetType().Name}: {ex.Message}");
        }

        sb.AppendLine($"  官方自带等待时长 ApplyDelay: {DescribeApplyDelay()}");
        sb.AppendLine("[K1] ========================");

        Info(sb.ToString().TrimEnd());
    }

    /// <summary>失败现场快照——每次尝试结果都带上它，方便对照日志。</summary>
    internal static string Snapshot()
    {
        try
        {
            var cur = Screen.currentResolution;
            return $"mode={Screen.fullScreenMode}, fullScreen={Screen.fullScreen}, "
                 + $"size={Screen.width}x{Screen.height}, currentResolution={cur.width}x{cur.height}";
        }
        catch (Exception ex)
        {
            return $"读取 Screen 失败（{ex.GetType().Name}: {ex.Message}）";
        }
    }

    private static string Yn(bool b) => b ? "OK" : "缺失";

    private static void Info(string msg) => K1FullscreenFix.Diagnostics.K1Log.Info(msg);

    private static void Warn(string msg) => K1FullscreenFix.Diagnostics.K1Log.Warn(msg);

    private static void WarnOnce(ref bool flag, string msg)
    {
        if (flag) return;
        flag = true;
        Warn(msg);
    }
}
