namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>The particles of an emitter at one moment of its steady state, in the attributes the game's programs read them from.</summary>
/// <remarks>
/// Nothing moves: a particle sits where it was emitted, and the emitter's clock is chosen so each is at its age. A particle's age is its life's fraction
/// of the emitter's clock less its birth, so ages spread evenly across the life stand for an emitter that has been running for longer than a life.
/// </remarks>
internal static class ParticleScatter
{
    // The emitter's clock for each arrangement.
    public static float SingleClock(ParticleEmitterManifest emitter) => emitter.Life / 2f;

    public static float ShapeClock(ParticleEmitterManifest emitter) => emitter.Life;

    // One particle at the emitter's origin, half way through its life.
    public static float[] Single(ParticleEmitterManifest emitter) =>
        Particle(emitter, 0f, 0f, 0f, birth: 0f, random: 0f);

    // The particles alive at once, spread through the emitter's cylinder: the shape's radius across and its height up, centred on the emitter.
    public static float[] InCylinder(ParticleEmitterManifest emitter)
    {
        int count = Math.Max(1, (int)MathF.Round(emitter.ParticlesAlive));
        var data = new List<float>(count * ParticleMesh.ParticleFloats);
        for (int index = 0; index < count; index++)
        {
            // Low-discrepancy sequences spread them evenly and the same way every frame.
            float radius = emitter.ShapeRadius[0] * MathF.Sqrt(Sequence(index, 0.6180340f, 0.1f));
            float angle = MathF.Tau * Sequence(index, 0.7548777f, 0.3f);
            float height = (Sequence(index, 0.5698403f, 0.7f) - 0.5f) * emitter.ShapeRadius[1];
            float age = (index + 0.5f) / count * emitter.Life;
            data.AddRange(Particle(emitter, radius * MathF.Cos(angle), height, radius * MathF.Sin(angle), birth: ShapeClock(emitter) - age, random: Sequence(index, 0.3247180f, 0.5f)));
        }
        return [.. data];
    }

    static float Sequence(int index, float step, float start) => (start + (index * step)) % 1f;

    static float[] Particle(ParticleEmitterManifest emitter, float x, float y, float z, float birth, float random) =>
    [
        x, y, z, emitter.Life,
        0f, 0f, 0f, birth,
        emitter.ParticleScale[0], emitter.ParticleScale[1], emitter.ParticleScale[2], 1f,
        random, random, random, 0f,
        0f, 0f, 0f, 0f,
    ];
}
