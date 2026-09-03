using SherpaManager.Models;

namespace SherpaManager.Services;

public interface IDisplayConfigurationService
{
    Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot, NvidiaSurroundMode surroundMode,
        CancellationToken cancellationToken = default);

    Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot, NvidiaSurroundMode surroundMode,
        Func<DisplaySnapshot, Task<bool>> confirm, CancellationToken cancellationToken = default,
        bool confirmOnlyWhenVerificationChanged = false);

    Task<DisplayRestoreResult> RestoreLastRecoveryAsync(CancellationToken cancellationToken = default);
}
