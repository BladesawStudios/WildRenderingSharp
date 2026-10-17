namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>Counts the draw calls and binds a frame's actor passes make, for telling a frame limited by submission from one limited by shading.</summary>
internal static class DrawStatistics
{
    static long _calls, _commands, _textureBinds, _programSwitches;

    public static void Call(int commands)
    {
        _calls++;
        _commands += commands;
    }

    // The draw calls counted so far, without starting again.
    public static long Calls => _calls;

    public static void TextureBind() => _textureBinds++;

    public static void ProgramSwitch() => _programSwitches++;

    // What has been counted since the last call, and starts again.
    public static (long Calls, long Commands, long TextureBinds, long ProgramSwitches) Take()
    {
        var counts = (_calls, _commands, _textureBinds, _programSwitches);
        _calls = _commands = _textureBinds = _programSwitches = 0;
        return counts;
    }
}
