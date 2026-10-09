using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Rendering;

/// <summary>Applies shader parameter animations by rewriting the affected materials' <c>gsys_material</c> blocks, as <c>nn::g3d2::MaterialAnimObj::ApplyTo</c> does.</summary>
public static class MaterialAnimPose
{
    public readonly record struct Playing(MaterialAnimManifest Anim, float Frame);

    // TexSrt sub-fields can't be written independently, so each group is gathered by the param's block offset and baked once.
    sealed record SrtGroup(string ParamName, Dictionary<int, uint> BitsByFieldOffset);

    public static void Apply(LoadedModel model, IReadOnlyList<Playing> playing)
    {
        foreach (var shape in model.Shapes)
        {
            if (shape.MaterialParams is not { } layout || shape.MaterialBlock.Authored.Length == 0)
                continue;

            // Copy-on-first-write: a material no anim touches never allocates or re-uploads, which is most of them on most frames.
            byte[]? buffer = null;
            Dictionary<int, SrtGroup>? srtGroups = null;

            foreach (var (anim, frame) in playing)
                foreach (var mat in anim.Materials)
                {
                    if (string.Equals(mat.Material, shape.Material, StringComparison.Ordinal))
                        ApplyTargets(shape, layout, mat, frame, ref buffer, ref srtGroups);
                }

            if (srtGroups is not null)
            {
                buffer ??= shape.MaterialBlock.Authored.ToArray();
                foreach (var (paramOffset, group) in srtGroups)
                    ApplyTexSrtGroup(layout, buffer, paramOffset, group.ParamName, group.BitsByFieldOffset);
            }

            if (buffer is not null)
                shape.MaterialBlock.Show(buffer);
            else
                shape.MaterialBlock.Restore();
        }
    }

    public static void Clear(LoadedModel model)
    {
        foreach (var shape in model.Shapes)
            shape.MaterialBlock.Restore();
    }

    static void ApplyTargets(LoadedShape shape, MaterialParamLayout layout, MaterialAnimMaterialEntry mat, float frame,
        ref byte[]? buffer, ref Dictionary<int, SrtGroup>? srtGroups)
    {
        foreach (var target in mat.Targets)
        {
            // The compiled block may not declare this parameter at all.
            if (!layout.TryGetOffset(target.Param, out int paramOffset))
                continue;

            if (target.ParamType is "TexSrt" or "TexSrtEx")
            {
                srtGroups ??= new Dictionary<int, SrtGroup>();
                if (!srtGroups.TryGetValue(paramOffset, out var group))
                    srtGroups[paramOffset] = group = new SrtGroup(target.Param, new Dictionary<int, uint>());
                // The last anim in the playing list wins for a sub-field, matching the engine's sequential ApplyTo.
                group.BitsByFieldOffset[target.ByteOffset] = target.Bits(frame);
                continue;
            }

            int offset = paramOffset + target.ByteOffset;
            if (offset < 0 || offset + 4 > shape.MaterialBlock.Authored.Length)
                continue;

            buffer ??= shape.MaterialBlock.Authored.ToArray();
            BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), target.Bits(frame));
        }
    }

    // Starts from the material's authored raw SRT, overlays the sub-fields the anims drive, re-bakes the set, and writes the 2x2 matrix and
    // translation at the parameter's offset, as BuildMaterialUbo.BuildBlock does once at export time.
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
}
