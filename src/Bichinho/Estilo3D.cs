namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Globalization;

 // The 3D style: the same moods and poses as the pixel art, translated into a Pose3D and drawn by Pet3D.
 // A frame is only re-rendered when the pose changes (kept per stage).
 public partial class Widget {
  bool Style3D { get { return settings.CreatureStyle == "3d"; } }

  void Paint3D(Graphics g, float ox, float baseY, float px, string act, int lift, int sink, int tilt, int lookX, int lookY, Color body, bool running, bool still, double t, DateTime now, double fe) {
   int size = (int)Math.Round(16 * px); float unit = size / 3f;
   var p = new Pose3D { Body = Color.FromArgb(255, body), Alpha = body.A / 255f, Antenna = Shape == "robot", Mouth = "cat" };
   p.Lift = lift * px / unit; if (sink > 0) p.Squash = .9f;
   p.Tilt = tilt * .12f; p.LookX = lookX; p.LookY = lookY; p.Yaw = lookX * .3f;
   switch (mood) {
    case "work": { bool up = ((int)(t * 8)) % 2 == 0; p.Mouth = "smile"; if (lookX == 0 && lookY == 0) p.LookY = .8f; p.ArmL = up ? .9f : .6f; p.ArmR = up ? .6f : .9f; break; }
    case "happy": p.HappyEyes = true; p.Mouth = "open"; break;
    case "dance": { bool u = ((int)(t / .4)) % 2 == 0; p.HappyEyes = true; p.Mouth = "open"; p.ArmL = u ? 2.5f : .8f; p.ArmR = u ? .8f : 2.5f; break; }
    case "laugh": p.HappyEyes = true; p.Mouth = "open"; p.ArmL = p.ArmR = 2.3f; break;
    case "wave": p.Mouth = "open"; p.ArmR = 2.6f + .3f * (float)Math.Sin(t * 10); break;
    case "tap": p.Mouth = "flat"; p.StepR = ((int)(t * 3)) % 2 == 0 ? .1f : 0; break;
    case "cross": p.Mouth = "flat"; p.ArmL = p.ArmR = 1.25f; break;
    case "doze": case "nap": case "sleep": p.EyesClosed = true; p.Mouth = "flat"; p.ArmL = p.ArmR = .2f; p.Squash = .93f; break;
    case "shock": p.Mouth = "o"; p.ArmL = p.ArmR = 1.6f; p.Squash = 1.06f; break;
    case "oops": p.Mouth = "frown"; break;
    case "dizzy": p.Mouth = "o"; p.EyesClosed = ((int)(t * 6)) % 2 == 0; p.Tilt = (float)Math.Sin(t * 6) * .18f; break;
    case "stretch": p.EyesClosed = true; p.Mouth = "open"; p.ArmL = p.ArmR = 2.9f; p.Squash = 1.1f; break;
    case "eat": p.Mouth = ((int)(t * 4)) % 2 == 0 ? "o" : "cat"; p.ArmR = ((int)(t * 2)) % 2 == 0 ? 1.9f : .6f; break;
    case "grumpy": {
     double gr = (now - grumpyAt).TotalSeconds;
     if (gr < .9) { p.Mouth = "frown"; p.LookY = 0; }
     else if (gr < 1.8) { bool a = ((int)(t * 7)) % 2 == 0; p.EyesClosed = true; p.Mouth = "flat"; p.ArmL = a ? 2.2f : 1.8f; p.ArmR = a ? 1.8f : 2.2f; }
     else { p.EyesClosed = true; p.Mouth = "open"; }
     break;
    }
   }
   switch (act) {
    case "reach": case "toss": p.ArmL = p.ArmR = 2.6f; p.Mouth = "o"; break;
    case "perch": p.Mouth = "cat"; p.LookY = -1; break;
    case "bye": p.ArmR = 2.6f + .3f * (float)Math.Sin(t * 10); p.Mouth = "open"; break;
    case "wow": p.Mouth = "o"; p.LookY = -1; break;
    case "point": if (lookX < 0) p.ArmL = 2.3f; else p.ArmR = 2.3f; p.Mouth = "o"; break;
    case "groove": { bool u = ((int)(t * 2)) % 2 == 0; p.HappyEyes = true; p.Mouth = "cat"; p.ArmL = u ? 2f : .7f; p.ArmR = u ? .7f : 2f; break; }
    case "held": { bool u = ((int)(t * 10)) % 2 == 0; p.Mouth = "o"; p.ArmL = u ? 2.7f : 2.2f; p.ArmR = u ? 2.2f : 2.7f; p.StepL = u ? .08f : 0; p.StepR = u ? 0 : .08f; p.Tilt = tilt * .15f; break; }
    case "land": p.Mouth = "o"; p.ArmL = p.ArmR = 1.5f; p.Squash = .8f; break;
    case "shake": p.HappyEyes = true; break;
    case "knock": p.ArmR = ((int)(t * 5)) % 2 == 0 ? 1.4f : .9f; p.Mouth = "open"; break;
    case "pout": p.Mouth = "frown"; p.ArmL = p.ArmR = 1.25f; break;
    case "rps": p.ArmL = p.ArmR = .05f; break;
    case "fish": p.ArmL = 1.3f; break;
    case "paw": { float a = ((int)(t * 4)) % 2 == 0 ? 1.8f : 1.4f; if (pawSide > 0) p.ArmR = a; else p.ArmL = a; p.Mouth = "o"; break; }
    case "scratch": p.ArmR = ((int)(t * 8)) % 2 == 0 ? 2.9f : 2.6f; break;
    case "swing": p.StepL = ((int)(t * 5)) % 2 == 0 ? .1f : 0; p.StepR = .1f - p.StepL; break;
    case "waitTap": p.Mouth = "flat"; p.LookY = .5f; p.ArmL = p.ArmR = .3f; p.Squash = .95f; p.StepR = ((int)(t * 3)) % 2 == 0 ? .12f : 0; break;
    case "whistle": p.Mouth = "o"; break;
    case "yawn": if (fe < 1.9) { p.EyesClosed = true; p.Mouth = "open"; if (fe > .9) p.ArmL = p.ArmR = 2.8f; } break;
    case "reachUp": p.ArmL = p.ArmR = 2.8f; p.EyesClosed = true; p.Squash = 1.08f; break;
    case "lick": p.ArmR = 2f; p.HappyEyes = true; break;
    case "earScratch": p.StepR = .3f; p.HappyEyes = true; break;
   }
   if (running) { bool st = ((int)(t * 8)) % 2 == 0; p.StepL = st ? .1f : 0; p.StepR = st ? 0 : .1f; p.ArmL = st ? .9f : .4f; p.ArmR = st ? .4f : .9f; p.Tilt += lookX * .06f; }
   if (stage.Pose == "sit") { bool st = ((int)(t * 3.2)) % 2 == 0; p.StepL = st ? .06f : -.06f; p.StepR = -p.StepL; }
   // Breathing while idle, and natural blinks.
   if (!still && mood == "idle" && act.Length == 0) p.Squash *= 1 + .015f * (float)Math.Sin(t * 2.2);
   if (!p.EyesClosed && !p.HappyEyes && !still && act != "held" && act != "land" && (mood == "idle" || mood == "work" || mood == "wave" || mood == "tap" || mood == "cross" || mood == "eat") && Blinking(now)) p.EyesClosed = true;
   var bmp = Rendered(stage, p, size);
   g.InterpolationMode = InterpolationMode.NearestNeighbor;
   g.DrawImage(bmp, new Rectangle((int)Math.Round(ox + 6 * px - size / 2f), (int)Math.Round(baseY + 10 * px - size * .86f), size, size));
   stage.Crown = baseY - lift * px + (p.Antenna ? -1.2f : .6f) * px; // top of the head (or of the antenna), where a bird or a leaf lands
  }
  // Re-renders only when something visible changed.
  Bitmap Rendered(Stage st, Pose3D p, int size) {
   string key = Key3D(p, size);
   if (st.Img3D != null && st.Key3D == key) return st.Img3D;
   if (st.Img3D != null) st.Img3D.Dispose();
   st.Img3D = Pet3D.Render(p, size); st.Key3D = key;
   return st.Img3D;
  }
  static string Key3D(Pose3D p, int size) {
   var c = CultureInfo.InvariantCulture;
   return String.Join("|", Pet3D.Generation.ToString(c), size.ToString(c), p.Body.ToArgb().ToString(c), p.Alpha.ToString("0.00", c), p.Yaw.ToString("0.00", c), p.Tilt.ToString("0.00", c), p.Lift.ToString("0.00", c), p.Squash.ToString("0.000", c),
    p.ArmL.ToString("0.00", c), p.ArmR.ToString("0.00", c), p.StepL.ToString("0.00", c), p.StepR.ToString("0.00", c), p.LookX.ToString("0.0", c), p.LookY.ToString("0.0", c), p.EyesClosed ? "c" : "o", p.HappyEyes ? "h" : "", p.Antenna ? "a" : "", p.Mouth);
  }

  // The small creatures (family, visitors) in 3D, cached by pose.
  readonly Dictionary<string,Bitmap> miniCache = new Dictionary<string,Bitmap>();
  void PaintMini3D(Graphics g, float cx, float ground, float width, Color body, string state, double t, int seed) {
   int size = (int)Math.Round(width / .75f);
   var p = new Pose3D { Body = body, Antenna = false, Mouth = "cat" };
   bool u = ((int)(t * 6 + seed)) % 2 == 0;
   switch (state) {
    case "cheer": p.HappyEyes = true; p.Mouth = "open"; p.ArmL = u ? 2.6f : 1.6f; p.ArmR = u ? 1.6f : 2.6f; break;
    case "waiting": p.Mouth = "open"; p.ArmR = ((int)(t * 4)) % 2 == 0 ? 2.7f : 2.3f; break;
    case "work": p.Mouth = "smile"; p.LookY = .8f; p.ArmL = u ? .9f : .6f; p.ArmR = u ? .6f : .9f; break;
    case "walk": p.StepL = u ? .1f : 0; p.StepR = u ? 0 : .1f; p.ArmL = u ? .9f : .4f; p.ArmR = u ? .4f : .9f; break;
   }
   string key = Key3D(p, size);
   Bitmap bmp;
   if (!miniCache.TryGetValue(key, out bmp)) {
    if (miniCache.Count > 60) { foreach (var b in miniCache.Values) b.Dispose(); miniCache.Clear(); }
    bmp = Pet3D.Render(p, size); miniCache[key] = bmp;
   }
   g.InterpolationMode = InterpolationMode.NearestNeighbor;
   g.DrawImage(bmp, new Rectangle((int)Math.Round(cx - size / 2f), (int)Math.Round(ground - size * .86f), size, size));
  }
 }
}
