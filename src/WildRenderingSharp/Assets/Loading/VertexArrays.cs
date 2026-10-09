using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Assets.Loading;

/// <summary>Builds the vertex arrays that read a shape's buffers for a program, and for the pass-ID stamp.</summary>
sealed unsafe class VertexArrays(GL gl)
{
    // The pass-ID shader declares position and the four blend attributes, which the stamp skins with so its silhouette and depth match the real draw.
    static readonly string[] PassIdAttributes = ["aPosition", .. VertexCompactor.BlendAttributes];

    // Binds each attribute of the manifest's fixed layout at its location, for every location this linked program reads. Matched
    // by location, not name: the programs declare inputs at the exporter's locations but not always under its names.
    public uint ForProgram(uint program, ShapeBuffers buffers)
    {
        uint vao = Begin(buffers);
        var active = ActiveLocations(program);
        foreach (var entry in buffers.Layout.OrderBy(e => e.Offset))
            if (active.Contains(entry.Location))
                BindAttribute(entry, buffers.Stride);
        if (buffers.ConstantSkin)
            BindConstantSkin(name => gl.GetAttribLocation(program, name));
        gl.BindVertexArray(0);
        return vao;
    }

    // Blend indices are integers in the float attribute's bit pattern, read as Float here and unpacked with floatBitsToInt in the shader.
    public uint ForPassId(ShapeBuffers buffers)
    {
        uint vao = Begin(buffers);
        foreach (var entry in buffers.Layout)
            if (Array.IndexOf(PassIdAttributes, entry.Name) >= 0)
                BindAttribute(entry, buffers.Stride);
        if (buffers.ConstantSkin)
            BindConstantSkin(PassIdLocation);
        gl.BindVertexArray(0);
        return vao;
    }

    // The attribute locations a linked program reads; it drops the inputs its code never reads.
    public HashSet<int> ActiveLocations(uint program)
    {
        var locations = new HashSet<int>();
        gl.GetProgram(program, ProgramPropertyARB.ActiveAttributes, out int count);
        for (uint i = 0; i < count; i++)
        {
            int location = gl.GetAttribLocation(program, gl.GetActiveAttrib(program, i, out _, out _));
            if (location >= 0)
                locations.Add(location);
        }
        return locations;
    }

    static int PassIdLocation(string name) => name switch
    {
        "aBlendWeight0" => 4,
        "aBlendWeight1" => 5,
        "aBlendIndex0" => 6,
        "aBlendIndex1" => 7,
        _ => -1,
    };

    uint Begin(ShapeBuffers buffers)
    {
        uint vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffers.Vbo);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, buffers.Ibo);
        return vao;
    }

    void BindAttribute(VertexLayoutEntry entry, int stride)
    {
        gl.EnableVertexAttribArray((uint)entry.Location);
        gl.VertexAttribPointer((uint)entry.Location, entry.Components, VertexAttribPointerType.Float, false, (uint)stride, (void*)(nint)entry.Offset);
    }

    // Reads the blend attributes from the one shared vertex through a divisor no instance count reaches, so every vertex of every
    // instance reads element 0.
    void BindConstantSkin(Func<string, int> location)
    {
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, ConstantSkinBuffer.For(gl));
        for (int i = 0; i < VertexCompactor.BlendAttributes.Length; i++)
        {
            int at = location(VertexCompactor.BlendAttributes[i]);
            if (at < 0)
                continue;
            gl.EnableVertexAttribArray((uint)at);
            gl.VertexAttribPointer((uint)at, 4, VertexAttribPointerType.Float, false, 0, (void*)(nint)(i * 16));
            gl.VertexAttribDivisor((uint)at, uint.MaxValue);
        }
    }
}
