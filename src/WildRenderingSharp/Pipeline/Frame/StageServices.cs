using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Pipeline.Frame;

/// <summary>What every stage of a frame is built on.</summary>
public sealed record StageServices(
    GL Gl,
    IGameProfile Profile,
    GLResourceCache Resources,
    ShaderProgramCache Programs,
    ExposureProbe Exposure,
    AssetDirectories Directories);
