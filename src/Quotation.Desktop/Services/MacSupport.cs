using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Quotation.Desktop.Services;

/// <summary>
/// The Mac version of the PC app: ⌘ instead of Ctrl for shortcuts, printing through the Mac's own printing (CUPS),
/// Finder and the clipboard. Everything here does nothing on Windows.
/// </summary>
public static class MacSupport
{
    public static bool IsMac => OperatingSystem.IsMacOS();

    /// <summary>The modifier for shortcuts: ⌘ on a Mac, Ctrl elsewhere.</summary>
    public static KeyModifiers Command => IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>On a Mac, every Ctrl shortcut of a page also works with ⌘ (hot keys on buttons move to ⌘).</summary>
    public static void Apply(Control view)
    {
        if (!IsMac) return;
        Mirror(view);
        view.AttachedToLogicalTree += (_, _) =>
        {
            foreach (var c in view.GetLogicalDescendants().OfType<Control>()) Mirror(c);
        };
    }

    private static void Mirror(Control control)
    {
        foreach (var binding in control.KeyBindings.ToList())
        {
            if (binding.Gesture is not { } g || !g.KeyModifiers.HasFlag(KeyModifiers.Control)) continue;
            var mods = (g.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta;
            if (control.KeyBindings.Any(b => b.Gesture is { } other && other.Key == g.Key && other.KeyModifiers == mods)) continue;
            control.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(g.Key, mods), Command = binding.Command, CommandParameter = binding.CommandParameter });
        }
        if (control is Button { HotKey: { } hot } button && hot.KeyModifiers.HasFlag(KeyModifiers.Control))
            button.HotKey = new KeyGesture(hot.Key, (hot.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta);
    }

    /// <summary>Prints a PDF on the Mac's default printer (lp); returns the printer's name.</summary>
    public static string Print(byte[] pdf, string documentName)
    {
        var file = Path.Combine(Path.GetTempPath(), "tsq-print-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(file, pdf);
        var printer = Run("lpstat", "-d") is { } d && d.Contains(':') ? d[(d.IndexOf(':') + 1)..].Trim() : "";
        if (printer.Length == 0) throw new InvalidOperationException("No default printer is set on this Mac (System Settings → Printers & Scanners).");
        var started = Process.Start(new ProcessStartInfo("lp") { ArgumentList = { "-t", documentName, file }, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })
                      ?? throw new InvalidOperationException("Printing could not start.");
        started.WaitForExit(15000);
        if (started.ExitCode != 0) throw new InvalidOperationException("The printer did not accept the document: " + started.StandardError.ReadToEnd().Trim());
        return printer;
    }

    /// <summary>Shows the file in Finder.</summary>
    public static void ShowInFinder(string path) => Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", path }, UseShellExecute = false });

    /// <summary>Puts the file itself on the clipboard (as if copied in Finder), for pasting into WhatsApp.</summary>
    public static void CopyFileToClipboard(string path)
    {
        var escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        using var p = Process.Start(new ProcessStartInfo("osascript") { ArgumentList = { "-e", $"set the clipboard to (POSIX file \"{escaped}\")" }, UseShellExecute = false });
        p?.WaitForExit(5000);
    }

    private static string? Run(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch (Exception) { return null; }
    }
}
