using System.Windows;

namespace DeskMeter.App;

/// <summary>单输入框对话框（配置命名/重命名用）。</summary>
public partial class InputDialog : Window
{
    private bool _ok;

    public InputDialog(string title, string prompt, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = defaultValue;
        ValueBox.SelectAll();
        Loaded += (_, _) => ValueBox.Focus();
    }

    public static string? Ask(Window owner, string title, string prompt, string defaultValue)
    {
        var dlg = new InputDialog(title, prompt, defaultValue) { Owner = owner };
        dlg.ShowDialog();
        return dlg._ok ? dlg.ValueBox.Text.Trim() : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _ok = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
