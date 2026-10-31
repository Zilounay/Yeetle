using Avalonia.Controls;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

// Closes with the chosen FirstScanChoice, or null.
public partial class FirstScanWindow : Window
{
    public FirstScanWindow()
    {
        InitializeComponent();
        ContinueButton.Click += (_, _) => Close((DataContext as FirstScanViewModel)?.Choice);
        CancelButton.Click += (_, _) => Close(null);
    }
}
