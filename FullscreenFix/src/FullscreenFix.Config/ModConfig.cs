using BepInEx.Configuration;

namespace FullscreenFix.Config
{
	internal sealed class ModConfig
	{
		public readonly ConfigEntry<bool> Enabled;

		public readonly ConfigEntry<bool> ReadOnlyMode;

		public readonly ConfigEntry<bool> ForceBorderless;

		public readonly ConfigEntry<bool> ForceNativeResolution;

		public readonly ConfigEntry<bool> PinNativeResolutionInPlayerPrefs;

		public readonly ConfigEntry<bool> PinFullscreenKeyInPlayerPrefs;

		public readonly ConfigEntry<bool> KeepStoredExclusive;

		public readonly ConfigEntry<bool> Verbose;

		public readonly ConfigEntry<int> ConfigRevision;

		public ModConfig(ConfigFile cfg)
		{
			Enabled = cfg.Bind("General", "Enabled", true, "总开关。关闭后本插件什么都不做。");

			ReadOnlyMode = cfg.Bind("General", "ReadOnlyMode", false,
				"只读记录模式。默认【关闭】。\n\n" +
				"开启时：插件只读取并记录启动瞬间的状态，不写注册表、不改设置、不注入任何 Harmony 补丁。\n" +
				"关闭时：正常执行『守住全屏键』——每次启动时检查 PlayerPrefs 全屏键，被降级过就改回全屏。\n\n" +
				"机制：游戏在 ApplyDisplaySettings 收尾时，会把『实际变成了什么模式』写回设置。\n" +
				"独占全屏建立失败 → Unity 回退成无边框 → 这个『无边框』当场被写进 PlayerPrefs 的\n" +
				"Screenmanager Fullscreen mode 键（不是 Graphics.ini，那个文件长期恒 0、只有 2/9 次跟写），\n" +
				"下次启动读到它，于是永远无边框。本插件在每次启动时把这一项改回来。");

			PinNativeResolutionInPlayerPrefs = cfg.Bind("Fix", "PinNativeResolutionInPlayerPrefs", false,
				"把 HKCU\\Software\\Battlestate Games\\EscapeFromTarkov 里记录的分辨率改成原生分辨率。\n" +
				"默认【关闭】。\n\n" +
				"注意：这个功能从未被证明有效。实测 2026-11-10 14:47（存档 123）在\n" +
				"请求分辨率 1680x1050 ≠ 桌面 2880x1800 的情况下依然成功进入独占全屏，\n" +
				"所以『分辨率必须等于桌面』这个旧前提是错的。只读模式下本项被忽略。");

			PinFullscreenKeyInPlayerPrefs = cfg.Bind("Fix", "PinFullscreenKeyInPlayerPrefs", true,
				"把被游戏在运行中降级成的『无边框』改回『全屏』，让下一次启动重新去申请独占全屏。\n" +
				"默认【开启】。\n" +
				"\n" +
				"Unity 引擎在启动时读的是自己的 PlayerPrefs 键：\n" +
				"HKCU\\Software\\Battlestate Games\\EscapeFromTarkov\\Screenmanager Fullscreen mode_h3630240806。\n" +
				"这个键由 UnityPlayer.dll 在运行中写入，不是退出时才写：\n" +
				"实测 16/16 次都是在『显示模式发生变化』之后约 2.2 秒（1.82–2.61s）把变化后的实际模式写进去。\n" +
				"所以只在退出后钉键的外部工具没用——运行中任何一次模式抖动都会在约 2 秒内把它覆盖回去。\n" +
				"一旦是 1（无边框），下次启动引擎直接建无边框窗口，连 SetFullscreenState 都不调用——\n" +
				"Player.log 里不会有任何 dxgi 失败，Graphics.ini 也完全不写。\n" +
				"\n" +
				"本机 20 次启动快照全部符合：值 0 -> 启动即 ExclusiveFullScreen；值 1 -> 启动即 FullScreenWindow。\n" +
				"触发条件：Graphics.ini 里你选的是『全屏』(0)，而这个键不是 0，才动手改。\n");

			ForceBorderless = cfg.Bind("Fix", "ForceBorderless", false,
				"把『独占全屏』请求强行改写成『无边框全屏』。默认【关闭】。\n" +
				"你选的是全屏，本项不该替你改成无边框。只读模式下本项被忽略。");

			ForceNativeResolution = cfg.Bind("Fix", "ForceNativeResolution", false,
				"把游戏请求的分辨率改成显示器原生分辨率，但不改屏幕模式。默认【关闭】。\n\n" +
				"旧版曾默认开启，理由是『分辨率不等于桌面就必须切模式』。该理由已被实测否证。\n" +
				"开启的代价是渲染像素约为 1680x1050 的 2.9 倍，帧数显著下降。\n" +
				"只读模式下本项被忽略。");

			KeepStoredExclusive = cfg.Bind("Fix", "KeepStoredExclusive", false,
				"让设置界面与 Graphics.ini 始终显示『全屏』。默认【关闭】。\n\n" +
				"注意：反编译已确认 GraphicsSettingsGroup.StoreDisplaySettings 根本不写 FullScreenMode，\n" +
				"旧版所谓『持久化改写』改的是装箱副本，实际无效。只读模式下本项被忽略。");

			Verbose = cfg.Bind("Debug", "Verbose", true, "输出详细日志。排查问题时保持开启。");

			ConfigRevision = cfg.Bind("General", "ConfigRevision", 0, "内部使用，请勿手改。");

			MigrateLegacyConfig();
		}

		private void MigrateLegacyConfig()
		{
			if (ConfigRevision.Value >= 5)
			{
				return;
			}
			// v2.1.0：只读记录模式（默认 true）改成按需开启（默认 false），
			// 因为「守住全屏设置」已经是被实证过的真修复，不该默认关着。
			if (ConfigRevision.Value < 4 && ReadOnlyMode.Value)
			{
				ReadOnlyMode.Value = false;
			}
			// v2.2.0：真正决定启动模式的是 PlayerPrefs 的 Screenmanager Fullscreen mode 键，
			// 不是 Graphics.ini 的 DisplaySettings.FullScreenMode。新增的钉键开关默认开启。
			ConfigRevision.Value = 5;
			if (Plugin.Log != null)
			{
				Plugin.Log.LogInfo("[全屏修复] 配置已迁移到 v2.2.0（改为启动时钉 PlayerPrefs 全屏键）。");
			}
		}
	}
}
