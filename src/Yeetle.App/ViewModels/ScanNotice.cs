using Yeetle.App.Resources;
using Yeetle.Application.Library;

namespace Yeetle.App.ViewModels;

// One line saying what a scan found; empty when nothing worth mentioning happened.
public static class ScanNotice
{
    private const string Separator = " · ";
    private const string ListSeparator = ", ";
    // Beyond this, changed files are only counted.
    private const int ChangedNamesShown = 3;

    public static string Describe(ScanReport report)
    {
        var parts = new List<string>();
        if (report.Added > 0)
            parts.Add(string.Format(Strings.ScanAdded, report.Added));
        if (report.Changed.Count > 0)
            parts.Add(string.Format(Strings.ScanChanged, report.Changed.Count,
                string.Join(ListSeparator, report.Changed.Take(ChangedNamesShown).Select(Path.GetFileName))));
        if (report.Missing > 0)
            parts.Add(string.Format(Strings.ScanMissing, report.Missing));
        if (report.StillRecording == 1)
            parts.Add(Strings.StillRecordingOne);
        else if (report.StillRecording > 1)
            parts.Add(string.Format(Strings.StillRecordingMany, report.StillRecording));
        if (report.Offline.Count > 0)
            parts.Add(string.Format(Strings.ScanOffline, string.Join(ListSeparator, report.Offline.Select(f => f.Path))));
        return string.Join(Separator, parts);
    }
}
