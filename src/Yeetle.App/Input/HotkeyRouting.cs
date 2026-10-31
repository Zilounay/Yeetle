using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Yeetle.App.Input;

// Whatever owns the hotkeys of a window: the keys from the settings file and what they do.
public interface IHotkeyTarget
{
    // The command for a key press, or null when the key does nothing here.
    ICommand? FindCommand(KeyPress press);
}

// Runs the hotkeys for a window. Every window the user can click in gets this, so the keys work
// whichever one has focus (the main window or the controls over the video).
public static class HotkeyRouting
{
    public static void Attach(TopLevel window, Func<IHotkeyTarget?> target)
    {
        // Read before any control handles the key, so they work wherever the focus is.
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (Find(window, target(), e) is not { } command)
                return;
            if (command.CanExecute(null))
                command.Execute(null);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        // The release is swallowed too, or a focused control would act on it: a focused button
        // clicks when Space is released, which undid the pause.
        window.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            if (Find(window, target(), e) is not null)
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private static ICommand? Find(TopLevel window, IHotkeyTarget? target, KeyEventArgs e)
    {
        // Hotkeys pause while a text field has focus (spec §14).
        if (target is null || window.FocusManager?.GetFocusedElement() is TextBox)
            return null;
        return target.FindCommand(KeyPress.From(e));
    }
}
