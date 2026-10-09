using ShaderLibrary.CompileTool;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// ShaderLibrary's extractors for the engine's own screen shaders (<c>agl</c>), which read their SARCs from a romfs folder and take
/// zstd's dictionaries from TotkCommon's process-wide game path. They are the last place preparation hands a folder path over, and
/// they only ever read the bare ROM.
/// </summary>
static class TotkAglExtractors
{
    public static void HdrCompose(string romfsRoot, string outDir) => Run(romfsRoot, () => TestAglShader.ExtractHdrCompose(romfsRoot, outDir));

    public static void CloudShader(string romfsRoot, string outDir) => Run(romfsRoot, () => TestAglShader.ExtractCloudShader(romfsRoot, outDir));

    public static void LensFlare(string romfsRoot, string outDir) => Run(romfsRoot, () => TestAglShader.ExtractLensFlareShaders(romfsRoot, outDir));

    public static void SkyPostFx(string romfsRoot, string outDir) => Run(romfsRoot, () => TestAglShader.ExtractSkyPostFxShaders(romfsRoot, outDir));

    static void Run(string romfsRoot, Action extract)
    {
        TotkCommon.Totk.Config.GamePath = romfsRoot;
        extract();
    }
}
