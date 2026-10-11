using BymlLibrary;
using SarcLibrary;

namespace WildRenderingSharp.Rom;

/// <summary>Reads the formats the games' files are made of straight out of a ROM.</summary>
public static class RomExtensions
{
    public static Sarc ReadSarc(this IRomAccess rom, string path) =>
        Sarc.FromBinary(new ArraySegment<byte>(rom.ReadAllBytesNested(path).ToArray()));

    public static Byml ReadByml(this IRomAccess rom, string path) => Byml.FromBinary(rom.ReadAllBytesNested(path).ToArray());
}
