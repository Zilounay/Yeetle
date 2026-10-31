namespace Yeetle.Application.Playback;

// Walks through a list of clips with the player: opens the current one and prepares the next
// and previous ones, so moving either way is instant. Review sessions tell it which clip to show.
public sealed class ClipNavigator(IClipPlayer player)
{
    public const int NoClip = -1;

    private IReadOnlyList<string> _clips = [];

    public IReadOnlyList<string> Clips => _clips;

    // Position in Clips, or NoClip when the list is empty.
    public int Index { get; private set; } = NoClip;

    // The position as shown to the user, counting from 1; 0 when there are no clips.
    public int Number => Index + 1;

    public string? Current => Index != NoClip ? _clips[Index] : null;

    public bool CanGoNext => Index != NoClip && Index < LastIndex;

    public bool CanGoPrevious => Index > FirstIndex;

    public event EventHandler? CurrentChanged;

    private static int FirstIndex => 0;

    private int LastIndex => _clips.Count - 1;

    public void Load(IReadOnlyList<string> clips, int start = 0)
    {
        _clips = clips;
        Index = NoClip;
        if (clips.Count > 0)
            GoTo(Math.Clamp(start, FirstIndex, LastIndex));
        else
            CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    // Opens the clip at index unless it is already the current one.
    public void Show(int index)
    {
        if (index != Index && index >= FirstIndex && index <= LastIndex)
            GoTo(index);
    }

    public void Next()
    {
        if (CanGoNext)
            GoTo(Index + 1);
    }

    public void Previous()
    {
        if (CanGoPrevious)
            GoTo(Index - 1);
    }

    private void GoTo(int index)
    {
        Index = index;
        player.Open(_clips[index]);
        // The next clip first: moving forward is the usual way.
        var neighbours = new List<string>();
        if (CanGoNext)
            neighbours.Add(_clips[index + 1]);
        if (CanGoPrevious)
            neighbours.Add(_clips[index - 1]);
        player.Prepare(neighbours);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
