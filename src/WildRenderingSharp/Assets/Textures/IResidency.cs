using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets.Textures;

/// <summary>What lets go of the bindless handles taken from a texture, so a texture is not deleted with its handles still resident.</summary>
internal interface IResidency
{
    void Release(GL gl, uint texture);
}
