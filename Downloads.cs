using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class DownloadAsset {
    internal string Name, Url, Tag;
    internal bool Direct { get { Uri uri; return Uri.TryCreate(Url,UriKind.Absolute,out uri) && uri.Scheme=="https" && uri.AbsolutePath.EndsWith(".zip",StringComparison.OrdinalIgnoreCase); } }
    public override string ToString() {return Tag+" · "+Name+(Direct?"（直接下载）":"（浏览器下载）");}
}
internal sealed class LauncherRelease { internal string Tag, Url, Notes; }
internal static class Downloads {
    internal const string EngineApi="https://gitgud.io/api/v4/projects/29406/releases?per_page=50";
    internal const string UpdateApi="https://api.github.com/repos/Werenor/era-electron-launcher/releases/latest";
    static HttpClient Client() {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var client=new HttpClient {Timeout=TimeSpan.FromSeconds(30)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Era-Electron-Launcher/0.4.1");return client;
    }
    static Dictionary<string,object> Dict(object value) {return (Dictionary<string,object>)value;}
    static string Str(Dictionary<string,object> d,string key) {return d.ContainsKey(key)?Convert.ToString(d[key]):"";}
    internal static List<DownloadAsset> ParseEngines(string json) {
        var result=new List<DownloadAsset>();
        foreach(var item in (object[])new JavaScriptSerializer().DeserializeObject(json)) {
            var release=Dict(item); if(!release.ContainsKey("assets"))continue;
            var assets=Dict(release["assets"]); if(!assets.ContainsKey("links"))continue;
            foreach(var entry in (object[])assets["links"]) {
                var link=Dict(entry);string url=Str(link,"direct_asset_url");if(String.IsNullOrEmpty(url))url=Str(link,"url");
                Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https")continue;
                result.Add(new DownloadAsset {Tag=Str(release,"tag_name"),Name=Str(link,"name"),Url=url});
            }
        }
        return result;
    }
    internal static async Task<List<DownloadAsset>> Engines() {using(var client=Client())return ParseEngines(await client.GetStringAsync(EngineApi));}
    internal static async Task<LauncherRelease> LauncherLatest() {
        using(var client=Client()) {
            var data=Dict(new JavaScriptSerializer().DeserializeObject(await client.GetStringAsync(UpdateApi)));
            string url=Str(data,"html_url");
            if(!url.StartsWith("https://github.com/Werenor/era-electron-launcher/releases/",StringComparison.Ordinal))throw new IOException("更新地址不属于启动器项目。");
            string notes=Str(data,"body"); if(notes.Length>1800)notes=notes.Substring(0,1800)+"…";
            return new LauncherRelease {Tag=Str(data,"tag_name"),Url=url,Notes=notes};
        }
    }
    internal static async Task<string> DownloadAndInstall(DownloadAsset asset,string parent,Action<string> progress) {
        if(!asset.Direct)throw new IOException("此资源需要浏览器下载。");
        string cache=Path.Combine(LauncherPaths.UserRoot,"downloads");Directory.CreateDirectory(cache);
        string file=Path.Combine(cache,Guid.NewGuid().ToString("N")+".zip.part");
        try {
            using(var client=Client()) {
                client.Timeout=TimeSpan.FromMinutes(30);
                using(var response=await client.GetAsync(asset.Url,HttpCompletionOption.ResponseHeadersRead)) {
                    response.EnsureSuccessStatusCode(); long total=response.Content.Headers.ContentLength??0,received=0;
                    if(total>2L*1024*1024*1024)throw new IOException("下载包超过 2GB 上限。");
                    using(var input=await response.Content.ReadAsStreamAsync()) using(var output=File.Create(file)) {
                        byte[] buffer=new byte[131072];int n;long last=0;
                        while((n=await input.ReadAsync(buffer,0,buffer.Length))>0) {
                            received+=n;if(received>2L*1024*1024*1024)throw new IOException("下载包超过 2GB 上限。");
                            await output.WriteAsync(buffer,0,n);
                            if(received-last>=1024*1024) {progress("下载引擎："+(received/1024/1024)+" MB"+(total>0?" / "+(total/1024/1024)+" MB":""));last=received;}
                        }
                        if(total>0 && received!=total)throw new IOException("下载文件不完整。");
                    }
                }
            }
            progress("正在解压和验证引擎…");return await Task.Run(()=>InstallZip(file,parent));
        } finally {if(File.Exists(file))File.Delete(file);}
    }
    internal static string InstallZip(string file,string parent) {
        parent=Path.GetFullPath(parent);Directory.CreateDirectory(parent);
        string stage=Path.Combine(parent,".ere-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try {
            using(var zip=ZipFile.OpenRead(file)) {
                if(zip.Entries.Count>20000)throw new IOException("ZIP 文件条目过多。");long size=0;
                foreach(var entry in zip.Entries) {
                    size+=entry.Length;if(size>4L*1024*1024*1024)throw new IOException("解压内容超过 4GB 上限。");
                    string path=SafeEntry(stage,entry.FullName);
                    if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("不支持 ZIP 符号链接。");
                    if(entry.FullName.EndsWith("/")||entry.FullName.EndsWith("\\")) {Directory.CreateDirectory(path);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var input=entry.Open()) using(var output=new FileStream(path,FileMode.CreateNew))input.CopyTo(output);
                }
            }
            string root=FindEngine(stage,0);if(root==null)throw new IOException("ZIP 中未找到 Windows Era-Electron 引擎。");
            var install=new EngineInstall {Directory=root};EngineCatalog.Inspect(install);
            string version=new string(install.Version.Select(c=>Char.IsLetterOrDigit(c)||c=='.'||c=='-'?c:'_').ToArray());
            string destination=Path.Combine(parent,"ERE_"+version+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.Move(root,destination);return destination;
        } finally {if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    internal static string SafeEntry(string root,string name) {
        if(String.IsNullOrEmpty(name)||name.Contains(":"))throw new IOException("ZIP 路径无效。");
        string path=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
        if(!path.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("ZIP 路径越界。");
        return path;
    }
    static string FindEngine(string root,int depth) {
        if(File.Exists(Path.Combine(root,"resources","app.asar"))||File.Exists(Path.Combine(root,"resources","app","package.json"))) {
            try {EngineCatalog.Executable(root);return root;}catch(IOException){}
        }
        if(depth>=5)return null;
        foreach(string directory in Directory.GetDirectories(root)) {string found=FindEngine(directory,depth+1);if(found!=null)return found;}
        return null;
    }
}
internal sealed class ReleaseDialog : Form {
    readonly ListBox assets=new ListBox {Dock=DockStyle.Fill};
    readonly Label hint=new Label {Dock=DockStyle.Top,Height=48,Text="正在读取官方发布列表…"};
    internal DownloadAsset Selected {get{return assets.SelectedItem as DownloadAsset;}}
    internal ReleaseDialog() {
        Text="下载 Era-Electron 引擎";Width=720;Height=420;StartPosition=FormStartPosition.CenterParent;
        var ok=new Button {Text="下载所选版本",Dock=DockStyle.Bottom,Height=40,Enabled=false};
        ok.Click+=(s,e)=>{if(Selected!=null){DialogResult=DialogResult.OK;Close();}};
        Controls.Add(assets);Controls.Add(hint);Controls.Add(ok);
        Shown+=async(s,e)=>{try {
            var list=await Downloads.Engines();if(IsDisposed)return;
            foreach(var item in list)assets.Items.Add(item);
            if(list.Count>0){assets.SelectedIndex=0;ok.Enabled=true;hint.Text="请选择版本和下载资源；网盘链接会打开浏览器。";}
            else hint.Text="官方暂未提供可用下载链接，可通过官方发布页下载。";
        }catch(Exception error){if(!IsDisposed)hint.Text="获取发布列表失败："+error.Message;}};
    }
}
