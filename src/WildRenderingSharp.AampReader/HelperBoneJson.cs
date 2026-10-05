using System.Text.Json;
using Nintendo.Aamp;
using Syroot.Maths;

namespace WildRenderingSharp.AampReader;

public static class HelperBoneJson
{
    public static string ParseToJson(byte[] bphhbBytes)
    {
        var file = AampFile.FromBinary(bphhbBytes);
        var root = file.RootNode;

        // In bphhb, the main lists are either directly under root or under root list 0x5b362b9b
        var rootList = root.Lists(0x5b362b9bu) ?? root.Lists("0x5b362b9b") ?? root;

        var result = new Dictionary<string, object?>();

        // 1. Bone list
        var bones = new List<string>();
        var boneList = rootList?.Lists("bone_list") ?? root.Lists("bone_list");
        if (boneList != null)
        {
            foreach (var obj in boneList.ParamObjects)
            {
                string? name = (obj.Params("name")?.Value ?? obj.Params(0x5e237e06u)?.Value ?? obj.ParamEntries.FirstOrDefault()?.Value)?.ToString();
                if (!string.IsNullOrEmpty(name))
                    bones.Add(name);
            }
        }
        result["bones"] = bones;

        // 2. Driver bone list
        var driverBones = new List<Dictionary<string, object?>>();
        var driverList = rootList?.Lists("driver_bone_list") ?? root.Lists("driver_bone_list");
        if (driverList != null)
        {
            foreach (var obj in driverList.ParamObjects)
            {
                var d = new Dictionary<string, object?>();
                if (obj.Params("bone_id")?.Value is int bId) d["bone_id"] = bId;
                if (obj.Params("base_bone_id")?.Value is int baseBId) d["base_bone_id"] = baseBId;

                if (obj.Params("base_translate")?.Value is Vector3F bt)
                    d["base_translate"] = new[] { bt.X, bt.Y, bt.Z };
                if (obj.Params("base_rotate")?.Value is Vector4F br)
                    d["base_rotate"] = new[] { br.X, br.Y, br.Z, br.W };
                if (obj.Params("aim_axis")?.Value is Vector3F aa)
                    d["aim_axis"] = new[] { aa.X, aa.Y, aa.Z };
                if (obj.Params("up_axis")?.Value is Vector3F ua)
                    d["up_axis"] = new[] { ua.X, ua.Y, ua.Z };

                driverBones.Add(d);
            }
        }
        result["driver_bones"] = driverBones;

        // 3. Connection curve list
        var curves = new List<Dictionary<string, object?>>();
        var curveList = rootList?.Lists("connection_curve_list") ?? root.Lists("connection_curve_list");
        if (curveList != null)
        {
            foreach (var obj in curveList.ParamObjects)
            {
                var c = new Dictionary<string, object?>();
                if (obj.Params("driver_bone_id")?.Value is int dId) c["driver_bone_id"] = dId;
                if (obj.Params("attr")?.Value is int attr) c["attr"] = attr;

                int keyNum = obj.Params("key_num")?.Value is int kn ? kn : 0;
                var keys = new List<float[]>();
                for (int k = 0; k < keyNum; k++)
                {
                    if (obj.Params($"key_{k}")?.Value is Vector4F kVal)
                    {
                        keys.Add(new[] { kVal.X, kVal.Y, kVal.Z, kVal.W });
                    }
                }
                c["keys"] = keys;
                curves.Add(c);
            }
        }
        result["connection_curves"] = curves;

        // 4. Output list
        var outputs = new List<List<int>>();
        var outList = rootList?.Lists("output_list") ?? root.Lists("output_list");
        if (outList != null)
        {
            foreach (var obj in outList.ParamObjects)
            {
                var conns = new List<int>();
                for (int c = 0; c < 8; c++)
                {
                    if (obj.Params($"connection_{c}_id")?.Value is int connId)
                    {
                        conns.Add(connId);
                    }
                }
                outputs.Add(conns);
            }
        }
        result["outputs"] = outputs;

        // 5. Driven bone list
        var drivenBones = new List<Dictionary<string, object?>>();
        var drivenList = rootList?.Lists("driven_bone_list") ?? root.Lists("driven_bone_list");
        if (drivenList != null)
        {
            foreach (var obj in drivenList.ParamObjects)
            {
                var d = new Dictionary<string, object?>();
                if (obj.Params("bone_id")?.Value is int bId) d["bone_id"] = bId;
                if (obj.Params("translate_driven_type")?.Value is int tt) d["translate_driven_type"] = tt;
                if (obj.Params("translate_driven_id")?.Value is int tid) d["translate_driven_id"] = tid;
                if (obj.Params("rotate_driven_type")?.Value is int rt) d["rotate_driven_type"] = rt;
                if (obj.Params("rotate_driven_id")?.Value is int rid) d["rotate_driven_id"] = rid;
                drivenBones.Add(d);
            }
        }
        result["driven_bones"] = drivenBones;

        // 6. Pose driven list
        var poseDriven = new List<Dictionary<string, object?>>();
        var poseList = rootList?.Lists("pose_driven_list") ?? root.Lists("pose_driven_list");
        if (poseList != null)
        {
            foreach (var obj in poseList.ParamObjects)
            {
                var p = new Dictionary<string, object?>();
                if (obj.Params("base_bone_id")?.Value is int baseBId) p["base_bone_id"] = baseBId;

                if (obj.Params("base_translate")?.Value is Vector3F bt)
                    p["base_translate"] = new[] { bt.X, bt.Y, bt.Z };
                if (obj.Params("base_rotate")?.Value is Vector4F br)
                    p["base_rotate"] = new[] { br.X, br.Y, br.Z, br.W };
                if (obj.Params("aim_axis")?.Value is Vector3F aa)
                    p["aim_axis"] = new[] { aa.X, aa.Y, aa.Z };
                if (obj.Params("up_axis")?.Value is Vector3F ua)
                    p["up_axis"] = new[] { ua.X, ua.Y, ua.Z };

                ReadBinding(obj, p, "roll");
                ReadBinding(obj, p, "bendH");
                ReadBinding(obj, p, "bendV");
                ReadBinding(obj, p, "translateX");
                ReadBinding(obj, p, "translateY");
                ReadBinding(obj, p, "translateZ");

                poseDriven.Add(p);
            }
        }
        result["pose_driven"] = poseDriven;

        return JsonSerializer.Serialize(result);
    }

    private static void ReadBinding(ParamObject obj, Dictionary<string, object?> dict, string name)
    {
        if (obj.Params($"{name}_id")?.Value is int id)
        {
            dict[$"{name}_id"] = id;
        }
        else if (obj.Params(name)?.Value is float f)
        {
            dict[name] = f;
        }
    }
}
