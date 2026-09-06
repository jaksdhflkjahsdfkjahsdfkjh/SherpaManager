namespace SherpaManager.Models;

/// <summary>
/// One graphics adapter that is driving displays, as seen when a layout was
/// captured.
/// </summary>
/// <remarks>
/// A PC can have more than one, and which one is actually driving the screens is
/// not a detail: on a laptop with switchable graphics, and on a desktop whose
/// processor graphics are still enabled, the NVIDIA card can be present and
/// idle while the monitors hang off the other adapter. NVAPI answers about the
/// card either way, so without this the difference is invisible until a Surround
/// change fails for no visible reason.
/// </remarks>
public sealed class DisplayAdapterSnapshot
{
    /// <summary>The adapter's device path, or a LUID if Windows would not name it.</summary>
    public string DevicePath { get; set; } = string.Empty;

    /// <summary>PCI vendor id, or 0 when the path does not carry one.</summary>
    public uint VendorId { get; set; }

    /// <summary>The vendor's name, or empty when it is not one Sherpa recognises.</summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>How many active displays this adapter was driving.</summary>
    public int DisplayCount { get; set; }

    public string Describe() =>
        $"{(string.IsNullOrEmpty(Vendor) ? "Unknown graphics" : Vendor)} " +
        $"({DisplayCount} display{(DisplayCount == 1 ? string.Empty : "s")})";
}
