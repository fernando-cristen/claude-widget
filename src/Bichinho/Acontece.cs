namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Linq;

 public partial class Widget {
  // ---- little happenings: a bird lands on the plant, a butterfly crosses, a ladybug walks the day line… ----
  sealed class Happening { public string Kind = ""; public DateTime Start; public double Seconds; public int Seed; }
  Happening happening; DateTime nextHappening = DateTime.Now.AddSeconds(40); readonly Random luck = new Random();
  public static readonly string[] HappeningKinds = { "bird", "butterfly", "ladybug", "leaf", "snail", "star", "firefly" };
  // Where the stage.Critter is (last frame), so the creature can follow it; the leaf can land on its head.
  bool HappeningActive { get { return happening != null && (DateTime.Now - happening.Start).TotalSeconds < happening.Seconds; } }
  // Frequent on purpose: one every 1 to 3 minutes while the strip is visible.
  void HappeningTick() {
   if (!settings.Events || !(settings.Compact || settings.Desk) || preview || stopped || HappeningActive || DateTime.Now < nextHappening) return;
   StartHappening(PickHappening());
  }
  string PickHappening() {
   DateTime n = DateTime.Now;
   if (Night) return luck.Next(2) == 0 ? "star" : "firefly";
   var bag = new List<string> { "bird", "bird", "bird", "butterfly", "butterfly", "butterfly", "ladybug", "ladybug", "snail" };
   if (n.Month >= 3 && n.Month <= 6) { bag.Add("leaf"); bag.Add("leaf"); } // autumn in Brazil
   if (n.DayOfWeek == DayOfWeek.Friday && n.Hour >= 14) { bag.Add("snail"); bag.Add("snail"); bag.Add("snail"); }
   return bag[luck.Next(bag.Count)];
  }
  public void StartHappening(string kind) {
   if (kind == "random") kind = HappeningKinds[luck.Next(HappeningKinds.Length)];
   double seconds = kind == "bird" ? 7 : kind == "butterfly" ? 8 : kind == "ladybug" ? 9 : kind == "leaf" ? 7.5 : kind == "snail" ? 22 : kind == "star" ? 1.8 : 10;
   happening = new Happening { Kind = kind, Start = DateTime.Now, Seconds = seconds, Seed = luck.Next(1000) };
   nextHappening = DateTime.Now.AddSeconds(60 + luck.Next(121));
   StartMotionTimer(); Invalidate();
  }
  void PaintHappening(Graphics g) {
   stage.CritterSeen = false; stage.LeafOnHead = false;
   if (!HappeningActive) return;
   double e = (DateTime.Now - happening.Start).TotalSeconds, p = e / happening.Seconds, t = MotionTime.Now / 1000.0;
   int w = stage.W, h = stage.H; float px = Math.Max(1f, Zs * 1.6f), q = px * 1.5f; // q: small critters drawn bigger
   var mode = g.SmoothingMode;
   try {
    switch (happening.Kind) {
     case "bird": {
      // Flies in, lands on the plant (or on the creature's head), pecks a little, flies off.
      // Lands on the creature's head (riding its bounce) and pecks a little.
      PointF perch = !stage.Head.IsEmpty && settings.Creature ? new PointF(stage.Head.X - px * 3.5f, stage.Crown - px * 5f) : new PointF(w / 2f, h / 3f);
      PointF at; bool flying;
      if (e < 1.6) { double k = MotionTime.Ease(e / 1.6); at = new PointF((float)(-px * 8 + (perch.X + px * 8) * k), (float)(perch.Y - Math.Sin(k * Math.PI) * px * 4 - (1 - k) * px * 2)); flying = true; }
      else if (e < 5.2) { at = perch; flying = false; }
      else { double k = (e - 5.2) / 1.8; at = new PointF((float)(perch.X + (w + px * 8 - perch.X) * k * k), (float)(perch.Y - k * px * 10)); flying = true; }
      PaintBird(g, at, px, flying, !flying && ((int)(t * 3)) % 3 == 0, t);
      stage.Critter = new PointF(at.X + px * 3.5f, at.Y + px * 2); stage.CritterSeen = true;
      break;
     }
     case "butterfly": {
      float x = (float)(-q * 6 + (w + q * 12) * p), y = (float)(h * 0.38 + Math.Sin(t * 2.6 + happening.Seed) * q * 4);
      float flap = (float)Math.Abs(Math.Sin(t * 16)); g.SmoothingMode = SmoothingMode.AntiAlias;
      stage.Critter = new PointF(x, y); stage.CritterSeen = true;
      Color a = Color.FromArgb(230, 250, 170, 210), b = Color.FromArgb(230, 190, 150, 250);
      using (var wing = new SolidBrush(a)) { g.FillEllipse(wing, x - q * 3 * (0.3f + flap), y - q * 2.5f, q * 3 * (0.3f + flap), q * 2.6f); g.FillEllipse(wing, x, y - q * 2.5f, q * 3 * (0.3f + flap), q * 2.6f); }
      using (var wing = new SolidBrush(b)) { g.FillEllipse(wing, x - q * 2 * (0.3f + flap), y, q * 2 * (0.3f + flap), q * 1.8f); g.FillEllipse(wing, x, y, q * 2 * (0.3f + flap), q * 1.8f); }
      using (var body = new SolidBrush(Color.FromArgb(60, 50, 60))) g.FillRectangle(body, x - q * .3f, y - q * 2, q * .6f, q * 3.6f);
      break;
     }
     case "ladybug": {
      // Walks along the day line, then opens its wings and flies off.
      float line = stage.Ground - q * 2.2f; double walk = Math.Min(1, e / 7.5);
      float x = (float)(Pz(14) + (w * 0.7f) * walk), y = e < 7.5 ? line : (float)(line - (e - 7.5) * q * 14);
      g.SmoothingMode = SmoothingMode.AntiAlias; stage.Critter = new PointF(x, y); stage.CritterSeen = true;
      if (e < 7.5) using (var legs = new Pen(Color.FromArgb(40, 30, 30), 1f)) for (int i = 0; i < 3; i++) { float lx = x - q + i * q; g.DrawLine(legs, lx, y + q * 1.6f, lx + (((int)(t * 10) + i) % 2 == 0 ? -q * .5f : q * .5f), y + q * 2.4f); }
      else using (var wings = new SolidBrush(Color.FromArgb(120, 220, 230, 255))) { g.FillEllipse(wings, x - q * 3, y - q * 1.5f, q * 2.6f, q * 2f); g.FillEllipse(wings, x + q * .4f, y - q * 1.5f, q * 2.6f, q * 2f); }
      using (var shell = new SolidBrush(Color.FromArgb(222, 52, 48))) g.FillEllipse(shell, x - q * 1.6f, y, q * 3.2f, q * 2.4f);
      using (var head = new SolidBrush(Color.FromArgb(30, 26, 26))) { g.FillEllipse(head, x + q * 1.2f, y + q * .5f, q * 1.2f, q * 1.2f); g.FillEllipse(head, x - q * .8f, y + q * .5f, q * .6f, q * .6f); g.FillEllipse(head, x + q * .2f, y + q * 1.2f, q * .6f, q * .6f); }
      break;
     }
     case "leaf": {
      // Every other leaf falls right onto the creature's head and rests there for a moment.
      bool onHead = happening.Seed % 2 == 0 && settings.Creature && !stage.Head.IsEmpty;
      float line = onHead ? stage.Crown - q : stage.Ground - Pz(.8f); double fall = Math.Min(1, e / 4.5);
      float home = onHead ? stage.Head.X + (happening.Seed % 7 - 3) * q * .3f : (float)(w * (0.3 + (happening.Seed % 40) / 100.0));
      float x = (float)(home + Math.Sin(e * 2.4) * q * 6 * (onHead ? 1 - fall : 1)), y = (float)(-q * 3 + (line + q * 3) * fall);
      stage.LeafOnHead = onHead && fall >= 1; stage.Critter = new PointF(x, y); stage.CritterSeen = true;
      int alpha = e < 6 ? 235 : (int)(235 * Math.Max(0, 1 - (e - 6) / 1.5));
      g.SmoothingMode = SmoothingMode.AntiAlias; var state = g.Save();
      g.TranslateTransform(x, y); g.RotateTransform(stage.LeafOnHead ? -12 : (float)(Math.Sin(e * 2.4) * 40));
      using (var leaf = new SolidBrush(onHead ? Color.FromArgb(alpha, 246, 206, 70) : Color.FromArgb(alpha, 226, 140, 60))) g.FillEllipse(leaf, -q * 2.2f, -q, q * 4.4f, q * 2f);
      using (var vein = new Pen(onHead ? Color.FromArgb(alpha, 150, 120, 30) : Color.FromArgb(alpha, 170, 90, 40), 1f)) g.DrawLine(vein, -q * 2, 0, q * 2, 0);
      g.Restore(state);
      break;
     }
     case "snail": {
      // Crosses the day line very slowly, from right to left, leaving a shiny trail.
      float line = stage.Ground - px * 3f, start = w - Pz(16), x = (float)(start - (w * 0.55f) * p);
      g.SmoothingMode = SmoothingMode.AntiAlias;
      using (var trail = new LinearGradientBrush(new RectangleF(x, line + px * 2.4f, start - x + 2, px), Color.FromArgb(90, 210, 230, 240), Color.FromArgb(0, 210, 230, 240), 0f)) g.FillRectangle(trail, x, line + px * 2.4f, start - x + 1, px * .6f);
      float stretch = (float)Math.Sin(t * 2) * px * .4f; stage.Critter = new PointF(x, line); stage.CritterSeen = true;
      using (var body = new SolidBrush(Color.FromArgb(170, 196, 150))) g.FillEllipse(body, x - px * .8f - stretch, line + px * 1.4f, px * 5.6f + stretch, px * 1.4f);
      using (var eye = new Pen(Color.FromArgb(170, 196, 150), 1f)) { g.DrawLine(eye, x - px * .2f - stretch, line + px * 1.6f, x - px * .9f - stretch, line - px * .2f); g.DrawLine(eye, x + px * .3f - stretch, line + px * 1.6f, x - px * .1f - stretch, line - px * .4f); }
      using (var shell = new SolidBrush(Color.FromArgb(180, 120, 70))) g.FillEllipse(shell, x + px * 1.2f, line - px * .4f, px * 3.4f, px * 3f);
      using (var spiral = new Pen(Color.FromArgb(240, 220, 170), 1f)) { g.DrawArc(spiral, x + px * 1.8f, line + px * .2f, px * 2.2f, px * 1.9f, 180, 270); g.DrawArc(spiral, x + px * 2.4f, line + px * .7f, px * 1f, px * .9f, 0, 270); }
      break;
     }
     case "star": {
      // A shooting star across the strip.
      float x = (float)(w * (1.05 - 1.3 * p)), y = (float)(h * (0.08 + 0.55 * p)), len = Pz(46);
      g.SmoothingMode = SmoothingMode.AntiAlias; stage.Critter = new PointF(x, y); stage.CritterSeen = true;
      using (var tail = new LinearGradientBrush(new PointF(x, y), new PointF(x + len, y - len * .32f), Color.FromArgb((int)(230 * (1 - p * .5)), 255, 250, 230), Color.FromArgb(0, 255, 250, 230)))
      using (var pen = new Pen(tail, Pz(1.6f))) g.DrawLine(pen, x, y, x + len, y - len * .32f);
      using (var head = new SolidBrush(Color.FromArgb(255, 255, 252, 240))) g.FillEllipse(head, x - Pz(1.5f), y - Pz(1.5f), Pz(3), Pz(3));
      break;
     }
     case "firefly": {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      for (int i = 0; i < 5; i++) {
       double s = (happening.Seed + i * 37) % 100 / 100.0;
       float x = (float)(w * (0.1 + 0.8 * ((s + e * 0.03 * (i % 2 == 0 ? 1 : -1)) % 1.0 + 1) % 1.0) + Math.Sin(t * 1.3 + i) * px * 5), y = (float)(h * (0.25 + 0.45 * s) + Math.Cos(t * 1.1 + i * 2) * px * 3);
       double blink = Math.Max(0, Math.Sin(t * 2.2 + i * 1.7)) * Math.Min(1, Math.Min(e, happening.Seconds - e));
       using (var glow = new GraphicsPath()) { float r = px * 3.5f; glow.AddEllipse(x - r, y - r, r * 2, r * 2); using (var b = new PathGradientBrush(glow) { CenterColor = Color.FromArgb((int)(150 * blink), 220, 255, 120), SurroundColors = new[] { Color.FromArgb(0, 220, 255, 120) } }) g.FillPath(b, glow); }
       using (var dot = new SolidBrush(Color.FromArgb((int)(255 * blink), 240, 255, 180))) g.FillEllipse(dot, x - px * .6f, y - px * .6f, px * 1.2f, px * 1.2f);
       // The creature follows the closest one.
       if (!stage.Head.IsEmpty && (!stage.CritterSeen || Math.Abs(x - stage.Head.X) < Math.Abs(stage.Critter.X - stage.Head.X))) { stage.Critter = new PointF(x, y); stage.CritterSeen = true; }
      }
      break;
     }
    }
   } finally { g.SmoothingMode = mode; }
  }
  void PaintBird(Graphics g, PointF at, float px, bool flying, bool peck, double t) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
   var p = new Pix(g, at.X, at.Y, px);
   try {
    Color body = Color.FromArgb(96, 150, 226), belly = Color.FromArgb(236, 230, 214), wing = Color.FromArgb(70, 110, 190), beak = Color.FromArgb(246, 170, 60), eye = Color.FromArgb(24, 24, 28);
    p.Row(2, 1, 4, body); p.Row(3, 1, 4, belly); p.Dot(0, 2, wing); // body and tail
    int hy = peck ? 2 : 1; p.Dot(4, hy, body); p.Dot(5, hy, body); p.Dot(5, hy - 1, body); p.Dot(5, hy - 1 + 0, eye); p.Dot(6, hy, beak);
    if (flying) { bool up = ((int)(t * 12)) % 2 == 0; if (up) { p.Dot(2, 1, wing); p.Dot(2, 0, wing); p.Dot(3, 1, wing); } else { p.Dot(2, 4, wing); p.Dot(3, 4, wing); } }
    else { p.Dot(2, 2, wing); p.Dot(3, 2, wing); p.Dot(2, 4, beak); p.Dot(4, 4, beak); }
   } finally { p.Dispose(); g.SmoothingMode = mode; }
  }

  // ---- constellation of today's commits on the day line: dim star = committed, bright = pushed, pushed ones linked ----
  DateTime constellationAt = DateTime.MinValue;
  bool DayPoint(int minute, out float x, out float y) {
   x = 0; y = Height - Px(3.2f);
   int left = Px(12), right = StripWidth - Px(12), gap = Px(7);
   var segments = WorkDay.Windows.Select(w => new[] { (int)w[0].TotalMinutes, (int)w[1].TotalMinutes }).ToList();
   int total = segments.Sum(s => s[1] - s[0]); float span = right - left - gap * (segments.Count - 1); if (span <= 0 || total <= 0) return false;
   // Commits outside the windows go to the nearest edge (lunch → end of the morning).
   if (minute < segments[0][0]) minute = segments[0][0];
   for (int i = 0; i + 1 < segments.Count; i++) if (minute >= segments[i][1] && minute < segments[i + 1][0]) minute = segments[i][1] - 1;
   if (minute >= segments[segments.Count - 1][1]) minute = segments[segments.Count - 1][1] - 1;
   float x0 = left;
   foreach (var s in segments) {
    float width = span * (s[1] - s[0]) / total;
    if (minute >= s[0] && minute < s[1]) { x = x0 + width * (minute - s[0]) / (s[1] - s[0]); return true; }
    x0 += width + gap;
   }
   return false;
  }
  void PaintConstellation(Graphics g) {
   if (!settings.DayLine || gitInfo.TodayCommits.Count == 0) return;
   var pushed = new HashSet<long>(gitInfo.TodayPushed);
   var stars = new List<KeyValuePair<PointF,bool>>();
   foreach (long unix in gitInfo.TodayCommits.OrderBy(v => v)) {
    var local = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().DateTime; if (local.Date != DateTime.Today) continue;
    float x, y; if (!DayPoint(local.Hour * 60 + local.Minute, out x, out y)) continue;
    stars.Add(new KeyValuePair<PointF,bool>(new PointF(x, y - Px(6)), pushed.Contains(unix)));
   }
   if (stars.Count == 0) return;
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   // Light threads between pushed stars; right after a push they draw themselves.
   var lit = stars.Where(s => s.Value).Select(s => s.Key).ToList();
   double draw = Math.Min(1, (DateTime.Now - constellationAt).TotalSeconds / 1.4);
   if (lit.Count > 1) {
    float lengthTotal = 0; for (int i = 1; i < lit.Count; i++) lengthTotal += Distance(lit[i - 1], lit[i]);
    float budget = (float)(lengthTotal * draw);
    using (var pen = new Pen(Color.FromArgb(120, 255, 236, 200), Math.Max(1f, Px(.8f)))) {
     for (int i = 1; i < lit.Count && budget > 0; i++) {
      float d = Distance(lit[i - 1], lit[i]), k = Math.Min(1, budget / Math.Max(1, d));
      g.DrawLine(pen, lit[i - 1], new PointF(lit[i - 1].X + (lit[i].X - lit[i - 1].X) * k, lit[i - 1].Y + (lit[i].Y - lit[i - 1].Y) * k));
      budget -= d;
     }
    }
   }
   for (int i = 0; i < stars.Count; i++) {
    var s = stars[i]; float twinkle = (float)(0.75 + 0.25 * Math.Sin(t * 2.5 + i * 1.7)), r = Px(s.Value ? 2.6f : 2f) * twinkle;
    Color c = s.Value ? Color.FromArgb(255, 255, 240, 200) : Color.FromArgb(150, 220, 220, 230);
    if (s.Value) using (var glow = new GraphicsPath()) { glow.AddEllipse(s.Key.X - r * 2.2f, s.Key.Y - r * 2.2f, r * 4.4f, r * 4.4f); using (var b = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(110, 255, 220, 150), SurroundColors = new[] { Color.FromArgb(0, 255, 220, 150) } }) g.FillPath(b, glow); }
    using (var star = new SolidBrush(c)) g.FillPolygon(star, new[] { new PointF(s.Key.X, s.Key.Y - r), new PointF(s.Key.X + r * .28f, s.Key.Y - r * .28f), new PointF(s.Key.X + r, s.Key.Y), new PointF(s.Key.X + r * .28f, s.Key.Y + r * .28f), new PointF(s.Key.X, s.Key.Y + r), new PointF(s.Key.X - r * .28f, s.Key.Y + r * .28f), new PointF(s.Key.X - r, s.Key.Y), new PointF(s.Key.X - r * .28f, s.Key.Y - r * .28f) });
   }
   g.SmoothingMode = mode;
  }
  static float Distance(PointF a, PointF b) { return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

  // ---- usage tubes: drain in a whirlpool when the limit renews ----
  readonly double?[] lastUsage = new double?[2]; readonly DateTime[] drainAt = { DateTime.MinValue, DateTime.MinValue }; readonly double[] drainFrom = new double[2];
  bool DrainActive { get { return (DateTime.Now - drainAt[0]).TotalSeconds < 1.8 || (DateTime.Now - drainAt[1]).TotalSeconds < 1.8; } }
  void TrackUsageDrain() {
   for (int i = 0; i < 2; i++) {
    double? now = buckets.Count > i ? buckets[i].Percent : null;
    if (now.HasValue && lastUsage[i].HasValue && lastUsage[i].Value - now.Value >= 30) { drainAt[i] = DateTime.Now; drainFrom[i] = lastUsage[i].Value; StartMotionTimer(); }
    if (now.HasValue) lastUsage[i] = now;
   }
  }
  public void TestDrain() { for (int i = 0; i < 1; i++) { drainAt[i] = DateTime.Now; drainFrom[i] = Math.Max(85, buckets.Count > i && buckets[i].Percent.HasValue ? buckets[i].Percent.Value : 85); } StartMotionTimer(); }
  void PaintUsagePill(Graphics g, int i, Rectangle pill, double? value, Color color, float radius) {
   double p = (DateTime.Now - drainAt[i]).TotalSeconds / 1.8;
   if (p < 0 || p >= 1 || !settings.Liquid) { PaintPill(g, pill, value, color, radius); return; }
   double shown = p < .5 ? drainFrom[i] * (1 - MotionTime.Ease(p * 2)) : (value ?? 0) * MotionTime.Ease((p - .5) * 2);
   PaintPill(g, pill, shown, p < .5 ? color : LevelColor(value ?? 0), radius);
   // Whirlpool while it empties.
   if (p < .6) {
    var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
    var saved = g.Save(); using (var clip = Rounded(pill, radius)) g.SetClip(clip, CombineMode.Intersect);
    float cx = pill.X + pill.Width * .2f, cy = pill.Y + pill.Height / 2f; double spin = MotionTime.Now / 1000.0 * 720;
    for (int k = 0; k < 3; k++) {
     float r = pill.Height * (0.18f + k * 0.16f);
     using (var pen = new Pen(Color.FromArgb((int)(170 * (1 - p / .6)), 255, 255, 255), 1f)) g.DrawArc(pen, cx - r * 1.6f, cy - r, r * 3.2f, r * 2, (float)(spin + k * 120), 200);
    }
    g.Restore(saved); g.SmoothingMode = mode;
   }
  }
 }
}
