using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal static class GamePlugins {
 sealed class Client : WebClient { protected override WebRequest GetWebRequest(Uri address) {var request=base.GetWebRequest(address);request.Timeout=20000;var http=request as HttpWebRequest;if(http!=null)http.ReadWriteTimeout=20000;return request;} }
 internal static bool Declared(string game) {return File.Exists(Path.Combine(game,"ere.launcher.json"));}
 static Dictionary<string,object> Obj(object value){return (Dictionary<string,object>)value;}
 static string Text(Dictionary<string,object> value,string key){return Convert.ToString(value[key]);}
 static object Read(string path){return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));}
 static object[] Requests(string game) {
  var data=Obj(Read(Path.Combine(game,"ere.launcher.json")));
  return data.ContainsKey("plugins")?(object[])data["plugins"]:new object[0];
 }
 static string Child(string root,string path) {
  string value=Path.GetFullPath(Path.Combine(root,path.Replace('/',Path.DirectorySeparatorChar)));
  if(!value.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)||path.Contains(":"))throw new IOException("插件路径越界。");return value;
 }
 internal static void Apply(string game,string runtime,Action<string> log) {Apply(game,runtime,log,null);}
 internal static void Apply(string game,string runtime,Action<string> log,GameProfile profile) {
  if(!Declared(game))return;
  var manifest=Obj(Read(Path.Combine(game,"ere.launcher.json")));
  foreach(var item in Requests(game)) {
   var request=Obj(item);
   if(profile!=null && Disabled(profile,Key(request))) {log("插件已停用："+Text(request,"id"));continue;}
   try {Load(request,runtime,log);}
   catch(Exception e) {
    if(!request.ContainsKey("fallback") || !(request["fallback"] is bool) || !(bool)request["fallback"])throw new IOException("必需插件加载失败："+e.Message,e);
    log("插件未启用，使用兼容输入："+e.Message);
   }
  }
 }
 internal sealed class State {
  internal string Key,Id,Repository,Status; internal bool Installed,Enabled,Fallback,Usable; internal Dictionary<string,object> Request;
 }
 static string Key(Dictionary<string,object> request){return Text(request,"source").ToLowerInvariant()+"/"+Text(request,"repository").ToLowerInvariant()+"/"+Text(request,"id");}
 static bool Disabled(GameProfile profile,string key){return profile.DisabledPlugins!=null && profile.DisabledPlugins.Contains(key);}
 internal static System.Collections.Generic.List<State> Check(GameProfile profile,string engineDirectory=null) {
  var result=new System.Collections.Generic.List<State>();string game=LauncherPaths.Resolve(profile.Directory);
  if(!Declared(game))return result;
  var codeCache=new Dictionary<string,string>();
  foreach(var entry in Requests(game)) {
   var request=Obj(entry);string cache=Cache(request);
   var state=new State {Key=Key(request),Id=Text(request,"id"),Repository=Text(request,"source")+"/"+Text(request,"repository"),Request=request,
    Fallback=request.ContainsKey("fallback") && request["fallback"] is bool && (bool)request["fallback"],Enabled=!Disabled(profile,Key(request))};
   state.Installed=File.Exists(Path.Combine(cache,"plugin.json"));state.Status=state.Installed?"已安装":"未安装";
   if(state.Installed)try {
    var plugin=Obj(Read(Path.Combine(cache,"plugin.json")));
    if(Text(plugin,"id")!=state.Id||Text(plugin,"source")!=Text(request,"source")||!String.Equals(Text(plugin,"repository"),Text(request,"repository"),StringComparison.OrdinalIgnoreCase))throw new IOException("身份不符");
    foreach(var file in (object[])plugin["files"]) {var data=Obj(file);if(!String.Equals(SessionTools.Sha(Child(cache,Text(data,"source"))),Text(data,"sha256"),StringComparison.OrdinalIgnoreCase))throw new IOException("文件损坏");}
    state.Usable=true;
    if(state.Enabled && !String.IsNullOrEmpty(engineDirectory)) {
     string main=Text(plugin,"main"),code;
     if(!codeCache.TryGetValue(main,out code))using(var archive=new EngineArchive(Path.Combine(engineDirectory,"resources","app.asar")))code=Encoding.UTF8.GetString(archive.FileBytes(main));
     foreach(var patchItem in (object[])plugin["replacements"]) {var patch=Obj(patchItem);code=EngineAdapter.ReplaceOnce(code,Text(patch,"find"),Text(patch,"replace"));}
     codeCache[main]=Text(plugin,"prepend")+code;
    }
   }catch(Exception e){state.Usable=false;state.Status="不可用："+e.Message;}
   result.Add(state);
  }
  return result;
 }
 internal static string DirectoryOf(State state) {return Cache(state.Request);}
 internal static void InstallOne(State state,Action<string> log) {
  string path=Cache(state.Request);
  if(Directory.Exists(path))throw new IOException("插件目录已存在但不可用，请修复或移走该插件目录后再安装。");
  Fetch(Text(state.Request,"repository"),path,log);
 }
 internal static bool AllowsFallback(string game) {
  if(!Declared(game))return true;
  foreach(var item in Requests(game)) {
   var request=Obj(item);
   if(File.Exists(Path.Combine(Cache(request),"plugin.json")))continue;
   if(!request.ContainsKey("fallback") || !(request["fallback"] is bool) || !(bool)request["fallback"])return false;
  }
  return true;
 }
 internal static string Missing(string game) {
  if(!Declared(game))return "";
  var missing=new List<string>();
  foreach(var item in Requests(game)) {
   var request=Obj(item);string path=Cache(request);
   if(!File.Exists(Path.Combine(path,"plugin.json")))missing.Add(Text(request,"id")+" — "+Text(request,"source")+"/"+Text(request,"repository"));
  }
  return String.Join("\n",missing);
 }
 static string Cache(Dictionary<string,object> request) {
  string id=Text(request,"id"),source=Text(request,"source"),repo=Text(request,"repository");
  if(source!="github"||!Regex.IsMatch(id,@"^[a-zA-Z0-9_-]+$")||!Regex.IsMatch(repo,@"^[a-zA-Z0-9_-]+/[a-zA-Z0-9_.-]+$"))throw new IOException("插件发布信息不支持。");
  return Path.Combine(LauncherPaths.UserRoot,"plugins","github",repo.Replace('/',Path.DirectorySeparatorChar),id);
 }
 internal static void InstallDeclared(string game,Action<string> log) {
  foreach(var item in Requests(game)) {
   var request=Obj(item);string root=Cache(request);
   if(!File.Exists(Path.Combine(root,"plugin.json")))Fetch(Text(request,"repository"),root,log);
  }
 }
 static void Load(Dictionary<string,object> request,string runtime,Action<string> log) {
  string id=Text(request,"id"),source=Text(request,"source"),repo=Text(request,"repository");
  if(source!="github"||!Regex.IsMatch(id,@"^[a-zA-Z0-9_-]+$")||!Regex.IsMatch(repo,@"^[a-zA-Z0-9_-]+/[a-zA-Z0-9_.-]+$"))throw new IOException("插件发布信息不支持。");
  string root=Path.Combine(LauncherPaths.UserRoot,"plugins","github",repo.Replace('/',Path.DirectorySeparatorChar),id);
  if(!File.Exists(Path.Combine(root,"plugin.json")))throw new IOException(id+" 未安装。");
  var plugin=Obj(Read(Path.Combine(root,"plugin.json")));
  if(Text(plugin,"id")!=id||Text(plugin,"source")!=source||!String.Equals(Text(plugin,"repository"),repo,StringComparison.OrdinalIgnoreCase))throw new IOException("插件身份与声明不符。");
  string archive=Path.Combine(runtime,"resources","app.asar"),temporary=archive+".plugin.tmp";
  var changes=new Dictionary<string,byte[]>();var external=new Dictionary<string,byte[]>();
  // Verify every payload and anchor before writing anything to the runtime.
  foreach(var entry in (object[])plugin["files"]) {
   var file=Obj(entry);string path=Child(root,Text(file,"source"));
   if(!String.Equals(SessionTools.Sha(path),Text(file,"sha256"),StringComparison.OrdinalIgnoreCase))throw new IOException(id+" 文件摘要不匹配。");
   string target=Text(file,"target");Child(runtime,target);
   if(Convert.ToBoolean(file["external"]))external[target]=File.ReadAllBytes(path);else changes[target]=File.ReadAllBytes(path);
  }
  try {
   using(var input=new EngineArchive(archive)) {
    string main=Text(plugin,"main"),code=Encoding.UTF8.GetString(input.FileBytes(main));
    foreach(var entry in (object[])plugin["replacements"]) {var patch=Obj(entry);code=EngineAdapter.ReplaceOnce(code,Text(patch,"find"),Text(patch,"replace"));}
    changes[main]=Encoding.UTF8.GetBytes(Text(plugin,"prepend")+code);input.Write(temporary,changes);
   }
   // External files are installed before the patched archive becomes active.
   foreach(var file in external){string path=Child(runtime,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,file.Value);}
   File.Replace(temporary,archive,null);
   log("已启用插件："+source+"/"+repo+" · "+id+" "+Text(plugin,"version"));
  }finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
 static void Fetch(string repo,string destination,Action<string> log) {
  ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
  string work=Path.Combine(LauncherPaths.UserRoot,"plugins",".download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
  try {
   using(var client=new Client()) {
    client.Encoding=Encoding.UTF8;client.Headers["User-Agent"]="Era-Electron-Launcher";
    var release=Obj(new JavaScriptSerializer().DeserializeObject(client.DownloadString("https://api.github.com/repos/"+repo+"/releases/latest")));
    string url=null;
    foreach(var entry in (object[])release["assets"]) {var asset=Obj(entry);if(Text(asset,"name").EndsWith("-win-x64.zip",StringComparison.OrdinalIgnoreCase)){url=Text(asset,"browser_download_url");break;}}
    if(url==null||!url.StartsWith("https://github.com/"+repo+"/releases/download/",StringComparison.OrdinalIgnoreCase))throw new IOException("Release 没有 Windows 插件包。");
    log("下载插件："+repo);string zip=Path.Combine(work,"plugin.zip");client.DownloadFile(url,zip);
    string extracted=Path.Combine(work,"content");Directory.CreateDirectory(extracted);
    using(var archive=ZipFile.OpenRead(zip)) {
     long total=0;if(archive.Entries.Count>1000)throw new IOException("插件包文件过多。");
     foreach(var entry in archive.Entries) {
      total+=entry.Length;if(total>32*1024*1024)throw new IOException("插件包过大。");
      string path=Child(extracted,entry.FullName);
      if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("插件包包含链接。");
      if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}
      Directory.CreateDirectory(Path.GetDirectoryName(path));using(var input=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew))input.CopyTo(output);
     }
    }
    var identity=Obj(Read(Path.Combine(extracted,"plugin.json")));
    if(!String.Equals(Text(identity,"repository"),repo,StringComparison.OrdinalIgnoreCase))throw new IOException("下载插件的发布者不匹配。");
    Directory.CreateDirectory(Path.GetDirectoryName(destination));Directory.Move(extracted,destination);
   }
  }finally{Directory.Delete(work,true);}
 }
}
