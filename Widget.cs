// Claude Widget — original, dependency-free .NET Framework / Windows Forms source.
// Network access is read-only and restricted to the two Anthropic hosts below.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
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
  public string SelectedSession = "";
  public int CompactX = -10000, CompactY = -10000;
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
     Value = new Settings { Mode = mode.SelectedIndex == 0 ? "browser" : "code", ProtectedSession = mode.SelectedIndex == 0 ? session : "", Organization = org.Text.Trim(), Interval = 60, OnTop = top.Checked, X = current.X, Y = current.Y, Compact = current.Compact, CompactX = current.CompactX, CompactY = current.CompactY, RadarAlerts = current.RadarAlerts, SelectedSession = current.SelectedSession };
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
  float ScaleFactor { get { return DeviceDpi / 96f; } }
  int Px(float value) { return (int)Math.Round(value * ScaleFactor); }
  public Widget() {
   settings = Data.Load();
   Text = "Meu Claude · Uso"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
   AutoScaleMode = AutoScaleMode.None; BackColor = Color.FromArgb(27,29,32); ForeColor = ink; TopMost = settings.OnTop;
   DoubleBuffered = true; ShowInTaskbar = false; Font = new Font("Segoe UI", 9);
   title = new Label { Text = "✳  CLAUDE", ForeColor = accent, Font = new Font("Segoe UI Semibold", 11), AutoSize = false }; Controls.Add(title);
   state = new Label { ForeColor = ink, AutoSize = false }; Controls.Add(state);
   timestamp = new Label { ForeColor = muted, AutoSize = false, Font = new Font("Segoe UI",8) }; Controls.Add(timestamp);
   radarSummary = new Label { BackColor = Color.FromArgb(38,41,45), ForeColor = ink, Padding = new Padding(9), Cursor = Cursors.Hand }; Controls.Add(radarSummary);
   radarSummary.Click += delegate { ShowRadar(); };
   compactButton = new Button { Text = "Compactar", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42,44,48), ForeColor = accent, TabStop = true, AccessibleName = "Voltar ao modo compacto" };
   compactButton.FlatAppearance.BorderSize = 0;
   compactButton.Click += delegate { if (!settings.Compact) ToggleCompact(); };
   Controls.Add(compactButton);
   actions = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false }; Controls.Add(actions);
   AddButton("Atualizar", async delegate { await RefreshUsage(); });
   AddButton("Configurar", delegate { Configure(); });
   AddButton("×", delegate { Close(); });
   menu = new ContextMenuStrip();
   compactItem = new ToolStripMenuItem("Modo compacto", null, delegate { ToggleCompact(); });
   menu.Items.Add(compactItem);
   menu.Items.Add("Alinhar perto do relógio", null, delegate { if (!settings.Compact) ToggleCompact(); DockNearClock(); RememberPosition(); TrySave(); });
   menu.Items.Add("Atualizar", null, async delegate { await RefreshUsage(); });
   menu.Items.Add("Status da conexão", null, delegate { ShowStatusTip(); });
   menu.Items.Add("Projetos e sessões…", null, delegate { ShowRadar(); });
   var notifyItem = new ToolStripMenuItem("Avisar quando precisar de você / terminar") { Checked = settings.RadarAlerts, CheckOnClick = true };
   notifyItem.CheckedChanged += delegate { settings.RadarAlerts = notifyItem.Checked; TrySave(); };
   menu.Items.Add(notifyItem);
   menu.Items.Add(new ToolStripSeparator());
   menu.Items.Add("Configurar", null, delegate { Show(); Configure(); });
   menu.Items.Add("Ver uso no Claude", null, delegate { Program.OpenUsage(); });
   menu.Items.Add("Desconectar e apagar chave", null, delegate { Disconnect(); });
   menu.Items.Add("Sair", null, delegate { Close(); }); ContextMenuStrip = menu;
   tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Meu Claude · Uso", ContextMenuStrip = menu, Visible = true };
   tray.DoubleClick += delegate { Show(); Activate(); };
   state.Click += delegate { ShowStatusTip(); };
   AttachDrag(this); AttachDrag(title);
   title.MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleCompact(); };
   MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.X < Px(254)) ToggleCompact(); };
   MouseClick += delegate(object sender, MouseEventArgs e) { if (settings.Compact && e.Button == MouseButtons.Left) { if (e.X >= Width-Px(28)) menu.Show(this, new Point(Width - 10, 0), ToolStripDropDownDirection.AboveLeft); else if (e.X >= Px(254) && !dragged) ShowRadar(); } };
   timer.Interval = 1000; timer.Tick += async delegate { if (stopped) return; PinCompact(); if (DateTimeOffset.UtcNow >= nextRadar) RefreshRadar(); if (!busy && DateTimeOffset.UtcNow >= next && settings.Mode.Length > 0) await RefreshUsage(); UpdateLabels(); Invalidate(); };
   Shown += async delegate {
    Arrange(); RestorePosition(); PinCompact(); RefreshRadar();
    timer.Start(); if (settings.Mode.Length == 0) Configure(); else await RefreshUsage();
   };
   FormClosing += delegate { stopped = true; timer.Stop(); RememberPosition(); TrySave(); tray.Visible = false; tray.Dispose(); api.Dispose(); timer.Dispose(); menu.Dispose(); hint.Dispose(); };
  }
  void AddButton(string text, EventHandler handler) {
   var b = new Button { Text = text, Width = text == "×" ? 27 : 78, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42,44,48), ForeColor = ink, Margin = new Padding(0,0,4,0), TabStop = true };
   b.FlatAppearance.BorderSize = 0; b.Click += handler; actions.Controls.Add(b);
  }
  Point dragMouse, dragWindow; bool dragged;
  void AttachDrag(Control c) {
   c.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && !(settings.Compact && c == this && e.X >= Width-Px(28))) { dragged = false; dragMouse = Cursor.Position; dragWindow = Location; c.Capture = true; } };
   c.MouseMove += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && c.Capture) { if (Math.Abs(Cursor.Position.X-dragMouse.X)+Math.Abs(Cursor.Position.Y-dragMouse.Y)>4) dragged=true; Location = new Point(dragWindow.X + Cursor.Position.X - dragMouse.X, dragWindow.Y + Cursor.Position.Y - dragMouse.Y); } };
   c.MouseUp += delegate { c.Capture = false; RememberPosition(); TrySave(); };
  }
  void TrySave() { try { Data.Save(settings); } catch { detail = "Não foi possível salvar preferências. Verifique a pasta LocalAppData."; } }
  string RadarTitle() {
   return activeSession == null ? "Radar · aguardando Claude Code" : activeSession.Project + " · " + activeSession.Label;
  }
  string RadarDetails() {
   if (activeSession == null) return "Radar sem eventos. Execute Ativar-Radar.cmd uma vez, reabra o Claude Code e envie uma mensagem.\nA integração é para Claude Code no Windows, fora do WSL.";
   return RadarTitle()+"\nSessão: "+activeSession.Id.Substring(0,8)+" · "+sessions.Count+" sessão(ões) nas últimas 24h\nÚltimo evento: "+activeSession.Event+" "+activeSession.Tool+" às "+activeSession.Time.ToLocalTime().ToString("dd/MM HH:mm:ss")+"\n"+gitInfo.Details+"\nEstado observado por eventos. Interrupções ou fechamento abrupto podem não gerar evento.";
  }
  void ShowRadar() {
   using (var form = new RadarForm(()=>new List<SessionInfo>(sessions),()=>RadarDetails(),id=>{
    settings.SelectedSession=id; SelectSession(); nextGit=DateTimeOffset.MinValue; TrySave(); UpdateLabels(); Invalidate(); RefreshGit();
   })) form.ShowDialog(this);
  }
  void SelectSession() {
   SessionInfo selected = sessions.Find(s=>s.Id==settings.SelectedSession);
   if (selected==null) selected=sessions.Find(s=>s.State!="ended");
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
     if(radarWarmed && settings.RadarAlerts && prior!=session.State && DateTimeOffset.UtcNow-session.Time<TimeSpan.FromSeconds(15) && (session.State=="waiting"||session.State=="done"))
      tray.ShowBalloonTip(4000,"Claude · "+session.Project,session.Label,ToolTipIcon.Info);
     observed[session.Id]=session.State;
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
  protected override void WndProc(ref Message m) {
   // MA_NOACTIVATE: moving/clicking the compact strip keeps the working app focused.
   if (m.Msg == 0x0021 && settings != null && settings.Compact) { m.Result = new IntPtr(3); return; }
   base.WndProc(ref m);
  }
  void PinCompact() {
   if (settings.Compact && settings.OnTop && Visible && Enabled && !menu.Visible && IsHandleCreated)
    Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // no move/size/activate
  }
  void RememberPosition() {
   if (settings.Compact) { settings.CompactX = Left; settings.CompactY = Top; }
   else { settings.X = Left; settings.Y = Top; }
  }
  void RestorePosition() {
   int x = settings.Compact ? settings.CompactX : settings.X;
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
   BackColor = settings.Compact ? Color.FromArgb(36,37,39) : Color.FromArgb(27,29,32);
   Region oldRegion = Region;
   if (settings.Compact) {
    ClientSize = new Size(Px(502),Px(40));
    using (var shape = Rounded(new RectangleF(0,0,Width,Height),Px(8))) Region = new Region(shape);
    if (oldRegion != null) oldRegion.Dispose();
    UpdateLabels(); Invalidate(); return;
   }
   Region = null; if (oldRegion != null) oldRegion.Dispose();
   int count = Math.Max(2, buckets.Count); ClientSize = new Size(Px(370), Px(228 + count * 88));
   title.SetBounds(Px(18),Px(17),Px(145),Px(27));
   actions.SetBounds(Px(155),Px(15),Px(204),Px(32));
   foreach (Control b in actions.Controls) { b.Width = Px(b.Text == "×" ? 27 : 78); b.Height = Px(28); }
   radarSummary.SetBounds(Px(18),Px(60 + count * 88),Px(334),Px(80));
   state.SetBounds(Px(18),Px(148 + count * 88),Px(334),Px(36));
   timestamp.SetBounds(Px(18),Px(189 + count * 88),Px(224),Px(32));
   compactButton.SetBounds(Px(250),Px(189 + count * 88),Px(102),Px(28));
   Invalidate();
  }
  void UpdateLabels() {
   state.Text = status; state.ForeColor = stale ? Color.FromArgb(232,179,112) : ink;
   string previous = last.HasValue ? "Última: " + last.Value.ToLocalTime().ToString("HH:mm:ss") : "Sem leitura recebida";
   int remaining = (int)Math.Max(0, Math.Ceiling((next - DateTimeOffset.UtcNow).TotalSeconds));
   timestamp.Text = previous + (settings.Mode.Length == 0 ? "" : busy ? " · consultando…" : " · próxima: " + remaining + "s");
   radarSummary.Text = RadarTitle()+"\n"+gitInfo.Summary+"\nClique para projetos e sessões";
   var summary = new StringBuilder("Meu Claude\n");
   summary.Append(RadarDetails()).Append("\n\n");
   foreach (Bucket b in buckets) summary.Append(b.Title).Append(": ").Append(b.Percent.HasValue ? b.Percent.Value.ToString("0.#")+"%" : "—").Append("\n").Append(Data.ResetLabel(b)).Append("\n");
   summary.Append(previous).Append("\n").Append(status).Append("\n").Append(detail).Append("\nArraste para mover · duplo clique para detalhes · botão direito para opções");
   string value = summary.ToString(); if (lastHint != value) { hint.SetToolTip(this,value); lastHint = value; }
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
  void Failure(string message) { stale = true; detail = message; status = (last.HasValue ? "Dado desatualizado" : "Sem dados") + " · tentativa automática"; }
  string ShortReset(Bucket b) {
   if (!b.Reset.HasValue) return "sem previsão";
   var left = b.Reset.Value-DateTimeOffset.UtcNow;
   if (left.TotalSeconds <= 0) return "atualizando…";
   if (left.TotalDays >= 1) return "renova " + (int)left.TotalDays + "d " + left.Hours + "h";
   if (left.TotalHours >= 1) return "renova " + (int)left.TotalHours + "h " + left.Minutes + "m";
   return "renova " + Math.Max(1,(int)Math.Ceiling(left.TotalMinutes)) + "min";
  }
  void PaintCompact(Graphics g) {
   using (var outline = Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),Px(8)))
   using (var pen = new Pen(Color.FromArgb(67,68,70))) g.DrawPath(pen,outline);
   using (var normal = new Font("Segoe UI",8f))
   using (var bold = new Font("Segoe UI Semibold",8.5f))
   using (var tiny = new Font("Segoe UI",7.2f)) {
    for (int i=0;i<2;i++) {
     Bucket b = buckets.Count > i ? buckets[i] : new Bucket();
     int x = Px(8+i*124), y=Px(5), w=Px(116), h=Px(16);
     Color baseColor = i==0 ? Color.FromArgb(231,175,76) : Color.FromArgb(98,167,230);
     Color color = stale ? Color.FromArgb(135,137,142) : b.Percent.HasValue && b.Percent.Value >= 90 ? Color.FromArgb(240,123,112) : baseColor;
     using (var pill = Rounded(new RectangleF(x,y,w,h),Px(8))) {
      using (var background = new SolidBrush(Color.FromArgb(48,50,53))) g.FillPath(background,pill);
      var saved = g.Save(); g.SetClip(pill);
      if (b.Percent.HasValue) using (var fill = new SolidBrush(Color.FromArgb(95,color))) g.FillRectangle(fill,x,y,(float)(w*Math.Min(100,b.Percent.Value)/100),h);
      g.Restore(saved);
     }
     TextRenderer.DrawText(g,i==0 ? "5h" : "7d",normal,new Rectangle(x+Px(8),y,w/2,h),color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
     string number = b.Percent.HasValue ? b.Percent.Value.ToString("0.#",CultureInfo.GetCultureInfo("pt-BR"))+"%" : "—";
     TextRenderer.DrawText(g,number,bold,new Rectangle(x+Px(28),y,w-Px(36),h),ink,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
     string caption = stale && last.HasValue ? "leitura antiga" : ShortReset(b);
     TextRenderer.DrawText(g,caption,tiny,new Rectangle(x+Px(3),Px(24),w,Px(12)),Color.FromArgb(175,177,182),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    }
    using (var dots = new SolidBrush(Color.FromArgb(181,183,187))) for (int i=0;i<3;i++) g.FillEllipse(dots,Width-Px(21-i*4),Px(11),Px(2),Px(2));
    using (var divider = new Pen(Color.FromArgb(65,67,70))) g.DrawLine(divider,Px(254),Px(7),Px(254),Px(33));
    Color radarColor = activeSession == null ? muted : activeSession.Label == "Trabalhando" ? accent : activeSession.Label == "Precisa de você" ? Color.FromArgb(231,175,76) : activeSession.State == "done" ? Color.FromArgb(110,190,145) : ink;
    TextRenderer.DrawText(g,RadarTitle(),normal,new Rectangle(Px(264),Px(3),Width-Px(298),Px(17)),radarColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    TextRenderer.DrawText(g,gitInfo.Summary,tiny,new Rectangle(Px(264),Px(23),Width-Px(298),Px(13)),muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    Color statusColor = busy ? accent : stale ? Color.FromArgb(225,174,84) : Color.FromArgb(110,170,134);
    using (var dot = new SolidBrush(statusColor)) g.FillEllipse(dot,Width-Px(17),Px(27),Px(4),Px(4));
   }
  }
  protected override void OnPaint(PaintEventArgs e) {
   base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
   if (settings.Compact) { PaintCompact(g); return; }
   using (var pen = new Pen(Color.FromArgb(63,64,66))) g.DrawRectangle(pen,0,0,Width-1,Height-1);
   var rows = buckets.Count > 0 ? buckets : new List<Bucket> { new Bucket { Title = "Sessão · 5 horas" }, new Bucket { Title = "Semana · todos os modelos" } };
   using (var small = new Font("Segoe UI",8)) using (var normal = new Font("Segoe UI",9)) using (var number = new Font("Segoe UI Semibold",16)) {
    for (int i=0;i<rows.Count;i++) {
     Bucket b = rows[i]; int y = Px(62+i*88);
     Color color = stale ? Color.FromArgb(126,128,134) : !b.Percent.HasValue ? muted : b.Percent.Value >= 90 ? Color.FromArgb(238,115,111) : b.Percent.Value >= 70 ? Color.FromArgb(229,184,100) : accent;
     using (var brush = new SolidBrush(ink)) g.DrawString(b.Title, normal, brush, Px(18),y);
     string value = b.Percent.HasValue ? b.Percent.Value.ToString("0.#",CultureInfo.GetCultureInfo("pt-BR"))+"%" : "—";
     using (var brush = new SolidBrush(color)) { SizeF size = g.MeasureString(value,number); g.DrawString(value,number,brush,Width-Px(18)-size.Width,y-Px(5)); }
     using (var brush = new SolidBrush(Color.FromArgb(49,51,56))) g.FillRectangle(brush,Px(18),y+Px(29),Px(334),Px(6));
     if (b.Percent.HasValue) using (var brush = new SolidBrush(color)) g.FillRectangle(brush,Px(18),y+Px(29),(float)(Px(334)*Math.Min(100,b.Percent.Value)/100),Px(6));
     using (var brush = new SolidBrush(muted)) g.DrawString(Data.ResetLabel(b),small,brush,Px(18),y+Px(44));
    }
   }
  }
 }
 public static class Program {
  public static void OpenUsage() { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true }); } catch { MessageBox.Show("Abra https://claude.ai/settings/usage no navegador."); } }
  public static void Run() {
   bool first; using (var mutex = new System.Threading.Mutex(true,"Local\\MeuClaudeWidget-v1",out first)) {
    if (!first) { MessageBox.Show("O widget já está aberto. Procure o ícone perto do relógio."); return; }
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Widget());
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
   return "OK: zero, dados ausentes, modelos, datas, resposta inválida, limites numéricos e DPAPI. Nenhuma requisição foi feita.";
  }
 }
}
