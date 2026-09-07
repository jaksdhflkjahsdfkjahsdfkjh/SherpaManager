using SherpaManager.Models;

namespace SherpaManager.Services;

public interface IDisplayConfigurationService
{
    /// <summary>Reads the live topology. Never changes anything.</summary>
    DisplaySnapshot Capture();

    /// <summary>
    /// A cheap description of where the desktop currently is: the bounds of every
    /// screen and the working area. Two identical readings mean nothing has moved
    /// between them.
    /// </summary>
    string DescribeDesktopGeometry();

    Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot, NvidiaSurroundMode surroundMode,
        CancellationToken cancellationToken = default);

    Task<DisplayRestoreResult> RestoreAsync(DisplaySnapshot snapshot, NvidiaSurroundMode surroundMode,
        Func<DisplaySnapshot, Task<bool>> confirm, CancellationToken cancellationToken = default,
        bool confirmOnlyWhenVerificationChanged = false);

    Task<DisplayRestoreResult> RestoreLastRecoveryAsync(CancellationToken cancellationToken = default);
}
