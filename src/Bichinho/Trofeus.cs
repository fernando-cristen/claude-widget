namespace MeuClaudeWidget {
 using System;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;
 using System.Linq;

 // Trophies on a small shelf next to the desktop creature. Earned ones shine; the others are faint outlines to aim for.
 public partial class Widget {
  sealed class Trophy { public string Id, Title, Kind; public Color Tint; }
  static readonly Trophy[] AllTrophies = {
   new Trophy { Id = "t10", Title = "10 tarefas finalizadas", Kind = "cup", Tint = Color.FromArgb(205, 132, 82) },
   new Trophy { Id = "t25", Title = "25 tarefas finalizadas", Kind = "cup", Tint = Color.FromArgb(196, 204, 214) },
   new Trophy { Id = "t50", Title = "50 tarefas finalizadas", Kind = "cup", Tint = Color.FromArgb(246, 200, 70) },
   new Trophy { Id = "streak3", Title = "3 semanas sem rejeição", Kind = "medal", Tint = Color.FromArgb(246, 200, 70) },
   new Trophy { Id = "friday", Title = "Sexta com tudo finalizado", Kind = "star", Tint = Color.FromArgb(250, 214, 90) }
  };
  DateTime trophyAt = DateTime.MinValue; string trophyTitle = "", trophyId = "";

  bool Earned(string id) {
   DateTime n = DateTime.Now; string today = n.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   switch (id) {
    case "t10": return settings.CreatureXp >= 10;
    case "t25": return settings.CreatureXp >= 25;
    case "t50": return settings.CreatureXp >= 50;
    case "streak3": return Streak >= 3;
    // Friday after 17:00: something was finished today and nothing is left in progress.
    case "friday": return n.DayOfWeek == DayOfWeek.Friday && n.Hour >= 17 && settings.LastFinishedDay == today && tasks.Count == 0;
   }
   return false;
  }
  void CheckTrophies() {
   if (settings.Trophies == null) settings.Trophies = new System.Collections.Generic.List<string>();
   // The first time, what was already earned goes on the shelf quietly.
   if (!settings.TrophiesSeeded) {
    foreach (var t in AllTrophies) if (Earned(t.Id) && !settings.Trophies.Contains(t.Id)) settings.Trophies.Add(t.Id);
    settings.TrophiesSeeded = true; TrySave(); return;
   }
   foreach (var t in AllTrophies) {
    if (settings.Trophies.Contains(t.Id) || !Earned(t.Id)) continue;
    settings.Trophies.Add(t.Id); TrySave();
    trophyAt = DateTime.Now; trophyTitle = t.Title; trophyId = t.Id; Say("Ganhei um troféu!", true); StartMotionTimer(); Invalidate();
    break;
   }
  }

  static readonly string[] CupIcon = { "#######", "#.###.#", ".#####.", "..###..", "...#...", "..###..", ".#####." };
  static readonly string[] MedalIcon = { "#.....#", ".#...#.", "..#.#..", "..###..", ".#####.", ".#####.", "..###.." };
  static readonly string[] CrownIcon = { "#..#..#", "#.###.#", "#######", "#######", ".#####." };
  static readonly string[] StarIcon = { "...#...", "..###..", "#######", ".#####.", "..###..", ".##.##.", "#.....#" };

  void PaintShelf(Graphics g, float x, float y, float width) {
   float sf = ScaleFactor;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var wood = new SolidBrush(Color.FromArgb(232, 150, 104, 66))) g.FillRectangle(wood, x, y, width, 5 * sf);
   using (var top = new SolidBrush(Color.FromArgb(240, 186, 136, 92))) g.FillRectangle(top, x, y, width, 1.6f * sf);
   using (var under = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(under, x + 2 * sf, y + 5 * sf, width - 4 * sf, 2 * sf);
   using (var bracket = new SolidBrush(Color.FromArgb(220, 120, 84, 54)))
    foreach (float bx in new[] { x + 8 * sf, x + width - 12 * sf }) g.FillPolygon(bracket, new[] { new PointF(bx, y + 5 * sf), new PointF(bx + 4 * sf, y + 5 * sf), new PointF(bx, y + 13 * sf) });
   g.SmoothingMode = SmoothingMode.None;
   float slot = width / AllTrophies.Length, px = Math.Max(1f, 2.3f * sf);
   double t = settings.Animations ? MotionTime.Now / 1000.0 : 0;
   for (int i = 0; i < AllTrophies.Length; i++) {
    var trophy = AllTrophies[i]; bool owned = settings.Trophies != null && settings.Trophies.Contains(trophy.Id);
    string[] icon = trophy.Kind == "cup" ? CupIcon : trophy.Kind == "medal" ? MedalIcon : trophy.Kind == "crown" ? CrownIcon : StarIcon;
    float ox = x + slot * i + (slot - 7 * px) / 2, oy = y - icon.Length * px;
    bool fresh = owned && trophy.Id == trophyId && (DateTime.Now - trophyAt).TotalSeconds < 3.5;
    if (fresh) oy -= (float)Math.Abs(Math.Sin(t * 6)) * px * 2;
    Color main = owned ? trophy.Tint : Color.FromArgb(26, 255, 255, 255);
    Color dim = owned ? Color.FromArgb(trophy.Tint.R * 3 / 4, trophy.Tint.G * 3 / 4, trophy.Tint.B * 3 / 4) : main;
    var p = new Pix(g, ox, oy, px);
    try {
     for (int r = 0; r < icon.Length; r++) for (int c = 0; c < 7; c++) if (icon[r][c] == '#') {
      Color color = main;
      if (trophy.Kind == "medal" && r < 3) color = owned ? (c < 3 ? Color.FromArgb(214, 64, 70) : Color.FromArgb(70, 120, 214)) : main;
      else if (trophy.Kind == "cup" && r >= 5) color = owned ? Color.FromArgb(110, 76, 52) : main;
      else if (r == icon.Length - 1 || c == 6) color = dim;
      p.Dot(c, r, color);
     }
     if (owned) p.Dot(trophy.Kind == "crown" ? 2 : 2, trophy.Kind == "medal" ? 4 : 1, Color.FromArgb(220, 255, 255, 255));
     if (owned && trophy.Kind == "crown") p.Dot(3, 1, Color.FromArgb(220, 60, 90));
    } finally { p.Dispose(); }
    // A sparkle on the newest one, and a slow glint on all of them.
    if (fresh || (owned && settings.Animations && (t * 0.35 + i * 0.37) % 1.0 < 0.04)) {
     g.SmoothingMode = SmoothingMode.AntiAlias;
     float cx = ox + 5.5f * px, cy = oy + px, r2 = px * (fresh ? 2.2f : 1.4f);
     using (var star = new Pen(Color.FromArgb(230, 255, 248, 210), Math.Max(1f, px * .5f))) { g.DrawLine(star, cx - r2, cy, cx + r2, cy); g.DrawLine(star, cx, cy - r2, cx, cy + r2); }
     g.SmoothingMode = SmoothingMode.None;
    }
   }
   g.SmoothingMode = mode;
  }
 }
}
