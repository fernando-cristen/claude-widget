namespace MeuClaudeWidget {
 using System.Diagnostics;

 // Automatic updates from the GitHub releases of the project: looks for a newer tag, downloads the MeuClaude.exe
 // asset next to the running one and swaps the two on restart. The running .exe is renamed (Windows allows that)
 // and the leftover ".old" is removed on the next start. Only the releases API and the asset URL are contacted.
 public static class Updater {
  public const string Repo = "fernando-cristen/claude-widget";
  public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases"; } }
  public static Version Current { get { var v = typeof(Updater).Assembly.GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); } }
  public sealed class Release { public Version Version; public string Tag = "", Url = "", Page = "", Notes = ""; public long Size; }
  static readonly HttpClient http = Client();
  static HttpClient Client() {
   var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
   c.DefaultRequestHeaders.UserAgent.ParseAdd("MeuClaudeWidget/" + Current);
   c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
   return c;
  }

  // The latest release, or null when there is none or it is not newer than this build.
  public static async Task<Release> CheckAsync() {
   using (var response = await http.GetAsync("https://api.github.com/repos/" + Repo + "/releases/latest")) {
    if (response.StatusCode == HttpStatusCode.NotFound) return null;
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("GitHub respondeu " + (int)response.StatusCode + ".");
    var root = Data.Map(Data.Json().DeserializeObject(await response.Content.ReadAsStringAsync()));
    string tag = Data.Str(Data.Get(root, "tag_name")).Trim();
    Version version; if (!Version.TryParse(tag.TrimStart('v', 'V'), out version)) return null;
    version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    if (version <= Current) return null;
    var release = new Release { Version = version, Tag = tag, Page = Data.Str(Data.Get(root, "html_url")), Notes = Data.Str(Data.Get(root, "body")) };
    foreach (var item in Items(Data.Get(root, "assets"))) {
     var asset = Data.Map(item);
     if (String.Equals(Data.Str(Data.Get(asset, "name")), "MeuClaude.exe", StringComparison.OrdinalIgnoreCase)) {
      release.Url = Data.Str(Data.Get(asset, "browser_download_url")); long size; Int64.TryParse(Data.Str(Data.Get(asset, "size")), out size); release.Size = size;
     }
    }
    return release;
   }
  }
  static object[] Items(object value) { return value as object[] ?? new object[0]; }

  public static string PendingPath(Release r) { return Path.Combine(Data.Folder, "update", "MeuClaude-" + r.Version + ".exe"); }

  // Downloads the asset to the local data folder and checks it looks like a Windows executable of the announced size.
  public static async Task<string> DownloadAsync(Release r) {
   if (String.IsNullOrEmpty(r.Url)) throw new InvalidOperationException("A versão " + r.Version + " não traz o arquivo MeuClaude.exe. Baixe pela página de versões.");
   string target = PendingPath(r), temp = target + ".part";
   Directory.CreateDirectory(Path.GetDirectoryName(target));
   if (File.Exists(target) && Valid(target, r.Size)) return target;
   using (var response = await http.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead)) {
    response.EnsureSuccessStatusCode();
    using (var stream = await response.Content.ReadAsStreamAsync()) using (var file = File.Create(temp)) await stream.CopyToAsync(file);
   }
   if (!Valid(temp, r.Size)) { File.Delete(temp); throw new InvalidOperationException("O arquivo baixado não confere com o publicado."); }
   if (File.Exists(target)) File.Delete(target);
   File.Move(temp, target);
   return target;
  }
  static bool Valid(string path, long size) {
   try {
    var info = new FileInfo(path);
    if (info.Length < 1024 * 1024 || (size > 0 && info.Length != size)) return false;
    using (var f = File.OpenRead(path)) { return f.ReadByte() == 'M' && f.ReadByte() == 'Z'; }
   } catch { return false; }
  }

  // Swaps the executables and restarts. Throws (with everything back in place) when the folder is not writable.
  public static void Apply(string downloaded) {
   string exe = Program.ExePath; if (String.IsNullOrEmpty(exe)) throw new InvalidOperationException("Caminho do aplicativo desconhecido.");
   string old = exe + ".old";
   if (File.Exists(old)) File.Delete(old);
   File.Move(exe, old);
   try { File.Move(downloaded, exe); }
   catch { File.Move(old, exe); throw; }
   Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
   Application.Exit();
  }
  public static void CleanOld() {
   try { string old = Program.ExePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
   try { string dir = Path.Combine(Data.Folder, "update"); if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.part")) File.Delete(f); } catch { }
  }
 }

 public partial class Widget {
  System.Windows.Forms.Timer updateTimer; bool updateBusy; DateTime nextUpdateCheck; Updater.Release updateFound; string updateFile;

  void InitUpdates() {
   if (preview) return;
   Updater.CleanOld();
   nextUpdateCheck = DateTime.Now.AddMinutes(2);
   updateTimer = new System.Windows.Forms.Timer { Interval = 60000 };
   updateTimer.Tick += async delegate {
    if (!settings.AutoUpdate || updateBusy || DateTime.Now < nextUpdateCheck) return;
    nextUpdateCheck = DateTime.Now.AddHours(6);
    await CheckUpdates(false);
   };
   updateTimer.Start();
  }

  // manual: from the menu, so the result is always told; automatic: silent unless a new version is ready.
  async Task CheckUpdates(bool manual) {
   if (updateBusy) return; updateBusy = true;
   try {
    var release = await Updater.CheckAsync();
    if (release == null) { if (manual) MessageBox.Show(this, "Você já está na versão mais recente (" + Updater.Current + ").", "Atualização"); return; }
    if (!manual && settings.UpdateSkip == release.Version.ToString()) return;
    string file;
    try { file = await Updater.DownloadAsync(release); }
    catch (Exception error) {
     if (manual && MessageBox.Show(this, "Há uma versão nova (" + release.Version + "), mas não consegui baixar:\n" + error.Message + "\n\nAbrir a página de versões?", "Atualização", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) OpenUrl(release.Page.Length > 0 ? release.Page : Updater.ReleasesPage);
     return;
    }
    updateFound = release; updateFile = file;
    if (manual) OfferUpdate();
    else { balloonSession = null; balloonAction = OfferUpdate; tray.ShowBalloonTip(10000, "Meu Claude " + release.Version, "Nova versão baixada. Clique para atualizar; leva um segundo.", ToolTipIcon.Info); }
   } catch (Exception error) {
    if (manual) MessageBox.Show(this, "Não foi possível procurar atualização:\n" + error.Message, "Atualização", MessageBoxButtons.OK, MessageBoxIcon.Warning);
   } finally { updateBusy = false; }
  }

  void OfferUpdate() {
   if (updateFound == null || updateFile == null) return;
   string notes = (updateFound.Notes ?? "").Trim(); if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";
   var answer = MessageBox.Show(this, "Atualizar para a versão " + updateFound.Version + " agora?\nO widget fecha e reabre sozinho; login e preferências continuam.\n\n" + (notes.Length > 0 ? notes : "Sem notas de versão."), "Atualização", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
   if (answer == DialogResult.Yes) ApplyUpdate();
   else if (answer == DialogResult.No) { settings.UpdateSkip = updateFound.Version.ToString(); TrySave(); } // não avisar de novo desta versão
  }
  void ApplyUpdate() {
   try { stopped = true; TrySave(); Updater.Apply(updateFile); }
   catch (Exception error) { stopped = false; MessageBox.Show(this, "Não consegui trocar o arquivo:\n" + error.Message + "\n\nA versão nova está em " + updateFile + ". Copie-a por cima do MeuClaude.exe com o widget fechado.", "Atualização", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
  }
 }
}
