namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;
 using System.Linq;

 // A colleague finished an issue in one of your projects: their creature runs onto your desktop, dances with the
 // issue number and runs off. Everything comes from Redmine (the projects of your own recent issues); nothing is stored.
 public partial class Widget {
  sealed class Visitor { public string Name = ""; public int Issue; public Color Tint; public DateTime At = DateTime.MinValue; }
  readonly Queue<Visitor> visitors = new Queue<Visitor>(); Visitor visitor;
  DateTime nextColleagues = DateTime.Now.AddMinutes(1); bool colleaguesBusy; DateTimeOffset colleaguesSince = DateTimeOffset.UtcNow;
  List<int> teamProjects; int myUserId; readonly HashSet<int> seenFinished = new HashSet<int>();

  async void CheckColleagues() {
   if (!settings.Colleagues || colleaguesBusy || DateTime.Now < nextColleagues || preview) return;
   nextColleagues = DateTime.Now.AddMinutes(5); colleaguesBusy = true;
   try {
    using (var client = RedmineClient.FromMcpConfig()) {
     if (client == null) { nextColleagues = DateTime.Now.AddHours(1); return; }
     if (myUserId == 0) myUserId = (int)AutoLog.Number(Data.Get(Data.Map(Data.Get(await client.Get("/users/current.json"), "user")), "id"));
     if (teamProjects == null || teamProjects.Count == 0) {
      var ids = myIssues.Concat(tasks.Select(t => t.Id)).Distinct().Take(40).ToList(); if (ids.Count == 0) return;
      var page = await client.Get("/issues.json?status_id=*&limit=100&issue_id=" + String.Join(",", ids));
      teamProjects = RedmineClient.Items(Data.Get(page, "issues")).Select(i => (int)AutoLog.Number(Data.Get(Data.Map(Data.Get(Data.Map(i), "project")), "id"))).Where(p => p > 0).Distinct().Take(6).ToList();
     }
     DateTimeOffset newest = colleaguesSince;
     foreach (int project in teamProjects) {
      var page = await client.Get("/issues.json?project_id=" + project + "&status_id=" + Score.FinishedStatus + "&sort=closed_on:desc&limit=15");
      foreach (var raw in RedmineClient.Items(Data.Get(page, "issues"))) {
       var issue = Data.Map(raw); int id = (int)AutoLog.Number(Data.Get(issue, "id"));
       string closedText = Data.Str(Data.Get(issue, "closed_on")); DateTimeOffset closed;
       if (closedText.Length == 0 || !DateTimeOffset.TryParse(closedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out closed) || closed <= colleaguesSince) continue;
       var who = Data.Map(Data.Get(issue, "assigned_to")); if (who == null) continue;
       int whoId = (int)AutoLog.Number(Data.Get(who, "id")); if (whoId == myUserId || !seenFinished.Add(id)) continue;
       string name = Data.Str(Data.Get(who, "name")).Split(' ')[0];
       visitors.Enqueue(new Visitor { Name = name, Issue = id, Tint = FamilyTints[Math.Abs(name.GetHashCode()) % FamilyTints.Length] });
       if (closed > newest) newest = closed;
      }
     }
     colleaguesSince = newest;
    }
   } catch { } finally { colleaguesBusy = false; }
  }

  // One visitor at a time: in (1.6 s), dance (4 s), out (1.6 s).
  void VisitTick() {
   if (visitor != null && (DateTime.Now - visitor.At).TotalSeconds > 7.4) visitor = null;
   if (visitor == null && visitors.Count > 0 && deskPet != null && !NightIdle) {
    visitor = visitors.Dequeue(); visitor.At = DateTime.Now;
   }
   if (visitor != null) {
    double e = (DateTime.Now - visitor.At).TotalSeconds;
    if (e >= 1.6 && (DateTime.Now - hopAt).TotalSeconds > 2 && e < 1.8) { hopAt = DateTime.Now; Say("Olha o " + visitor.Name + "! Finalizou a #" + visitor.Issue + "!", true); }
   }
  }
  string VisitBanner { get { return visitor != null && (DateTime.Now - visitor.At).TotalSeconds < 6 ? "★ " + visitor.Name + " finalizou a #" + visitor.Issue + "!" : null; } }

  void PaintVisitor(Graphics g, float ground) {
   if (visitor == null) return;
   double e = (DateTime.Now - visitor.At).TotalSeconds, t = MotionTime.Now / 1000.0;
   float sf = ScaleFactor, px = Math.Max(1f, sf * 2.9f), stop = 62 * sf, start = -20 * sf;
   float x = e < 1.6 ? start + (stop - start) * (float)MotionTime.Ease(e / 1.6) : e < 5.6 ? stop : stop - (stop - start) * (float)Math.Min(1, (e - 5.6) / 1.6);
   bool walking = e < 1.6 || e >= 5.6, dancing = !walking;
   int lift = walking ? ((int)(t * 10)) % 2 : (int)Math.Round(Math.Abs(Math.Sin(t * 6)) * 3);
   float ox = x - 6 * px, oy = ground - 10 * px - lift * px;
   var mode = g.SmoothingMode;
   g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0))) g.FillEllipse(shadow, x - 5 * px, ground - .6f * px, 10 * px, 1.4f * px);
   g.SmoothingMode = SmoothingMode.None;
   Color body = visitor.Tint, shade = Color.FromArgb(body.R * 3 / 4, body.G * 3 / 4, body.B * 3 / 4), dark = Color.FromArgb(28, 26, 30);
   var p = new Pix(g, ox, oy, px);
   try {
    for (int r = 0; r < CreatureBody.Length; r++) for (int c = 0; c < 12; c++) if (CreatureBody[r][c] == '#') p.Dot(c, r, r == 7 ? shade : body);
    if (dancing) { p.Dot(3, 5, dark); p.Dot(4, 4, dark); p.Dot(5, 5, dark); p.Dot(6, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 5, dark); p.Dot(2, 6, Color.FromArgb(200, 240, 130, 150)); p.Dot(9, 6, Color.FromArgb(200, 240, 130, 150)); }
    else { int look = e < 1.6 ? 1 : -1; p.Dot(4 + look, 4, dark); p.Dot(4 + look, 5, dark); p.Dot(7 + look, 4, dark); p.Dot(7 + look, 5, dark); }
    bool step = ((int)(t * 10)) % 2 == 0;
    p.Dot(3, walking && step ? 7.5f : 8, shade); p.Dot(8, walking && !step ? 7.5f : 8, shade);
    if (dancing) { bool up = ((int)(t * 4)) % 2 == 0; p.Dot(-1, up ? 1 : 4, shade); p.Dot(12, up ? 4 : 1, shade); }
    else { p.Dot(-1, step ? 5 : 6, shade); p.Dot(12, step ? 6 : 5, shade); }
   } finally { p.Dispose(); }
   g.SmoothingMode = SmoothingMode.AntiAlias;
   // While dancing: a little flag with the issue number and the name over the head.
   if (dancing) {
    string text = visitor.Name + " · #" + visitor.Issue + " ✓"; var font = Theme.F(Theme.SansBold, 7.5f);
    SizeF size = g.MeasureString(text, font); float tw = size.Width + 8 * sf, th = size.Height + 2 * sf, tx = Math.Max(2 * sf, x - tw / 2), ty = oy - th - 4 * sf;
    using (var path = Rounded(new RectangleF(tx, ty, tw, th), th / 2)) using (var fill = new SolidBrush(Color.FromArgb(230, body))) g.FillPath(fill, path);
    using (var ink2 = new SolidBrush(Color.FromArgb(40, 30, 30))) g.DrawString(text, font, ink2, tx + 4 * sf, ty + 1 * sf);
    // Confetti around them.
    for (int i = 0; i < 6; i++) {
     double ph = (t * .8 + i / 6.0) % 1.0; float cx = x + (float)Math.Sin(i * 2.1 + t) * 16 * sf, cy = oy - 6 * sf + (float)ph * 30 * sf;
     using (var bit = new SolidBrush(Color.FromArgb((int)(220 * (1 - ph)), FamilyTints[i % FamilyTints.Length]))) g.FillRectangle(bit, cx, cy, 2.4f * sf, 2.4f * sf);
    }
   }
   g.SmoothingMode = mode;
  }
 }
}
