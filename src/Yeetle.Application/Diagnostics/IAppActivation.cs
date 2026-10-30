namespace Yeetle.Application.Diagnostics;

// Raised, on any thread, when a second launch asks this instance to come forward (spec §10).
public interface IAppActivation
{
    event EventHandler? ActivationRequested;
}
