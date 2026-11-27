using System.ComponentModel;
using System.Globalization;

namespace Yeetle.App.Resources;

// The UI language (spec §11, decided in #20): switching it applies at once, without a restart.
// Texts in the views follow through the Tr markup extension; view models rebuild the texts they
// made when Changed is raised.
public sealed class Loc : INotifyPropertyChanged
{
    public const string English = "en";
    public const string French = "fr";

    public static Loc Instance { get; } = new();

    private Loc()
    {
    }

    // Bumped at each switch, so every Tr binding looks its text up again.
    public int Version { get; private set; }

    // "en" or "fr": what is shown now.
    public string Current { get; private set; } = English;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Raised after a switch, once the culture is in place.
    public event EventHandler? Changed;

    // Null follows the Windows display language: French for any French, English otherwise.
    public static string Resolve(string? language) => language ?? (CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == French
        ? French
        : English);

    // Sets the language for the resources, dates and numbers, on every thread.
    public void Use(string? language)
    {
        var code = Resolve(language);
        var culture = CultureInfo.GetCultureInfo(code == French ? French : English);
        // Dates and numbers keep the user's regional format when it matches the language.
        var format = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == code ? CultureInfo.CurrentCulture : culture;
        Strings.Culture = culture;
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = format;
        var changed = code != Current;
        Current = code;
        if (!changed)
            return;
        Version++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string Text(string key) => Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
}
