namespace WildRenderingSharp.Hosting;

/// <summary>How one name of a <see cref="PrepareBatchRequest"/> went.</summary>
public readonly record struct PrepareOutcome(string ActorOrModelName, string? ModelName, string? Error)
{
    public bool Succeeded => ModelName is not null;
}
