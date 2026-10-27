namespace Yeetle.Application.Playback;

// The speeds the player offers (spec §7) and the steps for the slower / faster keys (spec §14).
public static class PlaybackSpeeds
{
    public const double Normal = 1;

    public static IReadOnlyList<double> All { get; } = [Normal, 1.25, 1.5, 2, 3, 4];

    // Each speed with its number counting from 1, as on the number-row keys that select them.
    public static IEnumerable<(double Speed, int Number)> Numbered => All.Select((speed, index) => (speed, index + 1));

    public static double Faster(double current) => All.FirstOrDefault(s => s > current, All[^1]);

    public static double Slower(double current) => All.LastOrDefault(s => s < current, All[0]);
}
