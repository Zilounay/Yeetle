using System.Collections.Immutable;

namespace Yeetle.Domain.Clips;

// The marker rules of one clip (spec §7), shared by review sessions and the clip editor: markers
// stay in position order, never before 0:00, and a new one is numbered after the highest.
public static class ClipMarkers
{
    private static readonly Comparison<Marker> ByPosition = (a, b) =>
        a.Position != b.Position ? a.Position.CompareTo(b.Position) : a.Number.CompareTo(b.Number);

    // label gets the new marker's number ("Marker 3").
    public static (ImmutableList<Marker> Markers, Marker Added) Add(
        ImmutableList<Marker> markers, TimeSpan position, Func<int, string> label)
    {
        var number = markers.Count == 0 ? 1 : markers.Max(m => m.Number) + 1;
        var marker = new Marker(number, AtLeastZero(position), label(number));
        return (markers.Add(marker).Sort(ByPosition), marker);
    }

    public static ImmutableList<Marker> Rename(ImmutableList<Marker> markers, int number, string label) =>
        markers.ConvertAll(m => m.Number == number ? m with { Label = label } : m);

    public static ImmutableList<Marker> Move(ImmutableList<Marker> markers, int number, TimeSpan position) =>
        markers.ConvertAll(m => m.Number == number ? m with { Position = AtLeastZero(position) } : m).Sort(ByPosition);

    public static ImmutableList<Marker> Delete(ImmutableList<Marker> markers, int number) =>
        markers.RemoveAll(m => m.Number == number);

    private static TimeSpan AtLeastZero(TimeSpan position) => position < TimeSpan.Zero ? TimeSpan.Zero : position;
}
