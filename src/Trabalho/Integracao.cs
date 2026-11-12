namespace MeuClaudeWidget {
 using System.Text.Json;
 using System.Text.Json.Nodes;

 // Liga e desliga o radar: os hooks do Claude Code e a barra de status. Substitui o antigo Integracao-Radar.ps1
 // e produz exatamente o mesmo comando, então instalações da versão 3.x são reconhecidas, trocadas e removidas.
 // Os scripts RadarHook.ps1 e StatusLine.ps1 vêm embutidos no .exe e são gravados em %LOCALAPPDATA%\MeuClaudeWidget.
 public static class RadarSetup {
  static readonly string[] Events = { "SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest", "PostToolUse", "PostToolUseFailure", "Notification", "SubagentStart", "SubagentStop", "Stop", "StopFailure", "SessionEnd" };
  const string NotificationMatcher = "permission_prompt|idle_prompt|elicitation_dialog|elicitation_url_dialog|agent_needs_input";

  public static string ConfigDir {
   get {
    string dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
    if (String.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    return Path.GetFullPath(dir);
   }
  }
  public static string SettingsPath { get { return Path.Combine(ConfigDir, "settings.json"); } }
  public static string HookPath { get { return Path.Combine(Data.Folder, "RadarHook.ps1"); } }
  public static string StatusPath { get { return Path.Combine(Data.Folder, "StatusLine.ps1"); } }
  static string HookCommand { get { return Command("MeuClaudeWidgetRadar", HookPath); } }
  static string StatusCommand { get { return Command("MeuClaudeWidgetStatus", StatusPath); } }

  // EncodedCommand evita qualquer interpretação de espaços e caracteres especiais do caminho pelo shell.
  static string Command(string marker, string scriptPath) {
   string path64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(scriptPath));
   string wrapper = "# " + marker + "\n& ([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + path64 + "')))";
   return "powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapper));
  }

  // O radar está ligado quando o settings.json contém o nosso comando em algum evento.
  public static bool Installed() {
   try {
    if (!File.Exists(SettingsPath)) return false;
    return File.ReadAllText(SettingsPath).Contains(HookCommand);
   } catch { return false; }
  }

  public static string Install() { return Apply(false); }
  public static string Remove() { return Apply(true); }

  static string Apply(bool remove) {
   string path = SettingsPath, command = HookCommand, statusCommand = StatusCommand;
   bool existed = File.Exists(path);
   string original = existed ? File.ReadAllText(path) : null;
   JsonObject config;
   if (existed) {
    JsonNode node;
    try { node = JsonNode.Parse(original, null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
    catch (Exception error) { throw new InvalidOperationException("settings.json precisa ser um objeto JSON válido. Nenhum arquivo foi alterado. (" + error.Message + ")"); }
    config = node as JsonObject;
    if (config == null) throw new InvalidOperationException("settings.json precisa ser um objeto JSON válido. Nenhum arquivo foi alterado.");
   } else config = new JsonObject();
   if (config["hooks"] == null) config["hooks"] = new JsonObject();
   var hooks = config["hooks"] as JsonObject;
   if (hooks == null) throw new InvalidOperationException("Campo hooks inesperado. Nenhum arquivo foi alterado.");

   // Remove só o comando exato desta integração, deixando todos os outros hooks intactos.
   foreach (var name in hooks.Select(p => p.Key).ToList()) {
    var groups = new List<JsonNode>();
    foreach (var group in Items(hooks[name])) {
     var obj = group as JsonObject;
     var list = obj == null ? null : obj["hooks"] as JsonArray;
     if (list == null) { groups.Add(group); continue; }
     var kept = list.Where(h => !(h is JsonObject) || Str(((JsonObject)h)["command"]) != command).ToList();
     if (kept.Count == list.Count) groups.Add(group);
     else if (kept.Count > 0) { obj["hooks"] = new JsonArray(kept.Select(Clone).ToArray()); groups.Add(group); }
    }
    if (groups.Count > 0) hooks[name] = new JsonArray(groups.Select(Clone).ToArray()); else hooks.Remove(name);
   }

   // A barra de status é uma configuração única: só é tomada quando está vazia ou já é nossa.
   var status = config["statusLine"] as JsonObject;
   bool statusOurs = status != null && Str(status["command"]) == statusCommand;
   bool statusFree = !config.ContainsKey("statusLine") || config["statusLine"] == null;
   if (statusOurs) config.Remove("statusLine");
   string statusMessage = "";
   if (!remove) {
    var disabled = config["disableAllHooks"] as JsonValue; bool off;
    if (disabled != null && disabled.TryGetValue(out off) && off) throw new InvalidOperationException("Os hooks estão desativados na sua configuração. Nenhuma configuração foi alterada.");
    foreach (var eventName in Events) {
     // async: o Claude nunca espera pelo radar; cada evento custa nada no turno.
     var handler = new JsonObject { ["type"] = "command", ["command"] = command, ["async"] = true, ["timeout"] = 10 };
     var group = new JsonObject { ["hooks"] = new JsonArray(handler) };
     if (eventName == "Notification") group["matcher"] = NotificationMatcher;
     var existing = Items(hooks[eventName]).Select(Clone).ToList(); existing.Add(group);
     hooks[eventName] = new JsonArray(existing.ToArray());
    }
    if (statusOurs || statusFree) {
     config["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = statusCommand };
     statusMessage = "Barra de status ativada: modelo, contexto e custo aparecem no terminal e no widget.";
    } else statusMessage = "Você já tem uma barra de status própria; ela foi mantida. Contexto e custo não aparecerão no widget.";
   }
   if (hooks.Count == 0 && remove && !existed) config.Remove("hooks");

   string json = config.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
   JsonNode.Parse(json); // confere que o resultado é JSON válido antes de gravar
   Directory.CreateDirectory(ConfigDir);
   Directory.CreateDirectory(Data.Folder);
   if (File.Exists(path) != existed || (existed && File.ReadAllText(path) != original)) throw new InvalidOperationException("A configuração mudou durante a operação. Tente novamente.");
   string backup = path + ".meu-widget-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".bak";
   if (!remove) { WriteScript("RadarHook.ps1", HookPath); WriteScript("StatusLine.ps1", StatusPath); }
   string temp = path + ".meu-widget-" + Environment.ProcessId + ".tmp";
   try {
    File.WriteAllText(temp, json, new UTF8Encoding(false));
    if (existed) File.Replace(temp, path, backup); else File.Move(temp, path);
   } finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }

   var lines = new List<string>();
   if (remove) {
    foreach (var script in new[] { HookPath, StatusPath }) if (File.Exists(script)) File.Delete(script);
    lines.Add("Radar removido. Os demais hooks foram preservados.");
    if (statusOurs) lines.Add("Barra de status do widget removida.");
   } else {
    lines.Add("Radar ativado! Reabra as sessões do Claude Code e envie uma mensagem.");
    lines.Add(statusMessage);
   }
   if (existed) lines.Add("Backup: " + backup);
   lines.Add("Configuração: " + path);
   lines.Add("Nenhuma permissão de ferramenta foi aprovada ou alterada.");
   return String.Join(Environment.NewLine, lines);
  }

  // Grava o script embutido tal qual está no .exe (com BOM UTF-8, que o Windows PowerShell exige para ler acentos).
  static void WriteScript(string name, string target) {
   using (var stream = typeof(RadarSetup).Assembly.GetManifestResourceStream(name)) {
    if (stream == null) throw new InvalidOperationException("Script " + name + " não encontrado dentro do aplicativo.");
    using (var file = File.Create(target)) stream.CopyTo(file);
   }
  }
  static IEnumerable<JsonNode> Items(JsonNode node) {
   var array = node as JsonArray; if (array != null) return array.ToList();
   return node == null ? new List<JsonNode>() : new List<JsonNode> { node };
  }
  static JsonNode Clone(JsonNode node) { return node == null ? null : JsonNode.Parse(node.ToJsonString()); }
  static string Str(JsonNode node) { var value = node as JsonValue; string s; return value != null && value.TryGetValue(out s) ? s : null; }
 }
}
