using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yeetle.App.ViewModels;

namespace Yeetle.App.Input;

// Runs the player's hotkeys for a window. Every window the user can click in gets this, so the
// keys work whichever one has focus (the main window or the controls over the video).
public static class HotkeyRouting
{
    public static void Attach(TopLevel window, Func<PlayerViewModel?> player)
    {
        // Read before any control handles the key, so they work wherever the focus is.
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (Find(window, player(), e) is not { } command)
                return;
            if (command.CanExecute(null))
                command.Execute(null);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        // The release is swallowed too, or a focused control would act on it: a focused button
        // clicks when Space is released, which undid the pause.
        window.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            if (Find(window, player(), e) is not null)
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private static System.Windows.Input.ICommand? Find(TopLevel window, PlayerViewModel? player, KeyEventArgs e)
    {
        // Hotkeys pause while a text field has focus (spec §14).
        if (player is null || window.FocusManager?.GetFocusedElement() is TextBox)
            return null;
        return player.Hotkeys.Find(e.KeyModifiers, e.PhysicalKey, e.KeySymbol) is { } action
            ? player.Actions[action]
            : null;
    }
}
