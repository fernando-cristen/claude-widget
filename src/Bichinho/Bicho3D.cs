namespace MeuClaudeWidget {
 using System;
 using System.Collections.Generic;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Drawing.Imaging;
 using System.Runtime.InteropServices;
 using System.Threading.Tasks;

 // How the 3D creature stands in one frame.
 public sealed class Pose3D {
  public float Yaw, Tilt;                  // radians: turning towards something, leaning sideways
  public float Lift, Squash = 1;           // hop height; < 1 flatter, > 1 stretched (volume kept)
  public float ArmL = .5f, ArmR = .5f;     // arm angle from hanging down (radians, outwards/up)
  public float StepL, StepR;               // feet lifted
  public float LookX, LookY;               // eyes, -1..1
  public bool EyesClosed, HappyEyes, Antenna = true; // HappyEyes: closed as "^ ^"
  public float Alpha = 1;
  public string Mouth = "smile";           // smile, cat, o, open, flat, frown
  public Color Body = Color.FromArgb(232, 160, 128);
 }

 // A small 3D renderer for a cute "mochi" creature: soft toon shading, big sparkly eyes, always-on blush and a
 // thin outline. Made of ellipsoids ray-traced on the GPU (Bicho3DGpu.cs) or, failing that, here on the CPU with
 // 2x2 supersampling. The face lines are drawn on top with GDI+ either way.
 public static partial class Pet3D {
  public static int Generation; // bumped when the renderer changes, so cached frames are redrawn
  sealed class Ell {
   public float Cx, Cy, Cz, Ix, Iy, Iz, Max; public float[] M;
   public float R, G, B; public int Kind; // 0 body/limbs (toon), 1 eye (glossy dark), 2 antenna ball (glow)
  }
  const float ViewPitch = .26f;

  public static Bitmap Render(Pose3D p, int size) {
   float scale = size / 3.0f, ox = size / 2f, oy = size * .86f;
   var parts = Build(p);
   var bmp = RenderGpu(p, size, parts, scale, ox, oy) ?? RenderCpu(p, size, parts, scale, ox, oy);
   using (var gr = Graphics.FromImage(bmp)) { gr.SmoothingMode = SmoothingMode.AntiAlias; Face(gr, p, scale, ox, oy); }
   return bmp;
  }
  static Bitmap RenderCpu(Pose3D p, int size, List<Ell> parts, float scale, float ox, float oy) {
   int ss = 2, w = size * ss, h = size * ss;
   var rgba = new float[w * h * 4];
   var boxes = new int[parts.Count * 4];
   for (int i = 0; i < parts.Count; i++) {
    var e = parts[i]; float r = e.Max * scale * ss, sx = (ox + e.Cx * scale) * ss, sy = (oy - e.Cy * scale) * ss;
    boxes[i * 4] = (int)(sx - r) - 1; boxes[i * 4 + 1] = (int)(sy - r) - 1; boxes[i * 4 + 2] = (int)(sx + r) + 1; boxes[i * 4 + 3] = (int)(sy + r) + 1;
   }
   float lx = -.4f, ly = .78f, lz = .48f; Norm(ref lx, ref ly, ref lz);
   float hx = lx, hy = ly, hz = lz + 1; Norm(ref hx, ref hy, ref hz);
   Parallel.For(0, h, py => {
    for (int px = 0; px < w; px++) {
     float x = ((px + .5f) / ss - ox) / scale, y = (oy - (py + .5f) / ss) / scale;
     float best = float.MinValue; Ell hit = null; float nx = 0, ny = 0, nz = 0;
     for (int i = 0; i < parts.Count; i++) {
      if (px < boxes[i * 4] || px > boxes[i * 4 + 2] || py < boxes[i * 4 + 1] || py > boxes[i * 4 + 3]) continue;
      var e = parts[i]; var m = e.M;
      float dx = x - e.Cx, dy = y - e.Cy, dz = 50 - e.Cz;
      float olx = (m[0] * dx + m[1] * dy + m[2] * dz) * e.Ix, oly = (m[3] * dx + m[4] * dy + m[5] * dz) * e.Iy, olz = (m[6] * dx + m[7] * dy + m[8] * dz) * e.Iz;
      float dlx = -m[2] * e.Ix, dly = -m[5] * e.Iy, dlz = -m[8] * e.Iz;
      float a = dlx * dlx + dly * dly + dlz * dlz, b = 2 * (olx * dlx + oly * dly + olz * dlz), c = olx * olx + oly * oly + olz * olz - 1;
      float disc = b * b - 4 * a * c; if (disc < 0) continue;
      float t = (-b - (float)Math.Sqrt(disc)) / (2 * a), z = 50 - t;
      if (z <= best) continue;
      best = z; hit = e;
      float qx = (olx + t * dlx) * e.Ix, qy = (oly + t * dly) * e.Iy, qz = (olz + t * dlz) * e.Iz;
      nx = m[0] * qx + m[3] * qy + m[6] * qz; ny = m[1] * qx + m[4] * qy + m[7] * qz; nz = m[2] * qx + m[5] * qy + m[8] * qz;
     }
     if (hit == null) continue;
     Norm(ref nx, ref ny, ref nz);
     float ndl = nx * lx + ny * ly + nz * lz, ndh = Math.Max(0, nx * hx + ny * hy + nz * hz);
     float r, g, bl;
     if (hit.Kind == 1) {
      // Eyes: deep and glossy.
      float k = .55f + .45f * Math.Max(0, ndl);
      r = hit.R * k; g = hit.G * k; bl = hit.B * k + .04f;
     } else {
      // Toon: a soft step between a warm shadow tone and a light tone, a broad soft sheen and a pinkish rim.
      float band = Smooth(-.15f, .35f, ndl);
      float sr = hit.R * .7f + .03f, sg = hit.G * .62f, sb = hit.B * .72f + .06f;  // shadow leans warm-violet
      float tr = Math.Min(1, hit.R * 1.04f + .03f), tg = Math.Min(1, hit.G * 1.04f + .03f), tb = Math.Min(1, hit.B * 1.02f + .02f);
      r = sr + (tr - sr) * band; g = sg + (tg - sg) * band; bl = sb + (tb - sb) * band;
      float sheen = .16f * Smooth(.82f, .97f, ndh), rim = .2f * (float)Math.Pow(1 - Math.Max(0, nz), 3);
      r += sheen + rim; g += sheen + rim * .82f; bl += sheen + rim * .88f;
      if (hit.Kind == 2) { r += .15f; g += .12f; bl += .05f; }
     }
     int o = (py * w + px) * 4; rgba[o] = Clamp(r); rgba[o + 1] = Clamp(g); rgba[o + 2] = Clamp(bl); rgba[o + 3] = 1;
    }
   });
   // Down-sample 2x2; then a thin outline around the silhouette in a deep tone of the body color.
   var color = new float[size * size * 4];
   for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
    float r = 0, g = 0, b = 0, a = 0;
    for (int sy = 0; sy < ss; sy++) for (int sx = 0; sx < ss; sx++) { int o = ((y * ss + sy) * w + x * ss + sx) * 4; float al = rgba[o + 3]; r += rgba[o] * al; g += rgba[o + 1] * al; b += rgba[o + 2] * al; a += al; }
    int k = (y * size + x) * 4; color[k] = r / 4; color[k + 1] = g / 4; color[k + 2] = b / 4; color[k + 3] = a / 4;
   }
   float orr = p.Body.R / 255f * .38f, og = p.Body.G / 255f * .3f, ob = p.Body.B / 255f * .34f;
   float stroke = Math.Max(1f, size / 110f);
   var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
   var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
   var row = new byte[size * 4]; int reach = (int)Math.Ceiling(stroke);
   for (int y = 0; y < size; y++) {
    for (int x = 0; x < size; x++) {
     int k = (y * size + x) * 4; float a = color[k + 3];
     // Outline: how much of the shape is within the stroke distance (only outside or on the soft edge).
     float near = 0;
     if (a < .98f)
      for (int dy = -reach; dy <= reach; dy++) for (int dx = -reach; dx <= reach; dx++) {
       int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= size || yy >= size) continue;
       float d = (float)Math.Sqrt(dx * dx + dy * dy); if (d > stroke + .5f) continue;
       near = Math.Max(near, color[(yy * size + xx) * 4 + 3] * Math.Min(1, stroke + .5f - d));
      }
     float oa = near * (1 - a) * .9f; // outline goes under the soft edge
     float fa = (a + oa) * p.Alpha;
     float fr = (color[k] + orr * oa) * p.Alpha, fg = (color[k + 1] + og * oa) * p.Alpha, fb = (color[k + 2] + ob * oa) * p.Alpha; // premultiplied
     row[x * 4] = (byte)(Clamp(fb) * 255); row[x * 4 + 1] = (byte)(Clamp(fg) * 255); row[x * 4 + 2] = (byte)(Clamp(fr) * 255); row[x * 4 + 3] = (byte)(Clamp(fa) * 255);
    }
    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, size * 4);
   }
   bmp.UnlockBits(data);
   return bmp;
  }

  // ---- the model: a squat mochi body, stubby arms and feet, big low-set eyes, a little antenna sprout ----
  const float BodyRx = 1.12f, BodyRy = .86f, BodyRz = .96f, BodyY = .86f;
  static List<Ell> Build(Pose3D p) {
   var list = new List<Ell>();
   float br = p.Body.R / 255f, bg = p.Body.G / 255f, bb = p.Body.B / 255f;
   float sq = p.Squash, wide = 1 / (float)Math.Sqrt(sq);
   float[] pose = Mul(Mul(RotX(ViewPitch), RotZ(p.Tilt)), RotY(p.Yaw));
   Func<float, float, float, float[]> at = (x, y, z) => Apply(pose, x, y + BodyY * sq + p.Lift, z);
   Action<float[], float[], float, float, float, float, float, float, int> add = (c, local, rx, ry, rz, r, g, b, kind) => {
    list.Add(new Ell { Cx = c[0], Cy = c[1], Cz = c[2], Ix = 1 / rx, Iy = 1 / ry, Iz = 1 / rz, Max = Math.Max(rx, Math.Max(ry, rz)), M = Mul(pose, local), R = r, G = g, B = b, Kind = kind });
   };
   float[] id = RotY(0);
   add(at(0, 0, 0), id, BodyRx * wide, BodyRy * sq, BodyRz * wide, br, bg, bb, 0);
   // Little feet peeking out under the body.
   float foot = -BodyRy * sq * .92f;
   add(at(-.42f * wide, foot + p.StepL, .42f), id, .24f, .15f, .26f, br * .93f, bg * .9f, bb * .9f, 0);
   add(at(.42f * wide, foot + p.StepR, .42f), id, .24f, .15f, .26f, br * .93f, bg * .9f, bb * .9f, 0);
   // Stubby round arms close to the body.
   foreach (int side in new[] { -1, 1 }) {
    float a = side < 0 ? p.ArmL : p.ArmR, dx = side * (float)Math.Sin(a), dy = -(float)Math.Cos(a), reach = .17f + .16f * Smooth(.8f, 2.4f, a);
    add(at(side * 1.0f * wide + dx * reach, -.12f * sq + dy * reach + .17f, .12f), RotZ(side * a), .17f, .25f, .17f, br * .96f, bg * .94f, bb * .94f, 0);
   }
   // Antenna: a thin stem and a glowing ball on top.
   if (p.Antenna) {
    add(at(0, BodyRy * sq + .14f, -.05f), id, .035f, .17f, .035f, br * .8f, bg * .75f, bb * .75f, 0);
    add(at(0, BodyRy * sq + .34f, -.05f), id, .1f, .1f, .1f, 1f, .86f, .45f, 2);
   }
   // Big, low and wide-set eyes.
   if (!p.EyesClosed && !p.HappyEyes)
    foreach (int side in new[] { -1, 1 }) {
     if (side * p.Yaw < -.4f) continue;
     float ex = side * .4f + p.LookX * .07f, ey = -.02f * sq + p.LookY * .06f;
     float ez = EyeDepth(ex / wide, ey / sq) * wide;
     add(at(ex * wide, ey, ez), id, .16f, .21f, .06f, .1f, .08f, .12f, 1);
    }
   return list;
  }
  // Depth of the body surface at a point of the face (to place eyes and face lines on it).
  static float EyeDepth(float x, float y) { float v = 1 - x * x / (BodyRx * BodyRx) - y * y / (BodyRy * BodyRy); return BodyRz * (float)Math.Sqrt(Math.Max(.05f, v)) - .01f; }

  static void Face(Graphics g, Pose3D p, float scale, float ox, float oy) {
   float sq = p.Squash, wide = 1 / (float)Math.Sqrt(sq);
   float[] pose = Mul(Mul(RotX(ViewPitch), RotZ(p.Tilt)), RotY(p.Yaw));
   Func<float, float, PointF> spot = (x, y) => { var v = Apply(pose, x * wide, y * sq + BodyY * sq + p.Lift, EyeDepth(x, y) * wide + .03f); return new PointF(ox + v[0] * scale, oy - v[1] * scale); };
   float u = scale / 10f; Color ink = Color.FromArgb(240, 52, 36, 44);
   // Blush, always: soft pink ovals under the eyes.
   foreach (int side in new[] { -1, 1 }) {
    var c = spot(side * .66f, -.22f); float r = u * 1.7f;
    using (var path = new GraphicsPath()) { path.AddEllipse(c.X - r, c.Y - r * .55f, r * 2, r * 1.1f); using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(150, 250, 120, 140), SurroundColors = new[] { Color.FromArgb(0, 250, 120, 140) } }) g.FillPath(b, path); }
   }
   using (var pen = new Pen(ink, Math.Max(1.2f, u * .5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) {
    if (p.HappyEyes)
     foreach (int side in new[] { -1, 1 }) { var c = spot(side * .4f, -.02f); g.DrawArc(pen, c.X - u * 1.2f, c.Y - u * .4f, u * 2.4f, u * 1.8f, 205, 130); }
    else if (p.EyesClosed)
     foreach (int side in new[] { -1, 1 }) { var c = spot(side * .4f, -.04f); g.DrawArc(pen, c.X - u * 1.3f, c.Y - u * 1.1f, u * 2.6f, u * 1.7f, 25, 130); }
    else
     foreach (int side in new[] { -1, 1 }) {
      if (side * p.Yaw < -.4f) continue;
      // Two sparkles: a big one up-left and a small one down-right.
      var c = spot(side * .4f + p.LookX * .07f, -.02f + p.LookY * .06f);
      float r1 = u * .62f, r2 = u * .28f;
      using (var b = new SolidBrush(Color.FromArgb(250, 255, 255, 255))) { g.FillEllipse(b, c.X - u * .75f - r1, c.Y - u * .85f - r1, r1 * 2, r1 * 2); g.FillEllipse(b, c.X + u * .55f - r2, c.Y + u * .6f - r2, r2 * 2, r2 * 2); }
     }
    var m = spot(0, -.24f);
    switch (p.Mouth) {
     case "smile": g.DrawArc(pen, m.X - u * .9f, m.Y - u * .9f, u * 1.8f, u * 1.3f, 25, 130); break;
     case "cat":
      g.DrawArc(pen, m.X - u * 1.3f, m.Y - u * .7f, u * 1.3f, u * 1.1f, 0, 160);
      g.DrawArc(pen, m.X, m.Y - u * .7f, u * 1.3f, u * 1.1f, 20, 160); break;
     case "frown": g.DrawArc(pen, m.X - u * .9f, m.Y, u * 1.8f, u * 1.2f, 205, 130); break;
     case "flat": g.DrawLine(pen, m.X - u * .6f, m.Y, m.X + u * .6f, m.Y); break;
     case "o": using (var b = new SolidBrush(ink)) g.FillEllipse(b, m.X - u * .45f, m.Y - u * .3f, u * .9f, u); break;
     case "open":
      using (var b = new SolidBrush(ink)) g.FillPie(b, m.X - u * 1f, m.Y - u * .9f, u * 2f, u * 2f, 0, 180);
      using (var b = new SolidBrush(Color.FromArgb(235, 240, 120, 130))) g.FillEllipse(b, m.X - u * .5f, m.Y + u * .3f, u, u * .6f);
      break;
    }
   }
  }

  // ---- small math ----
  static float Clamp(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
  static float Smooth(float a, float b, float v) { float t = Clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); }
  static void Norm(ref float x, ref float y, ref float z) { float l = (float)Math.Sqrt(x * x + y * y + z * z); if (l > 0) { x /= l; y /= l; z /= l; } }
  static float[] RotX(float a) { float c = (float)Math.Cos(a), s = (float)Math.Sin(a); return new[] { 1, 0, 0, 0, c, s, 0, -s, c }; }
  static float[] RotY(float a) { float c = (float)Math.Cos(a), s = (float)Math.Sin(a); return new[] { c, 0, -s, 0, 1, 0, s, 0, c }; }
  static float[] RotZ(float a) { float c = (float)Math.Cos(a), s = (float)Math.Sin(a); return new[] { c, s, 0, -s, c, 0, 0, 0, 1 }; }
  static float[] Mul(float[] a, float[] b) {
   var r = new float[9];
   for (int col = 0; col < 3; col++) for (int row = 0; row < 3; row++) r[col * 3 + row] = a[row] * b[col * 3] + a[3 + row] * b[col * 3 + 1] + a[6 + row] * b[col * 3 + 2];
   return r;
  }
  static float[] Apply(float[] m, float x, float y, float z) { return new[] { m[0] * x + m[3] * y + m[6] * z, m[1] * x + m[4] * y + m[7] * z, m[2] * x + m[5] * y + m[8] * z }; }
 }
}
