using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using K1FullscreenFix.Diagnostics;
using K1FullscreenFix.Runtime;

namespace K1FullscreenFix.Hooks;

/// <summary>
/// 挂在「模式选择」界面（5.0 BE 模仿线上的 PVE / PVP / PVPS 选择页）上。
///
/// 为什么必须知道这个界面存在（2026-11-10 23:03 实测）：
///   5.0 BE 的启动流程是
///     登录 → 设备校验 → 侧别选择 → **模式选择（PVE/PVP/PVPS）** → 载入存档 → 主菜单。
///   那个选择页停留多久完全由玩家决定，而它恰好卡在「引擎正在切场景」的中间。
///   玩家在那页上多停一会儿，v0.3.1 那个「等 45s 超时就发请求」的兜底计时就会
///   正好落在里面，于是请求被接受、0.58s 后被回退，白试一次。
///
///   日志铁证（`K1Logs\K1FullscreenFix.log`）：
///     23:03:49.638  等待信号超时，按定时判断：等 1.0s 后发请求。   ← 45s 兜底，此刻人还在选择页
///     23:03:50.670  [观察] FullScreenWindow → ExclusiveFullScreen
///     23:03:51.247  [观察] ExclusiveFullScreen → FullScreenWindow  ← 只撑了 0.58s
///     23:03:56.834  ✘ 第 1 次尝试未成功
///     23:04:00.839  第 2/10 次尝试                                  ← 已经选完模式、进了主菜单流程
///     23:04:04.262  ✔ 第 2 次尝试成功：已连续保持 3.0s
///     23:04:07.884  检测到主菜单已显示                               ← 真正的信号比第 1 次晚 17s
///
/// 本类的三个挂点（全部用字符串反射，避免 Il2CppInterop 编译期不可达）：
///   1. EFT.UI.CharacterSelectionScreen.Show(...)   postfix → 「选择界面开了，别动」
///   2. EFT.UI.CharacterSelectionScreen.Close()     postfix → 「选择界面关了」
///   3. CharacterSelectionScreen.CharacterSelectionScreenController
///        .SubmitAsync(EGameMode, CharacterSelectionProfileData) / .Submit(...)
///                                                    postfix → 「玩家已选定模式」
///
/// 所有挂点都是可选的：挂不上只写日志，不影响 MenuHook 与定时兜底。
/// </summary>
internal static class ScreenHooks
{
    private const string SelectionScreenTypeName = "EFT.UI.CharacterSelectionScreen";
    private const string SelectionControllerTypeName =
        "EFT.UI.CharacterSelectionScreen+CharacterSelectionScreenController";

    private static bool _applied;

    internal static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _applied = true;

        try
        {
            var screenPostfix = Postfix(nameof(SelectionScreenPostfix));
            var closePostfix = Postfix(nameof(SelectionScreenClosePostfix));
            var chosenPostfix = Postfix(nameof(ModeChosenPostfix));

            int screenOk = HookByName(harmony, SelectionScreenTypeName, "Show", screenPostfix, "选择界面打开");
            int closeOk = HookByName(harmony, SelectionScreenTypeName, "Close", closePostfix, "选择界面关闭");
            int chosenOk = HookByName(harmony, SelectionControllerTypeName, "SubmitAsync", chosenPostfix, "已选定模式")
                         + HookByName(harmony, SelectionControllerTypeName, "Submit", chosenPostfix, "已选定模式");

            if (screenOk > 0 || closeOk > 0 || chosenOk > 0)
            {
                K1Log.Info(
                    "模式选择界面感知已就绪："
                    + $"打开={screenOk}，关闭={closeOk}，选定模式={chosenOk}。"
                    + " 停在选择界面期间不会发请求。");
            }
            else
            {
                K1Log.Warn(
                    "没能挂钩模式选择界面 —— 不影响主菜单触发与定时兜底，"
                    + "只是玩家在选择页停留时可能多发一次注定失败的请求。");
            }
        }
        catch (Exception ex)
        {
            K1Log.Warn("挂钩模式选择界面失败（不影响其它逻辑）", ex);
        }
    }

    /// <summary>
    /// 在指定类型上挂「所有指定名字的方法」。返回成功挂上的数量。
    /// 用 DeclaredOnly + 遍历重载，避免 AccessTools.Method 在多重载时抛
    /// AmbiguousMatchException（v0.2.0 就栽在这上面）。
    /// </summary>
    private static int HookByName(Harmony harmony, string typeName, string methodName,
                                  HarmonyMethod postfix, string what)
    {
        Type type = ResolveType(typeName);

        if (type == null)
        {
            K1Log.Detail($"[钩子] 没找到类型 {typeName}，跳过「{what}」。");
            return 0;
        }

        int ok = 0;

        MethodInfo[] all;

        try
        {
            all = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                  | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
        catch (Exception ex)
        {
            K1Log.Detail($"[钩子] 枚举 {typeName} 方法失败：{ex.GetType().Name}: {ex.Message}");
            return 0;
        }

        foreach (var m in all)
        {
            if (m == null) continue;
            if (m.IsAbstract || m.IsStatic) continue;
            if (m.Name.IndexOf('<') >= 0) continue;      // 编译器生成的 lambda / 状态机
            if (m.Name != methodName) continue;

            try
            {
                harmony.Patch(m, postfix: postfix);
                ok++;

                K1Log.Detail(
                    $"[钩子] 已挂 {typeName}.{m.Name}("
                    + string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name))
                    + $") → {what}");
            }
            catch (Exception ex)
            {
                K1Log.Detail(
                    $"[钩子] 挂 {typeName}.{m.Name} 失败：{ex.GetType().Name}: {ex.Message}");
            }
        }

        return ok;
    }

    private static HarmonyMethod Postfix(string name)
    {
        return new HarmonyMethod(typeof(ScreenHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
    }

    private static Type ResolveType(string fullName)
    {
        try
        {
            var t = Type.GetType(fullName + ", Assembly-CSharp");
            if (t != null) return t;
        }
        catch
        {
            // 继续用下面的遍历兜底
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            catch
            {
                // 单个程序集失败不影响继续找
            }
        }

        return null;
    }

    // ------------------------------------------------------------ postfix

    private static void SelectionScreenPostfix()
    {
        try
        {
            FullscreenRetrier.SignalSelectionState(true);
        }
        catch (Exception ex)
        {
            K1Log.Detail($"[钩子] SelectionScreenPostfix 异常：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SelectionScreenClosePostfix()
    {
        try
        {
            FullscreenRetrier.SignalSelectionState(false);
        }
        catch (Exception ex)
        {
            K1Log.Detail($"[钩子] SelectionScreenClosePostfix 异常：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ModeChosenPostfix()
    {
        try
        {
            FullscreenRetrier.SignalModeChosen();
        }
        catch (Exception ex)
        {
            K1Log.Detail($"[钩子] ModeChosenPostfix 异常：{ex.GetType().Name}: {ex.Message}");
        }
    }
}
