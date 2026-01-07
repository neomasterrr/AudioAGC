internal sealed partial class Program
{
    private static void PrintHelp() =>
        Console.WriteLine(
            @"Two-pass MP3 loudness normalization (EBU R128 / FFmpeg loudnorm)

Usage:
  AudioNormalize.exe --in <dir> --out <dir> [options]

Options:
  --i <LUFS>        Integrated loudness target (default -16)
  --tp <dBTP>       True-peak target (default -1.0)
  --lra <LU>        Loudness range target (default 11)
  --sr <Hz>         Output sample rate (default 48000)
  --q <0..9>        MP3 VBR quality for libmp3lame (default 2, 0 best)
  --threads <n>     Parallel files at once (default 1)
  --no-overwrite    Do not overwrite existing outputs
  --ffmpeg <path>   Full path to ffmpeg.exe if not in PATH
  --help            Show help

Examples:
  AudioNormalize.exe --in .\in --out .\out_mp3
  AudioNormalize.exe --in .\in --out .\out_mp3 --i -18 --tp -1 --threads 4
");
}