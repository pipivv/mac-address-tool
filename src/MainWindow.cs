using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MacTool {
 public sealed class UiController {
  public readonly Window Window;
  readonly bool demo;
  MacDatabase database;
  Generator generator;
  List<Adapter> adapters=new List<Adapter>();
  List<string> matches=new List<string>(),visible=new List<string>();
  readonly HashSet<string> used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  readonly DispatcherTimer searchTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(220)};
  int searchVersion,page,pageSize=3,fontIndex=1;
  bool binding,searching,networkLoading,busy,dark,reducedMotion=!SystemParameters.ClientAreaAnimation;
  string selectedVendor="Intel Corporate",appliedQuery="";
  TaskCompletionSource<bool> sheetCompletion;
  Task initialization=Task.FromResult(0);
  public Task Initialization {get{return initialization;}}
  public int CommonCount {get;private set;}
  public int MaximumVisibleCount {get;private set;}
  public bool ReducedMotion {get{return reducedMotion;}}
  public long DatabaseMilliseconds {get;private set;}
  public long AdapterMilliseconds {get;private set;}
  T C<T>(string name) where T:class {return Window.FindName(name) as T;}
  public UiController(bool demo){
   this.demo=demo;
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);
   var assembly=Assembly.GetExecutingAssembly();
   var author=assembly.GetCustomAttribute<AssemblyCompanyAttribute>().Company;
   var release=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
   C<TextBlock>("ReleaseCredit").Text=author+" · v"+release;
   using(var iconStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")){
    var icon=new IconBitmapDecoder(iconStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
    Window.Icon=icon.Frames.OrderByDescending(frame=>frame.PixelWidth).First();
   }
   if(demo)Window.Title="MAC 地址工具 · 示例";
   var area=SystemParameters.WorkArea;Window.Width=Math.Min(1180,area.Width*.94);Window.Height=Math.Min(690,area.Height*.92);Window.MinWidth=Math.Min(860,area.Width*.9);Window.MinHeight=Math.Min(580,area.Height*.88);Window.WindowStartupLocation=WindowStartupLocation.CenterScreen;
   Window.SizeChanged+=(s,e)=>FitPage();
   Window.Loaded+=async(s,e)=>{SetFontSize(1);FitPage();WireMotion();initialization=Initialize();await initialization;};
   Window.Closing+=(s,e)=>{if(busy)e.Cancel=true;};
   Window.Closed+=(s,e)=>{searchVersion++;searchTimer.Stop();if(generator!=null)generator.Dispose();};
   C<Grid>("Header").MouseLeftButtonDown+=(s,e)=>{if(Ancestor<ButtonBase>(e.OriginalSource as DependencyObject)!=null)return;if(e.ClickCount==2)ToggleMaximize();else if(e.ButtonState==MouseButtonState.Pressed)Window.DragMove();};
   C<Button>("Minimize").Click+=(s,e)=>SystemCommands.MinimizeWindow(Window);
   C<Button>("Maximize").Click+=(s,e)=>ToggleMaximize();C<Button>("Close").Click+=(s,e)=>Window.Close();
   C<Button>("Theme").Click+=(s,e)=>SetTheme(!dark);C<Button>("Motion").Click+=(s,e)=>{reducedMotion=!reducedMotion;UpdateMotionLabel();Toast(reducedMotion?"已减少动态效果":"已开启动效");ApplyPopupMotion();};
   Window.StateChanged+=(s,e)=>C<System.Windows.Shapes.Path>("MaximizeIcon").Data=Geometry.Parse(Window.WindowState==WindowState.Maximized?"M 3,1 L 11,1 11,9 M 1,3 L 9,3 9,11 1,11 Z":"M 1,1 L 11,1 11,11 1,11 Z");
   for(int i=0;i<3;i++){int index=i;C<RadioButton>("Size"+i).Checked+=(s,e)=>SetFontSize(index);}
   C<Button>("Refresh").Click+=async(s,e)=>await ReadAdapters();
   C<CheckBox>("Virtuals").Checked+=(s,e)=>{AnimateSwitch(true);BindAdapters();};C<CheckBox>("Virtuals").Unchecked+=(s,e)=>{AnimateSwitch(false);BindAdapters();};
   C<ComboBox>("Adapters").SelectionChanged+=(s,e)=>{if(!binding)ShowAdapter();};
   foreach(var name in new[]{"ChooseVendor","RandomVendor","LocalMode"}){
    C<RadioButton>(name).Checked+=async(s,e)=>{
     AnimateSegment("VendorThumb",IsLocal?196:IsRandom?98:0);
     RefreshControls();
     if(!IsLocal && (searching || C<TextBox>("Search").Text.Trim()!=appliedQuery)){
      searchTimer.Stop();await Search(true);
     }else Generate();
    };
   }
   C<TextBox>("Search").TextChanged+=(s,e)=>{searchVersion++;C<TextBlock>("SearchHint").Visibility=string.IsNullOrEmpty(C<TextBox>("Search").Text)?Visibility.Visible:Visibility.Collapsed;searchTimer.Stop();searchTimer.Start();RefreshControls();};
   searchTimer.Tick+=async(s,e)=>{searchTimer.Stop();await Search(false);};
   C<ListBox>("Vendors").SelectionChanged+=(s,e)=>{if(binding)return;selectedVendor=C<ListBox>("Vendors").SelectedItem as string;if(database!=null&&!IsRandom)Generate();};
   C<Button>("Previous").Click+=(s,e)=>ChangePage(-1);C<Button>("Next").Click+=(s,e)=>ChangePage(1);
   C<Grid>("MainContent").SizeChanged+=(s,e)=>UpdatePageSize();
   C<Button>("Generate").Click+=async(s,e)=>{searchTimer.Stop();if(!IsLocal&&(C<TextBox>("Search").Text.Trim()!=appliedQuery||searching))await Search(true);else Generate();};
   C<TextBox>("Address").TextChanged+=(s,e)=>ShowMatch();
   C<Button>("Copy").Click+=(s,e)=>{try{Clipboard.SetText(Mac.Format(C<TextBox>("Address").Text));Toast("地址已复制");}catch(Exception ex){Error("无法复制地址",ex.Message);}};
   C<Button>("Apply").Click+=async(s,e)=>await Change("set");C<Button>("Reset").Click+=async(s,e)=>await Change("reset");C<Button>("Undo").Click+=async(s,e)=>await Change("undo");
   C<Button>("SheetConfirm").Click+=(s,e)=>CompleteSheet(true);C<Button>("SheetCancel").Click+=(s,e)=>CompleteSheet(false);
   Window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape&&sheetCompletion!=null&&!busy){CompleteSheet(false);e.Handled=true;}};
   RefreshControls();
  }
  bool IsLocal {get{return C<RadioButton>("LocalMode").IsChecked==true;}}
  bool IsRandom {get{return C<RadioButton>("RandomVendor").IsChecked==true;}}
  Adapter SelectedAdapter {get{return C<ComboBox>("Adapters").SelectedItem as Adapter;}}
  static T Ancestor<T>(DependencyObject source) where T:DependencyObject {while(source!=null){var match=source as T;if(match!=null)return match;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);}return null;}
  void ToggleMaximize(){Window.WindowState=Window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
  public void FitPage(){
   var viewport=C<Grid>("Viewport");var design=C<Grid>("Design");if(viewport==null||viewport.ActualWidth<1||viewport.ActualHeight<1)return;
   double scale=Math.Min(1.25,Math.Min(viewport.ActualWidth/1100,viewport.ActualHeight/680));scale=Math.Max(.3,scale);
   C<ScaleTransform>("PageScale").ScaleX=scale;C<ScaleTransform>("PageScale").ScaleY=scale;
   design.Width=viewport.ActualWidth/scale;design.Height=viewport.ActualHeight/scale;
  }
  async Task Initialize(){
   var data=LoadDatabase();var network=ReadAdapters();await Task.WhenAll(data,network);if(database!=null)Generate();
  }
  async Task LoadDatabase(){var timer=Stopwatch.StartNew();try{database=await Task.Run(()=>new MacDatabase());generator=new Generator(database);var query=database.Query("",selectedVendor);ApplyQuery(query);CommonCount=visible.Count;}catch(Exception ex){Error("厂商库加载失败",ex.Message);}finally{DatabaseMilliseconds=timer.ElapsedMilliseconds;RefreshControls();}}
  async Task ReadAdapters(){
   if(networkLoading||busy)return;networkLoading=true;RefreshControls();var timer=Stopwatch.StartNew();
   try{
    string old=SelectedAdapter==null?null:SelectedAdapter.Id;
    if(demo)adapters=new List<Adapter>{new Adapter{Id=Guid.Empty.ToString("D"),Name="示例以太网",Description="Demo Ethernet Adapter",Current="020000000001",Permanent="020000000001",Physical=true,Status="Up"}};
    else{string note;adapters=await Task.Run(()=>NetworkOps.Read(out note));}
    BindAdapters(old);
   }catch(Exception ex){C<TextBlock>("AdapterState").Text="读取失败，请刷新";Error("无法读取网卡",ex.Message);}finally{networkLoading=false;AdapterMilliseconds=timer.ElapsedMilliseconds;RefreshControls();}
  }
  void BindAdapters(string previous=null){
   if(previous==null&&SelectedAdapter!=null)previous=SelectedAdapter.Id;
   var list=adapters.Where(a=>C<CheckBox>("Virtuals").IsChecked==true||a.Physical).ToList();
   binding=true;C<ComboBox>("Adapters").ItemsSource=list;C<ComboBox>("Adapters").SelectedItem=list.FirstOrDefault(a=>a.Id==previous)??list.FirstOrDefault(a=>a.Status=="Up")??list.FirstOrDefault();binding=false;ShowAdapter();
  }
  void ShowAdapter(){var a=SelectedAdapter;C<TextBlock>("PermanentLabel").Text=a!=null && a.Physical?"出厂默认地址":"永久 MAC 地址";C<TextBlock>("CurrentMac").Text=a==null?"—":Mac.Format(a.Current);C<TextBlock>("PermanentMac").Text=a==null?"—":Mac.Format(a.Permanent);C<TextBlock>("AdapterState").Text=a==null?(networkLoading?"正在读取网卡…":"未找到网卡"):(a.Wireless?"无线网卡":"以太网")+" · "+(a.Status=="Up"?"已连接":a.Disabled?"已禁用":"未连接");RefreshControls();}
  async Task Search(bool generateAfter){
   if(database==null)return;int version=++searchVersion;string query=C<TextBox>("Search").Text.Trim(),selected=selectedVendor;searching=true;RefreshControls();
   try{var result=await Task.Run(()=>database.Query(query,selected));if(version!=searchVersion)return;searching=false;ApplyQuery(result);if(generateAfter)Generate();}
   catch(Exception ex){if(version==searchVersion){searching=false;Error("搜索失败",ex.Message);}}finally{RefreshControls();}
  }
  void ApplyQuery(VendorQuery query){matches=query.Matches;visible=query.Visible;appliedQuery=query.Text;MaximumVisibleCount=Math.Max(MaximumVisibleCount,visible.Count);page=0;BindPage(true);RefreshControls();}
  void UpdatePageSize(){
   // Reserve space for card padding, search, pager and address controls.
   double available=C<Grid>("MainContent").ActualHeight-390;
   double rowHeight=Math.Ceiling(FontSize*1.5)+8;
   Window.Resources["VendorRowHeight"]=rowHeight;
   int size=Math.Max(1,Math.Min(12,(int)(available/(rowHeight+2))));
   if(size==pageSize)return;
   pageSize=size;
   BindPage(true);
  }
  void BindPage(bool preserve){
   int index=visible.FindIndex(s=>s.Equals(selectedVendor,StringComparison.OrdinalIgnoreCase));if(preserve&&index>=0)page=index/Math.Max(1,pageSize);
   int pages=Math.Max(1,(visible.Count+pageSize-1)/pageSize);page=Math.Max(0,Math.Min(page,pages-1));var rows=visible.Skip(page*pageSize).Take(pageSize).ToList();
   binding=true;C<ListBox>("Vendors").ItemsSource=rows;C<ListBox>("Vendors").SelectedItem=rows.FirstOrDefault(s=>s.Equals(selectedVendor,StringComparison.OrdinalIgnoreCase))??rows.FirstOrDefault();binding=false;
   selectedVendor=C<ListBox>("Vendors").SelectedItem as string;C<TextBlock>("PageInfo").Text=(visible.Count==0?0:page+1)+" / "+(visible.Count==0?0:pages);RefreshControls();
  }
  void ChangePage(int direction){page+=direction;BindPage(false);if(!IsRandom)Generate();}
  void Generate(){
   if(database==null||generator==null||busy||(!IsLocal&&searching))return;
   if(!IsLocal&&(matches.Count==0||(!IsRandom&&string.IsNullOrEmpty(selectedVendor))))return;
   string vendor=IsLocal?null:IsRandom?matches[generator.Index(matches.Count)]:selectedVendor;
   var excluded=new HashSet<string>(used);foreach(var a in adapters)if(a.Current!=null)excluded.Add(a.Current);
   try{VendorBlock block;string value=generator.Generate(vendor,IsLocal,excluded,out block);used.Add(value);if(used.Count>10000)used.Clear();C<TextBox>("Address").Text=Mac.Format(value);Reveal(C<TextBox>("Address"));}catch(Exception ex){Error("未能生成地址",ex.Message);}
  }
  void ShowMatch(){string text=C<TextBox>("Address").Text;var block=database==null?null:database.Lookup(text);C<TextBlock>("Match").Text=!Mac.Valid(text)?"请输入有效的单播 MAC 地址":Mac.Local(text)?"本地管理地址":block==null?"未找到对应厂商":block.Vendor+" · "+block.Prefix;RefreshControls();}
  void RefreshControls(){
   bool ready=database!=null&&!busy,local=IsLocal;
   C<TextBox>("Search").IsEnabled=ready&&!local;C<ListBox>("Vendors").IsEnabled=ready&&!local&&!searching;
   C<StackPanel>("VendorTools").Visibility=local?Visibility.Collapsed:Visibility.Visible;
   foreach(var name in new[]{"ChooseVendor","RandomVendor","LocalMode"})C<RadioButton>(name).IsEnabled=ready;
   C<Button>("Generate").Content=local?"生成本地地址":IsRandom?"随机厂商生成":"按所选厂商生成";
   C<Button>("Generate").IsEnabled=ready&&(local||(!searching&&matches.Count>0));
   C<Button>("Copy").IsEnabled=Mac.Valid(C<TextBox>("Address").Text);
   var a=SelectedAdapter;bool change=!demo&&!busy&&!networkLoading&&a!=null&&a.RegistryPath!=null;
   C<Button>("Apply").IsEnabled=change&&Mac.Valid(C<TextBox>("Address").Text);C<Button>("Reset").IsEnabled=change;C<Button>("Undo").IsEnabled=change&&a.HasPreviousSetting;
   C<Button>("Refresh").IsEnabled=!busy&&!networkLoading;C<ComboBox>("Adapters").IsEnabled=!busy&&!networkLoading;
   C<CheckBox>("Virtuals").IsEnabled=!busy;C<TextBox>("Address").IsReadOnly=busy;
   int pages=Math.Max(1,(visible.Count+pageSize-1)/pageSize);C<Button>("Previous").IsEnabled=ready&&!local&&!searching&&page>0;C<Button>("Next").IsEnabled=ready&&!local&&!searching&&page+1<pages;
   C<TextBlock>("VendorCount").Text=database==null?"正在加载厂商…":searching?"正在搜索…":appliedQuery.Length==0?"常用厂商 · "+visible.Count:matches.Count>100?"找到 "+matches.Count.ToString("N0")+" 个 · 前 100 个":"找到 "+matches.Count+" 个厂商";
  }
  async Task Change(string action){
   var adapter=SelectedAdapter;if(adapter==null||busy||demo)return;string mac=Mac.Normalize(C<TextBox>("Address").Text);if(action=="set"&&!Mac.Valid(mac))return;
   string what=action=="set"?"将地址改为 "+Mac.Format(mac):action=="reset"?"恢复网卡默认地址":"恢复上次修改前的设置";
   if(!await Ask("确认修改",adapter.Name+"\n"+what+"\n\n所选网卡会短暂断网，完成后重新启用。",true))return;
   busy=true;RefreshControls();ShowProgress("正在处理网卡","正在保存设置、重启所选网卡并核对实际地址…");
   try{var result=await RunElevated(adapter.Id,action,mac??"-");busy=false;HideSheet();await ReadAdapters();await Ask(result.Code==0?"操作完成":result.Code==3?"请确认实际地址":"操作未成功",result.Message,false);}
   catch(System.ComponentModel.Win32Exception ex){HideSheet();if(ex.NativeErrorCode!=1223)Error("无法启动操作",ex.Message);}
   catch(Exception ex){HideSheet();Error("操作失败",ex.Message);}finally{busy=false;RefreshControls();}
  }
  async Task<OperationResult> RunElevated(string id,string action,string value){
   string name="MacAddressTool-"+Guid.NewGuid().ToString("N");var security=new PipeSecurity();
   using(var identity=WindowsIdentity.GetCurrent())security.AddAccessRule(new PipeAccessRule(identity.User,PipeAccessRights.ReadWrite,AccessControlType.Allow));
   security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
   using(var pipe=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security)){
    var incoming=Task.Run(()=>{pipe.WaitForConnection();using(var reader=new StreamReader(pipe))return reader.ReadLine();});
    var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--change "+Guid.Parse(id).ToString("D")+" "+action+" "+value+" "+name){UseShellExecute=true,Verb="runas"};
    using(var process=Process.Start(info)){
     if(await Task.WhenAny(incoming,Task.Delay(300000))!=incoming)throw new TimeoutException("未收到操作结果，请刷新检查实际网卡状态。");
     var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await incoming);return new OperationResult(Convert.ToInt32(data["Code"]),Convert.ToString(data["Message"]));
    }
   }
  }
  async void Error(string title,string message){if(!Window.IsLoaded)return;await Ask(title,message,false);}
  Task<bool> Ask(string title,string text,bool confirm){
   C<TextBlock>("SheetTitle").Text=title;C<TextBlock>("SheetText").Text=text;C<ProgressBar>("SheetProgress").Visibility=Visibility.Collapsed;C<Grid>("SheetButtons").Visibility=Visibility.Visible;C<Button>("SheetCancel").Visibility=confirm?Visibility.Visible:Visibility.Collapsed;C<Button>("SheetConfirm").Content=confirm?"继续":"知道了";
   sheetCompletion=new TaskCompletionSource<bool>();C<Grid>("SheetOverlay").Visibility=Visibility.Visible;Reveal(C<Border>("Sheet"));C<Button>("SheetConfirm").Focus();return sheetCompletion.Task;
  }
  void CompleteSheet(bool answer){var completion=sheetCompletion;sheetCompletion=null;HideSheet();if(completion!=null)completion.TrySetResult(answer);}
  void HideSheet(){C<Grid>("SheetOverlay").Visibility=Visibility.Collapsed;}
  void ShowProgress(string title,string text){C<TextBlock>("SheetTitle").Text=title;C<TextBlock>("SheetText").Text=text;C<Grid>("SheetButtons").Visibility=Visibility.Collapsed;C<ProgressBar>("SheetProgress").Visibility=Visibility.Visible;C<Grid>("SheetOverlay").Visibility=Visibility.Visible;Reveal(C<Border>("Sheet"));}
  async void Toast(string message){var toast=C<Border>("Toast");C<TextBlock>("ToastText").Text=message;toast.Visibility=Visibility.Visible;Reveal(toast);await Task.Delay(1600);toast.Visibility=Visibility.Collapsed;}
  double FontSize {get{return Convert.ToDouble(Window.Resources["BodySize"]);}}
  void SetFontSize(int index){fontIndex=index;double multiplier=index==0?1:index==1?1.10:1.20;Window.Resources["BodySize"]=16*multiplier;Window.Resources["SmallSize"]=13*multiplier;Window.Resources["HeadingSize"]=21*(1+index*.05);C<TextBlock>("CurrentMac").FontSize=26*multiplier;C<TextBlock>("PermanentMac").FontSize=23*multiplier;C<TextBox>("Address").FontSize=28*multiplier;AnimateSegment("SizeThumb",82*index);Window.Dispatcher.BeginInvoke(new Action(UpdatePageSize),DispatcherPriority.Loaded);}
  void AnimateSegment(string name,double x){var element=C<Border>(name);var transform=(TranslateTransform)element.RenderTransform;if(reducedMotion){transform.BeginAnimation(TranslateTransform.XProperty,null);transform.X=x;return;}transform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(x,TimeSpan.FromMilliseconds(230)){EasingFunction=new BackEase{Amplitude=.10,EasingMode=EasingMode.EaseOut}});}
  void AnimateSwitch(bool enabled){
   var check=C<CheckBox>("Virtuals");
   check.ApplyTemplate();
   var thumb=check.Template.FindName("Thumb",check) as System.Windows.Shapes.Ellipse;
   if(thumb==null)return;
   var movement=(TranslateTransform)thumb.RenderTransform;
   if(movement.IsFrozen){
    movement=movement.Clone();
    thumb.RenderTransform=movement;
   }
   var animation=new DoubleAnimation(enabled?20:0,TimeSpan.FromMilliseconds(reducedMotion?0:180));
   animation.EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut};
   movement.BeginAnimation(TranslateTransform.XProperty,animation);
  }
  void Reveal(UIElement element){element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.68,1,TimeSpan.FromMilliseconds(reducedMotion?0:160)));}
  void WireMotion(){foreach(var button in VisualChildren<Button>(Window)){
   var sharedScale=button.RenderTransform as ScaleTransform;
   if(sharedScale!=null)button.RenderTransform=sharedScale.Clone();
   button.MouseEnter+=(s,e)=>{var hover=button.Template.FindName("Hover",button) as Border;if(hover!=null)hover.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.1,TimeSpan.FromMilliseconds(reducedMotion?0:100)));};
   button.MouseLeave+=(s,e)=>{var hover=button.Template.FindName("Hover",button) as Border;if(hover!=null)hover.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(reducedMotion?0:140)));ScaleButton(button,1);};
   button.PreviewMouseLeftButtonDown+=(s,e)=>ScaleButton(button,.965);button.PreviewMouseLeftButtonUp+=(s,e)=>ScaleButton(button,1);
  }UpdateMotionLabel();ApplyPopupMotion();}
  void UpdateMotionLabel(){C<Button>("Motion").Content=reducedMotion?"动效 · 关":"动效 · 开";}
  void ScaleButton(Button button,double scale){var transform=button.RenderTransform as ScaleTransform;if(transform==null)return;var easing=new CubicEase{EasingMode=EasingMode.EaseOut};transform.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(scale,TimeSpan.FromMilliseconds(reducedMotion?0:130)){EasingFunction=easing});transform.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(scale,TimeSpan.FromMilliseconds(reducedMotion?0:130)){EasingFunction=easing});}
  void ApplyPopupMotion(){foreach(var combo in new[]{C<ComboBox>("Adapters")}){combo.ApplyTemplate();var popup=combo.Template.FindName("PART_Popup",combo) as Popup;if(popup!=null)popup.PopupAnimation=reducedMotion?PopupAnimation.None:PopupAnimation.Fade;}}
  void SetTheme(bool value){
   dark=value;C<Button>("Theme").Content=dark?"外观 · 深色":"外观 · 浅色";
   string[] names={"WindowBrush","SurfaceBrush","GlassBrush","FieldBrush","InkBrush","MutedBrush","LineBrush","AccentSoftBrush","SelectionBrush","BlueGlow","MintGlow","AccentBrush","PrimaryBrush"};
   string[] light={"#EDF2F8","#F7FFFFFF","#BFFFFFFF","#EFF3F8","#182333","#6F7D90","#28FFFFFF","#E1EFFF","#F8FFFFFF","#599EC8FF","#458DE8D5","#087BFA","#006CDD"};
   string[] night={"#121316","#F71D1F24","#E82B2D33","#292C32","#F2F3F5","#B0B5BF","#18FFFFFF","#243A54","#434750","#123C64A4","#10397569","#7BB4FF","#1971D4"};
   for(int i=0;i<names.Length;i++)Window.Resources[names[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString((dark?night:light)[i]));Reveal(C<Grid>("Design"));
  }
  static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);var item=child as T;if(item!=null)yield return item;foreach(var descendant in VisualChildren<T>(child))yield return descendant;}}
  public async Task<object> Exercise(string captureDirectory){
   await Initialization;if(database==null||adapters.Count==0)throw new Exception("Initialization failed.");
   foreach(string query in new[]{"英特尔","苹果","a","华为",""}){C<TextBox>("Search").Text=query;searchTimer.Stop();await Search(true);if(visible.Count>100)throw new Exception("Visible vendor limit exceeded.");}
   int old=page;if(visible.Count>pageSize){ChangePage(1);if(page==old)throw new Exception("Paging failed.");ChangePage(-1);}
   for(int i=0;i<3;i++){C<RadioButton>("Size"+i).IsChecked=true;await Task.Delay(30);}SetFontSize(1);
   var themeButton=C<Button>("Theme");
   foreach(bool motionOff in new[]{false,true}){
    reducedMotion=motionOff;
    foreach(var button in VisualChildren<Button>(Window).Where(b=>b.RenderTransform is ScaleTransform).ToList()){
     button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
     button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent});
     button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseLeaveEvent});
    }
    foreach(bool expectedDark in new[]{true,false}){
     themeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
     if(dark!=expectedDark)throw new Exception("Appearance button did not switch theme.");
    }
    var virtuals=C<CheckBox>("Virtuals");virtuals.IsChecked=true;virtuals.IsChecked=false;
   }
   reducedMotion=false;
   for(int i=0;i<8;i++){Generate();if(!Mac.Valid(C<TextBox>("Address").Text))throw new Exception("Invalid generated address.");}
   string vendor=C<ListBox>("Vendors").SelectedItem as string;if(!IsLocal&&!IsRandom&&!database.Lookup(C<TextBox>("Address").Text).Vendor.Equals(vendor,StringComparison.OrdinalIgnoreCase))throw new Exception("Vendor selection mismatch.");
   C<RadioButton>("LocalMode").IsChecked=true;Window.UpdateLayout();if(C<ListBox>("Vendors").IsVisible)throw new Exception("Local mode shows vendor controls.");Generate();if(!Mac.Local(C<TextBox>("Address").Text))throw new Exception("Local mode failed.");C<RadioButton>("ChooseVendor").IsChecked=true;Window.UpdateLayout();if(!C<ListBox>("Vendors").IsEnabled||!C<ListBox>("Vendors").IsVisible)throw new Exception("Cannot select vendors after local mode.");
   int layouts=0,textChecks=0;reducedMotion=true;
   foreach(var size in new[]{new[]{860,580},new[]{900,650},new[]{1180,800},new[]{1366,768},new[]{1920,1080}}){
    Window.Width=size[0];Window.Height=size[1];
    for(int font=0;font<3;font++){
     C<RadioButton>("Size"+font).IsChecked=true;SetFontSize(font);await Task.Delay(40);FitPage();Window.UpdateLayout();ValidateViewport();textChecks+=ValidateText();layouts++;
     if(size[0]==1180 && font==1){
      SetTheme(true);Window.UpdateLayout();RenderDemo(Path.Combine(captureDirectory,"compact-dark.png"));
      C<RadioButton>("LocalMode").IsChecked=true;Window.UpdateLayout();RenderDemo(Path.Combine(captureDirectory,"compact-local.png"));
      C<RadioButton>("ChooseVendor").IsChecked=true;SetTheme(false);
     }
     if(size[0]==1180)RenderDemo(Path.Combine(captureDirectory,"layout-font-"+font+".png"));
    }
   }
   SetTheme(true);await Task.Delay(40);Window.UpdateLayout();textChecks+=ValidateText();RenderDemo(Path.Combine(captureDirectory,"layout-dark.png"));SetTheme(false);
   return new{CommonItems=CommonCount,MaximumVisibleItems=MaximumVisibleCount,DatabaseMilliseconds,AdapterMilliseconds,AdapterCount=adapters.Count,FontButtons=true,Paging=true,Themes=true,ReducedMotion=true,VendorSelection=true,LayoutsChecked=layouts,TextBoundsChecked=textChecks,NetworkSettingsChanged=false};
  }
  void ValidateViewport(){var viewport=C<Grid>("Viewport");foreach(string name in new[]{"Adapters","Search","Vendors","Generate","Copy","Apply","Reset","Undo","Size0","Size1","Size2","ChooseVendor","RandomVendor","Address","Theme","Motion","Minimize","Maximize","Close"}){var element=C<FrameworkElement>(name);var bounds=element.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(),element.RenderSize));if(bounds.Left< -1||bounds.Top< -1||bounds.Right>viewport.ActualWidth+1||bounds.Bottom>viewport.ActualHeight+1)throw new Exception("Control outside the single-page viewport: "+name);}}
  int ValidateText(){
   int checks=0;
   foreach(string name in new[]{"Size0","Size1","Size2","ChooseVendor","RandomVendor","Generate","Copy","Apply","Reset","Undo","Theme","Motion"}){
    var control=C<Control>(name);foreach(var text in VisualChildren<TextBlock>(control)){
     var measure=new TextBlock{Text=text.Text,FontFamily=text.FontFamily,FontSize=text.FontSize,FontWeight=text.FontWeight,FontStyle=text.FontStyle,FontStretch=text.FontStretch};measure.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
     if(text.ActualHeight+.75<measure.DesiredSize.Height||text.ActualWidth+.75<measure.DesiredSize.Width)throw new Exception("Button text clipped: "+name);checks++;
    }
   }
   foreach(string name in new[]{"Address","Search"}){
    var field=C<TextBox>(name);string saved=field.Text;if(name=="Search")field.Text="中文搜索 Test";field.UpdateLayout();
    var host=field.Template.FindName("PART_ContentHost",field) as ScrollViewer;
    if(host==null||host.ExtentHeight>host.ViewportHeight+.75)throw new Exception("Text input line clipped: "+name);
    if(field.Text.Length>0){Rect first=field.GetRectFromCharacterIndex(0),last=field.GetRectFromCharacterIndex(field.Text.Length-1);if(first.IsEmpty||first.Top<0||first.Bottom>field.ActualHeight||last.Right>field.ActualWidth)throw new Exception("Text input characters clipped: "+name);}
    field.Text=saved;checks++;
   }
   searchTimer.Stop();return checks;
  }
  public void RenderDemo(string path){if(!demo)throw new InvalidOperationException("Only generic demo data may be exported.");Window.UpdateLayout();FitPage();Window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Window.ActualWidth,(int)Window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(C<Grid>("Viewport"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))encoder.Save(output);}
 }
}
