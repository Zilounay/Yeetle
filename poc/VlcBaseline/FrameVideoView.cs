using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace VlcBaseline;

// Option B (#4): VLC decodes into our memory through its video callbacks and Avalonia draws
// the frame as a bitmap, so ordinary Avalonia controls can sit on top of the video.
public sealed class FrameVideoView : Control, IVideoSurface
{
    // Frames wider than this are scaled down by VLC, to bound the per-frame copy.
    private const uint MaxFrameWidth = 1920;

    private readonly object _gate = new();
    private readonly MediaPlayer.LibVLCVideoFormatCb _format;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanup;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;

    private MediaPlayer? _mediaPlayer;
    private IntPtr _front, _back; // VLC writes into _back; _front holds the last complete frame
    private int _width, _height, _pitch;
    private bool _frameReady;
    private int _copyPosted;
    private WriteableBitmap? _bitmap;
    private int _framesShown;

    public FrameVideoView()
    {
        // Keep the delegates in fields: VLC holds raw pointers to them.
        _format = OnFormat;
        _cleanup = OnCleanup;
        _lock = OnLock;
        _display = OnDisplay;
        ClipToBounds = true;
    }

    public MediaPlayer? MediaPlayer
    {
        get => _mediaPlayer;
        set
        {
            _mediaPlayer = value;
            if (value is null)
                return;
            value.SetVideoFormatCallbacks(_format, _cleanup);
            value.SetVideoCallbacks(_lock, null, _display);
        }
    }

    // Frames drawn since the last call, for the frames-per-second readout.
    public int TakeFramesShown() => Interlocked.Exchange(ref _framesShown, 0);

    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
        ref uint pitches, ref uint lines)
    {
        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4); // BGRA in memory
        if (width > MaxFrameWidth)
        {
            height = (uint)Math.Round(height * (double)MaxFrameWidth / width);
            width = MaxFrameWidth;
        }
        lock (_gate)
        {
            FreeBuffers();
            _width = (int)width;
            _height = (int)height;
            _pitch = _width * 4;
            _front = Marshal.AllocHGlobal(_pitch * _height);
            _back = Marshal.AllocHGlobal(_pitch * _height);
            _frameReady = false;
        }
        pitches = (uint)_pitch;
        lines = height;
        return 1; // one picture buffer
    }

    private void OnCleanup(ref IntPtr opaque)
    {
        lock (_gate)
            FreeBuffers();
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _back);
        return IntPtr.Zero;
    }

    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        lock (_gate)
        {
            (_front, _back) = (_back, _front);
            _frameReady = true;
        }
        // At most one pending copy: if the UI thread is behind, it just takes the newest frame.
        if (Interlocked.Exchange(ref _copyPosted, 1) == 0)
            Dispatcher.UIThread.Post(CopyFrame, DispatcherPriority.Render);
    }

    private void CopyFrame()
    {
        Interlocked.Exchange(ref _copyPosted, 0);
        lock (_gate)
        {
            if (!_frameReady || _front == IntPtr.Zero)
                return;
            _frameReady = false;
            if (_bitmap is null || _bitmap.PixelSize.Width != _width || _bitmap.PixelSize.Height != _height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            using var target = _bitmap.Lock();
            unsafe
            {
                if (target.RowBytes == _pitch)
                {
                    Buffer.MemoryCopy((void*)_front, (void*)target.Address, (long)_pitch * _height,
                        (long)_pitch * _height);
                }
                else
                {
                    for (var y = 0; y < _height; y++)
                        Buffer.MemoryCopy((void*)(_front + y * _pitch), (void*)(target.Address + y * target.RowBytes),
                            target.RowBytes, Math.Min(_pitch, target.RowBytes));
                }
            }
        }
        Interlocked.Increment(ref _framesShown);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_bitmap is null)
            return;
        // Letterbox: fit the frame inside the control, keeping its aspect ratio.
        var source = new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var size = new Size(source.Width * scale, source.Height * scale);
        var dest = new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size);
        context.DrawImage(_bitmap, source, dest);
    }

    private void FreeBuffers()
    {
        if (_front != IntPtr.Zero) Marshal.FreeHGlobal(_front);
        if (_back != IntPtr.Zero) Marshal.FreeHGlobal(_back);
        _front = _back = IntPtr.Zero;
    }
}
