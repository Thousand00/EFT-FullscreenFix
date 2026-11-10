using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using K1FullscreenFix.Diagnostics;
using K1FullscreenFix.Runtime;

namespace K1FullscreenFix.Hooks;

/// <summary>
/// 挂在「主菜单显示出来」的那一刻，作为「现在切独占全屏安全了」的信号。
///
/// 为什么需要它：靠固定秒数猜「引擎那几轮失败-重试-回退跑完没有」不可靠，
/// 机器快慢、读图速度、装了多少 mod 都会影响。而「主菜单已经显示」是确定事件。
///
/// v0.3.0 修正：v0.2.0 用 <c>AccessTools.Method(type, "Show")</c> 直接报
///   <c>AmbiguousMatchException: Ambiguous match in Harmony patch for EFT.UI.MenuScreen:Show</c>，
///   因为 MenuScreen 上确实有两个 Show 重载（已用 ilspycmd 核对）：
///     Show(MainMenuBaseScreenController)                                          // public virtual, CallerCount(0)
///     Show(MatchmakerPlayersController, ExpansionsPlayerInfo, GameModeDescriptor,
///          Profile, SeasonalRewardController)                                     // private,        CallerCount(1)
///   现在改成「把所有名叫 Show / Open 的重载全部挂上」，任一触发即视为菜单已出现——
///   这样不需要猜哪个才是实际入口，游戏改版换入口也仍然有效。
/// </summary>
internal static class MenuHook
{
    private const string TypeName = "EFT.UI.MenuScreen";

    private static bool _applied;

    internal static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _applied = true;

        try
        {
            Type type = ResolveType();

            if (type == null)
            {
                K1Log.Warn(
                    $"没找到 {TypeName}，跳过「主菜单出现即触发」。"
                    + " 定时逻辑仍会兜底，只是触发时机不那么准。");
                return;
            }

            var targets = CollectTargets(type);

            if (targets.Count == 0)
            {
                K1Log.Warn($"{TypeName} 上没找到可挂钩的显示方法，跳过「主菜单出现即触发」。");
                return;
            }

            var postfix = new HarmonyMethod(
                typeof(MenuHook).GetMethod(nameof(ShowPostfix), BindingFlags.Static | BindingFlags.NonPublic));

            int ok = 0;

            foreach (var m in targets)
            {
                try
                {
                    harmony.Patch(m, postfix: postfix);
                    ok++;

                    K1Log.Detail(
                        $"[钩子] 已挂 {TypeName}.{m.Name}("
                        + string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name))
                        + ")");
                }
                catch (Exception ex)
                {
                    K1Log.Warn(
                        $"挂钩 {m.Name} 失败（继续挂其它重载）：{ex.GetType().Name}: {ex.Message}");
                }
            }

            if (ok > 0)
            {
                K1Log.Info($"已挂钩 {TypeName} 的 {ok} 个显示入口：主菜单一出现就触发一次尝试。");
            }
            else
            {
                K1Log.Warn($"{TypeName} 的所有候选方法都挂不上，退化为定时判断。");
            }
        }
        catch (Exception ex)
        {
            K1Log.Warn($"挂钩主菜单失败（不影响定时逻辑）", ex);
        }
    }

    private static Type ResolveType()
    {
        try
        {
            var t = Type.GetType(TypeName + ", Assembly-CSharp");
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
                var t = asm.GetType(TypeName, false);
                if (t != null) return t;
            }
            catch
            {
                // 单个程序集失败不影响继续找
            }
        }

        return null;
    }

    /// <summary>
    /// 收集所有可作为「主菜单已显示」信号的方法：
    /// 名字是 Show / Open 的公开或私有实例方法（含 virtual/override）。
    /// 明确排除属性 getter 与编译器生成的方法（名字里带 '&lt;'）。
    /// </summary>
    private static List<MethodInfo> CollectTargets(Type type)
    {
        var result = new List<MethodInfo>();

        MethodInfo[] all;

        try
        {
            all = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                  | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
        catch (Exception ex)
        {
            K1Log.Warn($"枚举 {TypeName} 方法失败：{ex.GetType().Name}: {ex.Message}");
            return result;
        }

        foreach (var m in all)
        {
            if (m == null) continue;
            if (m.IsAbstract || m.IsStatic) continue;
            if (m.Name.IndexOf('<') >= 0) continue;   // 编译器生成
            if (m.Name != "Show" && m.Name != "Open") continue;

            result.Add(m);
        }

        return result;
    }

    private static void ShowPostfix()
    {
        try
        {
            if (FullscreenRetrier.MenuSeen) return;

            K1Log.Info("检测到主菜单已显示 —— 这是「可以安全切独占全屏」的确切信号。");
            FullscreenRetrier.SignalMenuReady();
        }
        catch (Exception ex)
        {
            K1Log.Warn($"通知重试器失败：{ex.GetType().Name}: {ex.Message}");
        }
    }
}
