namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;
 using System.IO;

 // Short lines the creature says on the desktop, drawn as a small bubble next to it (never a popup).
 public partial class Widget {
  string speech = "", spokenTool = ""; DateTime speechAt = DateTime.MinValue, lastSpeech = DateTime.MinValue; double speechFor = 3.5;
  readonly HashSet<string> saidToday = new HashSet<string>(); string saidDay = "";
  readonly Random talk = new Random();

  void Say(string text, bool important = false) {
   if (!settings.Speech || preview || String.IsNullOrEmpty(text)) return;
   if (!important && (DateTime.Now - lastSpeech).TotalSeconds < 6) return;
   speech = text; speechAt = DateTime.Now; lastSpeech = speechAt;
   speechFor = Math.Max(2.8, Math.Min(6, 1.4 + text.Length * 0.07));
  }
  bool Speaking { get { double e = (DateTime.Now - speechAt).TotalSeconds; return settings.Speech && speech.Length > 0 && e >= 0 && e < speechFor; } }
  string Pick(params string[] options) { return options[talk.Next(options.Length)]; }
  static string ToolPhrase(string tool) {
   tool = tool ?? "";
   if (tool == "Edit" || tool == "Write" || tool == "MultiEdit" || tool == "NotebookEdit") return "Editando o código…";
   if (tool == "Read" || tool == "Grep" || tool == "Glob" || tool == "LS") return "Lendo o código…";
   if (tool == "Bash" || tool == "PowerShell") return "Rodando um comando…";
   if (tool == "WebFetch" || tool == "WebSearch") return "Pesquisando na web…";
   if (tool == "Agent" || tool == "Task") return "Chamei um ajudante!";
   if (tool.StartsWith("mcp__redmine", StringComparison.Ordinal)) return "Olhando o Redmine…";
   return "Bora trabalhar!";
  }
  // Lines tied to the clock, once a day each.
  void SpeechClock() {
   DateTime n = DateTime.Now; string day = n.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   if (day != saidDay) { saidDay = day; saidToday.Clear(); }
   if (!WorkDay.IsWorkday(n)) return;
   var lines = new[] {
    new KeyValuePair<TimeSpan,string>(new TimeSpan(8, 0, 0), "Bom dia! Café?"),
    new KeyValuePair<TimeSpan,string>(new TimeSpan(12, 0, 0), "Bora almoçar?"),
    new KeyValuePair<TimeSpan,string>(new TimeSpan(13, 12, 0), "De volta ao trabalho!"),
    new KeyValuePair<TimeSpan,string>(new TimeSpan(17, 0, 0), n.DayOfWeek == DayOfWeek.Friday ? "Sextou!" : ""),
    new KeyValuePair<TimeSpan,string>(new TimeSpan(17, 55, 0), "Hora de apontar as horas!"),
    new KeyValuePair<TimeSpan,string>(new TimeSpan(18, 0, 0), "Até amanhã!")
   };
   foreach (var line in lines) {
    if (line.Value.Length == 0 || saidToday.Contains(line.Value)) continue;
    double after = (n.TimeOfDay - line.Key).TotalMinutes;
    if (after >= 0 && after < 2) { saidToday.Add(line.Value); Say(line.Value, true); }
   }
  }
  // The bubble: up and to the right of the head, with a little tail pointing at it.
  void PaintSpeech(Graphics g, PointF head, float crown, int width) {
   if (!Speaking || head.IsEmpty) return;
   double e = (DateTime.Now - speechAt).TotalSeconds;
   float pop = (float)MotionTime.Ease(Math.Min(1, e / 0.18)), fade = (float)Math.Min(1, (speechFor - e) / 0.35);
   float sf = ScaleFactor; var font = Theme.F(Theme.Sans, 8.5f);
   SizeF size = g.MeasureString(speech, font);
   float pad = 7 * sf, bw = Math.Min(width - 8 * sf, size.Width + pad * 2), bh = size.Height + pad * 1.1f;
   float bx = Math.Max(4 * sf, Math.Min(width - bw - 4 * sf, head.X + 12 * sf)), by = Math.Max(2 * sf, crown - 8 * sf - bh);
   var state = g.Save();
   var tip = new PointF(Math.Max(bx + 8 * sf, Math.Min(bx + bw - 8 * sf, head.X + 6 * sf)), by + bh + 6 * sf);
   g.TranslateTransform(tip.X, tip.Y); g.ScaleTransform(.6f + .4f * pop, .6f + .4f * pop); g.TranslateTransform(-tip.X, -tip.Y);
   int a = (int)(250 * Math.Max(0, fade));
   using (var path = Rounded(new RectangleF(bx, by, bw, bh), bh / 2.4f)) {
    using (var tail = new GraphicsPath()) {
     tail.AddPolygon(new[] { new PointF(tip.X - 5 * sf, by + bh - 1), new PointF(tip.X + 4 * sf, by + bh - 1), tip });
     using (var fill = new SolidBrush(Color.FromArgb(a, 248, 246, 242))) { g.FillPath(fill, path); g.FillPath(fill, tail); }
    }
    using (var edge = new Pen(Color.FromArgb(a / 4, 0, 0, 0), 1f)) g.DrawPath(edge, path);
   }
   using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
   using (var ink2 = new SolidBrush(Color.FromArgb(a, 44, 40, 38))) g.DrawString(speech, font, ink2, new RectangleF(bx + pad / 2, by, bw - pad, bh), f);
   g.Restore(state);
  }

  // For the terminal status line: how the creature is, its level and its name.
  string creatureFile = "";
  void WriteCreatureFile(bool force) {
   try {
    var data = new Dictionary<string,object> { { "Mood", mood }, { "Level", CreatureLevel }, { "Name", settings.CreatureName ?? "" }, { "Night", NightIdle }, { "Shape", Shape } };
    string json = Data.Json().Serialize(data);
    if (!force && json == creatureFile) return;
    creatureFile = json;
    File.WriteAllText(Path.Combine(Data.Folder, "bicho.json"), json);
   } catch { }
  }
 }
}
