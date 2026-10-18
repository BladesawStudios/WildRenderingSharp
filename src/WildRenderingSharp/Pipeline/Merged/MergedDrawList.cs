using Silk.NET.OpenGL;
using System.Runtime.InteropServices;
using WildRenderingSharp.Pipeline.Drawing;

namespace WildRenderingSharp.Pipeline.Merged;

/// <summary>What one draw is made of that its shaders look up: where its instances, material and textures lie and how its palette reads.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct DrawRecord(int InstanceBase, int Stride, int PaletteVec4s, int PaletteRepeat, int MaterialBase, int TextureBase, int Flags, int Spare);

/// <summary>The draws of one pass gathered by program and geometry chunk, so each group goes out as one multi-draw.</summary>
internal sealed class MergedDrawList
{
    public sealed class Bucket(uint program, GeometryChunk chunk)
    {
        public uint Program { get; } = program;

        public GeometryChunk Chunk { get; } = chunk;

        public List<DrawCommand> Commands { get; } = [];

        public List<DrawRecord> Records { get; } = [];
    }

    // Buckets are kept between passes, empty, since the same programs and chunks come up again.
    readonly Dictionary<long, Bucket> _buckets = [];

    public bool IsEmpty { get; private set; } = true;

    public IEnumerable<Bucket> Buckets => _buckets.Values.Where(b => b.Commands.Count > 0);

    public void Add(uint program, GeometryChunk chunk, in DrawCommand command, in DrawRecord record)
    {
        long key = ((long)program << 32) | (uint)chunk.Id;
        if (!_buckets.TryGetValue(key, out var bucket))
            _buckets[key] = bucket = new Bucket(program, chunk);
        bucket.Commands.Add(command);
        bucket.Records.Add(record);
        IsEmpty = false;
    }

    public void Clear()
    {
        foreach (var bucket in _buckets.Values)
        {
            bucket.Commands.Clear();
            bucket.Records.Clear();
        }
        IsEmpty = true;
    }
}
