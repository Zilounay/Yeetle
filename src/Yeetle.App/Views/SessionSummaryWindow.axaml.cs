using Avalonia.Controls;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Views;

// Closes with Commit or Discard, or null to go back to the session.
public partial class SessionSummaryWindow : Window
{
    public SessionSummaryWindow()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => Close(null);
        DiscardButton.Click += (_, _) => Close(SummaryChoice.Discard);
        CommitButton.Click += (_, _) => Close(SummaryChoice.Commit);
    }
}
