namespace MeuClaudeWidget {
 using System;
 using System.Drawing;
 using System.Runtime.InteropServices;
 using System.Text;
 using System.Windows.Automation;
 using System.Windows.Forms;

 // Free room on the primary taskbar: between the Widgets (weather) button and the centered icons, and between
 // the icons and the notification area. Found with UI Automation, the accessibility API Windows already uses.
 public sealed class TaskbarSlot {
  public Rectangle Bar, LeftGap, RightGap;
  public bool Hidden, Fullscreen;
  public string Weather = ""; // accessible name of the Windows weather button, e.g. "Widgets 21°C Pred. nublado"
 }
 public static class Taskbar {
  [StructLayout(LayoutKind.Sequential)] struct R { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
  [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out R rect);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
  static Rectangle Rect(IntPtr hwnd) { R r; return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty; }
  static Rectangle Rect(System.Windows.Rect r) { return r.IsEmpty ? Rectangle.Empty : Rectangle.FromLTRB((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom); }

  // Runs off the UI thread (UI Automation can take ~100 ms). Null when the taskbar is vertical or not found.
  public static TaskbarSlot Measure() {
   IntPtr shell = FindWindow("Shell_TrayWnd", null);
   var bar = Rect(shell); var screen = Screen.PrimaryScreen.Bounds;
   if (bar.IsEmpty || bar.Width < bar.Height) return null;
   var slot = new TaskbarSlot { Bar = bar, Hidden = bar.Top >= screen.Bottom - 6 || bar.Bottom <= screen.Top + 6, Fullscreen = ForegroundFullscreen(screen) };
   int left = bar.Left, groupLeft = Int32.MaxValue, groupRight = Int32.MinValue;
   int trayLeft = bar.Right - 220;
   var notify = Rect(FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null));
   if (!notify.IsEmpty && notify.Left > bar.Left) trayLeft = notify.Left;
   try {
    var root = AutomationElement.FromHandle(shell);
    foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, Condition.TrueCondition)) {
     string id = e.Current.AutomationId ?? ""; var r = Rect(e.Current.BoundingRectangle);
     if (r.IsEmpty || r.Height < 16) continue;
     if (id == "WidgetsButton") { slot.Weather = e.Current.Name ?? ""; if (r.Left < bar.Left + bar.Width / 3) left = Math.Max(left, r.Right); }
     else if (id == "StartButton" || id == "SearchButton" || id == "TaskViewButton" || id == "CopilotButton" || id.StartsWith("Appid:", StringComparison.Ordinal)) {
      groupLeft = Math.Min(groupLeft, r.Left); groupRight = Math.Max(groupRight, r.Right);
     } else if (id == "SystemTrayIcon" || e.Current.ClassName == "SystemTray.NormalButton") trayLeft = Math.Min(trayLeft, r.Left);
    }
   } catch { }
   if (groupLeft == Int32.MaxValue) { groupLeft = bar.Left + bar.Width / 2; groupRight = groupLeft; }
   int margin = 8;
   slot.LeftGap = Rectangle.FromLTRB(left + margin, bar.Top, Math.Max(left + margin, groupLeft - margin), bar.Bottom);
   slot.RightGap = Rectangle.FromLTRB(groupRight + margin, bar.Top, Math.Max(groupRight + margin, trayLeft - margin), bar.Bottom);
   return slot;
  }
  public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
  [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
  [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
  static WinEventProc keep; // the callback must outlive the hook
  // Calls back on the UI thread whenever another window comes to the foreground (EVENT_SYSTEM_FOREGROUND).
  public static IntPtr WatchForeground(Action changed) {
   keep = delegate { changed(); };
   return SetWinEventHook(3, 3, IntPtr.Zero, keep, 0, 0, 0);
  }
  public static bool ForegroundFullscreen() { return ForegroundFullscreen(Screen.PrimaryScreen.Bounds); }
  // A video or game covering the whole primary screen: the strip steps aside like the taskbar does.
  static bool ForegroundFullscreen(Rectangle screen) {
   IntPtr fore = GetForegroundWindow(); if (fore == IntPtr.Zero) return false;
   var name = new StringBuilder(64); GetClassName(fore, name, 64); string cls = name.ToString();
   if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
   var r = Rect(fore);
   return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
  }
  // The taskbar's color at a point, so the strip blends in like a native button.
  public static Color Sample(Point p) {
   try {
    using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(p, Point.Empty, new Size(1, 1)); return bmp.GetPixel(0, 0); }
   } catch { return Color.Empty; }
  }
 }
}
