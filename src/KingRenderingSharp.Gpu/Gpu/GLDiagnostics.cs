using KingRenderingSharp.Core.Logging;

namespace KingRenderingSharp.Gpu;

/// <summary>GL error reporting that says what it actually knows.</summary>
internal static class GLDiagnostics
{
    static readonly bool VerbosePerPass = Environment.GetEnvironmentVariable("KRS_GL_TRACE") == "1";

    public static bool CheckPass(GpuApi gl, string context)
    {
        GpuPassTimer.On(gl)?.Mark(context);
        return VerbosePerPass && Check(gl, context);
    }

    // How many errors to pop before giving up - a driver in a bad state can queue them faster than a loop clears them, and
    // hanging is worse than a truncated report.
    const int MaxDrain = 32;

    public static List<GLEnum> Drain(GpuApi gl)
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

    public static bool Check(GpuApi gl, string context)
    {
        var errors = Drain(gl);
        foreach (var error in errors)
            Log.Error($"[GL] {error} during {context}");
        return errors.Count > 0;
    }

    public static bool CheckPending(GpuApi gl, string context)
    {
        var errors = Drain(gl);
        foreach (var error in errors)
            Log.Error($"[GL] {error} was already pending before {context} - raised by something earlier, not by it");
        return errors.Count > 0;
    }
}
