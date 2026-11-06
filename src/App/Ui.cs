namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Linq;
 using System.Runtime.InteropServices;
 using System.Windows.Forms;

 // Small owner-drawn UI kit: Windows 11 look (dark title bar, rounded cards, Fluent icons, Segoe UI Variable).
 public static class Theme {
  public static Color Bg = Color.FromArgb(16,17,20), Surface = Color.FromArgb(25,27,31), Raised = Color.FromArgb(33,36,41), Hover = Color.FromArgb(37,40,46),
   Line = Color.FromArgb(43,46,53), Ink = Color.FromArgb(242,239,234), Soft = Color.FromArgb(186,188,194), Muted = Color.FromArgb(126,130,139),
   Accent = Color.FromArgb(217,150,114), Green = Color.FromArgb(98,196,140), Amber = Color.FromArgb(238,182,82), Red = Color.FromArgb(242,116,106), Blue = Color.FromArgb(108,166,242);
  static HashSet<string> installed;
  static string Pick(params string[] names) {
   if (installed == null) installed = new HashSet<string>(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
   foreach (var name in names) if (installed.Contains(name)) return name;
   return "Segoe UI";
  }
  public static readonly string Sans = Pick("Segoe UI Variable Text", "Segoe UI"), SansBold = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold"),
   Display = Pick("Segoe UI Variable Display Semib", "Segoe UI Semibold"), Icons = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets"), Mono = Pick("Cascadia Mono", "Consolas");
  static readonly Dictionary<string,Font> fonts = new Dictionary<string,Font>();
  public static Font F(string family, float size) {
   string key = family + "|" + size; Font font;
   if (!fonts.TryGetValue(key, out font)) fonts[key] = font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
   return font;
  }
  public static Color A(Color c, int alpha) { return Color.FromArgb(alpha, c); }
  public static Color Mix(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
  // Fluent icon glyphs (same code points in Segoe MDL2 Assets).
  public const string Clock = "", Check = "", CheckCircle = "", Calendar = "", Code = "", Chat = "", Warning = "",
   Error = "", Person = "", Star = "", Bolt = "", Terminal = "", Open = "", Sun = "", Sync = "",
   Add = "", Flag = "", Branch = "", Stop = "", Info = "", Sparkle = "";
  public static void UseDarkMenus() { ToolStripManager.Renderer = new DarkMenuRenderer(); }
  // Redmine status names to colors: progress green, QA blue, waiting amber, rejected red.
  public static Color ForStatus(string status) {
   switch (status) {
    case "Em andamento": case "Finalizada": case "Resolvida": return Green;
    case "Em teste": return Blue;
    case "Rejeitada": case "Análise rejeitada": case "Cancelada": return Red;
    case "Feedback": case "Em pausa": case "Aguardando terceiros": case "Aguardando devs": case "Em aprovação": case "Nova": return Amber;
    default: return Soft;
   }
  }
  // Rounded dark menus everywhere, with taller rows.
  public static void Style(ToolStripDropDown menu) {
   menu.Font = F(Sans, 9.5f);
   menu.Opened += delegate { Dwm.Style(menu.Handle, Surface); };
   foreach (ToolStripItem item in menu.Items) {
    if (item is ToolStripSeparator) continue;
    item.Padding = new Padding(2, 5, 2, 5);
    var sub = item as ToolStripMenuItem; if (sub != null && sub.HasDropDownItems) Style(sub.DropDown);
   }
  }
 }

 // Dark title bar matching the window, subtle border and rounded corners (Windows 11; ignored elsewhere).
 internal static class Dwm {
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
  static int Ref(Color c) { return c.R | (c.G << 8) | (c.B << 16); }
  public static void Style(IntPtr hwnd, Color caption) {
   try {
    int on = 1, round = 2, back = Ref(caption), border = Ref(Theme.Line), text = Ref(Theme.Soft);
    DwmSetWindowAttribute(hwnd, 20, ref on, 4); DwmSetWindowAttribute(hwnd, 33, ref round, 4);
    DwmSetWindowAttribute(hwnd, 35, ref back, 4); DwmSetWindowAttribute(hwnd, 34, ref border, 4); DwmSetWindowAttribute(hwnd, 36, ref text, 4);
   } catch { }
  }
 }

 public sealed class Ui { public readonly float Scale; public Ui(float scale) { Scale = scale; } public int P(float v) { return (int)Math.Round(v * Scale); } }

 public static class Draw {
  public const TextFormatFlags Base = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
  const TextFormatFlags Wrap = Base | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
  const TextFormatFlags Single = Base | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
  public static GraphicsPath Round(RectangleF r, float radius) {
   var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
   if (d <= 0) { path.AddRectangle(r); return path; }
   path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
   path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
  }
  public static void Fill(Graphics g, Rectangle r, int radius, Color c) { using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p); }
  public static void Stroke(Graphics g, Rectangle r, int radius, Color c) { using (var p = Round(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), radius)) using (var pen = new Pen(c)) g.DrawPath(pen, p); }
  public static int Measure(Graphics g, string text, Font f, int width) {
   return String.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(g, text, f, new Size(Math.Max(1, width), 100000), Wrap).Height;
  }
  public static int Width(Graphics g, string text, Font f) { return String.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(g, text, f, Size.Empty, Base | TextFormatFlags.SingleLine).Width; }
  public static void Text(Graphics g, string text, Font f, Rectangle r, Color c) { TextRenderer.DrawText(g, text, f, r, c, Wrap); }
  public static void Line(Graphics g, string text, Font f, Rectangle r, Color c, bool right = false) { TextRenderer.DrawText(g, text, f, r, c, Single | (right ? TextFormatFlags.Right : 0)); }
  public static void Glyph(Graphics g, string glyph, float size, Rectangle r, Color c) {
   TextRenderer.DrawText(g, glyph, Theme.F(Theme.Icons, size), r, c, Base | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
  }
  public static Size ChipSize(Graphics g, Ui ui, string text) { return new Size(Width(g, text, Theme.F(Theme.SansBold, 8)) + ui.P(16), ui.P(20)); }
  public static void Chip(Graphics g, Ui ui, string text, Color color, Rectangle r) {
   Fill(g, r, r.Height / 2, Theme.A(color, 34));
   TextRenderer.DrawText(g, text, Theme.F(Theme.SansBold, 8), r, color, Base | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
  }
 }

 // Element tree laid out top to bottom; Bounds are in content coordinates (before scrolling).
 public abstract class El {
  public Rectangle Bounds; public Action Click, DoubleClick; public string Tip; public int Before = 12, Inset;
  public virtual IEnumerable<El> Children { get { return Enumerable.Empty<El>(); } }
  public abstract int Layout(Graphics g, Ui ui, int x, int y, int width);
  public abstract void Paint(Graphics g, Ui ui, El hover);
  protected bool Hot(El hover) { return hover == this && Click != null; }
 }
 public sealed class SpacerEl : El {
  readonly int height; public SpacerEl(int height) { this.height = height; Before = 0; }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) { Bounds = new Rectangle(x, y, width, ui.P(height)); return Bounds.Height; }
  public override void Paint(Graphics g, Ui ui, El hover) { }
 }
 public sealed class TitleEl : El {
  public string Glyph = Theme.Info, Title = "", Subtitle = ""; public Color Color = Theme.Accent;
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
   int text = width - ui.P(60);
   int h = Draw.Measure(g, Title, Theme.F(Theme.Display, 17), text) + ui.P(2) + Draw.Measure(g, Subtitle, Theme.F(Theme.Sans, 9.5f), text);
   Bounds = new Rectangle(x, y, width, Math.Max(ui.P(46), h)); return Bounds.Height;
  }
  public override void Paint(Graphics g, Ui ui, El hover) {
   var tile = new Rectangle(Bounds.X, Bounds.Y, ui.P(46), ui.P(46));
   Draw.Fill(g, tile, ui.P(13), Theme.A(Color, 40)); Draw.Glyph(g, Glyph, 16, tile, Color);
   int x = Bounds.X + ui.P(60), w = Bounds.Width - ui.P(60), th = Draw.Measure(g, Title, Theme.F(Theme.Display, 17), w);
   Draw.Text(g, Title, Theme.F(Theme.Display, 17), new Rectangle(x, Bounds.Y - ui.P(2), w, th), Theme.Ink);
   Draw.Text(g, Subtitle, Theme.F(Theme.Sans, 9.5f), new Rectangle(x, Bounds.Y + th, w, Bounds.Height), Theme.Soft);
  }
 }
 public sealed class SectionEl : El {
  public string Text = "", Right = "";
  public SectionEl() { Before = 22; }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) { Bounds = new Rectangle(x + ui.P(4), y, width - ui.P(8), ui.P(18)); return Bounds.Height; }
  public override void Paint(Graphics g, Ui ui, El hover) {
   Draw.Line(g, Text.ToUpperInvariant(), Theme.F(Theme.SansBold, 8), Bounds, Theme.Muted);
   if (Right.Length > 0) Draw.Line(g, Right, Theme.F(Theme.Sans, 8.5f), Bounds, Theme.Muted, true);
  }
 }
 public sealed class TextEl : El {
  public string Text = ""; public Font Font = Theme.F(Theme.Sans, 9.5f); public Color Color = Theme.Soft; public int Indent;
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
   Bounds = new Rectangle(x + ui.P(Indent), y, width - ui.P(Indent), Draw.Measure(g, Text, Font, width - ui.P(Indent))); return Bounds.Height;
  }
  public override void Paint(Graphics g, Ui ui, El hover) { Draw.Text(g, Text, Font, Bounds, Color); }
 }
 // A labelled excerpt with an accent bar ("Você pediu", "Claude").
 public sealed class QuoteEl : El {
  public string Label = "", Text = ""; public Color Color = Theme.Accent, TextColor = Theme.Ink;
  public QuoteEl() { Before = 10; }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
   Bounds = new Rectangle(x, y, width, ui.P(18) + Draw.Measure(g, Text, Theme.F(Theme.Sans, 9.5f), width - ui.P(14))); return Bounds.Height;
  }
  public override void Paint(Graphics g, Ui ui, El hover) {
   Draw.Fill(g, new Rectangle(Bounds.X, Bounds.Y + ui.P(2), ui.P(3), Bounds.Height - ui.P(2)), ui.P(2), Theme.A(Color, 150));
   Draw.Line(g, Label.ToUpperInvariant(), Theme.F(Theme.SansBold, 7.5f), new Rectangle(Bounds.X + ui.P(14), Bounds.Y, Bounds.Width, ui.P(16)), Color);
   Draw.Text(g, Text, Theme.F(Theme.Sans, 9.5f), new Rectangle(Bounds.X + ui.P(14), Bounds.Y + ui.P(18), Bounds.Width - ui.P(14), Bounds.Height), TextColor);
  }
 }
 // List row: icon in a tinted circle, title, wrapped subtitle, mono meta line, chip and right-side text.
 public sealed class RowEl : El {
  public string Glyph = "", Title = "", Subtitle = "", Meta = "", Chip = "", Right = "";
  public Color GlyphColor = Theme.Accent, ChipColor = Theme.Soft, RightColor = Theme.Muted, TitleColor = Theme.Ink; public bool Selected;
  public RowEl() { Before = 2; }
  int RightWidth(Graphics g, Ui ui) {
   int w = 0;
   if (Chip.Length > 0) w = Draw.ChipSize(g, ui, Chip).Width;
   if (Right.Length > 0) w = Math.Max(w, Draw.Width(g, Right, Theme.F(Theme.Sans, 8.5f)));
   return w == 0 ? 0 : w + ui.P(12);
  }
  int TextX(Ui ui) { return Glyph.Length > 0 ? ui.P(54) : ui.P(14); }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
   int text = width - TextX(ui) - RightWidth(g, ui) - ui.P(12);
   int h = ui.P(20) + (Subtitle.Length > 0 ? ui.P(2) + Draw.Measure(g, Subtitle, Theme.F(Theme.Sans, 9), text) : 0) + (Meta.Length > 0 ? ui.P(4) + ui.P(16) : 0);
   Bounds = new Rectangle(x, y, width, Math.Max(ui.P(36), h) + ui.P(20)); return Bounds.Height;
  }
  public override void Paint(Graphics g, Ui ui, El hover) {
   if (Selected) { Draw.Fill(g, Bounds, ui.P(10), Theme.A(Theme.Accent, 30)); Draw.Fill(g, new Rectangle(Bounds.X + ui.P(3), Bounds.Y + ui.P(12), ui.P(3), Bounds.Height - ui.P(24)), ui.P(2), Theme.Accent); }
   else if (Hot(hover)) Draw.Fill(g, Bounds, ui.P(10), Theme.Hover);
   int top = Bounds.Y + ui.P(10);
   if (Glyph.Length > 0) {
    var circle = new Rectangle(Bounds.X + ui.P(12), top, ui.P(32), ui.P(32));
    Draw.Fill(g, circle, ui.P(16), Theme.A(GlyphColor, 36)); Draw.Glyph(g, Glyph, 11, circle, GlyphColor);
   }
   int x = Bounds.X + TextX(ui), right = RightWidth(g, ui), w = Bounds.Width - TextX(ui) - right - ui.P(12);
   Draw.Line(g, Title, Theme.F(Theme.SansBold, 10), new Rectangle(x, top, w, ui.P(20)), TitleColor);
   int y = top + ui.P(22);
   if (Subtitle.Length > 0) { int h = Draw.Measure(g, Subtitle, Theme.F(Theme.Sans, 9), w); Draw.Text(g, Subtitle, Theme.F(Theme.Sans, 9), new Rectangle(x, y, w, h), Theme.Soft); y += h + ui.P(4); }
   if (Meta.Length > 0) Draw.Line(g, Meta, Theme.F(Theme.Mono, 8), new Rectangle(x, y, w, ui.P(16)), Theme.Muted);
   int edge = Bounds.Right - ui.P(12), ry = top;
   if (Chip.Length > 0) { var size = Draw.ChipSize(g, ui, Chip); Draw.Chip(g, ui, Chip, ChipColor, new Rectangle(edge - size.Width, ry, size.Width, size.Height)); ry += size.Height + ui.P(4); }
   if (Right.Length > 0) Draw.Line(g, Right, Theme.F(Theme.Sans, 8.5f), new Rectangle(edge - right, ry, right, ui.P(18)), RightColor, true);
  }
 }
 // Rounded surface holding other elements; optional hairlines between rows.
 public sealed class CardEl : El {
  public readonly List<El> Items = new List<El>(); public int Pad = 6; public bool Dividers = true; public Color Tint = Color.Empty;
  public override IEnumerable<El> Children { get { return Items; } }
  public CardEl Add(El e) { Items.Add(e); return this; }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
   int pad = ui.P(Pad), cy = y + pad;
   for (int i = 0; i < Items.Count; i++) {
    if (i > 0) cy += ui.P(Items[i].Before);
    int inset = ui.P(Items[i].Inset); cy += Items[i].Layout(g, ui, x + pad + inset, cy, width - pad * 2 - inset * 2);
   }
   Bounds = new Rectangle(x, y, width, cy - y + pad); return Bounds.Height;
  }
  public override void Paint(Graphics g, Ui ui, El hover) {
   Draw.Fill(g, Bounds, ui.P(14), Tint.IsEmpty ? Theme.Surface : Theme.Mix(Theme.Surface, Tint, .1f));
   Draw.Stroke(g, Bounds, ui.P(14), Tint.IsEmpty ? Theme.Line : Theme.A(Tint, 90));
   for (int i = 0; i < Items.Count; i++) {
    if (Dividers && i > 0 && Items[i] is RowEl && Items[i - 1] is RowEl && hover != Items[i] && hover != Items[i - 1] && !((RowEl)Items[i]).Selected && !((RowEl)Items[i - 1]).Selected)
     using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, Items[i].Bounds.X + ui.P(54), Items[i].Bounds.Y - ui.P(1), Items[i].Bounds.Right - ui.P(12), Items[i].Bounds.Y - ui.P(1));
    Items[i].Paint(g, ui, hover);
   }
  }
 }
 public class Tile { public string Glyph = "", Value = "", Label = ""; public Color Color = Theme.Accent; }
 public sealed class StatsEl : El {
  public readonly List<Tile> Tiles = new List<Tile>(); public int Columns = 4;
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
   int rows = (Tiles.Count + Columns - 1) / Columns; Bounds = new Rectangle(x, y, width, rows * ui.P(104) - ui.P(10)); return Bounds.Height;
  }
  public override void Paint(Graphics g, Ui ui, El hover) {
   int gap = ui.P(10), w = (Bounds.Width - gap * (Columns - 1)) / Columns;
   for (int i = 0; i < Tiles.Count; i++) {
    var t = Tiles[i]; var r = new Rectangle(Bounds.X + (i % Columns) * (w + gap), Bounds.Y + (i / Columns) * ui.P(104), w, ui.P(94));
    Draw.Fill(g, r, ui.P(14), Theme.Surface); Draw.Stroke(g, r, ui.P(14), Theme.Line);
    var circle = new Rectangle(r.X + ui.P(14), r.Y + ui.P(14), ui.P(26), ui.P(26));
    Draw.Fill(g, circle, ui.P(13), Theme.A(t.Color, 40)); Draw.Glyph(g, t.Glyph, 9.5f, circle, t.Color);
    Draw.Line(g, t.Value, Theme.F(Theme.Display, 17), new Rectangle(r.X + ui.P(14), r.Y + ui.P(44), w - ui.P(20), ui.P(28)), Theme.Ink);
    Draw.Line(g, t.Label, Theme.F(Theme.Sans, 8.5f), new Rectangle(r.X + ui.P(14), r.Y + ui.P(70), w - ui.P(20), ui.P(16)), Theme.Muted);
   }
  }
 }
 // Day bar: label, rounded track, value and a note.
 public sealed class BarEl : El {
  public string Label = "", Value = "", Note = ""; public double Fraction; public Color Color = Theme.Accent, NoteColor = Theme.Muted;
  public BarEl() { Before = 0; }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) { Bounds = new Rectangle(x, y, width, ui.P(40)); return Bounds.Height; }
  public override void Paint(Graphics g, Ui ui, El hover) {
   if (Hot(hover)) Draw.Fill(g, Bounds, ui.P(10), Theme.Hover);
   int cy = Bounds.Y + Bounds.Height / 2, labelW = ui.P(78), valueW = ui.P(54), noteW = ui.P(110);
   Draw.Line(g, Label, Theme.F(Theme.Sans, 9.5f), new Rectangle(Bounds.X + ui.P(14), Bounds.Y, labelW, Bounds.Height), Theme.Soft);
   var track = new Rectangle(Bounds.X + ui.P(14) + labelW, cy - ui.P(4), Bounds.Width - labelW - valueW - noteW - ui.P(44), ui.P(8));
   Draw.Fill(g, track, ui.P(4), Theme.Raised);
   if (Fraction > 0) Draw.Fill(g, new Rectangle(track.X, track.Y, Math.Max(track.Height, (int)(track.Width * Math.Min(1, Fraction))), track.Height), ui.P(4), Color);
   Draw.Line(g, Value, Theme.F(Theme.SansBold, 9.5f), new Rectangle(track.Right + ui.P(12), Bounds.Y, valueW, Bounds.Height), Theme.Ink, true);
   Draw.Line(g, Note, Theme.F(Theme.Sans, 9), new Rectangle(Bounds.Right - noteW - ui.P(10), Bounds.Y, noteW, Bounds.Height), NoteColor, true);
  }
 }
 public sealed class ButtonEl : El {
  public string Text = "", Glyph = ""; public bool Primary; public bool Small;
  public int Measure(Graphics g, Ui ui) { return Draw.Width(g, Text, Theme.F(Theme.SansBold, Small ? 8.5f : 9.5f)) + ui.P(Small ? 22 : 34) + (Glyph.Length > 0 ? ui.P(22) : 0); }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) { Bounds = new Rectangle(x, y, width, ui.P(Small ? 30 : 36)); return Bounds.Height; }
  public override void Paint(Graphics g, Ui ui, El hover) {
   bool hot = Hot(hover);
   if (Primary) Draw.Fill(g, Bounds, ui.P(9), hot ? Theme.Mix(Theme.Accent, Color.White, .12f) : Theme.Accent);
   else { Draw.Fill(g, Bounds, ui.P(9), hot ? Theme.Hover : Theme.Raised); Draw.Stroke(g, Bounds, ui.P(9), Theme.Line); }
   Color ink = Primary ? Theme.Bg : Theme.Ink; var font = Theme.F(Theme.SansBold, Small ? 8.5f : 9.5f);
   int textW = Draw.Width(g, Text, font), glyphW = Glyph.Length > 0 ? ui.P(22) : 0, start = Bounds.X + (Bounds.Width - textW - glyphW) / 2;
   if (glyphW > 0) Draw.Glyph(g, Glyph, Small ? 8.5f : 9.5f, new Rectangle(start, Bounds.Y, ui.P(16), Bounds.Height), ink);
   Draw.Line(g, Text, font, new Rectangle(start + glyphW, Bounds.Y, textW + ui.P(4), Bounds.Height), ink);
  }
 }
 public sealed class ButtonsEl : El {
  public readonly List<ButtonEl> Buttons = new List<ButtonEl>(); public bool Right = true; public string Hint = "";
  public override IEnumerable<El> Children { get { return Buttons.Cast<El>(); } }
  public ButtonsEl Add(string text, bool primary, Action click, string glyph = "") { Buttons.Add(new ButtonEl { Text = text, Primary = primary, Click = click, Glyph = glyph }); return this; }
  public ButtonsEl AddSmall(string text, Action click, string glyph = "") { Buttons.Add(new ButtonEl { Text = text, Small = true, Click = click, Glyph = glyph }); return this; }
  public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
   int gap = ui.P(8), total = Buttons.Sum(b => b.Measure(g, ui)) + gap * Math.Max(0, Buttons.Count - 1);
   int cx = Right ? x + width - total : x, h = 0;
   foreach (var b in Buttons) { int w = b.Measure(g, ui); h = Math.Max(h, b.Layout(g, ui, cx, y, w)); b.Bounds = new Rectangle(cx, y, w, b.Bounds.Height); cx += w + gap; }
   Bounds = new Rectangle(x, y, width, Math.Max(h, ui.P(30))); return Bounds.Height;
  }
  public override void Paint(Graphics g, Ui ui, El hover) {
   if (Hint.Length > 0) Draw.Line(g, Hint, Theme.F(Theme.Sans, 8.5f), new Rectangle(Bounds.X, Bounds.Y, Bounds.Width / 2, Bounds.Height), Theme.Muted);
   foreach (var b in Buttons) b.Paint(g, ui, hover);
  }
 }

 // Scrollable, double-buffered surface that lays out, paints and hit-tests the element tree.
 public sealed class Canvas : Control {
  readonly Ui ui; List<El> items = new List<El>(); El hover; int offset, content; bool dirty = true; Point mouse = new Point(-1, -1);
  readonly ToolTip tip = new ToolTip { InitialDelay = 400, AutoPopDelay = 12000 }; string tipText = "";
  public Padding Inset; public bool TopLine;
  public Canvas(Ui ui) {
   this.ui = ui; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
   BackColor = Theme.Bg; Inset = new Padding(ui.P(24), ui.P(22), ui.P(24), ui.P(24));
  }
  public void SetItems(IEnumerable<El> list) { items = list.ToList(); dirty = true; hover = null; Invalidate(); if (mouse.X >= 0 && IsHandleCreated) BeginInvoke(new Action(() => Track(mouse))); }
  IEnumerable<El> All(IEnumerable<El> list) { foreach (var e in list) { yield return e; foreach (var c in All(e.Children)) yield return c; } }
  void LayoutAll(Graphics g) {
   int y = Inset.Top, width = ClientSize.Width - Inset.Horizontal;
   for (int i = 0; i < items.Count; i++) {
    if (i > 0) y += ui.P(items[i].Before);
    int inset = ui.P(items[i].Inset); y += items[i].Layout(g, ui, Inset.Left + inset, y, width - inset * 2);
   }
   content = y + Inset.Bottom; dirty = false; offset = Math.Max(0, Math.Min(offset, content - ClientSize.Height));
  }
  protected override void OnResize(EventArgs e) { dirty = true; base.OnResize(e); }
  protected override void OnPaint(PaintEventArgs e) {
   var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
   if (dirty) LayoutAll(g);
   if (TopLine) using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, 0, Width, 0);
   var state = g.Save(); g.TranslateTransform(0, -offset);
   foreach (var item in items) if (item.Bounds.Bottom >= offset && item.Bounds.Top <= offset + Height) item.Paint(g, ui, hover);
   g.Restore(state);
   if (content > ClientSize.Height) {
    float view = ClientSize.Height, ratio = view / content; int thumb = Math.Max(ui.P(36), (int)(view * ratio)), top = (int)((view - thumb) * offset / (float)(content - view));
    Draw.Fill(g, new Rectangle(ClientSize.Width - ui.P(7), top + ui.P(2), ui.P(4), thumb - ui.P(4)), ui.P(2), Theme.A(Theme.Soft, 70));
   }
  }
  El HitTest(Point p) {
   var at = new Point(p.X, p.Y + offset); El found = null;
   foreach (var e in All(items)) if ((e.Click != null || e.DoubleClick != null || e.Tip != null) && e.Bounds.Contains(at)) found = e;
   return found;
  }
  void Track(Point p) {
   if (IsDisposed) return;
   var hit = HitTest(p);
   if (hit != hover) { hover = hit; Invalidate(); }
   Cursor = hit != null && (hit.Click != null || hit.DoubleClick != null) ? Cursors.Hand : Cursors.Default;
   string text = hit != null ? hit.Tip ?? "" : "";
   if (text != tipText) { tipText = text; tip.SetToolTip(this, text); }
  }
  protected override void OnMouseMove(MouseEventArgs e) { mouse = e.Location; Track(e.Location); base.OnMouseMove(e); }
  protected override void OnMouseLeave(EventArgs e) { mouse = new Point(-1, -1); if (hover != null) { hover = null; Invalidate(); } base.OnMouseLeave(e); }
  protected override void OnMouseClick(MouseEventArgs e) { var hit = HitTest(e.Location); if (e.Button == MouseButtons.Left && hit != null && hit.Click != null) hit.Click(); base.OnMouseClick(e); }
  protected override void OnMouseDoubleClick(MouseEventArgs e) { var hit = HitTest(e.Location); if (hit != null && hit.DoubleClick != null) hit.DoubleClick(); base.OnMouseDoubleClick(e); }
  protected override void OnMouseWheel(MouseEventArgs e) {
   int max = Math.Max(0, content - ClientSize.Height);
   offset = Math.Max(0, Math.Min(max, offset - e.Delta * ui.P(48) / 120)); Invalidate(); Track(e.Location); base.OnMouseWheel(e);
  }
  protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }
  protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
 }

 // Base window: dark caption that blends with the content, scrollable body and a fixed footer.
 public class PremiumWindow : Form {
  protected readonly Ui ui; protected readonly Canvas Body, Footer;
  public PremiumWindow(string title, int width, int height) {
   Text = title; BackColor = Theme.Bg; ForeColor = Theme.Ink; ShowIcon = false; MinimizeBox = false; MaximizeBox = false;
   FormBorderStyle = FormBorderStyle.Sizable; StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.None; KeyPreview = true;
   ui = new Ui(DeviceDpi / 96f); Font = Theme.F(Theme.Sans, 9.5f);
   ClientSize = new Size(ui.P(width), ui.P(height)); MinimumSize = new Size(ui.P(width * 0.8f), ui.P(380));
   Footer = new Canvas(ui) { Dock = DockStyle.Bottom, Height = ui.P(68), TopLine = true };
   Footer.Inset = new Padding(ui.P(24), ui.P(16), ui.P(24), ui.P(16)); Controls.Add(Footer);
   Body = new Canvas(ui) { Dock = DockStyle.Fill }; Controls.Add(Body); Body.BringToFront();
   KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
  }
  protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dwm.Style(Handle, Theme.Bg); }
  protected void SetFooter(ButtonsEl buttons) { Footer.SetItems(new El[] { buttons }); }
  // Dark context menu with rounded corners.
  protected void ShowMenu(Point at, params KeyValuePair<string,Action>[] items) {
   var menu = new ContextMenuStrip { Font = Theme.F(Theme.Sans, 9.5f), ShowImageMargin = false };
   foreach (var item in items) { var action = item.Value; var entry = new ToolStripMenuItem(item.Key, null, delegate { action(); }) { Padding = new Padding(4, 6, 4, 6) }; menu.Items.Add(entry); }
   menu.Opened += delegate { Dwm.Style(menu.Handle, Theme.Surface); };
   menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
   menu.Show(Body, Body.PointToClient(at));
  }
 }

 public sealed class DarkMenuRenderer : ToolStripProfessionalRenderer {
  public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }
  protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Ink : Theme.Muted; base.OnRenderItemText(e); }
  protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Soft; base.OnRenderArrow(e); }
  protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
   if (!e.Item.Selected || !e.Item.Enabled) return;
   e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
   Draw.Fill(e.Graphics, new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2), 6, Theme.Hover);
  }
  protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
   var r = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
   e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Draw.Fill(e.Graphics, r, 5, Theme.A(Theme.Accent, 50));
   TextRenderer.DrawText(e.Graphics, Theme.Check, Theme.F(Theme.Icons, 8), r, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
  }
  protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
   using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
  }
 }
 sealed class DarkColors : ProfessionalColorTable {
  public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
  public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
  public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
  public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
  public override Color MenuBorder { get { return Theme.Line; } }
  public override Color MenuItemBorder { get { return Color.Transparent; } }
  public override Color MenuItemSelected { get { return Theme.Hover; } }
  public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
  public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
  public override Color MenuItemPressedGradientBegin { get { return Theme.Raised; } }
  public override Color MenuItemPressedGradientEnd { get { return Theme.Raised; } }
  public override Color SeparatorDark { get { return Theme.Line; } }
  public override Color SeparatorLight { get { return Theme.Surface; } }
  public override Color CheckBackground { get { return Color.Transparent; } }
  public override Color CheckSelectedBackground { get { return Color.Transparent; } }
  public override Color CheckPressedBackground { get { return Color.Transparent; } }
 }
}
