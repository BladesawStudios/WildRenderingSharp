using Silk.NET.OpenGL;
using KingRenderingSharp.Pipeline.Drawing;
using KingRenderingSharp.Pipeline.Resources;
using KingRenderingSharp.Shaders;

namespace KingRenderingSharp.Pipeline.Frame;

/// <summary>What every stage of a frame is built on.</summary>
internal sealed record StageServices(
    GL Gl,
    IGameProfile Profile,
    GLResourceCache Resources,
    ShaderProgramCache Programs,
    ShapeDrawer Drawer,
    ExposureProbe Exposure,
    AssetDirectories Directories);
