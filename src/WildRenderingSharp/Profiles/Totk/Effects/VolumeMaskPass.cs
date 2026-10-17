using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>
/// Fills the volume mask the way the game does: its main-field fog cylinder, and the fog it adds from sprites scattered around the camera, each drawn with the
/// game's own particle program against the scene's depth.
/// </summary>
/// <remarks>
/// The programs, the emitters' static blocks, textures and meshes come from the game's effect file (see <see cref="ParticleEmitterAsset"/>). Nothing is
/// simulated: the cylinder is one particle half way through its life, and the added fog's sprites are placed once, as an emitter that has been running a while has them.
/// </remarks>
internal sealed class VolumeMaskPass : IDisposable
{
    // The units of the samplers the programs read: the emitter's texture, and the fragment and vertex stages' custom ones, which the game fills from its callback.
    // TODO: the game's callback binds textures to the custom samplers that are not known, so both read as one. Reading which textures it binds would remove it.
    const int TextureUnit = 0, FragmentSamplerUnit = 9, VertexSamplerUnit = 20;

    const string ViewKey = "particle_view", CustomKey = "particle_custom", PluginKey = "particle_plugin";

    sealed record Layer(ParticleEmitterAsset Asset, uint Program, ParticleMesh Mesh, uint Texture, float Clock);

    readonly GL _gl;
    readonly List<Layer> _layers = [];
    readonly uint _white;

    VolumeMaskPass(GL gl, ShaderProgramCache programs, string effectsDirectory, ParticleEmitterAsset fog, ParticleEmitterAsset? addFog)
    {
        _gl = gl;
        _white = ConstantTextures.Rgba(gl, 1f, 1f, 1f, 1f);
        _layers.Add(Build(programs, effectsDirectory, fog, ParticleScatter.Single(fog.Manifest), ParticleScatter.SingleClock(fog.Manifest)));
        if (addFog is not null)
            _layers.Add(Build(programs, effectsDirectory, addFog, ParticleScatter.InCylinder(addFog.Manifest), ParticleScatter.ShapeClock(addFog.Manifest)));
    }

    // Null when the fog cylinder was not prepared.
    public static VolumeMaskPass? Create(GL gl, ShaderProgramCache programs, AssetDirectories directories) =>
        ParticleEmitterAsset.Load(directories.Decompiled, directories.Effects, ParticleEmitterManifest.VolumeMaskId) is { } fog
            ? new VolumeMaskPass(gl, programs, directories.Effects, fog, ParticleEmitterAsset.Load(directories.Decompiled, directories.Effects, ParticleEmitterManifest.AddFogId))
            : null;

    // Draws the mask and returns it, in the orientation of the G-buffer.
    public GpuTexture Run(GLResourceCache resources, RenderTargets targets, FrameSetup setup)
    {
        targets.BindColorAndDepthTarget(targets.VolumeMask, targets.GBufferDepth);
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // The mask is tested against the scene, which it must not change.
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        ClipOrigin.Game(_gl, true);

        resources.Bind(ParticleBlocks.View(setup.GameOrigin ? setup.Cam : setup.FlippedCam, ViewKey));
        resources.Bind(ParticleBlocks.Custom(CustomKey));
        _gl.BindTextureAt(FragmentSamplerUnit, _white);
        _gl.BindTextureAt(VertexSamplerUnit, _white);

        foreach (var layer in _layers)
            Draw(resources, setup, layer);

        ClipOrigin.Game(_gl, false);
        ParticleRenderState.Restore(_gl);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.DepthTest);
        GLDiagnostics.CheckPass(_gl, "volume mask");
        return targets.VolumeMask;
    }

    public void Dispose()
    {
        foreach (var layer in _layers)
        {
            _gl.ReleaseProgram(layer.Program);
            layer.Mesh.Dispose();
            if (layer.Texture != 0)
                _gl.DeleteTexture(layer.Texture);
        }
        _gl.DeleteTexture(_white);
    }

    Layer Build(ShaderProgramCache programs, string effectsDirectory, ParticleEmitterAsset asset, float[] particles, float clock)
    {
        uint program = programs.Load(ParticleEmitterManifest.ProgramName(asset.Id), patchVertex: ParticleBlocks.Rebind, patchFragment: ParticleBlocks.Rebind);
        var mesh = new ParticleMesh(_gl, asset);
        mesh.SetParticles(particles);
        return new Layer(asset, program, mesh, ParticleTexture.Load(_gl, ParticleEmitterManifest.TexturePath(effectsDirectory, asset.Id)), clock);
    }

    void Draw(GLResourceCache resources, FrameSetup setup, Layer layer)
    {
        var emitter = layer.Asset.Manifest;
        ParticleRenderState.Apply(_gl, emitter);
        Vector3 camera = setup.Cam.ViewInv.Translation;
        Vector3 position = camera + new Vector3(emitter.Translation[0], emitter.Translation[1], emitter.Translation[2]);
        resources.Bind(ParticleBlocks.Dynamic(emitter, position, layer.Clock, $"particle_dynamic_{layer.Asset.Id}"));
        resources.Bind(ParticleBlocks.Static(layer.Asset));
        _gl.UseProgram(layer.Program);
        _gl.BindTextureAt(TextureUnit, layer.Texture != 0 ? layer.Texture : _white);

        // An area loop draws the emitter again for each repeat, with its own block.
        var loop = layer.Asset.AreaLoop;
        for (int repeat = 0; repeat < (loop?.RepeatCount ?? 1); repeat++)
        {
            resources.Bind(ParticleBlocks.Plugin(loop?.Block(Matrix4x4.CreateTranslation(position), camera, setup.Cam.ViewInv, repeat), $"{PluginKey}_{layer.Asset.Id}_{repeat}"));
            layer.Mesh.Draw(position);
        }
    }
}
