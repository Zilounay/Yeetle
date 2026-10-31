using Yeetle.Domain.Clips;
using Yeetle.Domain.Sessions;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Sessions;

// What the Tags and Markers panels change: the current clip of a review session, or a clip
// opened from the Explorer (ClipEditor).
public interface IClipEditing
{
    TagCatalog Catalog { get; }

    // Null when there is no clip to edit, e.g. after a session's last clip.
    ClipReview? CurrentReview { get; }

    void SelectTag(TagId tag);

    void DeselectTag(TagId tag);

    Marker AddMarker(TimeSpan position, Func<int, string> label);

    void RenameMarker(int number, string label);

    void MoveMarker(int number, TimeSpan position);

    void DeleteMarker(int number);
}
