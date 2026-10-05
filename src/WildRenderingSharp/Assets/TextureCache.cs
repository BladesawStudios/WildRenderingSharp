using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

public sealed class LoadedTexture
{
    public required uint Handle { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>The real romfs texture name (e.g. "Cmn_Enemy_DungeonBoss_Eye_Alb") - lets a pass identify a specific known asset by name, such as <see cref="WildRenderingSharp.Pipeline.KnownMaterialFixes"/>.</summary>
    public required string Name { get; init; }
}

/// <summary>
/// One resolved texture binding on a shape: the shader unit, the SAMPLER KEY it was bound through
/// (e.g. "_a0"/"_e0"), and the texture itself. The key is carried because a texture pattern anim
/// re-points a sampler BY KEY - that is what <c>PatternAnimInfo.Name</c> names - so the draw has to
/// know which of a shape's units corresponds to which sampler.
/// </summary>
public readonly record struct ShapeSampler(int Unit, string Key, LoadedTexture Texture);

/// <summary>
/// Loads and caches textures by name so shapes sharing a texture share one GL object - mirrors
/// <c>load_shapes</c>'s <c>tex_cache</c>. Uploads compressed block data straight to the GPU (see
/// <see cref="CompressedTextureFormat"/>'s remarks); a texture whose format this codebase doesn't
/// handle, or whose bin file is simply absent (the manifest still lists the binding so it can be
/// logged), is skipped rather than aborting the whole model - matching
/// <c>ExportTestBench</c>/<c>load_texture</c>'s existing behavior for the same gap.
/// </summary>
public sealed class TextureCache : IDisposable
{
    readonly GL _gl;
    readonly string _dataDirectory;
    readonly Dictionary<string, LoadedTexture> _byTextureName = new(StringComparer.Ordinal);

    public TextureCache(GL gl, string dataDirectory)
    {
        _gl = gl;
        _dataDirectory = dataDirectory;
    }

    /// <summary>Resolves every texture a shape's sampler list references, skipping unbound units and logging anything it can't load. Returns bindings ready to bind.</summary>
    public List<ShapeSampler> Resolve(IEnumerable<SamplerBinding> samplers)
    {
        var result = new List<ShapeSampler>();
        foreach (var s in samplers)
        {
            if (string.IsNullOrEmpty(s.File))
                continue;
            var tex = GetOrLoad(s);
            if (tex is not null)
                result.Add(new ShapeSampler(s.Unit, s.Key, tex));
        }
        return result;
    }

    /// <summary>Loads (or returns the cached) texture for one binding - public so a texture pattern anim can pull in an alternate texture no material currently binds. Cached by texture NAME, so the first binding to ask for a given texture decides its sRGB interpretation; every real pattern anim drives one sampler slot consistently, so that is the same decision either way.</summary>
    public LoadedTexture? Load(SamplerBinding s) => GetOrLoad(s);

    LoadedTexture? GetOrLoad(SamplerBinding s)
    {
        if (_byTextureName.TryGetValue(s.Texture, out var cached))
            return cached;

        var info = CompressedTextureFormat.Resolve(s.Format);
        if (info is null)
        {
            Console.WriteLine($"[TextureCache] SKIPPED '{s.Texture}': unhandled format '{s.Format}'");
            return null;
        }

        string path = Path.Combine(_dataDirectory, s.File);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[TextureCache] SKIPPED '{s.Texture}': missing file '{s.File}'");
            return null;
        }

        byte[] raw = File.ReadAllBytes(path);
        bool srgb = CompressedTextureFormat.IsSrgb(s.Format, s.Key, s.Assigned, s.Texture);

        // Start from an empty error queue so the check below can only be about THIS upload. A
        // texture pattern anim loads its alternates on demand mid-frame, right after the render
        // pipeline has run, so without this the first such upload reports whatever the pipeline
        // left pending - which is how a texture came to be blamed for an error that only
        // framebuffer operations can raise. See GLDiagnostics.
        GLDiagnostics.CheckPending(_gl, $"uploading texture '{s.Texture}'");
        var internalFormat = srgb ? info.Value.FormatSrgb : info.Value.Format;

        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);

        if (info.Value.AstcFootprint is { } footprint)
        {
            // No guaranteed desktop GL/driver support for ASTC - decode to plain RGBA on the CPU
            // and upload uncompressed rather than risk glCompressedTexImage2D silently failing on
            // hardware without GL_KHR_texture_compression_astc_ldr.
            byte[] rgba = CompressedTextureFormat.DecodeAstc(raw, s.Width, s.Height, footprint, srgb);
            unsafe
            {
                fixed (byte* ptr = rgba)
                    _gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, (uint)s.Width, (uint)s.Height, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }
        else
        {
            _gl.CompressedTexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                (uint)s.Width, (uint)s.Height, 0, new ReadOnlySpan<byte>(raw));
        }

        GLDiagnostics.Check(_gl, $"uploading texture '{s.Texture}' ({s.Format}, {s.Width}x{s.Height}, {raw.Length} bytes)");

        ApplySwizzle(s);

        // Only mip 0 is ever exported (ExportTestBench takes tex.Surfaces[0] only), and
        // glGenerateMipmap has no defined behaviour for block-compressed internal formats - so
        // this is deliberately a single mip level, filtered as such, rather than a half-built
        // mip chain.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)MapWrapMode(s.WrapU));
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)MapWrapMode(s.WrapV));

        var loaded = new LoadedTexture { Handle = handle, Width = s.Width, Height = s.Height, Name = s.Texture };
        _byTextureName[s.Texture] = loaded;
        return loaded;
    }

    /// <summary>
    /// NVN configures a per-texture component swizzle - it's a property of the game's own texture
    /// descriptor, not something the compressed format's channel count implies on its own, so
    /// desktop GL's default identity swizzle can silently disagree with what a real shader expects
    /// to read from a "missing" channel. The real, authoritative source is the TXTG container's own
    /// <c>CompSelect</c> bytes (<see cref="SamplerBinding.CompSelect"/>), applied here as a genuine
    /// per-component GL swizzle using the ORIGINAL Switch-Toolbox-derived encoding
    /// (<c>0=R,1=G,2=B,3=A,4=Zero,5=One</c>) - see <see cref="MapCompSelect"/>.
    ///
    /// This encoding is deliberately NOT the one a later Ghidra trace derived (against a general
    /// BNTX/BRTI header) and applied project-wide, which was reverted after it broke textures
    /// broadly across the corpus. Cross-checked directly against raw romfs bytes (bypassing
    /// <c>TxtgTexture</c>'s own parser) for several real textures: under THIS encoding, the
    /// overwhelming majority carry the trivial identity <c>[0,1,2,3]</c> - a genuine no-op, since
    /// applying R&lt;-R,G&lt;-G,B&lt;-B,A&lt;-A reproduces GL's own default swizzle exactly, compressed
    /// format or not - confirmed on both a known-good BC5 texture (Enemy_Drake's/
    /// Enemy_MiasmaTentacle's shared iris mask, "Cmn_Enemy_DungeonBoss_Eye_Alb") and, separately, on
    /// "Npc_Ganondorf_Miasma_Body_Gn5" (whose own always-black-albedo bug turned out NOT to be a
    /// swizzle problem at all - its comp_select really is identity; see that shape's own
    /// investigation notes). Meanwhile several real "Gn4"/"AO"-style mask textures across UNRELATED
    /// character models (Npc_Zelda_Face_Gn4, Npc_Zelda_AncientHyrule_Face_Gn4, Link_Head_AO,
    /// Npc_Ganondorf_Miasma_Face_Gn4, MiasmaTentacle_L_Soul_Gn4,
    /// Npc_Ganondorf_Mummy_Battle_Soul_Gn4) consistently carry the genuinely non-trivial
    /// <c>[0,1,1,1]</c> - "B and A both source real channel G", a standard trick for packing a
    /// second mask into a 2-channel BC5's otherwise-constant B/A reads. That the same non-default
    /// value shows up identically on unrelated assets across unrelated models is what makes this
    /// encoding trustworthy where the earlier one wasn't (which broke EVERY texture, not just the
    /// ones it was meant to fix).
    ///
    /// Falls back to the old format-based BC4-&gt;RRRR heuristic only when a manifest predates this
    /// field entirely (<see cref="SamplerBinding.CompSelect"/> is null).
    ///
    /// CONFIRMED BROKEN AND FIXED once already this session, then OVER-corrected and fixed again -
    /// two separate real regressions from this same mechanism, so read both before touching it a
    /// third time:
    ///
    /// (1) `Npc_Ganondorf_Miasma_Noise_Gn4` (used in the exact alpha-test formula gating
    /// `Botom_Back__Mt_Body_Miasma_Lower`'s visibility) is BC4 (ONE real stored channel) but carries
    /// comp_select `[0,1,1,1]` - the same "source real channel G into B and A" pattern that's
    /// genuinely correct on a 2-channel BC5 texture. Naively mapping index 1 to `GL_GREEN` on a
    /// BC4/RED-only format doesn't read real data: OpenGL's own base-internal-format conversion
    /// table defines a RED-only format's G/B slots as the constant 0 BEFORE swizzling even sees
    /// them, so the shader's G/B/A reads all silently became 0 instead of the real, intended
    /// R-channel broadcast (confirmed live via the G-buffer step debugger: a texture read that
    /// should have shown as grayscale showed as pure red instead).
    ///
    /// (2) The first fix for (1) clamped EVERY format's comp_select index to its real channel count,
    /// not just BC4's - which broke BC5 textures with the ordinary identity `[0,1,2,3]` (Zelda's
    /// hair, confirmed live): BC5's OWN natural GL fallback for its non-existent B/A channels (0 and
    /// 1) is already exactly what every real shader in this corpus expects - that's the whole reason
    /// the "Cmn_Enemy_DungeonBoss_Eye_Alb" case (BC5, comp_select `[0,1,2,3]`) was already correct
    /// with a literal, unclamped mapping. Clamping index 2/3 down to `GL_GREEN` on a BC5 texture
    /// replaced that correct (0,1) fallback with a wrong duplicated-green read instead.
    ///
    /// The actual distinction: BC4's REAL hardware/game convention broadcasts its one channel into
    /// all four slots, which is NOT what GL's own RED-format default does (GL gives (R,0,0,1), the
    /// game wants (R,R,R,R)) - so BC4 alone needs a special-cased override. BC5's natural GL default
    /// ((R,G,0,1) for a two-component format) already matches the game's own convention, so BC5 (and
    /// every genuinely 4-channel format) uses the comp_select value LITERALLY with no clamping at
    /// all, trusting GL's own per-format channel semantics to supply the right constant for any
    /// index the format doesn't physically have.
    /// </summary>
    void ApplySwizzle(SamplerBinding s)
    {
        bool isBc4 = s.Format.StartsWith("BC4", StringComparison.Ordinal);

        if (s.CompSelect is { Length: 4 } cs)
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)MapCompSelect(cs[0], isBc4));
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)MapCompSelect(cs[1], isBc4));
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)MapCompSelect(cs[2], isBc4));
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)MapCompSelect(cs[3], isBc4));
            return;
        }

        if (isBc4)
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Red);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)GLEnum.Red);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Red);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)GLEnum.Red);
        }
    }

    /// <param name="isBc4">BC4 has exactly one real stored channel, and the game's own convention for it is to broadcast that channel into every output slot - ANY comp_select index in 0-3 means "the one real channel," never a literal G/B/A. Every other format uses the index literally.</param>
    static GLEnum MapCompSelect(int v, bool isBc4) => v switch
    {
        4 => GLEnum.Zero,
        5 => GLEnum.One,
        >= 0 and <= 3 => isBc4 ? GLEnum.Red : MapRealChannel(v),
        _ => GLEnum.Red, // not observed in real data - default to the texture's own first channel rather than silently injecting a constant.
    };

    static GLEnum MapRealChannel(int v) => v switch
    {
        0 => GLEnum.Red,
        1 => GLEnum.Green,
        2 => GLEnum.Blue,
        _ => GLEnum.Alpha,
    };

    /// <summary>
    /// <see cref="SamplerBinding.WrapU"/>/<see cref="SamplerBinding.WrapV"/> carry GX2TexClamp's
    /// own names (see <c>ExportManifest.BuildSamplers</c>) - every material texture used to be
    /// uploaded with GL_REPEAT hardcoded regardless of what the game actually authored here, which
    /// is why a texture the game clamps (an eye iris scrolled by a texture-SRT anim, in
    /// particular) visibly tiled once its UV moved outside 0..1. Null (an older manifest, prepared
    /// before this field existed) keeps that same old unconditional-repeat behaviour rather than
    /// guessing. GX2 has several border-colour clamp variants (Clamp/ClampBorder/ClampHalfBorder/
    /// ClampToEdge) that desktop GL has no per-variant equivalent for without also plumbing a real
    /// border colour through - all of them collapse to GL_CLAMP_TO_EDGE here, which is the
    /// "doesn't tile" behaviour that actually matters for this bug and a reasonable stand-in for
    /// the rest. Likewise the MirrorOnce* variants collapse to GL_CLAMP_TO_EDGE rather than
    /// GL_MIRRORED_REPEAT - GX2's "mirror once" clamps after the first reflection, which reads as
    /// "don't keep tiling" for a scrolled UV far outside 0..1, not "keep mirroring forever."
    /// </summary>
    // Plain string literals, not nameof(GX2TexClamp...) - WildRenderingSharp deliberately has no
    // BfresLibrary reference at all (see CLAUDE.md: "Parses no BFRES/BFSHA of its own"); the
    // manifest's wrap_u/wrap_v strings are just GX2TexClamp's ToString() from the offline side.
    static GLEnum MapWrapMode(string? wrap) => wrap switch
    {
        null or "Wrap" => GLEnum.Repeat,
        "Mirror" => GLEnum.MirroredRepeat,
        _ => GLEnum.ClampToEdge, // Clamp, ClampBorder, ClampHalfBorder, ClampToEdge, MirrorOnce, MirrorOnceBorder, MirrorOnceHalfBorder
    };

    public void Dispose()
    {
        foreach (var tex in _byTextureName.Values)
            _gl.DeleteTexture(tex.Handle);
        _byTextureName.Clear();
    }
}
