namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;
 using System.IO;
 using System.Linq;
 using System.Threading.Tasks;
 using Microsoft.Win32;

 // The day as one character per minute: '0' nothing, '1' Claude working, '2' Claude waiting for you. Kept for today only.
 public sealed class DayTrack {
  public string Date = "", Minutes = "";
  static string FilePath { get { return Path.Combine(Data.Folder, "dia.json"); } }
  public static DayTrack Load() {
   try { var d = Data.Json().Deserialize<DayTrack>(File.ReadAllText(FilePath)); if (d != null && d.Minutes != null && d.Minutes.Length == 1440) return d; } catch { }
   return new DayTrack();
  }
  public void Mark(DateTime now, int state) {
   string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   if (Date != today || Minutes == null || Minutes.Length != 1440) { Date = today; Minutes = new string('0', 1440); }
   int m = now.Hour * 60 + now.Minute; char c = (char)('0' + state);
   if (Minutes[m] < c) Minutes = Minutes.Substring(0, m) + c + Minutes.Substring(m + 1); // within a minute, "waiting" beats "working"
  }
  public int At(DateTime day, int minute) {
   if (Date != day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) || Minutes == null || Minutes.Length != 1440) return 0;
   return Minutes[minute] - '0';
  }
  public void Save() { try { Directory.CreateDirectory(Data.Folder); File.WriteAllText(FilePath, Data.Json().Serialize(this)); } catch { } }
 }

 public partial class Widget {
  static readonly Color ClaudeOrange = Color.FromArgb(216,156,125);
  DayTrack dayTrack = new DayTrack(); DateTime dayTrackSaved = DateTime.MinValue;
  bool wasNight; DateTime wakeAt = DateTime.MinValue; double loggedToday = -1; string loggedDay = ""; bool loggedBusy;
  string mood = "idle"; DateTime moodSince = DateTime.Now;

  // A5: after the workday (and on weekends) the strip rests: dimmer, still, the creature sleeps.
  internal bool ForceNight { get; set; } // test renders only
  bool Night {
   get {
    if (ForceNight) return true;
    if (!settings.NightMode || preview) return false;
    DateTime n = DateTime.Now;
    return !WorkDay.IsWorkday(n) || n.TimeOfDay < WorkDay.Windows[0][0] || n >= WorkDay.DayEnd(n);
   }
  }
  bool SessionBusy { get { return activeSession != null && activeSession.Active; } }
  // Continuous motion (liquid, creature) only in the daytime; at night the strip is still and costs nothing.
  bool LiveDecor { get { return settings.Animations && settings.Compact && !preview && !Night && (settings.Liquid || settings.Creature); } }

  void InitStyle() {
   if (preview) return;
   dayTrack = DayTrack.Load(); wasNight = Night;
   RefreshAccent();
   SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
   InitDesk();
  }
  void DisposeStyle() { SystemEvents.UserPreferenceChanged -= OnPreferenceChanged; dayTrack.Save(); DisposeDesk(); }
  void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) {
   if (IsHandleCreated && !stopped) BeginInvoke(new Action(RefreshAccent));
  }
  // A6: the Windows accent (its light variant, readable on dark), unless it is a gray: then Claude orange.
  public static Color? WindowsAccent() {
   try {
    var palette = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", null) as byte[];
    if (palette == null || palette.Length < 32) return null;
    var color = Color.FromArgb(palette[4], palette[5], palette[6]);
    return color.GetSaturation() < 0.18f ? (Color?)null : color;
   } catch { return null; }
  }
  void RefreshAccent() {
   Color next = (settings.WindowsAccent ? WindowsAccent() : null) ?? ClaudeOrange;
   accent = next; Theme.Accent = next; Ambient.Working = next;
   Invalidate();
  }
  // Called on every radar read (2 s): what the Claude sessions are doing this minute.
  void TrackDay() {
   if (preview) return;
   DateTime now = DateTime.Now;
   int state = sessions.Any(s => s.Active && s.State == "waiting") ? 2 : sessions.Any(s => s.Active && (s.State == "working" || s.State == "background")) ? 1 : 0;
   dayTrack.Mark(now, state);
   if ((now - dayTrackSaved).TotalSeconds >= 60) { dayTrackSaved = now; dayTrack.Save(); }
  }
  // Once a second from the main timer: wake-up glow, today's logged hours for the night line, keep the motion alive.
  void StyleTick() {
   bool night = Night;
   if (wasNight && !night) wakeAt = DateTime.Now;
   wasNight = night;
   if (night && !loggedBusy) {
    string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    if (loggedDay != today) FetchLoggedToday(today);
   }
   BichoTick();
   if (LiveDecor || (DateTime.Now - wakeAt).TotalSeconds < 1.6) StartMotionTimer();
  }
  async void FetchLoggedToday(string today) {
   loggedBusy = true; loggedDay = today;
   try {
    using (var client = RedmineClient.FromMcpConfig()) {
     if (client == null) { loggedToday = -1; return; }
     var entries = await Score.Entries(client, DateTime.Today);
     loggedToday = entries.Where(e => e.Day == DateTime.Today).Sum(e => e.Hours);
    }
   } catch { loggedToday = -1; loggedDay = ""; }
   finally { loggedBusy = false; Invalidate(); }
  }
  bool NightIdle { get { return Night && !SessionBusy && DateTime.Now >= bannerUntil; } }
  string NightTitle { get { return WorkDay.IsWorkday(DateTime.Now) ? "☾ Expediente encerrado" : "☾ Fim de semana"; } }
  string NightDetail { get { return loggedToday >= 0 && WorkDay.IsWorkday(DateTime.Now) ? WorkDay.Duration(loggedToday) + " apontadas hoje · bom descanso" : "bom descanso"; } }

  // A1: usage pill as a tube of liquid: level color, two waves on the surface, a few bubbles and a glassy top.
  Color LevelColor(double value) { return value < 60 ? Color.FromArgb(78,206,186) : value < 85 ? amber : red; }
  void PaintPill(Graphics g, Rectangle pill, double? value, Color color, float radius) {
   if (UseSkia && PaintPillSkia(g, pill, value, color, radius)) return;
   using (var shape = Rounded(pill, radius)) {
    using (var background = new SolidBrush(Color.FromArgb(48,50,53))) g.FillPath(background, shape);
    var saved = g.Save(); g.SetClip(shape, CombineMode.Intersect);
    try {
     float level = value.HasValue ? (float)(pill.Width * Math.Min(100, Math.Max(0, value.Value)) / 100) : 0;
     if (level > 0 && !settings.Liquid) using (var fill = new SolidBrush(Color.FromArgb(95, color))) g.FillRectangle(fill, pill.X, pill.Y, level, pill.Height);
     else if (level > 0) {
      double t = (settings.Animations && !Night ? MotionTime.Now : 0) / 1000.0;
      float amp = Math.Max(1f, Px(1.7f));
      for (int layer = 1; layer >= 0; layer--) {
       var points = new List<PointF> { new PointF(pill.X - 1, pill.Y) };
       for (int k = 0; k <= 12; k++) {
        float y = pill.Y + pill.Height * k / 12f;
        double phase = t * (layer == 0 ? 2.4 : 1.7) + k * (layer == 0 ? 0.85 : 1.25) + layer * 1.9;
        points.Add(new PointF(pill.X + level + (float)Math.Sin(phase) * amp * (layer == 0 ? 1f : .75f) - layer * Px(1.5f), y));
       }
       points.Add(new PointF(pill.X - 1, pill.Bottom));
       using (var body = new GraphicsPath()) {
        body.AddPolygon(points.ToArray());
        if (layer == 1) using (var back = new SolidBrush(Color.FromArgb(55, color))) g.FillPath(back, body);
        else using (var front = new LinearGradientBrush(new RectangleF(pill.X, pill.Y - 1, pill.Width, pill.Height + 2), Color.FromArgb(150, color), Color.FromArgb(85, color), 90f)) g.FillPath(front, body);
       }
      }
      // Bubbles rising through the liquid.
      if (level > Px(14) && settings.Animations && !Night)
       for (int i = 0; i < 3; i++) {
        double p = (t * (0.35 + i * 0.12) + i * 0.37) % 1.0;
        float bx = pill.X + level * (0.22f + 0.27f * i) + (float)Math.Sin(t * 2 + i) * Px(1), by = pill.Bottom - (float)p * pill.Height, br = Math.Max(1f, Px(1.1f));
        using (var bubble = new SolidBrush(Color.FromArgb((int)(110 * (1 - p)), 255, 255, 255))) g.FillEllipse(bubble, bx - br, by - br, br * 2, br * 2);
       }
      // Boiling near the limit: big fast bubbles all over the liquid.
      if (value.HasValue && value.Value >= 90 && settings.Animations && !Night)
       for (int i = 0; i < 6; i++) {
        double p = (t * (1.3 + i * 0.21) + i * 0.29) % 1.0;
        float bx = pill.X + level * ((i * 0.173f + 0.08f) % 1f), by = pill.Bottom - (float)p * pill.Height, br = Math.Max(1.2f, Px(1.3f + (i % 3) * .35f));
        using (var bubble = new Pen(Color.FromArgb((int)(170 * (1 - p)), 255, 255, 255), 1f)) g.DrawEllipse(bubble, bx - br, by - br, br * 2, br * 2);
       }
     }
     // Glassy top of the tube.
     using (var shine = new LinearGradientBrush(new RectangleF(pill.X, pill.Y - 1, pill.Width, pill.Height * .5f + 2), Color.FromArgb(42, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
      g.FillRectangle(shine, pill.X, pill.Y, pill.Width, pill.Height * .5f);
    } finally { g.Restore(saved); }
    using (var rim = new Pen(Color.FromArgb(26, 255, 255, 255))) g.DrawPath(rim, shape);
    // Steam curling up from a boiling tube.
    if (settings.Liquid && value.HasValue && value.Value >= 90 && settings.Animations && !Night) {
     double t = MotionTime.Now / 1000.0; var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
     float level = (float)(pill.Width * Math.Min(100, value.Value) / 100);
     for (int i = 0; i < 3; i++) {
      double p = (t * 0.55 + i / 3.0) % 1.0; float x = pill.X + level * (0.3f + i * 0.25f) + (float)Math.Sin(t * 3 + i * 2) * Px(2), y = pill.Y + pill.Height * .4f - (float)p * Px(9);
      using (var steam = new Pen(Color.FromArgb((int)(120 * Math.Sin(p * Math.PI)), 236, 236, 242), Math.Max(1f, Px(1.2f)))) g.DrawArc(steam, x - Px(1.5f), y - Px(2), Px(3), Px(4), 90 + (float)(p * 180), 160);
     }
     g.SmoothingMode = mode;
    }
   }
  }

  // A3: the workday along the bottom of the strip (08:00–12:00 · 13:12–18:00): orange while Claude worked,
  // amber while it waited for you, faint when idle, dimmer for what is still ahead, and a glowing "now".
  void PaintDayLine(Graphics g) {
   if (!settings.DayLine || preview) return;
   if (UseSkia && PaintDayLineSkia(g)) return;
   DateTime now = DateTime.Now, today = now.Date;
   int left = Px(12), right = StripWidth - Px(12), gap = Px(7);
   var segments = WorkDay.Windows.Select(w => new[] { (int)w[0].TotalMinutes, (int)w[1].TotalMinutes }).ToList();
   int total = segments.Sum(s => s[1] - s[0]); float span = right - left - gap * (segments.Count - 1); if (span <= 0 || total <= 0) return;
   float y = Height - Px(3.2f), thick = Math.Max(1.5f, Px(1.7f)); int nowMinute = now.Hour * 60 + now.Minute;
   bool workday = WorkDay.IsWorkday(now);
   float x0 = left; PointF? nowPoint = null;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   foreach (var s in segments) {
    float width = span * (s[1] - s[0]) / total;
    int runStart = s[0]; int runKind = Kind(today, s[0], nowMinute, workday);
    for (int m = s[0] + 1; m <= s[1]; m++) {
     int kind = m < s[1] ? Kind(today, m, nowMinute, workday) : -9;
     if (kind == runKind) continue;
     float a = x0 + width * (runStart - s[0]) / (s[1] - s[0]), b = x0 + width * (m - s[0]) / (s[1] - s[0]);
     Color c = runKind == 1 ? Color.FromArgb(235, accent) : runKind == 2 ? Color.FromArgb(235, amber) : runKind == 0 ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(16, 255, 255, 255);
     using (var pen = new Pen(c, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(pen, a + thick / 2, y, Math.Max(a + thick / 2, b - thick / 2), y);
     runStart = m; runKind = kind;
    }
    if (workday && nowMinute >= s[0] && nowMinute < s[1]) nowPoint = new PointF(x0 + width * (nowMinute - s[0] + now.Second / 60f) / (s[1] - s[0]), y);
    x0 += width + gap;
   }
   if (nowPoint.HasValue) {
    var p = nowPoint.Value; float glow = Px(5) + (settings.Animations ? (float)Math.Sin(MotionTime.Now / 420.0) * Px(1) : 0);
    using (var path = new GraphicsPath()) {
     path.AddEllipse(p.X - glow, p.Y - glow, glow * 2, glow * 2);
     using (var halo = new PathGradientBrush(path) { CenterColor = Color.FromArgb(150, accent), SurroundColors = new[] { Color.FromArgb(0, accent) } }) g.FillPath(halo, path);
    }
    float core = Math.Max(1.6f, Px(2));
    using (var dot = new SolidBrush(Color.FromArgb(255, 250, 240))) g.FillEllipse(dot, p.X - core, p.Y - core, core * 2, core * 2);
   }
   g.SmoothingMode = mode;
  }
  int Kind(DateTime today, int minute, int nowMinute, bool workday) { return !workday || minute > nowMinute ? -1 : dayTrack.At(today, minute); }

  // Time on the issue against its estimate: < 70% green, up to 100% amber (pulsing from 85%), beyond it red with the overrun outside.
  int ringLevel = -1, ringTask; DateTime ringCrossed = DateTime.MinValue;
  double TaskFraction(ActiveIssue t, out double total) { taskTotal.TryGetValue(t.Id, out total); return t.Estimated > 0 ? total / t.Estimated : -1; }
  Color TaskColor(double f) { return f >= 1 ? red : f >= 0.7 ? amber : green; }
  static string Hours(double h) { return Math.Abs(h - Math.Round(h)) < 0.01 ? Math.Round(h) + "h" : WorkDay.Duration(h); }
  // One ripple when 85% and 100% are crossed (not when the widget opens already past them).
  void TrackThreshold(ActiveIssue t, double f) {
   int level = f >= 1 ? 2 : f >= 0.85 ? 1 : 0;
   if (t.Id != ringTask) { ringTask = t.Id; ringLevel = level; return; }
   if (level > ringLevel) ringCrossed = DateTime.Now;
   ringLevel = level;
  }
  void PaintTaskRing(Graphics g, PointF c, float r, double f) {
   if (UseSkia && PaintTaskRingSkia(g, c, r, f)) return;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   Color color = TaskColor(f); float thick = Math.Max(2f, Px(2.6f));
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0;
   double pulse = f >= 0.85 ? (Math.Sin(t * (f >= 1 ? 4.2 : 2.6)) + 1) / 2 : 0;
   // Soft halo that breathes when close to or past the estimate.
   if (pulse > 0) using (var path = new GraphicsPath()) {
    float glow = r + Px(5) + (float)pulse * Px(2); path.AddEllipse(c.X - glow, c.Y - glow, glow * 2, glow * 2);
    using (var halo = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(40 + 70 * pulse), color), SurroundColors = new[] { Color.FromArgb(0, color) } }) g.FillPath(halo, path);
   }
   var box = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
   using (var track = new Pen(Color.FromArgb(38, 255, 255, 255), thick)) g.DrawEllipse(track, box);
   float sweep = (float)(360 * Math.Min(1, Math.Max(0.02, f)));
   using (var arc = new Pen(color, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, box, -90, sweep);
   // Bright head of the arc, like the hand of a timer.
   double head = (-90 + sweep) * Math.PI / 180; float hx = c.X + r * (float)Math.Cos(head), hy = c.Y + r * (float)Math.Sin(head), hr = thick * .55f;
   if (f < 1) using (var dot = new SolidBrush(Color.FromArgb(255, 255, 248, 236))) g.FillEllipse(dot, hx - hr, hy - hr, hr * 2, hr * 2);
   // Past the estimate: the overrun as a second arc outside (one full turn = another estimate).
   if (f > 1) {
    float outer = r + thick + Px(1.2f); var obox = new RectangleF(c.X - outer, c.Y - outer, outer * 2, outer * 2);
    using (var over = new Pen(Color.FromArgb(200, red), Math.Max(1.5f, thick * .7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(over, obox, -90, (float)(360 * Math.Min(1, f - 1)));
   }
   // Ripple right after crossing 85% or 100%.
   double rp = (DateTime.Now - ringCrossed).TotalSeconds / 1.3;
   if (rp >= 0 && rp <= 1) {
    float rr = r + Px(2) + (float)(1 - Math.Pow(1 - rp, 3)) * Px(10);
    using (var ring = new Pen(Color.FromArgb((int)(220 * (1 - rp)), color), Px(1.6f))) g.DrawEllipse(ring, c.X - rr, c.Y - rr, rr * 2, rr * 2);
   }
   g.SmoothingMode = mode;
  }
  // Dense strip, task column: number, hours over the estimate and the ring.
  void PaintDenseTask(Graphics g, Rectangle task, ActiveIssue current, Font normal, Font bold, Font tiny, TextFormatFlags flags) {
   double total; double f = TaskFraction(current, out total);
   int ring = f >= 0 ? Px(25) : 0;
   var text = new Rectangle(task.X, 0, task.Width - ring, Height);
   StripText(g, "#" + current.Id, normal, new Rectangle(text.X, Px(4), text.Width, Px(15)), green, flags);
   if (f < 0) { StripText(g, WorkDay.Duration(total) + (current.Spans.Count > 1 ? "  " + current.Spans.Count + "x" : ""), bold, new Rectangle(text.X, Px(21), text.Width, Px(15)), ink, flags); return; }
   TrackThreshold(current, f);
   string hours = WorkDay.Duration(total); int w = TextRenderer.MeasureText(g, hours, bold, Size.Empty, TextFormatFlags.NoPadding).Width;
   StripText(g, hours, bold, new Rectangle(text.X, Px(21), text.Width, Px(15)), f >= 1 ? red : ink, flags);
   StripText(g, "/ " + Hours(current.Estimated), tiny, new Rectangle(text.X + w + Px(3), Px(22), Math.Max(0, text.Width - w - Px(3)), Px(14)), muted, flags);
   PaintTaskRing(g, new PointF(task.Right - Px(11), Height / 2f), Px(8.5f), f);
  }
  // Full strip, task pill: fills up to the estimate like the usage tubes; the caption says how far along or how much beyond.
  bool PaintTaskPill(Graphics g, ActiveIssue task, Rectangle pill, out string caption, out Color captionColor) {
   double total; double f = TaskFraction(task, out total); caption = ""; captionColor = Color.FromArgb(175,177,182);
   if (f < 0) return false;
   TrackThreshold(task, f);
   PaintPill(g, pill, Math.Min(100, f * 100), TaskColor(f), Px(8));
   if (f >= 0.85) {
    double pulse = settings.Animations && !Night ? (Math.Sin(MotionTime.Now / 1000.0 * (f >= 1 ? 4.2 : 2.6)) + 1) / 2 : 0.5;
    using (var shape = Rounded(new RectangleF(pill.X - .5f, pill.Y - .5f, pill.Width + 1, pill.Height + 1), Px(8.5f)))
    using (var pen = new Pen(Color.FromArgb((int)(60 + 120 * pulse), TaskColor(f)), Px(1.2f))) g.DrawPath(pen, shape);
   }
   if (f >= 1) { caption = "+" + WorkDay.Duration(total - task.Estimated) + " além de " + Hours(task.Estimated); captionColor = red; }
   else caption = "de " + Hours(task.Estimated) + " · " + Math.Round(f * 100) + "%";
   return true;
  }

  // A5 dim layer and the wake-up glow at 08:00.
  void PaintNightAndWake(Graphics g) {
   if (Night) using (var veil = new SolidBrush(Color.FromArgb(95, 6, 6, 10))) g.FillRectangle(veil, 0, 0, StripWidth, Height);
   double w = (DateTime.Now - wakeAt).TotalSeconds / 1.4;
   if (w >= 0 && w <= 1) {
    float width = StripWidth * .6f, x = -width + (float)(1 - Math.Pow(1 - w, 2)) * (StripWidth + width);
    using (var brush = new LinearGradientBrush(new RectangleF(x - 1, 0, width + 2, Height), Color.FromArgb(0, accent), Color.FromArgb(0, accent), 0f)) {
     brush.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, accent), Color.FromArgb((int)(90 * (1 - w)), accent), Color.FromArgb(0, accent) }, Positions = new[] { 0f, .5f, 1f } };
     g.FillRectangle(brush, x, 0, width, Height);
    }
   }
  }
 }
}
