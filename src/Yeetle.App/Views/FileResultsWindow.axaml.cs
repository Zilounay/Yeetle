using Avalonia.Controls;

namespace Yeetle.App.Views;

// Closes with true when the user asked to restore the original names.
public partial class FileResultsWindow : Window
{
    public FileResultsWindow()
    {
        InitializeComponent();
        RestoreButton.Click += (_, _) => Close(true);
        CloseButton.Click += (_, _) => Close(false);
    }
}
