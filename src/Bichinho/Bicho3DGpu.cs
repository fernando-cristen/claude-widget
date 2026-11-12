namespace MeuClaudeWidget {
 using System.Drawing.Imaging;
 using Vortice.Direct3D;
 using Vortice.Direct3D11;
 using Vortice.DXGI;
 using Vortice.D3DCompiler;
 using Vortice.Mathematics;

 // The same ray-traced mochi, on the GPU (Direct3D 11): the per-pixel loop of Pet3D.RenderCpu as a pixel shader,
 // 4x4 supersampling instead of 2x2, and the outline as a second pass. Falls back to the CPU when anything fails
 // (no GPU, remote desktop, driver reset). Rendering stays off-screen: the result is a normal Bitmap.
 public static partial class Pet3D {
  public static bool GpuEnabled = true;            // menu: "Bichinho 3D na placa de vídeo"
  public static bool GpuActive { get { return GpuEnabled && gpu != null && !gpuFailed; } }
  static Gpu gpu; static bool gpuFailed; static readonly object gpuGate = new object();
  public static void Warm() { try { if (GpuEnabled) Render(new Pose3D(), 16).Dispose(); } catch { } }
  const int MaxParts = 16;

  static Bitmap RenderGpu(Pose3D p, int size, List<Ell> parts, float scale, float ox, float oy) {
   if (!GpuEnabled || gpuFailed || parts.Count > MaxParts) return null;
   lock (gpuGate) {
    try {
     if (gpu == null) gpu = new Gpu();
     return gpu.Render(p, size, parts, scale, ox, oy);
    } catch {
     // Device lost or an unexpected failure: stop using the GPU for this run; the CPU path takes over.
     gpuFailed = true;
     try { if (gpu != null) gpu.Dispose(); } catch { }
     gpu = null;
     return null;
    }
   }
  }

  sealed class Gpu : IDisposable {
   const int Supersample = 4;
   readonly ID3D11Device device; readonly ID3D11DeviceContext context;
   readonly ID3D11VertexShader vs; readonly ID3D11PixelShader psCast, psDown, psLine;
   readonly ID3D11Buffer constants; readonly float[] data = new float[(5 + MaxParts * 6) * 4];
   sealed class Targets : IDisposable {
    public int Size; public ID3D11Texture2D Cast, Down, Line, Staging; public ID3D11RenderTargetView CastRt, DownRt, LineRt; public ID3D11ShaderResourceView CastSrv, DownSrv;
    public void Dispose() { foreach (var o in new IDisposable[] { CastSrv, DownSrv, CastRt, DownRt, LineRt, Cast, Down, Line, Staging }) if (o != null) o.Dispose(); }
   }
   readonly Dictionary<int, Targets> targets = new Dictionary<int, Targets>();

   public Gpu() {
    var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
    ID3D11Device d; ID3D11DeviceContext c;
    var result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out d, out c);
    if (result.Failure || d == null) result = D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out d, out c);
    result.CheckError();
    device = d; context = c;
    vs = device.CreateVertexShader(Compiler.Compile(VertexSource, "main", "pet-vs", "vs_4_0").Span);
    psCast = device.CreatePixelShader(Compiler.Compile(Header + CastSource, "main", "pet-cast", "ps_4_0").Span);
    psDown = device.CreatePixelShader(Compiler.Compile(Header + DownSource, "main", "pet-down", "ps_4_0").Span);
    psLine = device.CreatePixelShader(Compiler.Compile(Header + LineSource, "main", "pet-line", "ps_4_0").Span);
    constants = device.CreateBuffer(new BufferDescription((uint)(data.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
   }

   Targets For(int size) {
    Targets t;
    if (targets.TryGetValue(size, out t)) return t;
    if (targets.Count >= 6) { foreach (var old in targets.Values) old.Dispose(); targets.Clear(); }
    int big = size * Supersample;
    t = new Targets { Size = size };
    t.Cast = device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)big, (uint)big, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
    t.Down = device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)size, (uint)size, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
    t.Line = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)size, (uint)size, 1, 1, BindFlags.RenderTarget));
    t.Staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)size, (uint)size, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
    t.CastRt = device.CreateRenderTargetView(t.Cast); t.DownRt = device.CreateRenderTargetView(t.Down); t.LineRt = device.CreateRenderTargetView(t.Line);
    t.CastSrv = device.CreateShaderResourceView(t.Cast); t.DownSrv = device.CreateShaderResourceView(t.Down);
    targets[size] = t;
    return t;
   }

   public Bitmap Render(Pose3D p, int size, List<Ell> parts, float scale, float ox, float oy) {
    var t = For(size);
    // Constants: view, lights, outline, dimensions, then the ellipsoids.
    float lx = -.4f, ly = .78f, lz = .48f; Norm(ref lx, ref ly, ref lz);
    float hx = lx, hy = ly, hz = lz + 1; Norm(ref hx, ref hy, ref hz);
    float stroke = Math.Max(1f, size / 110f);
    int k = 0;
    data[k++] = ox; data[k++] = oy; data[k++] = scale; data[k++] = Supersample;
    data[k++] = lx; data[k++] = ly; data[k++] = lz; data[k++] = parts.Count;
    data[k++] = hx; data[k++] = hy; data[k++] = hz; data[k++] = stroke;
    data[k++] = p.Body.R / 255f * .38f; data[k++] = p.Body.G / 255f * .3f; data[k++] = p.Body.B / 255f * .34f; data[k++] = p.Alpha;
    data[k++] = size; data[k++] = (float)Math.Ceiling(stroke); data[k++] = 0; data[k++] = 0;
    for (int i = 0; i < MaxParts; i++) {
     if (i < parts.Count) {
      var e = parts[i];
      data[k++] = e.Cx; data[k++] = e.Cy; data[k++] = e.Cz; data[k++] = 0;
      data[k++] = e.Ix; data[k++] = e.Iy; data[k++] = e.Iz; data[k++] = 0;
      for (int r = 0; r < 3; r++) { data[k++] = e.M[r * 3]; data[k++] = e.M[r * 3 + 1]; data[k++] = e.M[r * 3 + 2]; data[k++] = 0; }
      data[k++] = e.R; data[k++] = e.G; data[k++] = e.B; data[k++] = e.Kind;
     } else k += 24;
    }
    var map = context.Map(constants, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
    try { Marshal.Copy(data, 0, map.DataPointer, data.Length); } finally { context.Unmap(constants, 0); }

    context.IASetInputLayout(null); context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    context.VSSetShader(vs); context.PSSetConstantBuffer(0, constants);
    int big = size * Supersample;
    // 1. ray cast at 4x
    context.PSSetShaderResource(0, null);
    context.OMSetRenderTargets(t.CastRt); context.RSSetViewport(0, 0, big, big);
    context.ClearRenderTargetView(t.CastRt, new Color4(0, 0, 0, 0));
    context.PSSetShader(psCast); context.Draw(3, 0);
    // 2. down-sample to the final size
    context.OMSetRenderTargets(t.DownRt); context.RSSetViewport(0, 0, size, size);
    context.PSSetShaderResource(0, t.CastSrv); context.PSSetShader(psDown); context.Draw(3, 0);
    // 3. outline, alpha and premultiply
    context.OMSetRenderTargets(t.LineRt);
    context.PSSetShaderResource(0, t.DownSrv); context.PSSetShader(psLine); context.Draw(3, 0);
    context.PSSetShaderResource(0, null); context.OMSetRenderTargets((ID3D11RenderTargetView)null);
    context.CopyResource(t.Staging, t.Line);

    var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
    var bits = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
    try {
     var read = context.Map(t.Staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
     try {
      unsafe {
       byte* src = (byte*)read.DataPointer; byte* dst = (byte*)bits.Scan0;
       for (int y = 0; y < size; y++) Buffer.MemoryCopy(src + y * read.RowPitch, dst + y * bits.Stride, size * 4, size * 4);
      }
     } finally { context.Unmap(t.Staging, 0); }
    } finally { bmp.UnlockBits(bits); }
    return bmp;
   }

   public void Dispose() {
    foreach (var t in targets.Values) t.Dispose(); targets.Clear();
    foreach (var o in new IDisposable[] { constants, psLine, psDown, psCast, vs, context, device }) if (o != null) o.Dispose();
   }

   // ---- shaders ----
   const string VertexSource = @"
float4 main(uint id : SV_VertexID) : SV_Position {
 float2 uv = float2((id << 1) & 2, id & 2);
 return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}";
   const string Header = @"
struct Ell { float4 c; float4 inv; float4 m0; float4 m1; float4 m2; float4 col; };
cbuffer Params : register(b0) {
 float4 view;    // ox, oy, scale, supersample
 float4 light;   // direction, count
 float4 half_;   // half vector, stroke
 float4 outl;    // outline color, alpha
 float4 dims;    // size, reach
 Ell ells[16];
};
Texture2D<float4> src : register(t0);
float smooth(float a, float b, float v) { float t = saturate((v - a) / (b - a)); return t * t * (3 - 2 * t); }
";
   const string CastSource = @"
float4 main(float4 pos : SV_Position) : SV_Target {
 float ss = view.w, scale = view.z;
 float x = (pos.x / ss - view.x) / scale, y = (view.y - pos.y / ss) / scale;
 float best = -1e30; int kind = -1; float3 n = 0; float3 col = 0; int count = (int)light.w;
 [loop] for (int i = 0; i < count; i++) {
  float3 d = float3(x - ells[i].c.x, y - ells[i].c.y, 50 - ells[i].c.z);
  float3 m0 = ells[i].m0.xyz, m1 = ells[i].m1.xyz, m2 = ells[i].m2.xyz, inv = ells[i].inv.xyz;
  float3 o = float3(dot(m0, d) * inv.x, dot(m1, d) * inv.y, dot(m2, d) * inv.z);
  float3 dl = float3(-m0.z * inv.x, -m1.z * inv.y, -m2.z * inv.z);
  float a = dot(dl, dl), b = 2 * dot(o, dl), c = dot(o, o) - 1;
  float disc = b * b - 4 * a * c; if (disc < 0) continue;
  float t = (-b - sqrt(disc)) / (2 * a); float z = 50 - t;
  if (z <= best) continue;
  best = z; kind = (int)ells[i].col.w; col = ells[i].col.xyz;
  float3 q = (o + t * dl) * inv;
  n = float3(m0.x * q.x + m1.x * q.y + m2.x * q.z, m0.y * q.x + m1.y * q.y + m2.y * q.z, m0.z * q.x + m1.z * q.y + m2.z * q.z);
 }
 if (kind < 0) return float4(0, 0, 0, 0);
 n = normalize(n);
 float ndl = dot(n, light.xyz), ndh = max(0, dot(n, half_.xyz));
 float3 rgb;
 if (kind == 1) { float k = .55 + .45 * max(0, ndl); rgb = col * k + float3(0, 0, .04); }
 else {
  float band = smooth(-.15, .35, ndl);
  float3 shadow = float3(col.r * .7 + .03, col.g * .62, col.b * .72 + .06);
  float3 tone = min(1, float3(col.r * 1.04 + .03, col.g * 1.04 + .03, col.b * 1.02 + .02));
  rgb = lerp(shadow, tone, band);
  float sheen = .16 * smooth(.82, .97, ndh), rim = .2 * pow(1 - max(0, n.z), 3);
  rgb += float3(sheen + rim, sheen + rim * .82, sheen + rim * .88);
  if (kind == 2) rgb += float3(.15, .12, .05);
 }
 return float4(saturate(rgb), 1);
}";
   const string DownSource = @"
float4 main(float4 pos : SV_Position) : SV_Target {
 int ss = (int)view.w; int2 p = int2(pos.xy) * ss; float4 acc = 0;
 [loop] for (int sy = 0; sy < ss; sy++) [loop] for (int sx = 0; sx < ss; sx++) { float4 s = src.Load(int3(p + int2(sx, sy), 0)); acc += float4(s.rgb * s.a, s.a); }
 return acc / (ss * ss);
}";
   const string LineSource = @"
float4 main(float4 pos : SV_Position) : SV_Target {
 int2 p = int2(pos.xy); int size = (int)dims.x, reach = (int)dims.y; float stroke = half_.w;
 float4 c = src.Load(int3(p, 0)); float a = c.a, near = 0;
 if (a < .98) {
  [loop] for (int dy = -4; dy <= 4; dy++) [loop] for (int dx = -4; dx <= 4; dx++) {
   if (abs(dx) > reach || abs(dy) > reach) continue;
   int2 q = p + int2(dx, dy); if (q.x < 0 || q.y < 0 || q.x >= size || q.y >= size) continue;
   float d = sqrt((float)(dx * dx + dy * dy)); if (d > stroke + .5) continue;
   near = max(near, src.Load(int3(q, 0)).a * min(1, stroke + .5 - d));
  }
 }
 float oa = near * (1 - a) * .9, alpha = outl.w;
 return float4(saturate((c.rgb + outl.rgb * oa) * alpha), saturate((a + oa) * alpha));
}";
  }
 }
}
