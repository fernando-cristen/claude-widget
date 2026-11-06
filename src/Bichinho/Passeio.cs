namespace MeuClaudeWidget {
 using System;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Drawing.Imaging;
 using System.Drawing.Text;
 using System.Linq;
 using System.Runtime.InteropServices;
 using System.Text;
 using System.Windows.Forms;

 // Walks on the desktop: now and then the creature goes to the window you are using and sits on its top edge,
 // legs dangling (riding along if you move the window); when a session needs you it goes to that terminal and knocks.
 // A separate floating window carries it; the home spot shows "volto já!" meanwhile. It can be picked up and dropped.
 public partial class Widget {
  DeskPet roamPet; Bitmap roamFrame; readonly Stage roam = new Stage { Zoom = 1f }; // out walking it is the size of the taskbar one
  string roamState = "home"; // walkOut, jumpUp, perch, jumpDown, land, walkBack, held, fall
  PointF roamFeet, roamA, roamB; double roamT0, roamDur; IntPtr roamWin = IntPtr.Zero; bool roamCall, roamEdge; float roamOffset;
  DateTime roamUntil, roamHappyAt = DateTime.MinValue, nextVisit = DateTime.Now.AddSeconds(90); string callKey = "", callSession = "";
  readonly Random wander = new Random();

  bool Away { get { return roamState != "home"; } }
  int RoamW { get { return (int)Math.Round(200 * ScaleFactor); } }
  int RoamH { get { return (int)Math.Round(96 * ScaleFactor); } }
  float RoamFeetY { get { return RoamH - 14 * ScaleFactor; } }
  float RoamPx { get { return ScaleFactor * roam.Zoom * 2.45f; } }
  PointF HomeFeet { get { return new PointF(deskPet.Left + DeskW / 2f, deskPet.Top + DeskGround); } }
  static double Clock { get { return MotionTime.Now / 1000.0; } }

  void RoamTick() {
   if (deskPet == null) return;
   if (!Away) { MaybeRoam(); return; }
   double k = roamDur <= 0 ? 1 : Math.Min(1, (Clock - roamT0) / roamDur);
   roam.Walking = false; roam.Pose = "";
   switch (roamState) {
    case "walkOut": case "walkBack":
     roam.Walking = true; roamFeet = Lerp(roamA, roamB, k);
     if (k >= 1) { if (roamState == "walkOut") Jump(roamA = roamFeet, Target(), "jumpUp"); else EndRoam(); }
     break;
    case "jumpUp": case "jumpDown":
     roam.Pose = "jump";
     roamFeet = Lerp(roamA, roamB, k); roamFeet.Y -= (float)Math.Sin(k * Math.PI) * (26 * ScaleFactor + Math.Max(0, roamA.Y - roamB.Y) * .2f);
     if (roamState == "jumpUp" && !TargetAlive()) { GoDown(); break; }
     if (k >= 1) {
      if (roamState == "jumpUp") { roamState = "perch"; roamUntil = DateTime.Now.AddSeconds(roamCall ? 300 : 40 + wander.Next(40)); Say(roamCall ? "Psiu! Tem sessão te esperando." : Pick("Posso sentar aqui?", "Que vista!", "Vim te visitar!"), true); }
      else { roamState = "land"; roam.LandAt = DateTime.Now; roamT0 = Clock; roamDur = 0.9; }
     }
     break;
    case "perch":
     if (!TargetAlive()) { GoDown(); break; }
     roamFeet = Target();
     roam.Pose = roamCall ? "knock" : roamEdge ? "sit" : "";
     if (roamCall) {
      var s = sessions.FirstOrDefault(x => x.Id == callSession);
      bool answered = s == null || s.State != "waiting" || s.Closed || GetForegroundWindow() == roamWin;
      if (answered && roamHappyAt == DateTime.MinValue) { roamHappyAt = DateTime.Now; roam.Pose = ""; hopAt = DateTime.Now; Say("Valeu!", true); }
      if (roamHappyAt != DateTime.MinValue) roam.Pose = roamEdge ? "sit" : "";
      if (roamHappyAt != DateTime.MinValue && (DateTime.Now - roamHappyAt).TotalSeconds > 1.4) GoDown();
     }
     if (DateTime.Now > roamUntil || NightIdle) GoDown();
     break;
    case "land":
     if (k >= 1) GoDown(); // a hop up or down to the height of home first, then the walk (never walking through the air)
     break;
    case "held":
     roamFeet = new PointF(roamPet.Left + RoamW / 2f, roamPet.Top + RoamFeetY);
     break;
    case "fall":
     roamFeet = new PointF(roamA.X, roamA.Y + (roamB.Y - roamA.Y) * (float)(k * k));
     if (k >= 1) { roamState = "land"; roam.LandAt = DateTime.Now; roamT0 = Clock; roamDur = 0.9; }
     break;
   }
   if (roamPet != null && roamState != "held") roamPet.Location = new Point((int)Math.Round(roamFeet.X - RoamW / 2f), (int)Math.Round(roamFeet.Y - RoamFeetY));
  }

  // Decides whether to go out: calling you to a waiting session first, otherwise a visit now and then.
  void MaybeRoam() {
   if (!settings.Walks || !settings.Animations || desk.Held || NightIdle || preview) return;
   var waiting = sessions.FirstOrDefault(x => !x.Closed && !x.Silent && x.State == "waiting" && x.WindowPid > 0 && (DateTimeOffset.UtcNow - x.Time).TotalSeconds >= 12);
   if (waiting != null) {
    string key = waiting.Id + waiting.Updated;
    if (key != callKey) {
     callKey = key; IntPtr win = SessionWindow(waiting.WindowPid);
     if (win != IntPtr.Zero && GetForegroundWindow() != win && StartRoam(win, true)) { callSession = waiting.Id; return; }
    }
   }
   if (DateTime.Now < nextVisit) return;
   if (mood != "idle" && mood != "work" && mood != "happy") { nextVisit = DateTime.Now.AddSeconds(30); return; }
   // No window to visit right now: try again soon instead of waiting for the next round.
   nextVisit = StartVisit() ? DateTime.Now.AddMinutes(4 + wander.Next(5)) : DateTime.Now.AddSeconds(30);
  }
  // The window you are using, or else the top one on the screen.
  bool StartVisit() {
   IntPtr fg = GetForegroundWindow();
   if (fg != IntPtr.Zero && IsVisitable(fg) && StartRoam(fg, false)) return true;
   IntPtr found = IntPtr.Zero;
   EnumWindows((h, l) => { if (IsVisitable(h)) { found = h; return false; } return true; }, IntPtr.Zero);
   return found != IntPtr.Zero && StartRoam(found, false);
  }
  // Menu › Testar › Passeio: goes out right away.
  void WalkNow() {
   if (deskPet == null) { Say("Ligue o bichinho na área de trabalho primeiro."); return; }
   if (Away) return;
   if (!StartVisit()) Say("Não achei janela pra visitar.", true);
  }
  bool StartRoam(IntPtr win, bool call) {
   RectangleF r; if (!WindowBox(win, out r) || r.Width < 320 * ScaleFactor) return false;
   roamWin = win; roamCall = call; roamHappyAt = DateTime.MinValue;
   float lo = Math.Min(r.Width / 2, 110 * ScaleFactor), hi = Math.Max(lo, r.Width - 240 * ScaleFactor);
   roamOffset = lo + (float)wander.NextDouble() * (hi - lo);
   PointF target = Target();
   if (roamPet == null) {
    roamPet = new DeskPet(true) { Size = new Size(RoamW, RoamH) };
    roamPet.Poke = p => { current = roam; try { PokeCreature(p); } finally { current = null; } };
    roamPet.OpenMenu = p => menu.Show(p);
    roamPet.DragStart = delegate { roamState = "held"; roam.Held = true; dragLast = roamPet.Location; };
    roamPet.Dragging = delegate { int dx = roamPet.Left - dragLast.X; dragLast = roamPet.Location; roam.Swing = Math.Max(-3f, Math.Min(3f, roam.Swing * .6f - dx / (6f * ScaleFactor))); };
    roamPet.DragEnd = delegate { roam.Held = false; roam.Swing = 0; Drop(); };
   }
   roamFeet = HomeFeet; roamPet.Location = new Point((int)(roamFeet.X - RoamW / 2f), (int)(roamFeet.Y - RoamFeetY));
   if (!roamPet.Visible) roamPet.Show();
   Walk(HomeFeet, new PointF(target.X, HomeFeet.Y), "walkOut");
   return true;
  }
  void Walk(PointF from, PointF to, string state) {
   roamState = state; roamA = from; roamB = to; roamT0 = Clock;
   roamDur = Math.Max(0.35, Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y)) / (120 * ScaleFactor));
  }
  void Jump(PointF from, PointF to, string state) { roamState = state; roamA = from; roamB = to; roamT0 = Clock; roamDur = 0.75; }
  // Back down to the floor of the home spot (a hop off the edge), then the walk home.
  void GoDown() {
   PointF home = HomeFeet;
   if (Math.Abs(roamFeet.Y - home.Y) < 4) { Walk(roamFeet, home, "walkBack"); return; }
   Jump(roamFeet, new PointF(roamFeet.X, home.Y), "jumpDown");
  }
  // Let go with the mouse: falls down to the floor (if it is above it), bounces and walks home.
  void Drop() {
   roamFeet = new PointF(roamPet.Left + RoamW / 2f, roamPet.Top + RoamFeetY);
   PointF home = HomeFeet;
   if (roamFeet.Y < home.Y - 4) { roamState = "fall"; roamA = roamFeet; roamB = new PointF(roamFeet.X, home.Y); roamT0 = Clock; roamDur = Math.Max(0.25, Math.Sqrt((home.Y - roamFeet.Y) / (1400 * ScaleFactor))); }
   else { roamState = "land"; roam.LandAt = DateTime.Now; roamT0 = Clock; roamDur = 0.9; }
  }
  void EndRoam() {
   roamState = "home"; roamWin = IntPtr.Zero; roam.Pose = ""; roam.Walking = false; roam.Held = false;
   if (roamPet != null && roamPet.Visible) roamPet.Hide();
  }

  // Where it sits: on the top edge of the window if there is room above it; for maximized or snapped windows
  // (no room above), on the edge of the taskbar right under the window. Either way, legs dangling.
  PointF Target() {
   RectangleF r; if (!WindowBox(roamWin, out r)) return roamFeet;
   var area = Screen.FromHandle(roamWin).WorkingArea; float px = RoamPx;
   roamEdge = true;
   float x = Math.Max(r.Left + 40 * ScaleFactor, Math.Min(r.Right - 60 * ScaleFactor, r.Left + roamOffset));
   x = Math.Max(area.Left + 50 * ScaleFactor, Math.Min(area.Right - 50 * ScaleFactor, x));
   bool room = r.Top - area.Top >= 15 * px;
   return new PointF(x, (room ? r.Top : area.Bottom) + 2 * px);
  }
  bool TargetAlive() { return roamWin != IntPtr.Zero && IsWindow(roamWin) && IsWindowVisible(roamWin) && !IsIconic(roamWin) && !Cloaked(roamWin); }

  void PaintRoamFrame() {
   if (roamPet == null || !roamPet.Visible) return;
   if (roamFrame == null || roamFrame.Width != RoamW || roamFrame.Height != RoamH) { if (roamFrame != null) roamFrame.Dispose(); roamFrame = new Bitmap(RoamW, RoamH, PixelFormat.Format32bppArgb); roamPet.Size = roamFrame.Size; }
   roam.Swing *= .85f;
   using (var g = Graphics.FromImage(roamFrame)) {
    g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    current = roam;
    try {
     float px = RoamPx;
     roam.W = RoamW; roam.H = RoamH; roam.Ground = RoamFeetY; roam.Surface = roamPet; roam.Hover = roamPet.ClientRectangle.Contains(roamPet.PointToClient(Cursor.Position));
     // Sitting: the body rests on the edge, so the drawing goes 2 rows lower (legs hang over the window).
     int top = (int)Math.Round(RoamFeetY - (float)Math.Round(9.5f * ScaleFactor * roam.Zoom) - 10 * px);
     PaintCreature(g, (int)Math.Round(RoamW / 2f - 7 * px), top);
     PaintSpeech(g, roam.Head, roam.Crown, RoamW);
    } finally { current = null; }
   }
   roamPet.Present(roamFrame);
  }

  // ---- windows ----
  bool IsVisitable(IntPtr hwnd) {
   if (!TargetAlive2(hwnd)) return false;
   uint pid; GetWindowThreadProcessId(hwnd, out pid); if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;
   if (GetWindowTextLength(hwnd) == 0 || (GetWindowLong(hwnd, -20) & 0x80) != 0) return false; // untitled or tool windows (menus, popups)
   var name = new StringBuilder(64); GetClassName(hwnd, name, 64); string cls = name.ToString();
   if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow") return false;
   RectangleF r; if (!WindowBox(hwnd, out r)) return false;
   var bounds = Screen.FromHandle(hwnd).Bounds;
   return !(r.Width >= bounds.Width && r.Height >= bounds.Height); // not fullscreen (games, videos)
  }
  static bool TargetAlive2(IntPtr hwnd) { return hwnd != IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd) && !IsIconic(hwnd) && !Cloaked(hwnd); }
  static IntPtr SessionWindow(int pid) {
   try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) return p.MainWindowHandle; } catch { return IntPtr.Zero; }
  }
  // The visible frame of a window (without the invisible resize borders of Windows 10/11).
  static bool WindowBox(IntPtr hwnd, out RectangleF box) {
   box = RectangleF.Empty; if (hwnd == IntPtr.Zero) return false;
   RECT r;
   if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(hwnd, out r)) return false;
   if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return false;
   box = RectangleF.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); return true;
  }
  static bool Cloaked(IntPtr hwnd) { int v; return DwmGetWindowAttribute(hwnd, 14, out v, 4) == 0 && v != 0; }
  static PointF Lerp(PointF a, PointF b, double k) { return new PointF(a.X + (b.X - a.X) * (float)k, a.Y + (b.Y - a.Y) * (float)k); }

  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
  delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
  [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
 }
}
