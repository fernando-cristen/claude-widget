namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.IO;
 using System.Linq;
 using System.Media;
 using System.Text;

 // The strip "breathes": an animated status orb next to the project name and light effects along the strip,
 // so the Claude session state is readable from the corner of the eye, without notifications.
 public sealed class Ambient {
  public static readonly Color Working = Color.FromArgb(232,150,104), Background = Color.FromArgb(108,166,242), Waiting = Color.FromArgb(244,186,72),
   Done = Color.FromArgb(98,204,146), Error = Color.FromArgb(244,108,98), Idle = Color.FromArgb(128,131,139);
  string state = "idle"; DateTime changed = DateTime.MinValue, born = DateTime.Now;
  Color from = Idle, to = Idle;
  readonly List<KeyValuePair<DateTime,Color>> ripples = new List<KeyValuePair<DateTime,Color>>();
  DateTime sweep = DateTime.MinValue; Color sweepColor = Done;
  public string State { get { return state; } }

  public static Color ColorOf(string s) {
   switch (s) { case "working": return Working; case "background": return Background; case "waiting": return Waiting; case "done": return Done; case "error": return Error; default: return Idle; }
  }
  // New session state: crossfade the color; "done" gets a burst and a green sweep, "waiting" a first ripple right away.
  public void Update(string next, DateTime now) {
   if (next == state) return;
   from = CurrentColor(now); to = ColorOf(next); changed = now; state = next;
   if (next == "done") { ripples.Add(new KeyValuePair<DateTime,Color>(now, Done)); ripples.Add(new KeyValuePair<DateTime,Color>(now.AddMilliseconds(220), Done)); sweep = now; sweepColor = Done; }
   if (next == "waiting") ripples.Add(new KeyValuePair<DateTime,Color>(now, Waiting));
   if (next == "error") { sweep = now; sweepColor = Error; }
  }
  public bool Animating(DateTime now) {
   ripples.RemoveAll(r => (now - r.Key).TotalSeconds > 1.4);
   return state == "working" || state == "background" || state == "waiting" || ripples.Count > 0 || (now - changed).TotalSeconds < 1.2 || (now - sweep).TotalSeconds < 1.1;
  }
  Color CurrentColor(DateTime now) {
   double t = Math.Min(1, (now - changed).TotalSeconds / 0.6); t = t * t * (3 - 2 * t);
   return Color.FromArgb((int)(from.R + (to.R - from.R) * t), (int)(from.G + (to.G - from.G) * t), (int)(from.B + (to.B - from.B) * t));
  }
  static double Wave(DateTime now, double period, DateTime origin) { return (Math.Sin((now - origin).TotalSeconds * 2 * Math.PI / period - Math.PI / 2) + 1) / 2; }
  static Color A(Color c, double alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)alpha)), c); }

  // Orb centered at (cx, cy); s is the DPI scale.
  public void PaintOrb(Graphics g, float cx, float cy, float s, DateTime now) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   Color c = CurrentColor(now); bool active = state == "working" || state == "background" || state == "waiting";
   double breath = state == "waiting" ? Wave(now, 1.1, born) : state == "working" || state == "background" ? Wave(now, 1.9, born) : 0.35;
   // Ripples: rings that expand and fade.
   foreach (var r in ripples) {
    double t = (now - r.Key).TotalSeconds / 1.3; if (t < 0 || t > 1) continue;
    float radius = s * (4 + 13 * (float)(1 - Math.Pow(1 - t, 3)));
    using (var pen = new Pen(A(r.Value, 210 * (1 - t)), s * (1.8f - (float)t))) g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
   }
   if (state == "waiting" && (now - changed).TotalSeconds > 0.1) {
    double cycle = ((now - born).TotalSeconds % 1.6) / 1.3;
    if (cycle <= 1) { float radius = s * (4 + 11 * (float)(1 - Math.Pow(1 - cycle, 3))); using (var pen = new Pen(A(c, 170 * (1 - cycle)), s * 1.4f)) g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2); }
   }
   // Glow: soft radial halo that breathes.
   if (state != "idle") {
    float glow = s * (7f + 3.5f * (float)breath);
    using (var path = new GraphicsPath()) {
     path.AddEllipse(cx - glow, cy - glow, glow * 2, glow * 2);
     using (var brush = new PathGradientBrush(path)) {
      brush.CenterColor = A(c, (active ? 95 : 55) + 70 * breath); brush.SurroundColors = new[] { A(c, 0) };
      g.FillPath(brush, path);
     }
    }
   }
   // Spinner arc while Claude works.
   if (state == "working" || state == "background") {
    float arc = s * 6.6f; float angle = (float)(((now - born).TotalSeconds * 300) % 360);
    using (var pen = new Pen(A(c, 230), s * 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, cx - arc, cy - arc, arc * 2, arc * 2, angle, 110);
    using (var pen = new Pen(A(c, 90), s * 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, cx - arc, cy - arc, arc * 2, arc * 2, angle + 180, 50);
   }
   // Core with a glassy highlight; a small pop when the state just changed.
   float pop = (float)Math.Max(0, 1 - (now - changed).TotalSeconds / 0.35);
   float core = s * (3.4f + 0.6f * (float)breath + 1.4f * pop);
   using (var brush = new SolidBrush(c)) g.FillEllipse(brush, cx - core, cy - core, core * 2, core * 2);
   using (var path = new GraphicsPath()) {
    path.AddEllipse(cx - core, cy - core, core * 2, core * 2);
    using (var shine = new PathGradientBrush(path) { CenterPoint = new PointF(cx - core * .35f, cy - core * .4f), CenterColor = Color.FromArgb(105, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
     g.FillPath(shine, path);
   }
   g.SmoothingMode = mode;
  }

  // Effects across the whole strip: a comet along the bottom while working, a breathing outline while waiting, a sweep on done/error.
  public void PaintEdge(Graphics g, Rectangle strip, float s, float radius, DateTime now) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   Color c = CurrentColor(now);
   if (state == "working" || state == "background") {
    double p = ((now - born).TotalSeconds % 2.4) / 2.4; p = p < .5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
    float width = strip.Width * .34f, x = strip.Left - width + (float)p * (strip.Width + width), y = strip.Bottom - s * 2.2f;
    var band = new RectangleF(x, y, width, s * 1.6f);
    using (var brush = new LinearGradientBrush(new RectangleF(band.X - 1, band.Y, band.Width + 2, band.Height), A(c, 0), A(c, 0), 0f)) {
     brush.InterpolationColors = new ColorBlend { Colors = new[] { A(c, 0), A(c, 150), Color.FromArgb(230, 255, 240, 225), A(c, 150), A(c, 0) }, Positions = new[] { 0f, .35f, .5f, .65f, 1f } };
     using (var clip = Draw.Round(new RectangleF(strip.X + s, strip.Y, strip.Width - 2 * s, strip.Height), radius)) { var state0 = g.Save(); g.SetClip(clip); g.FillRectangle(brush, band); g.Restore(state0); }
    }
   }
   if (state == "waiting") {
    double breath = Wave(now, 1.1, born);
    using (var path = Draw.Round(new RectangleF(strip.X + s * .75f, strip.Y + s * .75f, strip.Width - s * 1.5f, strip.Height - s * 1.5f), radius))
    using (var pen = new Pen(A(c, 70 + 150 * breath), s * (1.2f + .6f * (float)breath))) g.DrawPath(pen, path);
   }
   double ts = (now - sweep).TotalSeconds / 1.0;
   if (ts >= 0 && ts <= 1) {
    float width = strip.Width * .5f, x = strip.Left - width + (float)(1 - Math.Pow(1 - ts, 2)) * (strip.Width + width);
    using (var brush = new LinearGradientBrush(new RectangleF(x - 1, strip.Y, width + 2, strip.Height), A(sweepColor, 0), A(sweepColor, 0), 0f)) {
     brush.InterpolationColors = new ColorBlend { Colors = new[] { A(sweepColor, 0), A(sweepColor, 70 * (1 - ts)), A(sweepColor, 0) }, Positions = new[] { 0f, .5f, 1f } };
     using (var clip = Draw.Round(new RectangleF(strip.X, strip.Y, strip.Width, strip.Height), radius)) { var state0 = g.Save(); g.SetClip(clip); g.FillRectangle(brush, x, strip.Y, width, strip.Height); g.Restore(state0); }
    }
   }
   g.SmoothingMode = mode;
  }
 }

 // Short chimes synthesized in memory (no sound files): "done" rises two notes, "needs you" is a brighter three-note arpeggio.
 public static class Chime {
  static byte[] done, waiting; static SoundPlayer player; // kept alive while playing
  struct Note { public double Freq, Start, Decay; public Note(double f, double s, double d) { Freq = f; Start = s; Decay = d; } }
  static byte[] Synth(double seconds, double peak, params Note[] notes) {
   const int rate = 44100; int count = (int)(rate * seconds); var mix = new double[count];
   foreach (var n in notes) {
    int start = (int)(n.Start * rate);
    for (int i = start; i < count; i++) {
     double t = (i - start) / (double)rate;
     double env = (1 - Math.Exp(-t / 0.004)) * Math.Exp(-t / n.Decay);
     // Bell-like timbre: fundamental, soft octave and fifth partials, slight detune for warmth.
     double w = Math.Sin(2 * Math.PI * n.Freq * t) + 0.42 * Math.Sin(2 * Math.PI * n.Freq * 2.0 * t) * Math.Exp(-t / (n.Decay * .5))
      + 0.18 * Math.Sin(2 * Math.PI * n.Freq * 3.01 * t) * Math.Exp(-t / (n.Decay * .3)) + 0.25 * Math.Sin(2 * Math.PI * (n.Freq * 1.003) * t);
     mix[i] += w * env;
    }
   }
   double max = mix.Max(v => Math.Abs(v)); if (max <= 0) max = 1;
   using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms)) {
    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
    for (int i = 0; i < count; i++) {
     double fade = i > count - 2000 ? (count - i) / 2000.0 : 1; // no click at the end
     w.Write((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, mix[i] / max * peak * fade * short.MaxValue)));
    }
    w.Flush(); return ms.ToArray();
   }
  }
  // Peaks around a third of full scale: clearly audible, not startling.
  public static byte[] DoneSound { get { return done ?? (done = Synth(1.1, 0.30, new Note(783.99, 0, 0.42), new Note(1046.5, 0.13, 0.55))); } }
  public static byte[] WaitingSound { get { return waiting ?? (waiting = Synth(1.0, 0.34, new Note(659.25, 0, 0.26), new Note(830.61, 0.1, 0.26), new Note(987.77, 0.2, 0.45), new Note(1318.5, 0.2, 0.3))); } }
  public static void Play(bool needsYou) {
   try {
    if (player != null) player.Dispose();
    player = new SoundPlayer(new MemoryStream(needsYou ? WaitingSound : DoneSound)); player.Play();
   } catch { }
  }
 }
}
