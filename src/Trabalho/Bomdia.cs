namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Globalization;
 using System.IO;
 using System.Linq;
 using System.Security.Cryptography;
 using System.Text;
 using System.Text.RegularExpressions;
 using System.Threading.Tasks;
 using System.Windows.Forms;

 public class SessionRecap {
  public string Id = "", Project = "", Cwd = "", Asked = "", Answered = "", Git = "";
  public int Issue; public bool Open; public DateTimeOffset Last;
 }
 public class TaskRecap { public int Id; public string Subject = "", Status = "", Url = ""; public double Hours; }
 public class NewsItem { public int Id; public string Kind = "", Title = "", Detail = "", Author = "", Url = ""; } // rejected, assigned, finished, status, note, new
 public class Briefing {
  public DateTime Day; // last workday summarized
  public List<TaskRecap> Tasks = new List<TaskRecap>();
  public List<SessionRecap> Sessions = new List<SessionRecap>();
  public List<NewsItem> News = new List<NewsItem>();
  public string Usage = "", RedmineError = "", Name = "";
 }

 // "Bom dia": where I stopped on the last workday (Claude sessions, Git, Redmine hours) and what changed overnight.
 // Transcripts are read locally, only their tail; nothing leaves the computer except the Redmine API calls.
 public static class Recap {
  public static readonly TimeSpan From = new TimeSpan(7,30,0), Until = new TimeSpan(12,0,0);
  public static bool Due(DateTime now, string shown) {
   return WorkDay.IsWorkday(now) && now.TimeOfDay >= From && now.TimeOfDay < Until && shown != now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  }
  public static DateTime PreviousWorkday(DateTime today) {
   DateTime d = today.Date.AddDays(-1); while (!WorkDay.IsWorkday(d)) d = d.AddDays(-1); return d;
  }
  static string Hash(string value) {
   using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
  }
  static string ClaudeFolder() {
   string folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
   return String.IsNullOrWhiteSpace(folder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude") : folder;
  }
  // Markdown and markup out, whitespace collapsed, cut at a word boundary.
  public static string Clean(string text, int max) {
   if (String.IsNullOrEmpty(text)) return "";
   text = Regex.Replace(text, "<system-reminder>[\\s\\S]*?</system-reminder>", " ");
   text = Regex.Replace(text, "```[\\s\\S]*?```", " ");
   text = Regex.Replace(text, "</?[A-Za-z][A-Za-z0-9_-]*[^<>\\n]{0,80}>?", " ");
   var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("|") && !l.StartsWith("<") && !Regex.IsMatch(l, "^[-=*_]{3,}$"));
   text = String.Join(" ", lines);
   text = Regex.Replace(text, "[#*`>]+", "");
   text = Regex.Replace(text, "\\s+", " ").Trim();
   if (text.Length <= max) return text;
   int cut = text.LastIndexOf(' ', max); if (cut < max / 2) cut = max;
   return text.Substring(0, cut).TrimEnd(',', '.', ':', ';', ' ') + "…";
  }
  static string Typed(object content) {
   var direct = content as string; if (direct != null) return direct;
   return String.Join("\n", RedmineClient.Items(content).Select(Data.Map).Where(m => Data.Str(Data.Get(m, "type")) == "text").Select(m => Data.Str(Data.Get(m, "text"))));
  }
  // Last prompt typed by the user and the last text Claude answered after it.
  public static void ReadTranscript(string path, SessionRecap into) {
   string text;
   using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
    long start = Math.Max(0, stream.Length - 4194304); stream.Position = start;
    var bytes = new byte[stream.Length - start]; int read = 0, n;
    while (read < bytes.Length && (n = stream.Read(bytes, read, bytes.Length - read)) > 0) read += n;
    text = Encoding.UTF8.GetString(bytes, 0, read);
    if (start > 0) { int first = text.IndexOf('\n'); text = first < 0 ? "" : text.Substring(first + 1); }
   }
   var json = Data.Json();
   foreach (var line in text.Split('\n')) {
    if (line.IndexOf("\"isSidechain\":true", StringComparison.Ordinal) >= 0) continue;
    bool user = line.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"tool_result\"", StringComparison.Ordinal) < 0;
    bool assistant = line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"type\":\"text\"", StringComparison.Ordinal) >= 0;
    if (!user && !assistant) continue;
    Dictionary<string,object> entry; try { entry = Data.Map(json.DeserializeObject(line)); } catch { continue; }
    if (entry == null || Data.Get(entry, "isMeta") is bool && (bool)Data.Get(entry, "isMeta")) continue;
    string said = Typed(Data.Get(Data.Map(Data.Get(entry, "message")), "content")).Trim();
    if (said.Length == 0) continue;
    if (user) {
     if (said.StartsWith("<command") || said.StartsWith("<local-command") || said.StartsWith("[Request interrupted") || said.StartsWith("Caveat:")) continue;
     string clean = Clean(said, 400); if (clean.Length == 0) continue;
     into.Asked = clean; into.Answered = "";
    } else into.Answered = said;
   }
   into.Answered = Clean(into.Answered, 320);
  }
  // Claude Code sessions with activity since "since" (radar records), newest first, one per project+issue.
  public static List<SessionRecap> Sessions(DateTime since, int max) {
   var result = new List<SessionRecap>();
   string radar = Path.Combine(Data.Folder, "radar");
   if (!Directory.Exists(radar)) return result;
   var found = new List<SessionInfo>();
   foreach (var file in new DirectoryInfo(radar).GetFiles("*.json").Where(f => f.LastWriteTime >= since)) {
    try {
     var s = Data.Json().Deserialize<SessionInfo>(File.ReadAllText(file.FullName));
     if (s == null || String.IsNullOrWhiteSpace(s.Id) || String.IsNullOrWhiteSpace(s.Cwd) || s.Time.LocalDateTime < since) continue;
     string status = Path.Combine(radar, "status", s.Id + ".json");
     if (File.Exists(status)) try { var r = Data.Json().Deserialize<StatusRecord>(File.ReadAllText(status)); if (r != null) s.Issue = r.Issue; } catch { }
     found.Add(s);
    } catch { }
   }
   // Transcript file names are the session ids; the radar keeps their hash.
   var transcripts = new Dictionary<string,string>();
   string projects = Path.Combine(ClaudeFolder(), "projects");
   if (Directory.Exists(projects))
    foreach (var dir in new DirectoryInfo(projects).GetDirectories())
     foreach (var file in dir.GetFiles("*.jsonl").Where(f => f.LastWriteTime >= since)) transcripts[Hash(Path.GetFileNameWithoutExtension(file.Name))] = file.FullName;
   foreach (var group in found.OrderByDescending(s => s.Time).GroupBy(s => s.Cwd.ToLowerInvariant() + "#" + s.Issue).Take(max)) {
    var s = group.First();
    var recap = new SessionRecap { Id = s.Id, Project = s.Project, Cwd = s.Cwd, Issue = s.Issue, Last = s.Time, Open = s.State != "ended" && RadarReader.Alive(s) };
    string path; if (transcripts.TryGetValue(s.Id, out path)) try { ReadTranscript(path, recap); } catch { }
    var git = GitReader.Read(s.Cwd);
    recap.Git = git.Error.Length > 0 ? "" : "branch " + git.Branch + " · " + (git.Files == 0 ? "tudo commitado" : git.Files + (git.Files == 1 ? " arquivo não commitado" : " arquivos não commitados"));
    result.Add(recap);
   }
   return result;
  }

  public static async Task<Briefing> Build(RedmineClient api, List<int> active, string usage, DateTime now) {
   var b = new Briefing { Day = PreviousWorkday(now), Usage = usage };
   if (api != null) {
    try {
     var entries = await Score.Entries(api, now.Date.AddDays(-90));
     var before = entries.Where(e => e.Day < now.Date).ToList();
     if (before.Count > 0) b.Day = before.Max(e => e.Day);
     var statuses = new Dictionary<string,string>();
     foreach (var raw in RedmineClient.Items(Data.Get(await api.Get("/issue_statuses.json"), "issue_statuses"))) { var m = Data.Map(raw); statuses[Data.Str(Data.Get(m, "id"))] = Data.Str(Data.Get(m, "name")); }
     Func<string,string> name = id => { string v; return statuses.TryGetValue(id, out v) ? v : id; };
     // Hours of the last workday, plus the issues still in progress.
     var hours = before.Where(e => e.Day == b.Day).GroupBy(e => e.Issue).ToDictionary(g => g.Key, g => g.Sum(e => e.Hours));
     var ids = hours.Keys.Concat(active).Distinct().ToList();
     foreach (var info in await Score.Issues(api, ids)) {
      double h; hours.TryGetValue(info.Id, out h);
      b.Tasks.Add(new TaskRecap { Id = info.Id, Subject = info.Subject, Status = name(info.Status), Url = info.Url, Hours = h });
     }
     b.Tasks = b.Tasks.OrderByDescending(t => t.Hours).ToList();
     // What others changed since the end of that day.
     var since = new DateTimeOffset(b.Day.Date + WorkDay.DayEnd(b.Day).TimeOfDay);
     var user = Data.Map(Data.Get(await api.Get("/users/current.json"), "user"));
     int me = (int)AutoLog.Number(Data.Get(user, "id")); b.Name = Data.Str(Data.Get(user, "firstname"));
     var assigned = RedmineClient.Items(Data.Get(await api.Get("/issues.json?assigned_to_id=me&status_id=*&sort=updated_on:desc&limit=25"), "issues")).Select(Data.Map)
      .Where(i => Parse(Data.Get(i, "updated_on")) > since).ToList();
     foreach (var issue in assigned) {
      int id = (int)AutoLog.Number(Data.Get(issue, "id")); string subject = Clean(Data.Str(Data.Get(issue, "subject")), 70);
      var full = Data.Map(Data.Get(await api.Get("/issues/" + id + ".json?include=journals"), "issue"));
      bool any = false;
      foreach (var j in RedmineClient.Items(Data.Get(full, "journals")).Select(Data.Map)) {
       var who = Data.Map(Data.Get(j, "user"));
       if (Parse(Data.Get(j, "created_on")) <= since || (int)AutoLog.Number(Data.Get(who, "id")) == me) continue;
       string author = Data.Str(Data.Get(who, "name")).Split(' ')[0], notes = Clean(Data.Str(Data.Get(j, "notes")), 160);
       var details = RedmineClient.Items(Data.Get(j, "details")).Select(Data.Map).ToList();
       var status = details.FirstOrDefault(d => Data.Str(Data.Get(d, "name")) == "status_id");
       bool toMe = details.Any(d => Data.Str(Data.Get(d, "name")) == "assigned_to_id" && (int)AutoLog.Number(Data.Get(d, "new_value")) == me);
       var item = new NewsItem { Id = id, Url = api.IssueUrl(id), Author = author };
       if (status != null && Data.Str(Data.Get(status, "new_value")) == Score.Rejected) { item.Kind = "rejected"; item.Title = "#" + id + " voltou rejeitada"; item.Detail = notes.Length > 0 ? author + ": " + notes : "por " + author; }
       else if (toMe) { item.Kind = "assigned"; item.Title = "#" + id + " atribuída a você"; item.Detail = subject + " · por " + author; }
       else if (status != null) { item.Kind = "status"; item.Title = "#" + id + "  " + name(Data.Str(Data.Get(status, "old_value"))) + " → " + name(Data.Str(Data.Get(status, "new_value"))); item.Detail = subject + " · por " + author; }
       else if (notes.Length > 0) { item.Kind = "note"; item.Title = author + " comentou na #" + id; item.Detail = notes; }
       else continue;
       b.News.Add(item); any = true;
      }
      if (!any && Parse(Data.Get(issue, "created_on")) > since) b.News.Add(new NewsItem { Id = id, Kind = "new", Url = api.IssueUrl(id), Title = "Nova tarefa #" + id, Detail = subject });
     }
     foreach (var f in (await Score.FinishedAmong(api, entries.Select(e => e.Issue))).Where(f => f.Closed.Value > since))
      b.News.Add(new NewsItem { Id = f.Id, Kind = "finished", Url = f.Url, Title = "#" + f.Id + " finalizada", Detail = Clean(f.Subject, 90) });
     var order = new[] { "rejected", "assigned", "new", "finished", "status", "note" };
     b.News = b.News.OrderBy(n => Array.IndexOf(order, n.Kind)).ToList();
    } catch (Exception e) { b.RedmineError = e.Message; }
   }
   var day = b.Day;
   b.Sessions = await Task.Run(() => Sessions(day, 3));
   return b;
  }
  static DateTimeOffset Parse(object value) {
   DateTimeOffset t; return DateTimeOffset.TryParse(Data.Str(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out t) ? t : DateTimeOffset.MinValue;
  }
  // Short text for the morning notification (Windows limits it to a few lines).
  public static string Toast(Briefing b) {
   var lines = new List<string>();
   var s = b.Sessions.FirstOrDefault(x => x.Asked.Length > 0) ?? b.Sessions.FirstOrDefault();
   if (s != null) lines.Add("Parou em " + s.Project + (s.Issue > 0 ? " · #" + s.Issue : "") + (s.Asked.Length > 0 ? ": " + Clean(s.Asked, 70) : ""));
   var worked = b.Tasks.Where(t => t.Hours > 0).Take(3).ToList();
   if (worked.Count > 0) lines.Add(DayName(b.Day) + ": " + String.Join(", ", worked.Select(t => "#" + t.Id + " " + WorkDay.Duration(t.Hours))));
   int rejected = b.News.Count(n => n.Kind == "rejected");
   lines.Add(b.News.Count == 0 ? "Nada novo no Redmine." : b.News.Count + (b.News.Count == 1 ? " novidade" : " novidades") + " no Redmine" + (rejected > 0 ? " (" + rejected + (rejected == 1 ? " rejeição)" : " rejeições)") : "") + ".");
   lines.Add("Clique para ver o resumo.");
   string text = String.Join("\n", lines);
   return text.Length > 250 ? text.Substring(0, 249) + "…" : text;
  }
  public static string DayName(DateTime day) {
   return day.ToString("dddd dd/MM", CultureInfo.GetCultureInfo("pt-BR"));
  }
 }

 public sealed class BriefingForm : PremiumWindow {
  readonly Action<string> open, focus;
  public Action Reload;
  public BriefingForm(Action<string> openUrl, Action<string> focusSession) : base("Bom dia", 640, 760) {
   open = openUrl; focus = focusSession;
   Body.SetItems(new El[] { Header("Bom dia!", "Montando o resumo de ontem…"), new TextEl { Text = "Lendo as sessões do Claude, o Git e o Redmine.", Color = Theme.Muted } });
   SetFooter(new ButtonsEl().Add("Bora trabalhar", true, Close));
  }
  static TitleEl Header(string title, string subtitle) { return new TitleEl { Glyph = Theme.Sun, Color = Theme.Amber, Title = title, Subtitle = subtitle }; }
  public void Fail(string text) { Body.SetItems(new El[] { Header("Bom dia!", "Não deu para montar o resumo"), new TextEl { Text = text, Color = Theme.Red } }); }
  static string Glyph(string kind) {
   switch (kind) { case "rejected": return Theme.Error; case "assigned": return Theme.Person; case "new": return Theme.Add; case "finished": return Theme.CheckCircle; case "status": return Theme.Sync; default: return Theme.Chat; }
  }
  static System.Drawing.Color Tone(string kind) {
   switch (kind) { case "rejected": return Theme.Red; case "assigned": case "new": return Theme.Amber; case "finished": return Theme.Green; case "status": return Theme.Blue; default: return Theme.Soft; }
  }
  public void SetBriefing(Briefing b) {
   var items = new List<El>();
   string day = Recap.DayName(b.Day), shortDay = day.Split('-', ' ')[0];
   items.Add(Header(b.Name.Length > 0 ? "Bom dia, " + b.Name + "!" : "Bom dia!", "Resumo de " + day + " e do que mudou desde então"));

   var stats = new StatsEl { Columns = 3 };
   stats.Tiles.Add(new Tile { Glyph = Theme.Clock, Color = Theme.Accent, Value = WorkDay.Duration(b.Tasks.Sum(t => t.Hours)), Label = "apontadas na " + shortDay });
   stats.Tiles.Add(new Tile { Glyph = Theme.Flag, Color = b.News.Any(n => n.Kind == "rejected") ? Theme.Red : Theme.Amber, Value = b.News.Count.ToString(), Label = b.News.Count == 1 ? "novidade no Redmine" : "novidades no Redmine" });
   stats.Tiles.Add(new Tile { Glyph = Theme.Terminal, Color = Theme.Blue, Value = b.Sessions.Count(s => s.Open).ToString(), Label = "sessões abertas" });
   items.Add(stats);

   items.Add(new SectionEl { Text = "Onde você parou" });
   if (b.Sessions.Count == 0) items.Add(new CardEl { Pad = 16 }.Add(new TextEl { Text = "Nenhuma sessão do Claude Code registrada desde " + day + ".", Color = Theme.Muted }));
   foreach (var s in b.Sessions) {
    var card = new CardEl { Pad = 6, Dividers = false };
    card.Add(new RowEl { Glyph = Theme.Terminal, GlyphColor = s.Open ? Theme.Green : Theme.Muted, Title = s.Project, Subtitle = s.Open ? "Sessão aberta" : "Sessão encerrada", Before = 0,
     Chip = s.Issue > 0 ? "#" + s.Issue : "", ChipColor = Theme.Accent, Right = s.Last.ToLocalTime().ToString("dd/MM HH:mm") });
    if (s.Asked.Length > 0) card.Add(new QuoteEl { Label = "Você pediu", Text = Recap.Clean(s.Asked, 240), Color = Theme.Accent, Inset = 14, Before = 2 });
    card.Add(new QuoteEl { Label = "Claude", Text = s.Answered.Length > 0 ? s.Answered : s.Asked.Length > 0 ? "Não chegou a responder esse pedido." : "Transcript não encontrado.", Color = Theme.Blue, TextColor = Theme.Soft, Inset = 14 });
    if (s.Git.Length > 0) card.Add(new TextEl { Text = s.Git, Font = Theme.F(Theme.Mono, 8.5f), Color = Theme.Muted, Inset = 14, Before = 12 });
    var actions = new ButtonsEl { Right = false, Inset = 14, Before = 12 };
    string id = s.Id; var task = b.Tasks.FirstOrDefault(t => t.Id == s.Issue);
    if (s.Open) actions.AddSmall("Abrir terminal", () => focus(id), Theme.Terminal);
    if (task != null) { string url = task.Url; actions.AddSmall("Abrir #" + task.Id, () => open(url), Theme.Open); }
    if (actions.Buttons.Count > 0) card.Add(actions);
    card.Add(new SpacerEl(8));
    items.Add(card);
   }

   items.Add(new SectionEl { Text = "Tarefas · " + day });
   var tasks = new CardEl { Pad = 6 };
   if (b.RedmineError.Length > 0) tasks.Add(new TextEl { Text = "Não foi possível ler o Redmine: " + b.RedmineError, Color = Theme.Red, Inset = 10, Before = 0 });
   else if (b.Tasks.Count == 0) tasks.Add(new TextEl { Text = "Nenhum apontamento nesse dia.", Color = Theme.Muted, Inset = 10, Before = 0 });
   foreach (var t in b.Tasks) {
    string url = t.Url;
    tasks.Add(new RowEl { Glyph = Theme.Code, GlyphColor = Theme.ForStatus(t.Status), Title = "#" + t.Id + "  " + t.Subject, Subtitle = t.Hours > 0 ? WorkDay.Duration(t.Hours) + " apontadas" : "sem horas nesse dia",
     Chip = t.Status, ChipColor = Theme.ForStatus(t.Status), Click = () => open(url) });
   }
   items.Add(tasks);

   items.Add(new SectionEl { Text = "Novidades no Redmine", Right = "desde " + shortDay + " às 18:00" });
   var news = new CardEl { Pad = 6 };
   if (b.RedmineError.Length == 0 && b.News.Count == 0) news.Add(new TextEl { Text = "Nada novo. Dia tranquilo pela frente.", Color = Theme.Muted, Inset = 10, Before = 0 });
   foreach (var n in b.News.Take(12)) {
    string url = n.Url;
    news.Add(new RowEl { Glyph = Glyph(n.Kind), GlyphColor = Tone(n.Kind), Title = n.Title, Subtitle = n.Detail, Click = () => open(url) });
   }
   items.Add(news);

   if (b.Usage.Length > 0) {
    items.Add(new SectionEl { Text = "Claude" });
    items.Add(new CardEl { Pad = 6 }.Add(new RowEl { Glyph = Theme.Sparkle, GlyphColor = Theme.Accent, Title = "Uso da assinatura", Subtitle = b.Usage, Before = 0 }));
   }
   Body.SetItems(items);
   var footer = new ButtonsEl { Hint = "Atualizado às " + DateTime.Now.ToString("HH:mm") };
   if (Reload != null) footer.Add("Atualizar", false, Reload, Theme.Sync);
   SetFooter(footer.Add("Bora trabalhar", true, Close));
  }
 }
}
