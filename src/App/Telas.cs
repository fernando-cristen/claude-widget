namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Linq;
 using System.Windows.Forms;

 // Claude Code sessions seen by the radar (last 24 h): state, Redmine issue, model and cost; details of the selected one.
 public sealed class RadarForm : PremiumWindow {
  readonly Func<List<SessionInfo>> read; readonly Func<string> describe; readonly Action<string> select, focus; readonly Func<SessionInfo,string> task;
  readonly Timer clock = new Timer { Interval = 2000 }; string selected = "";
  public RadarForm(Func<List<SessionInfo>> readSessions, Func<string> description, Action<string> selectSession, Action<string> focusSession, Func<SessionInfo,string> taskLabel)
   : base("Projetos e sessões", 760, 720) {
   read = readSessions; describe = description; select = selectSession; focus = focusSession; task = taskLabel;
   SetFooter(new ButtonsEl { Hint = "Duplo clique numa sessão abre o terminal" }
    .Add("Seguir a mais recente", false, () => { selected = ""; select(""); Rebuild(); }, Theme.Sync)
    .Add("Ir para o terminal", false, () => { if (selected.Length > 0) focus(selected); }, Theme.Terminal)
    .Add("Fechar", true, Close));
   clock.Tick += delegate { Rebuild(); }; Shown += delegate { Rebuild(); clock.Start(); };
   FormClosed += delegate { clock.Stop(); clock.Dispose(); };
  }
  static string Glyph(SessionInfo s) {
   if (s.Closed || s.State == "ended") return Theme.Stop;
   switch (s.State) { case "working": case "background": return Theme.Sync; case "waiting": return Theme.Warning; case "done": return Theme.CheckCircle; case "error": return Theme.Error; case "idle": return Theme.Clock; default: return Theme.Info; }
  }
  static Color Tone(SessionInfo s) {
   if (s.Closed || s.Silent || s.State == "ended") return Theme.Muted;
   switch (s.State) { case "working": return Theme.Accent; case "background": return Theme.Blue; case "waiting": return Theme.Amber; case "done": return Theme.Green; case "error": return Theme.Red; default: return Theme.Soft; }
  }
  void Rebuild() {
   var sessions = read();
   int active = sessions.Count(s => s.Active), waiting = sessions.Count(s => s.Active && s.State == "waiting");
   var items = new List<El> { new TitleEl { Glyph = Theme.Terminal, Color = Theme.Accent, Title = "Projetos e sessões",
    Subtitle = sessions.Count + (sessions.Count == 1 ? " sessão" : " sessões") + " nas últimas 24h · " + active + " ativa(s)" + (waiting > 0 ? " · " + waiting + " precisa(m) de você" : "") } };
   items.Add(new SectionEl { Text = "Sessões", Right = "clique para seguir no widget" });
   var list = new CardEl { Pad = 6 };
   if (sessions.Count == 0) list.Add(new TextEl { Text = "Nenhuma sessão do Claude Code nas últimas 24 horas. Rode Ativar-Radar.cmd uma vez e envie uma mensagem numa sessão.", Color = Theme.Muted, Inset = 10, Before = 0 });
   foreach (var s in sessions) {
    string id = s.Id, label = task(s);
    list.Add(new RowEl {
     Glyph = Glyph(s), GlyphColor = Tone(s), Title = s.Project, TitleColor = s.Closed || s.State == "ended" ? Theme.Soft : Theme.Ink,
     Subtitle = s.Activity, Meta = s.Usage, Chip = label, ChipColor = label.Contains("fora") ? Theme.Muted : Theme.Accent,
     Right = s.Time.ToLocalTime().ToString("HH:mm"), Selected = id == selected, Tip = s.Cwd,
     Click = () => { selected = id; select(id); Rebuild(); }, DoubleClick = () => focus(id) });
   }
   items.Add(list);
   items.Add(new SectionEl { Text = selected.Length > 0 ? "Detalhes da sessão" : "Detalhes da sessão seguida" });
   items.Add(new CardEl { Pad = 16 }.Add(new TextEl { Text = describe().Replace("\r\n", "\n"), Font = Theme.F(Theme.Sans, 9), Color = Theme.Soft }));
   Body.SetItems(items);
  }
 }

 // Preview of today's automatic time entry (nothing is written).
 public sealed class PreviewForm : PremiumWindow {
  public PreviewForm(List<LogItem> items, string error, string schedule, DateTime now) : base("Prévia do apontamento", 620, 420) {
   var list = new List<El> { new TitleEl { Glyph = Theme.Clock, Color = Theme.Accent, Title = "Prévia do apontamento", Subtitle = schedule } };
   list.Add(new SectionEl { Text = "Tarefas em andamento", Right = "jornada 08:00–12:00 · 13:12–18:00" });
   var card = new CardEl { Pad = 6 };
   if (error.Length > 0) card.Add(new TextEl { Text = "Não foi possível consultar o Redmine. " + error, Color = Theme.Red, Inset = 10, Before = 0 });
   else if (items.Count == 0) card.Add(new TextEl { Text = "Nenhuma tarefa sua em andamento agora. Nada seria apontado.", Color = Theme.Muted, Inset = 10, Before = 0 });
   foreach (var item in items) {
    string detail = "Em andamento desde " + AutoLog.Since(item, now) + " · hoje até 18:00: " + WorkDay.Duration(item.Raw);
    if (item.Logged > 0) detail += "\nJá apontado hoje nesta tarefa: " + WorkDay.Amount(item.Logged);
    card.Add(new RowEl { Glyph = Theme.Code, GlyphColor = item.Hours > 0 ? Theme.Green : Theme.Muted, Title = "#" + item.Issue + "  " + item.Subject, Subtitle = detail,
     Chip = item.Hours > 0 ? "aponta " + WorkDay.Amount(item.Hours) : "nada a apontar", ChipColor = item.Hours > 0 ? Theme.Green : Theme.Muted, Right = item.Hours > 0 ? "Desenvolvimento" : "" });
   }
   list.Add(card);
   list.Add(new TextEl { Text = "Arredonda para o 0,5h mais próximo e desconta o que você já apontou hoje na tarefa. Esta prévia não grava nada no Redmine.", Color = Theme.Muted, Font = Theme.F(Theme.Sans, 8.5f), Before = 16, Inset = 4 });
   Body.SetItems(list);
   SetFooter(new ButtonsEl().Add("Entendi", true, Close));
  }
 }
}
