using System;
using System.Text;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using UnityEngine;

namespace FullscreenFix
{
	internal static class DisplayEnvironment
	{
		private static bool _probed;

		internal static int RewriteCount;

		internal static int NativeWidth { get; private set; } = 2880;

		internal static int NativeHeight { get; private set; } = 1800;

		internal static int NativeRefresh { get; private set; } = 0;

		internal static void Probe()
		{
			if (_probed)
			{
				return;
			}
			_probed = true;
			try
			{
				Display main = Display.main;
				int systemWidth = main.systemWidth;
				int systemHeight = main.systemHeight;
				if (systemWidth > 0 && systemHeight > 0)
				{
					NativeWidth = systemWidth;
					NativeHeight = systemHeight;
				}
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				log.LogWarning($"[全屏修复] 读取 Display.main 失败，沿用默认 {NativeWidth}x{NativeHeight}：{ex.Message}");
			}
			try
			{
				Resolution currentResolution = Screen.currentResolution;
				NativeRefresh = (int)((currentResolution.refreshRateRatio.numerator != 0) ? (currentResolution.refreshRateRatio.numerator / Math.Max(1u, currentResolution.refreshRateRatio.denominator)) : 0);
			}
			catch
			{
			}
		}

		internal static void LogSummary()
		{
			Probe();
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("[全屏修复] ==== 显示环境 ====");
			try
			{
				Resolution currentResolution = Screen.currentResolution;
				stringBuilder.AppendLine($"  当前分辨率      : {Screen.width}x{Screen.height}");
				stringBuilder.AppendLine($"  桌面分辨率      : {currentResolution.width}x{currentResolution.height}");
				stringBuilder.AppendLine($"  Screen.fullScreenMode : {Screen.fullScreenMode} (0=Exclusive 1=Borderless 2=Maximized 3=Windowed)");
				stringBuilder.AppendLine($"  Screen.fullScreen     : {Screen.fullScreen}");
			}
			catch (Exception ex)
			{
				stringBuilder.AppendLine($"  读取 Screen 失败 : {ex.Message}");
			}
			stringBuilder.AppendLine($"  判定原生分辨率  : {NativeWidth}x{NativeHeight} (刷新 {NativeRefresh}Hz)");
			stringBuilder.AppendLine("[全屏修复] ==================");
			Plugin.Log.LogInfo(stringBuilder.ToString());
		}

		internal static bool IsNative(int w, int h)
		{
			if (w == NativeWidth)
			{
				return h == NativeHeight;
			}
			return false;
		}
	}
}
