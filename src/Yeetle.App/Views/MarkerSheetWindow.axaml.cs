using Avalonia.Controls;
using Avalonia.Input.Platform;
using Yeetle.App.Resources;

namespace Yeetle.App.Views;

// The marker window: the selection's tags and markers as plain text, for copying while editing.
public partial class MarkerSheetWindow : Window
{
    public MarkerSheetWindow() => InitializeComponent();

    public MarkerSheetWindow(string text) : this()
    {
        SheetText.Text = text;
        CloseButton.Click += (_, _) => Close();
        CopyButton.Click += async (_, _) =>
        {
            if (Clipboard is not { } clipboard)
                return;
            await clipboard.SetTextAsync(text);
            CopiedText.Text = Strings.Copied;
        };
    }
}
