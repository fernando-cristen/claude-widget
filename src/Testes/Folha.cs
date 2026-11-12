// Folha de sprites do bichinho pixel art: cada humor e ação lado a lado, em dois quadros, na escala da faixa
// e na da área de trabalho. Roda com: MeuClaude.exe --folha-bichinho <pasta>
namespace MeuClaudeWidget {
 public partial class Widget {
  internal string testMood, testAct, testTool; internal double? testT;
  static readonly string[][] SheetStates = {
   new[] { "parado", "idle", "" }, new[] { "trabalhando", "work", "" }, new[] { "lendo", "work", "", "Read" }, new[] { "comando longo", "work", "waitTap" },
   new[] { "precisa de você", "wave", "" }, new[] { "impaciente", "tap", "" }, new[] { "braços cruzados", "cross", "" }, new[] { "cochilando", "doze", "" },
   new[] { "feliz (terminou)", "happy", "" }, new[] { "dançando", "dance", "" }, new[] { "rindo (cutucado)", "laugh", "" }, new[] { "tonto", "dizzy", "" },
   new[] { "emburrado", "grumpy", "" }, new[] { "susto (erro)", "shock", "" }, new[] { "ops", "oops", "" }, new[] { "dormindo", "sleep", "" },
   new[] { "espreguiçando", "stretch", "" }, new[] { "almoçando", "eat", "" }, new[] { "aponta terminal", "work", "point" }, new[] { "alcança", "idle", "reach" },
   new[] { "tchau", "idle", "bye" }, new[] { "uau", "idle", "wow" }, new[] { "coça a cabeça", "idle", "scratch" }, new[] { "boceja", "idle", "yawn" },
   new[] { "carregado", "idle", "held" }, new[] { "pousando", "idle", "land" }, new[] { "bate na janela", "idle", "knock" }, new[] { "emburrado (bico)", "idle", "pout" },
  };
  public static string FolhaBichinho(string folder) {
   using (var w = new Widget(true)) {
    w.settings.NightMode = false; w.settings.WeatherFx = false; w.settings.Music = false;
    foreach (bool big in new[] { false, true }) {
     if (big) w.current = w.desk;
     float px = Math.Max(1f, w.Zs * 2.45f);
     int cw = (int)Math.Ceiling(px * 22), ch = (int)Math.Ceiling(px * 18) + 16, cols = 7, rows = (SheetStates.Length + cols - 1) / cols;
     using (var sheet = new Bitmap(cw * cols * 2, ch * rows))
     using (var g = Graphics.FromImage(sheet))
     using (var font = new Font("Segoe UI", 7f)) {
      g.Clear(Color.FromArgb(28, 30, 34));
      for (int i = 0; i < SheetStates.Length; i++) {
       var s = SheetStates[i];
       for (int frame = 0; frame < 2; frame++) {
        int cx = (i % cols) * cw * 2 + frame * cw, cy = (i / cols) * ch;
        w.testMood = s[1]; w.testAct = s[2]; w.testTool = s.Length > 3 ? s[3] : ""; w.testT = 10.0 + frame * 0.27;
        w.mood = ""; w.moodSince = DateTime.Now.AddSeconds(-0.3);
        var saved = g.Save(); g.SetClip(new Rectangle(cx, cy, cw, ch));
        w.PaintCreature(g, cx + (int)(px * 4), cy - (int)(px * 1));
        g.Restore(saved);
        if (frame == 0) TextRenderer.DrawText(g, s[0], font, new Rectangle(cx, cy + ch - 15, cw * 2, 14), Color.FromArgb(200, 204, 210), TextFormatFlags.HorizontalCenter);
       }
      }
      using (var view = new Bitmap(sheet.Width * (big ? 1 : 3), sheet.Height * (big ? 1 : 3))) {
       using (var vg = Graphics.FromImage(view)) { vg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; vg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half; vg.DrawImage(sheet, 0, 0, view.Width, view.Height); }
       view.Save(Path.Combine(folder, big ? "folha-mesa.png" : "folha-faixa.png"), System.Drawing.Imaging.ImageFormat.Png);
      }
     }
    }
    w.current = null;
   }
   return "Folhas gravadas em " + folder;
  }
 }
}
