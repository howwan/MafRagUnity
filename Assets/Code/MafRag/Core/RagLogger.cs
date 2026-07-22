// 统一日志（参考 unified-rag 的 logging_config.py：控制台 + 轮转文件）。
// 通过 Application.logMessageReceivedThreaded 捕获 Unity 全部 Debug.Log/Warn/Error（含异常），
// 带时间戳与级别写入 persistentDataPath/RagLogs/rag.log，5MB 自动轮转、保留 3 份备份。
// 日志文件必须放 persistentDataPath：dataPath/streamingAssetsPath 在 Android/iOS 上为只读，写不进去。

using System;
using System.IO;
using UnityEngine;

namespace MafRag
{
    public static class RagLogger
    {
        public enum Level { Debug, Info, Warn, Error }

        // 级别过滤（对齐 unified-rag 的 LOG_LEVEL）。默认 Info。
        public static Level MinLevel = Level.Info;

        private const long MaxBytes = 5L * 1024 * 1024; // 单文件 5MB
        private const int MaxBackups = 3;                // 保留 3 份历史
        private static readonly object _lock = new object();
        private static bool _subscribed;

        private static string Dir => Path.Combine(Application.persistentDataPath, "RagLogs");
        private static string LogPath => Path.Combine(Dir, "rag.log");

        // 在 RagBoot.Boot() 最前面调用一次，之后所有 Unity 日志自动落盘。
        public static void Init()
        {
            if (_subscribed) return;
            _subscribed = true;
            Application.logMessageReceivedThreaded += OnUnityLog;
            Info("RagLogger", "日志系统已启动，写入：" + LogPath);
        }

        // 捕获 Unity 原生日志（含异常）。线程安全（加锁）。
        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            Level lv = (type == LogType.Error || type == LogType.Exception) ? Level.Error
                     : (type == LogType.Warning) ? Level.Warn
                     : (type == LogType.Assert) ? Level.Error
                     : Level.Info;
            string line = condition;
#if UNITY_EDITOR
            // 编辑器下 condition 可能已含调用方，保持原样
#endif
            Write(lv, line, (lv == Level.Error) ? stackTrace : null);
        }

        public static void Info(string tag, string msg) => Write(Level.Info, $"[{tag}] {msg}", null);
        public static void Warn(string tag, string msg) => Write(Level.Warn, $"[{tag}] {msg}", null);
        public static void Error(string tag, string msg) => Write(Level.Error, $"[{tag}] {msg}", null);

        // ---------- 供 UI 使用的只读/导出辅助 ----------
        public static string CurrentLogPath => LogPath;
        public static string LogDirectory => Dir;

        // 读取日志尾部用于“查看”（默认最近 3000 行，避免一次性载入过大文件卡顿）
        public static string ReadTail(int maxLines = 3000)
        {
            try
            {
                if (!File.Exists(LogPath)) return "（暂无日志）";
                var lines = File.ReadAllLines(LogPath);
                if (lines.Length <= maxLines) return string.Join("\n", lines);
                return string.Join("\n", lines, lines.Length - maxLines, maxLines);
            }
            catch (Exception ex) { return "读取日志失败：" + ex.Message; }
        }

        // 导出：复制当前日志为带时间戳的独立文件，返回导出路径；无日志返回 null
        public static string ExportCopy()
        {
            try
            {
                if (!File.Exists(LogPath)) return null;
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string dst = Path.Combine(Dir, "rag-log-" + stamp + ".txt");
                File.Copy(LogPath, dst, true);
                return dst;
            }
            catch (Exception) { return null; }
        }

        // 级别切换：直接设置 / 按名称应用（名称无效则忽略）
        public static void SetMinLevel(Level lv) => MinLevel = lv;
        public static void ApplyMinLevel(string name)
        {
            if (Enum.TryParse<Level>(name, true, out var lv)) MinLevel = lv;
        }

        private static void Write(Level lv, string msg, string stack)
        {
            if (lv < MinLevel) return;
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{lv.ToString().ToUpper()}] {msg}";
            if (!string.IsNullOrEmpty(stack)) line += "\n" + stack;

            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                    RotateIfNeeded();
                    File.AppendAllText(LogPath, line + "\n");
                }
                catch
                {
                    // 日志写入失败绝不应影响主流程
                }
            }
        }

        // 超过阈值则轮转：rag.log -> rag.log.1 -> rag.log.2 -> rag.log.3（删最旧）
        private static void RotateIfNeeded()
        {
            if (!File.Exists(LogPath)) return;
            var fi = new FileInfo(LogPath);
            if (fi.Length < MaxBytes) return;

            string oldest = LogPath + "." + MaxBackups;
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int i = MaxBackups - 1; i >= 1; i--)
            {
                string src = (i == 1) ? LogPath : LogPath + "." + (i - 1);
                string dst = LogPath + "." + i;
                if (File.Exists(src)) File.Move(src, dst);
            }
        }
    }
}
