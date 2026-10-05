using System;
using System.Collections.Generic;
using System.Numerics;

namespace WildRenderingSharp.Cloth.Simulation;

/// <summary>
/// World container managing active cloth simulation instances in a scene.
/// </summary>
public sealed class ClothSimulationWorld
{
    public List<ClothInstance> Instances { get; } = new();

    public Vector3 Wind { get; set; } = Vector3.Zero;
    public float TimeScale { get; set; } = 1.0f;
    public bool IsPaused { get; set; }

    public void AddInstance(ClothInstance instance)
    {
        if (instance != null && !Instances.Contains(instance))
        {
            Instances.Add(instance);
        }
    }

    public bool RemoveInstance(ClothInstance instance)
    {
        return Instances.Remove(instance);
    }

    public void Clear()
    {
        Instances.Clear();
    }

    /// <summary>
    /// Steps all active cloth instances forward by dt seconds.
    /// </summary>
    public void Step(float dt, IReadOnlyDictionary<ClothInstance, ReadOnlyMemory<Matrix4x4>>? skeletonTransforms = null)
    {
        if (IsPaused || dt <= 0f) return;

        float effectiveDt = dt * TimeScale;

        foreach (var instance in Instances)
        {
            if (!instance.IsEnabled) continue;

            instance.Wind = Wind;

            ReadOnlySpan<Matrix4x4> transforms = ReadOnlySpan<Matrix4x4>.Empty;
            if (skeletonTransforms != null && skeletonTransforms.TryGetValue(instance, out var mem))
            {
                transforms = mem.Span;
            }
            else
            {
                transforms = instance.SkeletonTransforms;
            }

            instance.Step(effectiveDt, transforms);
        }
    }

    public void Reset()
    {
        foreach (var instance in Instances)
        {
            instance.Reset();
        }
    }
}
