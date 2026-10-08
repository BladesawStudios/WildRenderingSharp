using WildRenderingSharp.Assets;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// Applies shader parameter animations by rewriting the affected materials' <c>gsys_material</c> uniform blocks - which is exactly
/// what <c>nn::g3d2::MaterialAnimObj::ApplyTo</c> (Ghidra 0x7100080894) does: evaluate each curve, then copy the resulting 4-byte
/// word into the material's parameter block.
/// </summary>
public static class MaterialAnimPose
{
    /// <summary>One anim and the frame to sample it at.</summary>
    public readonly record struct Playing(MaterialAnimManifest Anim, float Frame);

    public static void Apply(GL gl, LoadedModel model, IReadOnlyList<Playing> playing)
    {
        var patched = new HashSet<string>(StringComparer.Ordinal);

        foreach (var shape in model.Shapes)
        {
            if (shape.MaterialParams is not { } layout || shape.MaterialBytes.Length == 0)
                continue;

            byte[]? buffer = null;
            // A TexSrt parameter's sub-fields can't be written independently (see this class's own
            // remarks and TexSrtBake) - collect every target that lands on one, keyed by the
            // param's block offset, and bake them together once all playing anims are gathered.
            Dictionary<int, (string paramName, Dictionary<int, uint> bitsByFieldOffset)>? srtGroups = null;

            foreach (var (anim, frame) in playing)
            {
                foreach (var mat in anim.Materials)
                {
                    if (!string.Equals(mat.Material, shape.Material, StringComparison.Ordinal))
                        continue;
                    foreach (var target in mat.Targets)
                    {
                        if (!layout.TryGetOffset(target.Param, out int paramOffset))
                            continue; // the compiled block does not declare this parameter at all

                        if (target.ParamType is "TexSrt" or "TexSrtEx")
                        {
                            srtGroups ??= new Dictionary<int, (string, Dictionary<int, uint>)>();
                            if (!srtGroups.TryGetValue(paramOffset, out var group))
                            {
                                group = (target.Param, new Dictionary<int, uint>());
                                srtGroups[paramOffset] = group;
                            }
                            // Last anim in the playing list wins for a given sub-field, matching
                            // the plain-parameter path below and the engine's own sequential ApplyTo.
                            group.bitsByFieldOffset[target.ByteOffset] = target.Bits(frame);
                            continue;
                        }

                        int offset = paramOffset + target.ByteOffset;
                        if (offset < 0 || offset + 4 > shape.MaterialBytes.Length)
                            continue;

                        // Copy-on-first-write: a material no anim actually touches never allocates
                        // and never re-uploads, which is most of them on most frames.
                        buffer ??= (byte[])shape.MaterialBytes.Clone();
                        BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), target.Bits(frame));
                    }
                }
            }

            if (srtGroups is not null)
            {
                buffer ??= (byte[])shape.MaterialBytes.Clone();
                foreach (var (paramOffset, group) in srtGroups)
                    ApplyTexSrtGroup(layout, buffer, paramOffset, group.paramName, group.bitsByFieldOffset);
            }

            if (buffer is not null)
            {
                UploadBlock(gl, shape.MaterialBuffer, buffer);
                patched.Add(shape.Name);
            }
            else if (shape.MaterialIsPatched)
            {
                UploadBlock(gl, shape.MaterialBuffer, shape.MaterialBytes);
            }
        }

        foreach (var shape in model.Shapes)
            shape.MaterialIsPatched = patched.Contains(shape.Name);
    }

    // Fills in the six raw TexSrt sub-fields from the material's own authored baseline (RawSrt), overlays whichever ones the
    // playing anims actually drive this frame, re-bakes the whole set, and writes the 24 meaningful bytes (a 2x2 matrix +
    // translation) at the parameter's block offset - mirroring exactly what the offline static overlay does in
    // BuildMaterialUbo.BuildBlock, just per-frame instead of once at export time.
    static void ApplyTexSrtGroup(MaterialParamLayout layout, byte[] buffer, int paramOffset, string paramName, Dictionary<int, uint> bitsByFieldOffset)
    {
        int mode = 0;
        float sx = 1f, sy = 1f, rot = 0f, tx = 0f, ty = 0f;
        if (layout.TryGetEntry(paramName, out var entry) && entry.RawSrt is { Length: 6 } raw)
        {
            mode = (int)raw[0]; sx = raw[1]; sy = raw[2]; rot = raw[3]; tx = raw[4]; ty = raw[5];
        }

        foreach (var (fieldOffset, bits) in bitsByFieldOffset)
        {
            switch (fieldOffset)
            {
                case 0: mode = unchecked((int)bits); break;
                case 4: sx = BitConverter.UInt32BitsToSingle(bits); break;
                case 8: sy = BitConverter.UInt32BitsToSingle(bits); break;
                case 12: rot = BitConverter.UInt32BitsToSingle(bits); break;
                case 16: tx = BitConverter.UInt32BitsToSingle(bits); break;
                case 20: ty = BitConverter.UInt32BitsToSingle(bits); break;
            }
        }

        Span<float> m0 = stackalloc float[4];
        Span<float> m1 = stackalloc float[2];
        TexSrtBake.Bake(mode, sx, sy, rot, tx, ty, m0, m1);

        if (paramOffset < 0 || paramOffset + 24 > buffer.Length)
            return;
        for (int i = 0; i < 4; i++)
            BitConverter.TryWriteBytes(buffer.AsSpan(paramOffset + i * 4, 4), m0[i]);
        for (int i = 0; i < 2; i++)
            BitConverter.TryWriteBytes(buffer.AsSpan(paramOffset + 16 + i * 4, 4), m1[i]);
    }

    public static void Clear(GL gl, LoadedModel model)
    {
        foreach (var shape in model.Shapes)
        {
            if (!shape.MaterialIsPatched)
                continue;
            UploadBlock(gl, shape.MaterialBuffer, shape.MaterialBytes);
            shape.MaterialIsPatched = false;
        }
    }

    // Uploads just the material's own bytes, leaving the rest of the padded buffer alone - the buffer is allocated at a fixed
    // 65536 bytes so any shading model's declared block size fits (see CreatePaddedUniformBuffer), and re-sending all of that
    // every frame for a few hundred bytes of real change would be pure waste.
    static unsafe void UploadBlock(GL gl, uint handle, byte[] bytes)
    {
        gl.BindBuffer(BufferTargetARB.UniformBuffer, handle);
        fixed (byte* ptr = bytes)
            gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)bytes.Length, ptr);
    }
}
