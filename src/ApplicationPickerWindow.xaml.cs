using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SherpaManager.Models;
using SherpaManager.Services;

namespace SherpaManager;

/// <summary>
/// Picks applications the way the Start menu offers them: a search box and a list
/// of what is installed, rather than a file dialog aimed at a folder the user has
/// to know.
/// </summary>
public partial class ApplicationPickerWindow : Window
{
    private readonly List<PickerRow> _all = [];
    private CancellationTokenSource? _icons;

    /// <summary>What the caller should add. Empty when nothing was chosen.</summary>
    public IReadOnlyList<InstalledApplication> Chosen { get; private set; } = [];

    /// <summary>True when the user asked for a file dialog instead.</summary>
    public bool WantsFileDialog { get; private set; }

    /// <summary>True when the user asked for an empty row to fill in by hand.</summary>
    public bool WantsEmptyRow { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.ApplyDarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    public ApplicationPickerWindow(IReadOnlyList<InstalledApplication>? catalog = null)
    {
        InitializeComponent();

        // Reading the shortcuts takes about a tenth of a second, so it happens
        // before the window is shown; the icons behind them take a second and a
        // half, so they do not.
        var applications = catalog ?? InstalledApplicationCatalog.Scan();
        _all.AddRange(applications.Select(app => new PickerRow(app)));

        SummaryText.Text = _all.Count == 0
            ? "No installed applications were found in the Start menu. Choose a file instead."
            : $"{_all.Count} applications from the Start menu. Anything installed without a Start menu entry, or started through a protocol address, has to be added the other way.";

        Apply(string.Empty);
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            BeginLoadingIcons();
        };
        Closed += (_, _) => _icons?.Cancel();
    }

    private void Apply(string query)
    {
        var matches = _all.Where(row => InstalledApplicationCatalog.Matches(row.Application, query)).ToList();
        ResultList.ItemsSource = matches;
        if (matches.Count > 0) ResultList.SelectedIndex = 0;
        UpdateAddButton();
    }

    /// <summary>
    /// Fills the icons in behind the list. Extracting them costs roughly fifteen
    /// milliseconds each, which is unnoticeable one at a time and well over a
    /// second for a full Start menu, so the list is usable first and decorated
    /// after.
    /// </summary>
    private void BeginLoadingIcons()
    {
        _icons?.Cancel();
        _icons = new CancellationTokenSource();
        var token = _icons.Token;
        var rows = _all.ToList();

        _ = Task.Run(() =>
        {
            foreach (var row in rows)
            {
                if (token.IsCancellationRequested) return;

                var icon = ExtractIcon(row.Application.Path);
                if (icon is null) continue;

                // Frozen on the worker thread so the UI thread only has to assign it.
                icon.Freeze();
                Dispatcher.BeginInvoke(() => row.Icon = icon);
            }
        }, token);
    }

    private static ImageSource? ExtractIcon(string executablePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null) return null;

            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        // A file can be unreadable, be missing an icon, or disappear between the
        // scan and here. None of that is worth a message.
        catch { return null; }
    }

    private void UpdateAddButton()
    {
        var count = ResultList.SelectedItems.Count;
        AddButton.IsEnabled = count > 0;
        AddButton.Content = count > 1 ? $"Add {count} apps" : "Add";
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        Apply(SearchBox.Text);

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            // Down moves into the results without losing the search text, so the
            // whole window works from the keyboard.
            case Key.Down when ResultList.Items.Count > 0:
                ResultList.Focus();
                if (ResultList.SelectedIndex < 0) ResultList.SelectedIndex = 0;
                e.Handled = true;
                break;
            case Key.Enter:
                Accept();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Accept();
        e.Handled = true;
    }

    private void ResultList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();

    private void Add_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        var chosen = ResultList.SelectedItems.OfType<PickerRow>().Select(row => row.Application).ToList();
        if (chosen.Count == 0) return;

        Chosen = chosen;
        DialogResult = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        WantsFileDialog = true;
        DialogResult = true;
    }

    private void AddEmpty_Click(object sender, RoutedEventArgs e)
    {
        WantsEmptyRow = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ResultList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateAddButton();

    /// <summary>One row: the application, plus an icon that arrives later.</summary>
    public sealed class PickerRow : ObservableObject
    {
        private ImageSource? _icon;

        public PickerRow(InstalledApplication application) => Application = application;

        public InstalledApplication Application { get; }
        public string Name => Application.Name;
        public string Detail => Application.Detail;

        public ImageSource? Icon
        {
            get => _icon;
            set => SetProperty(ref _icon, value);
        }
    }
}
