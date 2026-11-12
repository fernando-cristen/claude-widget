namespace MeuClaudeWidget {
 using System;
 using System.Drawing;
 using System.Drawing.Drawing2D;
 using System.Drawing.Imaging;
 using System.Windows.Forms;

 // Monotonic time avoids jumps when the Windows clock is corrected.
 public static class MotionTime {
  public static double Now { get { return System.Diagnostics.Stopwatch.GetTimestamp()*1000.0/System.Diagnostics.Stopwatch.Frequency; } }
  public static double Ease(double progress) { double t=Math.Max(0,Math.Min(1,progress)); return 1-Math.Pow(1-t,3); }
  public static int Mix(int a,int b,double t) { return (int)Math.Round(a+(b-a)*t); }
  public static Rectangle Mix(Rectangle a,Rectangle b,double t) { return new Rectangle(Mix(a.X,b.X,t),Mix(a.Y,b.Y,t),Math.Max(0,Mix(a.Width,b.Width,t)),Math.Max(0,Mix(a.Height,b.Height,t))); }
 }
 public sealed class UsageMotion {
  double? from,target; double started;
  public double? Value(double now) {
   if(!from.HasValue || !target.HasValue) return target;
   return from.Value+(target.Value-from.Value)*MotionTime.Ease((now-started)/420);
  }
  public void Set(double? value,double now,bool animate) {
   if(value==target) { if(!animate) Finish(); return; }
   from=animate?Value(now):value; target=value; started=now;
   if(!from.HasValue || !target.HasValue) from=target;
  }
  public bool Active(double now) { return from.HasValue && target.HasValue && from!=target && now-started<420; }
  public void Finish() { from=target; }
 }
 public sealed class BoundsMotion {
  public Rectangle Target { get; private set; }
  Rectangle from,allowed; double started; bool moving;
  public bool Active(double now) { return moving && now-started<240; }
  public void Set(Rectangle current,Rectangle target,Rectangle limit,double now,bool animate) {
   if(moving && target==Target && limit==allowed) return;
   from=current; Target=target; allowed=limit; started=now; moving=animate && current!=target;
  }
  public Rectangle Value(double now) {
   var r=Active(now)?MotionTime.Mix(from,Target,MotionTime.Ease((now-started)/240)):Target;
   // Never animate over newly occupied taskbar space. Only content may continue moving there.
   if(!allowed.IsEmpty) { r.Width=Math.Min(r.Width,allowed.Width); r.X=Math.Max(allowed.Left,Math.Min(r.X,allowed.Right-r.Width)); }
   return r;
  }
  public void Finish() { moving=false; }
  public void Reset(Rectangle bounds) { Target=from=bounds; allowed=Rectangle.Empty; moving=false; }
 }
 public struct StripLayout {
  public Rectangle Usage,Task,Radar;
  public static StripLayout Mix(StripLayout a,StripLayout b,double t) {
   return new StripLayout { Usage=MotionTime.Mix(a.Usage,b.Usage,t),Task=MotionTime.Mix(a.Task,b.Task,t),Radar=MotionTime.Mix(a.Radar,b.Radar,t) };
  }
 }
 // Only geometry is animated. Visible text is always drawn on the destination DC.
 public sealed class StripMotion : IDisposable {
  StripLayout before; string key,project; double started; bool moving,projectChanged;
  public bool HasFrame { get; private set; }
  public StripLayout Layout { get; private set; }
  public bool Active(double now) { return moving && now-started<240; }
  public int ProjectOffset(double now,int distance) { return Active(now) && projectChanged?(int)Math.Round(distance*(1-MotionTime.Ease((now-started)/240))):0; }
  public void Update(StripLayout target,string nextKey,string nextProject,double now,bool animate) {
   if(key!=nextKey) {
    before=Layout; started=now; moving=animate && HasFrame;
    projectChanged=project!=nextProject; project=nextProject; key=nextKey;
   }
   if(!animate) Finish();
   Layout=Active(now)?StripLayout.Mix(before,target,MotionTime.Ease((now-started)/240)):target;
   HasFrame=true;
  }
  public void Finish() { moving=false; }
  public void Reset() { Finish(); HasFrame=false; key=project=null; }
  public void Dispose() { Reset(); }
 }
 public partial class Widget {
  readonly UsageMotion[] usageMotion={new UsageMotion(),new UsageMotion()};
  readonly BoundsMotion boundsMotion=new BoundsMotion();
  readonly StripMotion stripMotion=new StripMotion();
  Bitmap retiringTask; string taskImageKey; int stripPaintWidth; bool compactBoundsReady;
  bool SmoothStrip { get { return settings.Animations && settings.Compact && (!preview || TestMotion) && !stopped && Visible; } }
  internal static bool TestMotion; // ligada apenas pelo teste de transições
  int StripWidth { get { return stripPaintWidth>0?stripPaintWidth:Width; } }
  int CompactWidth { get { return compactBoundsReady && boundsMotion.Active(MotionTime.Now)?boundsMotion.Target.Width:Width; } }
  double? VisualUsage(int index,Bucket bucket) { return stripPaintWidth>0?usageMotion[index].Value(MotionTime.Now):bucket.Percent; }
  void StartMotionTimer() { if(SmoothStrip && !ambientTimer.Enabled) ambientTimer.Start(); }
  bool ContentMoving(double now) { return boundsMotion.Active(now)||stripMotion.Active(now)||usageMotion[0].Active(now)||usageMotion[1].Active(now); }
  void AnimationTick() {
   double now=MotionTime.Now;
   if(stopped || !settings.Animations || !settings.Compact) { FinishStripMotion(); ambientTimer.Stop(); return; }
   if(compactBoundsReady) ApplyCompactBounds(boundsMotion.Value(now));
   bool effects=ambient.Transient(DateTime.Now) || ContentMoving(now) || (DateTime.Now-wakeAt).TotalSeconds<1.6 || CreatureFx;
   bool steady=LiveDecor || ambient.Continuous;
   if(!effects && !steady) ambientTimer.Stop();
   // Steady motion only (liquid, creature, comet): 15 fps is plenty; short events keep 30 fps.
   Invalidate();
  }
  void SetCompactBounds(Rectangle target,Rectangle allowed) {
   double now=MotionTime.Now;
   bool animate=SmoothStrip && (compactBoundsReady || stripMotion.HasFrame) && Bounds.IntersectsWith(target) && !dragging && !menu.Visible;
   boundsMotion.Set(Bounds,target,allowed,now,animate); compactBoundsReady=true;
   ApplyCompactBounds(boundsMotion.Value(now));
   if(boundsMotion.Active(now)) StartMotionTimer();
  }
  void ApplyCompactBounds(Rectangle bounds) {
   if(!settings.Compact || bounds.Width<=0 || bounds.Height<=0 || Bounds==bounds) return;
   bool resize=Size!=bounds.Size;
   placing=true;
   try { if(Docked) dockPlace=bounds.Location; Bounds=bounds; }
   finally { placing=false; }
   if(resize) UpdateCompactRegion();
  }
  void UpdateCompactRegion() {
   var previous=Region;
   using(var shape=Rounded(new RectangleF(0,0,Width,Height),Px(8))) Region=new Region(shape);
   if(previous!=null) previous.Dispose();
  }
  void FinishStripMotion() {
   if(compactBoundsReady && settings.Compact) { boundsMotion.Finish(); ApplyCompactBounds(boundsMotion.Value(MotionTime.Now)); }
   stripMotion.Reset(); foreach(var value in usageMotion) value.Finish();
   Invalidate();
  }
  void StopBoundsMotion() { boundsMotion.Reset(Bounds); compactBoundsReady=false; }
  StripLayout CurrentStripLayout() {
   if(Dense) {
    Rectangle usage,task,radar; DenseColumns(out usage,out task,out radar);
    int divider=usage.Right+Px(4), taskEnd=task.IsEmpty?divider:radar.X-Px(4);
    return new StripLayout { Usage=new Rectangle(0,0,divider,Height),Task=new Rectangle(divider,0,taskEnd-divider,Height),Radar=new Rectangle(taskEnd,0,Math.Max(0,StripWidth-taskEnd),Height) };
   }
   return new StripLayout { Usage=new Rectangle(0,0,Px(254),Height),Task=new Rectangle(Px(254),0,Lead,Height),Radar=new Rectangle(Px(254)+Lead,0,Math.Max(0,StripWidth-Px(254)-Lead),Height) };
  }
  StripLayout HitLayout {
   get { return stripMotion.Active(MotionTime.Now)?stripMotion.Layout:CurrentStripLayout(); }
  }
  void StripText(Graphics g,string text,Font font,Rectangle area,Color color,TextFormatFlags flags) {
   TextRenderer.DrawText(g,text,font,area,color,flags|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform);
  }
  void PaintNativeBlock(Graphics g,Rectangle source,Rectangle destination,int dy) {
   if(source.Width<=0 || destination.Width<=0 || destination.Height<=0) return;
   var saved=g.Save();
   try {
    g.SetClip(destination,CombineMode.Intersect);
    using(var brush=new SolidBrush(BackColor)) g.FillRectangle(brush,destination);
    g.TranslateTransform(destination.X-source.X,destination.Y-source.Y+dy);
    PaintCompact(g);
   } finally { g.Restore(saved); }
  }
  void RememberTaskImage(Graphics screen,Rectangle task) {
   var current=taskShown?CurrentTask:null;
   if(current==null || task.Width<=0) return;
   double total; taskTotal.TryGetValue(current.Id,out total);
   string cacheKey=current.Id+"/"+WorkDay.Duration(total)+"/"+task+"/"+DateTime.Now.ToString("yyyyMMddHHmm")+"/"+screen.DpiX+"/"+screen.DpiY;
   if(cacheKey==taskImageKey) return;
   if(retiringTask!=null) retiringTask.Dispose();
   retiringTask=new Bitmap(task.Width,task.Height,PixelFormat.Format32bppRgb);
   retiringTask.SetResolution(screen.DpiX,screen.DpiY);
   using(var image=Graphics.FromImage(retiringTask)) {
    image.Clear(BackColor); image.SmoothingMode=SmoothingMode.AntiAlias;
    PaintNativeBlock(image,task,new Rectangle(Point.Empty,task.Size),0);
   }
   taskImageKey=cacheKey;
  }
  void ForgetTaskImage() { if(retiringTask!=null) { retiringTask.Dispose(); retiringTask=null; } taskImageKey=null; }
  void PaintMovingCompact(Graphics g) {
   double now=MotionTime.Now;
   for(int i=0;i<2;i++) usageMotion[i].Set(buckets.Count>i?buckets[i].Percent:null,now,SmoothStrip);
   if(!SmoothStrip) { stripMotion.Reset(); ForgetTaskImage(); PaintCompact(g); return; }
   int width=compactBoundsReady?boundsMotion.Target.Width:Width;
   stripPaintWidth=width;
   try {
    var target=CurrentStripLayout();
    string project=activeSession==null?"":activeSession.Id+":"+activeSession.Effective;
    string key=width+"/"+Height+"/"+DeviceDpi+"/"+project+"/"+(taskShown && CurrentTask!=null?CurrentTask.Id.ToString():"");
    stripMotion.Update(target,key,project,now,true);
    if(!stripMotion.Active(now)) PaintCompact(g);
    else {
     var displayed=stripMotion.Layout;
     PaintNativeBlock(g,target.Usage,displayed.Usage,0);
     if(target.Task.Width>0) PaintNativeBlock(g,target.Task,displayed.Task,0);
     else if(retiringTask!=null && displayed.Task.Width>0) {
      // Only the disappearing task is retained, at its original DPI and exact pixel size.
      var saved=g.Save();
      try { g.SetClip(displayed.Task,CombineMode.Intersect); g.DrawImageUnscaled(retiringTask,displayed.Task.Location); }
      finally { g.Restore(saved); }
     }
     PaintNativeBlock(g,target.Radar,displayed.Radar,stripMotion.ProjectOffset(now,Px(4)));
    }
    if(target.Task.Width>0) RememberTaskImage(g,target.Task);
    else if(!stripMotion.Active(now)) ForgetTaskImage();
   } finally { stripPaintWidth=0; }
   if(ContentMoving(now)) StartMotionTimer();
  }
  void DisposeStripMotion() { stripMotion.Dispose(); ForgetTaskImage(); }
 }
}
