using WildRenderingSharp.Shaders.Common;

namespace WildRenderingSharp.Shaders.Profiles.Botw;

public class BotwShaderProfile : IGameShaderProfile
{
    public string GameTitle => "The Legend of Zelda: Breath of the Wild";
    public string GameIdentifier => "BotW";

    public IUboBlock CreateContextUbo() => null!;
    public IUboBlock CreateEnvUbo() => null!;
    public IUboBlock CreateSceneMatUbo() => null!;
    public IUboBlock CreateMaterialUbo(string materialName) => null!;
    public IUboBlock CreateShapeMatrixUbo() => null!;
    public IUboBlock CreateBonePaletteUbo() => null!;
}
