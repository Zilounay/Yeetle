using System.Globalization;
using System.Text;
using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Library;

// The marker window's text (spec §7), for the user to copy while editing:
//
// ### 2026-10-11 22-15-36.mp4 ###
// Tags: Counter-Strike 2, Dust2, M4, Funny
// Markers:
// 1:27  Smoke kill
public static class MarkerSheet
{
    private const string NameFrame = "###";
    private const string TagSeparator = ", ";
    private const string LabelSeparator = "  ";
    private const string MinutesAndSeconds = @"m\:ss";
    private const string HoursMinutesAndSeconds = @"h\:mm\:ss";

    // The labels come from the app's language; "Tags:" and "Markers:" in English.
    public static string Write(IEnumerable<LibraryClip> clips, TagVocabulary vocabulary, string tagsLabel, string markersLabel)
    {
        var text = new StringBuilder();
        foreach (var clip in clips)
        {
            if (text.Length > 0)
                text.AppendLine();
            text.AppendLine($"{NameFrame} {clip.FileName} {NameFrame}");
            var tags = vocabulary.NamesOf(clip.Tags);
            text.AppendLine($"{tagsLabel} {string.Join(TagSeparator, tags)}".TrimEnd());
            text.AppendLine(markersLabel);
            foreach (var marker in clip.Markers)
                text.AppendLine(Time(marker.Position) + LabelSeparator + marker.Label);
        }
        return text.ToString();
    }

    private static string Time(TimeSpan time) =>
        time.ToString(time >= TimeSpan.FromHours(1) ? HoursMinutesAndSeconds : MinutesAndSeconds, CultureInfo.InvariantCulture);
}
