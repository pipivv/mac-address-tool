using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace MacTool {
    public sealed class Adapter {
        public string Id,Name,Description,Current,Permanent,Status,RegistryPath,Override;
        public bool Physical,Wireless,Advertised,HadOverride,Disabled,HasPreviousSetting;
        public override string ToString(){return Name+"  ·  "+Description;}
    }
    public static class NetworkOps {
        public const string ClassPath=@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
        const string BackupRoot=@"SOFTWARE\MacAddressTool\PreviousSettings";
        const string RecoveryRoot=@"SOFTWARE\MacAddressTool\PendingRecovery";
        public static bool Admin { get { using(var id=WindowsIdentity.GetCurrent())return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } }
        public static string PowerShell(string script,int timeout) {
            var info=new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe"));
            info.Arguments="-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); "+script));
            info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;
            using(var p=Process.Start(info)) {
                Task<string> stdout=p.StandardOutput.ReadToEndAsync(),stderr=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(timeout)) {try{p.Kill();}catch{} throw new TimeoutException("Windows 网卡服务响应超时，请刷新网卡状态。");}
                string output=stdout.Result,error=stderr.Result;
                if(p.ExitCode!=0)throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)?"Windows 网卡操作失败。":error.Trim());
                return output.Trim();
            }
        }
        public static List<Adapter> Read(out string note) {
            note=""; var result=new List<Adapter>();
            try {
                result=NativeAdapters.ReadAll();
            } catch(Exception ex) {
                note="网卡服务暂不可用，已使用系统接口读取。";
                foreach(var n in NetworkInterface.GetAllNetworkInterfaces()) {
                    Guid g;if(!Guid.TryParse(n.Id,out g)||n.GetPhysicalAddress().GetAddressBytes().Length!=6)continue;
                    result.Add(new Adapter{Id=g.ToString("D"),Name=n.Name,Description=n.Description,Current=Mac.Normalize(n.GetPhysicalAddress().ToString()),Status=n.OperationalStatus.ToString(),Wireless=n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211,Physical=false});
                }
                if(result.Count==0)throw new InvalidOperationException("无法读取网卡。请尝试右键以管理员身份运行。\r\n"+ex.Message);
            }
            using(var root=Registry.LocalMachine.OpenSubKey(ClassPath)) {
                if(root!=null)foreach(string sub in root.GetSubKeyNames()) {
                    if(!System.Text.RegularExpressions.Regex.IsMatch(sub,@"\A\d{4}\z"))continue;
                    try {using(var key=root.OpenSubKey(sub)) {
                        Guid g;if(key==null||!Guid.TryParse(Convert.ToString(key.GetValue("NetCfgInstanceId")),out g))continue;
                        var a=result.FirstOrDefault(x=>x.Id==g.ToString("D"));if(a==null)continue;
                        a.RegistryPath=ClassPath+"\\"+sub;
                        a.HadOverride=key.GetValueNames().Any(x=>x.Equals("NetworkAddress",StringComparison.OrdinalIgnoreCase));
                        a.Override=a.HadOverride?Convert.ToString(key.GetValue("NetworkAddress")):null;
                        using(var param=key.OpenSubKey(@"Ndi\params\NetworkAddress"))a.Advertised=param!=null;
                        string bus=Convert.ToString(key.GetValue("MatchingDeviceId"));
                        if(note.Length>0)a.Physical=bus.StartsWith("pci\\",StringComparison.OrdinalIgnoreCase)||bus.StartsWith("usb\\",StringComparison.OrdinalIgnoreCase);
                    }}catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}
                }
            }
            foreach(var a in result)try{if(a.Permanent==null)NativeAdapters.Enrich(a);a.HasPreviousSetting=HasBackup(a.Id);}catch{}
            return result.OrderByDescending(x=>x.Physical).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        public static string BackupPath(string id){return BackupRoot+"\\"+Guid.Parse(id).ToString("D");}
        static string RecoveryPath(string id){return RecoveryRoot+"\\"+Guid.Parse(id).ToString("D");}
        static bool HasSnapshot(string path){using(var key=Registry.LocalMachine.OpenSubKey(path))return key!=null&&key.GetValue("HadOverride")!=null;}
        public static bool HasBackup(string id){return HasSnapshot(RecoveryPath(id))||HasSnapshot(BackupPath(id));}
        static void SaveSnapshot(string path,bool had,string value,Adapter adapter){
            using(var b=Registry.LocalMachine.CreateSubKey(path)){
                b.SetValue("HadOverride",had?1:0,RegistryValueKind.DWord);b.SetValue("Value",value??"");b.SetValue("Effective",adapter.Current??"");b.SetValue("Description",adapter.Description??"");b.SetValue("SavedAt",DateTimeOffset.Now.ToString("o"));b.Flush();
            }
        }
        static void ClearRecovery(string id){Registry.LocalMachine.DeleteSubKey(RecoveryPath(id),false);}
        static string FindPath(string id) {
            Guid target=Guid.Parse(id);
            using(var root=Registry.LocalMachine.OpenSubKey(ClassPath)) {
                if(root==null)throw new InvalidOperationException("网卡配置不存在。");
                foreach(string sub in root.GetSubKeyNames()) {
                    if(!System.Text.RegularExpressions.Regex.IsMatch(sub,@"\A\d{4}\z"))continue;
                    using(var k=root.OpenSubKey(sub)) {Guid g;if(k!=null&&Guid.TryParse(Convert.ToString(k.GetValue("NetCfgInstanceId")),out g)&&g==target)return ClassPath+"\\"+sub;}
                }
            }
            throw new InvalidOperationException("所选网卡已移除，请刷新网卡列表。");
        }
        static string TargetScript(string id) {
            return "$target=[guid]'"+Guid.Parse(id).ToString("D")+"'; $a=@(Get-NetAdapter -IncludeHidden | Where-Object { [guid]$_.InterfaceGuid -eq $target }); if($a.Count -ne 1){throw 'Selected adapter is missing or ambiguous.'}; ";
        }
        static void Write(string path,bool had,string value) {
            using(var k=Registry.LocalMachine.OpenSubKey(path,true)) {
                if(k==null)throw new InvalidOperationException("网卡配置已不存在。");
                if(had)k.SetValue("NetworkAddress",value,RegistryValueKind.String);else k.DeleteValue("NetworkAddress",false);
                k.Flush();
            }
        }
        static void Restart(string id) {
            try {PowerShell(TargetScript(id)+"$a[0] | Restart-NetAdapter -Confirm:$false -ErrorAction Stop | Out-Null",45000);}
            catch {
                // The caller only permits initially enabled adapters. Recover an interrupted disable/enable cycle.
                try{PowerShell(TargetScript(id)+"$a[0] | Enable-NetAdapter -Confirm:$false -ErrorAction Stop | Out-Null",20000);}catch{}
                throw;
            }
        }
        static Adapter ReadOne(string id) {string n;return Read(out n).FirstOrDefault(x=>x.Id==id);}
        static bool WaitMatch(string id,string expected,out Adapter actual) {
            actual=null;
            // Native reads avoid waiting on the management service while the adapter restarts.
            for(int i=0;i<30;i++) {Thread.Sleep(1000);try{var read=new Adapter{Id=id};if(NativeAdapters.Enrich(read))actual=read;}catch{} if(actual!=null&&actual.Current!=null&&(expected==null||actual.Current==expected))return true;}
            return false;
        }
        public static OperationResult Change(string id,string action,string mac) {
            id=Guid.Parse(id).ToString("D");
            if(!Admin)throw new InvalidOperationException("应用地址需要管理员权限。");
            if(action!="set" && action!="reset" && action!="undo")throw new ArgumentException("操作无效。");
            if(action=="set"&&!Mac.Valid(mac))throw new ArgumentException("请输入有效的 12 位单播 MAC 地址。");
            bool entered=false;
            using(var mutex=new Mutex(false,@"Local\MacAddressTool-"+id)) {
                try {
                    try{entered=mutex.WaitOne(0);}catch(AbandonedMutexException){entered=true;}
                    if(!entered)throw new InvalidOperationException("该网卡正在被另一个工具窗口修改。");
                    string path=FindPath(id); var before=ReadOne(id);
                    if(before==null)throw new InvalidOperationException("无法读取所选网卡。");
                    bool recovering=HasSnapshot(RecoveryPath(id));
                    if(recovering&&action!="undo")throw new InvalidOperationException("上次操作的恢复尚未核实。请先点击“恢复上次设置”。");
                    string state=PowerShell(TargetScript(id)+"[string]$a[0].AdminStatus",20000);
                    if(state!="Up" && state!="1")throw new InvalidOperationException("所选网卡处于禁用或不可操作状态。请先在 Windows 中启用后再修改。");
                    bool had;string old;
                    using(var k=Registry.LocalMachine.OpenSubKey(path)) {
                        had=k.GetValueNames().Any(x=>x.Equals("NetworkAddress",StringComparison.OrdinalIgnoreCase));
                        if(had&&k.GetValueKind("NetworkAddress")!=RegistryValueKind.String)throw new InvalidOperationException("现有地址配置类型异常，未进行修改。");
                        old=had?(string)k.GetValue("NetworkAddress"):"";
                    }
                    bool desiredHad=action=="set";string desired=action=="set"?Mac.Normalize(mac):"";string expected=action=="set"?desired:before.Permanent;
                    if(action=="undo") {
                        using(var b=Registry.LocalMachine.OpenSubKey(recovering?RecoveryPath(id):BackupPath(id))) {
                            if(b==null||b.GetValue("HadOverride")==null)throw new InvalidOperationException("这张网卡尚无可恢复的修改记录。");
                            desiredHad=Convert.ToInt32(b.GetValue("HadOverride"))==1;desired=Convert.ToString(b.GetValue("Value",""));
                            expected=Mac.Normalize(Convert.ToString(b.GetValue("Effective","")));
                            // A previous driver may have ignored an invalid override; restore its exact setting and observed address.
                        }
                    }
                    if(action=="set") {
                        string n;var all=Read(out n);
                        if(all.Any(x=>x.Id!=id && x.Current==desired))throw new InvalidOperationException("该地址与本机另一张网卡重复，请重新生成。");
                    }
                    if(had==desiredHad && (!had||old==desired) && (expected==null||before.Current==expected)){
                        if(recovering)ClearRecovery(id);
                        return new OperationResult(0,"所选网卡已经使用这项设置，无需重启。");
                    }
                    // Keep the last successful backup untouched until the next operation is confirmed.
                    if(!recovering)SaveSnapshot(RecoveryPath(id),had,old,before);
                    try {
                        // Re-resolve by stable interface GUID immediately before each registry mutation.
                        Write(FindPath(id),desiredHad,desired);
                        Restart(id);
                        Adapter actual;
                        bool verified=WaitMatch(id,expected,out actual);
                        if(verified && expected!=null){
                            SaveSnapshot(BackupPath(id),had,old,before);ClearRecovery(id);
                            return new OperationResult(0,(action=="set"?"地址已应用并核对成功。":"设置已恢复并核对成功。")+"\r\n网卡："+before.Name+"\r\n实际 MAC："+Mac.Format(actual.Current));
                        }
                        if(action!="set"){
                            SaveSnapshot(BackupPath(id),had,old,before);ClearRecovery(id);
                            return new OperationResult(3,"地址设置已恢复，网卡已重启。当前地址尚未与预期默认地址核对一致；Windows 无线随机地址也可能影响结果。\r\n网卡："+before.Name+"\r\n当前实际 MAC："+Mac.Format(actual==null?null:actual.Current));
                        }
                        throw new InvalidOperationException("驱动未采用预期地址 "+Mac.Format(expected)+"。无线驱动或 Windows 随机硬件地址可能影响结果。");
                    } catch(Exception changeError) {
                        string rollback="";
                        try {Write(FindPath(id),had,old);Restart(id);Adapter restored;if(before.Current!=null&&WaitMatch(id,before.Current,out restored)){rollback="已恢复修改前设置，并核对原地址。";if(!recovering)ClearRecovery(id);}else rollback="已写回修改前设置，但未能核实原地址。请点击“恢复上次设置”或重启电脑。";}
                        catch(Exception rollbackError){rollback="自动恢复未完成："+rollbackError.Message+"\r\n请使用“恢复上次设置”或在设备管理器中恢复网络地址。";}
                        return new OperationResult(2,changeError.Message+"\r\n\r\n"+rollback);
                    }
                } finally {if(entered)mutex.ReleaseMutex();}
            }
        }
    }
    public sealed class OperationResult {
        public int Code;public string Message;
        public OperationResult(int c,string m){Code=c;Message=m;}
    }
}
