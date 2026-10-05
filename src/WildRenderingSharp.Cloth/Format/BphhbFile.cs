using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using WildRenderingSharp.Cloth.Model.HelperBone;

namespace WildRenderingSharp.Cloth.Format;

/// <summary>
/// Parser and loader for Nintendo Phive Helper Bone (.bphhb) files.
/// Parsed through <see cref="IsolatedAamp"/> (the AAMP reader in its own load context), avoiding
/// Syroot version conflicts in the host application.
/// </summary>
public static class BphhbFile
{
    private static MethodInfo? _parseToJsonMethod;
    private static readonly object _lock = new();

    public static HelperBoneData FromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Helper bone file not found: {filePath}", filePath);

        byte[] bytes = File.ReadAllBytes(filePath);
        return FromBytes(bytes);
    }

    public static HelperBoneData FromBytes(byte[] bytes)
    {
        string json = ParseToJson(bytes);
        return Deserialize(json);
    }

    private static string ParseToJson(byte[] bytes)
    {
        lock (_lock)
        {
            _parseToJsonMethod ??= IsolatedAamp.GetEntryPoint("WildRenderingSharp.AampReader.HelperBoneJson", "ParseToJson");

            var result = _parseToJsonMethod.Invoke(null, [bytes]);
            return (string)(result ?? throw new InvalidOperationException("HelperBoneJson.ParseToJson returned null"));
        }
    }

    public static HelperBoneData Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var data = new HelperBoneData();

        // 1. Bones
        if (root.TryGetProperty("bones", out var bonesEl))
        {
            foreach (var b in bonesEl.EnumerateArray())
            {
                data.Bones.Add(b.GetString() ?? string.Empty);
            }
        }

        // 2. Driver bones
        if (root.TryGetProperty("driver_bones", out var driversEl))
        {
            foreach (var d in driversEl.EnumerateArray())
            {
                var driver = new DriverBone
                {
                    BoneId = d.GetProperty("bone_id").GetInt32(),
                    BaseBoneId = d.GetProperty("base_bone_id").GetInt32()
                };

                if (d.TryGetProperty("base_translate", out var bt))
                    driver.BaseTranslate = ReadVec3(bt);
                if (d.TryGetProperty("base_rotate", out var br))
                    driver.BaseRotate = ReadQuat(br);
                if (d.TryGetProperty("aim_axis", out var aa))
                    driver.AimAxis = ReadVec3(aa);
                if (d.TryGetProperty("up_axis", out var ua))
                    driver.UpAxis = ReadVec3(ua);

                data.DriverBones.Add(driver);
            }
        }

        // 3. Connection curves
        if (root.TryGetProperty("connection_curves", out var curvesEl))
        {
            foreach (var c in curvesEl.EnumerateArray())
            {
                var curve = new ConnectionCurve
                {
                    DriverBoneId = c.GetProperty("driver_bone_id").GetInt32(),
                    Attr = (DriverAttribute)c.GetProperty("attr").GetInt32()
                };

                if (c.TryGetProperty("keys", out var keysEl))
                {
                    foreach (var k in keysEl.EnumerateArray())
                    {
                        float time = k[0].GetSingle();
                        float val = k[1].GetSingle();
                        float inSlope = k[2].GetSingle();
                        float outSlope = k[3].GetSingle();
                        curve.Keys.Add(new HermiteKey(time, val, inSlope, outSlope));
                    }
                }

                data.ConnectionCurves.Add(curve);
            }
        }

        // 4. Outputs
        if (root.TryGetProperty("outputs", out var outputsEl))
        {
            foreach (var o in outputsEl.EnumerateArray())
            {
                var output = new Output();
                foreach (var id in o.EnumerateArray())
                {
                    output.ConnectionCurveIds.Add(id.GetInt32());
                }
                data.Outputs.Add(output);
            }
        }

        // 5. Driven bones
        if (root.TryGetProperty("driven_bones", out var drivenEl))
        {
            foreach (var db in drivenEl.EnumerateArray())
            {
                var driven = new DrivenBone
                {
                    BoneId = db.GetProperty("bone_id").GetInt32(),
                    TranslateDrivenType = db.TryGetProperty("translate_driven_type", out var tt) ? tt.GetInt32() : -1,
                    TranslateDrivenId = db.TryGetProperty("translate_driven_id", out var tid) ? tid.GetInt32() : -1,
                    RotateDrivenType = db.TryGetProperty("rotate_driven_type", out var rt) ? rt.GetInt32() : -1,
                    RotateDrivenId = db.TryGetProperty("rotate_driven_id", out var rid) ? rid.GetInt32() : -1
                };
                data.DrivenBones.Add(driven);
            }
        }

        // 6. Pose driven
        if (root.TryGetProperty("pose_driven", out var poseEl))
        {
            foreach (var p in poseEl.EnumerateArray())
            {
                var pose = new PoseDriven
                {
                    BaseBoneId = p.GetProperty("base_bone_id").GetInt32()
                };

                if (p.TryGetProperty("base_translate", out var bt))
                    pose.BaseTranslate = ReadVec3(bt);
                if (p.TryGetProperty("base_rotate", out var br))
                    pose.BaseRotate = ReadQuat(br);
                if (p.TryGetProperty("aim_axis", out var aa))
                    pose.AimAxis = ReadVec3(aa);
                if (p.TryGetProperty("up_axis", out var ua))
                    pose.UpAxis = ReadVec3(ua);

                pose.Roll = ReadBinding(p, "roll");
                pose.BendH = ReadBinding(p, "bendH");
                pose.BendV = ReadBinding(p, "bendV");
                pose.TranslateX = ReadBinding(p, "translateX");
                pose.TranslateY = ReadBinding(p, "translateY");
                pose.TranslateZ = ReadBinding(p, "translateZ");

                data.PoseDrivens.Add(pose);
            }
        }

        return data;
    }

    private static ChannelBinding ReadBinding(JsonElement el, string name)
    {
        if (el.TryGetProperty($"{name}_id", out var idProp))
        {
            return ChannelBinding.FromOutput(idProp.GetInt32());
        }
        if (el.TryGetProperty(name, out var valProp))
        {
            return ChannelBinding.FromConstant(valProp.GetSingle());
        }
        return ChannelBinding.FromConstant(0f);
    }

    private static Vector3 ReadVec3(JsonElement el)
    {
        return new Vector3(el[0].GetSingle(), el[1].GetSingle(), el[2].GetSingle());
    }

    private static Quaternion ReadQuat(JsonElement el)
    {
        return new Quaternion(el[0].GetSingle(), el[1].GetSingle(), el[2].GetSingle(), el[3].GetSingle());
    }
}
