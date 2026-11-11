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

		public readonly ConfigEntry<bool> KeepStoredExclusive;

		public readonly ConfigEntry<bool> Verbose;

		public readonly ConfigEntry<int> ConfigRevision;

		public ModConfig(ConfigFile cfg)
		{
			Enabled = cfg.Bind("General", "Enabled", true, "总开关。关闭后本插件什么都不做。");

			ReadOnlyMode = cfg.Bind("General", "ReadOnlyMode", false,
				"只读记录模式。默认【关闭】。\n\n" +
				"开启时：插件只读取并记录启动瞬间的状态，不写注册表、不改设置、不注入任何 Harmony 补丁。\n" +
				"关闭时：正常执行『守住全屏设置』——每次设置落盘前，把 FullScreenMode 改回你选的全屏。\n\n" +
				"机制：游戏在 ApplyDisplaySettings 收尾时，会把『实际变成了什么模式』写回设置。\n" +
				"独占全屏建立失败 → Unity 回退成无边框 → 这个『无边框』就被存进 Graphics.ini，\n" +
				"下次启动读到它，于是永远无边框。本插件在写盘那一刻把这一项改回来。");

			PinNativeResolutionInPlayerPrefs = cfg.Bind("Fix", "PinNativeResolutionInPlayerPrefs", false,
				"把 HKCU\\Software\\Battlestate Games\\EscapeFromTarkov 里记录的分辨率改成原生分辨率。\n" +
				"默认【关闭】。\n\n" +
				"注意：这个功能从未被证明有效。实测 2026-11-10 14:47（存档 123）在\n" +
				"请求分辨率 1680x1050 ≠ 桌面 2880x1800 的情况下依然成功进入独占全屏，\n" +
				"所以『分辨率必须等于桌面』这个旧前提是错的。只读模式下本项被忽略。");

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
			if (ConfigRevision.Value >= 4)
			{
				return;
			}
			// v2.1.0：只读记录模式（默认 true）改成按需开启（默认 false），
			// 因为「守住全屏设置」已经是被实证过的真修复，不该默认关着。
			if (ReadOnlyMode.Value)
			{
				ReadOnlyMode.Value = false;
			}
			ConfigRevision.Value = 4;
			if (Plugin.Log != null)
			{
				Plugin.Log.LogInfo("[全屏修复] 配置已迁移到 v2.1.0（默认启用守住全屏设置）。");
			}
		}
	}
}
