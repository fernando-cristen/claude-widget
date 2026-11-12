namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Linq;
 using System.Windows.Forms;

 // Preview of today's automatic time entry (nothing is written).
 public sealed class PreviewForm : PremiumWindow {
  public PreviewForm(List<LogItem> items, string error, string schedule, DateTime now) : base("Prévia do apontamento", 620, 420) {
   var list = new List<El> { new TitleEl { Glyph = Theme.Clock, Color = Theme.Accent, Title = "Prévia do apontamento", Subtitle = schedule } };
   list.Add(new SectionEl { Text = "Tarefas em andamento", Right = "jornada 08:00–12:00 · 13:12–18:00" });
   var card = new CardEl { Pad = 6 };
   if (error.Length > 0) card.Add(new TextEl { Text = "Não foi possível consultar o Redmine. " + error, Color = Theme.Red, Inset = 10, Before = 0 });
   else if (items.Count == 0) card.Add(new TextEl { Text = "Nenhuma tarefa sua em andamento agora. Nada seria apontado.", Color = Theme.Muted, Inset = 10, Before = 0 });
   foreach (var item in items) {
    string detail = "Em andamento desde " + AutoLog.Since(item, now) + " · hoje até 18:00: " + WorkDay.Duration(item.Raw);
    if (item.Logged > 0) detail += "\nJá apontado hoje nesta tarefa: " + WorkDay.Amount(item.Logged);
    card.Add(new RowEl { Title = "#" + item.Issue + "  " + item.Subject, Subtitle = detail,
     Chip = item.Hours > 0 ? "aponta " + WorkDay.Amount(item.Hours) : "nada a apontar", ChipColor = item.Hours > 0 ? Theme.Green : Theme.Muted, Right = item.Hours > 0 ? "Desenvolvimento" : "" });
   }
   list.Add(card);
   list.Add(new TextEl { Text = "Arredonda para o 0,5h mais próximo e desconta o que você já apontou hoje na tarefa. Esta prévia não grava nada no Redmine.", Color = Theme.Muted, Font = Theme.F(Theme.Sans, 8.5f), Before = 16, Inset = 4 });
   Body.SetItems(list);
   SetFooter(new ButtonsEl().Add("Entendi", true, Close));
  }
 }
}
