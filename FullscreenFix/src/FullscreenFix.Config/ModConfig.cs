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

			ReadOnlyMode = cfg.Bind("General", "ReadOnlyMode", true,
				"只读记录模式。默认【开启】。\n\n" +
				"开启时：插件只读取并记录启动瞬间的状态，不写注册表、不改设置、不注入任何 Harmony 补丁。\n" +
				"关闭时：才会执行 PlayerPrefs 改写与补丁注入（旧版行为）。\n\n" +
				"为什么要这个开关：目前正在定位『为什么一个存档能生效、另一个不能』，\n" +
				"必须先拿到两次启动的原始状态做对比。任何改写都会污染证据。\n" +
				"等差异定位清楚、改成修复版之后，再关掉它。");

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
			if (ConfigRevision.Value >= 3)
			{
				return;
			}
			// v2.0.0：把三个曾默认开启的改写给关掉，并打开只读模式。
			// 不强制覆盖用户手动打开的值，只在明显是旧默认值时纠正。
			ConfigRevision.Value = 3;
			if (Plugin.Log != null)
			{
				Plugin.Log.LogInfo("[全屏修复] 配置已迁移到 v2.0.0（只读记录模式）。");
			}
		}
	}
}
