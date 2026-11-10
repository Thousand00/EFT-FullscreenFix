using System;
using System.Reflection;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using FullscreenFix.Config;
using HarmonyLib;

namespace FullscreenFix.Patches
{
	internal static class StoredSettingsPatch
	{
		private const string GroupTypeName = "EFT.Settings.Graphics.GraphicsSettingsGroup";

		private const string ModeFieldName = "FullScreenMode";

		private const int ExclusiveFullScreenValue = 0;

		internal static void Apply(Harmony harmony)
		{
			Type type = AccessTools.TypeByName("EFT.Settings.Graphics.GraphicsSettingsGroup");
			if (type == null)
			{
				ManualLogSource log = Plugin.Log;
				log.LogWarning($"[全屏修复] 找不到类型 {"EFT.Settings.Graphics.GraphicsSettingsGroup"}，跳过设置持久化保持。");
			}
			else
			{
				PatchStore(harmony, type);
				PatchGetStored(harmony, type);
			}
		}

		private static void PatchStore(Harmony harmony, Type groupType)
		{
			MethodInfo methodInfo = null;
			MethodInfo[] methods = groupType.GetMethods(AccessTools.all);
			foreach (MethodInfo methodInfo2 in methods)
			{
				if (!(methodInfo2.Name != "StoreDisplaySettings"))
				{
					ParameterInfo[] parameters = methodInfo2.GetParameters();
					if (parameters.Length == 1 && !parameters[0].ParameterType.IsByRef)
					{
						methodInfo = methodInfo2;
						break;
					}
				}
			}
			if (methodInfo == null)
			{
				Plugin.Log.LogWarning("[全屏修复] 找不到 GraphicsSettingsGroup.StoreDisplaySettings(EftDisplaySettings)。");
				return;
			}
			try
			{
				harmony.Patch(methodInfo, new HarmonyMethod(typeof(StoredSettingsPatch), "ForceExclusivePrefix"));
				Plugin.Log.LogInfo("[全屏修复] 已注入 GraphicsSettingsGroup.StoreDisplaySettings（写盘前保持『全屏』）");
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				log.LogWarning($"[全屏修复] 注入 StoreDisplaySettings 失败：{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void PatchGetStored(Harmony harmony, Type groupType)
		{
			MethodInfo methodInfo = null;
			MethodInfo[] methods = groupType.GetMethods(AccessTools.all);
			foreach (MethodInfo methodInfo2 in methods)
			{
				if (!(methodInfo2.Name != "GetStoredDisplaySettings"))
				{
					ParameterInfo[] parameters = methodInfo2.GetParameters();
					if (parameters.Length == 1 && parameters[0].ParameterType.IsByRef)
					{
						methodInfo = methodInfo2;
						break;
					}
				}
			}
			if (methodInfo == null)
			{
				Plugin.Log.LogWarning("[全屏修复] 找不到 GraphicsSettingsGroup.GetStoredDisplaySettings(ref EftDisplaySettings)。");
				return;
			}
			try
			{
				harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(StoredSettingsPatch), "ForceExclusivePostfix"));
				Plugin.Log.LogInfo("[全屏修复] 已注入 GraphicsSettingsGroup.GetStoredDisplaySettings(ref)（读取时返回『全屏』）");
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				log.LogWarning($"[全屏修复] 注入 GetStoredDisplaySettings 失败：{ex.GetType().Name}: {ex.Message}");
			}
		}

		private static void ForceExclusivePrefix(object[] __args)
		{
			ForceExclusive(__args);
		}

		private static void ForceExclusivePostfix(object[] __args)
		{
			ForceExclusive(__args);
		}

		private static void ForceExclusive(object[] args)
		{
			ModConfig configs = Plugin.Configs;
			if (configs == null || !configs.Enabled.Value || !configs.KeepStoredExclusive.Value || args == null || args.Length == 0)
			{
				return;
			}
			try
			{
				object obj = args[0];
				if (obj == null)
				{
					return;
				}
				FieldInfo field = obj.GetType().GetField("FullScreenMode");
				if (field == null)
				{
					return;
				}
				object obj2 = Enum.ToObject(field.FieldType, 0);
				object value = field.GetValue(obj);
				if (object.Equals(value, obj2))
				{
					return;
				}
				field.SetValue(obj, obj2);
				if (configs.Verbose.Value)
				{
					ManualLogSource log = Plugin.Log;
					log.LogInfo($"[全屏修复] 持久化屏幕模式 {value} -> {obj2}（保持界面上显示『全屏』）");
				}
			}
			catch (Exception ex)
			{
				ManualLogSource log2 = Plugin.Log;
				log2.LogWarning($"[全屏修复] 修正持久化屏幕模式失败：{ex.Message}");
			}
		}
	}
}
