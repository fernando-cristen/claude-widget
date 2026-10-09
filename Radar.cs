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
  public string Id = "", Cwd = "", State = "", Event = "", Tool = "", Updated = "";
  public DateTimeOffset Time { get { DateTimeOffset t; return DateTimeOffset.TryParse(Updated, out t) ? t : DateTimeOffset.MinValue; } }
  public string Project { get { try { return new DirectoryInfo(Cwd).Name; } catch { return "Projeto"; } } }
  public string Label {
   get {
    if ((State == "working" || State == "waiting" || State == "background") && DateTimeOffset.UtcNow-Time > TimeSpan.FromMinutes(10)) return "Sem evento recente";
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
 }
 public static class RadarReader {
  public static List<SessionInfo> Read() {
   var result = new List<SessionInfo>();
   string dir = Path.Combine(Data.Folder,"radar"); if (!Directory.Exists(dir)) return result;
   foreach (var file in new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(f=>f.LastWriteTimeUtc).Take(300)) {
    try {
     if (file.Length > 32768) continue;
     var item = Data.Json().Deserialize<SessionInfo>(File.ReadAllText(file.FullName));
     if (item != null && !String.IsNullOrWhiteSpace(item.Id) && !String.IsNullOrWhiteSpace(item.Cwd) && DateTimeOffset.UtcNow-item.Time < TimeSpan.FromHours(24)) result.Add(item);
    } catch { }
   }
   return result.OrderByDescending(s=>s.Time).ToList();
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
 public sealed class RadarForm : Form {
  readonly ListView list=new ListView(); readonly TextBox details=new TextBox();
  readonly System.Windows.Forms.Timer clock=new System.Windows.Forms.Timer();
  readonly Func<List<SessionInfo>> read; readonly Func<string> describe; readonly Action<string> select;
  string selected=""; bool updating;
  public RadarForm(Func<List<SessionInfo>> readSessions,Func<string> description,Action<string> selectSession) {
   read=readSessions; describe=description; select=selectSession;
   Text="Claude · projetos e sessões"; Size=new Size(780,510); MinimumSize=new Size(680,440);
   StartPosition=FormStartPosition.CenterParent; Font=new Font("Segoe UI",9); AutoScaleMode=AutoScaleMode.Dpi;
   list.View=View.Details; list.FullRowSelect=true; list.MultiSelect=false; list.HideSelection=false;
   list.Columns.Add("Projeto",170); list.Columns.Add("Último estado observado",205); list.Columns.Add("Evento",140); list.Columns.Add("Horário",95);
   list.SetBounds(14,15,732,215); list.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom; Controls.Add(list);
   details.Multiline=true; details.ReadOnly=true; details.ScrollBars=ScrollBars.Vertical;
   details.SetBounds(14,243,732,165); details.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom; Controls.Add(details);
   var automatic=new Button { Text="Seguir sessão mais recente", Left=14,Top=421,Width=220,Height=30,Anchor=AnchorStyles.Bottom|AnchorStyles.Left }; Controls.Add(automatic);
   automatic.Click+=delegate { selected=""; select(""); RefreshList(); };
   var close=new Button { Text="Fechar",Left=646,Top=421,Width=100,Height=30,Anchor=AnchorStyles.Bottom|AnchorStyles.Right }; Controls.Add(close); close.Click+=delegate { Close(); };
   list.SelectedIndexChanged+=delegate { if(!updating && list.SelectedItems.Count>0) { selected=(string)list.SelectedItems[0].Tag; select(selected); details.Text=describe(); } };
   clock.Interval=2000; clock.Tick+=delegate { RefreshList(); }; Shown+=delegate { RefreshList(); clock.Start(); };
   FormClosed+=delegate { clock.Stop(); clock.Dispose(); };
  }
  void RefreshList() {
   updating=true; list.BeginUpdate(); list.Items.Clear();
   foreach(var s in read()) {
    var row=new ListViewItem(new string[]{s.Project,s.Label,s.Event,s.Time.ToLocalTime().ToString("HH:mm:ss")}); row.Tag=s.Id; row.ToolTipText=s.Cwd; list.Items.Add(row);
    if(s.Id==selected) row.Selected=true;
   }
   list.EndUpdate(); updating=false; details.Text=describe();
  }
 }
}
