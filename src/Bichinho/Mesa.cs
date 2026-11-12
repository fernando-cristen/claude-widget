namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Drawing.Imaging;
 using System.Drawing.Text;
 using System.Linq;
 using System.Runtime.InteropServices;
 using System.Windows.Forms;

 // A see-through window for the desktop creature (per-pixel alpha, like the old Windows 7 gadgets).
 // The home one stays below every window; the walker floats above them while it goes for a walk.
 sealed class DeskPet : Form {
  const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
  const int WM_WINDOWPOSCHANGING = 0x46, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3, SWP_NOZORDER = 0x4, GWL_HWNDPARENT = -8;
  static readonly IntPtr HWND_BOTTOM = new IntPtr(1), HWND_TOPMOST = new IntPtr(-1);
  public Action<Point> Poke, OpenMenu, Chat; public Action DragStart, Dragging, DragEnd;
  public bool Floating { get; private set; }
  // Brings the window over the others (games, visits) or sends it back behind them.
  public void SetFloating(bool on) {
   if (on == Floating) return; Floating = on;
   if (IsHandleCreated) SetWindowPos(Handle, on ? HWND_TOPMOST : HWND_BOTTOM, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
  }
  Point grab; bool down, moved;

  public DeskPet(bool floating) { Floating = floating; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Text = "Meu Claude · bichinho"; }
  public bool Moving { get { return down && moved; } }
  protected override bool ShowWithoutActivation { get { return true; } }
  protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE; return cp; } }
  protected override void OnHandleCreated(EventArgs e) {
   base.OnHandleCreated(e);
   // The home one is owned by the desktop window, so it lives with the desktop instead of with the apps.
   if (!Floating) { IntPtr desktop = FindWindow("Progman", null); if (desktop != IntPtr.Zero) SetWindowLongPtr(Handle, GWL_HWNDPARENT, desktop); }
  }
  protected override void WndProc(ref Message m) {
   if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
   if (m.Msg == WM_WINDOWPOSCHANGING) {
    var pos = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
    pos.hwndInsertAfter = Floating ? HWND_TOPMOST : HWND_BOTTOM; pos.flags &= ~SWP_NOZORDER;
    Marshal.StructureToPtr(pos, m.LParam, false);
   }
   base.WndProc(ref m);
  }
  protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; moved = false; grab = e.Location; } }
  protected override void OnMouseMove(MouseEventArgs e) {
   base.OnMouseMove(e); if (!down) return;
   if (!moved) { if (Math.Abs(e.X - grab.X) + Math.Abs(e.Y - grab.Y) < 4) return; moved = true; if (DragStart != null) DragStart(); }
   Location = new Point(Cursor.Position.X - grab.X, Cursor.Position.Y - grab.Y);
   if (Dragging != null) Dragging();
  }
  protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if (e.Button == MouseButtons.Left && Chat != null) Chat(e.Location); }
  protected override void OnMouseUp(MouseEventArgs e) {
   base.OnMouseUp(e);
   if (e.Button == MouseButtons.Right) { if (OpenMenu != null) OpenMenu(Cursor.Position); return; }
   if (e.Button != MouseButtons.Left || !down) return;
   down = false;
   if (moved) { moved = false; if (DragEnd != null) DragEnd(); } else if (Poke != null) Poke(e.Location);
  }

  // Shows a 32-bit ARGB frame with its own transparency.
  public void Present(Bitmap frame) {
   if (!IsHandleCreated || IsDisposed) return;
   IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen), bitmap = IntPtr.Zero, old = IntPtr.Zero;
   try {
    bitmap = frame.GetHbitmap(Color.FromArgb(0)); old = SelectObject(memory, bitmap);
    var size = new SIZE { cx = frame.Width, cy = frame.Height }; var source = new POINT(); var top = new POINT { x = Left, y = Top };
    var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
    UpdateLayeredWindow(Handle, screen, ref top, ref size, memory, ref source, 0, ref blend, 2);
   } finally {
    ReleaseDC(IntPtr.Zero, screen);
    if (bitmap != IntPtr.Zero) { SelectObject(memory, old); DeleteObject(bitmap); }
    DeleteDC(memory);
   }
  }
  // Whether the point (screen) shows this window, i.e. it is not hidden behind another one.
  public bool VisibleAt(Point p) { return WindowFromPoint(p) == Handle; }

  [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy, flags; }
  [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
  [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
  [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
  [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, int flags);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
  [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);
  [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern IntPtr SetWindowLong32(IntPtr hWnd, int index, IntPtr value);
  static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value) { return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, index, value) : SetWindowLong32(hWnd, index, value); }
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
 }

 public partial class Widget {
  DeskPet deskPet; Timer deskTimer; Bitmap deskFrame;
  Point dragLast; DateTime deskNightAt = DateTime.MinValue; bool deskWasNight; PointF captionSpot;
  int DeskW { get { return (int)Math.Round(320 * ScaleFactor); } }
  int DeskH { get { return (int)Math.Round(250 * ScaleFactor); } }
  float DeskPx { get { return ScaleFactor * desk.Zoom * 2.45f; } }
  int DeskTop { get { return (int)Math.Round(80 * ScaleFactor); } } // room above the head for a wrapped speech bubble
  // Where the feet touch the floor inside the home window.
  float DeskGround { get { return DeskTop + (float)Math.Round(9.5f * ScaleFactor * desk.Zoom) + 10 * DeskPx; } }
  // Something moving on the desktop that needs the full frame rate.
  bool DeskFx { get { DateTime n = DateTime.Now; return Away || game.Length > 0 || visitor != null || ChatActive || desk.Held || (n - desk.LandAt).TotalSeconds < 1 || Speaking || (n - deskNightAt).TotalSeconds < 3.6; } }

  void InitDesk() {
   if (preview) return;
   deskWasNight = NightIdle;
   deskTimer = new Timer { Interval = 16 }; deskTimer.Tick += delegate { DeskTick(); };
   SyncDesk();
  }
  void DisposeDesk() {
   if (deskTimer != null) { deskTimer.Stop(); deskTimer.Dispose(); deskTimer = null; }
   CloseDesk();
  }
  void CloseDesk() {
   EndRoam();
   if (roamPet != null) { roamPet.Close(); roamPet.Dispose(); roamPet = null; }
   if (roamFrame != null) { roamFrame.Dispose(); roamFrame = null; }
   if (deskPet != null) { deskPet.Close(); deskPet.Dispose(); deskPet = null; }
   if (deskFrame != null) { deskFrame.Dispose(); deskFrame = null; }
  }
  // Shows or hides the desktop creature to match the setting.
  void SyncDesk() {
   if (deskTimer == null) return;
   bool want = settings.Desk && settings.Creature && !stopped;
   if (want && deskPet == null) {
    deskPet = new DeskPet(false) { Size = new Size(DeskW, DeskH) };
    deskPet.Location = DeskHome();
    deskPet.Poke = p => { if (GameClick(p)) { DeskTick(); return; } current = desk; try { PokeCreature(p); } finally { current = null; } DeskTick(); };
    deskPet.OpenMenu = p => menu.Show(p);
    deskPet.Chat = p => { if (!desk.Creature.IsEmpty && desk.Creature.Contains(p)) OpenChat(); };
    deskPet.DragStart = delegate { desk.Held = true; dragLast = deskPet.Location; deskTimer.Interval = 16; };
    deskPet.Dragging = delegate { int dx = deskPet.Left - dragLast.X; dragLast = deskPet.Location; desk.Swing = Math.Max(-3f, Math.Min(3f, desk.Swing * .6f - dx / (6f * ScaleFactor))); };
    deskPet.DragEnd = delegate { desk.Held = false; desk.Swing = 0; desk.LandAt = DateTime.Now; Som.Chirp("land"); settings.DeskX = deskPet.Left; settings.DeskY = deskPet.Top; TrySave(); };
    deskPet.Show(); DeskTick(); deskTimer.Start();
   } else if (!want && deskPet != null) { deskTimer.Stop(); CloseDesk(); }
  }
  // The saved spot if it is still on a screen; otherwise the bottom right corner of the main screen.
  Point DeskHome() {
   if (!settings.DeskTall && settings.DeskX != Int32.MinValue) { settings.DeskY -= (int)Math.Round(36 * ScaleFactor); settings.DeskTall = true; TrySave(); }
   var saved = new Rectangle(settings.DeskX, settings.DeskY, DeskW, DeskH);
   if (settings.DeskX != Int32.MinValue) foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(Rectangle.Inflate(saved, -DeskW / 3, -DeskH / 3))) return saved.Location;
   var area = Screen.PrimaryScreen.WorkingArea;
   return new Point(area.Right - DeskW - (int)(48 * ScaleFactor), area.Bottom - DeskH - (int)(24 * ScaleFactor));
  }
  void DeskTick() {
   if (deskPet == null || stopped) return;
   bool nightIdle = NightIdle; if (nightIdle && !deskWasNight) deskNightAt = DateTime.Now; deskWasNight = nightIdle;
   desk.Swing *= .85f;
   RoamTick(); GameTick(); VisitTick();
   // While a game or a visit is on, the home comes to the front (only its drawing takes clicks); then back behind.
   deskPet.SetFloating(game.Length > 0 || visitor != null || ChatActive);
   // Hidden behind a window: only a slow heartbeat, no painting (the name tag is always opaque, so it is the probe).
   bool seen = captionSpot.IsEmpty || desk.Held || deskPet.Floating || deskPet.VisibleAt(deskPet.PointToScreen(Point.Round(captionSpot)));
   bool still = !settings.Animations || (Night && !SessionBusy && !CreatureFx && !DeskFx);
   // Anything moving (a walk, a game, a drag...) always gets the full frame rate, even with the home hidden.
   deskTimer.Interval = DeskFx || CreatureFx ? 16 : !seen ? 700 : still ? 400 : 16;
   if (Away) PaintRoamFrame();
   if (!seen && deskFrame != null) return;
   if (deskFrame == null || deskFrame.Width != DeskW || deskFrame.Height != DeskH) {
    if (deskFrame != null) deskFrame.Dispose();
    deskFrame = new Bitmap(DeskW, DeskH, PixelFormat.Format32bppArgb);
    if (deskPet.Size != deskFrame.Size) deskPet.Size = deskFrame.Size;
   }
   using (var g = Graphics.FromImage(deskFrame)) {
    g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    current = desk;
    try { PaintDesk(g); } finally { current = null; }
   }
   deskPet.Present(deskFrame);
  }

  // ---- the home scene: shelf on the left, the creature (or its bed at night), the task sign on the right, the family and the name tag ----
  void PaintDesk(Graphics g) {
   int w = DeskW, h = DeskH; float sf = ScaleFactor, px = DeskPx, ground = DeskGround;
   desk.W = w; desk.H = h; desk.Ground = ground; desk.Surface = deskPet; desk.Hover = deskPet.ClientRectangle.Contains(deskPet.PointToClient(Cursor.Position));
   bool bed = NightIdle && !Away && !desk.Held;
   PaintShelf(g, 10 * sf, ground - 62 * sf, 90 * sf);
   PaintSign(g, w - 10 * sf, ground);
   PaintDeskFamily(g, ground);
   PaintVisitor(g, ground);
   PaintPlantPot(g); PaintBowl(g); PaintGameBack(g);
   if (!bed && !Away) PaintPuddle(g, w / 2f, ground);
   desk.Pose = bed ? "bed" : "";
   if (bed) PaintBed(g, w / 2f, ground);
   else if (!Away && !PaintGameCreature(g)) PaintCreature(g, (int)Math.Round(HomeX - 7 * px), DeskTop);
   else { desk.Head = PointF.Empty; desk.Creature = RectangleF.Empty; }
   PaintGameFront(g);
   PaintHappening(g);
   if (!bed && !Away) PaintSpeech(g, desk.Head, desk.Crown, w);
   PaintDeskCaption(g, w, (int)Math.Round(ground + px * 2.2f));
  }

  // Name and state in a small dark tag under the creature, readable on any wallpaper.
  void PaintDeskCaption(Graphics g, int w, int y) {
   string project = activeSession == null ? "Meu Claude" : activeSession.Project, who = settings.CreatureName ?? "";
   string name = LevelBanner ?? (NightIdle ? NightTitle : who.Length > 0 ? who + " · " + project : project);
   string line = NightIdle ? NightDetail : Away && roamCall ? "Foi te chamar no terminal" : Away ? "Passeando pela tela" : activeSession == null ? "Aguardando uma sessão" : activeSession.Label;
   Color tone = activeSession == null ? muted : activeSession.Effective == "working" || activeSession.Effective == "background" ? accent : activeSession.Effective == "waiting" ? amber : activeSession.Effective == "done" ? green : activeSession.Effective == "error" ? red : muted;
   var bold = Theme.F(Theme.SansBold, 9.5f); var small = Theme.F(Theme.Sans, 8.5f);
   SizeF a = g.MeasureString(name, bold), b = line.Length > 0 ? g.MeasureString(line, small) : SizeF.Empty;
   float pad = 9 * ScaleFactor, tagW = Math.Min(w - 4, Math.Max(a.Width, b.Width + 12 * ScaleFactor) + pad * 2), tagH = a.Height + b.Height + pad * .8f;
   var tag = new RectangleF((w - tagW) / 2, y, tagW, tagH);
   captionSpot = new PointF(tag.X + tag.Width / 2, tag.Y + tag.Height / 2);
   using (var path = Rounded(tag, tagH / 2.6f)) {
    using (var fill = new SolidBrush(Color.FromArgb(218, 30, 30, 34))) g.FillPath(fill, path);
    using (var edge = new Pen(Color.FromArgb(60, 255, 255, 255), 1f)) g.DrawPath(edge, path);
   }
   using (var f = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }) {
    using (var brush = new SolidBrush(LevelBanner != null ? Color.FromArgb(246, 200, 70) : ink)) g.DrawString(name, bold, brush, new RectangleF(tag.X + pad / 2, tag.Y + pad * .4f, tag.Width - pad, a.Height), f);
    if (line.Length > 0) {
     float dot = 6 * ScaleFactor, textW = Math.Min(b.Width, tag.Width - pad * 2 - dot * 2), x0 = tag.X + (tag.Width - textW - dot * 1.8f) / 2, ly = tag.Y + pad * .4f + a.Height;
     using (var d = new SolidBrush(NightIdle ? muted : tone)) g.FillEllipse(d, x0, ly + (b.Height - dot) / 2, dot, dot);
     f.Alignment = StringAlignment.Near;
     using (var brush = new SolidBrush(Color.FromArgb(200, 214, 218, 224))) g.DrawString(line, small, brush, new RectangleF(x0 + dot * 1.8f, ly, textW + 2, b.Height), f);
    }
   }
  }

  // ---- the task sign, planted in the ground: number and hours; it smoulders near the estimate and burns past it ----
  void PaintSign(Graphics g, float right, float ground) {
   string first, second = ""; double f = -1;
   var task = CurrentTask;
   bool music = !Away && MusicOn && (task == null || ((int)(MotionTime.Now / 1000.0 / 5)) % 2 == 1);
   if (Away) first = "volto já!";
   else if (music) { first = "♪ tocando"; second = song; }
   else {
    if (task == null) return;
    double total; f = TaskFraction(task, out total);
    first = "#" + task.Id; second = f >= 0 ? WorkDay.Duration(total) + " / " + Hours(task.Estimated) : WorkDay.Duration(total);
   }
   float sf = ScaleFactor, bw = 70 * sf, bh = second.Length > 0 ? 32 * sf : 20 * sf, x = right - bw, y = ground - 30 * sf - bh;
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var post = new SolidBrush(Color.FromArgb(124, 86, 56))) g.FillRectangle(post, x + bw / 2 - 2 * sf, y + bh - 1, 4 * sf, ground - y - bh + 1);
   using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(shadow, x + bw / 2 - 8 * sf, ground - 2 * sf, 16 * sf, 4 * sf);
   var board = new RectangleF(x, y, bw, bh);
   // Past 85%: embers glowing on top; past 100%: flames and smoke.
   if (f >= 0.85) using (var path = new GraphicsPath()) {
    path.AddEllipse(x - 6 * sf, y - 14 * sf, bw + 12 * sf, 28 * sf);
    double pulse = (Math.Sin(t * 5) + 1) / 2;
    using (var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(f >= 1 ? 120 + 60 * pulse : 50 + 40 * pulse), 255, 140, 50), SurroundColors = new[] { Color.FromArgb(0, 255, 140, 50) } }) g.FillPath(glow, path);
   }
   using (var shape = Rounded(board, 3 * sf)) {
    using (var wood = new SolidBrush(f >= 1 ? Color.FromArgb(150, 98, 60) : Color.FromArgb(198, 148, 98))) g.FillPath(wood, shape);
    using (var grain = new Pen(Color.FromArgb(40, 90, 50, 20), 1f)) for (int i = 1; i < 4; i++) g.DrawLine(grain, x + 4 * sf, y + bh * i / 4f, x + bw - 4 * sf, y + bh * i / 4f + (i % 2 == 0 ? sf : -sf));
    using (var edge = new Pen(Color.FromArgb(140, 98, 62), 1.5f * sf)) g.DrawPath(edge, shape);
   }
   using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter }) {
    using (var brush = new SolidBrush(Color.FromArgb(62, 40, 24))) g.DrawString(first, Theme.F(Theme.SansBold, 8.5f), brush, new RectangleF(x, y + 2 * sf, bw, second.Length > 0 ? bh / 2 : bh - 4 * sf), fmt);
    if (music) {
     // Long song names scroll inside the board.
     var font = Theme.F(Theme.SansBold, 7.5f); float tw = g.MeasureString(second, font).Width, room = bw - 8 * sf, ty = y + bh / 2 - 1;
     var clip = g.Clip; g.SetClip(new RectangleF(x + 4 * sf, ty, room, bh / 2));
     using (var brush = new SolidBrush(Color.FromArgb(70, 44, 110))) {
      if (tw <= room) g.DrawString(second, font, brush, x + 4 * sf + (room - tw) / 2, ty);
      else { float gap = 24 * sf, off = (float)(MotionTime.Now / 1000.0 * 22 * sf % (tw + gap)); g.DrawString(second, font, brush, x + 4 * sf - off, ty); g.DrawString(second, font, brush, x + 4 * sf - off + tw + gap, ty); }
     }
     g.Clip = clip;
    }
    else if (second.Length > 0) using (var brush = new SolidBrush(f >= 1 ? Color.FromArgb(150, 24, 24) : f >= 0.7 ? Color.FromArgb(130, 82, 10) : Color.FromArgb(40, 100, 54))) g.DrawString(second, Theme.F(Theme.SansBold, 7.5f), brush, new RectangleF(x, y + bh / 2 - 1, bw, bh / 2 - 2 * sf), fmt);
   }
   if (f >= 1) {
    for (int i = 0; i < 5; i++) {
     float cx = x + bw * (0.12f + i * 0.19f), flick = (float)(Math.Sin(t * 13 + i * 2) * .18 + Math.Sin(t * 7.3 + i) * .12), fh = (8 + (i % 2) * 4) * sf * (1 + flick), fw = 4.5f * sf;
     using (var outer = Drop(cx, y + 2 * sf, fw, fh)) using (var b = new SolidBrush(Color.FromArgb(235, 245, 120, 50))) g.FillPath(b, outer);
     using (var inner = Drop(cx, y + 2 * sf, fw * .55f, fh * .6f)) using (var b = new SolidBrush(Color.FromArgb(250, 252, 214, 100))) g.FillPath(b, inner);
    }
    for (int i = 0; i < 3; i++) {
     double ph = (t * 0.45 + i / 3.0) % 1.0; float r = 2 * sf + (float)ph * 4 * sf;
     using (var puff = new SolidBrush(Color.FromArgb((int)(90 * (1 - ph)), 150, 150, 158))) g.FillEllipse(puff, x + bw * (0.3f + i * .2f) + (float)Math.Sin(t * 2 + i) * 3 * sf - r, y - 12 * sf - (float)ph * 24 * sf - r, r * 2, r * 2);
    }
   }
   g.SmoothingMode = mode;
  }
  static GraphicsPath Drop(float cx, float bottom, float w, float h) {
   var path = new GraphicsPath(); float top = bottom - h;
   path.AddBezier(cx, top, cx + w * .9f, bottom - h * .45f, cx + w * .7f, bottom, cx, bottom);
   path.AddBezier(cx, bottom, cx - w * .7f, bottom, cx - w * .9f, bottom - h * .45f, cx, top);
   return path;
  }

  // ---- rain: a puddle at its feet that it jumps into now and then (in step with the hop in PaintCreature) ----
  void PaintPuddle(Graphics g, float cx, float ground) {
   if (!settings.WeatherFx || !(weather.Kind == "rain" || weather.Kind == "storm")) return;
   float px = DeskPx; double t = settings.Animations ? MotionTime.Now / 1000.0 : 0, ph = t % 7;
   using (var water = new SolidBrush(Color.FromArgb(120, 110, 160, 220))) g.FillEllipse(water, cx - 9 * px, ground - .4f * px, 18 * px, 2.4f * px);
   using (var shine = new SolidBrush(Color.FromArgb(90, 200, 225, 255))) g.FillEllipse(shine, cx - 5 * px, ground + .1f * px, 5 * px, .6f * px);
   for (int i = 0; i < 2; i++) {
    double k = (t * .5 + i * .5) % 1.0; float rw = px * (3 + (float)k * 10), rh = rw * .14f;
    using (var ring = new Pen(Color.FromArgb((int)(110 * (1 - k)), 200, 225, 255), 1f)) g.DrawEllipse(ring, cx + (i == 0 ? -4 : 5) * px - rw / 2, ground + .8f * px - rh / 2, rw, rh);
   }
   if (ph >= .55 && ph < 1.15) {
    double k = (ph - .55) / .6;
    using (var drop = new SolidBrush(Color.FromArgb((int)(220 * (1 - k)), 170, 205, 245)))
     for (int i = 0; i < 6; i++) {
      double ang = Math.PI * (0.15 + i * 0.14); float d = (float)k * px * 9;
      g.FillEllipse(drop, cx + (float)Math.Cos(ang) * d * (i < 3 ? -1 : 1) - px * .4f, ground - (float)(Math.Sin(ang) * d * 1.2 - k * k * px * 8) - px * .4f, px * .8f, px * .8f);
     }
   }
  }

  // ---- night: a little bed; the lamp goes off a moment after the day ends (or after a poke) ----
  void PaintBed(Graphics g, float cx, float ground) {
   float sf = ScaleFactor, px = DeskPx;
   var mode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
   Color frame = Color.FromArgb(150, 104, 66), frameDark = Color.FromArgb(112, 76, 48);
   // Night stand with the lamp, left of the headboard.
   float stand = cx - 98 * sf;
   using (var b = new SolidBrush(frameDark)) g.FillRectangle(b, stand, ground - 24 * sf, 20 * sf, 24 * sf);
   using (var b = new SolidBrush(frame)) g.FillRectangle(b, stand - 1 * sf, ground - 26 * sf, 22 * sf, 3 * sf);
   double e = (DateTime.Now - deskNightAt).TotalSeconds; float light = e < 2.4 ? 1 : e < 3.2 ? (float)(1 - (e - 2.4) / 0.8) : 0;
   float lampX = stand + 10 * sf, lampTop = ground - 44 * sf;
   if (light > 0) using (var path = new GraphicsPath()) {
    path.AddEllipse(lampX - 46 * sf, lampTop - 30 * sf, 92 * sf, 80 * sf);
    using (var glow = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(110 * light), 255, 214, 140), SurroundColors = new[] { Color.FromArgb(0, 255, 214, 140) } }) g.FillPath(glow, path);
   }
   using (var b = new SolidBrush(Color.FromArgb(90, 96, 108))) g.FillRectangle(b, lampX - 1 * sf, lampTop + 8 * sf, 2 * sf, 10 * sf);
   using (var shade = new SolidBrush(light > 0 ? Color.FromArgb(255, (int)(200 + 50 * light), (int)(150 + 70 * light), 90) : Color.FromArgb(120, 112, 100)))
    g.FillPolygon(shade, new[] { new PointF(lampX - 5 * sf, lampTop), new PointF(lampX + 5 * sf, lampTop), new PointF(lampX + 8 * sf, lampTop + 9 * sf), new PointF(lampX - 8 * sf, lampTop + 9 * sf) });
   // Front view: headboard and pillow behind, the creature tucked in, the blanket up to its chest.
   float lower = 0, headTop = DeskTop + (float)Math.Round(9.5f * sf * desk.Zoom) + lower;
   using (var shape = Rounded(new RectangleF(cx - 62 * sf, headTop - 18 * sf, 124 * sf, ground - headTop + 12 * sf), 12 * sf)) {
    using (var b = new SolidBrush(frame)) g.FillPath(b, shape);
    using (var edge = new Pen(frameDark, 2 * sf)) g.DrawPath(edge, shape);
   }
   using (var shape = Rounded(new RectangleF(cx - 50 * sf, headTop + 6 * sf, 100 * sf, 26 * sf), 10 * sf)) using (var b = new SolidBrush(Color.FromArgb(246, 246, 250))) g.FillPath(b, shape);
   g.SmoothingMode = mode;
   PaintCreature(g, (int)Math.Round(cx - 7 * px), (int)Math.Round(DeskTop + lower));
   g.SmoothingMode = SmoothingMode.AntiAlias;
   var cover = new RectangleF(cx - 66 * sf, headTop + 6f * px, 132 * sf, ground + 2 * sf - (headTop + 6f * px));
   using (var shape = Rounded(cover, 6 * sf)) {
    using (var b = new SolidBrush(Color.FromArgb(98, 120, 196))) g.FillPath(b, shape);
    using (var stripe = new Pen(Color.FromArgb(60, 255, 255, 255), 2 * sf)) for (float sx = cover.X + 12 * sf; sx < cover.Right - 6 * sf; sx += 14 * sf) g.DrawLine(stripe, sx, cover.Y + 7 * sf, sx, cover.Bottom - 3 * sf);
   }
   using (var shape = Rounded(new RectangleF(cover.X, cover.Y, cover.Width, 7 * sf), 3.5f * sf)) using (var b = new SolidBrush(Color.FromArgb(236, 238, 246))) g.FillPath(b, shape); // the sheet folded over
   using (var b = new SolidBrush(frameDark)) { g.FillRectangle(b, cx - 64 * sf, ground - 6 * sf, 128 * sf, 4 * sf); g.FillRectangle(b, cx - 62 * sf, ground - 3 * sf, 5 * sf, 3 * sf); g.FillRectangle(b, cx + 57 * sf, ground - 3 * sf, 5 * sf, 3 * sf); }
   g.SmoothingMode = mode;
  }

  // ---- the other sessions as small creatures walking around under the shelf ----
  static readonly Color[] FamilyTints = { Color.FromArgb(98, 160, 234), Color.FromArgb(104, 190, 130), Color.FromArgb(236, 130, 170), Color.FromArgb(160, 124, 222), Color.FromArgb(92, 200, 186) };
  void PaintDeskFamily(Graphics g, float ground) {
   var others = OtherActive().OrderByDescending(s => s.State == "waiting").ThenBy(s => s.Id).Take(2).ToList(); if (others.Count == 0) return;
   double t = settings.Animations && !Night ? MotionTime.Now / 1000.0 : 0; bool cheer = (DateTime.Now - cheerAt).TotalSeconds < 1.9;
   float sf = ScaleFactor, px = Math.Max(1f, sf * 2.9f);
   var mode = g.SmoothingMode;
   for (int i = 0; i < others.Count; i++) {
    var s = others[i]; bool waiting = s.State == "waiting"; int seed = Math.Abs(s.Id.GetHashCode());
    double phase = t * 0.35 + seed % 100 / 16.0;
    float home = (i == 0 ? 36 : 86) * sf, x = home + (float)Math.Sin(phase) * 9 * sf; bool walking = !waiting && !cheer && Math.Abs(Math.Cos(phase)) > .35 && t > 0;
    int lift = cheer ? (int)Math.Round(Math.Abs(Math.Sin(t * 7 + i)) * 3) : waiting ? (int)Math.Round(Math.Abs(Math.Sin(t * 5 + i)) * 2) : walking ? ((int)(t * 8 + i)) % 2 : 0;
    float ox = x - 6 * px, oy = ground - 10 * px - lift * px;
    g.SmoothingMode = SmoothingMode.AntiAlias;
    using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0))) g.FillEllipse(shadow, x - 5 * px, ground - .6f * px, 10 * px, 1.4f * px);
    g.SmoothingMode = SmoothingMode.None;
    Color body = waiting ? amber : FamilyTints[seed % FamilyTints.Length], shade = Color.FromArgb(body.R * 3 / 4, body.G * 3 / 4, body.B * 3 / 4), dark = Color.FromArgb(28, 26, 30);
    if (Style3D) {
     PaintMini3D(g, x, ground - lift * px, 12 * px, body, cheer ? "cheer" : waiting ? "waiting" : walking ? "walk" : s.State == "working" ? "work" : "idle", t, i);
     if (waiting && ((int)(t * 3)) % 2 == 0) using (var b = new SolidBrush(amber)) g.DrawString("!", Theme.F(Theme.SansBold, 9f), b, x - 3 * Sf, ground - 17 * px);
     continue;
    }
    var p = new Pix(g, ox, oy, px);
    try {
     for (int r = 0; r < CreatureBody.Length; r++) for (int c = 0; c < 12; c++) if (CreatureBody[r][c] == '#') p.Dot(c, r, r == 7 ? shade : body);
     if (cheer) { p.Dot(3, 5, dark); p.Dot(4, 4, dark); p.Dot(5, 5, dark); p.Dot(6, 5, dark); p.Dot(7, 4, dark); p.Dot(8, 5, dark); }
     else { int look = walking ? (Math.Cos(phase) > 0 ? 1 : -1) : 0; p.Dot(4 + look, 4, dark); p.Dot(4 + look, 5, dark); p.Dot(7 + look, 4, dark); p.Dot(7 + look, 5, dark); }
     bool step = walking && ((int)(t * 8 + i)) % 2 == 0;
     p.Dot(3, step ? 7.5f : 8, shade); p.Dot(8, step ? 8 : 7.5f, shade);
     if (cheer) { bool up = ((int)(t * 6 + i)) % 2 == 0; p.Dot(-1, up ? 1 : 3, shade); p.Dot(12, up ? 3 : 1, shade); }
     else if (waiting) { bool up = ((int)(t * 4)) % 2 == 0; p.Dot(12, up ? 2 : 3, shade); p.Dot(-1, 6, shade); if (((int)(t * 3)) % 2 == 0) { p.Dot(5.5f, -4, amber); p.Dot(5.5f, -3, amber); p.Dot(5.5f, -1.5f, amber); } }
     else if (s.State == "working") { bool up = ((int)(t * 8 + i)) % 2 == 0; p.Dot(-1, up ? 5 : 6, shade); p.Dot(12, up ? 6 : 5, shade); }
     else { p.Dot(-1, 6, shade); p.Dot(12, 6, shade); }
    } finally { p.Dispose(); }
   }
   g.SmoothingMode = mode;
  }
 }
}
