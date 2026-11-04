using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Hosting.Preparers;

/// <summary>A request to prepare many actors or models at once - a map section's worth.</summary>
public sealed record PrepareBatchRequest(
    string RomfsRoot,
    IReadOnlyList<string> ActorOrModelNames,
    CacheLayout Cache,
    IReadOnlyList<string>? ModRomfsLayers = null,
    bool ImportAnimations = true,
    bool Force = false,
    int Parallelism = 0,
    IReadOnlyCollection<string>? AnimatedNames = null)
{
    // Whether a name in the batch is prepared with its animations: all of them when ImportAnimations is set, and only those named in AnimatedNames when it is not, so one batch
    // can hold the models that need animations and those that do not.
    public bool ImportsAnimationsOf(string name) => ImportAnimations || AnimatedNames?.Contains(name) == true;

    // A line of a batch's list file: a model's name, with a + before it when it is prepared with its animations whatever the rest of the batch is.
    public static string ListLine(string name, bool animated) => animated ? "+" + name : name;

    public static bool ParseListLine(string line, out string name)
    {
        bool animated = line.StartsWith('+');
        name = animated ? line[1..] : line;
        return animated;
    }

    public int EffectiveParallelism => Parallelism > 0 ? Parallelism : DefaultParallelism;

    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount - 1, 1, 12);
}
