namespace MeuClaudeWidget {
 // What the creature does because of what Claude is doing right now, read from the radar of the followed session:
 // a command that takes long (it sits down and taps a foot), a burst of edits (it runs from side to side) and a
 // pending permission or question (it looks at you and points to the terminal). Gestures, never signs.
 public partial class Widget {
  readonly List<KeyValuePair<DateTime, string>> toolLog = new List<KeyValuePair<DateTime, string>>();
  string toolKey = ""; int terminalSide; DateTime terminalSideAt = DateTime.MinValue;

  // Every radar read: remembers each tool event of the followed session (one entry per hook event).
  void TrackTools() {
   if (activeSession == null) return;
   string key = activeSession.Id + "|" + activeSession.Updated + "|" + activeSession.Event + "|" + activeSession.Tool;
   if (key == toolKey) return;
   toolKey = key;
   DateTime now = DateTime.Now;
   if (!String.IsNullOrEmpty(activeSession.Tool) && (activeSession.Event == "PreToolUse" || activeSession.Event == "PostToolUse")) toolLog.Add(new KeyValuePair<DateTime, string>(now, activeSession.Tool));
   toolLog.RemoveAll(e => (now - e.Key).TotalSeconds > 90);
   if (toolLog.Count > 60) toolLog.RemoveRange(0, toolLog.Count - 60);
  }
  static bool IsEdit(string tool) { return tool == "Edit" || tool == "Write" || tool == "MultiEdit" || tool == "NotebookEdit"; }
  static bool IsShell(string tool) { return tool == "Bash" || tool == "PowerShell"; }

  // "run": runTo is where to run (alternates sides); "wait": a long command; "point": side of the terminal (-1 left, 1 right).
  string WorkReaction(DateTime now, out float runTo, out int side) {
   runTo = 0; side = 1;
   var s = activeSession; if (s == null || s.Closed || s.Silent) return "";
   double age = (DateTimeOffset.UtcNow - s.Time).TotalSeconds;
   if (s.State == "waiting" && age < 180) { side = TerminalSide(now); return "point"; }
   if (s.Effective != "working") return "";
   // A shell command started and nothing came back for a while: it is going to take long.
   if (IsShell(s.Tool ?? "") && s.Event == "PreToolUse" && age > 8) return "wait";
   // Several edits in the last seconds: busy, busy.
   int edits = 0; foreach (var e in toolLog) if ((now - e.Key).TotalSeconds < 20 && IsEdit(e.Value)) edits++;
   if (edits >= 3) { runTo = (((int)(MotionTime.Now / 1300)) % 2 == 0 ? -1 : 1) * Pz(7); return "run"; }
   return "";
  }

  // Which side the session's terminal window is on, relative to the creature; checked every couple of seconds.
  int TerminalSide(DateTime now) {
   if ((now - terminalSideAt).TotalSeconds < 2) return terminalSide;
   terminalSideAt = now; terminalSide = 1;
   try {
    var s = activeSession; if (s == null || s.WindowPid <= 0 || stage == null || stage.Surface == null || stage.Head.IsEmpty) return terminalSide;
    using (var process = System.Diagnostics.Process.GetProcessById(s.WindowPid)) {
     IntPtr handle = process.MainWindowHandle; Native.Rect r;
     if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out r)) return terminalSide;
     var head = stage.Surface.PointToScreen(new Point((int)stage.Head.X, (int)stage.Head.Y));
     terminalSide = (r.Left + r.Right) / 2 < head.X ? -1 : 1;
    }
   } catch { }
   return terminalSide;
  }
 }
}
