using System.Reflection;
using WildRenderingSharp.Hosting;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The sky/cloud/colour-correction postfx half of the AAMP reader, reached through
/// <see cref="IsolatedAamp"/> - see that class for why the reader lives in its own
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> instead of being referenced.
/// </summary>
static class IsolatedAampReader
{
    static MethodInfo? _parseToJson;
    static bool _unavailable;

    /// <summary>Returns the AAMP-parsed postfx as JSON (see <c>SkyPostFxJson.ParseToJson</c>'s own remarks for the shape), or null - with a log line, never an exception - if the reader assembly can't be found or the call itself throws. A missing/broken AAMP reader should degrade <see cref="SkyPostFx"/>/<see cref="CloudPostFx"/> to their hand-transcribed defaults, not crash the whole background pass.</summary>
    public static string? TryParseToJson(byte[]? skyBytes, byte[]? cloudBytes, byte[]? ccrBytes = null)
    {
        if (_unavailable)
            return null;

        try
        {
            if (_parseToJson is null)
            {
                if (!IsolatedAamp.IsAvailable)
                {
                    Console.WriteLine($"[IsolatedAampReader] {IsolatedAamp.AssemblyFileName} not found - real postfx parsing unavailable this run.");
                    _unavailable = true;
                    return null;
                }
                _parseToJson = IsolatedAamp.GetEntryPoint("WildRenderingSharp.AampReader.SkyPostFxJson", "ParseToJson");
            }

            return (string?)_parseToJson.Invoke(null, [skyBytes, cloudBytes, ccrBytes]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IsolatedAampReader] failed to parse real postfx: {ex}");
            _unavailable = true;
            return null;
        }
    }
}
