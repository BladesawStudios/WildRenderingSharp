namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Decodes the MCPK container TotK wraps its BFRES files in.</summary>
public static class Mcpk
{
    public static byte[] ToBfres(byte[] mc, string name)
    {
        byte[] fres = McSharp.MeshCodec.DecompressMc(mc, out var status)
            ?? throw new InvalidDataException($"McSharp failed on {name}: {status}");
        if (fres.Length < 4 || fres[0] != 'F' || fres[1] != 'R' || fres[2] != 'E' || fres[3] != 'S')
            throw new InvalidDataException($"{name}: decompressed data does not start with 'FRES'");
        return fres;
    }
}
