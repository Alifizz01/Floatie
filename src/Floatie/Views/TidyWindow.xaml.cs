using System.Windows;
using Floatie.Core;
using Microsoft.Win32;

namespace Floatie.Views;

/// <summary>Preview, apply and undo a Tidy of one folder.</summary>
public partial class TidyWindow : Window
{
    private readonly App _app;
    private List<MoveOp> _plan = new();

    public TidyWindow(App app)
    {
        InitializeComponent();
        _app = app;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        Refresh();
    }

    private string Folder => KnownFolders.Target(_app.Settings.TidyFolder);

    private void Refresh()
    {
        FolderText.Text = Folder;
        _plan = FileOrganizer.Plan(Folder, _app.Settings.TidyRules);
        PlanList.ItemsSource = _plan.Select(op => new { File = Path.GetFileName(op.Source), op.Category }).ToList();
        EmptyText.Text = _plan.Count > 0 ? "" : "Already tidy: no loose files that match a category.";
        ApplyButton.Content = _plan.Count == 1 ? "Move 1 file" : $"Move {_plan.Count} files";
        ApplyButton.IsEnabled = _plan.Count > 0;
        int last = FileOrganizer.LoadLog(_app.Store.TidyLogPath).Count;
        UndoButton.IsEnabled = last > 0;
        UndoButton.Content = last > 0 ? $"Undo last tidy ({last})" : "Undo last tidy";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder to tidy", InitialDirectory = Folder };
        if (dialog.ShowDialog(this) != true) return;
        _app.Settings.TidyFolder = dialog.FolderName;
        _app.Save();
        Refresh();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var done = FileOrganizer.Apply(_plan);
        if (done.Count > 0) FileOrganizer.SaveLog(_app.Store.TidyLogPath, done);
        if (done.Count < _plan.Count)
            MessageBox.Show(this, $"Moved {done.Count} of {_plan.Count} files. The rest are open in another app or locked; close them and run Tidy again.",
                            "Tidy", MessageBoxButton.OK, MessageBoxImage.Information);
        Refresh();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        var log = FileOrganizer.LoadLog(_app.Store.TidyLogPath);
        int restored;
        try { restored = FileOrganizer.Undo(log); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the log: whatever didn't come back can still be retried.
            MessageBox.Show(this, ex.Message, "Undo stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
            return;
        }
        File.Delete(_app.Store.TidyLogPath);
        if (restored < log.Count)
            MessageBox.Show(this, $"Restored {restored} of {log.Count} files. The others were moved or renamed since, so they were left alone.",
                            "Undo", MessageBoxButton.OK, MessageBoxImage.Information);
        Refresh();
    }
}
