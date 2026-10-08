using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>GL error reporting that says what it actually knows.</summary>
/// <remarks>
/// <c>glGetError</c> pops one error off a queue GL keeps filling until someone drains it, so a lone call after an operation reports whatever was already pending and leaves further errors for the next
/// unlucky caller. That is how a texture upload came to be blamed for <c>GL_INVALID_FRAMEBUFFER_OPERATION</c>, which <c>glCompressedTexImage2D</c> cannot raise: the texture cache held the only
/// <c>glGetError</c> and drained errors the render pipeline had left behind. Use <see cref="Drain"/> to clear the queue before an operation you intend to check, and <see cref="Check"/> to report
/// everything it raised.
/// </remarks>
public static class GLDiagnostics
{
    /// <summary>Whether the fine-grained per-pass checks run. Off by default: each is a <c>glGetError</c>, which can force a driver sync. Turn it on to find which pass raised an error the frame-level bracket reported.</summary>
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

    /// <summary>Empties the error queue and returns what was in it, so a later <see cref="Check"/> reports only errors raised after this point. Reports nothing itself: at most call sites a pending error means something earlier failed.</summary>
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

    /// <summary>Reports anything already pending before <paramref name="context"/> runs, naming it as such, for bracketing a stage that wants a clean slate. An error found here was raised by something earlier.</summary>
    public static bool CheckPending(GL gl, string context)
    {
        var errors = Drain(gl);
        foreach (var error in errors)
            Console.WriteLine($"[GL] {error} was already pending before {context} - raised by something earlier, not by it");
        return errors.Count > 0;
    }
}
