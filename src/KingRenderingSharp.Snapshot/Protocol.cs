namespace KingRenderingSharp.Snapshot;

/// <summary>The lines a client sends and the server answers with.</summary>
/// <remarks>
/// A client writes one command per line to standard input and reads lines from standard output. Floats are written in invariant culture, a name list is separated by <c>|</c>.
/// <code>
/// open totk|botw TAB ROMFS TAB CACHE TAB ACTOR  (tab separated, as paths hold spaces) prepares the actor and loads it; answers STATUS lines, then READY, or FAIL message
/// hide MATERIAL|MATERIAL...          leaves the shapes of those materials out of the next pictures (no names shows all)
/// render W H EX EY EZ TX TY TZ UX UY UZ FOV NEAR FAR
///                                    draws the model; answers FRAME W H and then W*H*4 bytes of straight-alpha RGBA, top row first
/// preview MATERIAL W H               draws only the shapes of the material, framed to the model; answers like render
/// bounds                             answers BOUNDS minx miny minz maxx maxy maxz of the model
/// quit                               ends the server
/// </code>
/// Any command that fails answers FAIL and the message on one line; the server keeps running.
/// </remarks>
static class Protocol
{
    public const string Status = "STATUS ";
    public const string Ready = "READY";
    public const string Fail = "FAIL ";
    public const string Frame = "FRAME ";
    public const string Bounds = "BOUNDS ";
}
