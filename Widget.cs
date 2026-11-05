// Claude Widget — original, dependency-free .NET Framework / Windows Forms source.
// Network access to the two Anthropic hosts below is read-only. The only write is the daily
// time entry on the Redmine host from mcp-config.json (Redmine.cs), when enabled.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MeuClaudeWidget {
 public class Settings {
  public string Mode = "";
  public string ProtectedSession = "";
  public string Organization = "";
  public int Interval = 60;
  public int X = -10000, Y = -10000;
  public bool OnTop = true;
  public bool Compact = true;
  public bool RadarAlerts = true;
  public bool UsageAlerts = true;
  public string SelectedSession = "";
  public int CompactX = -10000, CompactY = -10000;
  public bool RedmineAuto = true;
  public string RedmineLastRun = "";
  public bool RedmineClock = true;
  public bool RedmineCelebrate = true;
  public string RedmineFinishedSince = "", RedmineScoreShown = "";
  public List<int> RedmineCelebrated = new List<int>();
  public List<string> DaysOff = new List<string>();
  public Dictionary<string,int> WeekRejections = new Dictionary<string,int>();
  public bool MorningBrief = true;
  public string BriefingShown = "";
  public bool DockTaskbar = true;
  public string DockSide = "auto"; // auto, left (next to the weather), right (next to the clock)
 }
 public class Bucket {
  public string Title;
  public double? Percent;
  public DateTimeOffset? Reset;
 }
 public class RequestFailure : Exception {
  public int Delay;
  public RequestFailure(string message, int delay) : base(message) { Delay = delay; }
 }
 public static class Data {
  public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeuClaudeWidget");
  public static readonly string ConfigPath = Path.Combine(Folder, "config.json");
  public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 1048576 }; }
  public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
  public static object Get(Dictionary<string, object> obj, string key) { object v; return obj != null && obj.TryGetValue(key, out v) ? v : null; }
  public static string Str(object value) { return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture); }
  public static Settings Load() {
   try { var s = Json().Deserialize<Settings>(File.ReadAllText(ConfigPath)); if (s != null) { s.Interval = 60; return s; } } catch { }
   return new Settings();
  }
  public static void Save(Settings s) {
   Directory.CreateDirectory(Folder);
   string temp = ConfigPath + ".tmp";
   File.WriteAllText(temp, Json().Serialize(s), new UTF8Encoding(false));
   if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, null); else File.Move(temp, ConfigPath);
  }
  public static string Protect(string value) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
  public static string Unprotect(string value) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
  public static string CodeToken() {
   string folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
   if (String.IsNullOrWhiteSpace(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
   string path = Path.Combine(folder, ".credentials.json");
   if (!File.Exists(path)) throw new RequestFailure("Login não encontrado. Entre no Claude Code no Windows ou use Navegador em Configurar.", 0);
   var root = Map(Json().DeserializeObject(File.ReadAllText(path)));
   var auth = Map(Get(root, "claudeAiOauth"));
   string token = Str(Get(auth, "accessToken"));
   if (token.Length == 0) throw new RequestFailure("É necessário um login de assinatura Claude no Claude Code. Chaves de API não servem.", 0);
   return token;
  }
  public static List<Bucket> ParseUsage(string json) {
   var root = Map(Json().DeserializeObject(json));
   if (root == null || (!root.ContainsKey("five_hour") && !root.ContainsKey("seven_day")))
    throw new RequestFailure("Resposta de uso incompatível. Confira a página oficial de uso.", 0);
   var result = new List<Bucket>();
   result.Add(ParseBucket("Sessão · 5 horas", Get(root, "five_hour")));
   result.Add(ParseBucket("Semana · todos os modelos", Get(root, "seven_day")));
   foreach (var pair in root) {
    if (pair.Key.StartsWith("seven_day_", StringComparison.Ordinal) && Map(pair.Value) != null)
     result.Add(ParseBucket("Semana · " + pair.Key.Substring(10).Replace('_', ' '), pair.Value));
   }
   return result;
  }
  static Bucket ParseBucket(string title, object value) {
   var b = new Bucket { Title = title }; var map = Map(value);
   double p; object raw = Get(map, "utilization");
   if (raw != null && !(raw is bool) && Double.TryParse(Str(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out p) && !Double.IsNaN(p) && !Double.IsInfinity(p) && p >= 0) b.Percent = p;
   DateTimeOffset time;
   if (DateTimeOffset.TryParse(Str(Get(map, "resets_at")), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time)) b.Reset = time;
   return b;
  }
  public static string ResetLabel(Bucket b) {
   if (!b.Reset.HasValue) return "Renovação não informada";
   var left = b.Reset.Value - DateTimeOffset.UtcNow;
   if (left.TotalSeconds <= 0) return "Prazo passou · aguardando nova consulta";
   string span = left.TotalDays >= 1 ? ((int)left.TotalDays) + "d " + left.Hours + "h" : left.TotalHours >= 1 ? ((int)left.TotalHours) + "h " + left.Minutes + "min" : Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + "min";
   return "Renova em " + span + " · " + b.Reset.Value.ToLocalTime().ToString("dd/MM HH:mm");
  }
  public static string Percent(double value) { return value.ToString("0.#",CultureInfo.GetCultureInfo("pt-BR"))+"%"; }
 }
 public class Reading { public long T; public double P5 = -1, P7 = -1; }
 // Local usage history (one point per change or every 10 minutes, 8 days) used to estimate the pace.
 public static class Pace {
  public static readonly string HistoryPath = Path.Combine(Data.Folder, "history.json");
  public static List<Reading> Load() {
   try { var list = Data.Json().Deserialize<List<Reading>>(File.ReadAllText(HistoryPath)); if (list != null) return list.OrderBy(r=>r.T).ToList(); } catch { }
   return new List<Reading>();
  }
  public static void Save(List<Reading> history) {
   Directory.CreateDirectory(Data.Folder);
   string temp = HistoryPath + ".tmp";
   File.WriteAllText(temp, Data.Json().Serialize(history), new UTF8Encoding(false));
   if (File.Exists(HistoryPath)) File.Replace(temp, HistoryPath, null); else File.Move(temp, HistoryPath);
  }
  static double Value(List<Bucket> b, int i) { return b.Count > i && b[i].Percent.HasValue ? b[i].Percent.Value : -1; }
  static double Value(Reading r, int index) { return index == 0 ? r.P5 : r.P7; }
  public static bool Add(List<Reading> history, List<Bucket> buckets, DateTimeOffset now) {
   var r = new Reading { T = now.ToUnixTimeSeconds(), P5 = Value(buckets,0), P7 = Value(buckets,1) };
   if (r.P5 < 0 && r.P7 < 0) return false;
   var last = history.Count > 0 ? history[history.Count-1] : null;
   if (last != null && last.P5 == r.P5 && last.P7 == r.P7 && r.T-last.T < 600) return false;
   history.Add(r);
   long oldest = r.T-8*86400; history.RemoveAll(x=>x.T < oldest);
   if (history.Count > 4000) history.RemoveRange(0, history.Count-4000);
   return true;
  }
  public static List<KeyValuePair<long,double>> Points(List<Reading> history, int index, TimeSpan span, DateTimeOffset now) {
   long from = (now-span).ToUnixTimeSeconds();
   return history.Where(r=>r.T >= from && Value(r,index) >= 0).Select(r=>new KeyValuePair<long,double>(r.T,Value(r,index))).ToList();
  }
  // Time to reach 100% at the pace of the last hour (5h) or day (7d); null when flat or not enough data.
  public static DateTimeOffset? Exhaustion(List<Reading> history, int index, Bucket b, DateTimeOffset now) {
   if (b == null || !b.Percent.HasValue) return null;
   double p = b.Percent.Value; if (p >= 100) return now;
   TimeSpan window = index == 0 ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
   TimeSpan look = index == 0 ? TimeSpan.FromHours(1) : TimeSpan.FromHours(24);
   TimeSpan least = index == 0 ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(2);
   DateTimeOffset from = now-look;
   if (b.Reset.HasValue && b.Reset.Value-window > from && b.Reset.Value-window < now) from = b.Reset.Value-window;
   Reading first = null;
   foreach (var r in history) {
    double v = Value(r,index); if (v < 0 || DateTimeOffset.FromUnixTimeSeconds(r.T) < from) continue;
    if (v > p) { first = null; continue; } // higher than now: belongs to a window that has since reset
    if (first == null) first = r;
   }
   if (first == null) return null;
   double hours = (now-DateTimeOffset.FromUnixTimeSeconds(first.T)).TotalHours, gained = p-Value(first,index);
   if (hours < least.TotalHours || gained <= 0) return null;
   return now.AddHours((100-p)/(gained/hours));
  }
  public static bool RunsOut(List<Reading> history, int index, Bucket b, DateTimeOffset now, out DateTimeOffset when) {
   var eta = Exhaustion(history,index,b,now); when = eta ?? now;
   return eta.HasValue && b.Percent.Value < 100 && (!b.Reset.HasValue || eta.Value < b.Reset.Value);
  }
  public static string Describe(List<Reading> history, int index, Bucket b, DateTimeOffset now) {
   if (b == null || !b.Percent.HasValue) return "";
   if (b.Percent.Value >= 100) return "Limite atingido";
   DateTimeOffset when;
   if (RunsOut(history,index,b,now,out when)) return "No ritmo atual, chega a 100% " + When(when);
   return Exhaustion(history,index,b,now).HasValue ? "No ritmo atual, não esgota antes de renovar" : "Ritmo: sem aumento recente";
  }
  public static string When(DateTimeOffset t) {
   DateTime local = t.LocalDateTime;
   return local.Date == DateTime.Now.Date ? "às " + local.ToString("HH:mm") : "em " + local.ToString("dd/MM HH:mm");
  }
 }
 public sealed class Api : IDisposable {
  readonly HttpClient http;
  public Api() {
   ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
   http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
   http.Timeout = TimeSpan.FromSeconds(25);
  }
  public async Task<string> Get(string url, string secret, bool oauth) {
   var uri = new Uri(url);
   if (uri.Scheme != "https" || (oauth ? uri.Host != "api.anthropic.com" : uri.Host != "claude.ai")) throw new InvalidOperationException("Destino não permitido.");
   using (var request = new HttpRequestMessage(HttpMethod.Get, uri)) {
    request.Headers.TryAddWithoutValidation("Accept", "application/json");
    request.Headers.TryAddWithoutValidation("User-Agent", "MeuClaudeWidget/1.0");
    if (secret.IndexOfAny(new char[] { '\r', '\n', ';' }) >= 0) throw new RequestFailure("Credencial inválida. Copie somente o valor da chave.", 0);
    if (oauth) {
     request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + secret);
     request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
    } else request.Headers.TryAddWithoutValidation("Cookie", "sessionKey=" + secret);
    using (var response = await http.SendAsync(request)) {
     int code = (int)response.StatusCode;
     if (code == 429) {
      double retry = 0;
      var header = response.Headers.RetryAfter;
      if (header != null && header.Delta.HasValue) retry = Math.Max(retry, header.Delta.Value.TotalSeconds);
      if (header != null && header.Date.HasValue) retry = Math.Max(retry, (header.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
      throw new RequestFailure("Consulta de uso limitada pelo Claude (429). Tentativa automática; um prazo enviado pelo servidor tem prioridade.", (int)Math.Min(Int32.MaxValue, Math.Ceiling(retry)));
     }
     if (code == 401) throw new RequestFailure(oauth ? "Login expirado. Abra o Claude Code e renove o login." : "Sessão expirada. Atualize a sessionKey em Configurar.", 0);
     if (code == 403) throw new RequestFailure("Acesso recusado (403). Pode ser sessão expirada, permissão ou bloqueio do site. Confira no navegador.", 0);
     if (!response.IsSuccessStatusCode) throw new RequestFailure("Consulta indisponível (HTTP " + code + "). Nova tentativa automática.", 0);
     string text = await response.Content.ReadAsStringAsync();
     if (!text.TrimStart().StartsWith("{") && !text.TrimStart().StartsWith("[")) throw new RequestFailure("O site não retornou dados de uso. Abra a página oficial para conferir.", 0);
     return text;
    }
   }
  }
  public void Dispose() { http.Dispose(); }
 }
 public class SetupForm : Form {
  public Settings Value;
  ComboBox mode; TextBox key, org; CheckBox top;
  public SetupForm(Settings current) {
   Text = "Configurar · Meu Claude"; ClientSize = new Size(510, 500);
   FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
   StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
   BackColor = Color.FromArgb(246,244,240);
   LabelAt("De onde ler o uso da sua assinatura?", 20, 18, 470, 25);
   mode = new ComboBox { Left = 20, Top = 48, Width = 470, DropDownStyle = ComboBoxStyle.DropDownList };
   mode.Items.AddRange(new object[] { "Navegador · chave de sessão do claude.ai", "Claude Code · usar login local existente" }); Controls.Add(mode);
   LabelAt("sessionKey (somente o valor; não envie no chat)",20,90,470,24);
   key = new TextBox { Left = 20, Top = 117, Width = 470, UseSystemPasswordChar = true }; Controls.Add(key);
   LabelAt("Organização (opcional; descoberta ao conectar)",20,158,470,24);
   org = new TextBox { Left = 20, Top = 184, Width = 470, Text = current.Organization ?? "" }; Controls.Add(org);
   LabelAt("Atualização automática: a cada 1 minuto",20,230,470,24);
   top = new CheckBox { Left = 20, Top = 270, Width = 470, Text = "Manter por cima das outras janelas", Checked = current.OnTop }; Controls.Add(top);
   LabelAt("Navegador: entre em claude.ai → F12 → Application/Aplicativo → Cookies → https://claude.ai → sessionKey.\nA chave salva é protegida pelo Windows para o seu usuário. No modo Claude Code, o arquivo de login é apenas lido.",20,309,470,84);
   var guide = new Button { Text = "Abrir página de uso", Left = 20, Top = 438, Width = 180, Height = 36 }; Controls.Add(guide);
   guide.Click += delegate { Program.OpenUsage(); };
   var save = new Button { Text = "Salvar e conectar", Left = 290, Top = 438, Width = 200, Height = 36 }; Controls.Add(save); AcceptButton = save;
   mode.SelectedIndexChanged += delegate { key.Enabled = org.Enabled = mode.SelectedIndex == 0; };
   mode.SelectedIndex = current.Mode == "code" ? 1 : 0;
   save.Click += delegate {
    try {
     string session = current.ProtectedSession;
     if (mode.SelectedIndex == 0) {
      string input = key.Text.Trim();
      if (input.Length > 0) {
       if (!input.StartsWith("sk-ant-") || input.IndexOfAny(new char[] { '\r', '\n', ';', ' ' }) >= 0) { MessageBox.Show(this,"Cole apenas o valor completo da sessionKey."); return; }
       session = Data.Protect(input);
      }
      if (String.IsNullOrEmpty(session)) { MessageBox.Show(this,"Informe a sessionKey para conectar pelo navegador."); return; }
      Guid id;
      if (org.Text.Trim().Length > 0 && !Guid.TryParse(org.Text.Trim(), out id)) { MessageBox.Show(this,"O ID da organização deve ser um UUID, ou deixe em branco."); return; }
     }
     Value = new Settings { Mode = mode.SelectedIndex == 0 ? "browser" : "code", ProtectedSession = mode.SelectedIndex == 0 ? session : "", Organization = org.Text.Trim(), Interval = 60, OnTop = top.Checked, X = current.X, Y = current.Y, Compact = current.Compact, CompactX = current.CompactX, CompactY = current.CompactY, RadarAlerts = current.RadarAlerts, UsageAlerts = current.UsageAlerts, SelectedSession = current.SelectedSession };
     Data.Save(Value); key.Clear(); DialogResult = DialogResult.OK; Close();
    } catch { MessageBox.Show(this,"Não foi possível salvar a configuração com proteção do Windows."); }
   };
  }
  void LabelAt(string text, int x, int y, int w, int h) { Controls.Add(new Label { Text = text, Left = x, Top = y, Width = w, Height = h }); }
 }
 // Native calls affect only this widget. Explorer is never modified or injected into.
 internal static class Native {
  [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string windowName);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr handle, out Rect rect);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr handle);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(IntPtr handle, int command);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(IntPtr handle);
  [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyIcon(IntPtr handle);
 }
 public class Widget : Form {
  Settings settings; readonly Api api = new Api(); readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
  List<Bucket> buckets = new List<Bucket>();
  DateTimeOffset? last; DateTimeOffset next = DateTimeOffset.MinValue;
  bool busy, stale = true, stopped;
  string status = "Configure sua conta para começar.", detail = "";
  Label title, state, timestamp, radarSummary; Button compactButton; FlowLayoutPanel actions; NotifyIcon tray; ContextMenuStrip menu;
  ToolTip hint = new ToolTip { InitialDelay = 350, ReshowDelay = 150, AutoPopDelay = 15000 };
  ToolStripMenuItem compactItem; string lastHint = "";
  List<SessionInfo> sessions = new List<SessionInfo>();
  SessionInfo activeSession;
  GitInfo gitInfo = new GitInfo { Error = "Aguardando projeto" };
  bool radarBusy, gitBusy, radarWarmed;
  DateTimeOffset nextRadar = DateTimeOffset.MinValue, nextGit = DateTimeOffset.MinValue;
  readonly Dictionary<string,string> observed = new Dictionary<string,string>();
  readonly Color ink = Color.FromArgb(236,232,224), muted = Color.FromArgb(161,161,161), accent = Color.FromArgb(216,156,125);
  readonly Color amber = Color.FromArgb(231,175,76), green = Color.FromArgb(110,190,145), red = Color.FromArgb(240,123,112);
  List<Reading> history = new List<Reading>();
  readonly int[] alertLevel = new int[2]; readonly DateTimeOffset?[] alertReset = new DateTimeOffset?[2]; readonly double?[] alertPercent = new double?[2];
  bool usageWarmed; string balloonSession, trayKey = ""; IntPtr trayHandle = IntPtr.Zero;
  DateTimeOffset nextCleanup = DateTimeOffset.MinValue;
  bool redmineBusy; DateTimeOffset nextRedmine = DateTimeOffset.MinValue; string redmineErrorDay = "";
  // Live clock of the Redmine issue in progress: issues refresh every 3 minutes, hours are recomputed every second.
  List<ActiveIssue> tasks = new List<ActiveIssue>(); bool taskShown, clockBusy; DateTimeOffset nextClock = DateTimeOffset.MinValue;
  Dictionary<int,double> taskTotal = new Dictionary<int,double>(), taskToday = new Dictionary<int,double>(); string clockStamp = "", redmineHost = "";
  Label taskLabel;
  // Celebration of finished issues: checked every 5 minutes; confetti over the strip for a few seconds.
  bool celebrateBusy; DateTimeOffset nextCelebrate = DateTimeOffset.MinValue, nextMine = DateTimeOffset.MinValue;
  List<int> myIssues = new List<int>(); Action balloonAction; ScoreForm scoreForm;
  // Morning summary ("Bom dia"): once per workday between 07:30 and 12:00, 20 s after the widget opens.
  BriefingForm briefForm; bool briefBusy; readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
  // Fixed in the taskbar like the weather button: free room measured every 2 s, taskbar color, dense layout when narrow.
  TaskbarSlot slot; bool slotBusy, hovering; IntPtr foregroundHook; DateTimeOffset nextSlot = DateTimeOffset.MinValue; Color dockBack = Color.Empty, dockLeft = Color.Empty, dockRight = Color.Empty; int dockWidth;
  bool Docked { get { return !preview && settings.Compact && settings.DockTaskbar && slot != null; } }
  int FullWidth { get { return Px(540) + Lead; } }
  bool Dense { get { return settings.Compact && Width < FullWidth - Px(2); } }
  sealed class Confetto { public float X, Y, VX, VY, Size, Angle, Spin; public Color Color; }
  List<Confetto> confetti; DateTime confettiUntil, bannerUntil; string banner = "";
  readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 30 };
  ActiveIssue CurrentTask { get { return tasks.Count == 0 ? null : tasks.OrderBy(t => t.Current.Start).Last(); } }
  // Extra width of the compact strip taken by the task pill; positions are saved without it.
  int Lead { get { return settings.Compact && taskShown ? Px(124) : 0; } }
  Rectangle TaskPill { get { return new Rectangle(Px(256),0,Px(120),Px(40)); } }
  float ScaleFactor { get { return DeviceDpi / 96f; } }
  int Px(float value) { return (int)Math.Round(value * ScaleFactor); }
  public Widget() : this(false) { }
  // preview: offscreen rendering for tests, without tray icon, timers or saved preferences.
  readonly bool preview;
  public Widget(bool preview) {
   this.preview = preview;
   settings = preview ? new Settings { Mode = "code" } : Data.Load();
   if (!preview) history = Pace.Load();
   Text = "Meu Claude · Uso"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
   AutoScaleMode = AutoScaleMode.None; BackColor = Color.FromArgb(27,29,32); ForeColor = ink; TopMost = settings.OnTop;
   DoubleBuffered = true; ShowInTaskbar = false; Font = new Font("Segoe UI", 9);
   title = new Label { Text = "✳  CLAUDE", ForeColor = accent, Font = new Font("Segoe UI Semibold", 11), AutoSize = false }; Controls.Add(title);
   state = new Label { ForeColor = ink, AutoSize = false }; Controls.Add(state);
   timestamp = new Label { ForeColor = muted, AutoSize = false, Font = new Font("Segoe UI",8) }; Controls.Add(timestamp);
   radarSummary = new Label { BackColor = Color.FromArgb(38,41,45), ForeColor = ink, Padding = new Padding(9), Cursor = Cursors.Hand }; Controls.Add(radarSummary);
   radarSummary.Click += delegate { ShowRadar(); };
   taskLabel = new Label { BackColor = Color.FromArgb(38,41,45), ForeColor = green, Padding = new Padding(9,4,9,4), Cursor = Cursors.Hand, Visible = false }; Controls.Add(taskLabel);
   taskLabel.Click += delegate { GoToTask(); };
   compactButton = new Button { Text = "Compactar", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42,44,48), ForeColor = accent, TabStop = true, AccessibleName = "Voltar ao modo compacto" };
   compactButton.FlatAppearance.BorderSize = 0;
   compactButton.Click += delegate { if (!settings.Compact) ToggleCompact(); };
   Controls.Add(compactButton);
   actions = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false }; Controls.Add(actions);
   AddButton("Atualizar", async delegate { await RefreshUsage(); });
   AddButton("Configurar", delegate { Configure(); });
   AddButton("×", delegate { Close(); });
   Theme.UseDarkMenus();
   menu = new ContextMenuStrip();
   compactItem = new ToolStripMenuItem("Modo compacto", null, delegate { ToggleCompact(); });
   menu.Items.Add(compactItem);
   var dockItem = new ToolStripMenuItem("Fixar na barra de tarefas") { Checked = settings.DockTaskbar, CheckOnClick = true };
   var sideMenu = new ToolStripMenuItem("Posição na barra");
   dockItem.CheckedChanged += delegate {
    settings.DockTaskbar = dockItem.Checked; sideMenu.Enabled = dockItem.Checked; TrySave();
    if (settings.DockTaskbar) { if (!settings.Compact) ToggleCompact(); nextSlot = DateTimeOffset.MinValue; }
    else { slot = null; dockWidth = 0; Arrange(); RestorePosition(); PinCompact(); }
   };
   foreach (var option in new[] { new KeyValuePair<string,string>("auto", "Automática (onde couber melhor)"), new KeyValuePair<string,string>("left", "Ao lado do clima (esquerda)"), new KeyValuePair<string,string>("right", "Perto do relógio (direita)") }) {
    string key = option.Key; var item = new ToolStripMenuItem(option.Value) { Checked = settings.DockSide == key };
    item.Click += delegate { settings.DockSide = key; foreach (ToolStripMenuItem other in sideMenu.DropDownItems) other.Checked = other == item; TrySave(); ApplyDock(); };
    sideMenu.DropDownItems.Add(item);
   }
   sideMenu.Enabled = settings.DockTaskbar;
   menu.Items.Add(dockItem); menu.Items.Add(sideMenu);
   menu.Items.Add("Alinhar perto do relógio", null, delegate { if (!settings.Compact) ToggleCompact(); if (settings.DockTaskbar) dockItem.Checked = false; DockNearClock(); RememberPosition(); TrySave(); });
   menu.Items.Add("Atualizar", null, async delegate { await RefreshUsage(); });
   menu.Items.Add("Status da conexão", null, delegate { ShowStatusTip(); });
   menu.Items.Add("Projetos e sessões…", null, delegate { ShowRadar(); });
   menu.Items.Add("Ir para o terminal da sessão", null, delegate { if (activeSession != null) FocusSession(activeSession.Id); else ShowRadar(); });
   var notifyItem = new ToolStripMenuItem("Avisar quando precisar de você / terminar") { Checked = settings.RadarAlerts, CheckOnClick = true };
   notifyItem.CheckedChanged += delegate { settings.RadarAlerts = notifyItem.Checked; TrySave(); };
   menu.Items.Add(notifyItem);
   var usageItem = new ToolStripMenuItem("Avisar uso alto (80% / 95%) e renovação") { Checked = settings.UsageAlerts, CheckOnClick = true };
   usageItem.CheckedChanged += delegate { settings.UsageAlerts = usageItem.Checked; TrySave(); };
   menu.Items.Add(usageItem);
   var redmineMenu = new ToolStripMenuItem("Redmine");
   var redmineAuto = new ToolStripMenuItem("Apontar horas às 17:55 (tarefa em andamento)") { Checked = settings.RedmineAuto, CheckOnClick = true };
   redmineAuto.CheckedChanged += delegate { settings.RedmineAuto = redmineAuto.Checked; TrySave(); };
   redmineMenu.DropDownItems.Add(redmineAuto);
   var redmineClock = new ToolStripMenuItem("Mostrar horas da tarefa em andamento") { Checked = settings.RedmineClock, CheckOnClick = true };
   redmineClock.CheckedChanged += delegate { settings.RedmineClock = redmineClock.Checked; TrySave(); nextClock = DateTimeOffset.MinValue; UpdateTaskClock(); };
   redmineMenu.DropDownItems.Add(redmineClock);
   redmineMenu.DropDownItems.Add("Abrir tarefa em andamento no Redmine", null, delegate { OpenTask(); });
   redmineMenu.DropDownItems.Add("Bom dia: onde parei…", null, delegate { ShowBrief(false); });
   var briefItem = new ToolStripMenuItem("Resumo de bom dia ao abrir (07:30–12:00)") { Checked = settings.MorningBrief, CheckOnClick = true };
   briefItem.CheckedChanged += delegate { settings.MorningBrief = briefItem.Checked; TrySave(); };
   redmineMenu.DropDownItems.Add(briefItem);
   redmineMenu.DropDownItems.Add("Placar da semana…", null, delegate { ShowScore(); });
   var celebrateItem = new ToolStripMenuItem("Comemorar tarefas finalizadas") { Checked = settings.RedmineCelebrate, CheckOnClick = true };
   celebrateItem.CheckedChanged += delegate { settings.RedmineCelebrate = celebrateItem.Checked; TrySave(); };
   redmineMenu.DropDownItems.Add(celebrateItem);
   redmineMenu.DropDownItems.Add("Ver exemplo da comemoração", null, delegate {
    Celebrate("#1234 finalizada!", "🎉 #1234 finalizada! (exemplo)", "Passou na QA de primeira 🔥 · 3h30 de 4h00 estimadas\n1ª tarefa finalizada da semana", null);
   });
   redmineMenu.DropDownItems.Add("Prévia do apontamento de hoje…", null, delegate { PreviewRedmine(); });
   redmineMenu.DropDownItems.Add("Não apontar hoje", null, delegate {
    settings.RedmineLastRun = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); TrySave();
    hint.Show("Hoje não será feito apontamento automático no Redmine.", this, new Point(Px(12), Px(4)), 5000);
   });
   menu.Items.Add(redmineMenu);
   var startupItem = new ToolStripMenuItem("Iniciar com o Windows") { Checked = Program.StartsWithWindows(), Enabled = Program.AppFolder.Length > 0 };
   startupItem.Click += delegate {
    try { Program.SetStartup(!startupItem.Checked); } catch { MessageBox.Show(this,"Não foi possível alterar a inicialização automática."); }
    startupItem.Checked = Program.StartsWithWindows();
   };
   menu.Items.Add(startupItem);
   menu.Items.Add(new ToolStripSeparator());
   menu.Items.Add("Configurar", null, delegate { Show(); Configure(); });
   menu.Items.Add("Ver uso no Claude", null, delegate { Program.OpenUsage(); });
   menu.Items.Add("Desconectar e apagar chave", null, delegate { Disconnect(); });
   menu.Items.Add("Sair", null, delegate { Close(); }); ContextMenuStrip = menu; Theme.Style(menu);
   tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Meu Claude · Uso", ContextMenuStrip = menu, Visible = !preview };
   tray.DoubleClick += delegate { Show(); Activate(); };
   tray.BalloonTipClicked += delegate { if (balloonAction != null) balloonAction(); else if (balloonSession != null) FocusSession(balloonSession); };
   anim.Tick += delegate { AnimateConfetti(); };
   UpdateTray();
   state.Click += delegate { ShowStatusTip(); };
   AttachDrag(this); AttachDrag(title);
   title.MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleCompact(); };
   MouseDoubleClick += delegate(object sender, MouseEventArgs e) {
    if (e.Button != MouseButtons.Left) return;
    Rectangle usage, task, radar; DenseColumns(out usage, out task, out radar);
    if (Dense ? usage.Contains(e.Location) : e.X < Px(254)) ToggleCompact();
   };
   MouseEnter += delegate { hovering = true; Invalidate(); };
   MouseLeave += delegate { hovering = false; Invalidate(); };
   MouseClick += delegate(object sender, MouseEventArgs e) {
    if (!settings.Compact || e.Button != MouseButtons.Left) return;
    if (e.X >= Width-Px(Dense ? 22 : 28)) menu.Show(this, new Point(Width - 10, 0), ToolStripDropDownDirection.AboveLeft);
    else if (Dense) {
     Rectangle usage, task, radar; DenseColumns(out usage, out task, out radar);
     if (task.Contains(e.Location)) GoToTask(); else if (radar.Contains(e.Location)) ShowRadar();
    }
    else if (taskShown && TaskPill.Contains(e.Location)) { if (!dragged) GoToTask(); }
    else if (e.X >= Px(254)+Lead && !dragged) ShowRadar();
   };
   timer.Interval = 1000; timer.Tick += async delegate {
    if (stopped) return; PinCompact();
    if (DateTimeOffset.UtcNow >= nextCleanup) { nextCleanup = DateTimeOffset.UtcNow.AddHours(1); var cleanup = Task.Run(() => RadarReader.Cleanup()); }
    if (DateTimeOffset.UtcNow >= nextRadar) RefreshRadar();
    if (settings.RedmineAuto && !redmineBusy && DateTimeOffset.UtcNow >= nextRedmine && WorkDay.Due(DateTime.Now, settings.RedmineLastRun)) RunRedmine();
    if (settings.RedmineClock && !clockBusy && DateTimeOffset.UtcNow >= nextClock) RefreshTaskClock();
    if (settings.RedmineCelebrate && !celebrateBusy && DateTimeOffset.UtcNow >= nextCelebrate) CheckFinished();
    if (settings.Compact && settings.DockTaskbar && !slotBusy && DateTimeOffset.UtcNow >= nextSlot) MeasureTaskbar();
    // Friday: the scoreboard opens after the 17:55 time entry.
    DateTime local = DateTime.Now; string day = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    if (settings.MorningBrief && !briefBusy && DateTimeOffset.UtcNow > startedAt.AddSeconds(20) && Recap.Due(local, settings.BriefingShown)) {
     settings.BriefingShown = day; TrySave(); ShowBrief(true);
    }
    if (local.DayOfWeek == DayOfWeek.Friday && local.TimeOfDay >= WorkDay.RunAt && settings.RedmineScoreShown != day && !redmineBusy && (!settings.RedmineAuto || settings.RedmineLastRun == day)) {
     settings.RedmineScoreShown = day; TrySave(); ShowScore(true);
    }
    UpdateTaskClock();
    if (!busy && DateTimeOffset.UtcNow >= next && settings.Mode.Length > 0) await RefreshUsage();
    UpdateLabels(); Invalidate();
   };
   Shown += async delegate {
    if (preview) return;
    Arrange(); RestorePosition(); PinCompact(); RefreshRadar();
    foregroundHook = Taskbar.WatchForeground(OnForeground);
    timer.Start(); if (settings.Mode.Length == 0) Configure(); else await RefreshUsage();
   };
   FormClosing += delegate { stopped = true; timer.Stop(); if (foregroundHook != IntPtr.Zero) Taskbar.UnhookWinEvent(foregroundHook); RememberPosition(); TrySave(); tray.Visible = false; tray.Dispose(); if (trayHandle != IntPtr.Zero) Native.DestroyIcon(trayHandle); api.Dispose(); timer.Dispose(); anim.Stop(); anim.Dispose(); menu.Dispose(); hint.Dispose(); };
  }
  void Balloon(string caption, string text, string session) { balloonSession = session; balloonAction = null; tray.ShowBalloonTip(5000, caption, text, ToolTipIcon.Info); }
  static void OpenUrl(string url) {
   if (String.IsNullOrEmpty(url)) return;
   try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
  }
  // Polls my issues (the ones I logged hours on) for new "Finalizada"; the first run only sets the starting point.
  async void CheckFinished() {
   if (preview) return;
   celebrateBusy = true; nextCelebrate = DateTimeOffset.UtcNow.AddMinutes(5);
   try {
    using (var client = RedmineClient.FromMcpConfig()) {
     if (client == null) return;
     DateTimeOffset since;
     if (!DateTimeOffset.TryParse(settings.RedmineFinishedSince, CultureInfo.InvariantCulture, DateTimeStyles.None, out since)) {
      settings.RedmineFinishedSince = DateTimeOffset.UtcNow.ToString("o"); TrySave(); return;
     }
     if (DateTimeOffset.UtcNow >= nextMine) {
      myIssues = (await Score.Entries(client, DateTime.Today.AddDays(-90))).Select(e => e.Issue).Distinct().ToList();
      nextMine = DateTimeOffset.UtcNow.AddHours(1);
     }
     var finished = await Score.FinishedAmong(client, myIssues.Concat(tasks.Select(t => t.Id)));
     var fresh = finished.Where(f => f.Closed.Value > since && !settings.RedmineCelebrated.Contains(f.Id)).OrderBy(f => f.Closed).ToList();
     if (fresh.Count == 0) return;
     var rejections = await Score.RejectionDates(client, fresh.Select(f => f.Id));
     foreach (var f in fresh) { f.Rejections = rejections[f.Id].Count; settings.RedmineCelebrated.Add(f.Id); }
     if (settings.RedmineCelebrated.Count > 200) settings.RedmineCelebrated.RemoveRange(0, settings.RedmineCelebrated.Count - 200);
     TrySave();
     DateTime monday = Score.Monday(DateTime.Now);
     int week = finished.Count(f => Score.Monday(f.Closed.Value.ToLocalTime().DateTime) == monday);
     var lines = fresh.Select(f => {
      string text = f.Rejections == 0 ? "Passou na QA de primeira 🔥" : "Finalizada depois de " + f.Rejections + (f.Rejections == 1 ? " rejeição" : " rejeições");
      if (f.Estimated > 0) text += " · " + WorkDay.Duration(f.Spent) + " de " + WorkDay.Duration(f.Estimated) + " estimadas";
      if (f.Estimated > 0 && f.Estimated - f.Spent >= 1) text += " ⚡";
      return (fresh.Count > 1 ? "#" + f.Id + ": " : "") + text;
     }).ToList();
     lines.Add(week + "ª tarefa finalizada da semana");
     var first = fresh[0];
     Celebrate(fresh.Count == 1 ? "#" + first.Id + " finalizada!" : fresh.Count + " tarefas finalizadas!",
      fresh.Count == 1 ? "🎉 #" + first.Id + " finalizada!" : "🎉 " + fresh.Count + " tarefas finalizadas!", String.Join("\n", lines), first.Url);
    }
   } catch { nextCelebrate = DateTimeOffset.UtcNow.AddMinutes(5); }
   finally { celebrateBusy = false; }
  }
  // Confetti over the widget for 3.5 s, a banner in the strip for 12 s and a Windows notification.
  void Celebrate(string bannerText, string caption, string text, string url) {
   banner = "★ " + bannerText; bannerUntil = DateTime.Now.AddSeconds(12); confettiUntil = DateTime.Now.AddSeconds(3.5);
   var random = new Random(); var colors = new[] { amber, accent, green, red, Color.FromArgb(98,167,230), ink };
   confetti = Enumerable.Range(0, 70).Select(i => new Confetto {
    X = (float)random.NextDouble() * Width, Y = -(float)random.NextDouble() * Height, VX = (float)(random.NextDouble() - 0.5) * Px(1.6f),
    VY = Px(0.6f) + (float)random.NextDouble() * Px(1.4f), Size = Px(2.5f) + (float)random.NextDouble() * Px(2.5f),
    Angle = random.Next(360), Spin = random.Next(-12, 12), Color = colors[random.Next(colors.Length)] }).ToList();
   anim.Start(); Invalidate();
   if (!preview) { tray.ShowBalloonTip(8000, caption, text, ToolTipIcon.Info); balloonSession = null; balloonAction = url == null ? (Action)null : () => OpenUrl(url); }
  }
  void AnimateConfetti() {
   if (confetti == null) { anim.Stop(); return; }
   bool more = DateTime.Now < confettiUntil;
   foreach (var c in confetti) {
    c.X += c.VX; c.Y += c.VY; c.Angle += c.Spin;
    if (c.Y > Height && more) { c.Y = -c.Size; } // keeps falling until the time is up
   }
   if (!more && confetti.All(c => c.Y > Height)) { confetti = null; anim.Stop(); }
   Invalidate();
  }
  void PaintConfetti(Graphics g) {
   if (confetti == null) return;
   foreach (var c in confetti) {
    var state = g.Save(); g.TranslateTransform(c.X, c.Y); g.RotateTransform(c.Angle);
    using (var brush = new SolidBrush(c.Color)) g.FillRectangle(brush, -c.Size / 2, -c.Size / 4, c.Size, c.Size / 2);
    g.Restore(state);
   }
  }
  // Automatic: builds quietly and shows a notification; clicking it opens the full summary.
  async void ShowBrief(bool automatic) {
   if (preview || briefBusy) return;
   if (!automatic && briefForm != null && !briefForm.IsDisposed) { briefForm.Activate(); return; }
   BriefingForm form = null;
   if (!automatic) { form = briefForm = NewBriefForm(); form.Show(this); }
   briefBusy = true;
   try {
    RedmineClient client = null; try { client = RedmineClient.FromMcpConfig(); } catch { }
    Briefing brief;
    using (client) brief = await Recap.Build(client, tasks.Select(t => t.Id).ToList(), UsageLine(), DateTime.Now);
    if (form != null) { if (!form.IsDisposed) form.SetBriefing(brief); }
    else {
     tray.ShowBalloonTip(15000, "☀️ Bom dia! Onde você parou", Recap.Toast(brief), ToolTipIcon.Info);
     balloonSession = null; balloonAction = () => OpenBrief(brief);
    }
   } catch (Exception e) { if (form != null && !form.IsDisposed) form.Fail("Não foi possível montar o resumo: " + e.Message); }
   finally { briefBusy = false; }
  }
  void OpenBrief(Briefing brief) {
   if (briefForm != null && !briefForm.IsDisposed) { briefForm.Activate(); return; }
   var form = briefForm = NewBriefForm(); form.Show(this); form.SetBriefing(brief);
  }
  // "Atualizar" closes and rebuilds the summary in a fresh window.
  BriefingForm NewBriefForm() {
   var form = new BriefingForm(OpenUrl, FocusSession);
   form.Reload = () => { form.Close(); ShowBrief(false); };
   return form;
  }
  string UsageLine() {
   var parts = new List<string>();
   if (buckets.Count > 0 && buckets[0].Percent.HasValue) parts.Add("Sessão de 5h: " + Data.Percent(buckets[0].Percent.Value));
   if (buckets.Count > 1 && buckets[1].Percent.HasValue) parts.Add("Semana: " + Data.Percent(buckets[1].Percent.Value) + " · " + Data.ResetLabel(buckets[1]).ToLowerInvariant());
   return String.Join(" · ", parts);
  }
  async void ShowScore(bool automatic = false) {
   if (scoreForm != null && !scoreForm.IsDisposed) { scoreForm.Activate(); return; }
   RedmineClient client;
   try { client = RedmineClient.FromMcpConfig(); } catch (Exception e) { if (!automatic) MessageBox.Show(this, e.Message, "Placar da semana"); return; }
   if (client == null) { if (!automatic) MessageBox.Show(this, "Redmine não configurado: adicione o servidor MCP \"redmine\" em %USERPROFILE%\\.claude\\mcp-config.json.", "Placar da semana"); return; }
   var form = scoreForm = new ScoreForm(settings, TrySave, OpenUrl, d => OpenUrl(client.NewEntryUrl(d)));
   form.FormClosed += delegate { client.Dispose(); };
   form.Show(this);
   try {
    var report = await Score.Week(client, DateTime.Now, settings.WeekRejections.Keys.ToList());
    foreach (var pair in report.Rejections) settings.WeekRejections[pair.Key] = pair.Value;
    string oldest = Score.Key(report.Monday.AddDays(-7 * 26));
    foreach (var key in settings.WeekRejections.Keys.Where(k => String.CompareOrdinal(k, oldest) < 0).ToList()) settings.WeekRejections.Remove(key);
    TrySave();
    if (!form.IsDisposed) form.SetReport(report, Score.Streak(settings.WeekRejections, report.Monday));
   } catch (Exception e) { if (!form.IsDisposed) form.Fail("Não foi possível montar o placar.\n" + e.Message); }
  }
  // Daily Redmine time entry: once per workday from 17:55; retries every 5 minutes on failure.
  async void RunRedmine() {
   if (preview) return;
   redmineBusy = true; DateTime now = DateTime.Now; string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   try {
    List<LogItem> items = new List<LogItem>();
    using (var client = RedmineClient.FromMcpConfig()) if (client != null) items = await AutoLog.Run(client, now);
    settings.RedmineLastRun = today; TrySave();
    var posted = items.Where(x => x.Posted).ToList();
    if (posted.Count > 0)
     Balloon("Redmine · horas apontadas", String.Join("\n", posted.Select(x => WorkDay.Amount(x.Hours) + " na #" + x.Issue + " (desde " + AutoLog.Since(x, now) + ")")), null);
   } catch (Exception e) {
    nextRedmine = DateTimeOffset.UtcNow.AddMinutes(5);
    if (redmineErrorDay != today) { redmineErrorDay = today; Balloon("Redmine · apontamento falhou", e.Message + " Nova tentativa em 5 min.", null); }
   } finally { redmineBusy = false; }
  }
  async void PreviewRedmine() {
   DateTime now = DateTime.Now; var items = new List<LogItem>(); string error = "";
   try {
    using (var client = RedmineClient.FromMcpConfig()) {
     if (client == null) error = "Redmine não configurado: adicione o servidor MCP \"redmine\" (REDMINE_URL e REDMINE_API_KEY) em %USERPROFILE%\\.claude\\mcp-config.json.";
     else items = await AutoLog.Plan(client, now);
    }
   }
   catch (Exception e) { error = e.Message; }
   string schedule = !settings.RedmineAuto ? "Apontamento automático desligado"
    : settings.RedmineLastRun == now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? "Hoje já foi apontado (ou pulado)"
    : !WorkDay.IsWorkday(now) ? "Hoje não é dia útil: nada será apontado"
    : "Apontamento automático hoje às 17:55";
   using (var form = new PreviewForm(items, error, schedule, now)) form.ShowDialog(this);
  }
  async void RefreshTaskClock() {
   if (preview) return;
   clockBusy = true; nextClock = DateTimeOffset.UtcNow.AddMinutes(3);
   try {
    using (var client = RedmineClient.FromMcpConfig()) {
     redmineHost = client == null ? "" : client.Host;
     tasks = client == null ? new List<ActiveIssue>() : await AutoLog.Load(client, DateTime.MaxValue);
    }
   } catch { nextClock = DateTimeOffset.UtcNow.AddMinutes(1); } // keeps the last known issues
   finally { clockBusy = false; }
   UpdateTaskClock();
  }
  // Recomputes the hours and shows or hides the pill, keeping the strip's right edge in place.
  void UpdateTaskClock() {
   bool show = settings.RedmineClock && tasks.Count > 0;
   DateTime now = DateTime.Now; string stamp = now.ToString("yyyyMMddHHmm") + tasks.GetHashCode();
   if (settings.RedmineClock && stamp != clockStamp) {
    // Every "Em andamento" period counts; time in any other status (pausa, aguardando terceiros, feedback…) does not.
    clockStamp = stamp;
    var spans = tasks.ToDictionary(t => t.Id, t => t.Spans);
    taskTotal = WorkDay.Totals(spans, now);
    taskToday = WorkDay.IsWorkday(now) ? WorkDay.Hours(spans, now.Date, now) : spans.Keys.ToDictionary(k => k, k => 0.0);
    WriteClock();
   } else if (!settings.RedmineClock && clockStamp.Length > 0) { clockStamp = ""; WriteClock(); }
   if (show == taskShown) return;
   if (Docked) { taskShown = show; ApplyDock(); Invalidate(); return; }
   int baseLeft = Left + Lead, bottom = Bottom;
   taskShown = show; Arrange();
   if (settings.Compact) Left = baseLeft - Lead; else Top = bottom - Height;
   PinCompact();
  }
  async void MeasureTaskbar() {
   if (preview || stopped) return;
   slotBusy = true; nextSlot = DateTimeOffset.UtcNow.AddSeconds(2);
   TaskbarSlot found = null;
   try { found = await Task.Run(() => Taskbar.Measure()); } catch { }
   finally { slotBusy = false; }
   if (stopped || !settings.DockTaskbar) return;
   bool wasDocked = Docked; slot = found;
   if (slot == null && wasDocked) { dockWidth = 0; Arrange(); RestorePosition(); return; } // vertical taskbar or not found: free mode
   ApplyDock();
  }
  // Puts the strip in the chosen free room of the taskbar; dense layout when the room is narrower than the full strip.
  void Place(Point at) {
   dockPlace = at;
   if (Location == at) return;
   placing = true; try { Location = at; } finally { placing = false; }
  }
  void ApplyDock() {
   if (!Docked || stopped) return;
   // Steps off-screen (instead of Hide/Show, which could take the focus from the app in use).
   if (slot.Hidden || slot.Fullscreen) { Place(new Point(-32000, -32000)); return; }
   bool left = settings.DockSide == "left" || (settings.DockSide != "right" && slot.LeftGap.Width >= slot.RightGap.Width);
   Rectangle gap = left ? slot.LeftGap : slot.RightGap;
   int width = Math.Max(Px(180), Math.Min(FullWidth, gap.Width)), height = Px(40);
   var place = new Point(left ? gap.Left : gap.Right - width, slot.Bar.Top + (slot.Bar.Height - height) / 2);
   if (width != dockWidth || Width != width) { placing = true; try { dockWidth = width; Arrange(); } finally { placing = false; } }
   Place(place);
   // Taskbar color on both sides (Mica is a horizontal gradient), painted as the strip's background so it reads as part of the bar.
   int middle = slot.Bar.Top + slot.Bar.Height / 2;
   Color a = Taskbar.Sample(new Point(place.X - 3, middle)), b = Taskbar.Sample(new Point(place.X + width + 3, middle));
   if (!a.IsEmpty && !b.IsEmpty && (a.ToArgb() != dockLeft.ToArgb() || b.ToArgb() != dockRight.ToArgb())) {
    dockLeft = Color.FromArgb(255, a); dockRight = Color.FromArgb(255, b);
    dockBack = Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2); BackColor = dockBack; Invalidate();
   }
   PinCompact();
  }
  // Clicking the taskbar raises it over the strip; the strip goes back on top right away, and steps aside for full-screen apps.
  void OnForeground() {
   if (!Docked || stopped) return;
   slot.Fullscreen = Taskbar.ForegroundFullscreen();
   ApplyDock();
  }
  // Shares the hours with the Claude Code status line (StatusLine.ps1), which shows them in the
  // terminal of the session whose user messages link the issue. Local file only; no Redmine key.
  static readonly string ClockPath = Path.Combine(Data.Folder, "redmine-clock.json");
  void WriteClock() {
   if (preview) return;
   try {
    if (!settings.RedmineClock || redmineHost.Length == 0) { if (File.Exists(ClockPath)) File.Delete(ClockPath); return; }
    var list = tasks.Select(t => {
     double total, today; taskTotal.TryGetValue(t.Id, out total); taskToday.TryGetValue(t.Id, out today);
     return new Dictionary<string,object> { { "Id", t.Id }, { "Total", WorkDay.Duration(total) }, { "Today", WorkDay.Duration(today) }, { "Periods", t.Spans.Count } };
    }).ToList();
    var doc = new Dictionary<string,object> { { "Host", redmineHost }, { "Updated", DateTimeOffset.UtcNow.ToString("o") }, { "Tasks", list } };
    Directory.CreateDirectory(Data.Folder);
    string temp = ClockPath + ".tmp";
    File.WriteAllText(temp, Data.Json().Serialize(doc), new UTF8Encoding(false));
    if (File.Exists(ClockPath)) File.Replace(temp, ClockPath, null); else File.Move(temp, ClockPath);
   } catch { }
  }
  // Pill click: the terminal of the session working on the issue, or the issue in the browser.
  void GoToTask() {
   var task = CurrentTask; if (task == null) return;
   var session = TaskSession(task.Id);
   if (session != null) FocusSession(session.Id); else OpenTask();
  }
  void OpenTask() {
   var task = CurrentTask; if (task == null) return;
   try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(task.Url) { UseShellExecute = true }); } catch { }
  }
  static string Periods(ActiveIssue t) { return t.Spans.Count == 1 ? "1 período" : t.Spans.Count + " períodos"; }
  string TaskLine(ActiveIssue t, DateTime now) {
   double total, today; taskTotal.TryGetValue(t.Id, out total); taskToday.TryGetValue(t.Id, out today);
   string line = "#" + t.Id + " · " + WorkDay.Duration(total) + " em andamento (" + Periods(t) + ", atual desde " + WorkDay.SinceLabel(t.Current.Start, now) + ") · hoje " + WorkDay.Duration(today);
   if (t.Estimated >= 0) line += " · estimativa " + WorkDay.Amount(t.Estimated);
   return line + " · apontado " + WorkDay.Amount(t.Spent);
  }
  void FocusSession(string id) {
   SessionInfo s = sessions.Find(x=>x.Id==id);
   string problem = s == null ? "Sessão não encontrada." : s.Closed || s.State == "ended" ? "Esta sessão já foi fechada." : s.WindowPid <= 0 ? "Terminal desconhecido: rode Ativar-Radar.cmd de novo e reabra a sessão." : Program.FocusWindow(s.WindowPid) ? "" : "O terminal desta sessão não foi encontrado.";
   if (problem.Length > 0) hint.Show(problem, this, new Point(Px(12), Px(4)), 5000);
  }
  // Tray icon shows the 5h percentage in the same colors as the strip.
  void UpdateTray() {
   Bucket b = buckets.Count > 0 ? buckets[0] : null;
   bool known = b != null && b.Percent.HasValue;
   string text = known ? Math.Min(999, Math.Round(b.Percent.Value)).ToString(CultureInfo.InvariantCulture) : "–";
   Color color = stale || !known ? Color.FromArgb(135,137,142) : b.Percent.Value >= 90 ? red : b.Percent.Value >= 70 ? amber : accent;
   string tip = "Meu Claude";
   for (int i = 0; i < 2 && i < buckets.Count; i++) tip += (i == 0 ? " · 5h " : " · 7d ") + (buckets[i].Percent.HasValue ? Data.Percent(buckets[i].Percent.Value) : "—");
   if (stale && last.HasValue) tip += " (antigo)";
   tray.Text = tip.Length > 63 ? tip.Substring(0,63) : tip;
   string key = text + "|" + color.ToArgb();
   if (key == trayKey) return; trayKey = key;
   Size size = SystemInformation.SmallIconSize;
   using (var bitmap = new Bitmap(size.Width,size.Height))
   using (var g = Graphics.FromImage(bitmap)) {
    g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; g.Clear(Color.Transparent);
    using (var shape = Rounded(new RectangleF(0,0,size.Width-1,size.Height-1),size.Width/4f)) using (var brush = new SolidBrush(color)) g.FillPath(brush,shape);
    using (var font = new Font("Segoe UI",size.Height*(text.Length >= 3 ? .45f : .62f),FontStyle.Bold,GraphicsUnit.Pixel))
    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
    using (var brush = new SolidBrush(Color.FromArgb(27,29,32))) g.DrawString(text,font,brush,new RectangleF(0,1,size.Width,size.Height),format);
    IntPtr handle = bitmap.GetHicon();
    tray.Icon = Icon.FromHandle(handle);
    if (trayHandle != IntPtr.Zero) Native.DestroyIcon(trayHandle);
    trayHandle = handle;
   }
  }
  // Warns once per level (80%, 95%) in each usage window, and when a well-used window renews.
  void CheckUsageAlerts() {
   for (int i = 0; i < 2 && i < buckets.Count; i++) {
    Bucket b = buckets[i]; if (!b.Percent.HasValue) continue;
    double p = b.Percent.Value; int level = p >= 95 ? 95 : p >= 80 ? 80 : 0;
    string name = i == 0 ? "sessão de 5h" : "semana";
    bool renewed = alertReset[i].HasValue && b.Reset.HasValue && b.Reset.Value > alertReset[i].Value.AddMinutes(30);
    if (renewed) alertLevel[i] = 0;
    if (usageWarmed && settings.UsageAlerts) {
     if (renewed && alertPercent[i].HasValue && alertPercent[i].Value >= 50 && p < alertPercent[i].Value) Balloon("Claude · limite renovado", "O uso da " + name + " voltou para " + Data.Percent(p) + ".", null);
     else if (level > alertLevel[i]) Balloon("Claude · " + name + " em " + Data.Percent(p), Data.ResetLabel(b) + "\n" + Pace.Describe(history,i,b,DateTimeOffset.UtcNow), null);
    }
    alertLevel[i] = Math.Max(alertLevel[i], level); alertReset[i] = b.Reset; alertPercent[i] = p;
   }
   usageWarmed = true;
  }
  void AddButton(string text, EventHandler handler) {
   var b = new Button { Text = text, Width = text == "×" ? 27 : 78, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42,44,48), ForeColor = ink, Margin = new Padding(0,0,4,0), TabStop = true };
   b.FlatAppearance.BorderSize = 0; b.Click += handler; actions.Controls.Add(b);
  }
  // Windows captures the mouse on any button press, so dragging uses its own flag: docked in the taskbar, it never moves.
  Point dragMouse, dragWindow; bool dragged, dragging;
  void AttachDrag(Control c) {
   c.MouseDown += delegate(object sender, MouseEventArgs e) {
    dragged = false; dragging = false;
    if (e.Button == MouseButtons.Left && !Docked && !(settings.Compact && c == this && e.X >= Width-Px(28))) { dragging = true; dragMouse = Cursor.Position; dragWindow = Location; c.Capture = true; }
   };
   c.MouseMove += delegate(object sender, MouseEventArgs e) {
    if (!dragging || e.Button != MouseButtons.Left || Docked) return;
    if (Math.Abs(Cursor.Position.X-dragMouse.X)+Math.Abs(Cursor.Position.Y-dragMouse.Y)>4) dragged=true;
    Location = new Point(dragWindow.X + Cursor.Position.X - dragMouse.X, dragWindow.Y + Cursor.Position.Y - dragMouse.Y);
   };
   c.MouseUp += delegate { bool was = dragging; dragging = false; c.Capture = false; if (was && !Docked) { RememberPosition(); TrySave(); } };
  }
  void TrySave() { if (preview) return; try { Data.Save(settings); } catch { detail = "Não foi possível salvar preferências. Verifique a pasta LocalAppData."; } }
  string RadarTitle() {
   if (activeSession == null) return "Radar · aguardando Claude Code";
   return activeSession.Project + (activeSession.Issue > 0 ? " · #" + activeSession.Issue : "") + " · " + activeSession.Activity;
  }
  // Redmine issue linked in a session: "#123 · 1h30" when in progress, otherwise "fora de andamento".
  string SessionTask(SessionInfo s) {
   if (s == null || s.Issue <= 0) return "";
   if (!settings.RedmineClock || redmineHost.Length == 0) return "#" + s.Issue;
   double total;
   return tasks.Any(t => t.Id == s.Issue) && taskTotal.TryGetValue(s.Issue, out total) ? "#" + s.Issue + " · " + WorkDay.Duration(total) : "#" + s.Issue + " · fora de andamento";
  }
  // Most recent open session linked to the issue, if any.
  SessionInfo TaskSession(int issue) {
   return sessions.Where(s => s.Issue == issue && !s.Closed && s.State != "ended").OrderByDescending(s => s.Time).FirstOrDefault();
  }
  List<SessionInfo> OtherActive() { return sessions.Where(s=>s!=activeSession && s.Active).ToList(); }
  string OthersSummary() {
   var others = OtherActive(); if (others.Count == 0) return "";
   int waiting = others.Count(s=>s.State=="waiting");
   var parts = new List<string>();
   if (waiting > 0) parts.Add(waiting + " precisa(m) de você");
   if (others.Count-waiting > 0) parts.Add((others.Count-waiting) + " trabalhando");
   return "Outras sessões: " + String.Join(", ", parts) + " (" + String.Join(", ", others.Select(s=>s.Project).Distinct()) + ")";
  }
  string RadarDetails() {
   if (activeSession == null) return "Radar sem eventos. Execute Ativar-Radar.cmd uma vez, reabra o Claude Code e envie uma mensagem.\nA integração é para Claude Code no Windows, fora do WSL.";
   string usage = activeSession.Usage.Length > 0 ? "Modelo: " + activeSession.Usage + "\n" : "";
   if (activeSession.Issue > 0) usage += "Tarefa Redmine: " + SessionTask(activeSession) + "\n";
   string closed = activeSession.Closed ? "O processo do Claude Code desta sessão não está mais aberto.\n" : "";
   string others = OthersSummary(); if (others.Length > 0) others += "\n";
   return RadarTitle()+"\nSessão: "+activeSession.Id.Substring(0,8)+" · "+sessions.Count+" sessão(ões) nas últimas 24h\nÚltimo evento: "+activeSession.Event+" "+activeSession.Tool+" às "+activeSession.Time.ToLocalTime().ToString("dd/MM HH:mm:ss")+"\n"+usage+closed+others+gitInfo.Details+"\nEstado observado por eventos e pelo processo do Claude Code.";
  }
  void ShowRadar() {
   using (var form = new RadarForm(()=>new List<SessionInfo>(sessions),()=>RadarDetails(),id=>{
    settings.SelectedSession=id; SelectSession(); nextGit=DateTimeOffset.MinValue; TrySave(); UpdateLabels(); Invalidate(); RefreshGit();
   },FocusSession,SessionTask)) form.ShowDialog(this);
  }
  void SelectSession() {
   SessionInfo selected = sessions.Find(s=>s.Id==settings.SelectedSession);
   if (selected==null) selected=sessions.Find(s=>s.State!="ended" && !s.Closed);
   if (selected==null && sessions.Count>0) selected=sessions[0];
   if ((activeSession==null ? "" : activeSession.Cwd)!=(selected==null ? "" : selected.Cwd)) {
    gitInfo=new GitInfo { Error=selected==null ? "Aguardando projeto" : "Lendo Git…" }; nextGit=DateTimeOffset.MinValue;
   }
   activeSession=selected;
  }
  async void RefreshRadar() {
   if(radarBusy||stopped) return;
   radarBusy=true;
   try {
    var found=await Task.Run(()=>RadarReader.Read()); if(stopped)return;
    foreach(var session in found) {
     string prior; observed.TryGetValue(session.Id,out prior);
     if(radarWarmed && settings.RadarAlerts && prior!=session.Effective && DateTimeOffset.UtcNow-session.Time<TimeSpan.FromSeconds(15) && (session.Effective=="waiting"||session.Effective=="done"))
      Balloon("Claude · "+session.Project,session.Activity+(session.WindowPid>0 ? "\nClique para ir ao terminal." : ""),session.Id);
     observed[session.Id]=session.Effective;
    }
    radarWarmed=true; sessions=found; SelectSession(); UpdateLabels(); Invalidate(); RefreshGit();
   } catch { gitInfo=new GitInfo { Error="Radar local indisponível" }; }
   finally { radarBusy=false; nextRadar=DateTimeOffset.UtcNow.AddSeconds(2); }
  }
  async void RefreshGit() {
   if(gitBusy||stopped||activeSession==null||DateTimeOffset.UtcNow<nextGit)return;
   string path=activeSession.Cwd; gitBusy=true; nextGit=DateTimeOffset.UtcNow.AddSeconds(5);
   try {
    var value=await Task.Run(()=>GitReader.Read(path));
    if(!stopped && activeSession!=null && activeSession.Cwd==path) { gitInfo=value; UpdateLabels(); Invalidate(); }
   } catch { if(!stopped)gitInfo=new GitInfo { Error="Git indisponível" }; }
   finally { gitBusy=false; }
  }
  protected override bool ShowWithoutActivation { get { return settings != null && settings.Compact; } }
  protected override CreateParams CreateParams {
   get { var cp = base.CreateParams; cp.ExStyle |= 0x00000080; return cp; } // WS_EX_TOOLWINDOW
  }
  [StructLayout(LayoutKind.Sequential)] struct WindowPos { public IntPtr Hwnd, After; public int X, Y, Cx, Cy; public uint Flags; }
  Point dockPlace = new Point(Int32.MinValue, 0); bool placing;
  protected override void WndProc(ref Message m) {
   // MA_NOACTIVATE: moving/clicking the compact strip keeps the working app focused.
   if (m.Msg == 0x0021 && settings != null && settings.Compact) { m.Result = new IntPtr(3); return; }
   // WM_WINDOWPOSCHANGING: docked in the taskbar, any move that is not the widget's own is undone before it happens.
   if (m.Msg == 0x0046 && settings != null && Docked && !placing && dockPlace.X != Int32.MinValue) {
    var pos = (WindowPos)Marshal.PtrToStructure(m.LParam, typeof(WindowPos));
    if ((pos.Flags & 0x0002) == 0 && (pos.X != dockPlace.X || pos.Y != dockPlace.Y)) { pos.X = dockPlace.X; pos.Y = dockPlace.Y; Marshal.StructureToPtr(pos, m.LParam, false); }
   }
   base.WndProc(ref m);
  }
  void PinCompact() {
   if (settings.Compact && settings.OnTop && Visible && Enabled && !menu.Visible && IsHandleCreated)
    Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // no move/size/activate
  }
  void RememberPosition() {
   if (settings.Compact && Docked) return; // docked: the position comes from the taskbar, the free one stays saved
   if (settings.Compact) { settings.CompactX = Left + Lead; settings.CompactY = Top; }
   else { settings.X = Left; settings.Y = Top; }
  }
  void RestorePosition() {
   if (Docked) { ApplyDock(); return; }
   int x = settings.Compact ? (settings.CompactX == -10000 ? -10000 : settings.CompactX - Lead) : settings.X;
   int y = settings.Compact ? settings.CompactY : settings.Y;
   if (x != -10000) {
    var rect = new Rectangle(x,y,Width,Height);
    foreach (Screen screen in Screen.AllScreens) if ((settings.Compact ? screen.Bounds : screen.WorkingArea).Contains(rect)) { Location = rect.Location; return; }
   }
   if (settings.Compact) DockNearClock();
   else { Rectangle area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right-Width-Px(20), area.Bottom-Height-Px(20)); }
  }
  void DockNearClock() {
   Rectangle screen = Screen.PrimaryScreen.Bounds;
   Native.Rect bar, notify;
   IntPtr shell = Native.FindWindow("Shell_TrayWnd", null);
   if (shell != IntPtr.Zero && Native.GetWindowRect(shell,out bar) && bar.Right-bar.Left > bar.Bottom-bar.Top) {
    int right = bar.Right - Px(220); // fallback reserves the clock and system icons
    IntPtr area = Native.FindWindowEx(shell,IntPtr.Zero,"TrayNotifyWnd",null);
    if (area != IntPtr.Zero && Native.GetWindowRect(area,out notify) && notify.Left > bar.Left && notify.Left < bar.Right) right = notify.Left - Px(8);
    int x = Math.Max(screen.Left, Math.Min(right-Width,screen.Right-Width));
    int y = Math.Max(screen.Top, Math.Min(bar.Top + ((bar.Bottom-bar.Top)-Height)/2,screen.Bottom-Height));
    Location = new Point(x,y);
   } else {
    Rectangle work = Screen.PrimaryScreen.WorkingArea;
    Location = new Point(Math.Max(screen.Left,work.Right-Width-Px(180)),work.Bottom-Height-Px(6));
   }
   PinCompact();
  }
  void ToggleCompact() {
   RememberPosition(); settings.Compact = !settings.Compact; Arrange(); RestorePosition(); TrySave(); PinCompact();
  }
  static GraphicsPath Rounded(RectangleF rect, float radius) {
   var path = new GraphicsPath(); float d = Math.Min(radius*2,Math.Min(rect.Width,rect.Height));
   path.AddArc(rect.Left,rect.Top,d,d,180,90); path.AddArc(rect.Right-d,rect.Top,d,d,270,90);
   path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
  }
  void Arrange() {
   compactItem.Checked = settings.Compact;
   title.Visible = actions.Visible = state.Visible = timestamp.Visible = compactButton.Visible = radarSummary.Visible = !settings.Compact;
   taskLabel.Visible = !settings.Compact && taskShown;
   BackColor = settings.Compact ? (Docked && !dockBack.IsEmpty ? dockBack : Color.FromArgb(36,37,39)) : Color.FromArgb(27,29,32);
   Region oldRegion = Region;
   if (settings.Compact) {
    ClientSize = new Size(Docked && dockWidth > 0 ? dockWidth : FullWidth, Px(40));
    using (var shape = Rounded(new RectangleF(0,0,Width,Height),Px(8))) Region = new Region(shape);
    if (oldRegion != null) oldRegion.Dispose();
    UpdateLabels(); Invalidate(); return;
   }
   Region = null; if (oldRegion != null) oldRegion.Dispose();
   int count = Math.Max(2, buckets.Count), extra = taskShown ? 50 : 0; ClientSize = new Size(Px(370), Px(240 + extra + count * 88));
   title.SetBounds(Px(18),Px(17),Px(145),Px(27));
   actions.SetBounds(Px(155),Px(15),Px(204),Px(32));
   foreach (Control b in actions.Controls) { b.Width = Px(b.Text == "×" ? 27 : 78); b.Height = Px(28); }
   taskLabel.SetBounds(Px(18),Px(60 + count * 88),Px(334),Px(42));
   radarSummary.SetBounds(Px(18),Px(60 + extra + count * 88),Px(334),Px(92));
   state.SetBounds(Px(18),Px(160 + extra + count * 88),Px(334),Px(36));
   timestamp.SetBounds(Px(18),Px(201 + extra + count * 88),Px(224),Px(32));
   compactButton.SetBounds(Px(250),Px(201 + extra + count * 88),Px(102),Px(28));
   Invalidate();
  }
  void UpdateLabels() {
   state.Text = status; state.ForeColor = stale ? Color.FromArgb(232,179,112) : ink;
   string previous = last.HasValue ? "Última: " + last.Value.ToLocalTime().ToString("HH:mm:ss") : "Sem leitura recebida";
   int remaining = (int)Math.Max(0, Math.Ceiling((next - DateTimeOffset.UtcNow).TotalSeconds));
   timestamp.Text = previous + (settings.Mode.Length == 0 ? "" : busy ? " · consultando…" : " · próxima: " + remaining + "s");
   string usage = activeSession != null && activeSession.Usage.Length > 0 ? activeSession.Usage : "Clique para projetos e sessões";
   radarSummary.Text = RadarTitle()+"\n"+gitInfo.Summary+"\n"+usage;
   var summary = new StringBuilder("Meu Claude\n");
   summary.Append(RadarDetails()).Append("\n\n");
   if (taskShown && CurrentTask != null) {
    DateTime now = DateTime.Now; var task = CurrentTask; double total;
    taskTotal.TryGetValue(task.Id, out total);
    string subject = task.Subject.Length > 48 ? task.Subject.Substring(0, 48) + "…" : task.Subject;
    taskLabel.Text = "#" + task.Id + " · " + WorkDay.Duration(total) + " · " + Periods(task) + " · desde " + WorkDay.SinceLabel(task.Current.Start, now) + "\n" + subject;
    summary.Append("Redmine (horário de trabalho)\n");
    foreach (var t in tasks.OrderByDescending(x => x.Current.Start)) summary.Append(TaskLine(t, now)).Append("\n");
    summary.Append("\n");
   }
   for (int i = 0; i < buckets.Count; i++) {
    Bucket b = buckets[i];
    summary.Append(b.Title).Append(": ").Append(b.Percent.HasValue ? Data.Percent(b.Percent.Value) : "—").Append("\n").Append(Data.ResetLabel(b)).Append("\n");
    if (i < 2 && b.Percent.HasValue) summary.Append(Pace.Describe(history,i,b,DateTimeOffset.UtcNow)).Append("\n");
   }
   summary.Append(previous).Append("\n").Append(status).Append("\n").Append(detail).Append("\nArraste para mover · duplo clique para detalhes · botão direito para opções");
   string value = summary.ToString(); if (lastHint != value) { hint.SetToolTip(this,value); lastHint = value; }
   UpdateTray();
  }
  void ShowStatusTip() {
   hint.Show(status + "\n\n" + detail, this, new Point(Px(12), Px(4)), 10000);
  }
  async void Configure() {
   if (busy) { MessageBox.Show(this,"Aguarde a consulta terminar (até 25 segundos por requisição)."); return; }
   timer.Stop();
   using (var form = new SetupForm(settings)) {
    if (form.ShowDialog(this) == DialogResult.OK) {
     settings = form.Value; TopMost = settings.OnTop; buckets.Clear(); last = null; next = DateTimeOffset.MinValue; stale = true; Arrange();
    }
   }
   timer.Start(); if (settings.Mode.Length > 0) await RefreshUsage();
  }
  void Disconnect() {
   if (busy) { MessageBox.Show(this,"Aguarde a consulta terminar antes de desconectar."); return; }
   settings.Mode = ""; settings.ProtectedSession = ""; settings.Organization = ""; TrySave();
   buckets.Clear(); last = null; stale = true; status = "Desconectado. Configure para começar."; detail = ""; Arrange(); UpdateLabels();
  }
  string ChooseOrganization(object response) {
   var array = response as IEnumerable;
   var options = new List<KeyValuePair<string,string>>();
   if (array != null) foreach (object item in array) {
    var obj = Data.Map(item); string id = Data.Str(Data.Get(obj,"uuid")); Guid guid;
    if (Guid.TryParse(id,out guid)) options.Add(new KeyValuePair<string,string>(id, Data.Str(Data.Get(obj,"name")) + " · " + id));
   }
   if (options.Count == 0) throw new RequestFailure("Nenhuma organização encontrada para esta sessão.", 0);
   if (options.Count == 1) return options[0].Key;
   using (var dialog = new Form { Text = "Escolha a conta / organização", ClientSize = new Size(560,140), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI",10), AutoScaleMode = AutoScaleMode.Dpi }) {
    var list = new ComboBox { Left = 16, Top = 20, Width = 528, DropDownStyle = ComboBoxStyle.DropDownList };
    foreach (var opt in options) list.Items.Add(opt.Value); list.SelectedIndex = 0; dialog.Controls.Add(list);
    var button = new Button { Text = "Usar esta conta", Left = 350, Top = 78, Width = 190, Height = 34, DialogResult = DialogResult.OK }; dialog.Controls.Add(button); dialog.AcceptButton = button;
    if (dialog.ShowDialog(this) == DialogResult.OK) return options[list.SelectedIndex].Key;
   }
   throw new RequestFailure("Escolha uma organização na próxima consulta.", 0);
  }
  async Task RefreshUsage() {
   if (busy || stopped || settings.Mode.Length == 0) return;
   // One request cycle per minute; manual refresh also respects an explicit Retry-After.
   if (DateTimeOffset.UtcNow < next) { UpdateLabels(); return; }
   busy = true; status = "Consultando seu uso…"; UpdateLabels();
   DateTimeOffset started = DateTimeOffset.UtcNow;
   int serverDelay = 0;
   try {
    string data;
    if (settings.Mode == "code") data = await api.Get("https://api.anthropic.com/api/oauth/usage", Data.CodeToken(), true);
    else {
     string secret;
     try { secret = Data.Unprotect(settings.ProtectedSession); } catch { throw new RequestFailure("Não foi possível abrir a chave salva. Informe-a novamente em Configurar.", 0); }
     if (String.IsNullOrWhiteSpace(settings.Organization)) {
      string raw = await api.Get("https://claude.ai/api/organizations",secret,false);
      if (stopped) return;
      settings.Organization = ChooseOrganization(Data.Json().DeserializeObject(raw)); TrySave();
     }
     Guid id; if (!Guid.TryParse(settings.Organization,out id)) throw new RequestFailure("Organização inválida. Apague o campo em Configurar para descobrir novamente.", 0);
     data = await api.Get("https://claude.ai/api/organizations/" + id.ToString() + "/usage",secret,false);
    }
    if (stopped) return;
    buckets = Data.ParseUsage(data); last = DateTimeOffset.UtcNow; stale = false;
    if (Pace.Add(history, buckets, last.Value)) { try { Pace.Save(history); } catch { } }
    CheckUsageAlerts();
    status = "Conectado · " + (settings.Mode == "code" ? "login atual do Claude Code" : "sessão do navegador");
    detail = "Percentuais retornados pelo Claude. Atualização periódica, não instantânea.\n" + (settings.Mode == "code" ? "Acompanha a conta atualmente conectada no Claude Code do Windows." : "Organização: " + settings.Organization);
    Arrange();
   } catch (RequestFailure ex) { Failure(ex.Message); serverDelay = ex.Delay; }
   catch (TaskCanceledException) { Failure("Consulta demorou demais. Verifique sua conexão."); }
   catch (HttpRequestException) { Failure("Sem conexão com Claude. Nova tentativa automática."); }
   catch { Failure("Não foi possível ler os dados. Confira o login e a página oficial de uso."); }
   finally {
    busy = false;
    if (!stopped) {
     DateTimeOffset now = DateTimeOffset.UtcNow;
     next = started.AddSeconds(60);
     if (next <= now) next = now.AddSeconds(1);
     if (serverDelay > 0) {
      DateTimeOffset allowed = now.AddSeconds(serverDelay);
      if (allowed > next) next = allowed;
     }
     UpdateLabels(); Invalidate();
    }
   }
  }
  void Failure(string message) {
   stale = true; detail = message; status = (last.HasValue ? "Dado desatualizado" : "Sem dados") + " · tentativa automática";
   // When the usage API refuses (429, 403...), the limits Claude Code passed to its status line are used without any request.
   var local = RadarReader.LatestLimits(); DateTimeOffset time;
   if (local == null || !DateTimeOffset.TryParse(local.Updated, out time) || (last.HasValue && time <= last.Value) || DateTimeOffset.UtcNow-time > TimeSpan.FromHours(6)) return;
   var fresh = new List<Bucket> { LocalBucket("Sessão · 5 horas", local.Five, local.FiveReset), LocalBucket("Semana · todos os modelos", local.Week, local.WeekReset) };
   fresh.AddRange(buckets.Skip(2));
   buckets = fresh; last = time; stale = DateTimeOffset.UtcNow-time > TimeSpan.FromMinutes(15);
   status = "Uso via barra de status do Claude Code" + (stale ? " · leitura antiga" : "");
   detail = message + "\nPercentuais lidos localmente da barra de status do Claude Code às " + time.ToLocalTime().ToString("HH:mm") + ", sem requisição.";
   if (Pace.Add(history, buckets, time)) { try { Pace.Save(history); } catch { } }
   CheckUsageAlerts(); Arrange();
  }
  static Bucket LocalBucket(string title, double percent, string reset) {
   DateTimeOffset time;
   return new Bucket { Title = title, Percent = percent >= 0 ? percent : (double?)null, Reset = DateTimeOffset.TryParse(reset, out time) ? time : (DateTimeOffset?)null };
  }
  string ShortReset(Bucket b) {
   if (!b.Reset.HasValue) return "sem previsão";
   var left = b.Reset.Value-DateTimeOffset.UtcNow;
   if (left.TotalSeconds <= 0) return "atualizando…";
   if (left.TotalDays >= 1) return "renova " + (int)left.TotalDays + "d " + left.Hours + "h";
   if (left.TotalHours >= 1) return "renova " + (int)left.TotalHours + "h " + left.Minutes + "m";
   return "renova " + Math.Max(1,(int)Math.Ceiling(left.TotalMinutes)) + "min";
  }
  Color StateColor(SessionInfo s) {
   if (s == null || s.Closed || s.Silent || s.State == "ended") return muted;
   switch (s.State) { case "working": case "background": return accent; case "waiting": return amber; case "done": return green; case "error": return red; default: return ink; }
  }
  // Narrow taskbar room: three columns of two lines, like the weather button (usage · Redmine issue · Claude session).
  void DenseColumns(out Rectangle usage, out Rectangle task, out Rectangle radar) {
   usage = new Rectangle(Px(6), 0, Px(70), Height);
   int x = usage.Right + Px(9);
   task = taskShown && CurrentTask != null ? new Rectangle(x, 0, Px(92), Height) : Rectangle.Empty;
   if (!task.IsEmpty) x = task.Right + Px(9);
   radar = new Rectangle(x, 0, Math.Max(0, Width - Px(24) - x), Height);
  }
  void PaintDense(Graphics g) {
   Rectangle usage, task, radar; DenseColumns(out usage, out task, out radar);
   var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
   using (var normal = new Font("Segoe UI", 8f)) using (var bold = new Font("Segoe UI Semibold", 8.5f)) using (var tiny = new Font("Segoe UI", 7.2f))
   using (var divider = new Pen(Color.FromArgb(40, 255, 255, 255))) {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    for (int i = 0; i < 2; i++) {
     Bucket b = buckets.Count > i ? buckets[i] : new Bucket();
     Color baseColor = i == 0 ? amber : Color.FromArgb(98,167,230);
     DateTimeOffset when; bool runsOut = !stale && buckets.Count > i && Pace.RunsOut(history, i, b, now, out when);
     Color color = stale ? Color.FromArgb(135,137,142) : (b.Percent.HasValue && b.Percent.Value >= 90) || runsOut ? red : baseColor;
     // Same pill as the full strip: filled up to the usage, label in the bucket color, value on the right.
     var pill = new Rectangle(usage.X, i == 0 ? Px(4) : Px(21), usage.Width, Px(15));
     using (var shape = Rounded(pill, Px(7.5f))) {
      using (var background = new SolidBrush(Color.FromArgb(48,50,53))) g.FillPath(background, shape);
      var saved = g.Save(); g.SetClip(shape);
      if (b.Percent.HasValue) using (var fill = new SolidBrush(Color.FromArgb(95, color))) g.FillRectangle(fill, pill.X, pill.Y, (float)(pill.Width * Math.Min(100, b.Percent.Value) / 100), pill.Height);
      g.Restore(saved);
     }
     TextRenderer.DrawText(g, i == 0 ? "5h" : "7d", tiny, new Rectangle(pill.X + Px(6), pill.Y, pill.Width, pill.Height), color, flags);
     TextRenderer.DrawText(g, b.Percent.HasValue ? Data.Percent(b.Percent.Value) : "—", bold, new Rectangle(pill.X, pill.Y, pill.Width - Px(6), pill.Height), ink, (flags & ~TextFormatFlags.Left) | TextFormatFlags.Right);
    }
    g.DrawLine(divider, usage.Right + Px(4), Px(9), usage.Right + Px(4), Height - Px(9));
    var current = CurrentTask;
    if (!task.IsEmpty && current != null) {
     double total; taskTotal.TryGetValue(current.Id, out total);
     TextRenderer.DrawText(g, "#" + current.Id, normal, new Rectangle(task.X, Px(4), task.Width, Px(15)), green, flags);
     TextRenderer.DrawText(g, WorkDay.Duration(total) + (current.Spans.Count > 1 ? "  " + current.Spans.Count + "x" : ""), bold, new Rectangle(task.X, Px(21), task.Width, Px(15)), ink, flags);
     g.DrawLine(divider, task.Right + Px(4), Px(9), task.Right + Px(4), Height - Px(9));
    }
    bool celebrating = DateTime.Now < bannerUntil;
    string headline = celebrating ? banner : activeSession == null ? "Radar · aguardando" : activeSession.Project + " · " + activeSession.Label;
    TextRenderer.DrawText(g, headline, celebrating ? bold : normal, new Rectangle(radar.X, Px(4), radar.Width, Px(15)), celebrating ? amber : StateColor(activeSession), flags);
    var detail = new List<string>();
    if (activeSession != null) {
     if (activeSession.Elapsed.Length > 0) detail.Add(activeSession.Elapsed);
     if (activeSession.Context >= 0) detail.Add("ctx " + Math.Round(activeSession.Context) + "%");
    }
    detail.Add(gitInfo.Summary);
    TextRenderer.DrawText(g, String.Join(" · ", detail), tiny, new Rectangle(radar.X, Px(21), radar.Width, Px(15)), muted, flags);
    // Other active sessions: small dots (amber needs you, accent working); then the menu dots.
    var others = OtherActive(); int waiting = others.Count(s => s.State == "waiting"), x = Width - Px(18);
    if (others.Count - waiting > 0) using (var brush = new SolidBrush(accent)) g.FillEllipse(brush, x, Px(8), Px(6), Px(6));
    if (waiting > 0) using (var brush = new SolidBrush(amber)) g.FillEllipse(brush, x + Px(7), Px(8), Px(6), Px(6));
    using (var dots = new SolidBrush(Color.FromArgb(181,183,187))) for (int i = 0; i < 3; i++) g.FillEllipse(dots, Width - Px(15) + i * Px(4), Px(26), Px(2), Px(2));
   }
  }
  void PaintCompact(Graphics g) {
   if (Docked && !dockLeft.IsEmpty && Width > 0)
    using (var back = new LinearGradientBrush(new Rectangle(-1, 0, Width + 2, Height), dockLeft, dockRight, 0f)) g.FillRectangle(back, ClientRectangle);
   if (Docked && hovering) using (var glow = Rounded(new RectangleF(Px(1),Px(1),Width-Px(2),Height-Px(2)),Px(6))) using (var brush = new SolidBrush(Color.FromArgb(22,255,255,255))) g.FillPath(brush,glow);
   if (Dense) { PaintDense(g); return; }
   if (!Docked) using (var outline = Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),Px(8)))
   using (var pen = new Pen(Color.FromArgb(67,68,70))) g.DrawPath(pen,outline);
   using (var normal = new Font("Segoe UI",8f))
   using (var bold = new Font("Segoe UI Semibold",8.5f))
   using (var tiny = new Font("Segoe UI",7.2f)) {
    DateTimeOffset now = DateTimeOffset.UtcNow;
    for (int i=0;i<2;i++) {
     Bucket b = buckets.Count > i ? buckets[i] : new Bucket();
     int x = Px(8+i*124), y=Px(5), w=Px(116), h=Px(16);
     Color baseColor = i==0 ? amber : Color.FromArgb(98,167,230);
     Color color = stale ? Color.FromArgb(135,137,142) : b.Percent.HasValue && b.Percent.Value >= 90 ? red : baseColor;
     using (var pill = Rounded(new RectangleF(x,y,w,h),Px(8))) {
      using (var background = new SolidBrush(Color.FromArgb(48,50,53))) g.FillPath(background,pill);
      var saved = g.Save(); g.SetClip(pill);
      if (b.Percent.HasValue) using (var fill = new SolidBrush(Color.FromArgb(95,color))) g.FillRectangle(fill,x,y,(float)(w*Math.Min(100,b.Percent.Value)/100),h);
      g.Restore(saved);
     }
     TextRenderer.DrawText(g,i==0 ? "5h" : "7d",normal,new Rectangle(x+Px(8),y,w/2,h),color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
     string number = b.Percent.HasValue ? Data.Percent(b.Percent.Value) : "—";
     TextRenderer.DrawText(g,number,bold,new Rectangle(x+Px(28),y,w-Px(36),h),ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
     // When the recent pace runs out before the renewal, the caption says when instead.
     DateTimeOffset when = now; bool runsOut = !stale && buckets.Count > i && Pace.RunsOut(history,i,b,now,out when);
     string caption = stale && last.HasValue ? "leitura antiga" : runsOut ? "esgota " + Pace.When(when).Replace("às ","") : ShortReset(b);
     TextRenderer.DrawText(g,caption,tiny,new Rectangle(x+Px(3),Px(24),w,Px(12)),runsOut ? red : Color.FromArgb(175,177,182),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    }
    // Third pill: working hours on the Redmine issue in progress since it entered "Em andamento".
    var task = taskShown ? CurrentTask : null;
    if (task != null) {
     int x = Px(256), y = Px(5), w = Px(116), h = Px(16); double total; taskTotal.TryGetValue(task.Id, out total);
     using (var pill = Rounded(new RectangleF(x,y,w,h),Px(8))) using (var background = new SolidBrush(Color.FromArgb(48,50,53))) g.FillPath(background,pill);
     TextRenderer.DrawText(g,"#"+task.Id,normal,new Rectangle(x+Px(8),y,w-Px(8),h),green,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
     TextRenderer.DrawText(g,WorkDay.Duration(total),bold,new Rectangle(x+Px(28),y,w-Px(36),h),ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
     string since = (task.Spans.Count > 1 ? task.Spans.Count + "x · " : "") + "desde " + WorkDay.SinceLabel(task.Current.Start, DateTime.Now) + (tasks.Count > 1 ? " · +" + (tasks.Count-1) : "");
     TextRenderer.DrawText(g,since,tiny,new Rectangle(x+Px(3),Px(24),w,Px(12)),Color.FromArgb(175,177,182),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    }
    int lead = Lead;
    using (var dots = new SolidBrush(Color.FromArgb(181,183,187))) for (int i=0;i<3;i++) g.FillEllipse(dots,Width-Px(21-i*4),Px(11),Px(2),Px(2));
    using (var divider = new Pen(Color.FromArgb(65,67,70))) g.DrawLine(divider,Px(254)+lead,Px(7),Px(254)+lead,Px(33));
    // Badges count the other active sessions: amber needs you, accent is working.
    var others = OtherActive();
    int waiting = others.Count(s=>s.State=="waiting");
    var badges = new List<KeyValuePair<int,Color>>();
    if (waiting > 0) badges.Add(new KeyValuePair<int,Color>(waiting,amber));
    if (others.Count-waiting > 0) badges.Add(new KeyValuePair<int,Color>(others.Count-waiting,accent));
    for (int i=0;i<badges.Count;i++) {
     var area = new Rectangle(Width-Px(47)-i*Px(17),Px(4),Px(15),Px(15));
     using (var brush = new SolidBrush(badges[i].Value)) g.FillEllipse(brush,area);
     TextRenderer.DrawText(g,Math.Min(9,badges[i].Key).ToString(CultureInfo.InvariantCulture),tiny,area,Color.FromArgb(27,29,32),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    }
    int radarWidth = Width-Px(298)-lead-badges.Count*Px(17);
    bool celebrating = DateTime.Now < bannerUntil;
    string headline = celebrating ? banner : activeSession == null ? RadarTitle() : activeSession.Project + " · " + activeSession.Label;
    TextRenderer.DrawText(g,headline,celebrating ? bold : normal,new Rectangle(Px(264)+lead,Px(3),radarWidth,Px(17)),celebrating ? amber : StateColor(activeSession),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    var detail = new List<string>();
    if (activeSession != null) {
     if (activeSession.Elapsed.Length > 0) detail.Add(activeSession.Elapsed);
     if (activeSession.Extras.Length > 0) detail.Add(activeSession.Extras.Replace(" subagentes"," sub").Replace(" subagente"," sub"));
     if (activeSession.Context >= 0) detail.Add("ctx " + Math.Round(activeSession.Context) + "%");
    }
    detail.Add(gitInfo.Summary);
    TextRenderer.DrawText(g,String.Join(" · ",detail),tiny,new Rectangle(Px(264)+lead,Px(23),Width-Px(298)-lead,Px(13)),muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    Color statusColor = busy ? accent : stale ? Color.FromArgb(225,174,84) : Color.FromArgb(110,170,134);
    using (var dot = new SolidBrush(statusColor)) g.FillEllipse(dot,Width-Px(17),Px(27),Px(4),Px(4));
   }
  }
  void DrawSpark(Graphics g, int index, Rectangle area, Color color) {
   DateTimeOffset now = DateTimeOffset.UtcNow; TimeSpan span = index == 0 ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
   var points = Pace.Points(history,index,span,now);
   if (points.Count < 2) return;
   long from = (now-span).ToUnixTimeSeconds(), to = now.ToUnixTimeSeconds();
   var line = points.Select(p=>new PointF(area.Left+area.Width*(float)(p.Key-from)/(to-from), area.Bottom-area.Height*(float)Math.Min(100,p.Value)/100)).ToArray();
   using (var axis = new Pen(Color.FromArgb(49,51,56))) g.DrawLine(axis,area.Left,area.Bottom,area.Right,area.Bottom);
   using (var pen = new Pen(color,Math.Max(1,Px(1.3f)))) g.DrawLines(pen,line);
  }
  protected override void OnPaint(PaintEventArgs e) {
   base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
   if (settings.Compact) { PaintCompact(g); PaintConfetti(g); return; }
   PaintFull(g); PaintConfetti(g);
  }
  void PaintFull(Graphics g) {
   using (var pen = new Pen(Color.FromArgb(63,64,66))) g.DrawRectangle(pen,0,0,Width-1,Height-1);
   var rows = buckets.Count > 0 ? buckets : new List<Bucket> { new Bucket { Title = "Sessão · 5 horas" }, new Bucket { Title = "Semana · todos os modelos" } };
   DateTimeOffset now = DateTimeOffset.UtcNow;
   using (var small = new Font("Segoe UI",8)) using (var normal = new Font("Segoe UI",9)) using (var number = new Font("Segoe UI Semibold",16)) {
    for (int i=0;i<rows.Count;i++) {
     Bucket b = rows[i]; int y = Px(62+i*88);
     Color color = stale ? Color.FromArgb(126,128,134) : !b.Percent.HasValue ? muted : b.Percent.Value >= 90 ? Color.FromArgb(238,115,111) : b.Percent.Value >= 70 ? Color.FromArgb(229,184,100) : accent;
     using (var brush = new SolidBrush(ink)) g.DrawString(b.Title, normal, brush, Px(18),y);
     string value = b.Percent.HasValue ? Data.Percent(b.Percent.Value) : "—";
     using (var brush = new SolidBrush(color)) { SizeF size = g.MeasureString(value,number); g.DrawString(value,number,brush,Width-Px(18)-size.Width,y-Px(5)); }
     using (var brush = new SolidBrush(Color.FromArgb(49,51,56))) g.FillRectangle(brush,Px(18),y+Px(29),Px(334),Px(6));
     if (b.Percent.HasValue) using (var brush = new SolidBrush(color)) g.FillRectangle(brush,Px(18),y+Px(29),(float)(Px(334)*Math.Min(100,b.Percent.Value)/100),Px(6));
     using (var brush = new SolidBrush(muted)) g.DrawString(Data.ResetLabel(b),small,brush,Px(18),y+Px(44));
     if (i < 2 && buckets.Count > i && b.Percent.HasValue) {
      DrawSpark(g,i,new Rectangle(Width-Px(108),y+Px(46),Px(90),Px(12)),color);
      DateTimeOffset when = now; bool runsOut = !stale && Pace.RunsOut(history,i,b,now,out when);
      using (var brush = new SolidBrush(runsOut ? red : muted)) g.DrawString(Pace.Describe(history,i,b,now),small,brush,Px(18),y+Px(60));
     }
    }
   }
  }
  internal Bitmap TrayImage() { return tray.Icon.ToBitmap(); }
  // Fills the widget with sample data so tests can render it without an account.
  internal void Demo(bool compact) {
   DateTimeOffset now = DateTimeOffset.UtcNow;
   settings.Compact = compact;
   buckets = new List<Bucket> {
    new Bucket { Title = "Sessão · 5 horas", Percent = 62, Reset = now.AddHours(2.2) },
    new Bucket { Title = "Semana · todos os modelos", Percent = 31, Reset = now.AddDays(3.5) },
    new Bucket { Title = "Semana · opus", Percent = 12, Reset = now.AddDays(3.5) }
   };
   history = new List<Reading>();
   for (int m = 300; m >= 0; m -= 10) history.Add(new Reading { T = now.AddMinutes(-m).ToUnixTimeSeconds(), P5 = Math.Max(0, 62-m*0.35), P7 = 31-m*0.01 });
   last = now; stale = false; status = "Conectado · login atual do Claude Code";
   sessions = new List<SessionInfo> {
    new SessionInfo { Id = "a1b2c3d4e5", Cwd = @"C:\workspace\api-backend", State = "working", Event = "PreToolUse", Tool = "Bash", Updated = now.ToString("o"), TurnStarted = now.AddMinutes(-4).ToString("o"), Subagents = 2, Model = "Opus 5.5", Context = 42, Cost = 1.23, Issue = 1234 },
    new SessionInfo { Id = "f6e5d4c3b2", Cwd = @"C:\workspace\painel-admin", State = "waiting", Event = "PermissionRequest", Tool = "Edit", Updated = now.AddMinutes(-1).ToString("o") },
    new SessionInfo { Id = "0918273645", Cwd = @"C:\workspace\site", State = "working", Event = "PostToolUse", Updated = now.ToString("o"), TurnStarted = now.AddSeconds(-40).ToString("o") }
   };
   activeSession = sessions[0];
   gitInfo = new GitInfo { Branch = "main", Files = 5, Added = 120, Removed = 34, LinesKnown = true };
   DateTime local = DateTime.Now;
   tasks = new List<ActiveIssue> { new ActiveIssue { Id = 1234, Subject = "Tarefa de exemplo", Url = "https://example.invalid/issues/1234",
    Spans = new List<StatusSpan> { new StatusSpan { Start = local.Date.AddDays(-1).AddHours(9), End = local.Date.AddDays(-1).AddHours(11) },
     new StatusSpan { Start = local.Date.AddDays(-1).AddHours(17).AddMinutes(14), End = DateTime.MaxValue } }, Estimated = 4, Spent = 4.5 } };
   UpdateTaskClock();
   Arrange(); UpdateLabels();
  }
 }
 public static class Program {
  public static string AppFolder = "";
  public static void OpenUsage() { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true }); } catch { MessageBox.Show("Abra https://claude.ai/settings/usage no navegador."); } }
  public static void Run(string folder) {
   AppFolder = folder ?? "";
   bool first; using (var mutex = new System.Threading.Mutex(true,"Local\\MeuClaudeWidget-v1",out first)) {
    if (!first) { MessageBox.Show("O widget já está aberto. Procure o ícone perto do relógio."); return; }
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Widget());
   }
  }
  static string StartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Meu Claude Widget.lnk"); } }
  public static bool StartsWithWindows() { return File.Exists(StartupLink); }
  // The shortcut starts PowerShell hidden directly, avoiding the console flash of the .cmd launcher.
  public static void SetStartup(bool enabled) {
   if (!enabled) { if (File.Exists(StartupLink)) File.Delete(StartupLink); return; }
   string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
   Type type = Type.GetTypeFromProgID("WScript.Shell"); object host = Activator.CreateInstance(type);
   try {
    object link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, host, new object[] { StartupLink });
    try {
     Type kind = link.GetType();
     SetMember(kind, link, "TargetPath", shell);
     SetMember(kind, link, "Arguments", "-NoLogo -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + Path.Combine(AppFolder, "Iniciar.ps1") + "\"");
     SetMember(kind, link, "WorkingDirectory", AppFolder);
     SetMember(kind, link, "WindowStyle", 7);
     SetMember(kind, link, "Description", "Meu Claude · widget de uso");
     kind.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
    } finally { Marshal.ReleaseComObject(link); }
   } finally { Marshal.ReleaseComObject(host); }
  }
  static void SetMember(Type type, object target, string name, object value) { type.InvokeMember(name, BindingFlags.SetProperty, null, target, new object[] { value }); }
  // Brings the terminal hosting a session forward; a synthetic Alt press lifts Windows' foreground lock.
  public static bool FocusWindow(int pid) {
   try {
    using (var process = System.Diagnostics.Process.GetProcessById(pid)) {
     IntPtr handle = process.MainWindowHandle; if (handle == IntPtr.Zero) return false;
     if (Native.IsIconic(handle)) Native.ShowWindow(handle, 9);
     Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
     bool done = Native.SetForegroundWindow(handle);
     Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
     return done;
    }
   } catch { return false; }
  }
  public static void Preview(string folder) {
   foreach (bool compact in new bool[] { true, false })
    using (var widget = new Widget(true)) {
     widget.Location = new Point(-20000,-20000); widget.Show(); widget.Demo(compact); Application.DoEvents();
     using (var bitmap = new Bitmap(widget.Width,widget.Height)) {
      widget.DrawToBitmap(bitmap,new Rectangle(0,0,widget.Width,widget.Height));
      bitmap.Save(Path.Combine(folder,compact ? "preview-compacto.png" : "preview-painel.png"),System.Drawing.Imaging.ImageFormat.Png);
     }
     if (compact) using (var icon = widget.TrayImage()) icon.Save(Path.Combine(folder,"preview-bandeja.png"),System.Drawing.Imaging.ImageFormat.Png);
    }
  }
  public static string SelfTest() {
   int untracked;
   if (GitReader.CountFiles("R  novo\0antigo\0?? novo.txt\0 M tracked.txt\0",out untracked)!=3 || untracked!=1) throw new Exception("Falha: status Git e renome.");
   var git=new GitInfo(); GitReader.CountLines("2\t1\tfile.txt\0-\t-\tbinary\0"+"0\t0\t\0old\0new\0",git);
   if(git.Added!=2 || git.Removed!=1 || git.Binary!=1) throw new Exception("Falha: numstat Git.");

   var b = Data.ParseUsage("{\"five_hour\":{\"utilization\":0,\"resets_at\":null},\"seven_day\":null,\"seven_day_sonnet\":{\"utilization\":92.5,\"resets_at\":\"2030-01-01T00:00:00Z\"}}");
   if (b.Count != 3 || b[0].Percent != 0 || b[1].Percent.HasValue || b[2].Percent != 92.5 || !b[2].Reset.HasValue) throw new Exception("Falha: zero, null, modelo ou data.");
   bool rejected = false; try { Data.ParseUsage("{\"error\":\"denied\"}"); } catch (RequestFailure) { rejected = true; }
   if (!rejected) throw new Exception("Falha: erro aceito como uso.");
   b = Data.ParseUsage("{\"five_hour\":{\"utilization\":-1},\"seven_day\":{\"utilization\":120}}");
   if (b[0].Percent.HasValue || b[1].Percent != 120) throw new Exception("Falha: limites numéricos.");
   const string fixture = "credencial-ficticia-apenas-para-teste";
   if (Data.Unprotect(Data.Protect(fixture)) != fixture) throw new Exception("Falha: proteção DPAPI.");

   // Pace: +30%/h over the last hour, 60% left = 2h; an older, higher value from a previous window is ignored.
   var now = DateTimeOffset.UtcNow; var history = new List<Reading>();
   for (int m = 60; m >= 0; m -= 10) history.Add(new Reading { T = now.AddMinutes(-m).ToUnixTimeSeconds(), P5 = 40-m*0.5, P7 = 10 });
   history.Insert(1, new Reading { T = now.AddMinutes(-55).ToUnixTimeSeconds(), P5 = 90, P7 = 10 });
   var five = new Bucket { Percent = 40, Reset = now.AddHours(1) }; DateTimeOffset when;
   var eta = Pace.Exhaustion(history,0,five,now);
   if (!eta.HasValue || Math.Abs((eta.Value-now).TotalHours-2) > 0.05) throw new Exception("Falha: ritmo de uso.");
   if (Pace.RunsOut(history,0,five,now,out when)) throw new Exception("Falha: previsão após a renovação.");
   five.Reset = now.AddHours(4);
   if (!Pace.RunsOut(history,0,five,now,out when)) throw new Exception("Falha: previsão antes da renovação.");
   if (Pace.Exhaustion(history,1,new Bucket { Percent = 10, Reset = now.AddDays(3) },now).HasValue) throw new Exception("Falha: ritmo estável.");

   var session = Data.Json().Deserialize<SessionInfo>("{\"Id\":\"x\",\"Cwd\":\"C:\\\\p\",\"State\":\"working\",\"Tool\":\"Bash\",\"Updated\":\""+now.ToString("o")+"\",\"TurnStarted\":\""+now.AddMinutes(-4).ToString("o")+"\",\"Subagents\":2,\"Agents\":[{\"Id\":\"a\",\"Stopped\":false}]}");
   if (session.Activity != "Trabalhando há 4min · Bash · 2 subagentes") throw new Exception("Falha: atividade da sessão (" + session.Activity + ").");
   using (var self = System.Diagnostics.Process.GetCurrentProcess()) {
    session.ClaudePid = self.Id; session.ClaudeStarted = new DateTimeOffset(self.StartTime.ToUniversalTime()).ToString("o");
    if (!RadarReader.Alive(session)) throw new Exception("Falha: processo vivo marcado como fechado.");
    session.ClaudeStarted = new DateTimeOffset(self.StartTime.ToUniversalTime()).AddMinutes(-5).ToString("o");
    if (RadarReader.Alive(session)) throw new Exception("Falha: PID reaproveitado tratado como a mesma sessão.");
   }
   // Redmine: hours inside 08:00–12:00 / 13:12–18:00 from the moment the issue entered "Em andamento".
   var day = new DateTime(2026,11,4); DateTime end = WorkDay.DayEnd(day);
   Func<int,int,StatusChange> toWork = (h,m) => new StatusChange { When = day.AddHours(h).AddMinutes(m), Old = "6", New = "2" };
   Func<List<StatusSpan>,double> today = s => WorkDay.Hours(new Dictionary<int,List<StatusSpan>> { { 1, s } }, day)[1];
   var spans = WorkDay.Spans(new[] { toWork(14,0) }, day.AddDays(-5), "2", end);
   if (Math.Abs(today(spans)-4) > 1e-9 || WorkDay.Round(today(spans)) != 4) throw new Exception("Falha: Redmine a partir das 14:00.");
   spans = WorkDay.Spans(new[] { new StatusChange { When = day.AddDays(-1).AddHours(15), Old = "1", New = "2" } }, day.AddDays(-5), "2", end);
   if (Math.Abs(today(spans)-8.8) > 1e-9 || WorkDay.Round(today(spans)) != 9) throw new Exception("Falha: Redmine desde ontem.");
   spans = WorkDay.Spans(new[] { toWork(11,30) }, day.AddDays(-5), "2", end);
   if (Math.Abs(today(spans)-5.3) > 1e-9 || WorkDay.Round(today(spans)) != 5.5) throw new Exception("Falha: Redmine com almoço.");
   spans = WorkDay.Spans(new[] { toWork(14,0), new StatusChange { When = day.AddHours(15), Old = "2", New = "8" }, new StatusChange { When = day.AddHours(16), Old = "8", New = "2" } }, day.AddDays(-5), "2", end);
   if (Math.Abs(today(spans)-3) > 1e-9) throw new Exception("Falha: Redmine com pausa.");
   spans = WorkDay.Spans(new[] { toWork(17,50) }, day.AddDays(-5), "2", end);
   if (WorkDay.Round(today(spans)) != 0 || WorkDay.Round(0.25) != 0.5) throw new Exception("Falha: Redmine menos de 15 min.");
   spans = WorkDay.Spans(new StatusChange[0], day.AddHours(9), "2", end);
   if (Math.Abs(today(spans)-7.8) > 1e-9) throw new Exception("Falha: Redmine criada em andamento.");
   var both = new Dictionary<int,List<StatusSpan>> { { 1, WorkDay.Spans(new[] { toWork(8,0) }, day.AddDays(-5), "2", end) }, { 2, WorkDay.Spans(new[] { toWork(15,0) }, day.AddDays(-5), "2", end) } };
   var split = WorkDay.Hours(both, day);
   if (Math.Abs(split[1]-5.8) > 1e-9 || Math.Abs(split[2]-3) > 1e-9) throw new Exception("Falha: Redmine com duas tarefas sobrepostas.");
   if (WorkDay.Round(3.8 - 2) != 2 || WorkDay.Round(4 - 4) != 0) throw new Exception("Falha: Redmine desconto do já apontado.");
   if (!WorkDay.Due(day.AddHours(17).AddMinutes(55), "") || WorkDay.Due(day.AddHours(17).AddMinutes(54), "") || WorkDay.Due(day.AddHours(18), "2026-11-04") || WorkDay.Due(new DateTime(2026,11,7,18,0,0), ""))
    throw new Exception("Falha: Redmine horário de execução.");
   // Live clock: from yesterday 15:00 until today 10:00 = 3h + 2h; Friday 17:00 → Monday 09:00 skips the weekend.
   var open = new Dictionary<int,List<StatusSpan>> { { 1, new List<StatusSpan> { new StatusSpan { Start = day.AddDays(-1).AddHours(15), End = DateTime.MaxValue } } } };
   if (Math.Abs(WorkDay.Totals(open, day.AddHours(10))[1]-5) > 1e-9) throw new Exception("Falha: relógio Redmine entre dias.");
   open[1][0].Start = new DateTime(2026,11,6,17,0,0);
   if (Math.Abs(WorkDay.Totals(open, new DateTime(2026,11,9,9,0,0))[1]-2) > 1e-9) throw new Exception("Falha: relógio Redmine no fim de semana.");
   if (Math.Abs(WorkDay.Totals(open, new DateTime(2026,11,9,12,30,0))[1]-5) > 1e-9) throw new Exception("Falha: relógio Redmine no almoço.");
   // All periods add up; "Em pausa" and "Aguardando terceiros" do not: yesterday 16–18 (2h) + today 08–09 (1h) + 14–16 (2h) = 5h.
   var changes = new[] {
    new StatusChange { When = day.AddDays(-1).AddHours(16), Old = "1", New = "2" },
    new StatusChange { When = day.AddHours(9), Old = "2", New = "8" },
    new StatusChange { When = day.AddHours(10), Old = "8", New = "2" }, new StatusChange { When = day.AddHours(10), Old = "2", New = "7" },
    new StatusChange { When = day.AddHours(14), Old = "7", New = "2" } };
   var periods = WorkDay.Spans(changes, day.AddDays(-5), "2", DateTime.MaxValue);
   if (periods.Count != 2 || Math.Abs(WorkDay.Totals(new Dictionary<int,List<StatusSpan>> { { 1, periods } }, day.AddHours(16))[1]-5) > 1e-9)
    throw new Exception("Falha: relógio Redmine somando períodos com pausas.");
   // Scoreboard: week starts on Monday; day states; streak of weeks without rejection.
   if (Score.Monday(day) != new DateTime(2026,11,2) || Score.Monday(new DateTime(2026,11,8)) != new DateTime(2026,11,2) || Score.Monday(new DateTime(2026,11,9)) != new DateTime(2026,11,9))
    throw new Exception("Falha: placar início da semana.");
   DateTime friday = new DateTime(2026,11,6,10,0,0);
   if (Score.DayState(day, 9, friday, false) != "ok" || Score.DayState(day, 6.5, friday, false) != "short" || Score.DayState(day, 0, friday, true) != "off"
    || Score.DayState(friday.Date, 2, friday, false) != "today" || Score.DayState(friday.Date, 2, friday.Date.AddHours(18.5), false) != "short" || Score.DayState(friday.Date.AddDays(3), 0, friday, false) != "future")
    throw new Exception("Falha: placar estado dos dias.");
   var weeks = new Dictionary<string,int> { { "2026-11-02", 0 }, { "2026-10-26", 0 }, { "2026-10-19", 2 }, { "2026-10-12", 0 } };
   if (Score.Streak(weeks, new DateTime(2026,11,2)) != 2 || Score.Streak(weeks, new DateTime(2026,10,19)) != 0 || Score.Streak(weeks, new DateTime(2026,11,9)) != 0)
    throw new Exception("Falha: placar sequência sem rejeição.");
   // Morning summary: previous workday, time window, markdown cleanup and the transcript's last ask/answer.
   if (Recap.PreviousWorkday(new DateTime(2026,11,9)) != new DateTime(2026,11,6) || Recap.PreviousWorkday(day) != new DateTime(2026,11,3)) throw new Exception("Falha: bom dia dia útil anterior.");
   if (!Recap.Due(day.AddHours(8), "") || Recap.Due(day.AddHours(7), "") || Recap.Due(day.AddHours(13), "") || Recap.Due(day.AddHours(8), "2026-11-04") || Recap.Due(new DateTime(2026,11,7,8,0,0), ""))
    throw new Exception("Falha: bom dia horário.");
   if (Recap.Clean("## Pronto\n**Feito** o `teste`.\n| a | b |\n```\ncode\n```\nFim", 100) != "Pronto Feito o teste. Fim") throw new Exception("Falha: bom dia limpeza de texto.");
   string transcript = Path.Combine(Path.GetTempPath(), "meu-claude-selftest-" + Guid.NewGuid().ToString("N") + ".jsonl");
   try {
    File.WriteAllText(transcript, String.Join("\n", new[] {
     "{\"type\":\"user\",\"isSidechain\":false,\"message\":{\"role\":\"user\",\"content\":\"primeiro pedido\"}}",
     "{\"type\":\"assistant\",\"isSidechain\":false,\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"resposta antiga\"}]}}",
     "{\"type\":\"user\",\"isSidechain\":false,\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"ajusta a grade de CT-e\"}]}}",
     "{\"type\":\"user\",\"isSidechain\":false,\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"x\",\"type\":\"tool_result\",\"content\":\"saída\"}]}}",
     "{\"type\":\"user\",\"isSidechain\":true,\"message\":{\"role\":\"user\",\"content\":\"prompt de subagente\"}}",
     "{\"type\":\"assistant\",\"isSidechain\":false,\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"## Pronto\\nDesempate pelo **nf_id** aplicado.\"}]}}",
     "{\"type\":\"user\",\"isSidechain\":false,\"message\":{\"role\":\"user\",\"content\":\"<command-name>/clear</command-name>\"}}" }) + "\n");
    var recap = new SessionRecap(); Recap.ReadTranscript(transcript, recap);
    if (recap.Asked != "ajusta a grade de CT-e" || recap.Answered != "Pronto Desempate pelo nf_id aplicado.") throw new Exception("Falha: bom dia leitura do transcript (" + recap.Asked + " / " + recap.Answered + ").");
   } finally { try { File.Delete(transcript); } catch { } }
   return "OK: zero, dados ausentes, modelos, datas, resposta inválida, limites numéricos, DPAPI, ritmo de uso, atividade das sessões e cálculo de horas do Redmine. Nenhuma requisição foi feita.";
  }
 }
}
