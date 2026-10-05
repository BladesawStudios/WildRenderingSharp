using WildRenderingSharp.Shaders.Common;
using WildRenderingSharp.Shaders.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Shaders.Profiles.Totk;

public class TotkShaderProfile : IGameShaderProfile
{
    public string GameTitle => "The Legend of Zelda: Tears of the Kingdom";
    public string GameIdentifier => "TotK";

    public IUboBlock CreateContextUbo() => new ContextUbo();
    public IUboBlock CreateEnvUbo() => new EnvUbo();
    public IUboBlock CreateSceneMatUbo() => new SceneMatUbo();

    /// <summary>
    /// A material's real content is per-shading-model bytes loaded from
    /// <c>matubo/&lt;materialName&gt;.gsys_material.bin</c> (see <see cref="MaterialUbo"/>'s
    /// remarks) - the profile abstraction has no data-directory context, so this only returns an
    /// empty placeholder. Callers that actually have a data directory (<c>WildRenderingSharp</c>'s
    /// model loader) should call <see cref="MaterialUbo.LoadFromFile"/> directly instead.
    /// </summary>
    public IUboBlock CreateMaterialUbo(string materialName) => new MaterialUbo(materialName, []);

    public IUboBlock CreateShapeMatrixUbo() => ShapeMatrixUbo.BuildFromModelMatrix(IdentityRows);
    public IUboBlock CreateBonePaletteUbo() => BonePaletteUbo.FillIdentity();

    static readonly System.Numerics.Vector4[] IdentityRows =
        [new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0)];
}
