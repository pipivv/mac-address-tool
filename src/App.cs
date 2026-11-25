using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;

[assembly:AssemblyTitle("MAC Address Tool")]
[assembly:AssemblyDescription("Offline Windows MAC utility with a Liquid Glass inspired interface")]
[assembly:AssemblyCompany("MAC Address Tool contributors")]
[assembly:AssemblyCopyright("Copyright 2026 MAC Address Tool contributors")]
[assembly:AssemblyVersion("2.0.1.0")]
[assembly:AssemblyFileVersion("2.0.1.0")]
namespace MacTool {
 public static class App {
  [STAThread] public static int Main(string[] args){
   try{
    if(args.Length==5&&args[0]=="--change")return Change(args);
    if(args.Length==2&&args[0]=="--core-test"){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(CoreTests.Run()),Encoding.UTF8);return 0;}
    bool test=args.Length==2&&args[0]=="--self-test",render=args.Length==2&&args[0]=="--render-demo",demo=args.Length==1&&args[0]=="--demo";
    if(args.Length>0&&!test&&!render&&!demo)throw new ArgumentException("启动参数无效。");
    var application=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var watch=Stopwatch.StartNew();var ui=new UiController(test||render||demo);int exitCode=0;
    if(test||render){
     ui.Window.Opacity=0;
     ui.Window.Loaded+=async(s,e)=>{
      try{
       await ui.Initialization;
       if(render){await Task.Delay(200);ui.RenderDemo(args[1]);}
       else{
        long ready=watch.ElapsedMilliseconds,last=ready,maxGap=0;int ticks=0;
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(sender,ev)=>{long now=watch.ElapsedMilliseconds;maxGap=Math.Max(maxGap,now-last);last=now;ticks++;};timer.Start();
        string captures=Path.GetDirectoryName(Path.GetFullPath(args[1]));Directory.CreateDirectory(captures);object exercise=await ui.Exercise(captures);await Task.Delay(150);timer.Stop();
        if(ticks<3||maxGap>1000)throw new InvalidOperationException("UI responsiveness check failed.");
        File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{Result="PASS",ReadyMilliseconds=ready,MaxMessageGapMilliseconds=maxGap,HeartbeatTicks=ticks,Exercise=exercise,GenericDemoDataOnly=true}),Encoding.UTF8);
       }
      }catch(Exception ex){exitCode=1;File.WriteAllText(args[1]+".error.txt",ex.GetType().Name+": "+ex.Message,Encoding.UTF8);}
      finally{ui.Window.Close();}
     };
    }
    application.Run(ui.Window);return exitCode;
   }catch(Exception ex){
    if(args.Length==2){File.WriteAllText(args[1]+".error.txt",ex.GetType().Name+": "+ex.Message,Encoding.UTF8);return 1;}
    MessageBox.Show(ex.Message,"MAC 地址工具",MessageBoxButton.OK,MessageBoxImage.Error);return 1;
   }
  }
  static int Change(string[] args){
   string id=Guid.Parse(args[1]).ToString("D"),action=args[2],value=args[3],pipe=args[4];
   if(!Regex.IsMatch(pipe,"\\AMacAddressTool-[0-9a-f]{32}\\z"))throw new ArgumentException("Invalid result channel.");
   OperationResult result;
   try{result=NetworkOps.Change(id,action,value);}catch(Exception ex){result=new OperationResult(2,ex.Message);}
   using(var channel=new NamedPipeClientStream(".",pipe,PipeDirection.Out)){
    channel.Connect(10000);using(var writer=new StreamWriter(channel,new UTF8Encoding(false))){writer.AutoFlush=true;writer.WriteLine(new JavaScriptSerializer().Serialize(result));}
   }
   return result.Code;
  }
 }
}
