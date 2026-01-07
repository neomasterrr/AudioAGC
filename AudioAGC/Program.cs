using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

internal sealed partial class Program
{
    private const string DefaultFfmpeg = "ffmpeg"; // must be in PATH, or set full path

    public static async Task<int> Main(string[] args)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        // Defaults for a local workflow
        var inDir = Path.Combine(projectRoot, "Output/in");
        var outDir = Path.Combine(projectRoot, "Output/out");

        Console.WriteLine($"InDir: {inDir} \nOutDir: {outDir}");

        var targetI = -16.0;
        var targetTP = -1.0;
        var targetLRA = 11.0;
        var sampleRate = 48000;
        var vbrQuality = 2; // 0 best .. 9 worst
        var overwrite = true;
        var maxDegree = 12; // set >1 for parallel processing (e.g., 4)
        var ffmpeg = DefaultFfmpeg;

        // Args (simple parsing)
        // Usage: AudioNormalize.exe --in <dir> --out <dir> [--i -16] [--tp -1] [--lra 11] [--sr 48000] [--q 2] [--no-overwrite] [--threads 4] [--ffmpeg <path>]
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim();

            string Next()
            {
                return i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {a}");
            }

            switch (a)
            {
                case "--in": inDir = Next(); break;
                case "--out": outDir = Next(); break;
                case "--i": targetI = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--tp": targetTP = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--lra": targetLRA = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--sr": sampleRate = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--q": vbrQuality = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--threads": maxDegree = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--no-overwrite": overwrite = false; break;
                case "--ffmpeg": ffmpeg = Next(); break;
                case "--help":
                case "-h":
                case "/?":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown arg: {a}");
                    PrintHelp();
                    return 1;
            }
        }

        if (!Directory.Exists(inDir))
        {
            await Console.Error.WriteLineAsync($"Input directory not found: {inDir}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        if (!await CanRunToolAsync(ffmpeg, "-version"))
        {
            Console.Error.WriteLine(
                "ffmpeg not found or not runnable. Install ffmpeg and add to PATH, or pass --ffmpeg <full_path_to_ffmpeg.exe>.");
            return 1;
        }

        var files = Directory.GetFiles(inDir, "*.mp3", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Console.WriteLine($"No .mp3 files found in {inDir}");
            return 0;
        }

        Console.WriteLine($"Found {files.Length} mp3 file(s).");
        Console.WriteLine(
            $"Targets: I={targetI} LUFS, TP={targetTP} dBTP, LRA={targetLRA}, SR={sampleRate} Hz, MP3 VBR q={vbrQuality}");
        Console.WriteLine($"Output: {outDir}");
        Console.WriteLine(maxDegree > 1 ? $"Parallel: {maxDegree} threads" : "Parallel: off");

        int ok = 0, skipped = 0, failed = 0;

        // Limit concurrency (FFmpeg is heavy; don’t set threads too high)
        using var sem = new SemaphoreSlim(maxDegree);

        var tasks = files.Select(async inPath =>
        {
            await sem.WaitAsync();
            try
            {
                var rel = Path.GetRelativePath(inDir, inPath);
                var outPath = Path.Combine(outDir, Path.ChangeExtension(rel, ".mp3"));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                if (!overwrite && File.Exists(outPath))
                {
                    Interlocked.Increment(ref skipped);
                    return;
                }

                Console.WriteLine($"Processing: {rel}");

                var m = await LoudnormAnalyzeAsync(ffmpeg, inPath, targetI, targetTP, targetLRA);
                await LoudnormApplyAsync(ffmpeg, inPath, outPath, m, targetI, targetTP, targetLRA, sampleRate,
                    vbrQuality, overwrite);

                Interlocked.Increment(ref ok);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                Console.Error.WriteLine($"FAILED: {inPath}");
                Console.Error.WriteLine(ex.Message);
            }
            finally
            {
                sem.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Console.WriteLine($"Done. OK={ok}, Skipped={skipped}, Failed={failed}");
        return failed == 0 ? 0 : 2;
    }

    private static async Task<bool> CanRunToolAsync(string fileName, string arguments)
    {
        try
        {
            var (exit, _, _) = await RunProcessAsync(fileName, arguments);
            return exit == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<LoudnormMeasurement> LoudnormAnalyzeAsync(string ffmpeg, string inputPath, double targetI,
        double targetTP, double targetLRA)
    {
        var args =
            $"-hide_banner -i \"{inputPath}\" " +
            $"-af \"loudnorm=I={targetI.ToString(CultureInfo.InvariantCulture)}:TP={targetTP.ToString(CultureInfo.InvariantCulture)}:LRA={targetLRA.ToString(CultureInfo.InvariantCulture)}:print_format=json\" " +
            "-f null -";

        var (exitCode, _, stderr) = await RunProcessAsync(ffmpeg, args);
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

    private static async Task LoudnormApplyAsync(
        string ffmpeg,
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

        var (exitCode, _, stderr) = await RunProcessAsync(ffmpeg, args);
        if (exitCode != 0)
            throw new InvalidOperationException($"FFmpeg apply failed (exit {exitCode}).\n{stderr}");
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string fileName,
        string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        if (!p.Start())
            throw new InvalidOperationException($"Failed to start: {fileName}");

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync();

        return (p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private readonly record struct LoudnormMeasurement(
        string InputI,
        string InputTP,
        string InputLRA,
        string InputThresh,
        string TargetOffset
    );
}