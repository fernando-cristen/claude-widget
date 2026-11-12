namespace MeuClaudeWidget {
 using NAudio.Wave;
 using NAudio.Wave.SampleProviders;

 // Sound with mixing (NAudio): chimes and the creature's little noises play over each other instead of cutting
 // each other off, and everything is synthesized on the fly, so no two chirps sound exactly the same.
 public static class Som {
  public static bool Voice = true; // menu: "Barulhinhos do bichinho"
  public static bool Ready { get { return mixer != null && !failed; } }
  const int Rate = 44100;
  static WaveOutEvent output; static MixingSampleProvider mixer; static bool failed;
  static readonly object gate = new object(); static readonly Random dice = new Random();
  static DateTime lastInput; static System.Threading.Timer idle;

  static bool Ensure() {
   lock (gate) {
    if (failed) return false;
    if (mixer != null) return true;
    try {
     mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
     output = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 };
     output.Init(mixer);
     idle = new System.Threading.Timer(Idle, null, 2000, 2000);
     return true;
    } catch { failed = true; return false; }
   }
  }
  // The device is released after a few quiet seconds and reopened by the next sound.
  static void Idle(object state) {
   lock (gate) {
    try { if (output != null && output.PlaybackState == PlaybackState.Playing && !mixer.MixerInputs.Any() && (DateTime.Now - lastInput).TotalSeconds > 4) output.Stop(); } catch { }
   }
  }
  static void Add(ISampleProvider sound) {
   lock (gate) {
    try { mixer.AddMixerInput(sound); lastInput = DateTime.Now; if (output.PlaybackState != PlaybackState.Playing) output.Play(); } catch { }
   }
  }

  // A WAV in memory (the chimes), optionally softer.
  public static bool Wav(byte[] wav, float volume = 1f) {
   if (!Ensure()) return false;
   try {
    var reader = new WaveFileReader(new MemoryStream(wav));
    ISampleProvider s = reader.ToSampleProvider();
    if (s.WaveFormat.SampleRate != Rate) s = new WdlResamplingSampleProvider(s, Rate);
    if (s.WaveFormat.Channels == 1) s = new MonoToStereoSampleProvider(s);
    Add(new VolumeSampleProvider(s) { Volume = volume });
    return true;
   } catch { return false; }
  }

  // ---- the creature's noises: short blips with a sweep, an envelope and a little random pitch ----
  struct Seg { public float F0, F1, Seconds, Gain; public Seg(float f0, float f1, float s, float g = 1) { F0 = f0; F1 = f1; Seconds = s; Gain = g; } }
  static Seg Gap(float s) { return new Seg(0, 0, s, 0); }
  public static void Chirp(string kind) {
   if (!Voice || !Ensure()) return;
   float pitch = (float)Math.Pow(2, (dice.NextDouble() - .5) * .2); // ±1 semitone
   float volume = .16f; Seg[] segs;
   switch (kind) {
    case "hop": segs = new[] { new Seg(520, 880, .09f) }; break;
    case "giggle": segs = new[] { new Seg(700, 900, .06f), Gap(.03f), new Seg(760, 980, .06f), Gap(.03f), new Seg(820, 1080, .07f) }; break;
    case "grumpy": segs = new[] { new Seg(420, 300, .14f), Gap(.05f), new Seg(380, 250, .18f) }; volume = .14f; break;
    case "land": segs = new[] { new Seg(170, 90, .08f, 1.3f) }; volume = .2f; break;
    case "yawn": segs = new[] { new Seg(520, 330, .55f, .7f) }; volume = .1f; break;
    case "whistle": segs = new[] { new Seg(1200, 1700, .18f), Gap(.05f), new Seg(1700, 1100, .22f) }; volume = .09f; break;
    case "dizzy": segs = new[] { new Seg(720, 330, .45f) }; volume = .12f; break;
    case "catch": segs = new[] { new Seg(900, 1300, .06f), Gap(.02f), new Seg(1300, 1500, .07f) }; break;
    case "talk": {
     // Two to four mumbled syllables, like a tiny voice.
     int count = 2 + dice.Next(3); var list = new List<Seg>();
     for (int i = 0; i < count; i++) { float f = 480 + dice.Next(260); list.Add(new Seg(f, f + (dice.Next(2) == 0 ? 90 : -70), .05f + dice.Next(3) * .01f)); list.Add(Gap(.035f)); }
     segs = list.ToArray(); volume = .08f; break;
    }
    default: return;
   }
   Add(new Blip(segs, pitch, volume));
  }

  // One voice: sine with a soft second harmonic, exponential sweep per segment, 4 ms attack and a short release.
  sealed class Blip : ISampleProvider {
   readonly Seg[] segs; readonly float pitch, volume; int seg; int pos; double phase; readonly float release = .03f;
   public Blip(Seg[] segs, float pitch, float volume) { this.segs = segs; this.pitch = pitch; this.volume = volume; }
   public WaveFormat WaveFormat { get { return WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2); } }
   public int Read(float[] buffer, int offset, int count) {
    int frames = count / 2, written = 0;
    while (written < frames && seg < segs.Length) {
     var s = segs[seg]; int length = (int)(s.Seconds * Rate);
     if (pos >= length) { seg++; pos = 0; continue; }
     float value = 0;
     if (s.Gain > 0) {
      double t = pos / (double)Rate, u = pos / (double)length;
      double f = s.F0 * Math.Pow(s.F1 / s.F0, u) * pitch;
      phase += 2 * Math.PI * f / Rate;
      double env = (1 - Math.Exp(-t / .004)) * (u > 1 - release / s.Seconds ? (1 - u) / (release / s.Seconds) : 1);
      double w = Math.Sin(phase) + .28 * Math.Sin(2 * phase) + .08 * Math.Sin(3 * phase);
      value = (float)(Math.Tanh(w * 1.2) * env * s.Gain * volume);
     }
     buffer[offset + written * 2] = value; buffer[offset + written * 2 + 1] = value;
     written++; pos++;
    }
    return written * 2;
   }
  }
 }
}
