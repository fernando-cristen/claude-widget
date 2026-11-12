namespace MeuClaudeWidget {
 using System.Diagnostics;

 // Session center: every Claude Code conversation on this computer, grouped by project, with the live state of
 // the open ones (radar) and a search over what you asked, the titles, branches and the linked Redmine issue.
 // From here a session is focused, resumed in a new terminal tab, or started fresh in a project folder.
 public sealed class CentralForm : PremiumWindow {
  sealed class Conv { public HistorySession H; public SessionInfo Live; public string Key { get { return H != null && H.File.Length > 0 ? H.File : "live:" + Live.Id; } } }
  readonly Func<List<SessionInfo>> live; readonly Action<string> focus, follow, openUrl; readonly Settings settings; readonly Action save;
  readonly Canvas side; readonly TextBox search; readonly Panel bar;
  readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 2000 }, typing = new System.Windows.Forms.Timer { Interval = 180 };
  HistoryIndex index = new HistoryIndex(); bool indexing = true, fetching; DateTime nextScan = DateTime.MinValue;
  string scope = "all", query = "", selected = ""; List<Conv> shown = new List<Conv>(); readonly Dictionary<string, El> rows = new Dictionary<string, El>();
  string redmineHost = "";

  public CentralForm(Func<List<SessionInfo>> live, Action<string> focus, Action<string> follow, Action<string> openUrl, Settings settings, Action save)
   : base("Sessões", 1040, 700) {
   this.live = live; this.focus = focus; this.follow = follow; this.openUrl = openUrl; this.settings = settings; this.save = save;
   MinimumSize = new Size(ui.P(760), ui.P(460)); MaximizeBox = true; MinimizeBox = true;
   if (settings.PinnedProjects == null) settings.PinnedProjects = new List<string>();
   try { using (var c = RedmineClient.FromMcpConfig()) if (c != null) redmineHost = new Uri(c.IssueUrl(1)).Host; } catch { }

   // Search across the top.
   bar = new Panel { Dock = DockStyle.Top, Height = ui.P(64), BackColor = Theme.Bg };
   search = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Ink, Font = Theme.F(Theme.Sans, 10.5f), PlaceholderText = "Buscar no que você pediu, em projetos, branches ou #atividade" };
   bar.Controls.Add(search);
   bar.Resize += delegate { search.SetBounds(ui.P(44), (bar.Height - search.PreferredHeight) / 2, bar.Width - ui.P(80), search.PreferredHeight); };
   bar.Paint += (s, e) => {
    var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    var box = new Rectangle(ui.P(16), ui.P(14), bar.Width - ui.P(32), bar.Height - ui.P(28));
    Draw.Fill(g, box, ui.P(8), Theme.Surface); if (search.Focused) Draw.Stroke(g, box, ui.P(8), Theme.Raised);
    Draw.Glyph(g, "", 10, new Rectangle(box.X + ui.P(8), box.Y, ui.P(18), box.Height), Theme.Muted); // magnifier
    using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
   };
   search.GotFocus += delegate { bar.Invalidate(); }; search.LostFocus += delegate { bar.Invalidate(); };
   search.TextChanged += delegate { typing.Stop(); typing.Start(); };
   typing.Tick += delegate { typing.Stop(); query = search.Text.Trim(); selected = ""; Rebuild(true); };

   side = new Canvas(ui) { Dock = DockStyle.Left, Width = ui.P(248), BackColor = Theme.Bg };
   side.Inset = new Padding(ui.P(12), ui.P(14), ui.P(12), ui.P(16));
   var divider = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Line };
   Body.Inset = new Padding(ui.P(20), ui.P(18), ui.P(24), ui.P(20));
   Controls.Add(bar); Controls.Add(side); Controls.Add(divider);
   Controls.SetChildIndex(Body, 0); Controls.SetChildIndex(divider, 1); Controls.SetChildIndex(side, 2); Controls.SetChildIndex(bar, 3); Controls.SetChildIndex(Footer, 4);

   clock.Tick += delegate { Rebuild(false); if (DateTime.Now >= nextScan) Scan(); };
   Shown += delegate {
    index = History.Load(); indexing = index.Sessions.Count == 0;
    Rebuild(true); Scan(); clock.Start(); search.Focus();
   };
   FormClosed += delegate { clock.Stop(); clock.Dispose(); typing.Stop(); typing.Dispose(); };
  }

  public bool Busy { get { return indexing || fetching; } }
  public void Search(string text) { search.Text = text; query = text.Trim(); selected = ""; Rebuild(true); }

  // Reads new transcript lines in the background, then the subjects of the linked issues.
  void Scan() {
   nextScan = DateTime.Now.AddSeconds(30);
   var current = index;
   Task.Run(() => History.Refresh(current)).ContinueWith(t => {
    if (IsDisposed || t.IsFaulted) return;
    index = t.Result; indexing = false; Rebuild(false);
    if (!fetching && index.Sessions.Any(s => s.Issues.Count > 0)) {
     fetching = true;
     History.FetchTitles(index, index.Sessions.SelectMany(s => s.Issues)).ContinueWith(f => { fetching = false; if (!IsDisposed && !f.IsFaulted && f.Result) Rebuild(false); }, TaskScheduler.FromCurrentSynchronizationContext());
    }
   }, TaskScheduler.FromCurrentSynchronizationContext());
  }

  static string ProjectKey(string cwd) { return (cwd ?? "").TrimEnd('\\', '/').ToLowerInvariant(); }
  static string Name(string cwd) { try { return new DirectoryInfo((cwd ?? "").TrimEnd('\\', '/')).Name; } catch { return cwd; } }

  List<Conv> All() {
   var sessions = live() ?? new List<SessionInfo>();
   var byRadar = new Dictionary<string, SessionInfo>(); foreach (var s in sessions) byRadar[s.Id] = s;
   var list = new List<Conv>(); var matched = new HashSet<string>();
   foreach (var h in index.Sessions) {
    SessionInfo l; byRadar.TryGetValue(History.RadarId(h.Id), out l);
    if (l != null) matched.Add(l.Id);
    list.Add(new Conv { H = h, Live = l });
   }
   // Sessions the radar sees but whose transcript is not indexed yet.
   // Only open ones: a closed session without a transcript has nothing to resume.
   foreach (var s in sessions) if (!matched.Contains(s.Id) && !s.Closed && s.State != "ended") list.Add(new Conv { Live = s, H = new HistorySession { Cwd = s.Cwd, End = s.Time.UtcDateTime } });
   return list;
  }
  static bool Open(Conv c) { return c.Live != null && !c.Live.Closed && c.Live.State != "ended"; }
  static DateTime When(Conv c) { var t = c.H.End; if (c.Live != null && c.Live.Time.UtcDateTime > t) t = c.Live.Time.UtcDateTime; return t; }

  void Rebuild(bool toTop) {
   if (IsDisposed) return;
   var all = All();
   // ---- sidebar ----
   var pins = new HashSet<string>(settings.PinnedProjects);
   var projects = all.Where(c => c.H.Cwd.Length > 0).GroupBy(c => ProjectKey(c.H.Cwd)).Select(g => new { Key = g.Key, Cwd = g.First().H.Cwd, Count = g.Count(), Live = g.Count(Open), Last = g.Max(When) })
    .OrderByDescending(p => pins.Contains(p.Key)).ThenByDescending(p => p.Last).ToList();
   var nav = new List<El>();
   nav.Add(Nav("all", "Todas as conversas", all.Count, 0));
   nav.Add(Nav("live", "Abertas agora", all.Count(Open), all.Count(Open)));
   nav.Add(Nav("linked", "Com atividade", all.Count(c => c.H.Issues.Count > 0), 0));
   nav.Add(new NavHeaderEl { Text = "Projetos" });
   foreach (var p in projects) {
    string key = p.Key, cwd = p.Cwd;
    var el = Nav("p:" + key, Name(cwd), p.Count, p.Live); el.Pinned = pins.Contains(key); el.Tip = cwd;
    el.Menu = at => ProjectMenu(at, key, cwd);
    nav.Add(el);
   }
   side.SetItems(nav);

   // ---- list ----
   IEnumerable<Conv> view = all;
   if (scope == "live") view = view.Where(Open);
   else if (scope == "linked") view = view.Where(c => c.H.Issues.Count > 0);
   else if (scope.StartsWith("p:", StringComparison.Ordinal)) { string key = scope.Substring(2); view = view.Where(c => ProjectKey(c.H.Cwd) == key); }
   if (query.Length > 0) view = view.Where(c => History.Matches(c.H, query, index.IssueTitles) || (c.Live != null && History.Has(c.Live.Project, query)));
   shown = view.OrderByDescending(Open).ThenByDescending(When).Take(300).ToList();
   if (selected.Length > 0 && !shown.Any(c => c.Key == selected)) selected = "";

   string title = scope == "live" ? "Abertas agora" : scope == "linked" ? "Com atividade" : scope.StartsWith("p:", StringComparison.Ordinal) ? Name(projects.Where(p => "p:" + p.Key == scope).Select(p => p.Cwd).FirstOrDefault() ?? "") : "Todas as conversas";
   string sub = query.Length > 0 ? Count(shown.Count, "resultado", "resultados") + " para “" + query + "”" : Count(shown.Count, "conversa", "conversas");
   if (indexing) sub += " · lendo o histórico…";
   var items = new List<El> { new TitleEl { Title = title, Subtitle = sub } };
   rows.Clear();
   string group = null; DateTime today = DateTime.Today;
   foreach (var c in shown) {
    string g = Open(c) ? "Abertas" : Bucket(When(c).ToLocalTime(), today);
    if (g != group) { group = g; items.Add(new GroupEl { Text = g }); }
    var row = Row(c); rows[c.Key] = row; items.Add(row);
   }
   if (shown.Count == 0) items.Add(new TextEl { Text = indexing ? "Lendo as conversas do Claude Code pela primeira vez. Leva alguns segundos." : query.Length > 0 ? "Nada encontrado. Tente outra palavra ou o número da atividade." : "Nenhuma conversa aqui ainda.", Color = Theme.Muted, Before = 24 });
   Body.SetItems(items);
   if (toTop) Body.ScrollTop();
   Footer.SetItems(new El[] { Actions() });
  }
  static string Count(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }
  static string Bucket(DateTime t, DateTime today) {
   if (t.Date == today) return "Hoje";
   if (t.Date == today.AddDays(-1)) return "Ontem";
   if (t.Date > today.AddDays(-7)) return "Últimos 7 dias";
   if (t.Date > today.AddDays(-30)) return "Últimos 30 dias";
   return "Mais antigas";
  }
  NavEl Nav(string key, string text, int count, int liveCount) {
   return new NavEl { Text = text, Count = count, Live = liveCount, Selected = scope == key, Click = () => { scope = key; selected = ""; Rebuild(true); } };
  }

  ConvEl Row(Conv c) {
   var h = c.H; var l = c.Live;
   var row = new ConvEl { Title = l != null && h.Label == "Conversa sem mensagens" ? l.Project : h.Label, Selected = c.Key == selected, Right = Ago(When(c).ToLocalTime()) };
   var meta = new List<string> { Name(h.Cwd) };
   if (h.Branch.Length > 0 && h.Branch != "HEAD") meta.Add(h.Branch);
   if (h.Prompts > 0) meta.Add(Count(h.Prompts, "mensagem", "mensagens"));
   row.Meta = String.Join("  ·  ", meta);
   int issue = h.Issue;
   if (issue > 0) { string t; index.IssueTitles.TryGetValue(issue.ToString(CultureInfo.InvariantCulture), out t); row.Issue = "#" + issue; row.IssueTitle = t ?? ""; }
   if (Open(c)) { row.State = l.Activity; row.StateColor = Tone(l); }
   if (query.Length > 0) row.Snippet = History.Snippet(h, query);
   string key = c.Key;
   row.Click = () => { selected = key; Rebuild(false); };
   row.DoubleClick = () => { selected = key; Primary(); };
   row.Menu = at => { selected = key; Rebuild(false); ConvMenu(at); };
   return row;
  }
  static Color Tone(SessionInfo s) {
   if (s.Silent) return Theme.Muted;
   switch (s.State) { case "working": return Theme.Accent; case "background": return Theme.Blue; case "waiting": return Theme.Amber; case "done": return Theme.Green; case "error": return Theme.Red; default: return Theme.Soft; }
  }
  static string Ago(DateTime t) {
   var d = DateTime.Now - t;
   if (d.TotalMinutes < 1) return "agora";
   if (d.TotalMinutes < 60) return "há " + (int)d.TotalMinutes + " min";
   if (t.Date == DateTime.Today) return t.ToString("HH:mm");
   if (t.Date == DateTime.Today.AddDays(-1)) return "ontem " + t.ToString("HH:mm");
   if (d.TotalDays < 7) return t.ToString("ddd HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
   return t.ToString(t.Year == DateTime.Now.Year ? "dd MMM" : "dd MMM yy", CultureInfo.GetCultureInfo("pt-BR"));
  }

  // ---- actions ----
  Conv Current { get { return shown.FirstOrDefault(c => c.Key == selected); } }
  ButtonsEl Actions() {
   var c = Current; var b = new ButtonsEl();
   if (c == null) { b.Hint = "Selecione uma conversa · duplo clique retoma · botão direito mostra mais opções"; return b; }
   b.Hint = c.H.Cwd;
   if (c.H.Issue > 0 && redmineHost.Length > 0) { int issue = c.H.Issue; b.Add("Abrir #" + issue, false, () => OpenIssue(issue)); }
   if (c.H.Cwd.Length > 0) b.Add("Nova sessão aqui", false, () => Terminal.Start(c.H.Cwd, ""));
   if (Open(c) && c.Live.WindowPid > 0) b.Add("Ir para o terminal", true, Primary);
   else if (c.H.Id.Length > 0) b.Add("Retomar conversa", true, Primary);
   return b;
  }
  void Primary() {
   var c = Current; if (c == null) return;
   if (Open(c) && c.Live.WindowPid > 0) focus(c.Live.Id);
   else if (c.H.Id.Length > 0 && c.H.Cwd.Length > 0) Terminal.Start(c.H.Cwd, c.H.Id);
  }
  void OpenIssue(int issue) { try { using (var client = RedmineClient.FromMcpConfig()) if (client != null) openUrl(client.IssueUrl(issue)); } catch { } }
  void ConvMenu(Point at) {
   var c = Current; if (c == null) return;
   var list = new List<KeyValuePair<string, Action>>();
   if (Open(c) && c.Live.WindowPid > 0) list.Add(new KeyValuePair<string, Action>("Ir para o terminal", () => focus(c.Live.Id)));
   if (c.H.Id.Length > 0) list.Add(new KeyValuePair<string, Action>("Retomar numa nova aba", () => Terminal.Start(c.H.Cwd, c.H.Id)));
   if (c.Live != null) list.Add(new KeyValuePair<string, Action>("Seguir na faixa do widget", () => follow(c.Live.Id)));
   if (c.H.Issue > 0 && redmineHost.Length > 0) { int issue = c.H.Issue; list.Add(new KeyValuePair<string, Action>("Abrir #" + issue + " no Redmine", () => OpenIssue(issue))); }
   if (c.H.Id.Length > 0) list.Add(new KeyValuePair<string, Action>("Copiar comando para retomar", () => { try { Clipboard.SetText("cd \"" + c.H.Cwd + "\"; claude --resume " + c.H.Id); } catch { } }));
   if (c.H.Cwd.Length > 0) list.Add(new KeyValuePair<string, Action>("Abrir a pasta", () => Terminal.Folder(c.H.Cwd)));
   ShowMenuAt(at, list);
  }
  void ProjectMenu(Point at, string key, string cwd) {
   bool pinned = settings.PinnedProjects.Contains(key);
   ShowMenuAt(at, new List<KeyValuePair<string, Action>> {
    new KeyValuePair<string, Action>("Nova sessão neste projeto", () => Terminal.Start(cwd, "")),
    new KeyValuePair<string, Action>(pinned ? "Desafixar do topo" : "Fixar no topo", () => { if (pinned) settings.PinnedProjects.Remove(key); else settings.PinnedProjects.Add(key); save(); Rebuild(false); }),
    new KeyValuePair<string, Action>("Abrir a pasta", () => Terminal.Folder(cwd)),
   });
  }
  void ShowMenuAt(Point screen, List<KeyValuePair<string, Action>> items) {
   var menu = new ContextMenuStrip { Font = Theme.F(Theme.Sans, 9.5f), ShowImageMargin = false };
   foreach (var item in items) { var action = item.Value; menu.Items.Add(new ToolStripMenuItem(item.Key, null, delegate { action(); }) { Padding = new Padding(4, 6, 4, 6) }); }
   menu.Opened += delegate { Dwm.Style(menu.Handle, Theme.Surface); };
   menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
   menu.Show(screen);
  }

  // Keyboard: arrows move, Enter acts, Ctrl+F searches, Esc clears the search and then closes.
  protected override bool ProcessCmdKey(ref Message msg, Keys keys) {
   if (keys == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
   if (keys == Keys.Escape) { if (search.Text.Length > 0) { search.Clear(); return true; } Close(); return true; }
   if (keys == Keys.Down || keys == Keys.Up) {
    if (shown.Count == 0) return true;
    int i = shown.FindIndex(c => c.Key == selected); i = keys == Keys.Down ? Math.Min(shown.Count - 1, i + 1) : Math.Max(0, i < 0 ? 0 : i - 1);
    selected = shown[i].Key; Rebuild(false); El row; if (rows.TryGetValue(selected, out row)) Body.Reveal(row); return true;
   }
   if (keys == Keys.Enter) { if (selected.Length == 0 && shown.Count > 0) { selected = shown[0].Key; Rebuild(false); } Primary(); return true; }
   return base.ProcessCmdKey(ref msg, keys);
  }

  // ---- elements ----
  sealed class NavEl : El {
   public string Text = ""; public int Count, Live; public bool Selected, Pinned;
   public NavEl() { Before = 1; }
   public override int Layout(Graphics g, Ui ui, int x, int y, int width) { Bounds = new Rectangle(x, y, width, ui.P(32)); return Bounds.Height; }
   public override void Paint(Graphics g, Ui ui, El hover) {
    if (Selected) Draw.Fill(g, Bounds, ui.P(6), Theme.Raised); else if (hover == this) Draw.Fill(g, Bounds, ui.P(6), Theme.Hover);
    int x = Bounds.X + ui.P(12);
    if (Live > 0) { int d = ui.P(6); Draw.Fill(g, new Rectangle(x, Bounds.Y + (Bounds.Height - d) / 2, d, d), d / 2, Theme.Accent); }
    x += ui.P(14);
    string count = Count.ToString(CultureInfo.InvariantCulture); int cw = Draw.Width(g, count, Theme.F(Theme.Sans, 8.5f));
    Draw.Line(g, Text, Theme.F(Selected ? Theme.SansBold : Theme.Sans, 9.5f), new Rectangle(x, Bounds.Y, Bounds.Right - x - cw - ui.P(28), Bounds.Height), Selected ? Theme.Ink : Theme.Soft);
    if (Pinned) Draw.Glyph(g, "", 7.5f, new Rectangle(Bounds.Right - cw - ui.P(30), Bounds.Y, ui.P(14), Bounds.Height), Theme.Muted);
    Draw.Line(g, count, Theme.F(Theme.Sans, 8.5f), new Rectangle(Bounds.Right - cw - ui.P(12), Bounds.Y, cw, Bounds.Height), Theme.Muted, true);
   }
  }
  sealed class NavHeaderEl : El {
   public string Text = "";
   public NavHeaderEl() { Before = 20; }
   public override int Layout(Graphics g, Ui ui, int x, int y, int width) { Bounds = new Rectangle(x, y, width, ui.P(24)); return Bounds.Height; }
   public override void Paint(Graphics g, Ui ui, El hover) { Draw.Line(g, Text, Theme.F(Theme.Sans, 8.5f), new Rectangle(Bounds.X + ui.P(12), Bounds.Y, Bounds.Width, Bounds.Height), Theme.Muted); }
  }
  sealed class GroupEl : El {
   public string Text = "";
   public GroupEl() { Before = 18; }
   public override int Layout(Graphics g, Ui ui, int x, int y, int width) { Bounds = new Rectangle(x, y, width, ui.P(26)); return Bounds.Height; }
   public override void Paint(Graphics g, Ui ui, El hover) { Draw.Line(g, Text, Theme.F(Theme.Sans, 8.5f), new Rectangle(Bounds.X + ui.P(12), Bounds.Y, Bounds.Width, Bounds.Height), Theme.Muted); }
  }
  // A conversation: title and time; project, branch and size; the linked issue; the live state; the search excerpt.
  sealed class ConvEl : El {
   public string Title = "", Right = "", Meta = "", Issue = "", IssueTitle = "", State = "", Snippet = ""; public Color StateColor = Theme.Soft; public bool Selected;
   public ConvEl() { Before = 0; }
   public override int Layout(Graphics g, Ui ui, int x, int y, int width) {
    int h = ui.P(12) + ui.P(20) + ui.P(18) + (State.Length > 0 ? ui.P(18) : 0) + (Snippet.Length > 0 ? ui.P(20) : 0) + ui.P(12);
    Bounds = new Rectangle(x, y, width, h); return Bounds.Height;
   }
   public override void Paint(Graphics g, Ui ui, El hover) {
    if (Selected) Draw.Fill(g, Bounds, ui.P(6), Theme.Raised); else if (hover == this) Draw.Fill(g, Bounds, ui.P(6), Theme.Hover);
    else using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, Bounds.X + ui.P(12), Bounds.Bottom, Bounds.Right - ui.P(12), Bounds.Bottom);
    int x = Bounds.X + ui.P(12), y = Bounds.Y + ui.P(12), w = Bounds.Width - ui.P(24);
    int rw = Draw.Width(g, Right, Theme.F(Theme.Sans, 8.5f));
    if (State.Length > 0) { int d = ui.P(7); Draw.Fill(g, new Rectangle(x, y + (ui.P(20) - d) / 2, d, d), d / 2, StateColor); }
    int tx = x + (State.Length > 0 ? ui.P(15) : 0);
    Draw.Line(g, Title, Theme.F(Theme.SansBold, 9.5f), new Rectangle(tx, y, Bounds.Right - ui.P(24) - rw - tx, ui.P(20)), Theme.Ink);
    Draw.Line(g, Right, Theme.F(Theme.Sans, 8.5f), new Rectangle(Bounds.Right - ui.P(12) - rw, y, rw, ui.P(20)), Theme.Muted, true);
    y += ui.P(21);
    int mx = tx;
    if (Issue.Length > 0) {
     var f = Theme.F(Theme.SansBold, 8.5f); int iw = Draw.Width(g, Issue, f);
     Draw.Line(g, Issue, f, new Rectangle(mx, y, iw + ui.P(2), ui.P(18)), Theme.Accent); mx += iw + ui.P(6);
     if (IssueTitle.Length > 0) { var ft = Theme.F(Theme.Sans, 8.5f); string it = IssueTitle; int max = Math.Min(Draw.Width(g, it, ft), (Bounds.Right - mx) / 2); Draw.Line(g, it, ft, new Rectangle(mx, y, max, ui.P(18)), Theme.Soft); mx += max + ui.P(10); }
     else mx += ui.P(4);
    }
    Draw.Line(g, Meta, Theme.F(Theme.Sans, 8.5f), new Rectangle(mx, y, Bounds.Right - ui.P(12) - mx, ui.P(18)), Theme.Muted);
    y += ui.P(18);
    if (State.Length > 0) { Draw.Line(g, State, Theme.F(Theme.Sans, 8.5f), new Rectangle(tx, y, w, ui.P(18)), StateColor); y += ui.P(18); }
    if (Snippet.Length > 0) Draw.Line(g, Snippet, Theme.F(Theme.Sans, 9), new Rectangle(tx, y + ui.P(2), Bounds.Right - ui.P(12) - tx, ui.P(18)), Theme.Soft);
   }
  }
 }

 // Opens Claude Code in a new Windows Terminal tab (or a PowerShell window when Windows Terminal is missing).
 public static class Terminal {
  static string Wt() {
   string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
   return File.Exists(apps) ? apps : "";
  }
  public static void Start(string cwd, string resumeId) {
   try {
    string dir = (cwd ?? "").TrimEnd('\\'); if (dir.EndsWith(":", StringComparison.Ordinal)) dir += "\\";
    if (!Directory.Exists(dir)) { MessageBox.Show("A pasta não existe mais:\n" + cwd, "Sessões"); return; }
    string command = "claude" + (String.IsNullOrEmpty(resumeId) ? "" : " --resume " + resumeId);
    string title = new DirectoryInfo(dir).Name.Replace("\"", "");
    string wt = Wt(), arg = dir.EndsWith("\\", StringComparison.Ordinal) ? dir + "." : dir; // a trailing backslash would escape the quote
    if (wt.Length > 0) Process.Start(new ProcessStartInfo(wt, "-w 0 nt -d \"" + arg + "\" --title \"" + title + "\" powershell -NoExit -Command " + command) { UseShellExecute = false });
    else Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -Command " + command) { WorkingDirectory = dir, UseShellExecute = true });
   } catch (Exception error) { MessageBox.Show("Não foi possível abrir o terminal:\n" + error.Message, "Sessões"); }
  }
  public static void Folder(string cwd) { try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + cwd + "\"") { UseShellExecute = true }); } catch { } }
 }
}
