using Avalonia;

namespace ClipStrip;

// Card sizes, shared by the XAML and the code that centres the current card.
public static class StripLayout
{
    public const double CardWidth = 184;
    // 16:9 inside the card's padding and border (2 × 4 + 2 × 2).
    public const double ThumbnailHeight = (CardWidth - 12) * 9 / 16;
    public const double Gap = 4;

    public static Thickness CardMargin { get; } = new(Gap, 0);

    // Distance from one card's left edge to the next one's.
    public const double Pitch = CardWidth + 2 * Gap;
}
