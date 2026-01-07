using AudioAGC.Args;
using AudioAGC.FFmpeg;
using AudioAGC.Pipeline;

internal sealed partial class Program
{
    private const string DefaultFfmpeg = "ffmpeg"; // must be in PATH, or set full path

    public static async Task<int> Main(string[] args)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        // Parse CLI options
        var options = ArgumentParser.Parse(args, projectRoot, DefaultFfmpeg);

        var ffmpeg = new FfmpegService(options.FfmpegPath);
        var pipeline = new ProcessingPipeline(options, ffmpeg);

        var exitCode = await pipeline.RunAsync();
        return exitCode;
    }
}
