namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Diagnostics;
 using System.Drawing;
 using System.Globalization;
 using System.IO;
 using System.Linq;
 using System.Text;
 using System.Threading.Tasks;
 using System.Windows.Forms;

 public class SessionInfo {
  public string Id = "", Cwd = "", State = "", Event = "", Tool = "", Updated = "", TurnStarted = "", TurnEnded = "", ClaudeStarted = "";
  public int Subagents, ClaudePid, WindowPid;
  // Filled by RadarReader: process liveness and the status line record of the same session.
  public bool Closed;
  public string Model = "";
  public double Context = -1, Cost = -1;
  public int Issue; // Redmine issue linked in this session (found by the status line), 0 when none
  static DateTimeOffset? Parse(string value) { DateTimeOffset t; return DateTimeOffset.TryParse(value, out t) ? t : (DateTimeOffset?)null; }
  public DateTimeOffset Time { get { return Parse(Updated) ?? DateTimeOffset.MinValue; } }
  public string Project { get { try { return new DirectoryInfo(Cwd).Name; } catch { return "Projeto"; } } }
  public string Effective { get { return Closed && State != "ended" ? "closed" : State; } }
  public bool Silent { get { return (State == "working" || State == "waiting" || State == "background") && DateTimeOffset.UtcNow-Time > TimeSpan.FromMinutes(10); } }
  public bool Active { get { return !Closed && !Silent && (State == "working" || State == "waiting" || State == "background"); } }
  public string Label {
   get {
    if (Closed && State != "ended") return "Sessão fechada";
    if (Silent) return "Sem evento recente";
    switch(State) {
     case "working": return "Trabalhando";
     case "waiting": return "Precisa de você";
     case "done": return "Resposta concluída";
     case "background": return "Trabalho em segundo plano";
     case "error": return "Erro no turno";
     case "ended": return "Sessão encerrada";
     case "idle": return "Aguardando prompt";
     default: return "Pronto";
    }
   }
  }
  // How long the turn has run ("há 4min") or took ("em 3min").
  public string Elapsed {
   get {
    if (Closed || Silent) return "";
    DateTimeOffset? started = Parse(TurnStarted), ended = Parse(TurnEnded);
    if (State == "working" && started.HasValue) return "há " + Span(DateTimeOffset.UtcNow-started.Value);
    if (State == "waiting") return "há " + Span(DateTimeOffset.UtcNow-Time);
    if ((State == "done" || State == "error" || State == "background") && started.HasValue && ended.HasValue && ended.Value >= started.Value) return "em " + Span(ended.Value-started.Value);
    return "";
   }
  }
  // Tool and subagents of the current turn: "Bash · 2 subagentes".
  public string Extras {
   get {
    var parts = new List<string>();
    if (!Closed && !Silent && (State == "working" || State == "waiting") && Tool.Length > 0) parts.Add(Tool);
    if (!Closed && Subagents > 0) parts.Add(Subagents + (Subagents == 1 ? " subagente" : " subagentes"));
    return String.Join(" · ", parts);
   }
  }
  // "Trabalhando há 4min · Bash · 2 subagentes"
  public string Activity {
   get {
    string text = Label, elapsed = Elapsed, extras = Extras;
    if (elapsed.Length > 0) text += " " + elapsed;
    if (extras.Length > 0) text += " · " + extras;
    return text;
   }
  }
  public string Usage {
   get {
    var parts = new List<string>();
    if (Model.Length > 0) parts.Add(Model);
    if (Context >= 0) parts.Add("contexto " + Math.Round(Context) + "%");
    if (Cost >= 0) parts.Add("US$ " + Cost.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")));
    return String.Join(" · ", parts);
   }
  }
  public static string Span(TimeSpan value) {
   if (value.TotalSeconds < 60) return Math.Max(0,(int)value.TotalSeconds) + "s";
   if (value.TotalHours < 1) return (int)value.TotalMinutes + "min";
   return (int)value.TotalHours + "h " + value.Minutes + "min";
  }
 }
 public class StatusRecord { public string Model = "", Updated = "", FiveReset = "", WeekReset = ""; public double Context = -1, Cost = -1, Five = -1, Week = -1; public int Issue; }
 public static class RadarReader {
  static string Dir { get { return Path.Combine(Data.Folder,"radar"); } }
  public static List<SessionInfo> Read() {
   var result = new List<SessionInfo>();
   string dir = Dir; if (!Directory.Exists(dir)) return result;
   foreach (var file in new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc).Take(300)) {
    try {
     if (file.Length > 32768) continue;
     var item = Data.Json().Deserialize<SessionInfo>(File.ReadAllText(file.FullName));
     if (item == null || String.IsNullOrWhiteSpace(item.Id) || String.IsNullOrWhiteSpace(item.Cwd) || DateTimeOffset.UtcNow-item.Time >= TimeSpan.FromHours(24)) continue;
     item.Closed = item.State != "ended" && !Alive(item);
     string status = Path.Combine(dir,"status",item.Id+".json");
     if (File.Exists(status) && new FileInfo(status).Length < 8192) {
      try { var s = Data.Json().Deserialize<StatusRecord>(File.ReadAllText(status)); if (s != null) { item.Model = s.Model ?? ""; item.Context = s.Context; item.Cost = s.Cost; item.Issue = s.Issue; } } catch { }
     }
     result.Add(item);
    } catch { }
   }
   return result.OrderByDescending(s=>s.Time).ToList();
  }
  // Most recent subscription limits seen by any session's status line, or null.
  public static StatusRecord LatestLimits() {
   string dir = Path.Combine(Dir,"status"); if (!Directory.Exists(dir)) return null;
   foreach (var file in new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc).Take(20)) {
    try { var s = Data.Json().Deserialize<StatusRecord>(File.ReadAllText(file.FullName)); if (s != null && (s.Five >= 0 || s.Week >= 0)) return s; } catch { }
   }
   return null;
  }
  // Ctrl+C or closing the terminal fires no SessionEnd; the recorded Claude process tells the truth.
  internal static bool Alive(SessionInfo s) {
   if (s.ClaudePid <= 0) return true;
   DateTimeOffset expected; if (!DateTimeOffset.TryParse(s.ClaudeStarted, out expected)) return true;
   try {
    using (var p = Process.GetProcessById(s.ClaudePid)) {
     if (p.HasExited) return false;
     return Math.Abs((p.StartTime.ToUniversalTime()-expected.UtcDateTime).TotalSeconds) < 2;
    }
   } catch (ArgumentException) { return false; }
   catch { return true; }
  }
  // Session files only matter for 24 hours; older ones and interrupted temporaries are removed.
  public static void Cleanup() {
   foreach (string folder in new string[] { Dir, Path.Combine(Dir,"status") }) {
    if (!Directory.Exists(folder)) continue;
    foreach (var file in new DirectoryInfo(folder).GetFiles()) {
     try {
      var age = DateTime.UtcNow-file.LastWriteTimeUtc;
      if ((file.Extension == ".json" && age > TimeSpan.FromDays(7)) || (file.Extension == ".tmp" && age > TimeSpan.FromHours(1))) file.Delete();
     } catch { }
    }
   }
  }
 }
 public class GitInfo {
  public string Branch = "", Error = "", Root = "";
  public int Files, Untracked, Binary;
  public long Added, Removed;
  public DateTimeOffset? Commit;
  public bool LinesKnown;
  public string Summary {
   get {
    if (Error.Length > 0) return Error;
    return Files+" arq · "+(LinesKnown ? "+"+Added+" / −"+Removed : "sem commit inicial");
   }
  }
  public string Details {
   get {
    if (Error.Length > 0) return Error;
    string age = "sem commit inicial";
    if (Commit.HasValue) {
     TimeSpan elapsed = DateTimeOffset.UtcNow-Commit.Value;
     age = elapsed.TotalDays >= 1 ? ((int)elapsed.TotalDays)+"d" : elapsed.TotalHours >= 1 ? ((int)elapsed.TotalHours)+"h" : Math.Max(0,(int)elapsed.TotalMinutes)+"min";
     age = "último commit há "+age+" ("+Commit.Value.ToLocalTime().ToString("dd/MM HH:mm")+")";
    }
    return "Branch: "+Branch+" · "+age+"\r\n"+Summary+"\r\n"+Untracked+" arquivo(s) novo(s) fora do Git; suas linhas não entram em +/−.\r\n"+Binary+" arquivo(s) binário(s) no diff. Mudanças de submódulos não incluídas.\r\nMudanças totais do projeto, de qualquer autor, em relação a HEAD.\r\n"+Root;
   }
  }
 }
 public static class GitReader {
  static string GitPath() {
   foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
    try { string clean=folder.Trim().Trim('"'); if (!Path.IsPathRooted(clean)) continue; string candidate=Path.Combine(clean,"git.exe"); if (File.Exists(candidate)) return candidate; } catch { }
   }
   throw new InvalidOperationException("Git não encontrado no PATH do Windows");
  }
  sealed class Output { public int Code; public string Text; }
  static Output Run(string exe,string directory,string args) {
   var info=new ProcessStartInfo { FileName=exe, WorkingDirectory=directory, Arguments="--no-optional-locks -c core.fsmonitor=false -c core.hooksPath=NUL "+args, UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true, RedirectStandardInput=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
   info.EnvironmentVariables["GIT_TERMINAL_PROMPT"]="0";
   // No shell, external diff, textconv, fsmonitor, hooks, fetch, reset or commit.
   using (var process=new Process { StartInfo=info }) {
    process.Start(); process.StandardInput.Close();
    Task<string> stdout=process.StandardOutput.ReadToEndAsync(), stderr=process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(5000)) { try { process.Kill(); } catch { } throw new TimeoutException(); }
    Task.WaitAll(new Task[] { stdout,stderr },1000);
    if (!stdout.IsCompleted) throw new TimeoutException();
    return new Output { Code=process.ExitCode, Text=stdout.Result };
   }
  }
  public static int CountFiles(string status,out int untracked) {
   untracked=0; int count=0; var parts=status.Split('\0');
   for(int i=0;i<parts.Length;i++) {
    string item=parts[i]; if(item.Length<3) continue;
    count++; if(item.StartsWith("??",StringComparison.Ordinal)) untracked++;
    if(item[0]=='R'||item[0]=='C'||item[1]=='R'||item[1]=='C') i++;
   }
   return count;
  }
  public static void CountLines(string numstat,GitInfo value) {
   var parts=numstat.Split('\0');
   for(int i=0;i<parts.Length;i++) {
    string[] cells=parts[i].Split(new char[] {'\t'},3); if(cells.Length!=3) continue;
    long a,b;
    if(cells[0]=="-" && cells[1]=="-") value.Binary++;
    else if(Int64.TryParse(cells[0],out a)&&Int64.TryParse(cells[1],out b)) { value.Added+=a; value.Removed+=b; }
    if(cells[2].Length==0) i+=2; // -z rename has old/new names as two extra fields
   }
  }
  public static GitInfo Read(string directory) {
   var result=new GitInfo();
   try {
    if(!Path.IsPathRooted(directory)||!Directory.Exists(directory)) { result.Error="Pasta do projeto indisponível"; return result; }
    string exe=GitPath();
    var root=Run(exe,directory,"rev-parse --show-toplevel");
    if(root.Code!=0) { result.Error="Sem repositório Git acessível"; return result; }
    result.Root=root.Text.Trim();
    var status=Run(exe,result.Root,"status --porcelain=v1 -z --untracked-files=all --ignore-submodules=all");
    if(status.Code!=0) throw new InvalidOperationException("Git não pôde ler o estado do projeto");
    result.Files=CountFiles(status.Text,out result.Untracked);
    var branch=Run(exe,result.Root,"symbolic-ref --short HEAD"); result.Branch=branch.Code==0 ? branch.Text.Trim() : "HEAD separado";
    var log=Run(exe,result.Root,"log -1 --format=%ct --no-show-signature"); long unix;
    if(log.Code==0 && Int64.TryParse(log.Text.Trim(),out unix)) {
     result.Commit=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(unix);
     var diff=Run(exe,result.Root,"diff --no-ext-diff --no-textconv --ignore-submodules=all --numstat -z HEAD --");
     if(diff.Code!=0) throw new InvalidOperationException("Git não pôde calcular o diff");
     CountLines(diff.Text,result); result.LinesKnown=true;
    }
   } catch(TimeoutException) { result.Error="Git demorou demais; nova leitura em breve"; }
   catch(InvalidOperationException ex) { result.Error=ex.Message; }
   catch { result.Error="Não foi possível consultar o Git"; }
   return result;
  }
 }
}
