namespace AudioAGC.Args;

internal sealed record CliOptions(
    string InputDirectory,
    string OutputDirectory,
    double TargetI,
    double TargetTP,
    double TargetLRA,
    int SampleRate,
    int VbrQuality,
    bool OverwriteExisting,
    int MaxDegreeOfParallelism,
    string FfmpegPath)
{
    public static CliOptions Defaults(string projectRoot, string ffmpegPath) => new(
        InputDirectory: Path.Combine(projectRoot, "Output/in"),
        OutputDirectory: Path.Combine(projectRoot, "Output/out"),
        TargetI: -16.0,
        TargetTP: -1.0,
        TargetLRA: 11.0,
        SampleRate: 48_000,
        VbrQuality: 2,
        OverwriteExisting: true,
        MaxDegreeOfParallelism: Math.Max(1, Environment.ProcessorCount / 2),
        FfmpegPath: ffmpegPath);
}

