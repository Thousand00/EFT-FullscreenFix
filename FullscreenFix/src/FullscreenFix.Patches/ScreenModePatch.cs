using System;
using System.Reflection;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using FullscreenFix.Config;
using HarmonyLib;
using UnityEngine;

namespace FullscreenFix.Patches
{
	internal static class ScreenModePatch
	{
		internal static void Apply(Harmony harmony)
		{
			MethodInfo methodInfo = AccessTools.PropertySetter(typeof(Screen), "fullScreenMode");
			if (methodInfo == null)
			{
				Plugin.Log.LogWarning("[全屏修复] 找不到 UnityEngine.Screen.fullScreenMode 的 setter，跳过引擎层兜底。");
				return;
			}
			try
			{
				harmony.Patch(methodInfo, new HarmonyMethod(typeof(ScreenModePatch), "Prefix"));
				Plugin.Log.LogInfo("[全屏修复] 已注入 UnityEngine.Screen.fullScreenMode setter（托管侧兜底）");
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				log.LogWarning($"[全屏修复] 注入 Screen.fullScreenMode setter 失败：{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void Prefix(ref FullScreenMode value)
		{
			ModConfig configs = Plugin.Configs;
			if (configs == null || !configs.Enabled.Value || !configs.ForceBorderless.Value || value != FullScreenMode.ExclusiveFullScreen)
			{
				return;
			}
			value = FullScreenMode.FullScreenWindow;
			DisplayEnvironment.RewriteCount++;
			if (configs.Verbose.Value)
			{
				ManualLogSource log = Plugin.Log;
				log.LogInfo($"[全屏修复] Screen.fullScreenMode: ExclusiveFullScreen -> FullScreenWindow  [第 {DisplayEnvironment.RewriteCount} 次]");
			}
		}
	}
}
