param([string]$RenderFolder)
# Local synthetic data only. No application settings, account or Redmine requests are used.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$src = Join-Path (Split-Path $PSScriptRoot -Parent) 'src'
$files = @(Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' | Sort-Object { if ($_.Name -eq 'Widget.cs') { '' } else { $_.FullName } })
$source = ($files | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join [Environment]::NewLine
# Allow the animation renderer in preview instances; all preview guards for I/O stay intact.
$source = $source.Replace('settings.Animations && settings.Compact && !preview && !stopped && Visible', 'settings.Animations && settings.Compact && !stopped && Visible')
$source += @'

namespace MeuClaudeWidget {
 public partial class Widget {
  static void CheckMotion(bool ok,string message) { if(!ok) throw new System.Exception(message); }
  public static string TestTransitions(string folder) {
   var value=new UsageMotion(); value.Set(20,0,true); CheckMotion(value.Value(0)==20,"First read must not count from zero");
   value.Set(80,10,true); double middle=value.Value(100).Value;
   CheckMotion(middle>20 && middle<80,"Usage must interpolate");
   value.Set(40,100,true); CheckMotion(value.Value(100)==middle,"Retarget must start at the displayed value");
   CheckMotion(value.Value(600)==40 && !value.Active(600),"Usage must finish exactly");
   value.Set(null,700,true); CheckMotion(!value.Value(700).HasValue && !value.Active(700),"Unknown must not become zero");
   value.Set(90,800,false); CheckMotion(value.Value(800)==90 && !value.Active(800),"Disabled must be immediate");
   var bounds=new BoundsMotion();
   bounds.Set(new System.Drawing.Rectangle(100,10,664,40),new System.Drawing.Rectangle(224,10,540,40),System.Drawing.Rectangle.Empty,0,true);
   for(int t=0;t<=240;t+=20) CheckMotion(bounds.Value(t).Right==764,"Free strip must keep its right edge");
   var gap=new System.Drawing.Rectangle(300,10,200,40);
   bounds.Set(new System.Drawing.Rectangle(100,10,664,40),gap,gap,300,true);
   for(int t=300;t<=540;t+=20) CheckMotion(gap.Contains(bounds.Value(t)),"Strip must not cover new taskbar icons");
   var target=bounds.Value(400); bounds.Set(target,gap,gap,400,true);
   CheckMotion(!bounds.Active(550),"An identical target must not restart the transition");
   using(var widget=new Widget(true)) {
    widget.Location=new System.Drawing.Point(-20000,-20000); widget.Show(); widget.Demo(true);
    widget.settings.DockTaskbar=false;
    widget.FinishStripMotion(); widget.StopBoundsMotion();
    using(var first=widget.MotionFrame()) { }
    int right=widget.Right;
    var savedTasks=widget.tasks; widget.tasks=new System.Collections.Generic.List<ActiveIssue>(); widget.UpdateTaskClock();
    int index=0;
    foreach(int delay in new[]{0,35,45,80,110}) {
     System.Threading.Thread.Sleep(delay); widget.AnimationTick();
     using(var frame=widget.MotionFrame()) {
      if(!System.String.IsNullOrEmpty(folder)) frame.Save(System.IO.Path.Combine(folder,"task-out-"+index+".png"),System.Drawing.Imaging.ImageFormat.Png);
      using(var direct=new System.Drawing.Bitmap(widget.Width,widget.Height,System.Drawing.Imaging.PixelFormat.Format32bppRgb)) {
       using(var graphics=System.Drawing.Graphics.FromImage(direct)) { graphics.Clear(widget.BackColor); graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias; widget.PaintCompact(graphics); }
       for(int y=0;y<frame.Height;y++) for(int x=0;x<widget.Px(250);x++) CheckMotion(frame.GetPixel(x,y).ToArgb()==direct.GetPixel(x,y).ToArgb(),"Usage text mismatch during motion at "+x+","+y+": "+frame.GetPixel(x,y)+" vs "+direct.GetPixel(x,y));
      }
     }
     CheckMotion(widget.Right==right,"Animated widget moved its right edge"); index++;
     if(index==2) CheckMotion(widget.Width>widget.Px(540) && widget.Width<widget.Px(664),"Task exit must animate the window width");
    }
    CheckMotion(widget.Width==widget.Px(540),"Task exit did not retract");
    widget.tasks=savedTasks; widget.UpdateTaskClock();
    using(var first=widget.MotionFrame()) { }
    System.Threading.Thread.Sleep(270); widget.AnimationTick();
    using(var done=widget.MotionFrame()) { }
    CheckMotion(widget.Width==widget.Px(664),"Task entry did not expand");
    foreach(int width in new[]{280,220,180,664}) {
     var available=new System.Drawing.Rectangle(right-widget.Px(width),widget.Top,widget.Px(width),widget.Px(40));
     widget.SetCompactBounds(available,available);
     using(var first=widget.MotionFrame()) { }
     System.Threading.Thread.Sleep(270); widget.AnimationTick();
     using(var frame=widget.MotionFrame()) {
      CheckMotion(widget.Width==widget.Px(width),"Dense layout width is incorrect");
      if(!System.String.IsNullOrEmpty(folder)) frame.Save(System.IO.Path.Combine(folder,"width-"+width+".png"),System.Drawing.Imaging.ImageFormat.Png);
     }
    }
    widget.activeSession=widget.sessions[1];
    using(var first=widget.MotionFrame()) { }
    CheckMotion(widget.stripMotion.Active(MotionTime.Now),"Project switch did not animate");
    System.Threading.Thread.Sleep(270); widget.AnimationTick();
    using(var actual=widget.MotionFrame()) using(var expected=new System.Drawing.Bitmap(widget.Width,widget.Height,System.Drawing.Imaging.PixelFormat.Format32bppRgb)) {
     using(var graphics=System.Drawing.Graphics.FromImage(expected)) { graphics.Clear(widget.BackColor); graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias; widget.PaintCompact(graphics); }
     for(int y=0;y<actual.Height;y++) for(int x=0;x<actual.Width;x++) CheckMotion(actual.GetPixel(x,y).ToArgb()==expected.GetPixel(x,y).ToArgb(),"Settled visual differs from original drawing");
     if(!System.String.IsNullOrEmpty(folder)) actual.Save(System.IO.Path.Combine(folder,"settled.png"),System.Drawing.Imaging.ImageFormat.Png);
    }
    widget.AnimationTick();
    CheckMotion(!widget.ambientTimer.Enabled,"Idle motion must stop its timer");
    foreach(float dpi in new[]{96f,120f,144f,192f}) {
     using(var actual=widget.MotionFrame(dpi)) using(var expected=new System.Drawing.Bitmap(widget.Width,widget.Height,System.Drawing.Imaging.PixelFormat.Format32bppRgb)) {
      expected.SetResolution(dpi,dpi);
      using(var graphics=System.Drawing.Graphics.FromImage(expected)) { graphics.Clear(widget.BackColor); graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias; widget.PaintCompact(graphics); }
      for(int y=0;y<actual.Height;y++) for(int x=0;x<actual.Width;x++) CheckMotion(actual.GetPixel(x,y).ToArgb()==expected.GetPixel(x,y).ToArgb(),"Native text mismatch at DPI "+dpi);
     }
    }
    widget.settings.Animations=false; widget.FinishStripMotion(); widget.AnimationTick();
    CheckMotion(!widget.ambientTimer.Enabled && !widget.ContentMoving(MotionTime.Now),"Disabled animation must stop its timer");
    widget.Demo(false); widget.Demo(true); widget.AnimationTick();
    CheckMotion(widget.Width==widget.FullWidth,"Expanded/compact switch broke layout");
   }
   return "OK: usage/unknown/retarget, task entry and exit, animated width and fixed right edge, taskbar space, dense layouts, project switch, native text during motion and at 96/120/144/192 DPI, disabling and mode switch.";
  }
  System.Drawing.Bitmap MotionFrame(float dpi=96) {
   var frame=new System.Drawing.Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppRgb);
   frame.SetResolution(dpi,dpi);
   using(var graphics=System.Drawing.Graphics.FromImage(frame)) { graphics.Clear(BackColor); graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias; PaintMovingCompact(graphics); }
   return frame;
  }
 }
}
'@
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll')
foreach ($name in @('UIAutomationClient','UIAutomationTypes','WindowsBase')) { $references += [Reflection.Assembly]::Load($name + ', Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35').Location }
Add-Type -TypeDefinition $source -Language CSharp -ReferencedAssemblies $references
if ($RenderFolder) { [IO.Directory]::CreateDirectory($RenderFolder) | Out-Null }
[MeuClaudeWidget.Program]::SelfTest()
[MeuClaudeWidget.Widget]::TestTransitions($RenderFolder)
