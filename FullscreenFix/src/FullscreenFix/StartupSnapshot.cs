using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FullscreenFix
{
	/// <summary>
	/// 纯只读启动快照：把「启动那一瞬」能读到的所有状态一次性落盘。
	/// 不写注册表、不改设置、不碰游戏文件。
	/// </summary>
	internal static class StartupSnapshot
	{
		private const string RegPath = "Software\\Battlestate Games\\EscapeFromTarkov";
		private const int ENUM_CURRENT_SETTINGS = -1;
		private const string GameRoot = "D:\\EFT-5.0 BE";
		private const string OutDir = "D:\\EFT-WORKS\\full\\work\\snapshots";

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		internal struct DEVMODEW
		{
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
			public ushort dmSpecVersion;
			public ushort dmDriverVersion;
			public ushort dmSize;
			public ushort dmDriverExtra;
			public uint dmFields;
			public int dmPositionX;
			public int dmPositionY;
			public uint dmDisplayOrientation;
			public uint dmDisplayFixedOutput;
			public short dmColor;
			public short dmDuplex;
			public short dmYResolution;
			public short dmTTOption;
			public short dmCollate;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
			public ushort dmLogPixels;
			public uint dmBitsPerPel;
			public uint dmPelsWidth;
			public uint dmPelsHeight;
			public uint dmDisplayFlags;
			public uint dmDisplayFrequency;
			public uint dmICMMethod;
			public uint dmICMIntent;
			public uint dmMediaType;
			public uint dmDitherType;
			public uint dmReserved1;
			public uint dmReserved2;
			public uint dmPanningWidth;
			public uint dmPanningHeight;
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool EnumDisplaySettingsW(string lpszDeviceName, int iModeNum, ref DEVMODEW lpDevMode);

		[DllImport("user32.dll")]
		private static extern int GetSystemMetrics(int nIndex);

		internal static void Dump(string tag)
		{
			StringBuilder sb = new StringBuilder(4096);
			sb.AppendLine("[全屏修复][快照] ==================== START ====================");
			sb.AppendLine("[全屏修复][快照] tag            : " + tag);
			sb.AppendLine("[全屏修复][快照] 本地时间       : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
			sb.AppendLine("[全屏修复][快照] UTC 时间       : " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
			sb.AppendLine("[全屏修复][快照] 进程           : pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + "  exe=" + SafeCurrentProcessName());
			sb.AppendLine("[全屏修复][快照] 当前目录       : " + SafeGet(() => Environment.CurrentDirectory));
			sb.AppendLine("[全屏修复][快照] 64 位          : " + (Environment.Is64BitProcess ? "yes" : "no"));
			sb.AppendLine("[全屏修复][快照] 插件版本       : " + Plugin.PluginVersion);

			Section(sb, "1. HKCU PlayerPrefs（游戏退出时写入的那一份）", DumpRegistry);
			Section(sb, "2. EnumDisplaySettingsEx 当前桌面模式", DumpDisplayMode);
			Section(sb, "3. Unity Screen / Display", DumpUnityScreen);
			Section(sb, "4. SPT 服务端持久化 Graphics.ini", DumpSptIni);
			Section(sb, "5. SPT season-edition.json（存档 edition 记录）", DumpSeasonEdition);
			Section(sb, "6. 本次启动的 Player.log 头部（引擎初始化 swapchain 的证据）", DumpPlayerLogHead);

			sb.AppendLine("[全屏修复][快照] ===================== END =====================");
			Emit(sb.ToString(), tag);
		}

		private static void Section(StringBuilder sb, string title, Action<StringBuilder> body)
		{
			sb.AppendLine("[全屏修复][快照] ---- " + title + " ----");
			try
			{
				body(sb);
			}
			catch (Exception ex)
			{
				sb.AppendLine("[全屏修复][快照]   !! 读取失败: " + ex.GetType().Name + ": " + ex.Message);
			}
		}

		private static void DumpRegistry(StringBuilder sb)
		{
			using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath, false);
			if (key == null)
			{
				sb.AppendLine("[全屏修复][快照]   键不存在: HKCU\\" + RegPath);
				return;
			}
			string[] names = key.GetValueNames();
			sb.AppendLine("[全屏修复][快照]   HKCU\\" + RegPath + "  valueCount=" + names.Length);
			Array.Sort(names, StringComparer.OrdinalIgnoreCase);
			foreach (string name in names)
			{
				object v = null;
				string kind = "?";
				try
				{
					v = key.GetValue(name);
					kind = key.GetValueKind(name).ToString();
				}
				catch (Exception ex)
				{
					sb.AppendLine("[全屏修复][快照]     " + name + " = <读取失败 " + ex.GetType().Name + ">");
					continue;
				}
				string text;
				if (v is byte[] bytes)
				{
					text = "<byte[" + bytes.Length + "]>";
				}
				else if (v == null)
				{
					text = "<null>";
				}
				else
				{
					text = Convert.ToString(v);
				}
				sb.AppendLine("[全屏修复][快照]     " + name + " = " + text + "   (" + kind + ")");
			}
		}

		private static void DumpDisplayMode(StringBuilder sb)
		{
			DEVMODEW dm = new DEVMODEW();
			dm.dmSize = (ushort)Marshal.SizeOf<DEVMODEW>();
			bool ok = EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref dm);
			int err = Marshal.GetLastWin32Error();
			sb.AppendLine("[全屏修复][快照]   EnumDisplaySettingsW(NULL, ENUM_CURRENT_SETTINGS) = " + ok + "  lastError=" + err);
			if (ok)
			{
				sb.AppendLine("[全屏修复][快照]     模式     : " + dm.dmPelsWidth + "x" + dm.dmPelsHeight + "@" + dm.dmDisplayFrequency + "Hz  " + dm.dmBitsPerPel + "bpp");
				sb.AppendLine("[全屏修复][快照]     dmFields : 0x" + dm.dmFields.ToString("X8"));
				sb.AppendLine("[全屏修复][快照]     dmSize   : " + dm.dmSize + "  结构体实际大小: " + Marshal.SizeOf<DEVMODEW>());
				sb.AppendLine("[全屏修复][快照]     device   : '" + (dm.dmDeviceName ?? "") + "'   form='" + (dm.dmFormName ?? "") + "'");
				sb.AppendLine("[全屏修复][快照]     orientation=" + dm.dmDisplayOrientation + " fixedOutput=" + dm.dmDisplayFixedOutput + " flags=0x" + dm.dmDisplayFlags.ToString("X8"));
			}
			sb.AppendLine("[全屏修复][快照]   GetSystemMetrics: SM_CXSCREEN=" + GetSystemMetrics(0) + " SM_CYSCREEN=" + GetSystemMetrics(1) + " SM_CMONITORS=" + GetSystemMetrics(80));

			int modeCount = 0;
			try
			{
				for (int i = 0; i < 512; i++)
				{
					DEVMODEW probe = new DEVMODEW();
					probe.dmSize = (ushort)Marshal.SizeOf<DEVMODEW>();
					if (!EnumDisplaySettingsW(null, i, ref probe))
					{
						break;
					}
					modeCount++;
				}
			}
			catch
			{
			}
			sb.AppendLine("[全屏修复][快照]   可枚举模式总数: " + modeCount);
		}

		private static void DumpUnityScreen(StringBuilder sb)
		{
			try
			{
				sb.AppendLine("[全屏修复][快照]   Screen.width/height        = " + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height);
				sb.AppendLine("[全屏修复][快照]   Screen.fullScreen         = " + UnityEngine.Screen.fullScreen);
				sb.AppendLine("[全屏修复][快照]   Screen.fullScreenMode     = " + UnityEngine.Screen.fullScreenMode);
				UnityEngine.Resolution cr = UnityEngine.Screen.currentResolution;
				sb.AppendLine("[全屏修复][快照]   Screen.currentResolution  = " + cr.width + "x" + cr.height + "@" + cr.refreshRateRatio.numerator + "/" + cr.refreshRateRatio.denominator);
				sb.AppendLine("[全屏修复][快照]   Screen.resolutions 数量   = " + (UnityEngine.Screen.resolutions == null ? -1 : UnityEngine.Screen.resolutions.Length));
				try
				{
					UnityEngine.Display d = UnityEngine.Display.main;
					sb.AppendLine("[全屏修复][快照]   Display.main.systemSize   = " + d.systemWidth + "x" + d.systemHeight);
					sb.AppendLine("[全屏修复][快照]   Display.main.renderingSize= " + d.renderingWidth + "x" + d.renderingHeight);
					sb.AppendLine("[全屏修复][快照]   Display.displays 数量     = " + (UnityEngine.Display.displays == null ? -1 : UnityEngine.Display.displays.Length));
				}
				catch (Exception ex2)
				{
					sb.AppendLine("[全屏修复][快照]   Display.main 读取失败: " + ex2.GetType().Name + ": " + ex2.Message);
				}
			}
			catch (Exception ex)
			{
				sb.AppendLine("[全屏修复][快照]   UnityEngine.Screen 读取失败: " + ex.GetType().Name + ": " + ex.Message);
			}
		}

		private static void DumpSptIni(StringBuilder sb)
		{
			string path = GameRoot + "\\SPT_Runtime\\user\\sptSettings\\Graphics.ini";
			if (!File.Exists(path))
			{
				sb.AppendLine("[全屏修复][快照]   不存在: " + path);
				return;
			}
			FileInfo fi = new FileInfo(path);
			sb.AppendLine("[全屏修复][快照]   " + path);
			sb.AppendLine("[全屏修复][快照]   length=" + fi.Length + "  lastWrite=" + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
			string[] lines = File.ReadAllLines(path);
			int shown = 0;
			foreach (string line in lines)
			{
				string t = line.Trim();
				if (t.Length == 0)
				{
					continue;
				}
				sb.AppendLine("[全屏修复][快照]     | " + t);
				shown++;
				if (shown > 120)
				{
					sb.AppendLine("[全屏修复][快照]     | ...(截断)");
					break;
				}
			}
		}

		private static void DumpSeasonEdition(StringBuilder sb)
		{
			string path = GameRoot + "\\BepInEx\\season-edition.json";
			if (!File.Exists(path))
			{
				sb.AppendLine("[全屏修复][快照]   不存在: " + path);
				return;
			}
			sb.AppendLine("[全屏修复][快照]   " + File.ReadAllText(path).Trim());
		}

		private static void DumpPlayerLogHead(StringBuilder sb)
		{
			string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Low\\Battlestate Games\\EscapeFromTarkov");
			string path = Path.Combine(dir, "Player.log");
			if (!File.Exists(path))
			{
				sb.AppendLine("[全屏修复][快照]   不存在: " + path);
				return;
			}
			FileInfo fi = new FileInfo(path);
			sb.AppendLine("[全屏修复][快照]   " + path + "  length=" + fi.Length + " lastWrite=" + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
			string[] lines;
			using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true))
			{
				System.Collections.Generic.List<string> buf = new System.Collections.Generic.List<string>(600);
				string line;
				int n = 0;
				while (n < 600 && (line = sr.ReadLine()) != null)
				{
					buf.Add(line);
					n++;
				}
				lines = buf.ToArray();
			}
			sb.AppendLine("[全屏修复][快照]   已读取前 " + lines.Length + " 行，扫描 dxgi / SetFullscreenState / 分辨率回退关键字：");
			bool any = false;
			for (int i = 0; i < lines.Length; i++)
			{
				string l = lines[i];
				if (l.IndexOf("dxgi", StringComparison.OrdinalIgnoreCase) >= 0
					|| l.IndexOf("SetFullscreenState", StringComparison.OrdinalIgnoreCase) >= 0
					|| l.IndexOf("Failed to apply requested ExclusiveFullScreen", StringComparison.OrdinalIgnoreCase) >= 0
					|| l.IndexOf("reverting to FullscreenWindow", StringComparison.OrdinalIgnoreCase) >= 0
					|| l.IndexOf("reverting to current display resolution", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					sb.AppendLine("[全屏修复][快照]     L" + (i + 1) + ": " + l.Trim());
					any = true;
				}
			}
			sb.AppendLine("[全屏修复][快照]   >>> 结论: " + (any ? "本次启动【发生了】独占全屏失败（见上面 L 行）" : "本次启动【没有】任何独占全屏失败记录"));
			if (lines.Length > 17)
			{
				sb.AppendLine("[全屏修复][快照]   L18 原文: " + (lines.Length > 17 ? lines[17].Trim() : ""));
			}
		}

		private static void Emit(string text, string tag)
		{
			try
			{
				Plugin.Log.LogInfo(text);
			}
			catch
			{
			}
			try
			{
				if (!Directory.Exists(OutDir))
				{
					Directory.CreateDirectory(OutDir);
				}
				string name = "snap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + tag + ".txt";
				string full = Path.Combine(OutDir, name);
				File.WriteAllText(full, text, new UTF8Encoding(false));
				File.AppendAllText(Path.Combine(OutDir, "index.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + tag + "  -> " + name + "\r\n", new UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				try
				{
					Plugin.Log.LogWarning("[全屏修复][快照] 写文件失败: " + ex.GetType().Name + ": " + ex.Message);
				}
				catch
				{
				}
			}
		}

		private static string SafeCurrentProcessName()
		{
			try
			{
				return System.Diagnostics.Process.GetCurrentProcess().ProcessName;
			}
			catch
			{
				return "?";
			}
		}

		private static string SafeGet(Func<string> f)
		{
			try
			{
				return f();
			}
			catch (Exception ex)
			{
				return "<" + ex.GetType().Name + ">";
			}
		}
	}
}
