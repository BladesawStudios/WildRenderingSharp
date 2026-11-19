using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ARB;

namespace KingRenderingSharp.Gpu;

/// <summary>The driver's GL API behind the renderer's own names: the only type that holds it, so nothing outside the Gpu project can reach the driver any other way.</summary>
/// <remarks>The members of <c>GpuApi.Generated.cs</c> and <c>GpuEnums.Generated.cs</c> are generated from the calls the renderer makes; a call it does not make yet is added there. The calls that take pointers to enums, and the extensions, are written here.</remarks>
public sealed unsafe partial class GpuApi : IDisposable
{
    private ArbClipControl? _clipControl;
    private bool _clipLooked;
    private GpuBindless? _bindless;
    private bool _bindlessLooked;

    private GpuApi(GL native) => Native = native;

    internal GL Native { get; }

    /// <summary>What this context has put on the card, by kind.</summary>
    public GpuMemory Memory { get; } = new();

    internal DrawStatistics Statistics { get; } = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _states = new();

    /// <summary>State of type <typeparamref name="T"/> that belongs to this context, created on first use and dropped with the context.</summary>
    internal T State<T>() where T : class, new() => (T)_states.GetOrAdd(typeof(T), _ => new T());

    private long _lastId;

    /// <summary>A number no other call on this context returned, for naming GPU objects.</summary>
    internal int NextId() => (int)Interlocked.Increment(ref _lastId);

    /// <summary>Loads GL through a window's context.</summary>
    public static GpuApi Create(IGLContext context) => new(GL.GetApi(context));

    internal bool IsExtensionPresent(string name) => Native.IsExtensionPresent(name);

    internal void GetProgramBinary(uint program, uint bufSize, uint* length, GLEnum* binaryFormat, void* binary) =>
        Native.GetProgramBinary(program, bufSize, length, (Silk.NET.OpenGL.GLEnum*)binaryFormat, binary);

    internal string GetActiveAttrib(uint program, uint index, out int size, out GLEnum type)
    {
        string name = Native.GetActiveAttrib(program, index, out size, out AttributeType native);
        type = (GLEnum)native;
        return name;
    }

    internal void DrawBuffers(ReadOnlySpan<GLEnum> buffers) =>
        Native.DrawBuffers(System.Runtime.InteropServices.MemoryMarshal.Cast<GLEnum, Silk.NET.OpenGL.GLEnum>(buffers));

    internal void DrawBuffers(ReadOnlySpan<DrawBufferMode> buffers) =>
        Native.DrawBuffers(System.Runtime.InteropServices.MemoryMarshal.Cast<DrawBufferMode, Silk.NET.OpenGL.GLEnum>(buffers));

    /// <summary>True when the context has ARB_clip_control; a driver that cannot list its extensions counts as lacking it.</summary>
    internal bool SupportsClipControl
    {
        get
        {
            if (!_clipLooked)
            {
                try { _clipControl = Native.TryGetExtension(out ArbClipControl ext) ? ext : null; }
                catch { _clipControl = null; }
                _clipLooked = true;
            }

            return _clipControl is not null;
        }
    }

    internal void SetClipControl(GLEnum origin, GLEnum depth) => _clipControl!.ClipControl((ARB)origin, (ARB)depth);

    /// <summary>The bindless texture calls, or null when the context has no ARB_bindless_texture.</summary>
    internal GpuBindless? Bindless
    {
        get
        {
            if (!_bindlessLooked)
            {
                _bindless = Native.TryGetExtension(out ArbBindlessTexture ext) ? new GpuBindless(ext) : null;
                _bindlessLooked = true;
            }

            return _bindless;
        }
    }

    // The driver keeps calling the delegate it was given, so it is held for as long as the context.
    private Silk.NET.OpenGL.DebugProc? _debugCallback;

    /// <summary>Has the driver report its messages through <paramref name="callback"/>.</summary>
    internal void DebugMessageCallback(DebugProc callback, nint userParam)
    {
        _debugCallback = (source, type, id, severity, length, message, user) => callback((GLEnum)source, (GLEnum)type, id, (GLEnum)severity, length, message, user);
        Native.DebugMessageCallback(_debugCallback, userParam);
    }

    public void Dispose() => Native.Dispose();
}

/// <summary>What the driver reports about one of its messages.</summary>
internal delegate void DebugProc(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam);

/// <summary>The calls of ARB_bindless_texture the merged draw makes.</summary>
internal sealed class GpuBindless
{
    private readonly ArbBindlessTexture _extension;

    internal GpuBindless(ArbBindlessTexture extension) => _extension = extension;

    internal ulong GetTextureHandle(uint texture) => _extension.GetTextureHandle(texture);

    internal void MakeTextureHandleResident(ulong handle) => _extension.MakeTextureHandleResident(handle);

    internal void MakeTextureHandleNonResident(ulong handle) => _extension.MakeTextureHandleNonResident(handle);

    internal bool IsTextureHandleResident(ulong handle) => _extension.IsTextureHandleResident(handle);
}
