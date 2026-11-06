namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;
 using System.Linq;
 using System.Text.RegularExpressions;
 using System.Threading.Tasks;
 using System.Windows.Forms;

 // Pixel-art pen: whole pixels on a grid, so motion stays crisp.
 // Where the creature is painted (taskbar strip or desktop) and what it remembers between frames there.
 sealed class Stage {
  public float Zoom = 1; public int W, H; public float Ground; public Control Surface; public bool Hover;
  public RectangleF Creature = RectangleF.Empty; public PointF Head = PointF.Empty; public float Crown, ChaseX; public double ChaseClock;
  public PointF Critter; public bool CritterSeen, LeafOnHead;
  // Desktop only: carried by the mouse (Swing follows the drag), the landing after it, walking and the pose.
  public bool Held, Walking; public float Swing; public DateTime LandAt = DateTime.MinValue;
  // Set by the games: a pose to hold and a point to look at.
  public string Act = ""; public PointF LookTarget = PointF.Empty;
  public string Pose = ""; // "", "sit" (on a window edge, legs dangling), "knock", "jump", "bed"
 }

 sealed class Pix {
  readonly Graphics g; readonly float ox, oy, px; readonly Dictionary<int,SolidBrush> brushes = new Dictionary<int,SolidBrush>();
  public Pix(Graphics g, float ox, float oy, float px) { this.g = g; this.ox = ox; this.oy = oy; this.px = px; }
  public PointF At(float cx, float cy) { return new PointF(ox + cx * px, oy + cy * px); }
  public void Dot(float cx, float cy, Color c) {
   SolidBrush b; int key = c.ToArgb();
   if (!brushes.TryGetValue(key, out b)) brushes[key] = b = new SolidBrush(c);
   g.FillRectangle(b, ox + cx * px, oy + cy * px, px + .5f, px + .5f);
  }
  public void Row(float y, int x0, int x1, Color c) { for (int x = x0; x <= x1; x++) Dot(x, y, c); }
  public void Dispose() { foreach (var b in brushes.Values) b.Dispose(); }
 }

 // What the Windows weather button on the taskbar says ("Widgets 21°C Pred. nublado").
 public sealed class WeatherNow {
  public int? Temp; public string Kind = ""; // sun, rain, snow, cloud, fog, storm
  public static WeatherNow Parse(string text) {
   var w = new WeatherNow(); if (String.IsNullOrEmpty(text)) return w;
   var m = Regex.Match(text, "(-?\\d+)\\s*°"); int t; if (m.Success && Int32.TryParse(m.Groups[1].Value, out t)) w.Temp = t;
   string s = text.ToLowerInvariant();
   if (Regex.IsMatch(s, "trovoada|tempestade|raio")) w.Kind = "storm";
   else if (Regex.IsMatch(s, "chuv|garoa|aguaceiro|pancada")) w.Kind = "rain";
   else if (Regex.IsMatch(s, "neve|nevasca|granizo")) w.Kind = "snow";
   else if (Regex.IsMatch(s, "neblina|névoa|nevoeiro|bruma")) w.Kind = "fog";
   else if (Regex.IsMatch(s, "ensolarad|sol|limpo|claro")) w.Kind = "sun";
   else if (Regex.IsMatch(s, "nublad|nuvens|encoberto")) w.Kind = "cloud";
   return w;
  }
 }

 public partial class Widget {
  WeatherNow weather = new WeatherNow(); DateTimeOffset nextWeather = DateTimeOffset.MinValue; bool weatherBusy;
  string pushRepo = ""; int pushAhead = -1;
  string batterySession = ""; double batteryCtx = -1; DateTime rechargeAt = DateTime.MinValue;
  string streakDay = ""; bool streakBusy;
  // Creature interactions and events.
  readonly List<DateTime> pokes = new List<DateTime>(); DateTime hopAt = DateTime.MinValue, dizzyUntil = DateTime.MinValue;
  DateTime failAt = DateTime.MinValue, cheerAt = DateTime.MinValue, levelUpAt = DateTime.MinValue; string failKey = "", mainState = "";
  // The creature is painted on two stages: the taskbar strip and the desktop pet (bigger). Each keeps its own geometry.
  readonly Stage strip = new Stage(), desk = new Stage { Zoom = 2.8f }; Stage current;
  Stage stage { get { return current ?? strip; } }
  float Zs { get { return ScaleFactor * stage.Zoom; } }
  int Pz(float value) { return (int)Math.Round(value * Zs); }

  // ---- level: XP from finished issues ----
  // Shown in place of the project name for a few seconds after leveling up.
  string LevelBanner {
   get {
    if ((DateTime.Now - trophyAt).TotalSeconds < 4.5) return "★ Troféu: " + trophyTitle + "!";
    if (VisitBanner != null) return VisitBanner;
    return (DateTime.Now - levelUpAt).TotalSeconds < 3.5 ? "★ Nível " + CreatureLevel + "!" : null;
   }
  }
  static readonly int[] LevelXp = { 0, 2, 5, 9, 14, 20, 27, 35, 44, 54 };
  public static int LevelOf(int xp) { int level = 0; foreach (int need in LevelXp) if (xp >= need) level++; return Math.Max(1, level); }
  int CreatureLevel { get { return LevelOf(settings.CreatureXp); } }
  // First run counts the issues already finished in the last 90 days, so the creature starts at the level you earned.
  void SeedXp(int finished) { if (settings.XpSeeded) return; settings.XpSeeded = true; settings.CreatureXp = Math.Max(settings.CreatureXp, finished); TrySave(); }
  void AddXp(int amount) {
   if (amount <= 0) return; int before = CreatureLevel; settings.CreatureXp += amount; TrySave();
   if (CreatureLevel > before) { levelUpAt = DateTime.Now; Say("Subi pro nível " + CreatureLevel + "!", true); StartMotionTimer(); }
  }
  // Short animations that need the full frame rate.
  bool CreatureFx {
   get {
    DateTime n = DateTime.Now;
    return (n - hopAt).TotalSeconds < 1 || dizzyUntil > n || (n - failAt).TotalSeconds < 3.2 || (n - levelUpAt).TotalSeconds < 3 || (n - cheerAt).TotalSeconds < 2 || (n - wakeAt).TotalSeconds < 3.5 || HappeningActive || DrainActive || (n - planeAt).TotalSeconds < 2.4 || (n - trophyAt).TotalSeconds < 3;
   }
  }

  // ---- data, once a second from StyleTick ----
  void BichoTick() {
   if (preview) return;
   if (settings.WeatherFx && !weatherBusy && DateTimeOffset.UtcNow >= nextWeather && !(settings.Compact && settings.DockTaskbar)) ReadWeather();
   TrackPush(); TrackBattery(); TrackUsageDrain(); HappeningTick(); TrackSnow(); CheckTrophies(); SpeechClock(); WriteCreatureFile(false); TrackMusic(); CheckColleagues();
   string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   if (settings.Flame && streakDay != today && !streakBusy && DateTime.Now.Hour >= 8) RefreshStreak(today);
   if (CreatureFx) StartMotionTimer();
  }
  // From every radar read: a failed tool (lightning) and the followed session finishing (the family cheers).
  void TrackCreatureEvents() {
   if (activeSession == null) return;
   if (activeSession.Event == "PostToolUseFailure") {
    string key = activeSession.Id + activeSession.Updated;
    if (key != failKey) { if (failKey.Length > 0 || radarWarmed) { failAt = DateTime.Now; Say(Pick("Ops, deu erro…", "Eita, falhou!"), true); } failKey = key; }
   }
   string state = activeSession.Id + ":" + activeSession.Effective;
   if (state != mainState) {
    if (mainState.Length > 0) {
     string e = activeSession.Effective;
     if (e == "done") { cheerAt = DateTime.Now; Say(Pick("Terminei!", "Prontinho!", "Feito!")); }
     else if (e == "waiting") Say("Psiu! Preciso de você.", true);
     else if (e == "working") Say(ToolPhrase(activeSession.Tool));
    }
    mainState = state; spokenTool = activeSession.Tool ?? "";
   } else if (activeSession.Effective == "working" && (activeSession.Tool ?? "") != spokenTool && (DateTime.Now - lastSpeech).TotalSeconds > 25) {
    spokenTool = activeSession.Tool ?? ""; Say(ToolPhrase(spokenTool));
   }
   if (CreatureFx) StartMotionTimer();
  }
  async void ReadWeather() {
   weatherBusy = true; nextWeather = DateTimeOffset.UtcNow.AddMinutes(2);
   try { var s = await Task.Run(() => Taskbar.Measure()); if (s != null) weather = WeatherNow.Parse(s.Weather); } catch { }
   finally { weatherBusy = false; }
  }
  // A push lights the constellation threads and sends a paper plane.
  DateTimeOffset? pushCommit; DateTime planeAt = DateTime.MinValue;
  void TrackPush() {
   string repo = gitInfo.Root ?? "";
   if (gitInfo.Error.Length > 0) return;
   if (repo != pushRepo) { pushRepo = repo; pushAhead = gitInfo.Ahead; pushCommit = gitInfo.Commit; return; }
   if (gitInfo.Commit.HasValue && pushCommit.HasValue && gitInfo.Commit.Value > pushCommit.Value) Say(Pick("Commit feito!", "Mais um commit!"));
   if (pushAhead > 0 && gitInfo.Ahead == 0) { constellationAt = DateTime.Now; planeAt = DateTime.Now; Say("Lá vai o push!"); }
   pushAhead = gitInfo.Ahead; pushCommit = gitInfo.Commit;
  }
  // Snow piles up on its head while it snows (or it is freezing).
  DateTime snowSince = DateTime.MinValue;
  bool Snowy { get { return settings.WeatherFx && (weather.Kind == "snow" || (weather.Temp.HasValue && weather.Temp.Value <= 3)); } }
  void TrackSnow() { if (!Snowy) snowSince = DateTime.MinValue; else if (snowSince == DateTime.MinValue) snowSince = DateTime.Now; }

  // ---- looks: color and shape ----
  static readonly string[][] Tints = {
   new[] { "", "Automática (cor do Windows)" }, new[] { "claude", "Laranja Claude" }, new[] { "azul", "Azul" }, new[] { "verde", "Verde" },
   new[] { "rosa", "Rosa" }, new[] { "roxo", "Roxo" }, new[] { "amarelo", "Amarelo" }, new[] { "menta", "Menta" }, new[] { "cinza", "Cinza" }
  };
  Color CreatureTint {
   get {
    switch (settings.CreatureColor ?? "") {
     case "claude": return Color.FromArgb(216, 156, 125); case "azul": return Color.FromArgb(98, 160, 234); case "verde": return Color.FromArgb(104, 190, 130);
     case "rosa": return Color.FromArgb(236, 130, 170); case "roxo": return Color.FromArgb(160, 124, 222); case "amarelo": return Color.FromArgb(232, 196, 92);
     case "menta": return Color.FromArgb(92, 200, 186); case "cinza": return Color.FromArgb(150, 156, 168); default: return accent;
    }
   }
  }
  static readonly string[][] Shapes = { new[] { "round", "Redondinho" }, new[] { "cat", "Gatinho" }, new[] { "ghost", "Fantasminha" }, new[] { "robot", "Robozinho" } };
  static readonly string[] CatBody = { ".#........#.", ".##......##.", ".##########.", "############", "############", "############", ".##########.", "..########.." };
  static readonly string[] RobotBody = { "..########..", ".##########.", ".##########.", ".##########.", ".##########.", ".##########.", ".##########.", "..########.." };
  static readonly string[] GhostBody = { "....####....", "..########..", ".##########.", ".##########.", "############", "############", "############", "############", "#.##.##.##.#" };
  string Shape { get { string s = settings.CreatureShape ?? ""; return s == "cat" || s == "ghost" || s == "robot" ? s : "round"; } }
  string[] ShapeRows(double t) {
   switch (Shape) {
    case "cat": return CatBody; case "robot": return RobotBody;
    case "ghost": { var rows = (string[])GhostBody.Clone(); if (((int)(t * 3)) % 2 == 0) rows[8] = ".##.##.##.##"; return rows; }
    default: return CreatureBody;
   }
  }
  void TrackBattery() {
   if (activeSession == null || activeSession.Context < 0) return;
   if (activeSession.Id != batterySession) { batterySession = activeSession.Id; batteryCtx = activeSession.Context; return; }
   if (batteryCtx - activeSession.Context >= 25) rechargeAt = DateTime.Now;
   batteryCtx = activeSession.Context;
  }
  async void RefreshStreak(string today) {
   streakBusy = true;
   try {
    using (var client = RedmineClient.FromMcpConfig()) {
     if (client == null) { streakDay = today; return; }
     var report = await Score.Week(client, DateTime.Now, settings.WeekRejections.Keys.ToList());
     foreach (var pair in report.Rejections) settings.WeekRejections[pair.Key] = pair.Value;
     TrySave(); streakDay = today;
    }
   } catch { } finally { streakBusy = false; Invalidate(); }
  }
  int Streak { get { return Score.Streak(settings.WeekRejections, Score.Monday(DateTime.Now)); } }
  bool RejectedThisWeek { get { int v; return settings.WeekRejections.TryGetValue(Score.Key(Score.Monday(DateTime.Now)), out v) && v > 0; } }

  // Click on the creature: a laughing hop; five quick clicks make it dizzy.
  bool PokeCreature(Point at) {
   if (!settings.Creature || stage.Creature.IsEmpty || !stage.Creature.Contains(at)) return false;
   DateTime n = DateTime.Now; pokes.Add(n); pokes.RemoveAll(p => (n - p).TotalSeconds > 3);
   if (pokes.Count >= 5) { dizzyUntil = n.AddSeconds(3.2); pokes.Clear(); Say("Tô tonto…", true); } else { hopAt = n; Say(Pick("Hihi!", "Ei!", "Cócegas não!")); }
   if (stage == desk && NightIdle) deskNightAt = n; // at night a poke switches the lamp on for a moment
   StartMotionTimer(); Invalidate(); return true;
  }

  // ---- mood ----
  string CurrentMood() {
   DateTime n = DateTime.Now;
   if (dizzyUntil > n) return "dizzy";
   if ((n - hopAt).TotalSeconds < 0.9) return "laugh";
   if ((n - failAt).TotalSeconds < 3) return "shock";
   if (n < bannerUntil) return "dance";
   if (activeSession != null && !activeSession.Closed && activeSession.State == "waiting") {
    double waited = (DateTimeOffset.UtcNow - activeSession.Time).TotalMinutes;
    return waited < 3 ? "wave" : waited < 6 ? "tap" : waited < 10 ? "cross" : "doze";
   }
   if ((n - wakeAt).TotalSeconds < 3.2) return "stretch";
   if (Night && !SessionBusy) return "sleep";
   string m = "idle";
   if (activeSession != null && !activeSession.Silent)
    switch (activeSession.Effective) { case "working": case "background": m = "work"; break; case "done": {
      // Celebrates for a moment, then back to normal; if you take long to come back it gets impatient and then dozes off.
      double since = (DateTimeOffset.UtcNow - activeSession.Time).TotalSeconds;
      m = since < 6 ? "happy" : since < 300 ? "idle" : since < 600 ? "tap" : "sleep"; break;
     } case "error": m = "oops"; break; }
   // The daily routine shows while nothing else is going on.
   if ((m == "idle" || m == "happy") && WorkDay.IsWorkday(n)) {
    var tod = n.TimeOfDay;
    if (tod >= new TimeSpan(12, 0, 0) && tod < new TimeSpan(13, 0, 0)) return "eat";
    if (tod >= new TimeSpan(13, 0, 0) && tod < new TimeSpan(13, 12, 0)) return "nap";
   }
   return m;
  }
  // What it holds when not working with a tool: coffee in the first half hour, a drink on Friday evening.
  string HandItem() {
   DateTime n = DateTime.Now; if (!WorkDay.IsWorkday(n)) return "";
   var tod = n.TimeOfDay;
   if (tod >= new TimeSpan(8, 0, 0) && tod < new TimeSpan(8, 30, 0)) return "coffee";
   if (n.DayOfWeek == DayOfWeek.Friday && tod >= new TimeSpan(17, 0, 0) && tod < WorkDay.Windows[1][1]) return "drink";
   return "";
  }
  int Mascot(Graphics g, int x, int top) {
   if (settings.Creature) {
    PaintCreature(g, x, top);
    return Px(43);
   }
   if (settings.Animations) { ambient.PaintOrb(g, x + Px(5), Px(11.5f), ScaleFactor, DateTime.Now); return Px(15); }
   return 0;
  }
  string Hat(string mood) {
   DateTime d = DateTime.Now;
   if (d < bannerUntil) return "party";
   if (mood == "sleep" || mood == "nap") return "nightcap";
   if (d.Month == 12) return "santa";
   if (d.Month == 10 && d.Day == 31) return "witch";
   if (d.Month == 6) return "straw";
   return CreatureLevel >= 10 ? "crown" : "";
  }
  static string ToolGlyph(string tool) {
   if (String.IsNullOrEmpty(tool)) return "";
   if (tool == "Edit" || tool == "Write" || tool == "MultiEdit" || tool == "NotebookEdit") return "";
   if (tool == "Bash" || tool == "PowerShell") return "";
   if (tool == "Read" || tool == "Grep" || tool == "Glob" || tool == "LS") return "";
   if (tool == "WebFetch" || tool == "WebSearch") return "";
   if (tool == "Agent" || tool == "Task") return "";
   if (tool.StartsWith("mcp__", StringComparison.Ordinal)) return "";
   return "";
  }
  // Where the eyes look: towards the mouse when it is around the strip.
  void LookAt(float cx, float cy, out int dx, out int dy) {
   dx = 0; dy = 0; var surface = stage.Surface; if (preview || surface == null || !surface.IsHandleCreated) return;
   Point center = surface.PointToScreen(new Point((int)cx, (int)cy)), mouse = Cursor.Position;
   int mx = mouse.X - center.X, my = mouse.Y - center.Y;
   if (Math.Abs(mx) > 700 || my < -600 || my > 140) return;
   dx = mx > 30 ? 1 : mx < -30 ? -1 : 0; dy = my < -45 ? -1 : 0;
  }

  // ---- little signs of life: natural blinks, looking around, idle fidgets ----
  readonly Random life = new Random();
  DateTime nextBlink = DateTime.Now.AddSeconds(2), blinkAt = DateTime.MinValue, blinkAgainAt = DateTime.MinValue;
  // Blinks at random intervals (2.5–6 s), sometimes twice in a row.
  bool Blinking(DateTime n) {
   if (n >= nextBlink) { blinkAt = n; blinkAgainAt = life.Next(4) == 0 ? n.AddSeconds(.3) : DateTime.MinValue; nextBlink = n.AddSeconds(2.5 + life.NextDouble() * 3.5); }
   double a = (n - blinkAt).TotalSeconds, b = (n - blinkAgainAt).TotalSeconds;
   return (a >= 0 && a < .13) || (b >= 0 && b < .13);
  }
  DateTime nextGaze = DateTime.Now.AddSeconds(3), gazeUntil = DateTime.MinValue; int gazeX, gazeY;
  // When nothing has its attention it looks around now and then: a side, the other, up.
  void Gaze(DateTime n, out int dx, out int dy) {
   if (n >= nextGaze) {
    int[][] spots = { new[] { -1, 0 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { -1, -1 }, new[] { 1, -1 } };
    var s = spots[life.Next(spots.Length)]; gazeX = s[0]; gazeY = s[1];
    gazeUntil = n.AddSeconds(.9 + life.NextDouble() * .9); nextGaze = n.AddSeconds(3 + life.NextDouble() * 4);
   }
   if (n < gazeUntil) { dx = gazeX; dy = gazeY; } else { dx = 0; dy = 0; }
  }
  string fidget = ""; DateTime fidgetAt = DateTime.MinValue, nextFidget = DateTime.Now.AddSeconds(25);
  static readonly string[] Fidgets = { "scratch", "swing", "whistle", "yawn", "reachUp" };
  // Every 20–40 s of being idle: scratches its head, swings its feet, whistles, yawns or stretches.
  string Fidget(DateTime n, string m) {
   if (m != "idle") { fidget = ""; if (nextFidget < n.AddSeconds(15)) nextFidget = n.AddSeconds(15 + life.Next(15)); return ""; }
   if (fidget.Length > 0 && (n - fidgetAt).TotalSeconds > 2.6) fidget = "";
   if (fidget.Length == 0 && n >= nextFidget) { fidget = Fidgets[life.Next(Fidgets.Length)]; fidgetAt = n; nextFidget = n.AddSeconds(20 + life.Next(21)); }
   return fidget;
  }

  // ---- the creature ----
  static readonly string[] CreatureBody = {
   "....####....",
   "..########..",
   ".##########.",
   ".##########.",
   "############",
   "############",
   ".##########.",
   "..########.."
  };
  void PaintCreature(Graphics g, int x, int top) {
   DateTime now = DateTime.Now; string next = CurrentMood();
   if (next != mood) { mood = next; moodSince = now; }
   bool still = !settings.Animations || (Night && !SessionBusy && !CreatureFx);
   double t = still ? 0 : MotionTime.Now / 1000.0, since = (now - moodSince).TotalSeconds;
   float px = Math.Max(1f, Zs * 2.45f);
   int lift = 0, sink = 0;
   switch (mood) {
    case "work": lift = ((int)(t * 4) % 2 == 0) ? 0 : 1; break;
    case "idle": lift = Math.Sin(t * 1.4) > .6 ? 1 : 0; break;
    case "wave": lift = (int)Math.Round(Math.Abs(Math.Sin(t * 5)) * 2); break;
    case "happy": lift = since < 1.8 ? (int)Math.Round(Math.Abs(Math.Sin(since * Math.PI / 0.45)) * 4) : 0; break;
    case "dance": lift = (int)Math.Round(Math.Abs(Math.Sin(t * Math.PI / 0.4)) * 3); break;
    case "laugh": lift = (int)Math.Round(Math.Sin(Math.Min(1, (now - hopAt).TotalSeconds / 0.9) * Math.PI) * 4); break;
    case "stretch": lift = 1; break;
    case "doze": case "nap": sink = 2; break;
    case "shock": lift = ((int)(t * 20)) % 2; break;
   }
   // A happening going on: the eyes follow it, and it chases, reaches, waves or looks up.
   string act = ""; int seeX = 0, seeY = 0; bool sees = false; float chaseTarget = 0;
   if (HappeningActive && stage.CritterSeen && !still && !stage.Head.IsEmpty && (mood == "idle" || mood == "happy" || mood == "work")) {
    sees = true; float cdx = stage.Critter.X - stage.Head.X, cdy = stage.Critter.Y - stage.Head.Y; double e = (now - happening.Start).TotalSeconds;
    seeX = cdx > px * 2 ? 1 : cdx < -px * 2 ? -1 : 0; seeY = cdy < -px * 3 ? -1 : 0;
    switch (happening.Kind) {
     case "bird": if (e >= 1.5 && e < 5.2) { act = "perch"; seeX = 0; seeY = -1; } else if (e >= 5.2) act = "bye"; break;
     case "butterfly": case "firefly":
      if (Math.Abs(cdx) < Pz(70)) chaseTarget = Math.Max(-Pz(8), Math.Min(Pz(8), cdx * .5f));
      if (Math.Abs(cdx) < px * 9) act = "reach";
      break;
     case "ladybug":
      if (e >= 7.5) act = "bye";
      else if (Math.Abs(cdx) < Pz(90)) chaseTarget = Math.Max(-Pz(9), Math.Min(Pz(9), cdx * .5f));
      break;
     case "leaf": if (stage.LeafOnHead) { act = "wow"; seeX = 0; seeY = -1; } break;
     case "snail": if (Math.Abs(cdx) < Pz(80)) act = "bye"; break;
     case "star": act = "point"; break;
    }
   }
   // The ball game on the desktop: it watches the ball and raises its arms right after throwing it.
   if (!sees && stage == desk && ballLive && !still) {
    sees = true; float bdx = ballPos.X - stage.Head.X, bdy = ballPos.Y - stage.Head.Y;
    seeX = bdx > px * 2 ? 1 : bdx < -px * 2 ? -1 : 0; seeY = bdy < -px * 3 ? -1 : 0;
    if ((now - ballThrowAt).TotalSeconds < .4) act = "reach";
   }
   if (!sees && !stage.LookTarget.IsEmpty && !stage.Head.IsEmpty && !still) {
    sees = true; float ldx = stage.LookTarget.X - stage.Head.X, ldy = stage.LookTarget.Y - stage.Head.Y;
    seeX = ldx > px * 2 ? 1 : ldx < -px * 2 ? -1 : 0; seeY = ldy < -px * 3 ? -1 : 0;
   }
   double clock = MotionTime.Now; float dt = (float)Math.Max(0, Math.Min(0.1, (clock - stage.ChaseClock) / 1000.0)); stage.ChaseClock = clock;
   stage.ChaseX += (chaseTarget - stage.ChaseX) * Math.Min(1f, dt * 4f); if (chaseTarget == 0 && Math.Abs(stage.ChaseX) < .3f) stage.ChaseX = 0;
   bool running = Math.Abs(chaseTarget - stage.ChaseX) > px * .5f;
   if (running) lift = ((int)(t * 12)) % 2;
   if (act == "reach") lift = Math.Max(lift, (int)Math.Round(Math.Abs(Math.Sin(t * 7)) * 3));
   if (act == "perch" || act == "wow") lift = 0;
   // On the desktop: carried by the mouse, landing, walking, jumping, sitting on a window or knocking on it.
   double land = (now - stage.LandAt).TotalSeconds; bool lying = stage.Pose == "bed";
   if (stage.Walking) { running = true; lift = ((int)(t * 8)) % 2; }
   if (stage.Pose == "jump") act = "reach";
   if (stage.Pose == "knock") act = "knock";
   if (stage.Held) { act = "held"; lift = 3; }
   else if (land >= 0 && land < 0.9) { act = land < 0.45 ? "land" : "shake"; lift = land < 0.2 ? (int)Math.Round((1 - land / 0.2) * 5) : land < 0.45 ? (int)Math.Round(Math.Sin((land - 0.2) / 0.25 * Math.PI) * 2) : 0; }
   // Now and then a hop into the puddle when it rains (desktop only, where the puddle is).
   bool rainNow = settings.WeatherFx && (weather.Kind == "rain" || weather.Kind == "storm");
   if (stage != strip && rainNow && !still && act.Length == 0 && !running && stage.Pose.Length == 0 && (mood == "idle" || mood == "happy")) { double ph = t % 7; if (ph < .6) lift = Math.Max(lift, (int)Math.Round(Math.Sin(ph / .6 * Math.PI) * 3)); }
   // Music on: it grooves to the beat when it is free, nods while it works.
   bool groove = MusicOn && !still && act.Length == 0 && !running && stage.Pose.Length == 0 && (mood == "idle" || mood == "happy");
   if (groove) { act = "groove"; lift = (int)Math.Round(Math.Abs(Math.Sin(t * Math.PI * 2)) * 2); }
   else if (MusicOn && !still && mood == "work" && act.Length == 0) lift = ((int)(t * 4)) % 2;
   string fid = Fidget(now, mood); double fe = (now - fidgetAt).TotalSeconds;
   if (fid.Length > 0 && act.Length == 0 && !running && stage.Pose.Length == 0 && stage.Act.Length == 0 && !still) {
    act = fid;
    if (fid == "reachUp" || (fid == "yawn" && fe > .9 && fe < 1.9)) lift = 1;
   }
   if (stage.Act.Length > 0 && act != "held" && act != "land" && act != "shake" && mood != "laugh" && mood != "dizzy") { act = stage.Act; if (act == "pout" || act == "fish" || act == "hide") lift = 0; if (act == "groove") lift = (int)Math.Round(Math.Abs(Math.Sin(t * Math.PI * 2)) * 2); }
   int tilt = mood == "dance" ? (((int)(t / 0.4)) % 2 == 0 ? -1 : 1) : mood == "dizzy" ? (int)Math.Round(Math.Sin(t * 6)) : 0;
   if (groove) tilt = ((int)(t * 2)) % 2 == 0 ? -1 : 1;
   if (act == "shake") tilt = ((int)(land * 22)) % 2 == 0 ? -1 : 1;
   else if (act == "held") tilt = stage.Swing > .8f ? 1 : stage.Swing < -.8f ? -1 : 0;
   float baseY = top + Pz(9.5f), ox = x + px + stage.ChaseX, oy = baseY - lift * px + sink * px;
   stage.Creature = new RectangleF(ox - px, top + Pz(2), px * 15, Pz(36));
   stage.Head = new PointF(ox + 6 * px, oy + 2 * px);
   string shape = Shape; string[] rows = ShapeRows(t);
   Color body = mood == "oops" ? red : CreatureTint;
   if (mood == "sleep" || mood == "doze" || mood == "nap") body = Color.FromArgb((body.R + 110) / 2, (body.G + 112) / 2, (body.B + 118) / 2);
   if (mood == "shock" && (now - failAt).TotalSeconds < 0.35) body = Color.FromArgb(250, 248, 236);
   if (shape == "ghost") body = Color.FromArgb(226, Math.Min(255, body.R + 30), Math.Min(255, body.G + 30), Math.Min(255, body.B + 30));
   Color dark = shape == "robot" ? Color.FromArgb(120, 232, 255) : Color.FromArgb(28, 26, 30), light = Color.FromArgb(Math.Min(255, body.R + 55), Math.Min(255, body.G + 55), Math.Min(255, body.B + 55));
   Color shade = Color.FromArgb(body.A, body.R * 3 / 4, body.G * 3 / 4, body.B * 3 / 4), white = Color.FromArgb(240, 244, 246, 250);
   string hand = mood == "work" ? "" : HandItem();
   bool sunny = settings.WeatherFx && weather.Kind == "sun" && !Night, rainy = settings.WeatherFx && (weather.Kind == "rain" || weather.Kind == "storm");
   bool cold = settings.WeatherFx && weather.Temp.HasValue && weather.Temp.Value <= 15, hot = settings.WeatherFx && weather.Temp.HasValue && weather.Temp.Value >= 30;
   bool shades = (sunny || hand == "drink") && mood != "sleep" && mood != "doze" && mood != "nap" && mood != "oops" && mood != "shock" && mood != "dizzy" && mood != "laugh" && mood != "stretch";
   int level = CreatureLevel; bool waitingOther = OtherActive().Any(s => s.State == "waiting");
   int lookX, lookY; LookAt(ox + 6 * px, oy + 4 * px, out lookX, out lookY);
   if (sees) { lookX = seeX; lookY = seeY; }
   else if (lookX == 0 && lookY == 0 && !stage.Hover && !still && mood == "idle" && act.Length == 0) Gaze(now, out lookX, out lookY);
   bool looking = lookX != 0 || lookY != 0 || stage.Hover || sees;
   var mode = g.SmoothingMode;
   g.SmoothingMode = SmoothingMode.AntiAlias;
   float sw = px * (10 - lift * 1.2f);
   if (!lying && stage.Pose != "sit") using (var shadow = new SolidBrush(Color.FromArgb(Math.Max(20, 70 - lift * 12), 0, 0, 0))) g.FillEllipse(shadow, ox + 6 * px - sw / 2, baseY + 9.6f * px, sw, px * 1.6f);
   g.SmoothingMode = SmoothingMode.None;
   var p = new Pix(g, ox, oy, px);
   try {
    // Cape from level 5, behind the body.
    if (level >= 5 && !lying) { Color cape = Color.FromArgb(124, 70, 170), capeDark = Color.FromArgb(92, 50, 130); for (int r = 2; r <= 8; r++) { p.Dot(-1, r, r > 6 ? capeDark : cape); p.Dot(12, r, r > 6 ? capeDark : cape); } p.Row(8, 0, 11, capeDark); }
    // Umbrella behind the head when it rains.
    if (rainy && mood != "dance") {
     Color canopy = Color.FromArgb(98, 167, 230), stripe = Color.FromArgb(150, 200, 245);
     p.Row(-4, 4, 7, canopy); p.Row(-3, 2, 9, canopy); p.Row(-2, 1, 10, canopy); p.Row(-1, 0, 11, canopy);
     p.Dot(4, -3, stripe); p.Dot(3, -2, stripe); p.Dot(5, -4, stripe);
    }
    if (shape == "cat") { p.Dot(-2, 7, shade); p.Dot(-3, 6, shade); p.Dot(-3, 5, shade); p.Dot(-2, 4, shade); } // tail
    for (int r = 0; r < rows.Length; r++)
     for (int c = 0; c < 12; c++)
      if (rows[r][c] == '#') p.Dot(c + (r < 4 ? tilt : 0), r, r == rows.Length - 1 || (r >= 4 && (c == 0 || c == 11)) ? shade : body);
    if (rows[1][3] == '#') { p.Dot(3 + tilt, 1, light); p.Dot(4 + tilt, 1, light); }
    p.Dot(2 + tilt, 2, light);
    if (shape == "cat") { Color pink = Color.FromArgb(240, 150, 170); p.Dot(1 + tilt, 1, pink); p.Dot(10 + tilt, 1, pink); }
    if (shape == "robot") { Color plate = Color.FromArgb(40, 44, 54), bolt = Color.FromArgb(170, 176, 188); for (int r = 3; r <= 6; r++) p.Row(r, 2, 9, plate); p.Dot(0, 4, bolt); p.Dot(11, 4, bolt); }
    if (mood == "shock") { p.Dot(2, -1, shade); p.Dot(4, -1, shade); p.Dot(8, -1, shade); p.Dot(9, -1, shade); } // hair on end
    // Feet: dangling while carried or sitting on a window edge; none for the ghost.
    if (shape == "ghost") { }
    else if (act == "held" || stage.Pose == "sit") {
     int a = (int)Math.Round(Math.Sin(t * (act == "held" ? 9 : 3.2)));
     for (int r = 8; r <= 10; r++) { p.Dot(3 + (r == 10 ? a : 0), r, shade); p.Dot(8 + (r == 10 ? -a : 0), r, shade); }
    }
    else if (mood != "doze" && mood != "nap") {
     bool step = (mood == "dance" && ((int)(t / 0.4)) % 2 == 0) || (running && ((int)(t * 12)) % 2 == 0);
     int lf = step ? -1 : 0, rf = ((mood == "dance" || running) && !step) || (mood == "tap" && ((int)(t * 3)) % 2 == 0) ? -1 : 0;
     if (act == "swing") { bool a = ((int)(t * 5)) % 2 == 0; lf = a ? -1 : 0; rf = a ? 0 : -1; }
     p.Dot(3, 8 + lf, shade); p.Dot(2, 9 + lf, shade); p.Dot(3, 9 + lf, shade);
     p.Dot(8, 8 + rf, shade); p.Dot(8, 9 + rf, shade); p.Dot(9, 9 + rf, shade);
    }
    // Face.
    bool blink = !still && act != "held" && act != "land" && act != "pout" && (mood == "idle" || mood == "work" || mood == "wave" || mood == "tap" || mood == "cross" || mood == "eat") && Blinking(now);
    if (act == "yawn" && fe < 1.9) { p.Dot(3, 5, dark); p.Dot(4, 5, dark); p.Dot(7, 5, dark); p.Dot(8, 5, dark); float o = fe < .3 ? 0 : 1; p.Row(6, 5, 6, dark); if (o > 0) p.Row(7, 5, 6, Color.FromArgb(170, 70, 70)); }
    else if (act == "held" || act == "land") { p.Dot(3, 4, dark); p.Dot(4, 4, dark); p.Dot(3, 5, dark); p.Dot(4, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 4, dark); p.Dot(7, 5, dark); p.Dot(8, 5, dark); p.Dot(5.5f, 6.5f, dark); }
    else if (act == "pout") { p.Dot(3, 3, dark); p.Dot(8, 3, dark); p.Dot(4, 4, dark); p.Dot(4, 5, dark); p.Dot(7, 4, dark); p.Dot(7, 5, dark); p.Dot(4, 7, dark); p.Row(6, 5, 6, dark); p.Dot(7, 7, dark); }
    else if (mood == "dizzy") {
     bool a = ((int)(t * 6)) % 2 == 0;
     if (a) { p.Dot(3, 4, dark); p.Dot(4, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 5, dark); } else { p.Dot(4, 4, dark); p.Dot(3, 5, dark); p.Dot(8, 4, dark); p.Dot(7, 5, dark); }
     p.Dot(5, 6, dark); p.Dot(6, 7, dark);
    }
    else if (mood == "laugh") { p.Dot(3, 5, dark); p.Dot(4, 4, dark); p.Dot(5, 5, dark); p.Dot(6, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 5, dark); p.Row(6, 4, 7, dark); p.Dot(5, 7, Color.FromArgb(240, 120, 140)); p.Dot(6, 7, Color.FromArgb(240, 120, 140)); }
    else if (mood == "shock") { p.Dot(3, 4, dark); p.Dot(4, 4, dark); p.Dot(3, 5, dark); p.Dot(4, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 4, dark); p.Dot(7, 5, dark); p.Dot(8, 5, dark); p.Dot(3, 4, white); p.Dot(7, 4, white); p.Dot(5, 6, dark); p.Dot(6, 6, dark); p.Dot(5, 7, dark); p.Dot(6, 7, dark); }
    else if (mood == "stretch") { p.Dot(3, 5, dark); p.Dot(4, 5, dark); p.Dot(7, 5, dark); p.Dot(8, 5, dark); p.Dot(5, 6, dark); p.Dot(6, 6, dark); p.Dot(5, 7, dark); p.Dot(6, 7, dark); }
    else if (shades) {
     Color lens = Color.FromArgb(20, 22, 30); p.Row(4, 2, 9, lens); p.Row(5, 3, 4, lens); p.Row(5, 7, 8, lens);
     p.Dot(3, 4, Color.FromArgb(160, 255, 255, 255)); p.Dot(7, 4, Color.FromArgb(160, 255, 255, 255));
    }
    else if (mood == "sleep" || mood == "doze" || mood == "nap" || blink) { p.Dot(3, 5, dark); p.Dot(4, 5, dark); p.Dot(7, 5, dark); p.Dot(8, 5, dark); }
    else if (mood == "happy" || mood == "dance" || act == "groove") { p.Dot(3, 5, dark); p.Dot(4, 4, dark); p.Dot(5, 5, dark); p.Dot(6, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 5, dark); }
    else if (mood == "oops") { p.Dot(3, 4, dark); p.Dot(5, 4, dark); p.Dot(4, 5, dark); p.Dot(3, 6, dark); p.Dot(5, 6, dark); p.Dot(6, 4, dark); p.Dot(8, 4, dark); p.Dot(7, 5, dark); p.Dot(6, 6, dark); p.Dot(8, 6, dark); }
    else if (mood == "tap" || mood == "cross") { p.Dot(3, 3, dark); p.Dot(4, 3, dark); p.Dot(7, 3, dark); p.Dot(8, 3, dark); p.Dot(4 + lookX, 5, dark); p.Dot(7 + lookX, 5, dark); p.Dot(5, 6, dark); p.Dot(6, 6, dark); }
    else {
     // Open eyes: they follow the mouse; while working they look down at the keyboard unless the mouse is around.
     int ey = (mood == "work" && !looking) || act == "knock" ? 5 : 4 + lookY, ex = looking && act != "knock" ? lookX : 0;
     p.Dot(4 + ex, ey, dark); p.Dot(4 + ex, ey + 1, dark); p.Dot(7 + ex, ey, dark); p.Dot(7 + ex, ey + 1, dark);
     if (mood == "eat") { bool chew = ((int)(t * 4)) % 2 == 0; p.Dot(5, 6, dark); if (chew) p.Dot(6, 6, dark); }
    }
    if (mood == "wave") { p.Dot(5, 6, dark); p.Dot(6, 6, dark); }
    if (act == "wow" || act == "point" || act == "reach" || act == "whistle") { p.Dot(5.5f, 6, dark); p.Dot(5.5f, 7, dark); } // "oh!" (or whistling)
    if (act == "perch" || act == "bye") { p.Dot(2, 6, Color.FromArgb(200, 240, 130, 150)); p.Dot(9, 6, Color.FromArgb(200, 240, 130, 150)); p.Dot(5, 6, dark); p.Dot(6, 6, dark); }
    if (mood == "happy" || mood == "dance" || mood == "laugh") { p.Dot(2, 6, Color.FromArgb(200, 240, 130, 150)); p.Dot(9, 6, Color.FromArgb(200, 240, 130, 150)); }
    if (level >= 7 && !(cold && mood != "dance")) { Color bow = Color.FromArgb(214, 52, 58); p.Dot(4, 7, bow); p.Dot(5, 7, Color.FromArgb(240, 90, 96)); p.Dot(6, 7, Color.FromArgb(240, 90, 96)); p.Dot(7, 7, bow); } // bow tie from level 7
    if (cold && mood != "dance") { Color knit = Color.FromArgb(222, 84, 96), knit2 = Color.FromArgb(240, 140, 150); for (int c = 1; c <= 10; c++) p.Dot(c, 7, c % 2 == 0 ? knit : knit2); p.Dot(10, 8, knit); p.Dot(10, 9, knit2); }
    // Lunch bowl in front.
    if (mood == "eat") { Color bowl = Color.FromArgb(236, 238, 242), rim = Color.FromArgb(98, 167, 230), rice = Color.FromArgb(252, 250, 240); p.Row(6, 3, 8, rice); p.Row(7, 2, 9, rim); p.Row(8, 3, 8, bowl); p.Dot(5, 5, Color.FromArgb(110, 196, 118)); }
    // Arms: a reaction to the happening wins over the mood.
    bool armsDone = true;
    if (act == "reach") { p.Dot(-1, 2, shade); p.Dot(-1, 1, shade); p.Dot(12, 2, shade); p.Dot(12, 1, shade); }
    else if (act == "bye") { bool up = ((int)(t * 4)) % 2 == 0; p.Dot(-1, 6, shade); p.Dot(12, up ? 2 : 3, shade); p.Dot(up ? 13 : 12, up ? 1 : 2, shade); }
    else if (act == "point") { if (seeX < 0) { p.Dot(12, 6, shade); p.Dot(-1, 3, shade); p.Dot(-2, 2, shade); p.Dot(-3, 1, shade); } else { p.Dot(-1, 6, shade); p.Dot(12, 3, shade); p.Dot(13, 2, shade); p.Dot(14, 1, shade); } }
    else if (act == "groove") { bool up = ((int)(t * 2)) % 2 == 0; p.Dot(-1, up ? 2 : 4, shade); p.Dot(12, up ? 4 : 2, shade); }
    else if (act == "scratch") { bool a = ((int)(t * 8)) % 2 == 0; p.Dot(-1, 6, shade); p.Dot(12, 3, shade); p.Dot(11, a ? 1 : 0, shade); }
    else if (act == "reachUp" || (act == "yawn" && fe > .9 && fe < 1.9)) { p.Dot(-1, 1, shade); p.Dot(-1, 0, shade); p.Dot(12, 1, shade); p.Dot(12, 0, shade); }
    else if (act == "fish") { p.Dot(12, 6, shade); p.Dot(-1, 5, shade); p.Dot(-2, 4, shade); }
    else if (act == "rps") { }
    else if (act == "pout") { Color arm = Color.FromArgb(shade.R * 4 / 5, shade.G * 4 / 5, shade.B * 4 / 5); p.Row(6, 2, 9, arm); p.Dot(1, 5, arm); p.Dot(10, 5, arm); }
    else if (act == "held") { bool up = ((int)(t * 10)) % 2 == 0; p.Dot(-1, up ? 1 : 3, shade); p.Dot(-2, up ? 0 : 2, shade); p.Dot(12, up ? 3 : 1, shade); p.Dot(13, up ? 2 : 0, shade); }
    else if (act == "land") { p.Dot(-2, 4, shade); p.Dot(-1, 4, shade); p.Dot(12, 4, shade); p.Dot(13, 4, shade); }
    else if (act == "knock") { bool hit = ((int)(t * 5)) % 2 == 0; p.Dot(-1, 6, shade); p.Dot(12, hit ? 8 : 6, shade); p.Dot(13, hit ? 9 : 5, shade); }
    else if (running) { bool up = ((int)(t * 12)) % 2 == 0; p.Dot(-1, up ? 4 : 5, shade); p.Dot(12, up ? 5 : 4, shade); }
    else armsDone = false;
    if (!armsDone)
    switch (mood) {
     case "work": { bool up = ((int)(t * 8)) % 2 == 0; p.Dot(-1, up ? 5 : 6, shade); p.Dot(12, up ? 6 : 5, shade); break; }
     case "wave": { bool up = ((int)(t * 4)) % 2 == 0; p.Dot(-1, 6, shade); p.Dot(12, up ? 2 : 3, shade); p.Dot(up ? 13 : 12, up ? 1 : 2, shade); break; }
     case "dance": case "laugh": { bool up = ((int)(t / 0.4)) % 2 == 0; p.Dot(-1, up ? 2 : 4, shade); p.Dot(12, up ? 4 : 2, shade); break; }
     case "stretch": p.Dot(-1, 1, shade); p.Dot(-1, 0, shade); p.Dot(12, 1, shade); p.Dot(12, 0, shade); break;
     case "shock": p.Dot(-2, 3, shade); p.Dot(-1, 4, shade); p.Dot(13, 3, shade); p.Dot(12, 4, shade); break;
     case "eat": { bool up = ((int)(t * 2)) % 2 == 0; p.Dot(-1, 6, shade); p.Dot(up ? 10 : 12, up ? 5 : 6, shade); if (up) p.Dot(9, 5, Color.FromArgb(200, 180, 140)); break; }
     case "cross": { Color arm = Color.FromArgb(shade.R * 4 / 5, shade.G * 4 / 5, shade.B * 4 / 5); p.Row(6, 2, 9, arm); p.Dot(1, 5, arm); p.Dot(10, 5, arm); break; }
     case "doze": {
      p.Dot(-1, 6, shade); p.Dot(12, 5, shade);
      Color board = Color.FromArgb(236, 230, 214); for (int r = 0; r <= 4; r++) p.Row(r, 13, 16, board);
      p.Dot(14.5f, 1, amber); p.Dot(14.5f, 2, amber); p.Dot(14.5f, 4, amber); p.Dot(13, 5, Color.FromArgb(120, 90, 70)); p.Dot(13, 6, Color.FromArgb(120, 90, 70));
      break;
     }
     default:
      p.Dot(-1, 6, shade);
      // Points at another session that needs you; otherwise the hand holds coffee or a drink.
      if (waitingOther && (mood == "idle" || mood == "happy")) { p.Dot(12, 5, shade); p.Dot(13, 5, shade); p.Dot(14, 5, shade); }
      else p.Dot(12, 6, shade);
      break;
    }
    if (act.Length == 0 && !running && hand == "coffee" && mood != "doze" && mood != "eat" && !(waitingOther && (mood == "idle" || mood == "happy"))) {
     Color mug = Color.FromArgb(236, 238, 242), coffee = Color.FromArgb(120, 76, 50);
     p.Dot(12, 5, mug); p.Dot(13, 5, mug); p.Dot(12, 6, mug); p.Dot(13, 6, mug); p.Dot(14, 5, mug); p.Dot(12, 4.6f, coffee); p.Dot(13, 4.6f, coffee);
     for (int i = 0; i < 2; i++) { double s = (t * 0.8 + i * 0.5) % 1.0; p.Dot(12.5f + i + (float)Math.Sin(t * 3 + i) * .5f, 3.5f - (float)s * 3, Color.FromArgb((int)(150 * (1 - s)), 230, 230, 236)); }
    }
    // A popsicle on hot days.
    if (hot && hand.Length == 0 && act.Length == 0 && !running && !waitingOther && (mood == "idle" || mood == "happy")) {
     Color ice = Color.FromArgb(236, 120, 150), ice2 = Color.FromArgb(250, 172, 120), stick = Color.FromArgb(214, 180, 130);
     p.Dot(12, 3, ice2); p.Dot(13, 3, ice); p.Dot(12, 4, ice); p.Dot(13, 4, ice2); p.Dot(12, 5, ice); p.Dot(13, 5, ice); p.Dot(12.5f, 6, stick);
     if (!still) { double d = (t * .7) % 1.0; p.Dot(13, 6 + (float)(d * 3), Color.FromArgb((int)(200 * (1 - d)), 236, 120, 150)); }
    }
    if (act.Length == 0 && !running && hand == "drink" && mood != "doze" && mood != "eat") {
     Color glass = Color.FromArgb(170, 200, 230, 250), juice = Color.FromArgb(250, 170, 70);
     p.Dot(12, 4, glass); p.Dot(13, 4, glass); p.Dot(12, 5, juice); p.Dot(13, 5, juice); p.Dot(12, 6, juice); p.Dot(13, 6, juice); p.Dot(14, 3, Color.FromArgb(240, 120, 160)); p.Dot(13.5f, 2, Color.FromArgb(240, 120, 160));
    }
    // Hats; the antenna from level 3 shows when there is no hat.
    string hat = rainy && mood != "dance" ? "" : Hat(mood);
    stage.Crown = oy - px * (rainy && mood != "dance" ? 4 : hat == "party" || hat == "witch" ? 4 : hat.Length > 0 ? 3 : level >= 3 ? 3 : 0);
    if (hat == "party") { Color a = Color.FromArgb(240, 120, 160), b = Color.FromArgb(250, 210, 90); p.Row(-1, 4, 7, a); p.Row(-2, 5, 6, b); p.Dot(5, -3, a); p.Dot(6, -3, a); p.Dot(5.5f, -4, Color.FromArgb(120, 210, 255)); }
    else if (hat == "santa") { Color r = Color.FromArgb(214, 52, 58); p.Row(-1, 2, 9, white); p.Row(-2, 3, 8, r); p.Row(-3, 5, 9, r); p.Dot(10, -3, r); p.Dot(11, -2, white); }
    else if (hat == "witch") { Color v = Color.FromArgb(92, 56, 140), band = Color.FromArgb(240, 150, 60); p.Row(-1, 1, 10, v); p.Row(-2, 3, 8, band); p.Row(-3, 4, 7, v); p.Row(-4, 5, 6, v); }
    else if (hat == "straw") { Color s = Color.FromArgb(226, 190, 110), band = Color.FromArgb(214, 52, 58); p.Row(-1, 0, 11, s); p.Row(-2, 3, 8, band); p.Row(-3, 4, 7, s); }
    else if (hat == "nightcap") { Color c = Color.FromArgb(70, 92, 170); p.Row(-1, 2, 9, Color.FromArgb(200, 210, 240)); p.Row(-2, 4, 9, c); p.Row(-3, 7, 10, c); p.Dot(11, -2, white); }
    else if (hat == "crown") { Color gold = Color.FromArgb(246, 200, 70), jewel = Color.FromArgb(220, 60, 90); p.Row(-1, 3, 8, gold); p.Dot(3, -2, gold); p.Dot(5.5f, -2, gold); p.Dot(8, -2, gold); p.Dot(5.5f, -1, jewel); }
    else if (level >= 3 || shape == "robot") { double glow = (Math.Sin(t * 3) + 1) / 2; p.Dot(6, -1, shade); p.Dot(6, -2, shade); p.Dot(6, -3, Color.FromArgb(255, (int)(180 + 70 * glow), (int)(220 + 30 * glow), 120)); }
    // Headphones while music plays.
    if (MusicOn && !lying) {
     Color cup = Color.FromArgb(54, 56, 64), pad = CreatureTint == accent ? Color.FromArgb(120, 210, 255) : accent;
     if (hat.Length == 0 && !rainy) { p.Row(-1, 3, 8, cup); p.Dot(2, 0, cup); p.Dot(9, 0, cup); p.Dot(1, 1, cup); p.Dot(10, 1, cup); }
     for (int r = 2; r <= 4; r++) { p.Dot(-1 + tilt, r, cup); p.Dot(0 + tilt, r, cup); p.Dot(11 + tilt, r, cup); p.Dot(12 + tilt, r, cup); }
     p.Dot(-1 + tilt, 3, pad); p.Dot(12 + tilt, 3, pad);
    }
    // Snow piling up on the head.
    if (Snowy && hat.Length == 0 && !rainy) {
     int depth = Math.Min(2, (int)((now - snowSince).TotalMinutes / 10));
     p.Row(0, 4, 7, white); p.Dot(3, 1, white); p.Dot(8, 1, white);
     if (depth >= 1) p.Row(-1, 4, 7, white);
     if (depth >= 2) p.Row(-2, 5, 6, white);
    }
    if (hot && !still) { double d = (t * 0.8) % 1.0; p.Dot(11, 1 + (float)Math.Floor(d * 4), Color.FromArgb((int)(220 * (1 - d)), 140, 200, 255)); }
    // Lightning cloud right after a failed command.
    if (mood == "shock") {
     Color cloud = Color.FromArgb(150, 154, 166), cloudDark = Color.FromArgb(110, 114, 126);
     p.Row(-4, 4, 8, cloud); p.Row(-3, 2, 10, cloud); p.Row(-2, 3, 9, cloudDark);
     if (((int)(t * 8)) % 2 == 0) { Color bolt = Color.FromArgb(255, 226, 90); p.Dot(6, -1, bolt); p.Dot(5, -1, bolt); p.Dot(5, 0, bolt); }
    }
   } finally { p.Dispose(); g.SmoothingMode = mode; }
   // Tool in the hand while it works; sparks fly while it edits.
   if (mood == "work" && activeSession != null && stage.Act != "hide" && stage.Act != "fish" && stage.Act != "rps") {
    string glyph = ToolGlyph(activeSession.Tool);
    if (glyph.Length > 0) {
     var at = new PointF(ox + 12.2f * px, oy + 3.4f * px);
     if (stage != strip) using (var b = new SolidBrush(Color.FromArgb(235, 238, 242))) g.DrawString(glyph, Theme.F(Theme.Icons, 8f * stage.Zoom), b, at.X, at.Y); // GDI text has no alpha on the layered desktop window
     else TextRenderer.DrawText(g, glyph, Theme.F(Theme.Icons, 8f), new Rectangle((int)at.X, (int)at.Y, (int)(px * 6), (int)(px * 6)), Color.FromArgb(235, 238, 242), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
     if (glyph == "" && !still) {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      for (int i = 0; i < 4; i++) {
       double ph = (t * 2.2 + i * 0.25) % 1.0; double ang = -0.6 - i * 0.55;
       float sx = at.X + px * 2 + (float)(Math.Cos(ang) * ph * px * 6), sy = at.Y + px * 1.5f + (float)(Math.Sin(ang) * ph * px * 6);
       using (var spark = new SolidBrush(Color.FromArgb((int)(230 * (1 - ph)), 255, 214, 120))) g.FillEllipse(spark, sx - px * .45f, sy - px * .45f, px * .9f, px * .9f);
      }
      g.SmoothingMode = mode;
     }
    }
   }
   g.SmoothingMode = SmoothingMode.AntiAlias;
   // Dizzy stars orbiting the head.
   if (mood == "dizzy")
    for (int i = 0; i < 3; i++) {
     double a = t * 5 + i * 2.09; float sx = ox + 6 * px + (float)Math.Cos(a) * px * 6, sy = oy - px * 1.5f + (float)Math.Sin(a) * px * 1.6f;
     using (var star = new Pen(Color.FromArgb(250, 220, 90), Math.Max(1f, px * .6f))) { g.DrawLine(star, sx - px, sy, sx + px, sy); g.DrawLine(star, sx, sy - px, sx, sy + px); }
    }
   // Level up: a golden burst; the headline says the new level (LevelBanner).
   double lu = (now - levelUpAt).TotalSeconds / 2.8;
   if (lu >= 0 && lu <= 1) {
    float rr = px * (4 + (float)lu * 12);
    using (var ring = new Pen(Color.FromArgb((int)(220 * (1 - lu)), 246, 200, 70), px * .8f)) g.DrawEllipse(ring, ox + 6 * px - rr, oy + 4 * px - rr, rr * 2, rr * 2);
   }
   // Dust when it lands.
   if (land >= 0.15 && land < 0.9) {
    double k = (land - 0.15) / 0.75;
    for (int i = 0; i < 4; i++) {
     float side = i < 2 ? -1 : 1, r = px * (0.8f + (float)k * 1.6f), dx = side * px * (5 + (float)k * 6 + (i % 2) * 2);
     using (var puff = new SolidBrush(Color.FromArgb((int)(150 * (1 - k)), 220, 214, 204))) g.FillEllipse(puff, ox + 6 * px + dx - r, baseY + 9.5f * px - r - (i % 2) * px, r * 2, r * 2);
    }
   }
   // A paper plane after a push.
   double pl = (now - planeAt).TotalSeconds / 2.2;
   if (pl >= 0 && pl < 1) {
    float sx = ox + 8 * px + (float)pl * Pz(90), sy = oy - (float)pl * Pz(26) + (float)Math.Sin(pl * 9) * px; int a = (int)(255 * Math.Min(1, (1 - pl) * 4));
    var wing = new[] { new PointF(sx + 3 * px, sy), new PointF(sx - 2 * px, sy - 1.5f * px), new PointF(sx - .8f * px, sy + .2f * px), new PointF(sx - 2 * px, sy + 1.3f * px) };
    using (var b = new SolidBrush(Color.FromArgb(a, 246, 246, 250))) g.FillPolygon(b, wing);
    using (var fold = new Pen(Color.FromArgb(a, 160, 168, 182), Math.Max(1f, px * .3f))) g.DrawLine(fold, sx + 3 * px, sy, sx - .8f * px, sy + .2f * px);
   }
   // A whistled note.
   if (act == "whistle") {
    double pn = (fe % 1.2) / 1.2;
    using (var font = new Font("Segoe UI Symbol", 6f * stage.Zoom)) using (var b = new SolidBrush(Color.FromArgb((int)(220 * (1 - pn)), 236, 238, 242))) g.DrawString("♪", font, b, ox + 7 * px + (float)pn * 2 * px, oy + 4 * px - (float)pn * Pz(10));
   }
   // Music notes floating up.
   if (MusicOn && !still && !lying)
    using (var font = new Font("Segoe UI Symbol", 7f * stage.Zoom))
     for (int i = 0; i < 2; i++) {
      double pn = ((MotionTime.Now / 1000.0) * 0.5 + i * 0.5) % 1.0;
      using (var b = new SolidBrush(Color.FromArgb((int)(220 * (1 - pn)), 255, 236, 170))) g.DrawString(i == 0 ? "♪" : "♫", font, b, ox + (i == 0 ? -3 : 11) * px + (float)Math.Sin(pn * 6 + i) * px, oy - (float)pn * Pz(12));
     }
   // "!" when it needs you; floating "z" while it sleeps or dozes.
   if ((mood == "wave" || act == "knock") && ((int)(t * 3)) % 2 == 0)
    using (var mark = new SolidBrush(amber)) { g.FillRectangle(mark, ox - px * 3, oy, px, px * 3); g.FillRectangle(mark, ox - px * 3, oy + px * 4, px, px); }
   if (mood == "sleep" || mood == "doze" || mood == "nap")
    using (var font = new Font("Segoe UI Semibold", 7f * stage.Zoom))
     for (int i = 0; i < 2; i++) {
      double pz = ((MotionTime.Now / 1000.0) * 0.45 + i * 0.5) % 1.0;
      using (var b = new SolidBrush(Color.FromArgb((int)(170 * (1 - pz)), 200, 200, 210))) g.DrawString("z", font, b, ox + (mood == "doze" ? 8 : 12) * px + (float)pz * Pz(4), top + Pz(6) - (float)pz * Pz(8));
     }
   g.SmoothingMode = mode;
  }

  // ---- family: the other active sessions as small creatures (right column); they cheer when yours finishes ----
  int FamilyWidth { get { int n = Math.Min(2, OtherActive().Count); return n == 0 ? 0 : Px(4) + n * Px(15); } }
  void PaintFamily(Graphics g, int right, int top) {
   var others = OtherActive().OrderByDescending(s => s.State == "waiting").ThenByDescending(s => s.Time).ToList(); if (others.Count == 0) return;
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0; bool cheer = (DateTime.Now - cheerAt).TotalSeconds < 1.9;
   float px = Math.Max(1f, ScaleFactor * 1.15f); var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
   for (int i = 0; i < Math.Min(2, others.Count); i++) {
    var s = others[i]; bool waiting = s.State == "waiting";
    int lift = cheer ? (int)Math.Round(Math.Abs(Math.Sin(t * 7 + i)) * 3) : waiting ? (int)Math.Round(Math.Abs(Math.Sin(t * 5 + i)) * 2) : (((int)(t * 4 + i)) % 2);
    float ox = right - (i + 1) * Px(15), oy = top + Px(4) - lift * px;
    Color body = waiting ? amber : accent, shade = Color.FromArgb(body.R * 3 / 4, body.G * 3 / 4, body.B * 3 / 4), dark = Color.FromArgb(28, 26, 30);
    var p = new Pix(g, ox, oy, px);
    try {
     var rows = ShapeRows(t); for (int r = 0; r < rows.Length; r++) for (int c = 0; c < 12; c++) if (rows[r][c] == '#') p.Dot(c, r, r == rows.Length - 1 ? shade : body);
     if (cheer) { p.Dot(3, 5, dark); p.Dot(4, 4, dark); p.Dot(5, 5, dark); p.Dot(6, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 5, dark); }
     else { p.Dot(4, 4, dark); p.Dot(4, 5, dark); p.Dot(7, 4, dark); p.Dot(7, 5, dark); }
     p.Dot(3, 8, shade); p.Dot(8, 8, shade);
     if (cheer) { bool up = ((int)(t * 6 + i)) % 2 == 0; p.Dot(-1, up ? 1 : 3, shade); p.Dot(12, up ? 3 : 1, shade); }
     else if (waiting) { bool up = ((int)(t * 4)) % 2 == 0; p.Dot(12, up ? 2 : 3, shade); p.Dot(-1, 6, shade); }
     else { bool up = ((int)(t * 8 + i)) % 2 == 0; p.Dot(-1, up ? 5 : 6, shade); p.Dot(12, up ? 6 : 5, shade); }
    } finally { p.Dispose(); }
   }
   g.SmoothingMode = mode;
   if (others.Count > 2) TextRenderer.DrawText(g, "+" + (others.Count - 2), Theme.F(Theme.SansBold, 6.5f), new Point(right - 2 * Px(15) - Px(12), top + Px(3)), muted, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
  }

  // ---- streak flame (or smoke after a rejection this week), where the menu dots were ----
  bool PaintFlame(Graphics g, int cx, int bottom) {
   if (!settings.Flame) return false;
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   try {
    if (RejectedThisWeek) {
     for (int i = 0; i < 3; i++) {
      double ph = (t * 0.5 + i / 3.0) % 1.0; float r = Px(1.5f) + (float)ph * Px(2.5f);
      using (var puff = new SolidBrush(Color.FromArgb((int)(110 * (1 - ph)), 170, 170, 178))) g.FillEllipse(puff, cx - r + (float)Math.Sin(t * 2 + i) * Px(2), bottom - Px(4) - (float)ph * Px(12) - r, r * 2, r * 2);
     }
     return true;
    }
    int weeks = Streak; if (weeks <= 0) return false;
    float size = Px(weeks >= 3 ? 7f : weeks == 2 ? 6f : 5f), flick = (float)(Math.Sin(t * 13) * .08 + Math.Sin(t * 7.3) * .06);
    Func<float, float, GraphicsPath> drop = (w, h) => {
     var path = new GraphicsPath(); float top = bottom - h * (1 + flick);
     path.AddBezier(cx, top, cx + w * .9f, bottom - h * .45f, cx + w * .7f, bottom, cx, bottom);
     path.AddBezier(cx, bottom, cx - w * .7f, bottom, cx - w * .9f, bottom - h * .45f, cx, top);
     return path;
    };
    using (var glow = new GraphicsPath()) {
     glow.AddEllipse(cx - size * 1.4f, bottom - size * 2f, size * 2.8f, size * 2.4f);
     using (var halo = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, 255, 150, 60), SurroundColors = new[] { Color.FromArgb(0, 255, 150, 60) } }) g.FillPath(halo, glow);
    }
    using (var outer = drop(size * .75f, size * 1.6f)) using (var b = new SolidBrush(Color.FromArgb(240, 245, 120, 50))) g.FillPath(b, outer);
    using (var inner = drop(size * .45f, size * 1.05f)) using (var b = new SolidBrush(Color.FromArgb(250, 252, 206, 90))) g.FillPath(b, inner);
    using (var core = drop(size * .2f, size * .5f)) using (var b = new SolidBrush(Color.FromArgb(255, 255, 248, 230))) g.FillPath(b, core);
    return true;
   } finally { g.SmoothingMode = mode; }
  }

  // ---- battery of the context window (remaining), at the start of the detail line ----
  int PaintBattery(Graphics g, int x, int y, int height) {
   if (!settings.Battery || activeSession == null || activeSession.Context < 0) return 0;
   double left = Math.Max(0, 100 - activeSession.Context) / 100.0;
   double rc = (DateTime.Now - rechargeAt).TotalSeconds / 1.2;
   if (rc >= 0 && rc < 1) left = left * MotionTime.Ease(rc);
   Color c = left > .4 ? green : left > .2 ? amber : red;
   int alpha = left <= .2 && settings.Animations && !Night ? (int)(150 + 105 * (Math.Sin(MotionTime.Now / 180.0) + 1) / 2) : 255;
   var body = new Rectangle(x, y + (height - Px(7)) / 2, Px(13), Px(7));
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var shape = Rounded(new RectangleF(body.X + .5f, body.Y + .5f, body.Width - 1, body.Height - 1), Px(1.6f)))
   using (var pen = new Pen(Color.FromArgb(150, 200, 202, 206), 1f)) g.DrawPath(pen, shape);
   using (var nub = new SolidBrush(Color.FromArgb(150, 200, 202, 206))) g.FillRectangle(nub, body.Right, body.Y + Px(2), Px(1.5f), body.Height - Px(4));
   float w = (float)((body.Width - Px(3)) * left);
   if (w > 0) using (var fill = new SolidBrush(Color.FromArgb(alpha, c))) g.FillRectangle(fill, body.X + Px(1.5f), body.Y + Px(1.5f), w, body.Height - Px(3));
   if (rc >= 0 && rc < 1) using (var glow = new SolidBrush(Color.FromArgb((int)(120 * (1 - rc)), 255, 255, 255))) g.FillRectangle(glow, body.X + Px(1.5f), body.Y + Px(1.5f), w, body.Height - Px(3));
   g.SmoothingMode = mode;
   return Px(18);
  }

  // ---- weather in the strip background (behind everything) ----
  void PaintWeatherBack(Graphics g) {
   if (!settings.WeatherFx || Night || weather.Kind.Length == 0 && !weather.Temp.HasValue) return;
   double t = settings.Animations ? MotionTime.Now / 1000.0 : 0; int w = StripWidth, h = Height;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   try {
    if (weather.Kind == "rain" || weather.Kind == "storm") {
     using (var pen = new Pen(Color.FromArgb(46, 150, 200, 255), Math.Max(1f, Px(1)))) {
      for (int i = 0; i < 16; i++) {
       double seed = (i * 0.618) % 1.0, ph = (t * (0.9 + seed * 0.5) + seed) % 1.0;
       float x = (float)(seed * (w + Px(20))) - (float)ph * Px(8), y = (float)(ph * (h + Px(10))) - Px(6);
       g.DrawLine(pen, x, y, x - Px(2), y + Px(6));
      }
     }
     if (weather.Kind == "storm" && (t % 7) < 0.12) using (var flash = new SolidBrush(Color.FromArgb(40, 220, 230, 255))) g.FillRectangle(flash, 0, 0, w, h);
    } else if (weather.Kind == "snow" || (weather.Temp.HasValue && weather.Temp.Value <= 10)) {
     for (int i = 0; i < 14; i++) {
      double seed = (i * 0.618) % 1.0, ph = (t * (0.12 + seed * 0.1) + seed) % 1.0;
      float x = (float)(seed * w) + (float)Math.Sin(t * 0.8 + i) * Px(3), y = (float)(ph * (h + Px(6))) - Px(3), r = Px(0.8f + (float)seed);
      using (var flake = new SolidBrush(Color.FromArgb(110, 240, 244, 255))) g.FillEllipse(flake, x - r, y - r, r * 2, r * 2);
     }
    } else if (weather.Kind == "sun") {
     using (var path = new GraphicsPath()) {
      float r = h * 1.6f; path.AddEllipse(w - r * .9f, -r * .9f, r * 1.8f, r * 1.8f);
      using (var warm = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(34 + 8 * Math.Sin(t * .7)), 255, 200, 120), SurroundColors = new[] { Color.FromArgb(0, 255, 200, 120) } }) g.FillPath(warm, path);
     }
    } else if (weather.Kind == "fog") {
     for (int i = 0; i < 3; i++) {
      float x = (float)(((t * 6 * (i + 1)) % (w + 120)) - 120), y = h * (0.25f + i * 0.25f);
      using (var band = new LinearGradientBrush(new RectangleF(x, y - 5, 121, 10), Color.FromArgb(0, 200, 205, 215), Color.FromArgb(0, 200, 205, 215), 0f)) {
       band.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(0, 200, 205, 215), Color.FromArgb(28, 200, 205, 215), Color.FromArgb(0, 200, 205, 215) }, Positions = new[] { 0f, .5f, 1f } };
       g.FillRectangle(band, x, y - 4, 120, 8);
      }
     }
    }
   } finally { g.SmoothingMode = mode; }
  }
 }
}
