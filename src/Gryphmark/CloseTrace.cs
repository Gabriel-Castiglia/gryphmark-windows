using System.Diagnostics;
using System.IO;

namespace Gryphmark;

/// <summary>Diagnostics for slow shutdown. With GM_TRACE_CLOSE=&lt;log file&gt; the window closes itself
/// two seconds after the editor is ready, and each shutdown step is timed into that file.</summary>
public static class CloseTrace
{
    private static readonly string? LogPath = Environment.GetEnvironmentVariable("GM_TRACE_CLOSE");
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static bool On => !string.IsNullOrEmpty(LogPath);

    public static void Log(string step)
    {
        if (!On) return;
        try { File.AppendAllText(LogPath!, $"{DateTime.Now:HH:mm:ss.fff}  {Clock.Elapsed.TotalMilliseconds,7:F0} ms  {step}{Environment.NewLine}"); } catch { }
    }
}
