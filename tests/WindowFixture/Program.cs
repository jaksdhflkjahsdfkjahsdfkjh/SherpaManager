namespace WindowFixture;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var windowLauncherDelay = ReadIntegerArgument(args, "--window-launcher-delay-ms=");
        if (windowLauncherDelay is not null)
        {
            RunCloseableLauncher(args, windowLauncherDelay.Value);
            return;
        }

        var launcherDelay = ReadIntegerArgument(args, "--launcher-delay-ms=");
        if (launcherDelay is not null)
        {
            Thread.Sleep(launcherDelay.Value);
            var childPath = ReadStringArgument(args, "--child-path=") ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("The fixture executable path is unavailable.");
            var child = new System.Diagnostics.ProcessStartInfo
            {
                FileName = childPath,
                UseShellExecute = false
            };
            var stateFile = ReadStringArgument(args, "--state-file=");
            if (!string.IsNullOrWhiteSpace(stateFile)) child.ArgumentList.Add("--state-file=" + stateFile);
            var startedFile = ReadStringArgument(args, "--started-file=");
            if (!string.IsNullOrWhiteSpace(startedFile)) child.ArgumentList.Add("--started-file=" + startedFile);
            if (args.Contains("--ignore-close", StringComparer.OrdinalIgnoreCase))
                child.ArgumentList.Add("--ignore-close");
            System.Diagnostics.Process.Start(child)?.Dispose();
            return;
        }

        // Models an application that exists immediately but is not ready for a
        // while: the process is there at once, its message loop is not.
        var startupDelay = ReadIntegerArgument(args, "--startup-delay-ms=");
        if (startupDelay is > 0) Thread.Sleep(startupDelay.Value);

        var ignoreClose = args.Contains("--ignore-close", StringComparer.OrdinalIgnoreCase);
        var hidden = args.Contains("--hidden", StringComparer.OrdinalIgnoreCase);
        var minimizedStateFile = ReadStringArgument(args, "--state-file=");
        var startedStateFile = ReadStringArgument(args, "--started-file=");
        if (!string.IsNullOrWhiteSpace(startedStateFile))
            File.WriteAllText(startedStateFile, Environment.ProcessId.ToString());
        using var window = new Form
        {
            Text = "Sherpa process test fixture",
            Width = 480,
            Height = 240,
            ShowInTaskbar = !hidden
        };
        if (ignoreClose) window.FormClosing += (_, e) => e.Cancel = true;
        if (hidden) window.Shown += (_, _) => window.Hide();
        if (!string.IsNullOrWhiteSpace(minimizedStateFile))
        {
            window.Resize += (_, _) =>
            {
                if (window.WindowState == FormWindowState.Minimized)
                    File.WriteAllText(minimizedStateFile, "minimized");
            };
        }
        Application.Run(window);
    }

    private static void RunCloseableLauncher(string[] args, int delayMilliseconds)
    {
        using var window = new Form
        {
            Text = "Sherpa closeable launcher fixture",
            Width = 360,
            Height = 180
        };
        using var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, delayMilliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var childPath = ReadStringArgument(args, "--child-path=") ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("The fixture executable path is unavailable.");
            var child = new System.Diagnostics.ProcessStartInfo
            {
                FileName = childPath,
                UseShellExecute = false
            };
            var startedFile = ReadStringArgument(args, "--started-file=");
            if (!string.IsNullOrWhiteSpace(startedFile)) child.ArgumentList.Add("--started-file=" + startedFile);
            System.Diagnostics.Process.Start(child)?.Dispose();
            window.Close();
        };
        window.Shown += (_, _) => timer.Start();
        Application.Run(window);
    }

    private static int? ReadIntegerArgument(IEnumerable<string> args, string prefix)
    {
        var value = ReadStringArgument(args, prefix);
        return int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : null;
    }

    private static string? ReadStringArgument(IEnumerable<string> args, string prefix) => args
        .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..];
}
