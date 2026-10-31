using Avalonia.Controls;

namespace Yeetle.App.Views;

public partial class SourceFoldersWindow : Window
{
    public SourceFoldersWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }
}
