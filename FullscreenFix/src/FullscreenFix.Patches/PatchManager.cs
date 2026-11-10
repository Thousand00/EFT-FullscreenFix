using HarmonyLib;

namespace FullscreenFix.Patches
{
	internal static class PatchManager
	{
		private const string HarmonyId = "dsh.FullscreenFix";

		private static bool _applied;

		internal static void Apply()
		{
			if (!_applied)
			{
				_applied = true;
				Harmony harmony = new Harmony("dsh.FullscreenFix");
				DisplayPatches.Apply(harmony);
				ScreenModePatch.Apply(harmony);
				StoredSettingsPatch.Apply(harmony);
				Plugin.Log.LogInfo("[全屏修复] 补丁注册结束。");
			}
		}
	}
}
