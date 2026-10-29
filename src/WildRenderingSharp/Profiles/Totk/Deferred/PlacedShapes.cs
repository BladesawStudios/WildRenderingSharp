using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>The shapes of the frame's models that pass a test, grouped by what places them, for a pass that draws shapes the frame's own lists leave out.</summary>
/// <remarks>
/// The frame's groups hold what its ordinary passes draw, without the hall a shrine's doorway shows (<see cref="LoadedShape.PortalOnly"/>) and without some
/// see-through shapes, so the models themselves are asked: each batch with visible instances, and each posed actor. An enabled shape the viewer has not hidden
/// is offered to the test.
/// </remarks>
internal static class PlacedShapes
{
    public static List<ActorDrawGroup> Of(FrameContext frame, IGameProfile profile, Func<LoadedShape, bool> include)
    {
        List<LoadedShape> Of(LoadedModel model) => model.Shapes.Where(s => s.Enabled && !s.Hidden && include(s)).ToList();

        List<ActorDrawGroup> groups = [];
        foreach (var batch in frame.Instances.Where(b => b.Visible.Count > 0))
            if (Of(batch.Model) is { Count: > 0 } shapes)
                groups.Add(ActorDrawGroup.ForBatch(profile, batch, shapes));
        foreach (var actor in frame.Request.Actors)
            if (Of(actor.Model) is { Count: > 0 } shapes)
                groups.Add(new ActorDrawGroup(profile.Actor(new SkinningData(actor.ModelMatrixRows, actor.Model.Skeleton, actor.BoneWorldMatrices)),
                    actor.ModelMatrixRows, shapes));
        return groups;
    }
}
