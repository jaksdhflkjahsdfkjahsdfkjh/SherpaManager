using SherpaManager.Models;

namespace SherpaManager.Services;

public interface INvidiaSurroundService
{
    NvidiaSurroundSnapshot GetStatus();
    void ApplyConfiguration(NvidiaSurroundSnapshot snapshot, bool enabled);
}
