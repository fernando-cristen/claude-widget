namespace MeuClaudeWidget {
 using System;
 using System.Collections;
 using System.Collections.Generic;
 using System.Globalization;
 using System.IO;
 using System.Linq;
 using System.Net;
 using System.Net.Http;
 using System.Text;
 using System.Threading.Tasks;

 // A stretch of time an issue spent in "Em andamento", in local time.
 public class StatusSpan { public DateTime Start, End; }
 public class StatusChange { public DateTime When; public string Old = "", New = ""; }
 public class LogItem {
  public int Issue; public string Subject = ""; public DateTime Since;
  public double Raw, Logged, Hours; public bool Posted;
 }

 // Workday 08:00–12:00 and 13:12–18:00 (local time), Monday to Friday. Runs at 17:55.
 public static class WorkDay {
  public static readonly TimeSpan[][] Windows = {
   new TimeSpan[] { new TimeSpan(8,0,0), new TimeSpan(12,0,0) },
   new TimeSpan[] { new TimeSpan(13,12,0), new TimeSpan(18,0,0) }
  };
  public static readonly TimeSpan RunAt = new TimeSpan(17,55,0);
  public const string InProgress = "2";
  public const int Activity = 9; // Desenvolvimento
  public static bool IsWorkday(DateTime d) { return d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday; }
  public static DateTime DayEnd(DateTime day) { return day.Date + Windows[Windows.Length-1][1]; }
  public static bool Due(DateTime now, string lastRun) {
   return IsWorkday(now) && now.TimeOfDay >= RunAt && lastRun != now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  }

  // Rebuilds the "Em andamento" spans from the status changes in the issue history.
  // A span still open (issue still in progress) ends at openEnd.
  public static List<StatusSpan> Spans(IEnumerable<StatusChange> changes, DateTime created, string currentStatus, DateTime openEnd) {
   var list = new List<StatusSpan>(); DateTime? start = null; bool first = true;
   foreach (var c in changes.OrderBy(x => x.When)) {
    if (first && c.Old == InProgress) start = created; // issue was created already in progress
    first = false;
    if (c.New == InProgress && c.Old != InProgress) { if (!start.HasValue) start = c.When; }
    else if (c.Old == InProgress && c.New != InProgress) { if (start.HasValue && c.When > start.Value) list.Add(new StatusSpan { Start = start.Value, End = c.When }); start = null; }
   }
   if (first && currentStatus == InProgress) start = created;
   if (start.HasValue && currentStatus == InProgress && openEnd > start.Value) list.Add(new StatusSpan { Start = start.Value, End = openEnd });
   return list;
  }

  // Hours of each issue inside the day's windows. When two issues overlap,
  // the time goes to the one that entered "Em andamento" last, so no minute counts twice.
  public static Dictionary<int,double> Hours(Dictionary<int,List<StatusSpan>> spans, DateTime day) {
   var result = spans.Keys.ToDictionary(k => k, k => 0.0);
   var cuts = new List<DateTime>();
   foreach (var w in Windows) { cuts.Add(day.Date + w[0]); cuts.Add(day.Date + w[1]); }
   foreach (var l in spans.Values) foreach (var s in l) { cuts.Add(s.Start); cuts.Add(s.End); }
   cuts = cuts.Distinct().OrderBy(x => x).ToList();
   for (int i = 0; i + 1 < cuts.Count; i++) {
    DateTime a = cuts[i], b = cuts[i+1], mid = a + TimeSpan.FromTicks((b - a).Ticks / 2);
    if (!InWindow(mid, day)) continue;
    bool found = false; int owner = 0; DateTime latest = DateTime.MinValue;
    foreach (var pair in spans) foreach (var s in pair.Value)
     if (s.Start <= mid && mid < s.End && (!found || s.Start >= latest)) { found = true; latest = s.Start; owner = pair.Key; }
    if (found) result[owner] += (b - a).TotalHours;
   }
   return result;
  }
  static bool InWindow(DateTime t, DateTime day) {
   foreach (var w in Windows) if (t >= day.Date + w[0] && t < day.Date + w[1]) return true;
   return false;
  }
  // Nearest half hour: 15min → 0,5h; under 15min → 0 (nothing is logged).
  public static double Round(double hours) { return hours <= 0 ? 0 : Math.Floor(hours * 2 + 0.5) / 2; }
  public static string Duration(double hours) {
   int minutes = (int)Math.Round(hours * 60); return (minutes / 60) + "h" + (minutes % 60).ToString("00");
  }
  public static string Amount(double hours) { return hours.ToString("0.0#", CultureInfo.GetCultureInfo("pt-BR")) + "h"; }
 }

 // Talks to the Redmine configured for the Redmine MCP server in mcp-config.json.
 // The API key is read on each use and never copied, saved or shown.
 public sealed class RedmineClient : IDisposable {
  readonly HttpClient http; readonly Uri root; readonly string key;
  RedmineClient(Uri root, string key) {
   this.root = root; this.key = key;
   ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
   http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
   http.Timeout = TimeSpan.FromSeconds(25);
  }
  public string Host { get { return root.Host; } }
  public string IssueUrl(int id) { return new Uri(root, "issues/" + id).ToString(); }
  // Returns null when no Redmine MCP server is configured: the feature then stays silent.
  public static RedmineClient FromMcpConfig() {
   var folders = new List<string>();
   string custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
   if (!String.IsNullOrWhiteSpace(custom)) folders.Add(custom);
   folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"));
   string path = folders.Select(f => Path.Combine(f, "mcp-config.json")).FirstOrDefault(File.Exists);
   if (path == null) return null;
   var server = Data.Map(Data.Get(Data.Map(Data.Get(Data.Map(Data.Json().DeserializeObject(File.ReadAllText(path))), "mcpServers")), "redmine"));
   if (server == null) return null;
   var env = Data.Map(Data.Get(server, "env"));
   string url = Data.Str(Data.Get(env, "REDMINE_URL")).Trim(), secret = Data.Str(Data.Get(env, "REDMINE_API_KEY")).Trim();
   Uri uri;
   if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out uri) || uri.Scheme != "https") throw new RequestFailure("REDMINE_URL ausente ou sem https em mcp-config.json.", 0);
   if (secret.Length == 0 || secret.IndexOfAny(new char[] { '\r', '\n' }) >= 0) throw new RequestFailure("REDMINE_API_KEY ausente ou inválida em mcp-config.json.", 0);
   return new RedmineClient(uri, secret);
  }
  public Task<Dictionary<string,object>> Get(string path) { return Send(HttpMethod.Get, path, null); }
  public Task<Dictionary<string,object>> Post(string path, object body) { return Send(HttpMethod.Post, path, body); }
  async Task<Dictionary<string,object>> Send(HttpMethod method, string path, object body) {
   var uri = new Uri(root, path.TrimStart('/'));
   if (uri.Host != root.Host || uri.Scheme != "https") throw new InvalidOperationException("Destino não permitido.");
   using (var request = new HttpRequestMessage(method, uri)) {
    request.Headers.TryAddWithoutValidation("Accept", "application/json");
    request.Headers.TryAddWithoutValidation("User-Agent", "MeuClaudeWidget/1.0");
    request.Headers.TryAddWithoutValidation("X-Redmine-API-Key", key);
    if (body != null) request.Content = new StringContent(Data.Json().Serialize(body), new UTF8Encoding(false), "application/json");
    using (var response = await http.SendAsync(request)) {
     int code = (int)response.StatusCode;
     string text = await response.Content.ReadAsStringAsync();
     if (code == 401) throw new RequestFailure("Redmine recusou a chave de API (401). Confira REDMINE_API_KEY em mcp-config.json.", 0);
     if (code == 403) throw new RequestFailure("Redmine negou acesso (403).", 0);
     if (code == 422) throw new RequestFailure("Redmine recusou o apontamento: " + Errors(text), 0);
     if (!response.IsSuccessStatusCode) throw new RequestFailure("Redmine indisponível (HTTP " + code + ").", 0);
     if (text.Trim().Length == 0) return new Dictionary<string,object>();
     var map = Data.Map(Data.Json().DeserializeObject(text));
     if (map == null) throw new RequestFailure("Resposta inesperada do Redmine.", 0);
     return map;
    }
   }
  }
  static string Errors(string text) {
   try { return String.Join("; ", Items(Data.Get(Data.Map(Data.Json().DeserializeObject(text)), "errors")).Select(Data.Str)); } catch { return "erro de validação"; }
  }
  public static object[] Items(object value) {
   var array = value as object[]; if (array != null) return array;
   var list = value as ArrayList; if (list != null) return list.ToArray();
   return new object[0];
  }
  public void Dispose() { http.Dispose(); }
 }

 public static class AutoLog {
  static DateTime Local(object value) {
   return DateTimeOffset.Parse(Data.Str(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToLocalTime().DateTime;
  }
  static double Number(object value) {
   double n; return Double.TryParse(Data.Str(value), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 0;
  }
  // What would be logged today for the issues assigned to me that are in "Em andamento".
  // Open spans run until 18:00; hours already logged today on the issue are discounted.
  public static async Task<List<LogItem>> Plan(RedmineClient api, DateTime now) {
   DateTime day = now.Date; string today = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   var list = await api.Get("/issues.json?assigned_to_id=me&status_id=" + WorkDay.InProgress + "&limit=100");
   var spans = new Dictionary<int,List<StatusSpan>>(); var items = new List<LogItem>();
   foreach (var raw in RedmineClient.Items(Data.Get(list, "issues"))) {
    int id = (int)Number(Data.Get(Data.Map(raw), "id")); if (id <= 0) continue;
    var issue = Data.Map(Data.Get(await api.Get("/issues/" + id + ".json?include=journals"), "issue"));
    var changes = new List<StatusChange>();
    foreach (var j in RedmineClient.Items(Data.Get(issue, "journals"))) {
     var journal = Data.Map(j); DateTime when = Local(Data.Get(journal, "created_on"));
     foreach (var d in RedmineClient.Items(Data.Get(journal, "details"))) {
      var detail = Data.Map(d);
      if (Data.Str(Data.Get(detail, "property")) == "attr" && Data.Str(Data.Get(detail, "name")) == "status_id")
       changes.Add(new StatusChange { When = when, Old = Data.Str(Data.Get(detail, "old_value")), New = Data.Str(Data.Get(detail, "new_value")) });
     }
    }
    string status = Data.Str(Data.Get(Data.Map(Data.Get(issue, "status")), "id"));
    var s = WorkDay.Spans(changes, Local(Data.Get(issue, "created_on")), status, WorkDay.DayEnd(day));
    if (s.Count == 0) continue;
    spans[id] = s;
    items.Add(new LogItem { Issue = id, Subject = Data.Str(Data.Get(issue, "subject")), Since = s[s.Count-1].Start });
   }
   var hours = WorkDay.Hours(spans, day);
   foreach (var item in items) {
    item.Raw = hours[item.Issue];
    // Some Redmine servers ignore from/to/spent_on filters, so the date is checked here.
    var entries = await api.Get("/time_entries.json?user_id=me&issue_id=" + item.Issue + "&limit=100");
    foreach (var e in RedmineClient.Items(Data.Get(entries, "time_entries"))) {
     var entry = Data.Map(e);
     if ((int)Number(Data.Get(Data.Map(Data.Get(entry, "issue")), "id")) == item.Issue && Data.Str(Data.Get(entry, "spent_on")) == today)
      item.Logged += Number(Data.Get(entry, "hours"));
    }
    item.Hours = WorkDay.Round(item.Raw - item.Logged);
   }
   return items;
  }
  public static async Task<List<LogItem>> Run(RedmineClient api, DateTime now) {
   var items = await Plan(api, now); string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   foreach (var item in items.Where(x => x.Hours > 0)) {
    var entry = new Dictionary<string,object> { { "issue_id", item.Issue }, { "spent_on", today }, { "hours", item.Hours }, { "activity_id", WorkDay.Activity }, { "comments", "" } };
    await api.Post("/time_entries.json", new Dictionary<string,object> { { "time_entry", entry } });
    item.Posted = true;
   }
   return items;
  }
  public static string Since(LogItem item, DateTime now) {
   return item.Since.Date == now.Date ? item.Since.ToString("HH:mm") : item.Since.ToString("dd/MM HH:mm");
  }
  public static string Describe(List<LogItem> items, DateTime now) {
   if (items.Count == 0) return "Nenhuma tarefa sua em andamento agora. Nada seria apontado.";
   var text = new StringBuilder();
   foreach (var item in items) {
    string subject = item.Subject.Length > 70 ? item.Subject.Substring(0, 70) + "…" : item.Subject;
    text.AppendLine("#" + item.Issue + " · " + subject);
    text.AppendLine("   Em andamento desde " + Since(item, now) + " · hoje até 18:00: " + WorkDay.Duration(item.Raw));
    if (item.Logged > 0) text.AppendLine("   Já apontado hoje nesta tarefa: " + WorkDay.Amount(item.Logged));
    text.AppendLine("   → " + (item.Hours > 0 ? "aponta " + WorkDay.Amount(item.Hours) + " (Desenvolvimento)" : "nada a apontar"));
    text.AppendLine();
   }
   return text.ToString().TrimEnd();
  }
 }
}
