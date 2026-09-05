namespace SherpaManager.Models;

/// <summary>
/// An application found on this machine, ready to be added to a profile.
/// </summary>
/// <param name="Name">What the Start menu calls it.</param>
/// <param name="Path">The executable, which is what a profile entry launches.</param>
/// <param name="Arguments">Arguments the shortcut passes, usually none.</param>
/// <param name="WorkingDirectory">The directory the shortcut starts it in, if it sets one.</param>
/// <param name="Group">
/// The Start menu folder it came from, which is normally the publisher. Empty for
/// applications that sit at the top level.
/// </param>
public sealed record InstalledApplication(
    string Name,
    string Path,
    string Arguments,
    string WorkingDirectory,
    string Group)
{
    /// <summary>
    /// Where it came from and what it runs, for the second line of a row. The
    /// arguments are part of it because two shortcuts to the same executable are
    /// told apart by nothing else: a keyboard utility and its startup variant can
    /// share a name and a path and differ only by a switch.
    /// </summary>
    public string Detail
    {
        get
        {
            var target = string.IsNullOrWhiteSpace(Arguments) ? Path : $"{Path} {Arguments}";
            return string.IsNullOrWhiteSpace(Group) ? target : $"{Group}  ·  {target}";
        }
    }
}

/// <summary>What a Windows shortcut points at.</summary>
public readonly record struct ShortcutTarget(string Target, string Arguments, string WorkingDirectory);
