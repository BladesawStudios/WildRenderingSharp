using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>
/// GL error reporting that says what it actually knows.
///
/// <c>glGetError</c> pops ONE error off a queue that GL keeps filling until someone drains it, so a
/// lone <c>GetError()</c> after an operation reports whatever was already pending - not necessarily
/// anything that operation caused - and leaves any further errors for the next unlucky caller. That
/// is exactly how a texture upload came to be blamed for <c>GL_INVALID_FRAMEBUFFER_OPERATION</c>,
/// an error <c>glCompressedTexImage2D</c> is not capable of raising: the texture cache held the only
/// <c>glGetError</c> in the codebase, so it drained errors the render pipeline had left behind and
/// attributed them to whichever texture happened to be loading.
///
/// Use <see cref="Drain"/> to clear the queue before an operation you intend to check, and
/// <see cref="Check"/> to report everything that operation itself raised.
/// </summary>
public static class GLDiagnostics
{
    /// <summary>
    /// Whether the fine-grained per-pass checks run. Off by default: each one is a
    /// <c>glGetError</c>, which can force a driver sync, and nine of them per frame is a real cost
    /// for an interactive viewer. Turn it on to find WHICH pass raised an error the frame-level
    /// bracket has already reported.
    /// </summary>
    public static bool VerbosePerPass { get; set; }
        = (Environment.GetEnvironmentVariable("WRS_GL_TRACE") ?? Environment.GetEnvironmentVariable("MARROW_GL_TRACE")) == "1";

    /// <summary>A per-pass check, skipped entirely unless <see cref="VerbosePerPass"/> is set.</summary>
    public static bool CheckPass(GL gl, string context)
    {
        Pipeline.GpuPassTimer.Current?.Mark(context);
        return VerbosePerPass && Check(gl, context);
    }

    /// <summary>How many errors to pop before giving up - a driver in a bad state can queue them faster than a loop clears them, and hanging is worse than a truncated report.</summary>
    const int MaxDrain = 32;

    /// <summary>
    /// Empties the error queue and returns what was in it, so a later <see cref="Check"/> can only
    /// report errors raised after this point. Reports nothing itself: the caller decides whether a
    /// pending error is worth mentioning, because at most call sites it means "something earlier
    /// failed", not "this failed".
    /// </summary>
    public static List<GLEnum> Drain(GL gl)
    {
        var errors = new List<GLEnum>();
        for (int i = 0; i < MaxDrain; i++)
        {
            var error = gl.GetError();
            if (error == GLEnum.NoError)
                break;
            errors.Add(error);
        }
        return errors;
    }

    /// <summary>
    /// Reports every error raised since the last drain, prefixed with <paramref name="context"/>.
    /// Returns true if any were found.
    /// </summary>
    public static bool Check(GL gl, string context)
    {
        var errors = Drain(gl);
        foreach (var error in errors)
            Console.WriteLine($"[GL] {error} during {context}");
        return errors.Count > 0;
    }

    /// <summary>
    /// Reports anything already pending BEFORE <paramref name="context"/> runs, naming it as such -
    /// for bracketing a stage that wants to know it started from a clean slate. An error found here
    /// was raised by something earlier, and saying so is the whole point.
    /// </summary>
    public static bool CheckPending(GL gl, string context)
    {
        var errors = Drain(gl);
        foreach (var error in errors)
            Console.WriteLine($"[GL] {error} was already pending before {context} - raised by something earlier, not by it");
        return errors.Count > 0;
    }
}
