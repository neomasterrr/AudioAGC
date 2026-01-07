namespace AudioAGC.Models;

internal readonly record struct LoudnormMeasurement(
    string InputI,
    string InputTP,
    string InputLRA,
    string InputThresh,
    string TargetOffset
);

