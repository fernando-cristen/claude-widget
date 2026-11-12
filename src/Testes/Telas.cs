// Captura das janelas com dados de exemplo, para revisar o visual sem abrir o widget.
// Roda com: MeuClaude.exe --capturar-telas <pasta>
namespace MeuClaudeWidget {
 public static class CapturaTelas {
  static void Save(Form form, string path) {
   form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
   form.Show(); for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); }
   using (var bmp = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height)); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
   form.Close(); form.Dispose();
  }
  // The center reads the history in the background: wait for it, type the search if any, then capture.
  static void SaveCentral(CentralForm form, string path, string query) {
   form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false; form.Show();
   var until = DateTime.Now.AddSeconds(120);
   do { Application.DoEvents(); System.Threading.Thread.Sleep(50); } while (form.Busy && DateTime.Now < until);
   if (query.Length > 0) form.Search(query);
   for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
   using (var bmp = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height)); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
   form.Close(); form.Dispose();
  }
  public static string Run(string folder) {
   var now = DateTime.Now;
   var sessions = new List<SessionInfo> {
    new SessionInfo { Id = "a", Cwd = @"C:\workspace\panoverse-backend", State = "working", Tool = "Edit", Updated = DateTimeOffset.UtcNow.ToString("o"), TurnStarted = DateTimeOffset.UtcNow.AddMinutes(-3).ToString("o"), Model = "Opus", Context = 42, Cost = 1.2 },
    new SessionInfo { Id = "b", Cwd = @"C:\workspace\admin-panorama-sistemas", State = "waiting", Updated = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"), Model = "Sonnet", Context = 18, Cost = .4 },
    new SessionInfo { Id = "c", Cwd = @"C:\Users\me\claude-widget", State = "done", Updated = DateTimeOffset.UtcNow.AddMinutes(-40).ToString("o"), Model = "Opus", Context = 71, Cost = 3.1 },
   };
   // The session center with the real history of this computer and the sessions the radar sees now.
   Func<List<SessionInfo>> radar = () => RadarReader.Read();
   SaveCentral(new CentralForm(radar, id => { }, id => { }, u => { }, new Settings(), () => { }), Path.Combine(folder, "tela-sessoes.png"), "");
   SaveCentral(new CentralForm(radar, id => { }, id => { }, u => { }, new Settings(), () => { }), Path.Combine(folder, "tela-sessoes-busca.png"), "473854");
   var items = new List<LogItem> { new LogItem { Issue = 473854, Subject = "Ajustar domínio no hosts do vault ao publicar configs", Since = now.Date.AddHours(9), Raw = 6.2, Hours = 6 }, new LogItem { Issue = 485944, Subject = "Revisar fila de e-mails", Since = now.Date.AddHours(14), Raw = .2 } };
   Save(new PreviewForm(items, "", "Hoje às 17:55 · seg a sex", now), Path.Combine(folder, "tela-apontamento.png"));
   var brief = new Briefing { Day = now.Date.AddDays(-1), Name = "Fernando", Usage = "Sessão 5h em 12% · semana em 31%" };
   brief.Tasks.Add(new TaskRecap { Id = 473854, Subject = "Ajustar domínio no hosts do vault", Status = "Em andamento", Hours = 5.5 });
   brief.Tasks.Add(new TaskRecap { Id = 485944, Subject = "Revisar fila de e-mails", Status = "Em teste", Hours = 2 });
   brief.Sessions.Add(new SessionRecap { Id = "a", Project = "panoverse-backend", Asked = "corrige o domínio do vault quando publicar as configs", Answered = "Ajustei o gerador de hosts e adicionei um teste para o domínio novo.", Git = "master · 3 arquivos · +120 / −14", Issue = 473854, Open = true, Last = DateTimeOffset.Now.AddHours(-15) });
   brief.News.Add(new NewsItem { Kind = "rejected", Title = "#485944 rejeitada na QA", Detail = "\"Faltou tratar o reenvio\" · Ana" });
   brief.News.Add(new NewsItem { Kind = "assigned", Title = "#486010 atribuída a você", Detail = "Erro ao exportar relatório · Bruno" });
   var bf = new BriefingForm(u => { }, id => { }); bf.SetBriefing(brief);
   Save(bf, Path.Combine(folder, "tela-bomdia.png"));
   return "Telas gravadas em " + folder;
  }
 }
}
