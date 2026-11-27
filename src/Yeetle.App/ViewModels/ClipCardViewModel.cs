using Yeetle.App.Resources;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;

namespace Yeetle.App.ViewModels;

public enum CardBadge
{
    New,
    Playing,
    Kept,
    Yeeted,
    Skipped,
}

// One card of the clip strip (spec §5): position, name, recording date and a decision badge.
public sealed class ClipCardViewModel(int number, string name, DateTime recordedAt, CardBadge badge)
{
    public static CardBadge BadgeFor(ClipReview review, bool isCurrent) => isCurrent ? CardBadge.Playing
        : review.Decision switch
        {
            Decision.Kept => CardBadge.Kept,
            Decision.Yeeted => CardBadge.Yeeted,
            _ => review.Visited ? CardBadge.Skipped : CardBadge.New,
        };

    public int Number { get; } = number;

    public string Name { get; } = name;

    public string Date { get; } = string.Format(Strings.ClipDateFormat, recordedAt);

    public CardBadge Badge { get; } = badge;

    public bool IsCurrent => Badge == CardBadge.Playing;

    public bool IsKept => Badge == CardBadge.Kept;

    public bool IsYeeted => Badge == CardBadge.Yeeted;

    public bool IsSkipped => Badge == CardBadge.Skipped;

    public string BadgeText => TextFor(Badge);

    public static string TextFor(CardBadge badge) => badge switch
    {
        CardBadge.Playing => Strings.BadgePlaying,
        CardBadge.Kept => Strings.BadgeKept,
        CardBadge.Yeeted => Strings.BadgeYeeted,
        CardBadge.Skipped => Strings.BadgeSkipped,
        _ => Strings.BadgeNew,
    };
}

// One cell of the session timeline (#68): the clip's state, the text shown on hover, and whether
// it can be jumped to (only clips already reached).
public sealed record TimelineCell(CardBadge State, string Tip, bool CanJump);
