using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AudioAGC.Models;

namespace AudioAGC.FFmpeg;

internal sealed class FfmpegService
{
    private readonly string _ffmpegPath;

    public FfmpegService(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task<bool> CanRunAsync() => (await RunProcessAsync("-version")).ExitCode == 0;

    public async Task<LoudnormMeasurement> AnalyzeAsync(string inputPath, double targetI, double targetTP, double targetLRA)
    {
        var args =
            $"-hide_banner -i \"{inputPath}\" " +
            $"-af \"loudnorm=I={targetI.ToString(CultureInfo.InvariantCulture)}:TP={targetTP.ToString(CultureInfo.InvariantCulture)}:LRA={targetLRA.ToString(CultureInfo.InvariantCulture)}:print_format=json\" " +
            "-f null -";

        var (exitCode, _, stderr) = await RunProcessAsync(args);
        if (exitCode != 0)
            throw new InvalidOperationException($"FFmpeg analysis failed (exit {exitCode}).\n{stderr}");

        var json = ExtractJsonObject(stderr);
        if (json is null)
            throw new InvalidOperationException("Could not find loudnorm JSON in ffmpeg output.");

        using var doc = JsonDocument.Parse(json);

        string Get(string name)
        {
            if (!doc.RootElement.TryGetProperty(name, out var p))
                throw new InvalidOperationException($"Missing JSON field: {name}");
            return p.GetString() ?? throw new InvalidOperationException($"Null JSON field: {name}");
        }

        return new LoudnormMeasurement(
            Get("input_i"),
            Get("input_tp"),
            Get("input_lra"),
            Get("input_thresh"),
            Get("target_offset")
        );
    }

    public async Task ApplyAsync(
        string inputPath,
        string outputPath,
        LoudnormMeasurement m,
        double targetI,
        double targetTP,
        double targetLRA,
        int sampleRate,
        int vbrQuality,
        bool overwrite)
    {
        var filter =
            "loudnorm=" +
            $"I={targetI.ToString(CultureInfo.InvariantCulture)}:" +
            $"TP={targetTP.ToString(CultureInfo.InvariantCulture)}:" +
            $"LRA={targetLRA.ToString(CultureInfo.InvariantCulture)}:" +
            $"measured_I={m.InputI}:" +
            $"measured_TP={m.InputTP}:" +
            $"measured_LRA={m.InputLRA}:" +
            $"measured_thresh={m.InputThresh}:" +
            $"offset={m.TargetOffset}:" +
            "linear=true";

        var overwriteFlag = overwrite ? "-y" : "-n";

        var args =
            $"-hide_banner {overwriteFlag} -i \"{inputPath}\" " +
            $"-ar {sampleRate} " +
            $"-af \"{filter}\" " +
            $"-c:a libmp3lame -q:a {vbrQuality} " +
            $"\"{outputPath}\"";

        var (exitCode, _, stderr) = await RunProcessAsync(args);
        if (exitCode != 0)
            throw new InvalidOperationException($"FFmpeg apply failed (exit {exitCode}).\n{stderr}");
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        if (!p.Start())
            throw new InvalidOperationException($"Failed to start: {_ffmpegPath}");

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync();

        return (p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
    }
}

