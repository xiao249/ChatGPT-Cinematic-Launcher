using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

internal static class Launcher
{
    private sealed class AppWindow {public int Id;public DateTime Started;public IntPtr Handle;}
    private const string Family="OpenAI.Codex_2p2nqsd0c76g0";
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int id);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr process,ref uint length,StringBuilder family);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder fullName);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackagePathByFullName(string fullName,ref uint length,StringBuilder path);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFinalPathNameByHandle(IntPtr file,StringBuilder path,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder name,ref uint length);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint id);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window,int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private delegate bool WindowCallback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,IntPtr state);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CommandLineToArgvW(string command,out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static string FinalPath(string path)
    {
        IntPtr file=CreateFile(path,0,7,IntPtr.Zero,3,0,IntPtr.Zero);
        if(file==new IntPtr(-1))throw new IOException("Cannot query the installed executable path.");
        try {
            var result=new StringBuilder(32768);uint count=GetFinalPathNameByHandle(file,result,32768,0);
            if(count==0||count>=32768)throw new IOException("Cannot resolve the installed executable path.");
            return result.ToString();
        } finally {CloseHandle(file);}
    }
    private static bool SameFile(string left,string right)
    {
        if(String.IsNullOrEmpty(left)||String.IsNullOrEmpty(right))return false;
        try{return String.Equals(FinalPath(left),FinalPath(right),StringComparison.OrdinalIgnoreCase);}catch{return false;}
    }
    private static string ImagePath(int id,bool official)
    {
        IntPtr handle=OpenProcess(0x1000,false,id);
        if(handle==IntPtr.Zero)return null;
        try {
            if(official){uint n=256;var family=new StringBuilder(256);if(GetPackageFamilyName(handle,ref n,family)!=0||family.ToString()!=Family)return null;}
            uint length=32768;var name=new StringBuilder((int)length);
            if(!QueryFullProcessImageName(handle,0,name,ref length))return null;
            string path=Path.GetFullPath(name.ToString());
            if(official){
                uint n=1024;var fullName=new StringBuilder(1024);if(GetPackageFullName(handle,ref n,fullName)!=0)return null;
                uint size=32768;var packageRoot=new StringBuilder(32768);if(GetPackagePathByFullName(fullName.ToString(),ref size,packageRoot)!=0)return null;
                if(!SameFile(path,Path.Combine(packageRoot.ToString(),"app","ChatGPT.exe")))return null;
            }
            return path;
        } finally {CloseHandle(handle);}
    }
    private static Dictionary<string,object> ReadRecord(string file)
    {
        try {
            var info=new FileInfo(file);if(!info.Exists||info.Length<2||info.Length>65536)return null;
            return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file,Encoding.UTF8));
        } catch {return null;}
    }
    private static bool ManagedSession(int id)
    {
        try {
            string stateRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin");
            var state=ReadRecord(Path.Combine(stateRoot,"state.json"));if(state==null)return false;
            if(!SameFile(Convert.ToString(state["codexExe"]),ImagePath(id,true)))return false;
            DateTime created=DateTime.Parse(Convert.ToString(state["createdAt"]),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
            using(var app=Process.GetProcessById(id)){if(app.StartTime.ToUniversalTime()>created)return false;}
            int cdpPort=Convert.ToInt32(state["port"]);if(cdpPort<1024||cdpPort>65535)return false;
            var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+cdpPort+"/json/version");
            request.Proxy=null;request.Timeout=1000;request.ReadWriteTimeout=1000;request.AllowAutoRedirect=false;
            using(var response=request.GetResponse())using(var stream=response.GetResponseStream()){
                byte[] buffer=new byte[8193];int total=0,read;
                while(total<buffer.Length&&(read=stream.Read(buffer,total,buffer.Length-total))>0)total+=read;
                if(total==buffer.Length)return false;
                var version=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(buffer,0,total));
                Uri endpoint=new Uri(Convert.ToString(version["webSocketDebuggerUrl"]));
                if(endpoint.Scheme!="ws"||endpoint.Host!="127.0.0.1"||endpoint.Port!=cdpPort||endpoint.AbsolutePath!="/devtools/browser/"+Convert.ToString(state["browserId"]))return false;
            }
            int injector=Convert.ToInt32(state["injectorPid"]);
            using(var process=Process.GetProcessById(injector)){
                string expected=Path.Combine(stateRoot,@"engine\runtime\node\node.exe");
                if(!SameFile(ImagePath(injector,false),expected))return false;
                DateTime start=DateTime.Parse(Convert.ToString(state["injectorStartedAt"]),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
                if(Math.Abs((process.StartTime.ToUniversalTime()-start).TotalSeconds)>1)return false;
            }
            string command=null;
            using(var searcher=new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId="+id.ToString(CultureInfo.InvariantCulture))){
                searcher.Options.Timeout=TimeSpan.FromSeconds(2);
                using(var items=searcher.Get()){foreach(ManagementObject item in items){using(item){command=Convert.ToString(item["CommandLine"]);}}}
            }
            if(String.IsNullOrEmpty(command))return false;
            int count;IntPtr argv=CommandLineToArgvW(command,out count);if(argv==IntPtr.Zero)return false;
            bool address=false,port=false,profile=false;
            try {for(int i=1;i<count;i++){
                string arg=Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size));
                if(arg=="--remote-debugging-address=127.0.0.1")address=true;
                if(arg=="--remote-debugging-port="+Convert.ToString(state["port"],CultureInfo.InvariantCulture))port=true;
                if(String.Equals(arg,"--user-data-dir="+Path.Combine(stateRoot,"cdp-profile"),StringComparison.OrdinalIgnoreCase))profile=true;
            }} finally {LocalFree(argv);}
            return address&&port&&profile;
        } catch {return false;}
    }
    private static AppWindow FindWindow(bool managed)
    {
        var windows=new Dictionary<int,List<IntPtr>>();
        EnumWindows((window,unused)=>{
            if(!EligibleWindow(window))return true;
            uint owner;GetWindowThreadProcessId(window,out owner);
            List<IntPtr> owned;if(!windows.TryGetValue((int)owner,out owned)){owned=new List<IntPtr>();windows.Add((int)owner,owned);}
            owned.Add(window);return true;
        },IntPtr.Zero);
        var found=new List<AppWindow>();
        foreach(var process in Process.GetProcessesByName("ChatGPT")){
            using(process){try {
                if(process.Id==Process.GetCurrentProcess().Id)continue;
                List<IntPtr> handles;
                if(process.SessionId!=Process.GetCurrentProcess().SessionId||!windows.TryGetValue(process.Id,out handles)||ImagePath(process.Id,true)==null)continue;
                if(managed&&!ManagedSession(process.Id))continue;
                foreach(var handle in handles)found.Add(new AppWindow{Id=process.Id,Started=process.StartTime.ToUniversalTime(),Handle=handle});
            } catch {}}
        }
        if(found.Count==1)return found[0];
        IntPtr foreground=GetForegroundWindow();
        foreach(var candidate in found)if(candidate.Handle==foreground)return candidate;
        return null;
    }
    private static bool EligibleWindow(IntPtr window)
    {
        // The pet/floating surface shares the app process and title; it is not
        // the chat window. MainWindowHandle alone can return that surface.
        return window!=IntPtr.Zero&&IsWindowVisible(window)&&!IsHungAppWindow(window)&&GetWindow(window,4)==IntPtr.Zero&&(GetWindowLongPtr(window,-20).ToInt64()&0x08000080L)==0;
    }
    private static bool StillValid(AppWindow target)
    {
        if(target==null)return false;
        try {uint owner;GetWindowThreadProcessId(target.Handle,out owner);using(var process=Process.GetProcessById(target.Id)){
            return owner==target.Id&&process.StartTime.ToUniversalTime()==target.Started&&process.SessionId==Process.GetCurrentProcess().SessionId&&EligibleWindow(target.Handle)&&ImagePath(target.Id,true)!=null;
        }} catch {return false;}
    }
    private static bool Activate(AppWindow target)
    {
        if(!StillValid(target))return false;
        if(IsIconic(target.Handle))ShowWindowAsync(target.Handle,9);
        SetForegroundWindow(target.Handle);
        if(IsTargetForeground(target))return true;
        uint owner;uint targetThread=GetWindowThreadProcessId(target.Handle,out owner);
        IntPtr foreground=GetForegroundWindow();uint foregroundThread=GetWindowThreadProcessId(foreground,out owner),currentThread=GetCurrentThreadId();
        bool joinedForeground=false,joinedTarget=false;
        try {
            if(foregroundThread!=0&&foregroundThread!=currentThread&&!IsHungAppWindow(foreground))joinedForeground=AttachThreadInput(currentThread,foregroundThread,true);
            if(targetThread!=0&&targetThread!=currentThread&&targetThread!=foregroundThread)joinedTarget=AttachThreadInput(currentThread,targetThread,true);
            BringWindowToTop(target.Handle);SetForegroundWindow(target.Handle);
            return IsTargetForeground(target);
        } finally {
            if(joinedTarget)AttachThreadInput(currentThread,targetThread,false);
            if(joinedForeground)AttachThreadInput(currentThread,foregroundThread,false);
        }
    }
    private static int ForegroundProcessId()
    {
        uint id;GetWindowThreadProcessId(GetForegroundWindow(),out id);return (int)id;
    }
    private static bool IsTargetForeground(AppWindow target)
    {
        IntPtr foreground=GetForegroundWindow();
        return foreground==target.Handle||GetAncestor(foreground,3)==target.Handle;
    }
    private static bool OfficialProcessesExist()
    {
        foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process){try{if(process.Id!=Process.GetCurrentProcess().Id&&process.SessionId==Process.GetCurrentProcess().SessionId&&ImagePath(process.Id,true)!=null)return true;}catch{}}
        return false;
    }
    private static void WriteRecord(string file,Dictionary<string,object> record)
    {
        File.WriteAllText(file,new JavaScriptSerializer().Serialize(record),new UTF8Encoding(false));
    }
    private static Dictionary<string,object> ReadLaunchRecord(string status,string token)
    {
        string result=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin",".start-result-"+token+".json");
        if(!File.Exists(result))return ReadRecord(status);
        try {
            string json;
            using(var stream=new FileStream(result,FileMode.Open,FileAccess.Read,FileShare.Read)){
                if(stream.Length<2||stream.Length>4096)throw new InvalidDataException();
                byte[] bytes=new byte[(int)stream.Length];int count=0,read;
                while(count<bytes.Length&&(read=stream.Read(bytes,count,bytes.Length-count))>0)count+=read;
                if(count!=bytes.Length)throw new InvalidDataException();
                json=new UTF8Encoding(false,true).GetString(bytes);
            }
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
            if(data==null||data.Count!=5||!data.ContainsKey("schemaVersion")||!data.ContainsKey("token")||!data.ContainsKey("outcome")||!data.ContainsKey("category")||!data.ContainsKey("appearanceRecovery")||!(data["schemaVersion"] is int)||(int)data["schemaVersion"]!=1||!(data["token"] is string)||(string)data["token"]!=token)throw new InvalidDataException();
            string outcome=data["outcome"] as string,category=data["category"] as string,recovery=data["appearanceRecovery"] as string;
            string[] categories={"none","cdp-launch-failed","cdp-direct-access-denied","cdp-endpoint-unavailable","port-unavailable","state-reconciliation-failed","injector-start-failed","renderer-verification-failed","superseded","internal-start-failure"};
            string[] recoveries={"not-needed","retained","restored","conflict-preserved","blocked","preserved-rendered"};
            if((outcome!="success"&&outcome!="failure")||Array.IndexOf(categories,category)<0||Array.IndexOf(recoveries,recovery)<0||(outcome=="success")!=(category=="none"))throw new InvalidDataException();
            var record=new Dictionary<string,object>{{"phase",outcome=="success"?"complete":"error"},{"mode","verified-engine-start"},{"token",token},{"message",outcome=="success"?"桌面客户端已启动。":"启动检查未通过（"+category+"）。请查看启动记录。"}};
            WriteRecord(status,record);return record;
        } catch {
            var record=new Dictionary<string,object>{{"phase","error"},{"message","启动结果不完整，请查看启动记录。"}};
            WriteRecord(status,record);return record;
        }
    }
    private static Process StartWorker(string root,string status,bool check,bool restart,string token,bool direct=false)
    {
        string ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        string arguments;
        if(direct){
            string script=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"CodexDreamSkin\engine\scripts\start-dream-skin.ps1");
            arguments="-NoLogo -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File \""+script+"\" -ResultToken "+token+" -OperationLockTimeoutMilliseconds 15000";
            if(new FileInfo(script).Length<262144&&Regex.IsMatch(File.ReadAllText(script,Encoding.UTF8),@"(?m)^\s*\[switch\]\$ActiveThemeOnly\s*,"))arguments+=" -ActiveThemeOnly";
        } else arguments="-NoLogo -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File \""+Path.Combine(root,"Launch-SavedSkin.ps1")+"\" -StatusPath \""+status+"\""+(check?" -ValidateOnly":"")+(restart?" -RestartExisting":"");
        var info=new ProcessStartInfo(ps,arguments);
        info.UseShellExecute=false;info.CreateNoWindow=true;info.WorkingDirectory=root;info.RedirectStandardError=true;info.RedirectStandardOutput=true;
        var process=Process.Start(info);
        WriteRecord(Path.Combine(root,"logs","process-"+token+".json"),new Dictionary<string,object>{{"directEngine",direct},{"createdAt",process.StartTime.ToUniversalTime().ToString("o")},{"processId",process.Id}});
        process.ErrorDataReceived+=(sender,e)=>{if(e.Data!=null){try{File.AppendAllText(Path.Combine(root,"logs","bootstrap-"+token+".log"),e.Data+Environment.NewLine,Encoding.UTF8);}catch{}}};
        process.OutputDataReceived+=(sender,e)=>{if(e.Data!=null){try{File.AppendAllText(Path.Combine(root,"logs","engine-"+token+".log"),e.Data+Environment.NewLine,Encoding.UTF8);}catch{}}};
        process.BeginErrorReadLine();process.BeginOutputReadLine();return process;
    }
    private static void Warning(string message,string logs)
    {
        Forms.MessageBox.Show(message+Environment.NewLine+"启动记录："+logs,"ChatGPT 启动",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Warning,Forms.MessageBoxDefaultButton.Button1,Forms.MessageBoxOptions.DefaultDesktopOnly);
    }
    private sealed class LaunchSnapshot
    {
        public AppWindow Target;
        public bool Warm, Complete, Activated, Failed, Cancelled;
        public string Message;
        public LaunchSnapshot Copy() { return (LaunchSnapshot)MemberwiseClone(); }
    }
    private static void RestoreForHandoff(AppWindow target)
    {
        if(target==null||!IsIconic(target.Handle))return;
        if(!StillValid(target))throw new InvalidOperationException("桌面窗口已变化，请重新打开。");
        ShowWindowAsync(target.Handle,9);
        DateTime deadline=DateTime.UtcNow.AddSeconds(2);
        while(IsIconic(target.Handle)&&DateTime.UtcNow<deadline)Thread.Sleep(25);
        if(IsIconic(target.Handle))throw new InvalidOperationException("桌面窗口暂时无法恢复，请重试。");
    }
    private sealed class ActivationGate
    {
        private readonly object sync = new object();
        private int preparedId;
        private IntPtr preparedHandle;
        public int Skip;
        public void Prepared(AppWindow target)
        {
            lock(sync) { preparedId=target.Id; preparedHandle=target.Handle; }
        }
        public bool IsPrepared(AppWindow target)
        {
            if(Thread.VolatileRead(ref Skip)!=0)return true;
            lock(sync)return target!=null&&preparedId==target.Id&&preparedHandle==target.Handle;
        }
    }
    private static LaunchSnapshot RunLaunch(string root,string status,string token,bool restart,ActivationGate gate,Action<LaunchSnapshot> publish)
    {
        var state=new LaunchSnapshot{Message="正在启动桌面客户端…"};
        Process worker=null;
        DateTime deadline=DateTime.UtcNow.AddSeconds(240);
        DateTime? exitedAt=null;
        try {
            state.Target=restart?null:FindWindow(true);
            RestoreForHandoff(state.Target);
            state.Warm=state.Target!=null;
            state.Complete=state.Warm;
            if(!state.Warm)worker=StartWorker(root,status,false,restart,token,!restart&&!OfficialProcessesExist());
            publish(state.Copy());
            while(true) {
                if(state.Target==null||!StillValid(state.Target)) {
                    if(state.Warm) {
                        state.Warm=false;state.Complete=false;
                        worker=StartWorker(root,status,false,false,token);exitedAt=null;
                    }
                    state.Target=FindWindow(false);RestoreForHandoff(state.Target);state.Activated=false;
                }
                if(worker!=null) {
                    var record=ReadLaunchRecord(status,token);
                    string phase=record==null?null:Convert.ToString(record["phase"]);
                    if(phase=="complete")state.Complete=true;
                    else if(phase=="cancelled") { state.Cancelled=true;state.Complete=true;publish(state.Copy());return state; }
                    else if(phase=="error")throw new InvalidOperationException(Convert.ToString(record["message"]));
                    else if(phase=="confirming") { deadline=DateTime.UtcNow.AddSeconds(240);state.Message="请确认是否重新打开客户端"; }
                    else state.Message="正在准备桌面页面…";
                    if(worker.HasExited&&!exitedAt.HasValue)exitedAt=DateTime.UtcNow;
                    if(!state.Complete&&exitedAt.HasValue&&DateTime.UtcNow>exitedAt.Value.AddSeconds(1))throw new InvalidOperationException("启动未完成，请查看记录。");
                }
                publish(state.Copy());
                if(state.Target!=null&&!state.Activated&&gate.IsPrepared(state.Target)) {
                    if(state.Warm&&!ManagedSession(state.Target.Id)) {
                        state.Warm=false;state.Complete=false;
                        worker=StartWorker(root,status,false,false,token);exitedAt=null;
                    } else state.Activated=Activate(state.Target);
                }
                if(state.Complete&&state.Activated) {
                    state.Message="桌面页面已准备好";publish(state.Copy());return state;
                }
                if(DateTime.UtcNow>deadline)throw new TimeoutException("客户端启动超时，请查看记录。");
                Thread.Sleep(200);
            }
        } catch(Exception error) {
            state.Failed=true;state.Message=error.Message;publish(state.Copy());return state;
        } finally { if(worker!=null)worker.Dispose(); }
    }
    [STAThread]
    private static void Main(string[] args)
    {
        string root=AppDomain.CurrentDomain.BaseDirectory,logs=Path.Combine(root,"logs");
        Directory.CreateDirectory(logs);
        if(args.Length==3&&args[0]=="--render-frame"){
            using(var frame=new CinematicSplash(root)){frame.RenderFrame(Double.Parse(args[1],CultureInfo.InvariantCulture),Path.GetFullPath(args[2]));}return;
        }
        bool check=Array.Exists(args,x=>x=="--preflight"),restart=Array.Exists(args,x=>x=="--restart"),preview=Array.Exists(args,x=>x=="--preview");
        string token=Guid.NewGuid().ToString("N"),status=Path.Combine(logs,"entry-"+token+".json"),ui=Path.Combine(logs,"animation-"+token+".json");
        using(var mutex=new Mutex(false,"Local\\ChatGPTDreamSkinVisibleEntry"+(preview?"Preview":""))){
            bool owned=false;try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
            if(!owned){Activate(FindWindow(false));Environment.ExitCode=2;return;}
            try {
                if(check){using(var worker=StartWorker(root,status,true,false,token)){if(!worker.WaitForExit(30000)){Environment.ExitCode=1;return;}Environment.ExitCode=worker.ExitCode;return;}}
                var gate=new ActivationGate();var sync=new object();
                var current=new LaunchSnapshot{Message="正在启动桌面客户端…"};
                Action<LaunchSnapshot> publish=value=>{lock(sync)current=value;};
                Task<LaunchSnapshot> backend=preview?null:Task.Factory.StartNew(()=>RunLaunch(root,status,token,restart,gate,publish));
                bool skipped=false,closing=false,readyAtClose=false,alignedReady=false,alignmentBypassed=false;
                int alignedId=0;IntPtr alignedHandle=IntPtr.Zero;
                DateTime closedAt=DateTime.UtcNow;
                using(var splash=new CinematicSplash(root)){
                    var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
                    Action close=()=>{if(!closing){closing=true;splash.FadeAndClose();}};
                    splash.SetStatus(preview?"动画预览 · 8 秒":"正在启动桌面客户端…");
                    splash.SkipRequested+=()=>{skipped=true;Interlocked.Exchange(ref gate.Skip,1);close();};
                    timer.Tick+=(sender,e)=>{
                        LaunchSnapshot snapshot;lock(sync)snapshot=current;
                        if(preview){if(splash.ElapsedSeconds>=8)close();return;}
                        if(snapshot.Failed||snapshot.Cancelled){close();return;}
                        splash.SetStatus(snapshot.Message);
                        if(snapshot.Target!=null&&!closing&&(alignedId!=snapshot.Target.Id||alignedHandle!=snapshot.Target.Handle)){
                            var target=snapshot.Target;
                            alignedReady=false;alignmentBypassed=false;
                            if(splash.AlignToWindow(target.Handle,()=>{alignedReady=true;gate.Prepared(target);})){alignedId=target.Id;alignedHandle=target.Handle;}
                            else {alignedId=target.Id;alignedHandle=target.Handle;alignmentBypassed=true;gate.Prepared(target);}
                        }
                        if(snapshot.Complete&&snapshot.Activated&&(alignedReady||alignmentBypassed)&&splash.ElapsedSeconds>=3){readyAtClose=true;close();}
                    };
                    splash.Window.ContentRendered+=(sender,e)=>timer.Start();
                    splash.Window.Closed+=(sender,e)=>{timer.Stop();Interlocked.Exchange(ref gate.Skip,1);};
                    splash.Window.ShowDialog();closedAt=DateTime.UtcNow;
                    LaunchSnapshot atClose;lock(sync)atClose=current;
                    WriteRecord(ui,new Dictionary<string,object>{{"preview",preview},{"warmActivation",atClose.Warm},{"skipped",skipped},{"animationSeconds",Math.Round(splash.ElapsedSeconds,2)},{"visibleClientProcessId",atClose.Target==null?0:atClose.Target.Id},{"targetWindowHandle",atClose.Target==null?0:atClose.Target.Handle.ToInt64()},{"foregroundWindowHandle",GetForegroundWindow().ToInt64()},{"activated",atClose.Activated},{"foregroundProcessId",ForegroundProcessId()},{"readyAtClose",readyAtClose},{"toolWindow",splash.IsToolWindow},{"windowAligned",splash.WindowAligned},{"alignmentBypassed",alignmentBypassed},{"nativeHandoff",splash.NativeHandoff},{"handoffFrameCount",splash.HandoffFrameCount},{"alignment",splash.AlignmentRecord},{"maximumFrameGapMs",Math.Round(splash.MaximumFrameGapMilliseconds,2)},{"finishedAt",closedAt.ToString("o")}});
                }
                if(backend!=null){
                    var result=backend.Result;
                    if(result.Warm&&result.Complete&&!result.Failed)WriteRecord(status,new Dictionary<string,object>{{"phase","complete"},{"mode","existing-managed-window"},{"clientProcessId",result.Target.Id},{"message","已打开现有桌面窗口；背景保持原样。"}});
                    if(result.Failed){Environment.ExitCode=1;Warning(result.Message,logs);}
                }
            } catch(Exception error){Environment.ExitCode=1;if(!check){Activate(FindWindow(false));Warning(error.Message,logs);}}
            finally{if(owned)mutex.ReleaseMutex();}
        }
    }
}
