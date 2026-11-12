namespace MeuClaudeWidget {
 using System.Drawing.Imaging;
 using SkiaSharp;

 // Strip drawing with Skia: the liquid pills, the day line and the task ring are rendered by SkiaSharp into a
 // small layer and composited at the same spot of the GDI+ paint order, so z-order, text (native ClearType) and
 // the pixel creature stay exactly as they are. Each layer is a pixel buffer shared by a Bitmap and an SKSurface:
 // Skia draws into it and GDI+ blits it, with no copy in between.
 public static class SkiaLayer {
  public static bool Enabled = true; static bool failed;
  public static bool Available { get { return Enabled && !failed; } }
  internal sealed class Slot { public int W, H; public IntPtr Pixels; public Bitmap Bmp; public SKSurface Surface; public bool InUse; }
  static readonly Dictionary<long, Slot> pool = new Dictionary<long, Slot>();

  public sealed class Layer : IDisposable {
   readonly Graphics g; readonly Rectangle bounds; readonly Slot slot;
   public readonly SKCanvas Canvas;
   internal Layer(Graphics g, Rectangle bounds, Slot slot) {
    this.g = g; this.bounds = bounds; this.slot = slot; slot.InUse = true;
    Canvas = slot.Surface.Canvas; Canvas.ResetMatrix(); Canvas.Clear(SKColors.Transparent); Canvas.Translate(-bounds.X, -bounds.Y);
   }
   public void Dispose() {
    try { Canvas.Flush(); g.DrawImageUnscaled(slot.Bmp, bounds.X, bounds.Y); } catch { }
    slot.InUse = false;
   }
  }

  // A layer covering 'bounds' in the coordinates of g; null when Skia is unavailable (then draw with GDI+).
  public static Layer Begin(Graphics g, Rectangle bounds) {
   if (!Available || bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 4096 || bounds.Height > 4096) return null;
   try {
    long key = ((long)bounds.Width << 20) | (long)bounds.Height; Slot slot;
    if (!pool.TryGetValue(key, out slot) || slot.InUse) {
     if (pool.Count >= 12) { foreach (var old in pool.Values.Where(v => !v.InUse).ToList()) { Free(old); pool.Remove(((long)old.W << 20) | (long)old.H); } }
     slot = new Slot { W = bounds.Width, H = bounds.Height };
     int stride = bounds.Width * 4;
     slot.Pixels = Marshal.AllocHGlobal(stride * bounds.Height);
     slot.Bmp = new Bitmap(bounds.Width, bounds.Height, stride, PixelFormat.Format32bppPArgb, slot.Pixels);
     slot.Surface = SKSurface.Create(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul), slot.Pixels, stride);
     if (slot.Surface == null) { Free(slot); failed = true; return null; }
     if (!slot.InUse) pool[key] = slot;
    }
    return new Layer(g, bounds, slot);
   } catch { failed = true; return null; }
  }
  static void Free(Slot s) { try { if (s.Surface != null) s.Surface.Dispose(); if (s.Bmp != null) s.Bmp.Dispose(); if (s.Pixels != IntPtr.Zero) Marshal.FreeHGlobal(s.Pixels); } catch { } }

  public static SKColor Sk(Color c) { return new SKColor(c.R, c.G, c.B, c.A); }
  public static SKColor Sk(int alpha, Color c) { return new SKColor(c.R, c.G, c.B, (byte)Math.Max(0, Math.Min(255, alpha))); }
  public static SKPaint Fill(SKColor c) { return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = c }; }
  public static SKPaint Stroke(SKColor c, float width, bool round = false) { return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = c, StrokeWidth = width, StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Butt }; }
  // Vertical gradient, like LinearGradientBrush(rect, top, bottom, 90f).
  public static SKPaint Vertical(float x, float top, float bottom, SKColor from, SKColor to) {
   return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = SKShader.CreateLinearGradient(new SKPoint(x, top), new SKPoint(x, bottom), new[] { from, to }, null, SKShaderTileMode.Clamp) };
  }
  // Soft disc, like a PathGradientBrush on an ellipse with a transparent surround.
  public static SKPaint Halo(float cx, float cy, float r, SKColor center) {
   return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r, new[] { center, center.WithAlpha(0) }, null, SKShaderTileMode.Clamp) };
  }
  public static void Arc(SKCanvas c, SKPaint pen, float x, float y, float w, float h, float start, float sweep) {
   using (var path = new SKPath()) { path.AddArc(new SKRect(x, y, x + w, y + h), start, sweep); c.DrawPath(path, pen); }
  }
 }

 public partial class Widget {
  bool UseSkia { get { return settings.Skia && SkiaLayer.Available; } }

  // A1 in Skia: the liquid tube (see PaintPill for the GDI+ original, kept as the fallback).
  bool PaintPillSkia(Graphics g, Rectangle pill, double? value, Color color, float radius) {
   int pad = Px(12);
   var bounds = new Rectangle(pill.X - Px(4), pill.Y - pad, pill.Width + Px(8), pill.Height + pad + Px(4));
   using (var layer = SkiaLayer.Begin(g, bounds)) {
    if (layer == null) return false;
    var c = layer.Canvas;
    var rect = new SKRect(pill.X, pill.Y, pill.Right, pill.Bottom);
    using (var shape = new SKRoundRect(rect, radius, radius)) {
     using (var background = SkiaLayer.Fill(new SKColor(48, 50, 53))) c.DrawRoundRect(shape, background);
     c.Save(); c.ClipRoundRect(shape, SKClipOperation.Intersect, true);
     float level = value.HasValue ? (float)(pill.Width * Math.Min(100, Math.Max(0, value.Value)) / 100) : 0;
     if (level > 0 && !settings.Liquid) using (var fill = SkiaLayer.Fill(SkiaLayer.Sk(95, color))) c.DrawRect(pill.X, pill.Y, level, pill.Height, fill);
     else if (level > 0) {
      double t = (settings.Animations && !Night ? MotionTime.Now : 0) / 1000.0;
      float amp = Math.Max(1f, Px(1.7f));
      for (int wave = 1; wave >= 0; wave--) {
       using (var body = new SKPath()) {
        body.MoveTo(pill.X - 1, pill.Y);
        for (int k = 0; k <= 12; k++) {
         float y = pill.Y + pill.Height * k / 12f;
         double phase = t * (wave == 0 ? 2.4 : 1.7) + k * (wave == 0 ? 0.85 : 1.25) + wave * 1.9;
         body.LineTo(pill.X + level + (float)Math.Sin(phase) * amp * (wave == 0 ? 1f : .75f) - wave * Px(1.5f), y);
        }
        body.LineTo(pill.X - 1, pill.Bottom); body.Close();
        if (wave == 1) using (var back = SkiaLayer.Fill(SkiaLayer.Sk(55, color))) c.DrawPath(body, back);
        else using (var front = SkiaLayer.Vertical(pill.X, pill.Y - 1, pill.Bottom + 1, SkiaLayer.Sk(150, color), SkiaLayer.Sk(85, color))) c.DrawPath(body, front);
       }
      }
      // Bubbles rising through the liquid.
      if (level > Px(14) && settings.Animations && !Night)
       for (int i = 0; i < 3; i++) {
        double p = (t * (0.35 + i * 0.12) + i * 0.37) % 1.0;
        float bx = pill.X + level * (0.22f + 0.27f * i) + (float)Math.Sin(t * 2 + i) * Px(1), by = pill.Bottom - (float)p * pill.Height, br = Math.Max(1f, Px(1.1f));
        using (var bubble = SkiaLayer.Fill(new SKColor(255, 255, 255, (byte)(110 * (1 - p))))) c.DrawCircle(bx, by, br, bubble);
       }
      // Boiling near the limit: big fast bubbles all over the liquid.
      if (value.HasValue && value.Value >= 90 && settings.Animations && !Night)
       for (int i = 0; i < 6; i++) {
        double p = (t * (1.3 + i * 0.21) + i * 0.29) % 1.0;
        float bx = pill.X + level * ((i * 0.173f + 0.08f) % 1f), by = pill.Bottom - (float)p * pill.Height, br = Math.Max(1.2f, Px(1.3f + (i % 3) * .35f));
        using (var bubble = SkiaLayer.Stroke(new SKColor(255, 255, 255, (byte)(170 * (1 - p))), 1f)) c.DrawCircle(bx, by, br, bubble);
       }
     }
     // Glassy top of the tube.
     using (var shine = SkiaLayer.Vertical(pill.X, pill.Y - 1, pill.Y - 1 + pill.Height * .5f + 2, new SKColor(255, 255, 255, 42), new SKColor(255, 255, 255, 0)))
      c.DrawRect(pill.X, pill.Y, pill.Width, pill.Height * .5f, shine);
     c.Restore();
     using (var rim = SkiaLayer.Stroke(new SKColor(255, 255, 255, 26), 1f)) c.DrawRoundRect(shape, rim);
    }
    // Steam curling up from a boiling tube.
    if (settings.Liquid && value.HasValue && value.Value >= 90 && settings.Animations && !Night) {
     double t = MotionTime.Now / 1000.0;
     float level = (float)(pill.Width * Math.Min(100, value.Value) / 100);
     for (int i = 0; i < 3; i++) {
      double p = (t * 0.55 + i / 3.0) % 1.0; float x = pill.X + level * (0.3f + i * 0.25f) + (float)Math.Sin(t * 3 + i * 2) * Px(2), y = pill.Y + pill.Height * .4f - (float)p * Px(9);
      using (var steam = SkiaLayer.Stroke(new SKColor(236, 236, 242, (byte)(120 * Math.Sin(p * Math.PI))), Math.Max(1f, Px(1.2f)), true)) SkiaLayer.Arc(c, steam, x - Px(1.5f), y - Px(2), Px(3), Px(4), 90 + (float)(p * 180), 160);
     }
    }
   }
   return true;
  }

  // A3 in Skia: the workday line along the bottom of the strip.
  bool PaintDayLineSkia(Graphics g) {
   DateTime now = DateTime.Now, today = now.Date;
   int left = Px(12), right = StripWidth - Px(12), gap = Px(7);
   var segments = WorkDay.Windows.Select(w => new[] { (int)w[0].TotalMinutes, (int)w[1].TotalMinutes }).ToList();
   int total = segments.Sum(s => s[1] - s[0]); float span = right - left - gap * (segments.Count - 1); if (span <= 0 || total <= 0) return true;
   float y = Height - Px(3.2f), thick = Math.Max(1.5f, Px(1.7f)); int nowMinute = now.Hour * 60 + now.Minute;
   bool workday = WorkDay.IsWorkday(now);
   using (var layer = SkiaLayer.Begin(g, new Rectangle(0, Height - Px(16), StripWidth, Px(16)))) {
    if (layer == null) return false;
    var c = layer.Canvas;
    float x0 = left; SKPoint? nowPoint = null;
    foreach (var s in segments) {
     float width = span * (s[1] - s[0]) / total;
     int runStart = s[0]; int runKind = Kind(today, s[0], nowMinute, workday);
     for (int m = s[0] + 1; m <= s[1]; m++) {
      int kind = m < s[1] ? Kind(today, m, nowMinute, workday) : -9;
      if (kind == runKind) continue;
      float a = x0 + width * (runStart - s[0]) / (s[1] - s[0]), b = x0 + width * (m - s[0]) / (s[1] - s[0]);
      SKColor col = runKind == 1 ? SkiaLayer.Sk(235, accent) : runKind == 2 ? SkiaLayer.Sk(235, amber) : runKind == 0 ? new SKColor(255, 255, 255, 46) : new SKColor(255, 255, 255, 16);
      using (var pen = SkiaLayer.Stroke(col, thick, true)) c.DrawLine(a + thick / 2, y, Math.Max(a + thick / 2, b - thick / 2), y, pen);
      runStart = m; runKind = kind;
     }
     if (workday && nowMinute >= s[0] && nowMinute < s[1]) nowPoint = new SKPoint(x0 + width * (nowMinute - s[0] + now.Second / 60f) / (s[1] - s[0]), y);
     x0 += width + gap;
    }
    if (nowPoint.HasValue) {
     var p = nowPoint.Value; float glow = Px(5) + (settings.Animations ? (float)Math.Sin(MotionTime.Now / 420.0) * Px(1) : 0);
     using (var halo = SkiaLayer.Halo(p.X, p.Y, glow, SkiaLayer.Sk(150, accent))) c.DrawCircle(p.X, p.Y, glow, halo);
     float core = Math.Max(1.6f, Px(2));
     using (var dot = SkiaLayer.Fill(new SKColor(255, 250, 240))) c.DrawCircle(p.X, p.Y, core, dot);
    }
   }
   return true;
  }

  // Task ring in Skia: progress against the estimate, halo, head, overrun and the ripple.
  bool PaintTaskRingSkia(Graphics g, PointF center, float r, double f) {
   Color color = TaskColor(f); float thick = Math.Max(2f, Px(2.6f));
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0;
   double pulse = f >= 0.85 ? (Math.Sin(t * (f >= 1 ? 4.2 : 2.6)) + 1) / 2 : 0;
   int reach = (int)Math.Ceiling(r + thick + Px(14));
   using (var layer = SkiaLayer.Begin(g, new Rectangle((int)Math.Floor(center.X) - reach, (int)Math.Floor(center.Y) - reach, reach * 2 + 1, reach * 2 + 1))) {
    if (layer == null) return false;
    var c = layer.Canvas;
    if (pulse > 0) { float glow = r + Px(5) + (float)pulse * Px(2); using (var halo = SkiaLayer.Halo(center.X, center.Y, glow, SkiaLayer.Sk((int)(40 + 70 * pulse), color))) c.DrawCircle(center.X, center.Y, glow, halo); }
    using (var track = SkiaLayer.Stroke(new SKColor(255, 255, 255, 38), thick)) c.DrawCircle(center.X, center.Y, r, track);
    float sweep = (float)(360 * Math.Min(1, Math.Max(0.02, f)));
    using (var arc = SkiaLayer.Stroke(SkiaLayer.Sk(color), thick, true)) SkiaLayer.Arc(c, arc, center.X - r, center.Y - r, r * 2, r * 2, -90, sweep);
    double head = (-90 + sweep) * Math.PI / 180; float hx = center.X + r * (float)Math.Cos(head), hy = center.Y + r * (float)Math.Sin(head), hr = thick * .55f;
    if (f < 1) using (var dot = SkiaLayer.Fill(new SKColor(255, 248, 236))) c.DrawCircle(hx, hy, hr, dot);
    if (f > 1) {
     float outer = r + thick + Px(1.2f);
     using (var over = SkiaLayer.Stroke(SkiaLayer.Sk(200, red), Math.Max(1.5f, thick * .7f), true)) SkiaLayer.Arc(c, over, center.X - outer, center.Y - outer, outer * 2, outer * 2, -90, (float)(360 * Math.Min(1, f - 1)));
    }
    double rp = (DateTime.Now - ringCrossed).TotalSeconds / 1.3;
    if (rp >= 0 && rp <= 1) {
     float rr = r + Px(2) + (float)(1 - Math.Pow(1 - rp, 3)) * Px(10);
     using (var ring = SkiaLayer.Stroke(SkiaLayer.Sk((int)(220 * (1 - rp)), color), Px(1.6f))) c.DrawCircle(center.X, center.Y, rr, ring);
    }
   }
   return true;
  }
 }
}
