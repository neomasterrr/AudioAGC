using System.Globalization;

namespace AudioAGC.Args;

internal static class ArgumentParser
{
    public static CliOptions Parse(string[] args, string projectRoot, string defaultFfmpeg)
    {
        var options = CliOptions.Defaults(projectRoot, defaultFfmpeg);

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim();

            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {a}");

            switch (a)
            {
                case "--in": options = options with { InputDirectory = Next() }; break;
                case "--out": options = options with { OutputDirectory = Next() }; break;
                case "--i": options = options with { TargetI = double.Parse(Next(), CultureInfo.InvariantCulture) }; break;
                case "--tp": options = options with { TargetTP = double.Parse(Next(), CultureInfo.InvariantCulture) }; break;
                case "--lra": options = options with { TargetLRA = double.Parse(Next(), CultureInfo.InvariantCulture) }; break;
                case "--sr": options = options with { SampleRate = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
                case "--q": options = options with { VbrQuality = int.Parse(Next(), CultureInfo.InvariantCulture) }; break;
                case "--threads": options = options with { MaxDegreeOfParallelism = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)) }; break;
                case "--no-overwrite": options = options with { OverwriteExisting = false }; break;
                case "--ffmpeg": options = options with { FfmpegPath = Next() }; break;
                case "--help":
                case "-h":
                case "/?":
                    Program.PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown arg: {a}");
                    Program.PrintHelp();
                    Environment.Exit(1);
                    break;
            }
        }

        return options;
    }
}
