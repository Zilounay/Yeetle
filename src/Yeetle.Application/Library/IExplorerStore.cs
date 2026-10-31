using Yeetle.Domain.Clips;
using Yeetle.Domain.Library;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Library;

// The Explorer's view of the database (spec §8). Blocking calls: ClipLibrary runs them in the
// background.
public interface IExplorerStore
{
    // Every clip except those of hidden source folders, with tags and markers.
    IReadOnlyList<LibraryClip> LoadClips();

    LibraryClip? LoadClip(ClipId clip);

    void SetDecision(IReadOnlyCollection<ClipId> clips, Decision decision);

    // Tags, markers and decision go; the clips are New again and come back in the next session.
    void Wipe(IReadOnlyCollection<ClipId> clips);

    // Saves an edit made in the clip editor, all of it in one transaction.
    void SaveEdit(ClipId clip, Decision decision, IReadOnlySet<TagId> tags, IReadOnlyList<Marker> markers);
}
