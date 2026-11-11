using System;
using System.IO;
using Microsoft.Win32;

namespace FullscreenFix
{
    internal static class FullscreenKeyPin
    {
        private const string RegPath = @"Software\Battlestate Games\EscapeFromTarkov";
        private const string ModeKeyPrefix = "Screenmanager Fullscreen mode_h";
        private const string GraphicsIniRelative = @"SPT_Runtime\user\sptSettings\Graphics.ini";

        private const int Exclusive = 0;
        private const int Borderless = 1;

        private static int _pins;

        internal static int Pins { get { return _pins; } }

        internal static void Apply()
        {
            var configs = Plugin.Configs;
            if (configs == null || !configs.Enabled.Value || !configs.PinFullscreenKeyInPlayerPrefs.Value)
            {
                return;
            }

            try
            {
                int intent = ReadIntentMode(configs);
                int actual = ReadRegistryMode();
                Plugin.Log.LogInfo("[全屏修复] 全屏键检查：你要的模式=" + Describe(intent) + "，上次退出时实际模式=" + Describe(actual) + "。");

                if (intent != Exclusive)
                {
                    Plugin.Log.LogInfo("[全屏修复] 你要的不是「全屏」，本次不动 PlayerPrefs 的全屏键。");
                    return;
                }

                if (actual == Exclusive)
                {
                    Plugin.Log.LogInfo("[全屏修复] 上次已经正常停在「全屏」，无需改动。");
                    return;
                }

                if (WriteRegistryMode(Exclusive))
                {
                    _pins++;
                    Plugin.Log.LogWarning(
                        "[全屏修复] 已把 PlayerPrefs 的全屏键从「" + Describe(actual) + "」改回「全屏」（第 " + _pins + " 次）。\n" +
                        "  → 这个改动【下次启动才生效】：下次启动引擎会重新去申请独占全屏。\n" +
                        "  → 如果不改，它会一直停在无边框，连申请都不做（Player.log 里不会有 dxgi 失败）。\n" +
                        "  → 想撤销：把 Fix/PinFullscreenKeyInPlayerPrefs 关掉，在游戏设置里重新选一次窗口模式。");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[全屏修复] 处理 PlayerPrefs 全屏键失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static int ReadIntentMode(Config.ModConfig configs)
        {
            try
            {
                string path = Path.Combine(PortableSettingsRoot(), GraphicsIniRelative);
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    int sec = text.IndexOf("DisplaySettings", StringComparison.Ordinal);
                    if (sec >= 0)
                    {
                        int key = text.IndexOf("FullScreenMode", sec, StringComparison.Ordinal);
                        if (key >= 0)
                        {
                            int colon = text.IndexOf(':', key);
                            if (colon >= 0)
                            {
                                int i = colon + 1;
                                while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                                {
                                    i++;
                                }
                                int start = i;
                                if (i < text.Length && (text[i] == '-' || text[i] == '+'))
                                {
                                    i++;
                                }
                                while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                                {
                                    i++;
                                }
                                int parsed;
                                if (int.TryParse(text.Substring(start, i - start), out parsed))
                                {
                                    return parsed;
                                }
                            }
                        }
                    }
                }
                if (configs.Verbose.Value)
                {
                    Plugin.Log.LogInfo("[全屏修复] 没能从 Graphics.ini 读到 FullScreenMode（" + path + "），按「全屏」处理。");
                }
            }
            catch (Exception ex)
            {
                if (configs.Verbose.Value)
                {
                    Plugin.Log.LogInfo("[全屏修复] 读 Graphics.ini 失败：" + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return Exclusive;
        }

        private static string PortableSettingsRoot()
        {
            string cwd = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(cwd, "SPT_Runtime")))
            {
                return cwd;
            }
            try
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int i = 0; i < 3 && dir != null; i++)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "SPT_Runtime")))
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
            return cwd;
        }

        private static int ReadRegistryMode()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, false))
            {
                if (key == null)
                {
                    return -1;
                }
                foreach (string name in key.GetValueNames())
                {
                    if (!name.StartsWith(ModeKeyPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    object v = key.GetValue(name);
                    if (v is int)
                    {
                        return (int)v;
                    }
                }
            }
            return -1;
        }

        private static bool WriteRegistryMode(int mode)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, true))
            {
                if (key == null)
                {
                    Plugin.Log.LogWarning("[全屏修复] 打不开 HKCU 的 EFT 注册表项，无法改全屏键。");
                    return false;
                }
                int changed = 0;
                foreach (string name in key.GetValueNames())
                {
                    if (!name.StartsWith(ModeKeyPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    object v = key.GetValue(name);
                    if (v is int && (int)v == mode)
                    {
                        continue;
                    }
                    key.SetValue(name, mode, RegistryValueKind.DWord);
                    changed++;
                    if (Plugin.Configs != null && Plugin.Configs.Verbose.Value)
                    {
                        Plugin.Log.LogInfo("[全屏修复] PlayerPrefs `" + name + "`: " + v + " -> " + mode);
                    }
                }
                return changed > 0;
            }
        }

        private static string Describe(int mode)
        {
            switch (mode)
            {
                case 0: return "全屏(0)";
                case 1: return "无边框(1)";
                case 2: return "窗口(2)";
                case 3: return "最大化窗口(3)";
                case -1: return "未设置";
                default: return "未知(" + mode + ")";
            }
        }
    }
}
