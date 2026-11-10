using System;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using FullscreenFix.Config;
using UnityEngine;

namespace FullscreenFix
{
	public sealed class FullscreenWatchdog : MonoBehaviour
	{
		private float _nextCheck;

		private int _fixCount;

		private int _samples;

		private FullScreenMode _lastSeen;

		private bool _hasSeen;

		private bool _loggedFirstFailure;

		public FullscreenWatchdog(IntPtr ptr)
			: base(ptr)
		{
		}

		private void Update()
		{
			ModConfig configs = Plugin.Configs;
			if (configs == null || !configs.Enabled.Value)
			{
				return;
			}
			float unscaledTime = Time.unscaledTime;
			if (unscaledTime < _nextCheck)
			{
				return;
			}
			_nextCheck = unscaledTime + 0.5f;
			try
			{
				FullScreenMode fullScreenMode = Screen.fullScreenMode;
				_samples++;
				bool flag = !_hasSeen || fullScreenMode != _lastSeen;
				if (flag)
				{
					_hasSeen = true;
					FullScreenMode lastSeen = _lastSeen;
					_lastSeen = fullScreenMode;
					if (configs.Verbose.Value)
					{
						ManualLogSource log = Plugin.Log;
						log.LogInfo($"[全屏修复] 显示模式变化：{(flag ? lastSeen.ToString() : "-")} -> {fullScreenMode}  (第 {_samples} 次采样, {unscaledTime:F1}s)");
					}
					if (fullScreenMode != FullScreenMode.ExclusiveFullScreen && !_loggedFirstFailure)
					{
						_loggedFirstFailure = true;
						Plugin.Log.LogWarning("[全屏修复] 运行时已不是独占全屏。若你选的是『全屏』而画面变成无边框，\n  原因就是本机无法建立 DXGI 独占全屏（详见上面的实际显示模式日志）。");
					}
				}
				if (configs.ForceBorderless.Value && fullScreenMode == FullScreenMode.ExclusiveFullScreen)
				{
					Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
					_fixCount++;
					ManualLogSource log2 = Plugin.Log;
					log2.LogWarning($"[全屏修复] 已按 ForceBorderless 把独占全屏切回无边框（累计 {_fixCount} 次）");
				}
			}
			catch (Exception ex)
			{
				if (!_loggedFirstFailure)
				{
					_loggedFirstFailure = true;
					ManualLogSource log3 = Plugin.Log;
					log3.LogWarning($"[全屏修复] 观察者采样失败：{ex.Message}");
				}
			}
		}
	}
}
