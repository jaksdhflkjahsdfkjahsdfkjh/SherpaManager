using System.Collections.Specialized;
using System.ComponentModel;
using SherpaManager.Models;

namespace SherpaManager.Services;

/// <summary>
/// Keeps a profile's application flags and launch order current as the list is
/// edited.
/// </summary>
/// <remarks>
/// The flags used to be refreshed by each editor action calling the scanner, and
/// every action added later forgot to: browsing for an executable and the
/// up/down buttons both left the numbering and the duplicate markers stale.
/// Watching the list itself means a new editor action cannot forget.
/// </remarks>
public sealed class ApplicationIssueWatcher : IDisposable
{
    /// <summary>
    /// The properties the scan reads. Everything else it writes, so reacting to
    /// those would loop.
    /// </summary>
    private static readonly HashSet<string> Inputs =
    [
        nameof(LaunchApplication.Path),
        nameof(LaunchApplication.Name),
        nameof(LaunchApplication.Enabled),
        nameof(LaunchApplication.WorkingDirectory)
    ];

    private readonly IProcessService _processes;
    private readonly Action<int> _onCounted;
    private SwitchProfile? _profile;
    private bool _scanning;

    /// <param name="onCounted">Receives how many entries need attention after each scan.</param>
    public ApplicationIssueWatcher(IProcessService processes, Action<int> onCounted)
    {
        _processes = processes;
        _onCounted = onCounted;
    }

    /// <summary>
    /// Follows a profile, or nothing when <paramref name="profile"/> is null.
    /// Safe to call with the profile already being watched; it rescans.
    /// </summary>
    public void Watch(SwitchProfile? profile)
    {
        Detach();
        _profile = profile;
        if (_profile is null)
        {
            _onCounted(0);
            return;
        }

        _profile.Applications.CollectionChanged += OnCollectionChanged;
        foreach (var app in _profile.Applications) app.PropertyChanged += OnApplicationChanged;
        Rescan();
    }

    /// <summary>Rescans the watched profile, if there is one.</summary>
    public void Rescan()
    {
        if (_profile is null) return;

        // The scan writes Issue and OrderLabel back onto the entries, which
        // notifies; without this the first write would start the scan again.
        _scanning = true;
        int count;
        try { count = ApplicationIssueScanner.Apply(_profile, _processes); }
        finally { _scanning = false; }

        _onCounted(count);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var app in e.OldItems?.OfType<LaunchApplication>() ?? [])
            app.PropertyChanged -= OnApplicationChanged;
        foreach (var app in e.NewItems?.OfType<LaunchApplication>() ?? [])
        {
            app.PropertyChanged -= OnApplicationChanged;
            app.PropertyChanged += OnApplicationChanged;
        }

        // A Reset carries no items, so the whole list is reattached.
        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var app in _profile?.Applications ?? [])
            {
                app.PropertyChanged -= OnApplicationChanged;
                app.PropertyChanged += OnApplicationChanged;
            }

        Rescan();
    }

    private void OnApplicationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_scanning || e.PropertyName is null || !Inputs.Contains(e.PropertyName)) return;
        Rescan();
    }

    private void Detach()
    {
        if (_profile is null) return;
        _profile.Applications.CollectionChanged -= OnCollectionChanged;
        foreach (var app in _profile.Applications) app.PropertyChanged -= OnApplicationChanged;
        _profile = null;
    }

    public void Dispose() => Detach();
}
