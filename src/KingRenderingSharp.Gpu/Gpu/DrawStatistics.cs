namespace KingRenderingSharp.Gpu;

/// <summary>Counts the draw calls and binds a frame's actor passes make, for telling a frame limited by submission from one limited by shading.</summary>
internal sealed class DrawStatistics
{
    long _calls, _commands, _textureBinds, _programSwitches;

    public void Call(int commands)
    {
        _calls++;
        _commands += commands;
    }

    // The draw calls counted so far, without starting again.
    public long Calls => _calls;

    public void TextureBind() => _textureBinds++;

    public void ProgramSwitch() => _programSwitches++;

    // What has been counted since the last call, and starts again.
    public (long Calls, long Commands, long TextureBinds, long ProgramSwitches) Take()
    {
        var counts = (_calls, _commands, _textureBinds, _programSwitches);
        _calls = _commands = _textureBinds = _programSwitches = 0;
        return counts;
    }
}
