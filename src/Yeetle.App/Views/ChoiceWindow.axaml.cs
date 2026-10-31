using Avalonia.Controls;

namespace Yeetle.App.Views;

// A question with a few answers and Cancel. Closes with the chosen value, or null on Cancel.
public partial class ChoiceWindow : Window
{
    public ChoiceWindow() => InitializeComponent();

    public ChoiceWindow(string title, string message, IEnumerable<(string Label, object Value)> choices) : this()
    {
        Title = title;
        MessageText.Text = message;
        foreach (var (label, value) in choices)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => Close(value);
            Choices.Children.Insert(Choices.Children.Count - 1, button);
        }
        CancelButton.Click += (_, _) => Close(null);
    }
}
