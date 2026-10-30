namespace Yeetle.Application.Shell;

// Hands things over to Windows itself.
public interface IShell
{
    void OpenFolder(string folder);
}
