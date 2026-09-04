namespace SherpaManager.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

/// <summary>
/// A parsed global hotkey such as <c>Ctrl+Alt+I</c>. Stored in profiles as text
/// so a profile file stays readable and portable between machines.
/// </summary>
public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, uint VirtualKey, string Text)
{
    public static bool TryParse(string? text, out HotkeyDefinition? hotkey, out string? error)
    {
        hotkey = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;
        string? keyName = null;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; continue;
                case "alt": modifiers |= HotkeyModifiers.Alt; continue;
                case "shift": modifiers |= HotkeyModifiers.Shift; continue;
                case "win" or "windows" or "meta": modifiers |= HotkeyModifiers.Windows; continue;
            }

            if (keyName is not null)
            {
                error = $"'{text}' names more than one key. Use one key with modifiers, such as Ctrl+Alt+I.";
                return false;
            }

            if (!TryParseKey(raw, out virtualKey))
            {
                error = $"'{raw}' is not a key Sherpa can register. Use A-Z, 0-9, or F1-F24.";
                return false;
            }
            keyName = Normalize(raw);
        }

        if (keyName is null)
        {
            error = $"'{text}' has no key. Add one, such as Ctrl+Alt+I.";
            return false;
        }

        // Shift alone is not enough: Windows would hand Sherpa ordinary typing.
        if ((modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) == 0)
        {
            error = $"'{text}' needs Ctrl, Alt, or Win so it does not capture ordinary typing.";
            return false;
        }

        hotkey = new HotkeyDefinition(modifiers, virtualKey, Format(modifiers, keyName));
        return true;
    }

    /// <summary>Canonical text for storage and display, or null when unparseable.</summary>
    public static string? Canonicalize(string? text) =>
        TryParse(text, out var hotkey, out _) ? hotkey!.Text : null;

    private static string Format(HotkeyModifiers modifiers, string keyName)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static string Normalize(string key) =>
        key.Length == 1 ? key.ToUpperInvariant() : "F" + key[1..];

    private static bool TryParseKey(string key, out uint virtualKey)
    {
        virtualKey = 0;
        if (key.Length == 1)
        {
            var character = char.ToUpperInvariant(key[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
            return false;
        }

        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') &&
            int.TryParse(key[1..], out var number) && number is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + number - 1); // VK_F1 == 0x70
            return true;
        }

        return false;
    }
}
