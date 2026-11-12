namespace MeuClaudeWidget {
 using System.Text.Json;
 using System.Text.RegularExpressions;

 // One Claude Code conversation, as found in its transcript (~/.claude/projects/<pasta>/<id>.jsonl).
 public sealed class HistorySession {
  public string Id = "", Cwd = "", Title = "", First = "", Last = "", Branch = "", File = "", Text = "";
  public DateTime Start, End; public int Prompts; public long Offset, Length;
  public List<int> Issues = new List<int>();
  public string Project { get { try { return String.IsNullOrEmpty(Cwd) ? "Sem pasta" : new DirectoryInfo(Cwd).Name; } catch { return Cwd; } } }
  public string Label { get { return Title.Length > 0 ? Title : First.Length > 0 ? First : "Conversa sem mensagens"; } }
  public int Issue { get { return Issues.Count > 0 ? Issues[Issues.Count - 1] : 0; } }
 }
 public sealed class HistoryIndex {
  public List<HistorySession> Sessions = new List<HistorySession>();
  public Dictionary<string, string> IssueTitles = new Dictionary<string, string>();
 }

 // Reads the transcripts once and then only the bytes added since; keeps what the search needs in historico.json.
 // Only your own typed messages are kept (no tool output, no Claude answers), plus the title Claude Code gives the
 // conversation. The Redmine issue of a conversation is the /issues/N link you pasted (same rule as the status line)
 // or a #N with five or more digits.
 public static class History {
  const int MaxText = 40000;
  static readonly object gate = new object();
  static readonly byte[] UserTag = Encoding.UTF8.GetBytes("\"type\":\"user\""), TitleTag = Encoding.UTF8.GetBytes("\"type\":\"ai-title\""),
   ToolTag = Encoding.UTF8.GetBytes("\"tool_result\""), MetaTag = Encoding.UTF8.GetBytes("\"isMeta\":true"), SideTag = Encoding.UTF8.GetBytes("\"isSidechain\":true");
  static readonly Regex Link = new Regex(@"/issues/(\d{3,8})", RegexOptions.Compiled), Hash = new Regex(@"(?<![\w/])#(\d{5,8})\b", RegexOptions.Compiled),
   Reminder = new Regex(@"<system-reminder>.*?</system-reminder>", RegexOptions.Singleline | RegexOptions.Compiled);

  public static string Root {
   get {
    string dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
    if (String.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    return Path.Combine(dir, "projects");
   }
  }
  static string IndexPath { get { return Path.Combine(Data.Folder, "historico.json"); } }

  public static HistoryIndex Load() {
   lock (gate) {
    try { if (File.Exists(IndexPath)) { var x = Data.Json().Deserialize<HistoryIndex>(File.ReadAllText(IndexPath)); if (x != null && x.Sessions != null) { if (x.IssueTitles == null) x.IssueTitles = new Dictionary<string, string>(); return x; } } } catch { }
    return new HistoryIndex();
   }
  }
  static void Save(HistoryIndex index) {
   lock (gate) {
    try {
     Directory.CreateDirectory(Data.Folder);
     string temp = IndexPath + ".tmp";
     File.WriteAllText(temp, new Json { MaxJsonLength = int.MaxValue }.Serialize(index), new UTF8Encoding(false));
     if (File.Exists(IndexPath)) File.Replace(temp, IndexPath, null); else File.Move(temp, IndexPath);
    } catch { }
   }
  }

  // Brings the index up to date. Unchanged files cost a stat; grown files are read from where they stopped.
  public static HistoryIndex Refresh(HistoryIndex index) {
   var known = index.Sessions.ToDictionary(s => s.File, StringComparer.OrdinalIgnoreCase);
   var fresh = new List<HistorySession>(); bool changed = false;
   if (Directory.Exists(Root))
    foreach (var dir in Directory.GetDirectories(Root))
     foreach (var file in Directory.GetFiles(dir, "*.jsonl")) {
      try {
       var info = new FileInfo(file); HistorySession s;
       if (!known.TryGetValue(file, out s) || info.Length < s.Offset) { s = new HistorySession { File = file, Id = Path.GetFileNameWithoutExtension(file) }; changed = true; }
       if (info.Length != s.Length) { Read(s, info.Length); changed = true; }
       s.End = info.LastWriteTimeUtc;
       if (s.Prompts > 0 || s.Title.Length > 0) fresh.Add(s);
      } catch { }
     }
   if (fresh.Count != index.Sessions.Count) changed = true;
   index.Sessions = fresh.OrderByDescending(s => s.End).ToList();
   if (changed) Save(index);
   return index;
  }

  // Reads the complete lines added since s.Offset; a line still being written is left for the next time.
  static void Read(HistorySession s, long length) {
   long size = length - s.Offset; if (size <= 0) { s.Length = length; return; }
   var bytes = new byte[size]; int got = 0;
   using (var f = new FileStream(s.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
    f.Seek(s.Offset, SeekOrigin.Begin);
    while (got < bytes.Length) { int n = f.Read(bytes, got, bytes.Length - got); if (n <= 0) break; got += n; }
   }
   int start = 0;
   for (int i = 0; i < got; i++) if (bytes[i] == (byte)'\n') { Line(s, new ReadOnlySpan<byte>(bytes, start, i - start)); start = i + 1; }
   s.Offset += start; s.Length = length;
  }

  static void Line(HistorySession s, ReadOnlySpan<byte> line) {
   bool user = line.IndexOf(UserTag) >= 0, title = !user && line.IndexOf(TitleTag) >= 0;
   if (!user && !title) return;
   if (user && (line.IndexOf(ToolTag) >= 0 || line.IndexOf(MetaTag) >= 0 || line.IndexOf(SideTag) >= 0)) return;
   try {
    using (var doc = JsonDocument.Parse(line.ToArray())) {
     var root = doc.RootElement; JsonElement v;
     if (title) { if (root.TryGetProperty("aiTitle", out v) && v.ValueKind == JsonValueKind.String) s.Title = Clean(v.GetString(), 120); return; }
     if (root.TryGetProperty("cwd", out v) && v.ValueKind == JsonValueKind.String && s.Cwd.Length == 0) s.Cwd = v.GetString();
     if (root.TryGetProperty("gitBranch", out v) && v.ValueKind == JsonValueKind.String && v.GetString().Length > 0) s.Branch = v.GetString();
     if (!root.TryGetProperty("message", out v) || !v.TryGetProperty("content", out v)) return;
     string text = "";
     if (v.ValueKind == JsonValueKind.String) text = v.GetString();
     else if (v.ValueKind == JsonValueKind.Array) foreach (var b in v.EnumerateArray()) { JsonElement t; if (b.TryGetProperty("type", out t) && t.GetString() == "text" && b.TryGetProperty("text", out t)) text += t.GetString() + "\n"; }
     text = Reminder.Replace(text ?? "", "").Trim();
     if (text.Length == 0 || text.StartsWith("<", StringComparison.Ordinal) || text.StartsWith("Caveat:", StringComparison.Ordinal)) return; // slash commands and their output
     DateTime when; if (root.TryGetProperty("timestamp", out v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when)) { if (s.Start == DateTime.MinValue) s.Start = when; }
     s.Prompts++;
     string one = Clean(text, 200);
     if (s.First.Length == 0) s.First = one;
     s.Last = one;
     foreach (Match m in Link.Matches(text)) Add(s, m.Groups[1].Value);
     foreach (Match m in Hash.Matches(text)) Add(s, m.Groups[1].Value);
     if (s.Text.Length < MaxText) s.Text += (s.Text.Length > 0 ? "\n" : "") + (text.Length > 2000 ? text.Substring(0, 2000) : text);
    }
   } catch { }
  }
  static void Add(HistorySession s, string number) { int id; if (Int32.TryParse(number, out id) && id > 0) { s.Issues.Remove(id); s.Issues.Add(id); } }
  public static string Clean(string text, int max) {
   text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
   return text.Length > max ? text.Substring(0, max - 1).TrimEnd() + "…" : text;
  }

  // ---- search: words in any order, accents and case ignored; "#123456" or a bare number finds the issue ----
  static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
  const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
  public static bool Has(string text, string word) { return !String.IsNullOrEmpty(text) && Compare.IndexOf(text, word, Loose) >= 0; }
  public static bool Matches(HistorySession s, string query, Dictionary<string, string> titles) {
   foreach (var raw in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
    string word = raw.TrimStart('#'); int number;
    if (Int32.TryParse(word, out number) && word.Length >= 3) { if (s.Issues.Contains(number)) continue; if (Has(s.Text, word)) continue; return false; }
    if (Has(s.Title, word) || Has(s.Project, word) || Has(s.Branch, word) || Has(s.Text, word)) continue;
    string t; if (s.Issues.Any(i => titles.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out t) && Has(t, word))) continue;
    return false;
   }
   return true;
  }
  // The part of the conversation around the first word found, for the result line.
  public static string Snippet(HistorySession s, string query) {
   foreach (var raw in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
    string word = raw.TrimStart('#'); if (word.Length < 2) continue;
    int at = Compare.IndexOf(s.Text ?? "", word, Loose); if (at < 0) continue;
    int from = Math.Max(0, at - 50), to = Math.Min(s.Text.Length, at + word.Length + 90);
    return (from > 0 ? "…" : "") + Clean(s.Text.Substring(from, to - from), 160) + (to < s.Text.Length ? "…" : "");
   }
   return "";
  }

  // Subjects of the linked issues, fetched once each and kept in the index.
  public static async Task<bool> FetchTitles(HistoryIndex index, IEnumerable<int> wanted) {
   var missing = wanted.Distinct().Where(i => !index.IssueTitles.ContainsKey(i.ToString(CultureInfo.InvariantCulture))).Take(100).ToList();
   if (missing.Count == 0) return false;
   using (var client = RedmineClient.FromMcpConfig()) {
    if (client == null) return false;
    for (int i = 0; i < missing.Count; i += 50) {
     var ids = missing.Skip(i).Take(50).ToList();
     var page = await client.Get("/issues.json?status_id=*&limit=100&issue_id=" + String.Join(",", ids));
     foreach (var item in RedmineClient.Items(Data.Get(page, "issues"))) {
      var issue = Data.Map(item); string id = Data.Str(Data.Get(issue, "id"));
      if (id.Length > 0) index.IssueTitles[id] = Data.Str(Data.Get(issue, "subject"));
     }
     foreach (var id in ids) { string key = id.ToString(CultureInfo.InvariantCulture); if (!index.IssueTitles.ContainsKey(key)) index.IssueTitles[key] = ""; } // not visible: do not ask again
    }
   }
   Save(index);
   return true;
  }

  // The radar names a session by the SHA-256 of its id (RadarHook.ps1); this is how a live session is matched.
  public static string RadarId(string sessionId) {
   using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sessionId))).Replace("-", "").ToLowerInvariant();
  }
 }
}
