using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FullscreenFix
{
	internal static class DisplayDiagnostics
	{
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct DISPLAY_DEVICEW
		{
			public int cb;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string DeviceName;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string DeviceString;

			public int StateFlags;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string DeviceID;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string DeviceKey;
		}

		private const int SM_CXSCREEN = 0;

		private const int SM_CYSCREEN = 1;

		private const int SM_CMONITORS = 80;

		private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 1;

		private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 4;

		private const int DISPLAY_DEVICE_MIRRORING_DRIVER = 8;

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern bool EnumDisplayDevicesW(string lpDevice, uint iDevNum, ref DISPLAY_DEVICEW lpDisplayDevice, uint dwFlags);

		[DllImport("user32.dll")]
		private static extern int GetSystemMetrics(int nIndex);

		internal static void Run()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("[全屏修复] ==== 显示环境自检（只读） ====");
			try
			{
				stringBuilder.AppendLine($"  桌面尺寸(逻辑)  : {GetSystemMetrics(0)}x{GetSystemMetrics(1)}");
				stringBuilder.AppendLine($"  显示器数量      : {GetSystemMetrics(80)}");
			}
			catch (Exception ex)
			{
				stringBuilder.AppendLine($"  读取系统度量失败 : {ex.Message}");
			}
			int num = 0;
			StringBuilder stringBuilder6 = new StringBuilder();
			try
			{
				DISPLAY_DEVICEW lpDisplayDevice = new DISPLAY_DEVICEW
				{
					cb = Marshal.SizeOf<DISPLAY_DEVICEW>()
				};
				for (uint num2 = 0u; num2 < 32; num2++)
				{
					lpDisplayDevice.cb = Marshal.SizeOf<DISPLAY_DEVICEW>();
					if (EnumDisplayDevicesW(null, num2, ref lpDisplayDevice, 0u))
					{
						bool flag = (lpDisplayDevice.StateFlags & 1) != 0;
						bool flag2 = (lpDisplayDevice.StateFlags & 4) != 0;
						bool flag3 = (lpDisplayDevice.StateFlags & 8) != 0;
						if (flag)
						{
							num++;
						}
						stringBuilder.AppendLine($"  [{num2}] {(lpDisplayDevice.DeviceName),-16} '{lpDisplayDevice.DeviceString}' {(flag2 ? "PRIMARY " : "")}{(flag ? "attached" : "detached")}{(flag3 ? " MIRROR" : "")}  id={lpDisplayDevice.DeviceID}");
						string text = lpDisplayDevice.DeviceString ?? string.Empty;
						if (text.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("GameViewer", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("IDD", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Sunshine", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Parsec", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							stringBuilder6.AppendLine($"    - {lpDisplayDevice.DeviceName} '{text}' ({(flag ? "已附加" : "未附加")})");
						}
						continue;
					}
					break;
				}
			}
			catch (Exception ex2)
			{
				stringBuilder.AppendLine($"  枚举显示设备失败 : {ex2.Message}");
			}
			stringBuilder.AppendLine($"  附加到桌面的设备: {num}");
			stringBuilder.AppendLine("[全屏修复] ==================================");
			Plugin.Log.LogInfo(stringBuilder.ToString());
			if (stringBuilder6.Length > 0)
			{
				Plugin.Log.LogWarning("[全屏修复] 检测到虚拟/串流显示适配器：\n" + stringBuilder6?.ToString() + "  这类适配器（尤其是网易 UU / GameViewer 的 IDD 虚拟屏）会干扰 DXGI 独占全屏，\n  它们常驻后台时 SetFullscreenState 会返回 0x887A0022 / E_FAIL。\n  建议：在任务栏退出 UU/GameViewer，或在「服务」里把 GameViewerService 停掉后重开游戏。");
			}
		}
	}
}
