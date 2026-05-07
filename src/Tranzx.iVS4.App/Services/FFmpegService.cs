// ============================================================================
// Tranzx.iVS4.App / Services / FFmpegService.cs
//
// Phase 5-9 (B) 強化：
//   - 偵測系統是否有 ffmpeg.exe
//   - 把 AVI 用 H.264 (libx264) 編成 MP4
//   - 進度回報（解析 ffmpeg stderr 的 frame= xxx）
//   - 無 ffmpeg 時提供下載指引
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tranzx.iVS4.App.Services;

public static class FFmpegService
{
    /// <summary>偵測 ffmpeg.exe 路徑，找不到回 null</summary>
    public static string? FindFFmpeg()
    {
        // 1. App 同層
        string? appDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appDir))
        {
            var local = Path.Combine(appDir, "ffmpeg.exe");
            if (File.Exists(local)) return local;
            var localTools = Path.Combine(appDir, "tools", "ffmpeg.exe");
            if (File.Exists(localTools)) return localTools;
        }

        // 2. PATH 環境變數
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* 路徑非法直接略過 */ }
        }

        // 3. 常見位置
        string[] commonPaths =
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
            @"C:\tools\ffmpeg\bin\ffmpeg.exe",
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
        };
        foreach (var p in commonPaths)
            if (File.Exists(p)) return p;

        // 4. winget / Scoop 路徑
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] userPaths =
        {
            Path.Combine(localApp, "Microsoft", "WinGet", "Packages",
                "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-7.1-essentials_build", "bin", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe"),
        };
        foreach (var p in userPaths)
            if (File.Exists(p)) return p;

        // 5. winget 通用搜尋（找最新版）
        var wingetRoot = Path.Combine(localApp, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetRoot))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(wingetRoot, "Gyan.FFmpeg*"))
                {
                    var found = Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories);
                    if (found.Length > 0) return found[0];
                }
            }
            catch { /* 權限等問題就不撈 */ }
        }

        return null;
    }

    /// <summary>查 ffmpeg version，順便驗證是不是真的能跑</summary>
    public static string? GetVersion(string ffmpegPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string firstLine = p.StandardOutput.ReadLine() ?? "";
            p.WaitForExit(3000);
            return firstLine;
        }
        catch { return null; }
    }

    /// <summary>把 AVI 編成 MP4 (H.264 libx264)，回傳是否成功</summary>
    /// <param name="onProgress">傳入目前已處理的 frame 數（給 UI 進度條）</param>
    public static async Task<bool> ConvertAviToMp4Async(
        string ffmpegPath, string aviPath, string mp4Path,
        int? totalFrames = null,
        Action<int, string>? onProgress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(ffmpegPath) || !File.Exists(aviPath)) return false;

        // -y         覆蓋
        // -i         輸入
        // -c:v libx264   H.264 編碼
        // -crf 23    品質（18~28，越小越好但檔案越大；23 是預設甜蜜點）
        // -preset fast   編碼速度
        // -pix_fmt yuv420p   相容性最高的 pixel format
        // -movflags +faststart   metadata 放前面，網頁播放更友善
        // -an        無音訊
        var args = $"-y -i \"{aviPath}\" -c:v libx264 -crf 23 -preset fast " +
                   $"-pix_fmt yuv420p -movflags +faststart -an \"{mp4Path}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = new Process { StartInfo = psi };

        // ffmpeg 進度資訊輸出在 stderr
        var rxFrame = new Regex(@"frame=\s*(\d+)", RegexOptions.Compiled);
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var m = rxFrame.Match(e.Data);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int f))
            {
                onProgress?.Invoke(f, e.Data);
            }
        };

        try
        {
            if (!proc.Start()) return false;
            proc.BeginErrorReadLine();

            // 用 Task 等 process 結束，讓 cancellation 能 kill
            var waitTask = Task.Run(() => proc.WaitForExit(), ct);
            await waitTask;
            if (ct.IsCancellationRequested)
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            return proc.ExitCode == 0 && File.Exists(mp4Path) && new FileInfo(mp4Path).Length > 0;
        }
        catch
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            return false;
        }
    }
}
