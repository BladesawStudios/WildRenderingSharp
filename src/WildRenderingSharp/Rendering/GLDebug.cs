using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

using WildRenderingSharp.Logging;
namespace WildRenderingSharp.Rendering;

/// <summary>
/// Wrapper class to register OpenGL debug callbacks
/// </summary>
public class GLDebug {
    public static bool showNotificationMessages = false;
    
    /// <summary>
    /// Register debug callbacks with OpenGL, that output to the current logger
    /// </summary>
    /// <param name="gl">The OpenGL context</param>
    public static void GLInit(GL gl) {
        // The driver reports through a synchronous callback once this is on, which keeps it from queueing work ahead of the thread and costs every call after it.
        if (Environment.GetEnvironmentVariable("PRISM_GLDEBUG") is null && Environment.GetEnvironmentVariable("WRS_GLDEBUG") is null)
            return;
        gl.DebugMessageCallback(DebugProcCallback, IntPtr.Zero);
        gl.Enable(EnableCap.DebugOutput);
        gl.Enable(EnableCap.DebugOutputSynchronous);
    }

    // OpenGL debug logger callback
    public readonly static DebugProc DebugProcCallback = DebugCallback;
    private static void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        string msgStr = Marshal.PtrToStringAnsi(message, length);

        bool showMessage = true;
        switch (source) {
            case GLEnum.DebugSourceApplication:
                showMessage = false;
                break;
            default:
                showMessage = true;
                break;
        }

        // These are too verbose (often perf tips), allow filtering them out
        if (severity == GLEnum.DebugSeverityNotification) {
            showMessage = showNotificationMessages;
        }

        string msg = "";
        LogLevel lvl = LogLevel.Info;
        if (!showMessage) {
            return;
        }
        
        switch (severity) {
            case GLEnum.DontCare:
                msg = $"[DontCare] [{source}] {msgStr}";
                break;
            case GLEnum.DebugSeverityNotification:
                msg = $"Notification: [{source}] {msgStr}";
                break;
            case GLEnum.DebugSeverityHigh:
                msg = $"Error: [{source}] {msgStr}";
                lvl = LogLevel.Error;
                break;
            case GLEnum.DebugSeverityMedium:
                msg = $"Warning: [{source}] {msgStr}";
                lvl = LogLevel.Warning;
                break;
            case GLEnum.DebugSeverityLow:
                msg = $"Info: [{source}] {msgStr}";
                break;
            default:
                msg = $"[default] [{source}] {msgStr}";
                break;
        }

        switch (lvl) {
            case LogLevel.Info:    Log.Info(msg); break;
            case LogLevel.Warning: Log.Warning(msg); break;
            case LogLevel.Error:   Log.Error(msg); break;
        }
    }
}
