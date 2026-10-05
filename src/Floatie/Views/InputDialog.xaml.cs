using System.Windows;

namespace Floatie.Views;

/// <summary>A one-line text prompt (used for rename).</summary>
public partial class InputDialog : Window
{
    public string Value => Input.Text.Trim();

    public InputDialog(string title, string prompt, string value, bool selectStem = false)
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        Input.Text = value;
        Loaded += (_, _) =>
        {
            Input.Focus();
            // Like Explorer: select the name, not the extension.
            int dot = value.LastIndexOf('.');
            if (selectStem && dot > 0) Input.Select(0, dot); else Input.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = Value.Length > 0;
}
