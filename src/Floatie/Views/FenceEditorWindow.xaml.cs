using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Floatie.Core;
using Microsoft.Win32;

namespace Floatie.Views;

/// <summary>Create or edit a fence: name, source folder, filter, sort and colour.</summary>
public partial class FenceEditorWindow : Window
{
    public static readonly string[] Accents =
        { "#3DBFA7", "#5AB3F2", "#9A8CF2", "#F2A541", "#F2706A", "#E37DC1", "#8BC34A", "#B0B8C4" };

    private readonly FenceConfig _fence;
    private readonly List<Ellipse> _rings = new();
    private string _folder;
    private string _accent;
    private bool _settingPattern;

    /// <summary>True when the user pressed "Delete fence".</summary>
    public bool DeleteRequested { get; private set; }

    public FenceEditorWindow(FenceConfig fence, bool isNew)
    {
        InitializeComponent();
        _fence = fence;
        _folder = fence.Folder;
        _accent = fence.Accent;
        Title = isNew ? "New fence" : $"Edit fence · {fence.Title}";
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        NameBox.Text = fence.Title;
        PatternBox.Text = fence.Patterns;
        FoldersBox.IsChecked = fence.ShowFolders;

        BuildSources();
        foreach (var (name, patterns) in FilterPresets.All)
            FilterChips.Children.Add(Chip("filter", name, patterns, () =>
            {
                _settingPattern = true;
                PatternBox.Text = patterns;
                _settingPattern = false;
            }));
        MarkFilterChip();
        foreach (var order in Enum.GetValues<SortOrder>())
        {
            var chip = Chip("sort", order.ToString(), order, () => { });
            chip.IsChecked = order == fence.Sort;
            SortChips.Children.Add(chip);
        }
        foreach (var hex in Accents) Swatches.Children.Add(Swatch(hex));
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private RadioButton Chip(string group, string text, object tag, Action onPick)
    {
        var chip = new RadioButton { Content = text, GroupName = group, Tag = tag, Style = (Style)FindResource("Chip") };
        chip.Checked += (_, _) => onPick();
        return chip;
    }

    private void BuildSources()
    {
        SourceChips.Children.Clear();
        (string Name, string Token)[] known =
        {
            ("Desktop", KnownFolders.DesktopToken),
            ("Downloads", KnownFolders.DownloadsToken),
            ("Documents", KnownFolders.DocumentsToken),
        };
        foreach (var (name, token) in known)
        {
            var chip = Chip("source", name, token, () => { _folder = token; ShowSourcePath(); });
            chip.IsChecked = token == _folder;
            SourceChips.Children.Add(chip);
        }
        bool custom = known.All(k => k.Token != _folder);
        var pick = Chip("source", custom ? System.IO.Path.GetFileName(_folder.TrimEnd('\\')) + "  ✎" : "Other folder…", "", () => { });
        pick.IsChecked = custom;
        pick.Click += (_, _) => PickFolder();
        SourceChips.Children.Add(pick);
        ShowSourcePath();
    }

    private void PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Show files from…" };
        if (Directory.Exists(_folder)) dialog.InitialDirectory = _folder;
        if (dialog.ShowDialog(this) == true) _folder = dialog.FolderName;
        BuildSources();
    }

    private void ShowSourcePath() => SourcePath.Text = string.Join("  +  ", KnownFolders.Sources(_folder));

    private void OnPatternChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingPattern) MarkFilterChip();
    }

    /// <summary>Highlight the preset that matches the typed patterns, if any.</summary>
    private void MarkFilterChip()
    {
        if (FilterChips is null) return;
        foreach (RadioButton chip in FilterChips.Children)
            chip.IsChecked = string.Equals((string)chip.Tag, PatternBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private FrameworkElement Swatch(string hex)
    {
        var ring = new Ellipse { Width = 30, Height = 30, StrokeThickness = 2 };
        _rings.Add(ring);
        ring.Stroke = string.Equals(hex, _accent, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
        var dot = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) };
        var host = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
        host.Children.Add(ring);
        host.Children.Add(dot);
        host.MouseLeftButtonUp += (_, _) =>
        {
            _accent = hex;
            foreach (var r in _rings) r.Stroke = Brushes.Transparent;
            ring.Stroke = Brushes.White;
        };
        return host;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var patterns = PatternBox.Text.Trim();
        _fence.Title = name.Length > 0 ? name : KnownFolders.DisplayName(_folder);
        _fence.Folder = _folder;
        _fence.Patterns = patterns.Length > 0 ? patterns : "*";
        _fence.ShowFolders = FoldersBox.IsChecked == true;
        _fence.Sort = SortChips.Children.OfType<RadioButton>().FirstOrDefault(c => c.IsChecked == true)?.Tag is SortOrder s ? s : SortOrder.Name;
        _fence.Accent = _accent;
        DialogResult = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Delete the fence \"{_fence.Title}\"?\nYour files stay where they are.", "Delete fence",
                            MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        DeleteRequested = true;
        DialogResult = true;
    }
}
