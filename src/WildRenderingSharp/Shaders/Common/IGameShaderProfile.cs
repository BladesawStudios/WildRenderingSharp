namespace WildRenderingSharp.Shaders.Common;

/// <summary>
/// Defines a game-specific shader profile (TotK, BotW, etc.) with its specific UBO layouts and shader collections.
/// </summary>
public interface IGameShaderProfile
{
    string GameTitle { get; }
    string GameIdentifier { get; }
    
    // Core uniform blocks
    IUboBlock CreateContextUbo();
    IUboBlock CreateEnvUbo();
    IUboBlock CreateSceneMatUbo();
    IUboBlock CreateMaterialUbo(string materialName);
    IUboBlock CreateShapeMatrixUbo();
    IUboBlock CreateBonePaletteUbo();
}
