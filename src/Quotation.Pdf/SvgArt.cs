using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PdfSharp.Drawing;

namespace Quotation.Pdf;

/// <summary>
/// Draws the vector logos of the "Modern" layout (Assets/*.svg) as PDF paths. Only what those files use is
/// supported: groups with translate/scale, polygon, polyline and paths with absolute M L H V Q C Z commands,
/// fill, stroke, stroke-width and opacities.
/// </summary>
internal static partial class SvgArt
{
    private static readonly Dictionary<string, XDocument> Cache = [];
    private static readonly Lock Gate = new();

    public static XDocument Load(string name)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out var doc)) return doc;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Quotation.Pdf.Assets.{name}")
                               ?? throw new InvalidOperationException($"Logo {name} is missing.");
            return Cache[name] = XDocument.Load(stream);
        }
    }

    /// <summary>Draws the SVG scaled uniformly into the box (centred, like the browser's default).</summary>
    public static void Draw(XGraphics g, string name, double x, double y, double width, double height, double opacity = 1)
    {
        var root = Load(name).Root!;
        var vb = Numbers(root.Attribute("viewBox")!.Value);
        var scale = Math.Min(width / vb[2], height / vb[3]);
        var state = g.Save();
        g.TranslateTransform(x + (width - vb[2] * scale) / 2, y + (height - vb[3] * scale) / 2);
        g.ScaleTransform(scale);
        g.TranslateTransform(-vb[0], -vb[1]);
        DrawChildren(g, root, new Style(null, null, 1, 1, 1, opacity));
        g.Restore(state);
    }

    private sealed record Style(string? Fill, string? Stroke, double StrokeWidth, double FillOpacity, double StrokeOpacity, double Opacity);

    private static void DrawChildren(XGraphics g, XElement parent, Style style)
    {
        foreach (var e in parent.Elements())
        {
            var s = style with
            {
                Fill = Attr(e, "fill") ?? style.Fill,
                Stroke = Attr(e, "stroke") ?? style.Stroke,
                StrokeWidth = Number(Attr(e, "stroke-width")) ?? style.StrokeWidth,
                FillOpacity = Number(Attr(e, "fill-opacity")) ?? style.FillOpacity,
                StrokeOpacity = Number(Attr(e, "stroke-opacity")) ?? style.StrokeOpacity,
                Opacity = style.Opacity * (Number(Attr(e, "opacity")) ?? 1),
            };
            switch (e.Name.LocalName)
            {
                case "g":
                    var state = g.Save();
                    if (Attr(e, "transform") is { } t) ApplyTransform(g, t);
                    DrawChildren(g, e, s);
                    g.Restore(state);
                    break;
                case "polygon":
                case "polyline":
                    var p = Numbers(Attr(e, "points") ?? "");
                    var points = Enumerable.Range(0, p.Length / 2).Select(i => new XPoint(p[2 * i], p[2 * i + 1])).ToArray();
                    if (points.Length < 2) break;
                    var poly = new XGraphicsPath { FillMode = XFillMode.Winding };
                    poly.AddLines(points);
                    if (e.Name.LocalName == "polygon") poly.CloseFigure();
                    Paint(g, poly, s);
                    break;
                case "path":
                    Paint(g, ParsePath(Attr(e, "d") ?? ""), s);
                    break;
            }
        }
    }

    private static void Paint(XGraphics g, XGraphicsPath path, Style s)
    {
        var brush = s.Fill is null or "none" ? null : new XSolidBrush(Colour(s.Fill, s.FillOpacity * s.Opacity));
        var pen = s.Stroke is null or "none" ? null
            : new XPen(Colour(s.Stroke, s.StrokeOpacity * s.Opacity), s.StrokeWidth) { LineCap = XLineCap.Square, LineJoin = XLineJoin.Miter };
        if (brush is not null && pen is not null) g.DrawPath(pen, brush, path);
        else if (brush is not null) g.DrawPath(brush, path);
        else if (pen is not null) g.DrawPath(pen, path);
    }

    private static XColor Colour(string hex, double alpha)
    {
        var c = XColor.FromArgb(int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture) | unchecked((int)0xFF000000));
        return XColor.FromArgb((int)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);
    }

    private static void ApplyTransform(XGraphics g, string transform)
    {
        foreach (Match m in TransformPart().Matches(transform))
        {
            var v = Numbers(m.Groups[2].Value);
            switch (m.Groups[1].Value)
            {
                case "translate":
                    g.TranslateTransform(v[0], v.Length > 1 ? v[1] : 0);
                    break;
                case "scale":
                    g.ScaleTransform(v[0], v.Length > 1 ? v[1] : v[0]);
                    break;
            }
        }
    }

    internal static XGraphicsPath ParsePath(string d)
    {
        var path = new XGraphicsPath { FillMode = XFillMode.Winding };
        var tokens = PathToken().Matches(d).Select(m => m.Value).ToList();
        var i = 0;
        XPoint current = default, start = default;
        var command = 'M';
        double Next() => double.Parse(tokens[i++], CultureInfo.InvariantCulture);
        bool HasNumber() => i < tokens.Count && !char.IsLetter(tokens[i][0]);
        while (i < tokens.Count)
        {
            if (char.IsLetter(tokens[i][0])) command = tokens[i++][0];
            switch (command)
            {
                case 'M':
                    path.StartFigure();
                    current = start = new XPoint(Next(), Next());
                    command = 'L'; // further pairs are lines
                    break;
                case 'L':
                    var to = new XPoint(Next(), Next());
                    path.AddLine(current, to);
                    current = to;
                    break;
                case 'H':
                    var h = new XPoint(Next(), current.Y);
                    path.AddLine(current, h);
                    current = h;
                    break;
                case 'V':
                    var v = new XPoint(current.X, Next());
                    path.AddLine(current, v);
                    current = v;
                    break;
                case 'Q':
                    var c = new XPoint(Next(), Next());
                    var end = new XPoint(Next(), Next());
                    // A quadratic curve as the equivalent cubic one.
                    path.AddBezier(current,
                        new XPoint(current.X + 2.0 / 3 * (c.X - current.X), current.Y + 2.0 / 3 * (c.Y - current.Y)),
                        new XPoint(end.X + 2.0 / 3 * (c.X - end.X), end.Y + 2.0 / 3 * (c.Y - end.Y)),
                        end);
                    current = end;
                    break;
                case 'C':
                    var c1 = new XPoint(Next(), Next());
                    var c2 = new XPoint(Next(), Next());
                    var e2 = new XPoint(Next(), Next());
                    path.AddBezier(current, c1, c2, e2);
                    current = e2;
                    break;
                case 'Z':
                    path.CloseFigure();
                    current = start;
                    break;
                default:
                    throw new NotSupportedException($"SVG path command '{command}' is not supported.");
            }
            if (command == 'Z' && HasNumber()) command = 'L';
        }
        return path;
    }

    private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value;

    private static double? Number(string? s) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double[] Numbers(string s) =>
        NumberToken().Matches(s).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();

    [GeneratedRegex(@"(translate|scale)\s*\(([^)]*)\)")]
    private static partial Regex TransformPart();

    [GeneratedRegex(@"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex PathToken();

    [GeneratedRegex(@"-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberToken();
}
