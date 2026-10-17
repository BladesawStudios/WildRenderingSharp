using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>
/// An emitter's area loop plugin: the emitter is drawn several times, each with a growing offset, and its program wraps every particle into a box that
/// follows the camera, so a few particles fill the space around the viewer.
/// </summary>
/// <remarks>The payload is the plugin chunk of the emitter in the effect file, and the draw block is what the engine's area loop uploads before each repeat.</remarks>
internal sealed class AreaLoop
{
    readonly Vector3 _repeatOffset, _areaSize, _areaPosition, _alphaRatio, _areaRotation;
    readonly float _repeatNumber, _clippingType, _isCameraLoop, _clippingHeight;

    public AreaLoop(byte[] payload)
    {
        float Single(int at) => BitConverter.ToSingle(payload, at);
        Vector3 Vector(int at) => new(Single(at), Single(at + 4), Single(at + 8));
        _repeatOffset = Vector(0x00);
        _repeatNumber = Single(0x0C);
        _areaSize = Vector(0x10);
        _areaPosition = Vector(0x20);
        _clippingType = BitConverter.ToInt32(payload, 0x2C);
        _alphaRatio = Vector(0x30);
        _isCameraLoop = Single(0x3C);
        _areaRotation = Vector(0x40);
        _clippingHeight = payload.Length > 0x4C ? Single(0x4C) : 0f;
    }

    public int RepeatCount => (int)(_repeatNumber + 1);

    // The twelve rows of the plugin block for one repeat: the offset, the alpha ratio, the box's matrix and its inverse, the clipping, and the box's size.
    // The box sits at the camera when the loop follows it, with its position turned by the camera's orientation, and at the emitter otherwise.
    public float[] Block(Matrix4x4 emitterTransform, Vector3 camera, Matrix4x4 cameraOrientation, int repeat)
    {
        Matrix4x4 box = Matrix4x4.CreateRotationX(_areaRotation.X) * Matrix4x4.CreateRotationY(_areaRotation.Y) * Matrix4x4.CreateRotationZ(_areaRotation.Z);
        Matrix4x4 world;
        if (_isCameraLoop == 0f)
        {
            box.Translation += _areaPosition;
            world = box * emitterTransform;
        }
        else
        {
            box.Translation += Vector3.TransformNormal(_areaPosition, cameraOrientation);
            world = box * Matrix4x4.CreateTranslation(camera);
        }
        Matrix4x4.Invert(world, out Matrix4x4 inverse);

        float[] rows = new float[12 * 4];
        Set(rows, 0, _repeatOffset * repeat, _isCameraLoop);
        Set(rows, 1, _alphaRatio, 0f);
        SetMatrix(rows, 2, world);
        SetMatrix(rows, 6, inverse);
        rows[(10 * 4) + 0] = _clippingType;
        rows[(10 * 4) + 1] = _clippingHeight;
        Set(rows, 11, _areaSize, 0f);
        return rows;
    }

    static void Set(float[] rows, int row, Vector3 value, float w)
    {
        rows[(row * 4) + 0] = value.X;
        rows[(row * 4) + 1] = value.Y;
        rows[(row * 4) + 2] = value.Z;
        rows[(row * 4) + 3] = w;
    }

    static void SetMatrix(float[] rows, int row, Matrix4x4 m)
    {
        Set(rows, row, new Vector3(m.M11, m.M12, m.M13), m.M14);
        Set(rows, row + 1, new Vector3(m.M21, m.M22, m.M23), m.M24);
        Set(rows, row + 2, new Vector3(m.M31, m.M32, m.M33), m.M34);
        Set(rows, row + 3, new Vector3(m.M41, m.M42, m.M43), m.M44);
    }
}
