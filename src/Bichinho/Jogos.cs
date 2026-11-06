namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;
 using System.Linq;

 // Little games in the desktop home while Claude works (from 3 s into a turn). One is drawn at random and
 // swapped for another every ~45 s: ball, hide and seek, tag, bubbles, fishing, rock-paper-scissors,
 // rhythm (with music), guess-how-long and watering the plant. Everything is played with clicks.
 public partial class Widget {
  static readonly string[] AllGames = { "ball", "hide", "chase", "bubbles", "fish", "rps", "rhythm", "guess", "water" };
  static readonly string[][] GameNames = {
   new[] { "ball", "Bolinha" }, new[] { "hide", "Esconde-esconde" }, new[] { "chase", "Pega-pega" }, new[] { "bubbles", "Bolhas de sabão" },
   new[] { "fish", "Pescaria" }, new[] { "rps", "Pedra, papel e tesoura" }, new[] { "rhythm", "Ritmo (com música)" }, new[] { "guess", "Adivinha quanto tempo" }, new[] { "water", "Regar a plantinha" }
  };
  string game = "", lastGame = "", forcedGame = ""; DateTime gameAt = DateTime.MinValue, playTestUntil = DateTime.MinValue, turnStart = DateTime.MinValue;
  double gameClock; float gameShift; DateTime nagAt = DateTime.MinValue;

  bool PlayTime {
   get {
    if (!settings.Games || deskPet == null || Away || NightIdle || desk.Held) return false;
    if (DateTime.Now < playTestUntil) return true;
    return mood == "work" && (DateTime.Now - moodSince).TotalSeconds >= 3;
   }
  }
  float Sf { get { return ScaleFactor; } }

  void GameTick() {
   double clock = MotionTime.Now / 1000.0; float dt = (float)Math.Max(0, Math.Min(0.05, clock - gameClock)); gameClock = clock;
   if (!PlayTime) {
    if (game.Length > 0) EndGame(true);
    gameShift *= .85f; if (Math.Abs(gameShift) < .5f) gameShift = 0;
    desk.Walking = false;
    return;
   }
   if (game.Length == 0) { turnStart = DateTime.Now.AddSeconds(-3); StartGame(NextGame()); }
   else if (forcedGame.Length == 0 && game != "guess" && (DateTime.Now - gameAt).TotalSeconds > 45) StartGame(NextGame());
   switch (game) {
    case "ball": BallTick(); break;
    case "hide": HideTick(); break;
    case "chase": ChaseTick(dt); break;
    case "bubbles": BubbleTick(dt); break;
    case "fish": FishTick(); break;
    case "rps": RpsTick(); break;
    case "rhythm": RhythmTick(); break;
    case "water": WaterTick(); break;
   }
  }
  string NextGame() {
   if (forcedGame.Length > 0) return forcedGame;
   var options = AllGames.Where(x => x != lastGame && (x != "rhythm" || MusicOn) && (x != "water" || PlantThirsty) && (x != "guess" || game.Length == 0)).ToList();
   return options[talk.Next(options.Count)];
  }
  void StartGame(string name) {
   if (game.Length > 0) EndGame(false);
   game = name; lastGame = name; gameAt = DateTime.Now; desk.Act = ""; desk.LookTarget = PointF.Empty;
   switch (name) {
    case "ball": ballThrowAt = DateTime.MinValue; break;
    case "hide": HideStart(); break;
    case "chase": chaseIt = true; Say("Pega-pega! Tô indo te pegar!", true); break;
    case "bubbles": bubbles.Clear(); bubbleScore = 0; Say("Estoura as bolhas!", true); break;
    case "fish": FishCast(); Say("Bora pescar? Clica quando a boia afundar!", true); break;
    case "rps": rpsPhase = "choose"; Say("Pedra, papel ou tesoura?", true); break;
    case "rhythm": beats.Clear(); nextBeat = DateTime.Now.AddSeconds(.6); Say("Clica no ritmo!", true); break;
    case "guess": GuessStart(); break;
    case "water": Say("A plantinha tá com sede! Clica nela.", true); break;
   }
  }
  void EndGame(bool turnEnded) {
   if (game == "guess" && turnEnded) GuessResolve();
   if (game == "bubbles" && bubbleScore >= 5) Say("Você estourou " + bubbleScore + " bolhas!");
   ballLive = false; bubbles.Clear(); beats.Clear(); desk.Act = ""; desk.LookTarget = PointF.Empty; desk.Walking = false;
   game = ""; if (turnEnded) forcedGame = "";
  }
  // Clicks on the home window go to the game first.
  bool GameClick(Point at) {
   if (PlantClick(at)) return true;
   switch (game) {
    case "ball": return CatchBall(at);
    case "hide": return HideClick(at);
    case "chase": return ChaseClick(at);
    case "bubbles": return BubbleClick(at);
    case "fish": return FishClick(at);
    case "rps": return RpsClick(at);
    case "rhythm": return RhythmClick(at);
    case "guess": return GuessClick(at);
   }
   return false;
  }
  // Drawn behind the creature.
  void PaintGameBack(Graphics g) { if (game == "fish") PaintPond(g); }
  // Drawn over it.
  void PaintGameFront(Graphics g) {
   switch (game) {
    case "ball": PaintBall(g); break;
    case "bubbles": PaintBubbles(g); break;
    case "fish": PaintFishing(g); break;
    case "rps": PaintRps(g); break;
    case "rhythm": PaintRhythm(g); break;
    case "guess": PaintGuess(g); break;
   }
   if (game != "ball") PaintBall(g); // the catch burst may still be fading
   PaintFishJump(g);
  }
  // Hide and seek draws the creature itself (behind a prop).
  bool PaintGameCreature(Graphics g) { if (game != "hide") return false; PaintHide(g); return true; }
  float HomeX { get { return DeskW / 2f + gameShift; } }

  // ---- hide and seek: three props; it hides behind one and peeks now and then ----
  int hideSpot; DateTime hideFoundAt = DateTime.MinValue, hideShakeAt = DateTime.MinValue; int hideShakeSpot = -1;
  float[] HideX { get { return new[] { 54 * Sf, 160 * Sf, 236 * Sf }; } }
  void HideStart() { hideSpot = talk.Next(3); hideFoundAt = DateTime.MinValue; Say("Me acha! Tô escondido...", true); }
  void HideTick() { if (hideFoundAt != DateTime.MinValue && (DateTime.Now - hideFoundAt).TotalSeconds > 2.6) { hideSpot = (hideSpot + 1 + talk.Next(2)) % 3; hideFoundAt = DateTime.MinValue; Say("De novo! Me acha!"); } }
  RectangleF PropBox(int i) { float w = (i == 0 ? 54 : i == 1 ? 46 : 40) * Sf, h = (i == 0 ? 34 : i == 1 ? 34 : 38) * Sf; return new RectangleF(HideX[i] - w / 2, DeskGround - h, w, h); }
  bool HideClick(Point at) {
   for (int i = 0; i < 3; i++) {
    if (!PropBox(i).Contains(at)) continue;
    if (hideFoundAt != DateTime.MinValue) return true;
    if (i == hideSpot) { hideFoundAt = DateTime.Now; hopAt = DateTime.Now; Say(Pick("Achou!", "Ah, me achou!", "Droga, achou!"), true); }
    else { hideShakeAt = DateTime.Now; hideShakeSpot = i; Say(Math.Abs(i - hideSpot) == 1 ? "Quente!" : "Frio!", true); }
    return true;
   }
   return false;
  }
  void PaintHide(Graphics g) {
   float px = DeskPx; double t = MotionTime.Now / 1000.0;
   for (int i = 0; i < 3; i++) {
    var box = PropBox(i);
    if (i == hideSpot) {
     bool found = hideFoundAt != DateTime.MinValue; bool peek = !found && (t % 4.2) < 0.8;
     // Rows above the top of the prop: just the top of the head, the eyes when peeking, all of it when found.
     float rows = found ? 9.5f : peek ? 5.6f : 0.9f;
     float oy = box.Top - rows * px; int top = (int)Math.Round(oy - (float)Math.Round(9.5f * ScaleFactor * desk.Zoom));
     desk.Act = "hide"; desk.LookTarget = PointF.Empty;
     PaintCreature(g, (int)Math.Round(HideX[i] - 7 * px), top);
    }
    float shake = i == hideShakeSpot && (DateTime.Now - hideShakeAt).TotalSeconds < .5 ? (float)Math.Sin(t * 50) * 2 * Sf : 0;
    PaintProp(g, i, new RectangleF(box.X + shake, box.Y, box.Width, box.Height));
   }
  }
  void PaintProp(Graphics g, int kind, RectangleF b) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; float sf = Sf;
   using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(shadow, b.X + 2 * sf, b.Bottom - 3 * sf, b.Width - 4 * sf, 5 * sf);
   if (kind == 0) { // bush
    using (var dark = new SolidBrush(Color.FromArgb(62, 140, 80))) { g.FillEllipse(dark, b.X, b.Y + b.Height * .3f, b.Width * .5f, b.Height * .7f); g.FillEllipse(dark, b.X + b.Width * .5f, b.Y + b.Height * .3f, b.Width * .5f, b.Height * .7f); }
    using (var leaf = new SolidBrush(Color.FromArgb(92, 176, 104))) g.FillEllipse(leaf, b.X + b.Width * .2f, b.Y, b.Width * .6f, b.Height * .8f);
    using (var dot = new SolidBrush(Color.FromArgb(236, 96, 110))) { g.FillEllipse(dot, b.X + b.Width * .3f, b.Y + b.Height * .35f, 4 * sf, 4 * sf); g.FillEllipse(dot, b.X + b.Width * .65f, b.Y + b.Height * .5f, 4 * sf, 4 * sf); }
   } else if (kind == 1) { // box
    using (var card = new SolidBrush(Color.FromArgb(198, 150, 98))) g.FillRectangle(card, b);
    using (var top = new SolidBrush(Color.FromArgb(222, 182, 128))) g.FillRectangle(top, b.X, b.Y, b.Width, 6 * sf);
    using (var tape = new SolidBrush(Color.FromArgb(240, 226, 180))) g.FillRectangle(tape, b.X + b.Width / 2 - 3 * sf, b.Y, 6 * sf, b.Height * .6f);
    using (var edge = new Pen(Color.FromArgb(150, 108, 66), 1.2f * sf)) g.DrawRectangle(edge, b.X, b.Y, b.Width, b.Height);
   } else { // barrel
    using (var shape = Rounded(b, 7 * sf)) {
     using (var wood = new SolidBrush(Color.FromArgb(150, 98, 58))) g.FillPath(wood, shape);
     using (var edge = new Pen(Color.FromArgb(100, 64, 38), 1.2f * sf)) g.DrawPath(edge, shape);
    }
    using (var band = new SolidBrush(Color.FromArgb(110, 112, 120))) { g.FillRectangle(band, b.X, b.Y + b.Height * .22f, b.Width, 3 * sf); g.FillRectangle(band, b.X, b.Y + b.Height * .7f, b.Width, 3 * sf); }
   }
   g.SmoothingMode = mode;
  }

  // ---- tag: it runs after the mouse; once it catches you, run and click it to catch it back ----
  bool chaseIt; DateTime chaseSwapAt = DateTime.MinValue;
  void ChaseTick(float dt) {
   Point c = deskPet.PointToClient(Cursor.Position); bool inside = deskPet.ClientRectangle.Contains(c);
   float limit = DeskW / 2f - 36 * Sf, x = HomeX, target = gameShift;
   if (inside) {
    if (chaseIt) target = c.X - DeskW / 2f;
    else target = gameShift + Math.Sign(x - c.X == 0 ? 1 : x - c.X) * 60 * Sf;
   }
   target = Math.Max(-limit, Math.Min(limit, target));
   float speed = (chaseIt ? 150 : 120) * Sf * dt, d = target - gameShift;
   gameShift += Math.Abs(d) < speed ? d : Math.Sign(d) * speed;
   desk.Walking = Math.Abs(d) > 2 * Sf;
   desk.LookTarget = inside ? new PointF(c.X, c.Y) : PointF.Empty;
   // Caught you: the cursor is on it.
   if (chaseIt && inside && (DateTime.Now - chaseSwapAt).TotalSeconds > 1.2 && !desk.Creature.IsEmpty && desk.Creature.Contains(c)) {
    chaseIt = false; chaseSwapAt = DateTime.Now; hopAt = DateTime.Now; Say("Peguei! Agora me pega!", true);
   }
  }
  bool ChaseClick(Point at) {
   if (chaseIt || desk.Creature.IsEmpty || !desk.Creature.Contains(at)) return false;
   chaseIt = true; chaseSwapAt = DateTime.Now; dizzyUntil = DateTime.Now.AddSeconds(1); Say(Pick("Ai, pegou!", "Tá com você! Quer dizer, comigo!"), true);
   return true;
  }

  // ---- soap bubbles: it blows them, you pop them (gold ones are worth 5) ----
  sealed class Bubble { public PointF P; public float R, Seed; public bool Gold; }
  readonly List<Bubble> bubbles = new List<Bubble>(); DateTime nextBubble = DateTime.MinValue; int bubbleScore;
  readonly List<KeyValuePair<PointF,DateTime>> pops = new List<KeyValuePair<PointF,DateTime>>();
  void BubbleTick(float dt) {
   desk.Act = (DateTime.Now - nextBubble).TotalSeconds > -.25 ? "wow" : "";
   if (DateTime.Now >= nextBubble && bubbles.Count < 8 && !desk.Head.IsEmpty) {
    bubbles.Add(new Bubble { P = new PointF(desk.Head.X + 2 * DeskPx, desk.Head.Y + 3 * DeskPx), R = (5 + talk.Next(6)) * Sf, Seed = talk.Next(100), Gold = talk.Next(9) == 0 });
    nextBubble = DateTime.Now.AddSeconds(.7 + talk.NextDouble() * .5);
   }
   double t = MotionTime.Now / 1000.0;
   foreach (var b in bubbles) b.P = new PointF(b.P.X + (float)Math.Sin(t * 2 + b.Seed) * 14 * Sf * dt + 6 * Sf * dt, b.P.Y - (24 + b.R) * Sf * dt);
   bubbles.RemoveAll(b => b.P.Y < -b.R);
   pops.RemoveAll(p => (DateTime.Now - p.Value).TotalSeconds > .4);
  }
  bool BubbleClick(Point at) {
   for (int i = bubbles.Count - 1; i >= 0; i--) {
    var b = bubbles[i]; float dx = at.X - b.P.X, dy = at.Y - b.P.Y;
    if (dx * dx + dy * dy > (b.R + 4 * Sf) * (b.R + 4 * Sf)) continue;
    bubbles.RemoveAt(i); pops.Add(new KeyValuePair<PointF,DateTime>(b.P, DateTime.Now));
    int before = bubbleScore; bubbleScore += b.Gold ? 5 : 1;
    if (b.Gold) Say("Dourada! +5", true); else if (bubbleScore / 10 > before / 10) Say(bubbleScore + " bolhas!", true);
    return true;
   }
   return false;
  }
  void PaintBubbles(Graphics g) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   foreach (var b in bubbles) {
    Color tint = b.Gold ? Color.FromArgb(250, 210, 80) : Color.FromArgb(200, 230, 255);
    using (var fill = new SolidBrush(Color.FromArgb(b.Gold ? 90 : 40, tint))) g.FillEllipse(fill, b.P.X - b.R, b.P.Y - b.R, b.R * 2, b.R * 2);
    using (var edge = new Pen(Color.FromArgb(170, tint), 1.2f * Sf)) g.DrawEllipse(edge, b.P.X - b.R, b.P.Y - b.R, b.R * 2, b.R * 2);
    using (var shine = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(shine, b.P.X - b.R * .55f, b.P.Y - b.R * .6f, b.R * .45f, b.R * .35f);
   }
   foreach (var p in pops) {
    double k = (DateTime.Now - p.Value).TotalSeconds / .4;
    using (var pen = new Pen(Color.FromArgb((int)(200 * (1 - k)), 220, 240, 255), 1.2f * Sf))
     for (int i = 0; i < 6; i++) { double a = i * Math.PI / 3; float r0 = (float)(4 + k * 8) * Sf; g.DrawLine(pen, p.Key.X + (float)Math.Cos(a) * r0, p.Key.Y + (float)Math.Sin(a) * r0, p.Key.X + (float)Math.Cos(a) * (r0 + 3 * Sf), p.Key.Y + (float)Math.Sin(a) * (r0 + 3 * Sf)); }
   }
   if (bubbleScore > 0) DrawScore(g, "bolhas: " + bubbleScore);
   g.SmoothingMode = mode;
  }

  // ---- fishing: a pond on the left; click when the float goes under. Fish go to the bowl. ----
  DateTime biteAt = DateTime.MaxValue, fishCaughtAt = DateTime.MinValue;
  PointF PondCenter { get { return new PointF(66 * Sf, DeskGround + 1 * Sf); } }
  bool Biting { get { double e = (DateTime.Now - biteAt).TotalSeconds; return e >= 0 && e < 1.2; } }
  void FishCast() { biteAt = DateTime.Now.AddSeconds(2.5 + talk.NextDouble() * 4); }
  void FishTick() {
   desk.Act = "fish"; desk.LookTarget = PondCenter;
   if ((DateTime.Now - biteAt).TotalSeconds >= 1.2) { Say(Pick("Escapou!", "Ih, fugiu!"), true); FishCast(); }
  }
  bool FishClick(Point at) {
   var c = PondCenter; if (Math.Abs(at.X - c.X) > 56 * Sf || Math.Abs(at.Y - c.Y) > 24 * Sf) return false;
   if (Biting) {
    settings.Fish++; TrySave(); fishCaughtAt = DateTime.Now; hopAt = DateTime.Now;
    Say(settings.Fish == 1 ? "Primeiro peixe! Vai pro aquário!" : "Peguei! " + settings.Fish + " no aquário!", true); FishCast();
   } else if ((DateTime.Now - nagAt).TotalSeconds > 2) { nagAt = DateTime.Now; Say("Calma, ainda não mordeu!", true); }
   return true;
  }
  void PaintPond(Graphics g) {
   var c = PondCenter; float sf = Sf; double t = MotionTime.Now / 1000.0;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var rim = new SolidBrush(Color.FromArgb(150, 120, 100, 80))) g.FillEllipse(rim, c.X - 50 * sf, c.Y - 9 * sf, 100 * sf, 20 * sf);
   using (var water = new SolidBrush(Color.FromArgb(220, 70, 130, 200))) g.FillEllipse(water, c.X - 46 * sf, c.Y - 7 * sf, 92 * sf, 15 * sf);
   using (var shine = new SolidBrush(Color.FromArgb(90, 200, 230, 255))) g.FillEllipse(shine, c.X - 30 * sf, c.Y - 5 * sf, 26 * sf, 4 * sf);
   g.SmoothingMode = mode;
  }
  PointF Bobber { get { var c = PondCenter; return new PointF(c.X + 8 * Sf, c.Y - (Biting ? -2 : 2) * Sf + (float)Math.Sin(MotionTime.Now / 1000.0 * 2.4) * Sf); } }
  void PaintFishing(Graphics g) {
   if (desk.Head.IsEmpty) return;
   float sf = Sf, px = DeskPx; var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   PointF hand = new PointF(desk.Head.X - 7.5f * px, desk.Head.Y + 2.5f * px), tip = new PointF(hand.X - 26 * sf, hand.Y - 22 * sf), bob = Bobber;
   using (var rod = new Pen(Color.FromArgb(130, 90, 56), 2.2f * sf)) g.DrawLine(rod, hand, tip);
   using (var line = new Pen(Color.FromArgb(160, 230, 230, 236), 1f)) g.DrawBezier(line, tip, new PointF(tip.X - 6 * sf, tip.Y + 20 * sf), new PointF(bob.X + 6 * sf, bob.Y - 20 * sf), bob);
   using (var red = new SolidBrush(Color.FromArgb(230, 70, 70))) g.FillEllipse(red, bob.X - 3 * sf, bob.Y - 3 * sf, 6 * sf, 4 * sf);
   using (var white = new SolidBrush(Color.FromArgb(240, 240, 244))) g.FillEllipse(white, bob.X - 3 * sf, bob.Y, 6 * sf, 3 * sf);
   if (Biting) {
    double k = ((DateTime.Now - biteAt).TotalSeconds * 2) % 1.0; float rw = (float)(6 + k * 18) * sf;
    using (var ring = new Pen(Color.FromArgb((int)(200 * (1 - k)), 220, 240, 255), 1.2f * sf)) g.DrawEllipse(ring, bob.X - rw / 2, bob.Y - rw * .15f, rw, rw * .3f);
    using (var b = new SolidBrush(Color.FromArgb(250, 220, 90))) g.DrawString("!", Theme.F(Theme.SansBold, 9f), b, bob.X - 3 * sf, bob.Y - 22 * sf);
   }
   g.SmoothingMode = mode;
  }
  // The caught fish flies from the pond to the bowl.
  void PaintFishJump(Graphics g) {
   double k = (DateTime.Now - fishCaughtAt).TotalSeconds / 1.1; if (k < 0 || k >= 1) return;
   PointF a = PondCenter, b = BowlSpot; float x = a.X + (b.X - a.X) * (float)k, y = a.Y + (b.Y - a.Y) * (float)k - (float)Math.Sin(k * Math.PI) * 50 * Sf;
   PaintFish(g, x, y, 1.6f * Sf, Color.FromArgb(250, 150, 70), k > .5);
  }
  void PaintFish(Graphics g, float x, float y, float s, Color c, bool left) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; float d = left ? -1 : 1;
   using (var b = new SolidBrush(c)) { g.FillEllipse(b, x - 4 * s, y - 2.2f * s, 8 * s, 4.4f * s); g.FillPolygon(b, new[] { new PointF(x - d * 3.5f * s, y), new PointF(x - d * 7 * s, y - 2.5f * s), new PointF(x - d * 7 * s, y + 2.5f * s) }); }
   using (var eye = new SolidBrush(Color.FromArgb(30, 30, 34))) g.FillEllipse(eye, x + d * 2 * s - .6f * s, y - 1 * s, 1.2f * s, 1.2f * s);
   g.SmoothingMode = mode;
  }
  // The fish bowl in the bottom left corner, with the fish caught so far swimming.
  PointF BowlSpot { get { return new PointF(26 * Sf, DeskGround + 22 * Sf); } }
  void PaintBowl(Graphics g) {
   if (settings.Fish <= 0) return;
   float sf = Sf; var c = BowlSpot; double t = MotionTime.Now / 1000.0;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   var bowl = new RectangleF(c.X - 18 * sf, c.Y - 16 * sf, 36 * sf, 30 * sf);
   using (var water = new SolidBrush(Color.FromArgb(150, 90, 160, 230))) g.FillEllipse(water, bowl.X + 2 * sf, bowl.Y + 6 * sf, bowl.Width - 4 * sf, bowl.Height - 7 * sf);
   var tints = new[] { Color.FromArgb(250, 150, 70), Color.FromArgb(250, 210, 80), Color.FromArgb(236, 110, 140), Color.FromArgb(120, 200, 255), Color.FromArgb(150, 230, 140) };
   for (int i = 0; i < Math.Min(5, settings.Fish); i++) {
    double ph = t * (.5 + i * .13) + i * 1.7; float fx = c.X + (float)Math.Sin(ph) * 9 * sf, fy = c.Y - 2 * sf + (i - 2) * 3 * sf + (float)Math.Sin(ph * 1.7) * sf;
    PaintFish(g, fx, fy, .9f * sf, tints[i], Math.Cos(ph) < 0);
   }
   using (var glass = new Pen(Color.FromArgb(150, 220, 236, 255), 1.4f * sf)) g.DrawEllipse(glass, bowl);
   using (var shine = new Pen(Color.FromArgb(120, 255, 255, 255), 1.4f * sf)) g.DrawArc(shine, bowl.X + 4 * sf, bowl.Y + 4 * sf, bowl.Width - 8 * sf, bowl.Height - 8 * sf, 200, 50);
   if (settings.Fish > 5) using (var b = new SolidBrush(Color.FromArgb(220, 230, 236, 244))) g.DrawString(settings.Fish.ToString(CultureInfo.InvariantCulture), Theme.F(Theme.SansBold, 7f), b, bowl.Right - 4 * sf, bowl.Y - 2 * sf);
   g.SmoothingMode = mode;
  }

  // ---- rock, paper, scissors ----
  string rpsPhase = "choose"; int rpsYou = -1, rpsPet = -1, rpsScoreYou, rpsScorePet; DateTime rpsAt = DateTime.MinValue;
  static readonly string[] RpsGlyph = { "✊", "✋", "✌" };
  PointF RpsSpot(int i) { return new PointF((112 + i * 48) * Sf, 26 * Sf); }
  void RpsTick() {
   if (rpsPhase == "choose") desk.Act = "rps";
   else if ((DateTime.Now - rpsAt).TotalSeconds > 2.6) { rpsPhase = "choose"; desk.Act = "rps"; }
  }
  bool RpsClick(Point at) {
   if (rpsPhase != "choose") return false;
   for (int i = 0; i < 3; i++) {
    var c = RpsSpot(i); if (Math.Abs(at.X - c.X) > 16 * Sf || Math.Abs(at.Y - c.Y) > 16 * Sf) continue;
    rpsYou = i; rpsPet = talk.Next(3); rpsPhase = "reveal"; rpsAt = DateTime.Now;
    int r = (rpsYou - rpsPet + 3) % 3; // 0 tie, 1 you win, 2 it wins
    if (r == 0) { desk.Act = ""; Say("Empate!", true); }
    else if (r == 1) { rpsScoreYou++; desk.Act = "pout"; Say(Pick("Ah, você ganhou...", "Hmpf! Ganhou."), true); }
    else { rpsScorePet++; desk.Act = ""; hopAt = DateTime.Now; Say(Pick("Ganhei!", "Rá! Ganhei!"), true); }
    return true;
   }
   return false;
  }
  void PaintRps(Graphics g) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; var font = new Font("Segoe UI Symbol", 13f);
   try {
    for (int i = 0; i < 3; i++) {
     var c = RpsSpot(i); bool mine = rpsPhase == "reveal" && i == rpsYou; if (rpsPhase == "reveal" && !mine) continue;
     float r = 14 * Sf;
     using (var fill = new SolidBrush(Color.FromArgb(mine ? 240 : 225, mine ? Color.FromArgb(120, 200, 140) : Color.FromArgb(40, 42, 50)))) g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
     using (var edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawEllipse(edge, c.X - r, c.Y - r, r * 2, r * 2);
     using (var b = new SolidBrush(Color.FromArgb(240, 244, 246, 250))) using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(RpsGlyph[i], font, b, new RectangleF(c.X - r, c.Y - r, r * 2, r * 2), f);
    }
    // Its own pick, over its head.
    if (rpsPhase == "reveal" && !desk.Head.IsEmpty) {
     var c = new PointF(desk.Head.X, desk.Crown - 18 * Sf); float r = 14 * Sf;
     using (var fill = new SolidBrush(Color.FromArgb(235, 236, 150, 110))) g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
     using (var b = new SolidBrush(Color.FromArgb(250, 255, 255, 255))) using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(RpsGlyph[rpsPet], font, b, new RectangleF(c.X - r, c.Y - r, r * 2, r * 2), f);
    }
   } finally { font.Dispose(); }
   if (rpsScoreYou + rpsScorePet > 0) DrawScore(g, "você " + rpsScoreYou + " × " + rpsScorePet + " eu");
   g.SmoothingMode = mode;
  }

  // ---- rhythm: circles pop up on the beat; click when the ring closes ----
  sealed class Beat { public PointF P; public DateTime Born; }
  readonly List<Beat> beats = new List<Beat>(); DateTime nextBeat = DateTime.MinValue; int rhythmScore;
  readonly List<KeyValuePair<PointF,string>> beatLabels = new List<KeyValuePair<PointF,string>>(); DateTime beatLabelAt = DateTime.MinValue;
  void RhythmTick() {
   if (!MusicOn && forcedGame.Length == 0) { StartGame(NextGame()); return; }
   if (DateTime.Now >= nextBeat) {
    float x = (40 + talk.Next(240)) * Sf, y = (24 + talk.Next(70)) * Sf;
    if (Math.Abs(x - DeskW / 2f) < 40 * Sf) x += 80 * Sf * (x < DeskW / 2f ? -1 : 1);
    beats.Add(new Beat { P = new PointF(x, y), Born = DateTime.Now }); nextBeat = DateTime.Now.AddSeconds(.95);
   }
   beats.RemoveAll(b => (DateTime.Now - b.Born).TotalSeconds > 1.25);
   desk.Act = "groove";
  }
  bool RhythmClick(Point at) {
   foreach (var b in beats) {
    float dx = at.X - b.P.X, dy = at.Y - b.P.Y; if (dx * dx + dy * dy > 16 * 16 * Sf * Sf) continue;
    double off = Math.Abs((DateTime.Now - b.Born).TotalSeconds - .9);
    string label = off < .15 ? "Perfeito!" : off < .3 ? "Boa!" : "Quase";
    rhythmScore += off < .15 ? 2 : off < .3 ? 1 : 0; if (off < .3) hopAt = DateTime.Now;
    beatLabels.Clear(); beatLabels.Add(new KeyValuePair<PointF,string>(b.P, label)); beatLabelAt = DateTime.Now;
    beats.Remove(b); return true;
   }
   return false;
  }
  void PaintRhythm(Graphics g) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; float r = 9 * Sf;
   foreach (var b in beats) {
    double e = (DateTime.Now - b.Born).TotalSeconds; float ring = r + (float)Math.Max(0, (.9 - e) / .9) * 22 * Sf; int a = e > .9 ? (int)(255 * Math.Max(0, 1 - (e - .9) / .35)) : 255;
    using (var fill = new SolidBrush(Color.FromArgb(a * 3 / 4, accent))) g.FillEllipse(fill, b.P.X - r, b.P.Y - r, r * 2, r * 2);
    using (var pen = new Pen(Color.FromArgb(a, 255, 240, 200), 1.6f * Sf)) g.DrawEllipse(pen, b.P.X - ring, b.P.Y - ring, ring * 2, ring * 2);
    using (var note = new SolidBrush(Color.FromArgb(a, 255, 255, 255))) using (var font = new Font("Segoe UI Symbol", 9f)) using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("♪", font, note, new RectangleF(b.P.X - r, b.P.Y - r, r * 2, r * 2), f);
   }
   double k = (DateTime.Now - beatLabelAt).TotalSeconds / .7;
   if (k >= 0 && k < 1) foreach (var l in beatLabels) using (var b = new SolidBrush(Color.FromArgb((int)(255 * (1 - k)), 255, 230, 140))) g.DrawString(l.Value, Theme.F(Theme.SansBold, 8.5f), b, l.Key.X - 18 * Sf, l.Key.Y - 22 * Sf - (float)k * 8 * Sf);
   if (rhythmScore > 0) DrawScore(g, "ritmo: " + rhythmScore);
   g.SmoothingMode = mode;
  }

  // ---- guess how long Claude takes: you and it each bet on a range; settled when the turn ends ----
  int guessYou = -1, guessPet = -1, guessScoreYou, guessScorePet;
  static readonly string[] GuessLabels = { "< 20 s", "20 s – 1 min", "> 1 min" };
  PointF GuessSpot(int i) { return new PointF((70 + i * 90) * Sf, 22 * Sf); }
  void GuessStart() { guessYou = -1; guessPet = talk.Next(3); Say("Aposto que vai levar " + GuessLabels[guessPet] + "! E você?", true); }
  bool GuessClick(Point at) {
   if (guessYou >= 0) return false;
   for (int i = 0; i < 3; i++) { var c = GuessSpot(i); if (Math.Abs(at.X - c.X) > 40 * Sf || Math.Abs(at.Y - c.Y) > 12 * Sf) continue; guessYou = i; Say(i == guessPet ? "Mesma aposta!" : "Valendo!", true); return true; }
   return false;
  }
  void GuessResolve() {
   double took = (DateTime.Now - turnStart).TotalSeconds; int real = took < 20 ? 0 : took < 60 ? 1 : 2;
   bool you = guessYou == real, pet = guessPet == real; if (you) guessScoreYou++; if (pet) guessScorePet++;
   string time = took < 60 ? Math.Round(took) + " s" : WorkDay.Duration(took / 3600.0);
   Say("Levou " + time + "! " + (you && pet ? "Os dois acertaram!" : you ? "Você acertou!" : pet ? "Eu acertei!" : guessYou < 0 ? "Errei..." : "Ninguém acertou!"), true);
  }
  void PaintGuess(Graphics g) {
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; var font = Theme.F(Theme.SansBold, 7.5f);
   for (int i = 0; i < 3; i++) {
    var c = GuessSpot(i); bool you = i == guessYou, pet = i == guessPet; if (guessYou >= 0 && !you && !pet) continue;
    var box = new RectangleF(c.X - 38 * Sf, c.Y - 10 * Sf, 76 * Sf, 20 * Sf);
    using (var path = Rounded(box, 10 * Sf)) {
     using (var fill = new SolidBrush(you ? Color.FromArgb(235, 120, 200, 140) : pet ? Color.FromArgb(235, 236, 150, 110) : Color.FromArgb(225, 40, 42, 50))) g.FillPath(fill, path);
     using (var edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawPath(edge, path);
    }
    string text = GuessLabels[i] + (you && pet ? " ★" : you ? " (você)" : pet ? " (eu)" : "");
    using (var b = new SolidBrush(Color.FromArgb(245, 250, 250, 252))) using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(text, font, b, box, f);
   }
   // A stopwatch with the time so far.
   double took = (DateTime.Now - turnStart).TotalSeconds;
   using (var b = new SolidBrush(Color.FromArgb(230, 236, 238, 242))) g.DrawString("⏱ " + (took < 60 ? Math.Floor(took) + " s" : Math.Floor(took / 60) + " min " + Math.Floor(took % 60) + " s"), Theme.F(Theme.SansBold, 8f), b, DeskW / 2f - 22 * Sf, 38 * Sf);
   if (guessScoreYou + guessScorePet > 0) DrawScore(g, "você " + guessScoreYou + " × " + guessScorePet + " eu");
   g.SmoothingMode = mode;
  }

  // ---- the plant: grows one step for each day you water it; thirsty again the next day ----
  string Today { get { return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }
  bool PlantThirsty { get { return settings.PlantWatered != Today; } }
  DateTime wateredAt = DateTime.MinValue;
  PointF PlantSpot { get { return new PointF(DeskW - 66 * Sf, DeskGround); } }
  void WaterTick() { desk.Act = "point"; desk.LookTarget = PlantSpot; if (!PlantThirsty && (DateTime.Now - wateredAt).TotalSeconds > 2) StartGame(NextGame()); }
  bool PlantClick(Point at) {
   if (game == "hide") return false;
   var c = PlantSpot; if (Math.Abs(at.X - c.X) > 12 * Sf || at.Y < c.Y - 40 * Sf || at.Y > c.Y + 2 * Sf) return false;
   if (PlantThirsty) { settings.PlantWatered = Today; settings.PlantDays++; TrySave(); wateredAt = DateTime.Now; Say("Obrigado! Ela vai crescer.", true); }
   else Say(Pick("Já reguei hoje!", "Ela tá feliz!"));
   return true;
  }
  void PaintPlantPot(Graphics g) {
   if (game == "hide") return;
   var c = PlantSpot; float px = Math.Max(1f, 3f * Sf); bool thirsty = PlantThirsty; int days = settings.PlantDays;
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
   var p = new Pix(g, c.X - 3 * px, c.Y - 3 * px, px);
   try {
    Color pot = Color.FromArgb(196, 110, 74), potDark = Color.FromArgb(150, 80, 56), soil = Color.FromArgb(70, 50, 40);
    Color leaf = thirsty ? Color.FromArgb(150, 170, 90) : Color.FromArgb(104, 196, 118), stem = thirsty ? Color.FromArgb(130, 150, 80) : Color.FromArgb(86, 160, 96);
    p.Row(0, 0, 5, pot); p.Row(1, 1, 4, potDark); p.Row(2, 1, 4, potDark); p.Row(0, 1, 4, soil);
    int height = days <= 0 ? 1 : days <= 3 ? 2 : days <= 9 ? 4 : 6; float droop = thirsty ? .5f : 0, sway = Math.Sin(t * 1.6) > .5 ? .5f : 0;
    for (int r = 1; r <= height; r++) p.Dot(2.5f + (r == height ? sway + droop : 0), -r, stem);
    p.Dot(1.5f, -height + droop, leaf); p.Dot(3.5f + droop, -height - 1 + droop * 2, leaf);
    if (height >= 4) { p.Dot(.5f, -2 + droop, leaf); p.Dot(4.5f, -3 + droop, leaf); }
    if (height >= 6) { Color petal = Color.FromArgb(246, 150, 186), center = Color.FromArgb(252, 214, 96); p.Dot(1.5f, -8, petal); p.Dot(3.5f, -8, petal); p.Dot(2.5f, -9, petal); p.Dot(2.5f, -7, petal); p.Dot(2.5f, -8, center); }
   } finally { p.Dispose(); }
   g.SmoothingMode = SmoothingMode.AntiAlias;
   // A drop over it while it is thirsty; a watering can pouring right after you water it.
   if (thirsty && settings.Animations) {
    float y = c.Y - 34 * Sf + (float)Math.Sin(t * 3) * 2 * Sf;
    using (var drop = Drop(c.X, y + 9 * Sf, 4 * Sf, 9 * Sf)) using (var b = new SolidBrush(Color.FromArgb(220, 110, 180, 245))) g.FillPath(b, drop);
   }
   double w = (DateTime.Now - wateredAt).TotalSeconds / 1.6;
   if (w >= 0 && w < 1) {
    float cx = c.X + 10 * Sf, cy = c.Y - 40 * Sf;
    using (var can = new SolidBrush(Color.FromArgb(120, 170, 220))) { g.FillRectangle(can, cx, cy, 14 * Sf, 9 * Sf); g.FillPolygon(can, new[] { new PointF(cx, cy + 2 * Sf), new PointF(cx - 9 * Sf, cy - 3 * Sf), new PointF(cx - 9 * Sf, cy), new PointF(cx, cy + 5 * Sf) }); }
    using (var water = new SolidBrush(Color.FromArgb(200, 140, 200, 250)))
     for (int i = 0; i < 4; i++) { double k = (w * 3 + i * .25) % 1.0; g.FillEllipse(water, cx - 10 * Sf + i * 2 * Sf, cy + (float)k * 30 * Sf, 2 * Sf, 3 * Sf); }
   }
   g.SmoothingMode = mode;
  }

  // Score of the current game, small at the top left.
  void DrawScore(Graphics g, string text) {
   var font = Theme.F(Theme.SansBold, 8f); SizeF size = g.MeasureString(text, font);
   var box = new RectangleF(6 * Sf, 4 * Sf, size.Width + 10 * Sf, size.Height + 2 * Sf);
   using (var path = Rounded(box, box.Height / 2)) using (var fill = new SolidBrush(Color.FromArgb(215, 30, 30, 34))) g.FillPath(fill, path);
   using (var b = new SolidBrush(Color.FromArgb(240, 250, 214, 120))) g.DrawString(text, font, b, box.X + 5 * Sf, box.Y + 1 * Sf);
  }
 }
}
