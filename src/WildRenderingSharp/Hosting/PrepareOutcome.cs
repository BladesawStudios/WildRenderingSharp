namespace WildRenderingSharp.Hosting;

/// <summary>How one name of a <see cref="PrepareBatchRequest"/> went.</summary>
/// <param name="ModelName">The resolved model name on success, as <see cref="IModelPreparer.PrepareAsync"/> returns it; null on failure.</param>
/// <param name="Error">Why it failed, on one line; null on success.</param>
public readonly record struct PrepareOutcome(string ActorOrModelName, string? ModelName, string? Error)
{
    public bool Succeeded => ModelName is not null;
}
