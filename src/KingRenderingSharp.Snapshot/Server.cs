using System.Globalization;
using System.Numerics;
using System.Text;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using KingRenderingSharp.Content.Assets;
using KingRenderingSharp.Content.Rendering.Cameras;
using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Hosting.Preparers;

namespace KingRenderingSharp.Snapshot;

/// <summary>Reads the commands of <see cref="Protocol"/> and draws the model they name.</summary>
sealed class Server(Stream input, Stream output) : IDisposable
{
    readonly StreamReader _in = new(input, Encoding.UTF8);
    readonly StreamWriter _lines = new(output, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    IWindow? _window;
    GL? _gl;
    ISnapshotScene? _scene;
    HashSet<string> _hidden = new(StringComparer.Ordinal);

    public int Run()
    {
        string? line;
        while ((line = _in.ReadLine()) is not null)
        {
            string[] part = line.Split(' ', 2);
            try
            {
                switch (part[0])
                {
                    case "open": Open(part[1].Split('\t')); break;
                    case "hide": Hide(part.Length > 1 ? part[1] : ""); break;
                    case "render": Send(Camera(part[1].Split(' ')), Size(part[1].Split(' ')), only: null); break;
                    case "preview": Preview(part[1].Split(' ')); break;
                    case "bounds": Bounds(); break;
                    case "quit": return 0;
                    default: throw new InvalidOperationException($"Unknown command '{part[0]}'.");
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _lines.WriteLine(Protocol.Fail + exception.Message.ReplaceLineEndings(" "));
                Console.Error.WriteLine(exception);
            }
        }

        return 0;
    }

    void Open(string[] arguments)
    {
        string game = arguments[0], romfs = arguments[1], actor = arguments[3];
        string? modelFile = arguments.Length > 4 && arguments[4].Length > 0 ? arguments[4] : null;
        CacheLayout cache = new(arguments[2]);
        OutOfProcessPreparer preparer = new();
        if (!preparer.IsAvailable) throw new FileNotFoundException($"The model preparer is not beside the server ({preparer.ExecutablePath}).");

        Status("Preparing the shared assets");
        preparer.EnsureSystemAssetsAsync(romfs, cache, Status, game: game).GetAwaiter().GetResult();
        Status($"Preparing {actor}");
        string model = preparer.PrepareAsync(new PrepareRequest(romfs, actor, cache, Game: game, ModelFile: modelFile), Status).GetAwaiter().GetResult();

        _scene?.Dispose();
        _window ??= Window.Create(WindowOptions.Default with
        {
            IsVisible = false,
            Size = new(64, 64),
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 5)),
        });
        if (_gl is null)
        {
            _window.Initialize();
            _gl = GL.GetApi(_window.GLContext);
        }

        Status("Loading the model");
        _scene = game == "botw" ? new BotwScene(_gl, cache, model, 512) : new TotkScene(_gl, romfs, cache, model, 512);
        _lines.WriteLine(Protocol.Ready);
    }

    void Hide(string names)
    {
        _hidden = [.. names.Split('|', StringSplitOptions.RemoveEmptyEntries)];
    }

    void Status(string text) => _lines.WriteLine(Protocol.Status + text.ReplaceLineEndings(" "));

    ISnapshotScene Scene => _scene ?? throw new InvalidOperationException("No model is open.");

    static float F(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    static (int Width, int Height) Size(string[] p) => (int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture));

    static Camera Camera(string[] p) => new()
    {
        Eye = new Vector3(F(p[2]), F(p[3]), F(p[4])),
        Target = new Vector3(F(p[5]), F(p[6]), F(p[7])),
        Up = new Vector3(F(p[8]), F(p[9]), F(p[10])),
        FovDegrees = F(p[11]),
        NearPlane = F(p[12]),
        FarPlane = F(p[13]),
    };

    void Bounds()
    {
        (Vector3 min, Vector3 max) = Scene.Bounds;
        _lines.WriteLine(FormattableString.Invariant($"{Protocol.Bounds}{min.X} {min.Y} {min.Z} {max.X} {max.Y} {max.Z}"));
    }

    void Preview(string[] p)
    {
        // Framed to the model, as it sits at the origin of its own space, seen from the renderer's default direction.
        (Vector3 min, Vector3 max) = Scene.Bounds;
        Vector3 center = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 0.01f);
        Camera camera = new() { Target = center, Eye = center + Vector3.Normalize(new Vector3(0.6f, 0.35f, 1f)) * radius * 3.2f, FovDegrees = 38f, NearPlane = radius * 0.05f, FarPlane = radius * 20f };
        Send(camera, (int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture)), only: p[0]);
    }

    // Draws with the hidden materials left out (and, for a preview, only the one material), then restores every shape.
    void Send(Camera camera, (int Width, int Height) size, string? only)
    {
        LoadedModel model = Scene.Model;
        var saved = model.Shapes.Select(s => s.Enabled).ToArray();
        for (int i = 0; i < model.Shapes.Count; i++)
        {
            LoadedShape shape = model.Shapes[i];
            shape.Enabled = saved[i] && !_hidden.Contains(shape.Material) && (only is null || shape.Material == only);
        }

        byte[] rgba;
        try
        {
            rgba = Scene.Render(camera, size.Width, size.Height);
        }
        finally
        {
            for (int i = 0; i < model.Shapes.Count; i++) model.Shapes[i].Enabled = saved[i];
        }

        _lines.WriteLine($"{Protocol.Frame}{size.Width} {size.Height}");
        output.Write(rgba, 0, rgba.Length);
        output.Flush();
    }

    public void Dispose()
    {
        _scene?.Dispose();
        _gl?.Dispose();
        _window?.Dispose();
    }
}
