using Avalonia.Controls;

namespace Yeetle.App.Views;

// Closes with true only when the user chose to rename.
public partial class RenamePromptWindow : Window
{
    public RenamePromptWindow()
    {
        InitializeComponent();
        KeepButton.Click += (_, _) => Close(false);
        RenameButton.Click += (_, _) => Close(true);
    }
}
