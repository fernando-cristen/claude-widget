namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Diagnostics;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;
 using System.IO;
 using System.Runtime.InteropServices;
 using System.Text;
 using System.Text.RegularExpressions;
 using System.Threading.Tasks;
 using System.Windows.Forms;

 // A one-line text field that looks like part of the creature's home (dark pill, no title bar).
 sealed class ChatBox : Form {
  public readonly TextBox Input; public Action<string> Send; readonly Label answer;
  const int EM_SETCUEBANNER = 0x1501;
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

  public ChatBox(float sf) {
   FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
   BackColor = Color.FromArgb(30, 30, 34); ForeColor = Color.FromArgb(236, 238, 242); KeyPreview = true;
   Size = new Size((int)(270 * sf), (int)(32 * sf));
   answer = new Label { AutoSize = false, Visible = false, ForeColor = Color.FromArgb(214, 218, 224), BackColor = BackColor, Font = Theme.F(Theme.Sans, 9f), Left = (int)(12 * sf), Top = (int)(8 * sf), Width = Width - (int)(24 * sf) };
   Input = new TextBox { BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = ForeColor, Font = Theme.F(Theme.Sans, 9.5f), Left = (int)(14 * sf), Width = Width - (int)(28 * sf), MaxLength = 400 };
   Input.Top = (Height - Input.PreferredHeight) / 2;
   Controls.Add(answer); Controls.Add(Input);
   Input.KeyDown += (s, e) => {
    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; string text = Input.Text.Trim(); if (text.Length > 0 && Send != null) { Input.Text = ""; Send(text); } }
    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
   };
   Deactivate += delegate { if (!IsDisposed) Close(); };
   Shape();
  }
  protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SendMessage(Input.Handle, EM_SETCUEBANNER, (IntPtr)1, "Fala comigo… (Enter envia, Esc fecha)"); }
  void Shape() {
   using (var path = new GraphicsPath()) {
    float r = Math.Min(Height, 34) / 2f; var b = new RectangleF(0, 0, Width, Height);
    path.AddArc(b.X, b.Y, r * 2, r * 2, 180, 90); path.AddArc(b.Right - r * 2, b.Y, r * 2, r * 2, 270, 90);
    path.AddArc(b.Right - r * 2, b.Bottom - r * 2, r * 2, r * 2, 0, 90); path.AddArc(b.X, b.Bottom - r * 2, r * 2, r * 2, 90, 90); path.CloseFigure();
    Region = new Region(path);
   }
  }
  // Without the desktop home there is no bubble: the answer shows inside the box, above the field.
  public void ShowAnswer(string text, float sf) {
   using (var g = CreateGraphics()) {
    int h = (int)Math.Ceiling(g.MeasureString(text, answer.Font, answer.Width).Height) + 2;
    answer.Text = text; answer.Height = h; answer.Visible = true;
    int field = (int)(32 * sf); Height = h + (int)(10 * sf) + field; Input.Top = Height - field + (field - Input.PreferredHeight) / 2;
    Shape();
   }
  }
 }

 // Talking with the creature: it is Claude, so the answer comes from Claude Code itself (headless, Haiku),
 // with the user's Redmine MCP server for read-only questions. The conversation continues while you keep talking.
 public partial class Widget {
  ChatBox chat; bool chatBusy; string chatSession = ""; DateTime chatLast = DateTime.MinValue, chatAnswerUntil = DateTime.MinValue;
  bool ChatActive { get { return chat != null || chatBusy || DateTime.Now < chatAnswerUntil; } }

  const string Persona = "Voce e o bichinho do widget Meu Claude: a versao fofa e pequenininha do Claude, que mora na barra de tarefas e numa casinha na area de trabalho do usuario. " +
   "Fale em portugues do Brasil, em primeira pessoa, com jeito fofo, animado e carinhoso, como um bichinho de estimacao muito inteligente. " +
   "Responda curto: no maximo 2 ou 3 frases e uns 240 caracteres, sem markdown, sem listas, sem titulos e sem emojis. " +
   "Voce tem acesso ao Redmine do usuario pela ferramenta redmine_request: use apenas leitura (GET) e nunca crie, altere ou apague nada; se pedirem para mudar algo no Redmine, diga com carinho que voce so consegue consultar. " +
   "Cada mensagem traz o contexto do momento (hora, tarefa, o que o Claude esta fazendo, humor); use quando ajudar, sem repetir tudo.";

  void OpenChat() {
   if (chat != null && !chat.IsDisposed) { chat.Activate(); chat.Input.Focus(); return; }
   if (Away) GoDown();
   float sf = ScaleFactor;
   chat = new ChatBox(sf);
   chat.Send = text => Ask(text);
   chat.FormClosed += delegate { chat = null; };
   Point at;
   if (deskPet != null && !captionSpot.IsEmpty) at = deskPet.PointToScreen(new Point((int)(captionSpot.X - chat.Width / 2f), (int)(captionSpot.Y + 22 * sf)));
   else at = PointToScreen(new Point(Math.Max(0, (int)strip.Head.X - chat.Width / 2), -chat.Height - (int)(6 * sf)));
   var area = Screen.FromPoint(at).WorkingArea;
   chat.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - chat.Width, at.X)), Math.Max(area.Top, Math.Min(area.Bottom - chat.Height, at.Y)));
   chat.Show(); chat.Activate(); chat.Input.Focus();
   if (!chatBusy) Say(Pick("Oi! Fala comigo.", "Tô ouvindo!", "Pode perguntar!"), true);
  }

  async void Ask(string text) {
   if (chatBusy) return;
   chatBusy = true; Invalidate(); StartMotionTimer();
   if ((DateTime.Now - chatLast).TotalMinutes > 30) chatSession = "";
   string prompt = ChatContext() + "\n\nMensagem do usuario: " + text, session = chatSession;
   string answer, error;
   try {
    var result = await Task.Run(() => RunClaude(prompt, session));
    answer = result.Key; error = result.Value;
   } catch (Exception e) { answer = ""; error = e.Message; }
   chatBusy = false; chatLast = DateTime.Now;
   string shown = answer.Length > 0 ? CleanAnswer(answer) : "Ih, não consegui pensar agora" + (error.Length > 0 ? " (" + Short(error, 60) + ")" : "") + ".";
   double seconds = Math.Max(5, Math.Min(16, 2.5 + shown.Length * .06));
   chatAnswerUntil = DateTime.Now.AddSeconds(seconds);
   if (deskPet == null && chat != null && !chat.IsDisposed) chat.ShowAnswer(shown, ScaleFactor);
   Say(shown, true, seconds); hopAt = answer.Length > 0 ? DateTime.Now : hopAt;
   Invalidate();
  }

  // What is going on right now, sent with every message.
  string ChatContext() {
   var sb = new StringBuilder("[contexto] ");
   DateTime n = DateTime.Now;
   sb.Append(n.ToString("dddd HH:mm", new CultureInfo("pt-BR"))).Append(". ");
   var task = CurrentTask;
   if (task != null) {
    double total; double f = TaskFraction(task, out total);
    sb.Append("Tarefa em andamento: #").Append(task.Id).Append(" ").Append(Short(task.Subject, 90)).Append(", ").Append(WorkDay.Duration(total));
    if (f >= 0) sb.Append(" de ").Append(Hours(task.Estimated)).Append(" estimadas");
    sb.Append(". ");
   } else sb.Append("Nenhuma tarefa em andamento no Redmine. ");
   if (activeSession != null) sb.Append("Sessao do Claude no projeto ").Append(activeSession.Project).Append(": ").Append(activeSession.Label).Append(". ");
   sb.Append("Seu humor agora: ").Append(mood).Append(". ");
   if ((settings.CreatureName ?? "").Length > 0) sb.Append("Seu nome: ").Append(settings.CreatureName).Append(". ");
   if (weather.Temp.HasValue) sb.Append("Clima: ").Append(weather.Temp.Value).Append(" graus ").Append(weather.Kind).Append(". ");
   if (MusicOn) sb.Append("Tocando: ").Append(Short(song, 60)).Append(". ");
   if (NightIdle) sb.Append("O expediente acabou. ");
   return sb.ToString();
  }

  // Runs Claude Code headless; returns (answer, error). The session id keeps the conversation going.
  KeyValuePair<string,string> RunClaude(string prompt, string session) {
   string exe = ClaudeExe(); if (exe.Length == 0) return new KeyValuePair<string,string>("", "Claude Code não encontrado");
   string dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
   if (String.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
   string mcp = Path.Combine(dir, "mcp-config.json");
   var args = new StringBuilder("-p --model haiku --output-format json --append-system-prompt \"" + Persona + "\"");
   if (File.Exists(mcp)) args.Append(" --mcp-config \"" + mcp + "\" --strict-mcp-config --allowedTools mcp__redmine__redmine_request");
   if (session.Length > 0) args.Append(" --resume " + session);
   var info = new ProcessStartInfo(exe, args.ToString()) {
    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
   };
   using (var p = Process.Start(info)) {
    using (var input = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false))) input.Write(prompt);
    var output = p.StandardOutput.ReadToEndAsync(); var errors = p.StandardError.ReadToEndAsync();
    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return new KeyValuePair<string,string>("", "demorou demais"); }
    string json = output.Result;
    try {
     var obj = Data.Map(Data.Json().DeserializeObject(json));
     string id = Data.Str(Data.Get(obj, "session_id")); if (id.Length > 0) chatSession = id;
     return new KeyValuePair<string,string>(Data.Str(Data.Get(obj, "result")), "");
    } catch { return new KeyValuePair<string,string>("", Short(errors.Result.Trim().Length > 0 ? errors.Result.Trim() : json.Trim(), 120)); }
   }
  }
  static string ClaudeExe() {
   string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
   if (File.Exists(local)) return local;
   foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
    foreach (string name in new[] { "claude.exe", "claude.cmd" }) {
     try { string full = Path.Combine(dir.Trim(), name); if (File.Exists(full)) return full; } catch { }
    }
   return "";
  }
  // Plain text for the bubble: no markdown, single spaces, not too long.
  static string CleanAnswer(string text) {
   text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
   text = Regex.Replace(text, @"[*_`#>]+", "");
   text = Regex.Replace(text, @"[\uD800-\uDBFF][\uDC00-\uDFFF]|[☀-➿️‍]", ""); // emoji: the bubble font cannot draw them
   text = Regex.Replace(text, @"\s+", " ").Trim();
   return Short(text, 320);
  }
  static string Short(string text, int max) { text = text ?? ""; return text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…"; }
 }
}
