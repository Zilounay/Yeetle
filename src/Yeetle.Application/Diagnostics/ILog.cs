namespace Yeetle.Application.Diagnostics;

// Text log under the data folder (spec §10). Full paths stay in it, since logs never leave the PC.
public interface ILog
{
    void Info(string message);

    void Error(string message, Exception? exception = null);
}
