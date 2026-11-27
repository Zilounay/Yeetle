namespace Yeetle.App.Services;

// Closes Yeetle and starts it again, for a backup restore chosen in Settings (#20).
public interface IAppRestart
{
    void Restart();
}
