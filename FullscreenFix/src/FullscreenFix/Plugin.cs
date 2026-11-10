using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using FullscreenFix.Config;
using FullscreenFix.Patches;
using Il2CppInterop.Runtime.Injection;

namespace FullscreenFix
{
	[BepInPlugin("dsh.FullscreenFix", "全屏修复", "2.0.0-readonly")]
	public sealed class Plugin : BasePlugin
	{
		public const string PluginGuid = "dsh.FullscreenFix";

		public const string PluginName = "全屏修复";

		public const string PluginVersion = "2.0.0-readonly";

		internal new static ManualLogSource Log;

		internal static Plugin Instance;

		internal static ModConfig Configs;

		public override void Load()
		{
			Instance = this;
			Log = base.Log;
			Configs = new ModConfig(base.Config);
			Log.LogInfo("[全屏修复] v" + PluginVersion + " 载入中（只读记录模式）…");
			try
			{
				// 第一件事：把启动那一瞬的所有状态原样落盘。不写任何东西。
				StartupSnapshot.Dump("load");

				DisplayEnvironment.LogSummary();
				DisplayDiagnostics.Run();

				if (Configs.ReadOnlyMode.Value)
				{
					Log.LogInfo("[全屏修复] 只读模式 = true：跳过 PlayerPrefs 改写、跳过全部 Harmony 补丁。本插件本次运行不会改变任何设置。");
				}
				else
				{
					Log.LogWarning("[全屏修复] 只读模式 = false：将执行 PlayerPrefs 改写与补丁注入。");
					PlayerPrefsPinner.Apply();
					PatchManager.Apply();
				}

				ClassInjector.RegisterTypeInIl2Cpp<FullscreenWatchdog>();
				base.AddComponent<FullscreenWatchdog>();
				Log.LogInfo("[全屏修复] 完成（只读记录器）。快照目录: D:\\EFT-WORKS\\full\\work\\snapshots");
			}
			catch (Exception ex)
			{
				Log.LogError("[全屏修复] 初始化失败：" + ex.GetType().Name + ": " + ex.Message);
				Log.LogError(ex.ToString());
			}
		}
	}
}
