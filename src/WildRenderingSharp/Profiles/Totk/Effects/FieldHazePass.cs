using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Rendering.Cameras;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>
/// Veils the image in the game's field haze: the pale warm colour of its nested spheres of haze sprites, as much as the view ray crosses of them.
/// </summary>
/// <remarks>
/// The sprites are not drawn one by one. Each sphere holds a known number of them, so a ray through it is covered in proportion to its length in the
/// sphere, weighted at each point by how much of a sprite there shows: the fades the game's vertex programs give it by its distance from the camera,
/// and the soft edge its fragment programs give it where it comes near the scene's depth.
/// </remarks>
internal sealed class FieldHazePass : IDisposable
{
    // The most levels the shader holds.
    const int MaxLevels = 8;

    static readonly string FragmentSource = GlslFiles.Load("Totk/Effects/FieldHaze.frag");

    readonly GL _gl;
    readonly uint _program;
    readonly FieldHazeManifest _haze;

    FieldHazePass(GL gl, FieldHazeManifest haze)
    {
        _gl = gl;
        _haze = haze;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, FragmentSource, "field_haze");
    }

    // Null when the haze was not prepared.
    public static FieldHazePass? Create(GL gl, AssetDirectories directories) =>
        FieldHazeManifest.Load(directories.Effects) is { } haze ? new FieldHazePass(gl, haze) : null;

    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture image, Camera camera, Vector3 tint, float strength, float falloff, float skyInfluence)
    {
        float tanHalf = MathF.Tan(camera.FovRadians / 2f);
        float aspect = image.Width / (float)image.Height;
        var levels = _haze.Levels.Take(MaxLevels).ToArray();

        targets.BindColorTarget(image);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.One);
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tDepth", 0, targets.LinearDepth.Handle);
        _gl.SetFloat(_program, "uNear", camera.NearPlane);
        _gl.SetFloat(_program, "uFar", camera.FarPlane);
        _gl.Uniform2(_gl.UniformLocation(_program, "uViewScale"), aspect * tanHalf, tanHalf);
        _gl.Uniform3(_gl.UniformLocation(_program, "uColor"), _haze.Color[0] * tint.X, _haze.Color[1] * tint.Y, _haze.Color[2] * tint.Z);
        _gl.SetFloat(_program, "uStrength", strength);
        _gl.SetFloat(_program, "uFalloff", falloff);
        _gl.SetFloat(_program, "uSkyInfluence", skyInfluence);
        _gl.SetInt(_program, "uCount", levels.Length);
        _gl.Uniform3(_gl.UniformLocation(_program, "uLevels"), levels.SelectMany(Level).ToArray());
        _gl.Uniform4(_gl.UniformLocation(_program, "uBands"), levels.SelectMany(l => new[] { l.NearStart, l.NearEnd, l.FarStart, l.FarEnd }).ToArray());
        _gl.Uniform1(_gl.UniformLocation(_program, "uSoft"), levels.Select(l => l.SoftDistance).ToArray());
        resources.DrawFullscreenTriangle();
        _gl.Disable(EnableCap.Blend);
        GLDiagnostics.CheckPass(_gl, "field haze");
    }

    public void Dispose() => _gl.ReleaseProgram(_program);

    // A level's offset and radius, and how fast a ray's cover grows with its length in the sphere: the sprites' shadow area per cubic metre of the sphere.
    static float[] Level(FieldHazeLevel level)
    {
        float volume = 4f / 3f * MathF.PI * level.Radius * level.Radius * level.Radius;
        return [level.Offset, level.Radius, level.Particles * level.Area * level.Alpha / volume];
    }
}
