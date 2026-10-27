using LibVLCSharp.Shared;

namespace VlcBaseline;

// A control that shows a MediaPlayer's video: NativeVideoView (option A) or FrameVideoView (option B).
public interface IVideoSurface
{
    MediaPlayer? MediaPlayer { get; set; }
}
