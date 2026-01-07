using AudioAGC.Args;
using AudioAGC.FFmpeg;

namespace AudioAGC.Pipeline;

internal sealed class ProcessingPipeline
{
    private readonly CliOptions _options;
    private readonly FfmpegService _ffmpeg;

    public ProcessingPipeline(CliOptions options, FfmpegService ffmpeg)
    {
        _options = options;
        _ffmpeg = ffmpeg;
    }

    public async Task<int> RunAsync()
    {
        if (!Directory.Exists(_options.InputDirectory))
        {
            await Console.Error.WriteLineAsync($"Input directory not found: {_options.InputDirectory}");
            return 1;
        }

        Directory.CreateDirectory(_options.OutputDirectory);

        if (!await _ffmpeg.CanRunAsync())
        {
            Console.Error.WriteLine("ffmpeg not found or not runnable. Install ffmpeg and add to PATH, or pass --ffmpeg <full_path_to_ffmpeg.exe>.");
            return 1;
        }

        var files = Directory.GetFiles(_options.InputDirectory, "*.mp3", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Console.WriteLine($"No .mp3 files found in {_options.InputDirectory}");
            return 0;
        }

        Console.WriteLine($"Found {files.Length} mp3 file(s).");
        Console.WriteLine($"Targets: I={_options.TargetI} LUFS, TP={_options.TargetTP} dBTP, LRA={_options.TargetLRA}, SR={_options.SampleRate} Hz, MP3 VBR q={_options.VbrQuality}");
        Console.WriteLine($"Output: {_options.OutputDirectory}");
        Console.WriteLine(_options.MaxDegreeOfParallelism > 1 ? $"Parallel: {_options.MaxDegreeOfParallelism} threads" : "Parallel: off");

        int ok = 0, skipped = 0, failed = 0;

        using var sem = new SemaphoreSlim(_options.MaxDegreeOfParallelism);

        var tasks = files.Select(async inPath =>
        {
            await sem.WaitAsync();
            try
            {
                var rel = Path.GetRelativePath(_options.InputDirectory, inPath);
                var outPath = Path.Combine(_options.OutputDirectory, Path.ChangeExtension(rel, ".mp3"));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                if (!_options.OverwriteExisting && File.Exists(outPath))
                {
                    Interlocked.Increment(ref skipped);
                    return;
                }

                Console.WriteLine($"Processing: {rel}");

                var m = await _ffmpeg.AnalyzeAsync(inPath, _options.TargetI, _options.TargetTP, _options.TargetLRA);
                await _ffmpeg.ApplyAsync(inPath, outPath, m, _options.TargetI, _options.TargetTP, _options.TargetLRA, _options.SampleRate, _options.VbrQuality, _options.OverwriteExisting);

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
}

