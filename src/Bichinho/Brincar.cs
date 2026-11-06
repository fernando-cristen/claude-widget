namespace MeuClaudeWidget {
 using System;
 using System.Drawing;
 using System.Drawing.Drawing2D;

 // Music (Spotify) and the ball game while Claude works.
 public partial class Widget {
  // ---- music: the Spotify window title is "Artist - Song" while playing and "Spotify ..." when paused ----
  string song = ""; DateTime nextMusic = DateTime.MinValue;
  bool MusicOn { get { return settings.Music && song.Length > 0; } }
  void TrackMusic() {
   if (!settings.Music || DateTime.Now < nextMusic) return;
   nextMusic = DateTime.Now.AddSeconds(3);
   string title = "";
   try {
    foreach (var p in System.Diagnostics.Process.GetProcessesByName("Spotify"))
     using (p) { string t = p.MainWindowTitle; if (!String.IsNullOrEmpty(t)) title = t; }
   } catch { }
   bool playing = title.Length > 0 && !title.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase) && title != "Advertisement";
   string next = playing ? title : "";
   if (next.Length > 0 && next != song && song.Length == 0) Say(Pick("Que som!", "Aumenta!", "Essa é boa!"));
   song = next;
  }

  // ---- ball (one of the games in Jogos.cs): it throws a ball; click it to catch ----
  PointF ballPos, ballVel; bool ballLive; int ballBounces, ballStreak; double ballClock;
  DateTime ballThrowAt = DateTime.MinValue, ballCaughtAt = DateTime.MinValue; PointF ballCaughtPos;
  float BallR { get { return 7f * ScaleFactor; } }
  void BallTick() {
   double clock = MotionTime.Now / 1000.0; float dt = (float)Math.Max(0, Math.Min(0.05, clock - ballClock)); ballClock = clock;
   if (!ballLive) { if ((DateTime.Now - ballThrowAt).TotalSeconds > 2.2 && (DateTime.Now - ballCaughtAt).TotalSeconds > 1.4 && !desk.Head.IsEmpty) Throw(); return; }
   float sf = ScaleFactor, r = BallR, floor = DeskGround - r;
   ballVel.Y += 620 * sf * dt;
   ballPos = new PointF(ballPos.X + ballVel.X * dt, ballPos.Y + ballVel.Y * dt);
   if (ballPos.X < r || ballPos.X > DeskW - r) { ballVel.X = -ballVel.X * .8f; ballPos.X = Math.Max(r, Math.Min(DeskW - r, ballPos.X)); }
   if (ballPos.Y > floor) {
    ballPos.Y = floor; ballVel.Y = -ballVel.Y * .5f; ballVel.X *= .75f; ballBounces++;
    if (ballBounces >= 3 || Math.Abs(ballVel.Y) < 60 * sf) {
     // Landed: it goes to pick it up and throws again in a moment.
     ballLive = false; ballThrowAt = DateTime.Now;
     if (ballStreak >= 3) Say("Ah, caiu! Foram " + ballStreak + " seguidas."); ballStreak = 0;
    }
   }
  }
  void Throw() {
   float sf = ScaleFactor;
   ballPos = new PointF(desk.Head.X + 7 * DeskPx, desk.Head.Y);
   ballVel = new PointF((talk.Next(2) == 0 ? -1 : 1) * (50 + talk.Next(90)) * sf, -(280 + talk.Next(80)) * sf);
   ballLive = true; ballBounces = 0; ballThrowAt = DateTime.Now;
   if (ballStreak == 0 && talk.Next(3) == 0) Say(Pick("Pega!", "Bora brincar?", "Lá vai!"));
  }
  // Clicks on the home window land here first: a catch, if the ball is near the click.
  bool CatchBall(Point at) {
   if (!ballLive) return false;
   float dx = at.X - ballPos.X, dy = at.Y - ballPos.Y;
   if (dx * dx + dy * dy > BallR * BallR * 9) return false;
   ballLive = false; ballCaughtAt = DateTime.Now; ballCaughtPos = ballPos; ballThrowAt = DateTime.Now; ballStreak++;
   hopAt = DateTime.Now;
   Say(ballStreak >= 3 ? ballStreak + " seguidas!" : Pick("Boa!", "Pegou!", "Isso!"), true);
   return true;
  }
  void PaintBall(Graphics g) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   float r = BallR, sf = ScaleFactor;
   if (ballLive) {
    // Shadow on the floor, smaller the higher it goes.
    float height = Math.Max(0, DeskGround - r - ballPos.Y), k = Math.Max(.3f, 1 - height / (120 * sf));
    using (var shadow = new SolidBrush(Color.FromArgb((int)(60 * k), 0, 0, 0))) g.FillEllipse(shadow, ballPos.X - r * k, DeskGround - 1.5f * sf, r * 2 * k, 3 * sf);
    // A faint halo makes it easier to click (and keeps the click on this window).
    using (var halo = new SolidBrush(Color.FromArgb(3, 255, 255, 255))) g.FillEllipse(halo, ballPos.X - r * 3, ballPos.Y - r * 3, r * 6, r * 6);
    var box = new RectangleF(ballPos.X - r, ballPos.Y - r, r * 2, r * 2);
    float spin = (float)(MotionTime.Now / 1000.0 * 540 * Math.Sign(ballVel.X == 0 ? 1 : ballVel.X));
    using (var white = new SolidBrush(Color.FromArgb(246, 246, 248))) g.FillEllipse(white, box);
    using (var red = new SolidBrush(Color.FromArgb(230, 70, 70))) g.FillPie(red, box.X, box.Y, box.Width, box.Height, spin, 60);
    using (var blue = new SolidBrush(Color.FromArgb(70, 130, 230))) g.FillPie(blue, box.X, box.Y, box.Width, box.Height, spin + 120, 60);
    using (var yellow = new SolidBrush(Color.FromArgb(246, 200, 70))) g.FillPie(yellow, box.X, box.Y, box.Width, box.Height, spin + 240, 60);
    using (var edge = new Pen(Color.FromArgb(90, 0, 0, 0), 1f)) g.DrawEllipse(edge, box);
   }
   // A burst where it was caught.
   double c = (DateTime.Now - ballCaughtAt).TotalSeconds / .5;
   if (c >= 0 && c < 1)
    using (var spark = new Pen(Color.FromArgb((int)(230 * (1 - c)), 255, 236, 150), 1.5f * sf))
     for (int i = 0; i < 8; i++) {
      double a = i * Math.PI / 4; float d0 = (float)(r + c * 10 * sf), d1 = d0 + 4 * sf;
      g.DrawLine(spark, ballCaughtPos.X + (float)Math.Cos(a) * d0, ballCaughtPos.Y + (float)Math.Sin(a) * d0, ballCaughtPos.X + (float)Math.Cos(a) * d1, ballCaughtPos.Y + (float)Math.Sin(a) * d1);
     }
   g.SmoothingMode = mode;
  }
 }
}
