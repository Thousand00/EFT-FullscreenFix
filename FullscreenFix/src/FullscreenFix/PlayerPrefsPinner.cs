using System;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using FullscreenFix.Config;
using Microsoft.Win32;

namespace FullscreenFix
{
	internal static class PlayerPrefsPinner
	{
		private const string RegPath = "Software\\Battlestate Games\\EscapeFromTarkov";

		private static readonly string[] WidthPrefixes = new string[2] { "Screenmanager Resolution Width_h", "Screenmanager Resolution Window Width_h" };

		private static readonly string[] HeightPrefixes = new string[2] { "Screenmanager Resolution Height_h", "Screenmanager Resolution Window Height_h" };

		internal static void Apply()
		{
			ModConfig configs = Plugin.Configs;
			if (configs == null || !configs.Enabled.Value || !configs.PinNativeResolutionInPlayerPrefs.Value)
			{
				return;
			}
			DisplayEnvironment.Probe();
			int nativeWidth = DisplayEnvironment.NativeWidth;
			int nativeHeight = DisplayEnvironment.NativeHeight;
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Battlestate Games\\EscapeFromTarkov", writable: true);
				if (registryKey == null)
				{
					ManualLogSource log = Plugin.Log;
					log.LogWarning($"[全屏修复] 找不到注册表项 HKCU\\{"Software\\Battlestate Games\\EscapeFromTarkov"}，跳过分辨率钉定。");
					return;
				}
				int num = PinMatching(registryKey, WidthPrefixes, nativeWidth);
				int num2 = PinMatching(registryKey, HeightPrefixes, nativeHeight);
				if (num + num2 == 0)
				{
					ManualLogSource log2 = Plugin.Log;
					log2.LogInfo($"[全屏修复] PlayerPrefs 里的分辨率已经是 {nativeWidth}x{nativeHeight}，无需改动。");
				}
				else
				{
					Plugin.Log.LogWarning($"[全屏修复] 已把 PlayerPrefs 里的分辨率改成原生 {nativeWidth}x{nativeHeight}（改了 {num + num2} 个键）。\n" + "  → 这个改动【下次启动才生效】，请退出游戏后重新进入。\n  → 下次启动时引擎请求的独占全屏不再需要切换显示模式，这是真·全屏唯一可能成立的前提。\n  → 想撤销：把 Fix/PinNativeResolutionInPlayerPrefs 关掉，然后在游戏设置里重新选一次分辨率。");
				}
			}
			catch (Exception ex)
			{
				ManualLogSource log3 = Plugin.Log;
				log3.LogWarning($"[全屏修复] 修改 PlayerPrefs 分辨率失败：{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static int PinMatching(RegistryKey key, string[] prefixes, int value)
		{
			int num = 0;
			string[] valueNames = key.GetValueNames();
			foreach (string text in valueNames)
			{
				foreach (string value2 in prefixes)
				{
					if (!text.StartsWith(value2, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					object value3 = key.GetValue(text);
					if (value3 is int num2 && num2 == value)
					{
						break;
					}
					key.SetValue(text, value, RegistryValueKind.DWord);
					num++;
					if (Plugin.Configs != null && Plugin.Configs.Verbose.Value)
					{
						ManualLogSource log = Plugin.Log;
						log.LogInfo($"[全屏修复] PlayerPrefs `{text}`: {value3} -> {value}");
					}
					break;
				}
			}
			return num;
		}
	}
}
