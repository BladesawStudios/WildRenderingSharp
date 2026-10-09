using System.Buffers.Binary;
using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Sky;

namespace WildRenderingSharp.Tests;

public class UboWriterTests
{
    static readonly UboSpec Spec = new("test_block", 3, 64);

    static float FloatAt(Ubo ubo, int byteOffset) => BinaryPrimitives.ReadSingleLittleEndian(ubo.Bytes.Span[byteOffset..]);

    [Fact]
    public void ASlotAndComponentAddressSixteenBytesToTheSlotAndFourToTheComponent()
    {
        var block = new UboWriter(Spec);
        block.Set(2, 1, 7.5f);

        Assert.Equal(7.5f, FloatAt(block.ToUbo("k"), 36));
    }

    [Fact]
    public void SetXyzLeavesTheFourthComponentAsItWas()
    {
        var block = new UboWriter(Spec);
        block.Set(1, 1f, 2f, 3f, 4f);
        block.SetXyz(1, new Vector3(9f, 8f, 7f));

        Assert.Equal(new Vector4(9f, 8f, 7f, 4f), block.Get(1));
    }

    [Fact]
    public void ASetOfAVector3WithAWritesAllFourComponents()
    {
        var block = new UboWriter(Spec);
        block.Set(0, new Vector3(1f, 2f, 3f), 0.5f);

        Assert.Equal(new Vector4(1f, 2f, 3f, 0.5f), block.Get(0));
    }

    [Fact]
    public void AnIntegerIsStoredAsItsOwnBitsInAFloatComponent()
    {
        var block = new UboWriter(Spec);
        block.SetInt(0, 3, 1920);

        Assert.Equal(1920, BinaryPrimitives.ReadInt32LittleEndian(block.ToUbo("k").Bytes.Span[12..]));
    }

    [Fact]
    public void ByteOffsetsWriteConsecutiveFloats()
    {
        var block = new UboWriter(Spec);
        block.SetAt(20, 1f, 2f, 3f);
        var ubo = block.ToUbo("k");

        Assert.Equal([1f, 2f, 3f], new[] { FloatAt(ubo, 20), FloatAt(ubo, 24), FloatAt(ubo, 28) });
    }

    [Fact]
    public void AMatrixFieldHoldsTheTransposedRowsOfAMatrix()
    {
        var matrix = Matrix4x4.CreateTranslation(1f, 2f, 3f) * Matrix4x4.CreateScale(2f);
        var block = new UboWriter(Spec);
        block.Set(new UboMatrix(1, 3), matrix);

        Vector4[] expected = GpuMatrix.Rows(matrix, 3);
        Assert.Equal(expected, new[] { block.Get(1), block.Get(2), block.Get(3) });
        Assert.Equal(Vector4.Zero, block.Get(0));
    }

    [Fact]
    public void RowsOfTheWrongCountForAFieldAreRefused()
    {
        var block = new UboWriter(Spec);

        Assert.Throws<ArgumentException>(() => block.Set(new UboMatrix(0, 3), new Vector4[2]));
    }

    [Fact]
    public void AWriterStartedFromABlockChangesACopyAndNotTheBlock()
    {
        var original = new UboWriter(Spec);
        original.Set(0, 0, 1f);
        var ubo = original.ToUbo("k");

        var edit = new UboWriter(ubo);
        edit.Set(0, 0, 2f);

        Assert.Equal(1f, FloatAt(ubo, 0));
        Assert.Equal(2f, FloatAt(edit.ToUbo("k"), 0));
    }

    [Fact]
    public void ABlockMustBeAPositiveMultipleOfSixteenBytes()
    {
        Assert.Throws<ArgumentException>(() => new UboWriter(new UboSpec("bad", 0, 20)));
        Assert.Throws<ArgumentException>(() => new UboWriter(new UboSpec("empty", 0, 0)));
    }

    [Fact]
    public void ABlockOfTheWrongSizeForItsSpecIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Ubo("k", Spec, new byte[32]));
    }

    [Fact]
    public void WritingPastTheEndOfTheBlockIsRefused()
    {
        var block = new UboWriter(Spec);

        Assert.ThrowsAny<ArgumentException>(() => block.Set(4, 0, 1f));
    }

    [Fact]
    public void TheSkySizeInfoMatchesTheGamesCapture()
    {
        var (ok, worstError, worstSlot) = SkyPrecomputeVerification.VerifySizeInfoAgainstCapture();

        Assert.True(ok, $"worst relative error {worstError} at slot {worstSlot}");
    }
}
