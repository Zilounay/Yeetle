using Avalonia.Controls;

namespace Yeetle.App.Views;

// Closes with true only when the user chose to delete.
public partial class DeletePromptWindow : Window
{
    public DeletePromptWindow()
    {
        InitializeComponent();
        KeepButton.Click += (_, _) => Close(false);
        DeleteButton.Click += (_, _) => Close(true);
    }
}
