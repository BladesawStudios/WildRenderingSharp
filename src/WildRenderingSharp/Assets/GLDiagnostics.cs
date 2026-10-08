using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

/// <summary>GL error reporting that says what it actually knows.</summary>
public static class GLDiagnostics
{
    public static bool VerbosePerPass { get; set; }
        = (Environment.GetEnvironmentVariable("WRS_GL_TRACE") ?? Environment.GetEnvironmentVariable("MARROW_GL_TRACE")) == "1";

    public static bool CheckPass(GL gl, string context)
    {
        Pipeline.GpuPassTimer.Current?.Mark(context);
        return VerbosePerPass && Check(gl, context);
    }

    // How many errors to pop before giving up - a driver in a bad state can queue them faster than a loop clears them, and
    // hanging is worse than a truncated report.
    const int MaxDrain = 32;

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

    public static bool Check(GL gl, string context)
    {
        var errors = Drain(gl);
        foreach (var error in errors)
            Console.WriteLine($"[GL] {error} during {context}");
        return errors.Count > 0;
    }

    public static bool CheckPending(GL gl, string context)
    {
        var errors = Drain(gl);
        foreach (var error in errors)
            Console.WriteLine($"[GL] {error} was already pending before {context} - raised by something earlier, not by it");
        return errors.Count > 0;
    }
}
