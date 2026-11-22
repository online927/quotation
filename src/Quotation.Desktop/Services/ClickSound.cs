using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Quotation.Desktop.Services;

/// <summary>
/// Round 35: a soft click when a quotation is saved and two quick clicks when it is approved, unless "Silent" is on in
/// My Settings (this PC; on at first). Windows plays it with the system sound player, a Mac with afplay; elsewhere (tests,
/// Linux) nothing is played. A sound never stops the work: any failure is ignored.
/// </summary>
public static class ClickSound
{
    /// <summary>My Settings → Silent (this PC). On = no sounds.</summary>
    public static bool Silent { get; set; } = true;

    /// <summary>What was asked last ("click" / "click-double"), also when silent or not played (for tests).</summary>
    public static string? LastAsked { get; private set; }

    /// <summary>Played sounds (not when silent), for tests.</summary>
    public static int PlayedCount { get; private set; }

    public static void Saved() => Play("click");

    public static void Approved() => Play("click-double");

    private static void Play(string name)
    {
        LastAsked = name;
        if (Silent) return;
        PlayedCount++;
        try
        {
            var file = Extract(name);
            if (file is null) return;
            if (OperatingSystem.IsWindows())
            {
                PlaySound(file, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault);
            }
            else if (OperatingSystem.IsMacOS())
            {
                using var p = Process.Start(new ProcessStartInfo("/usr/bin/afplay", $"\"{file}\"") { UseShellExecute = false, CreateNoWindow = true });
            }
        }
        catch (Exception)
        {
            // A sound is only a nicety.
        }
    }

    private static readonly Dictionary<string, string> Files = new();

    /// <summary>The sound as a file in the temp folder (copied from the app once).</summary>
    private static string? Extract(string name)
    {
        lock (Files)
        {
            if (Files.TryGetValue(name, out var known) && File.Exists(known)) return known;
            var path = Path.Combine(Path.GetTempPath(), $"Quotation-{name}.wav");
            using (var source = AssetLoader.Open(new Uri($"avares://TSQuotation/Assets/Sounds/{name}.wav")))
            using (var target = File.Create(path))
            {
                source.CopyTo(target);
            }
            Files[name] = path;
            return path;
        }
    }

    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndFilename = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);
}
