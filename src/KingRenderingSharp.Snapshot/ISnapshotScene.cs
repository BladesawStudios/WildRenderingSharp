using System.Numerics;
using KingRenderingSharp.Content.Assets;
using KingRenderingSharp.Content.Rendering.Cameras;

namespace KingRenderingSharp.Snapshot;

/// <summary>A prepared model of one game, drawn offscreen.</summary>
interface ISnapshotScene : IDisposable
{
    LoadedModel Model { get; }

    /// <summary>The corners of the model's bounds.</summary>
    (Vector3 Min, Vector3 Max) Bounds { get; }

    /// <summary>Draws the model and returns straight-alpha RGBA, top row first.</summary>
    byte[] Render(Camera camera, int width, int height);
}
