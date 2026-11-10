using System;
using System.IO;
using System.Text;

namespace K1FullscreenFix.Diagnostics;

/// <summary>
/// 本插件专用的日志：同时写一份独立文件，方便排查时只看这一个文件。
///
/// 为什么需要它：v0.2.0 的故障（Start() 抛 NRE）只有一行日志，
/// 而且混在 6900 行别的插件日志里，定位非常费劲。
///
/// 文件位置：&lt;BepInEx 根目录&gt;\K1Logs\K1FullscreenFix.log
///   上一次运行的会被改名成 K1FullscreenFix.prev.log 保留。
///
/// 所有写操作都吞异常：日志系统本身绝不能把插件搞崩。
/// </summary>
internal static class K1Log
{
    private static readonly object Gate = new object();

    private static StreamWriter _writer;
    private static string _path;
    private static bool _broken;

    /// <summary>
    /// 是否输出高频细节（<see cref="Detail"/>）。由 Plugin.Load() 按 General/Verbose 设置。
    /// v0.3.0 里这个开关只被读进配置快照却没真正生效，v0.3.1 补上。
    /// </summary>
    private static bool _verbose = true;

    internal static string FilePath => _path ?? "(未初始化)";
    internal static bool FileOk => _writer != null;

    /// <summary>由 Plugin.Load() 在配置快照完成后调用。</summary>
    internal static void SetVerbose(bool verbose)
    {
        _verbose = verbose;
    }

    internal static void Init()
    {
        if (_writer != null || _broken) return;

        try
        {
            string root = null;

            try
            {
                root = BepInEx.Paths.BepInExRootPath;
            }
            catch
            {
                // BepInEx 的 Paths 在某些加载阶段可能还没就绪，走下面的兜底。
            }

            if (string.IsNullOrEmpty(root))
            {
                // 兜底：用插件 dll 自己所在目录的上一级（正常情况下是 plugins\<名>\）。
                try
                {
                    string asm = typeof(Plugin).Assembly.Location;
                    if (!string.IsNullOrEmpty(asm))
                    {
                        string dir = Path.GetDirectoryName(asm);
                        if (!string.IsNullOrEmpty(dir)) root = dir;
                    }
                }
                catch
                {
                    // 继续用当前工作目录兜底
                }
            }

            if (string.IsNullOrEmpty(root)) root = Directory.GetCurrentDirectory();

            string folder = Path.Combine(root, "K1Logs");
            Directory.CreateDirectory(folder);

            _path = Path.Combine(folder, "K1FullscreenFix.log");

            // 保留上一轮，方便对照「这次和上次差在哪」。
            try
            {
                if (File.Exists(_path))
                {
                    string prev = Path.Combine(folder, "K1FullscreenFix.prev.log");
                    if (File.Exists(prev)) File.Delete(prev);
                    File.Move(_path, prev);
                }
            }
            catch
            {
                // 改名失败就直接覆盖，不影响后续
            }

            var fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            _broken = true;
            SafeBepInEx("WARN", $"独立日志文件创建失败（只写 BepInEx 日志）：{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void Banner(string text)
    {
        Write("INFO", "==================================================", true);
        Write("INFO", text, true);
        Write("INFO", "==================================================", true);
    }

    internal static void Info(string msg) => Write("INFO", msg, true);

    internal static void Warn(string msg) => Write("WARN", msg, true);

    internal static void Warn(string msg, Exception ex)
    {
        Write("WARN", msg + Environment.NewLine + Describe(ex), true);
    }

    internal static void Error(string msg) => Write("ERROR", msg, true);

    internal static void Error(string msg, Exception ex)
    {
        Write("ERROR", msg + Environment.NewLine + Describe(ex), true);
    }

    /// <summary>只写文件、不进 BepInEx 日志——高频细节用这个，避免把主日志顶爆。</summary>
    internal static void Detail(string msg)
    {
        if (!_verbose) return;
        Write("INFO", msg, false);
    }

    private static void Write(string level, string msg, bool alsoBepInEx)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // 写文件失败不影响游戏
            }
        }

        if (alsoBepInEx) SafeBepInEx(level, msg);
    }

    private static void SafeBepInEx(string level, string msg)
    {
        try
        {
            var log = Plugin.Log;
            if (log == null) return;

            if (level == "ERROR") log.LogError("[K1] " + msg);
            else if (level == "WARN") log.LogWarning("[K1] " + msg);
            else log.LogInfo("[K1] " + msg);
        }
        catch
        {
            // 日志系统自身异常一律吞掉
        }
    }

    internal static string Describe(Exception ex)
    {
        if (ex == null) return "(null)";

        var sb = new StringBuilder();
        sb.AppendLine($"  异常类型: {ex.GetType().FullName}");
        sb.AppendLine($"  消息    : {ex.Message}");

        if (ex.InnerException != null)
        {
            sb.AppendLine($"  内层异常: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
        }

        try
        {
            sb.Append(ex.StackTrace);
        }
        catch
        {
            sb.Append("  (取不到堆栈)");
        }

        return sb.ToString();
    }
}
