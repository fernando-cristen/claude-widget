namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Linq;
 using System.Windows.Forms;

 // Weekly scoreboard: finished issues, first-pass QA, hours per day and the no-rejection streak.
 public sealed class ScoreForm : PremiumWindow {
  static readonly string[] Names = { "Segunda", "Terça", "Quarta", "Quinta", "Sexta" };
  readonly Settings settings; readonly Action save; readonly Action<string> open; readonly Action<DateTime> logDay;
  WeekReport report; int streak;

  public ScoreForm(Settings settings, Action save, Action<string> open, Action<DateTime> logDay) : base("Placar da semana", 640, 780) {
   this.settings = settings; this.save = save; this.open = open; this.logDay = logDay;
   Put(Header(Score.Monday(DateTime.Now)), new TextEl { Text = "Montando o placar…", Color = Theme.Muted });
   SetFooter(new ButtonsEl().Add("Fechar", true, Close));
  }
  void Put(params El[] items) { Body.SetItems(items); }
  static TitleEl Header(DateTime monday) {
   return new TitleEl { Glyph = Theme.Star, Color = Theme.Amber, Title = "Placar da semana", Subtitle = monday.ToString("dd/MM") + " a " + monday.AddDays(4).ToString("dd/MM") + " · horas, entregas e qualidade" };
  }
  public void SetReport(WeekReport value, int weeks) { report = value; streak = weeks; Build(); }
  public void Fail(string text) { Put(Header(Score.Monday(DateTime.Now)), new TextEl { Text = text, Color = Theme.Red }); }

  static string Key(DateTime day) { return day.ToString("yyyy-MM-dd"); }
  bool Off(DateTime day) { return settings.DaysOff.Contains(Key(day)); }

  void Build() {
   DateTime now = DateTime.Now, monday = report.Monday;
   var items = new List<El> { Header(monday) };
   var days = Enumerable.Range(0, 5).Select(i => monday.AddDays(i)).ToList();
   var states = days.Select((d, i) => Score.DayState(d, report.Days[i], now, Off(d))).ToList();
   int counted = states.Count(s => s == "ok" || s == "short"), complete = states.Count(s => s == "ok");
   double journey = WorkDay.Windows.Sum(w => (w[1] - w[0]).TotalHours), expected = days.Count(d => !Off(d)) * journey;
   int firstPass = report.Finished.Count(f => f.Rejections == 0);

   var stats = new StatsEl { Columns = 4 };
   stats.Tiles.Add(new Tile { Glyph = Theme.CheckCircle, Color = Theme.Green, Value = report.Finished.Count.ToString(), Label = report.Finished.Count == 1 ? "finalizada" : "finalizadas" });
   stats.Tiles.Add(new Tile { Glyph = Theme.Bolt, Color = Theme.Amber, Value = firstPass + "/" + report.Finished.Count, Label = "de primeira na QA" });
   stats.Tiles.Add(new Tile { Glyph = Theme.Clock, Color = Theme.Accent, Value = WorkDay.Duration(report.Total), Label = "de " + WorkDay.Duration(expected) + " apontadas" });
   stats.Tiles.Add(new Tile { Glyph = Theme.Calendar, Color = counted > 0 && complete == counted ? Theme.Green : Theme.Blue, Value = complete + "/" + counted, Label = "dias completos" });
   items.Add(stats);

   items.Add(new SectionEl { Text = "Horas por dia", Right = "clique num dia para lançar horas ou marcar folga" });
   var week = new CardEl { Pad = 6, Dividers = false };
   for (int i = 0; i < 5; i++) {
    DateTime day = days[i]; string state = states[i]; double hours = report.Days[i];
    var bar = new BarEl {
     Label = Names[i] + " " + day.ToString("dd"), Value = state == "future" ? "" : WorkDay.Duration(hours), Fraction = state == "off" ? 0 : hours / journey,
     Color = state == "ok" ? Theme.Green : state == "short" ? Theme.Amber : Theme.Accent,
     Note = state == "ok" ? "completo" : state == "short" ? "faltou " + WorkDay.Duration(journey - hours) : state == "off" ? "folga" : state == "today" ? "hoje" : "",
     NoteColor = state == "ok" ? Theme.Green : state == "short" ? Theme.Amber : Theme.Muted };
    if (state != "future") { DateTime target = day; bar.Click = () => DayMenu(target); }
    week.Add(bar);
   }
   items.Add(week);

   items.Add(new SectionEl { Text = "Finalizadas na semana", Right = report.Finished.Count > 0 ? "clique para abrir no Redmine" : "" });
   var done = new CardEl { Pad = 6 };
   if (report.Finished.Count == 0) done.Add(new TextEl { Text = "Nenhuma ainda. A próxima vem com confete.", Color = Theme.Muted, Inset = 10, Before = 0 });
   var withEstimate = report.Finished.Where(f => f.Estimated > 0).ToList();
   IssueInfo fastest = withEstimate.Count > 1 ? withEstimate.OrderBy(f => f.Spent / f.Estimated).First() : null;
   foreach (var f in report.Finished.OrderByDescending(f => f.Closed)) {
    string hours = f.Estimated > 0 ? WorkDay.Duration(f.Spent) + " de " + WorkDay.Duration(f.Estimated) + " estimadas" : WorkDay.Duration(f.Spent) + " apontadas";
    if (f == fastest) hours += " · a mais rápida";
    string url = f.Url;
    done.Add(new RowEl {
     Glyph = Theme.CheckCircle, GlyphColor = Theme.Green, Title = "#" + f.Id + "  " + f.Subject, Subtitle = hours,
     Chip = f.Rejections == 0 ? "1ª na QA" : f.Rejections + (f.Rejections == 1 ? " rejeição" : " rejeições"), ChipColor = f.Rejections == 0 ? Theme.Amber : Theme.Muted,
     Right = f.Closed.HasValue ? f.Closed.Value.ToLocalTime().ToString("ddd HH:mm", new System.Globalization.CultureInfo("pt-BR")) : "", Click = () => open(url) });
   }
   items.Add(done);

   var banner = new CardEl { Pad = 6, Tint = streak > 0 ? Theme.Amber : Theme.Muted, Before = 16 };
   banner.Add(new RowEl {
    Glyph = Theme.Bolt, GlyphColor = streak > 0 ? Theme.Amber : Theme.Muted, Before = 0,
    Title = streak > 0 ? streak + (streak == 1 ? " semana" : " semanas") + " sem rejeição" : "A sequência recomeça na próxima semana",
    Subtitle = streak > 0 ? "Siga assim: cada semana sem tarefa voltando da QA aumenta a sequência." : "Teve rejeição nesta semana. Faz parte; a próxima conta do zero." });
   items.Add(banner);
   Body.SetItems(items);
  }
  void DayMenu(DateTime day) {
   bool off = Off(day);
   ShowMenu(Cursor.Position,
    new KeyValuePair<string,Action>("Lançar horas de " + day.ToString("dd/MM") + " no Redmine", () => logDay(day)),
    new KeyValuePair<string,Action>(off ? "Desmarcar folga" : "Marcar como folga (feriado, férias)", () => {
     if (off) settings.DaysOff.Remove(Key(day)); else settings.DaysOff.Add(Key(day));
     save(); Build();
    }));
  }
 }
}
