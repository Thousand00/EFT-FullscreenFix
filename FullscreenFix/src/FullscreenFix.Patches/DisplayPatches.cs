using System;
using System.Reflection;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using FullscreenFix.Config;
using HarmonyLib;
using UnityEngine;

namespace FullscreenFix.Patches
{
	internal static class DisplayPatches
	{
		private const string HelperTypeName = "EFT.Settings.Graphics.EFTDisplayHelper";

		private const string SetResolutionName = "SetResolution";

		private static bool _reportedFirstResult;

		internal static void Apply(Harmony harmony)
		{
			PatchThreeArg(harmony);
			PatchTwoArg(harmony);
		}

		private static void PatchThreeArg(Harmony harmony)
		{
			Type type = AccessTools.TypeByName("EFT.Settings.Graphics.EFTDisplayHelper");
			if (type == null)
			{
				ManualLogSource log = Plugin.Log;
				log.LogWarning($"[全屏修复] 找不到类型 {"EFT.Settings.Graphics.EFTDisplayHelper"}，跳过三参补丁。");
				return;
			}
			MethodInfo methodInfo = AccessTools.Method(type, "SetResolution", new Type[3]
			{
				typeof(int),
				typeof(int),
				typeof(FullScreenMode)
			});
			if (methodInfo == null)
			{
				ManualLogSource log2 = Plugin.Log;
				log2.LogWarning($"[全屏修复] 找不到 {"EFT.Settings.Graphics.EFTDisplayHelper"}.{"SetResolution"}(int,int,FullScreenMode)，跳过。");
				return;
			}
			try
			{
				harmony.Patch(methodInfo, new HarmonyMethod(typeof(DisplayPatches), "PrefixThreeArg"), new HarmonyMethod(typeof(DisplayPatches), "PostfixThreeArg"));
				ManualLogSource log3 = Plugin.Log;
				log3.LogInfo($"[全屏修复] 已注入 {"EFT.Settings.Graphics.EFTDisplayHelper"}.{"SetResolution"}(int,int,FullScreenMode)（观测 + 可选改写）");
			}
			catch (Exception ex)
			{
				ManualLogSource log4 = Plugin.Log;
				log4.LogWarning($"[全屏修复] 注入三参 SetResolution 失败：{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void PatchTwoArg(Harmony harmony)
		{
			Type type = AccessTools.TypeByName("EFT.Settings.Graphics.EFTDisplayHelper");
			if (type == null)
			{
				return;
			}
			MethodInfo methodInfo = AccessTools.Method(type, "SetResolution", new Type[2]
			{
				typeof(int),
				typeof(int)
			});
			if (methodInfo == null)
			{
				ManualLogSource log = Plugin.Log;
				log.LogWarning($"[全屏修复] 找不到 {"EFT.Settings.Graphics.EFTDisplayHelper"}.{"SetResolution"}(int,int)，跳过。");
				return;
			}
			try
			{
				harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(DisplayPatches), "PostfixTwoArg"));
				ManualLogSource log2 = Plugin.Log;
				log2.LogInfo($"[全屏修复] 已注入 {"EFT.Settings.Graphics.EFTDisplayHelper"}.{"SetResolution"}(int,int)（观测）");
			}
			catch (Exception ex)
			{
				ManualLogSource log3 = Plugin.Log;
				log3.LogWarning($"[全屏修复] 注入两参 SetResolution 失败：{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void PrefixThreeArg(ref int width, ref int height, ref FullScreenMode mode)
		{
			ModConfig configs = Plugin.Configs;
			if (configs == null || !configs.Enabled.Value)
			{
				return;
			}
			int t = width;
			int t2 = height;
			FullScreenMode t3 = mode;
			bool flag = false;
			if (configs.ForceBorderless.Value && mode == FullScreenMode.ExclusiveFullScreen)
			{
				mode = FullScreenMode.FullScreenWindow;
				flag = true;
			}
			if (configs.ForceNativeResolution.Value)
			{
				DisplayEnvironment.Probe();
				if (!DisplayEnvironment.IsNative(width, height))
				{
					width = DisplayEnvironment.NativeWidth;
					height = DisplayEnvironment.NativeHeight;
					flag = true;
				}
			}
			if (!flag)
			{
				if (configs.Verbose.Value)
				{
					ManualLogSource log = Plugin.Log;
					log.LogInfo($"[全屏修复] 游戏请求 SetResolution({t}x{t2}, {t3}) —— 原样放行");
				}
				return;
			}
			DisplayEnvironment.RewriteCount++;
			if (configs.Verbose.Value)
			{
				ManualLogSource log2 = Plugin.Log;
				log2.LogInfo($"[全屏修复] SetResolution({t}x{t2}, {t3}) -> ({width}x{height}, {mode})  [第 {DisplayEnvironment.RewriteCount} 次]");
			}
		}

		private static void PostfixThreeArg(int width, int height, FullScreenMode mode)
		{
			ReportActualMode($"SetResolution({width}x{height}, {mode})");
			EnforceNotExclusive("三参 SetResolution 之后");
		}

		private static void PostfixTwoArg(int width, int height)
		{
			ReportActualMode($"SetResolution({width}x{height}, 沿用上次模式)");
			EnforceNotExclusive("两参 SetResolution 之后");
		}

		private static void ReportActualMode(string what)
		{
			ModConfig configs = Plugin.Configs;
			if (configs == null || !configs.Verbose.Value)
			{
				return;
			}
			try
			{
				FullScreenMode fullScreenMode = Screen.fullScreenMode;
				string t = fullScreenMode switch
				{
					FullScreenMode.ExclusiveFullScreen => "✔ 真·独占全屏（成功）", 
					FullScreenMode.FullScreenWindow => "✘ 无边框全屏（独占全屏失败后 Unity 的回退结果）", 
					FullScreenMode.MaximizedWindow => "✘ 最大化窗口", 
					FullScreenMode.Windowed => "✘ 窗口化", 
					_ => "（未知）", 
				};
				ManualLogSource log = Plugin.Log;
				log.LogInfo($"[全屏修复] {what} → 实际显示模式 = {fullScreenMode} {t}");
				if (!_reportedFirstResult && fullScreenMode != FullScreenMode.ExclusiveFullScreen)
				{
					_reportedFirstResult = true;
					Plugin.Log.LogWarning("[全屏修复] 首次结果：游戏请求的是独占全屏，但运行时落到了非独占模式。\n  原因几乎总是同一个：调用 SetFullscreenState 的那一瞬间，本进程还没拿到前台。\n  DXGI 此时返回 0x887A0022，Unity 随即回退成无边框，并把『无边框』当成实际模式写回。\n  已实测排除的解释：驱动不支持、请求分辨率与桌面不一致、虚拟显示器/串流软件干扰、\n  多显示器、HDR、刷新率、DPI 缩放——以上都不是原因。\n  能做的：1) 确认 Fix/PinFullscreenKeyInPlayerPrefs 开着，让下次启动重新去申请独占全屏；\n  2) 进入主菜单后按热键手动补一次（运行中申请是能成功的，K 系列已验证）。");
				}
			}
			catch (Exception ex)
			{
				ManualLogSource log2 = Plugin.Log;
				log2.LogWarning($"[全屏修复] 读取实际显示模式失败：{ex.Message}");
			}
		}

		private static void EnforceNotExclusive(string where)
		{
			ModConfig configs = Plugin.Configs;
			if (configs == null || !configs.Enabled.Value || !configs.ForceBorderless.Value)
			{
				return;
			}
			try
			{
				if (Screen.fullScreenMode != FullScreenMode.ExclusiveFullScreen)
				{
					return;
				}
				Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
				DisplayEnvironment.RewriteCount++;
				if (configs.Verbose.Value)
				{
					ManualLogSource log = Plugin.Log;
					log.LogInfo($"[全屏修复] {where}运行时仍为独占全屏，已改回无边框全屏。");
				}
			}
			catch (Exception ex)
			{
				ManualLogSource log2 = Plugin.Log;
				log2.LogWarning($"[全屏修复] {where}修正失败：{ex.Message}");
			}
		}
	}
}
