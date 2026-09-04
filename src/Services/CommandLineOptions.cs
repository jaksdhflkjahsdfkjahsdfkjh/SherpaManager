namespace SherpaManager.Services;

/// <summary>
/// Parses the arguments Sherpa Manager understands. Unknown arguments are
/// collected rather than rejected so a future flag, or a stray argument added by
/// a shortcut, never stops the application from starting.
/// </summary>
public sealed record CommandLineOptions
{
    public const string ActivateSwitch = "--activate";

    public string? ActivateProfile { get; init; }
    public bool SmokeTest { get; init; }
    public bool ShowHelp { get; init; }
    public IReadOnlyList<string> Unknown { get; init; } = [];

    public static CommandLineOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return new CommandLineOptions();

        string? activate = null;
        var smokeTest = false;
        var showHelp = false;
        var unknown = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index]?.Trim() ?? string.Empty;
            if (argument.Length == 0) continue;

            var (name, inlineValue) = Split(argument);

            switch (name.ToLowerInvariant())
            {
                case "--activate":
                case "-a":
                case "/activate":
                    // Accept both "--activate Work" and "--activate=Work".
                    var value = inlineValue;
                    if (value is null && index + 1 < args.Count && !LooksLikeSwitch(args[index + 1]))
                        value = args[++index];
                    if (!string.IsNullOrWhiteSpace(value)) activate = value.Trim();
                    else unknown.Add(argument);
                    break;

                case "--smoke-test":
                    smokeTest = true;
                    break;

                case "--help":
                case "-h":
                case "-?":
                case "/?":
                    showHelp = true;
                    break;

                default:
                    unknown.Add(argument);
                    break;
            }
        }

        return new CommandLineOptions
        {
            ActivateProfile = activate,
            SmokeTest = smokeTest,
            ShowHelp = showHelp,
            Unknown = unknown
        };
    }

    private static (string Name, string? Value) Split(string argument)
    {
        var separator = argument.IndexOf('=');
        return separator < 0
            ? (argument, null)
            : (argument[..separator], argument[(separator + 1)..].Trim('"'));
    }

    private static bool LooksLikeSwitch(string? argument) =>
        argument is not null && argument.Length > 0 && argument[0] is '-' or '/';

    public static string HelpText =>
        """
        Sherpa Manager

          SherpaManager.exe                      Open the window.
          SherpaManager.exe --activate <profile>  Switch to a profile by name.
          SherpaManager.exe --help                Show this message.

        The profile name is matched without case sensitivity. If Sherpa Manager is
        already running, the request is handed to the running copy instead of
        starting a second one.
        """;
}
