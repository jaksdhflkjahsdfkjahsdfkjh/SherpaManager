namespace SherpaManager.Services;

public sealed record DisplayRestoreResult(bool UsedAdjustedModes, string Message, bool Kept = true);
