using KingRenderingSharp.Gpu;
using KingRenderingSharp.Graph.Pipeline.Drawing;
using KingRenderingSharp.Graph.Pipeline.Resources;
using KingRenderingSharp.Content.Shaders;

namespace KingRenderingSharp.Graph.Pipeline.Frame;

/// <summary>What every stage of a frame is built on.</summary>
internal sealed record StageServices(
    GpuApi Gl,
    IGameProfile Profile,
    GLResourceCache Resources,
    ShaderProgramCache Programs,
    ShapeDrawer Drawer,
    ExposureProbe Exposure,
    AssetDirectories Directories);
