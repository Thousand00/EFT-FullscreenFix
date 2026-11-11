using System;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace FullscreenFix.Patches
{
	/// <summary>
	/// 守住「全屏模式」这个设置，不让它被运行时的降级结果覆盖。

	///
	/// 机制（反编译实证，GraphicsSettingsController._ApplyDisplaySettings_d__29.MoveNext）：
	///   0xC4347A  mov dword ptr [rdi + 0x24], eax     ; eax = Screen.fullScreenMode（实际值）
	/// 引擎在 ApplyDisplaySettings 收尾时，把「实际上是什么模式」写回返回值里的
	/// EftDisplaySettings.FullScreenMode 字段。于是：
	///   - 请求 ExclusiveFullScreen(0)
	///   - DXGI 建立独占失败，Unity 回退成 FullScreenWindow(1)
	///   - 上面那条指令把 1 写进设置
	///   - 这份设置被序列化进 Graphics.ini 的 "DisplaySettings": { "FullScreenMode": 1 }
	///   - 下次启动读到 1，就是无边框
	///
	/// 所以要在「落盘那一刻」把用户的选择改回来。
	/// 挂 Bsg.GameSettings.Json.JsonFileSettingsProvider.SaveJson(JObject) 的 Prefix，
	/// 因为它正是最终把 JObject 写进 .ini 的唯一出口；此时改 JSON 没有副作用，
	/// 不改内存里的运行状态，也不影响本次画面。
	/// </summary>
	internal static class SaveSettingsPatch
	{
		private const string ProviderTypeName = "Bsg.GameSettings.Json.JsonFileSettingsProvider";

		private const string ProviderAssemblyName = "BSG.GameSettings";

		private const string SaveMethodName = "SaveJson";

		private const string SectionKey = "DisplaySettings";

		private const string ModeKey = "FullScreenMode";

		private static readonly ManualLogSource Log = Plugin.Log;

		private static bool _applied;

		private static int _rewrites;

		internal static int Rewrites
		{
			get { return _rewrites; }
		}

		internal static void Apply(Harmony harmony)
		{
			if (_applied)
			{
				return;
			}
			_applied = true;
			try
			{
				Type type = AccessTools.TypeByName(ProviderTypeName);
				if (type == null)
				{
					type = Type.GetType(ProviderTypeName + ", " + ProviderAssemblyName);
				}
				if (type == null)
				{
					Log.LogWarning("[全屏修复] 没找到 " + ProviderTypeName + "，跳过「守住全屏设置」。");
					return;
				}
				var method = AccessTools.Method(type, SaveMethodName, new Type[] { typeof(JObject) });
				if (method == null)
				{
					Log.LogWarning("[全屏修复] 没找到 " + ProviderTypeName + "." + SaveMethodName + "(JObject)，跳过「守住全屏设置」。");
					return;
				}
				var prefix = new HarmonyMethod(typeof(SaveSettingsPatch).GetMethod("Prefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
				if (prefix == null)
				{
					Log.LogWarning("[全屏修复] 找不到 Prefix 方法，跳过「守住全屏设置」。");
					return;
				}
				harmony.Patch(method, prefix, null);
				Log.LogInfo("[全屏修复] 已挂钩 " + ProviderTypeName + "." + SaveMethodName + "：写盘前把 FullScreenMode 改回你选的全屏。");
			}
			catch (Exception ex)
			{
				Log.LogWarning("[全屏修复] 挂钩「守住全屏设置」失败：" + ex.GetType().Name + ": " + ex.Message);
			}
		}

		/// <summary>
		/// 写盘前把 DisplaySettings.FullScreenMode 改回 0（独占全屏）。
		/// 只改要写出去的那份 JSON，不动内存里的运行时状态，因此不影响本次画面。
		/// </summary>
		private static void Prefix(object[] __args)
		{
			try
			{
				if (__args == null || __args.Length == 0)
				{
					return;
				}
				JObject json = AsJObject(__args[0]);
				if (json == null)
				{
					return;
				}
				var displaySettings = json[SectionKey] as JObject;
				if (displaySettings == null)
				{
					return;
				}
				var current = displaySettings[ModeKey];
				if (current == null)
				{
					return;
				}
				// 不调用泛型 Value<int>()：IL2CPP 对值类型泛型实例化支持不稳，改用 ToString 比较，
				// 这里只需要判断「是不是 0」。
				string before = current.ToString();
				if (before == "0")
				{
					return;
				}
				displaySettings[ModeKey] = new JValue(0L);
				_rewrites++;
				Log.LogInfo("[全屏修复] 写盘前拦下了一次降级：DisplaySettings." + ModeKey + " " + before + " -> 0（已守住第 " + _rewrites + " 次）。");
			}
			catch (Exception ex)
			{
				Log.LogWarning("[全屏修复] 改写待写 JSON 失败（本次照原样写盘）：" + ex.GetType().Name + ": " + ex.Message);
			}
		}
		/// <summary>
		/// Harmony 传进来的参数在不同 IL2CPP 场景下可能是 JObject 包装对象，也可能是裸指针，
		/// 两种都接住；拿不到就放弃（本次照原样写盘，不影响游戏）。
		/// </summary>
		private static JObject AsJObject(object raw)
		{
			if (raw == null)
			{
				return null;
			}
			JObject direct = raw as JObject;
			if (direct != null)
			{
				return direct;
			}
			if (raw is IntPtr)
			{
				IntPtr ptr = (IntPtr)raw;
				if (ptr == IntPtr.Zero)
				{
					return null;
				}
				return new JObject(ptr);
			}
			return null;
		}
	}
}
