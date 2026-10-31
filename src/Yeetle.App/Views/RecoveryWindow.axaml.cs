using Avalonia.Controls;

namespace Yeetle.App.Views;

public partial class RecoveryWindow : Window
{
    public RecoveryWindow()
    {
        InitializeComponent();
        QuitButton.Click += (_, _) => Close();
    }
}
