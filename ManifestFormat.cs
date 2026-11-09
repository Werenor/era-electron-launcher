using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Versioned external declarations are validated before conversion to legacy
// internal identity fields. Cache keys and per-game enable choices stay stable.
internal static class ManifestFormat {
 internal sealed class RepositoryInfo {internal string Url,Host,Project;}
 static IOException Error(string path,string message){return new IOException(path+"："+message);}
 static Dictionary<string,object> Object(object value,string path){var obj=value as Dictionary<string,object>;if(obj==null)throw Error(path,"必须是对象");return obj;}
 static string String(Dictionary<string,object> obj,string key,string path,bool required=true){object value;if(!obj.TryGetValue(key,out value)){if(required)throw Error(path+"."+key,"缺少必填字段");return null;}var text=value as string;if(text==null||System.String.IsNullOrWhiteSpace(text))throw Error(path+"."+key,"必须是非空字符串");return text;}
 static bool Versioned(Dictionary<string,object> obj){object schema;if(obj.TryGetValue("$schema",out schema)&&!(schema is string))throw Error("$schema","必须是字符串");object value;if(!obj.TryGetValue("schemaVersion",out value))return false;if(!(value is int)||(int)value!=1)throw Error("schemaVersion","仅支持整数 1；请更新启动器或检查声明格式");return true;}
 static void Keys(Dictionary<string,object> obj,string path,params string[] keys){foreach(var key in obj.Keys)if(!keys.Contains(key))throw Error(path+"."+key,"未知字段");}
 static Dictionary<string,object> Parse(string json){try{return Object(new JavaScriptSerializer().DeserializeObject(json.TrimStart('\uFEFF')),"声明");}catch(IOException){throw;}catch(Exception){throw Error("声明","不是有效的 JSON 对象");}}
 internal static RepositoryInfo Repository(string text){
  Uri uri;
  if(text==null||text.Contains("\\")||text.Split('/').Any(x=>x=="."||x=="..")||!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="https"||!System.String.IsNullOrEmpty(uri.UserInfo)||!System.String.IsNullOrEmpty(uri.Query)||!System.String.IsNullOrEmpty(uri.Fragment)||!uri.IsDefaultPort)throw Error("repository","必须是完整的 HTTPS 仓库根地址");
  string project=uri.AbsolutePath.Trim('/');if(project.EndsWith(".git",StringComparison.OrdinalIgnoreCase))project=project.Substring(0,project.Length-4);
  if((uri.Host!="github.com"&&uri.Host!="gitgud.io")||!Regex.IsMatch(project,@"^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+$")||project.Split('/').Any(x=>x=="."||x=="..")||(uri.Host=="github.com"&&project.Split('/').Length!=2)||project.Split('/').Any(x=>x=="-"))throw Error("repository","仅支持 GitHub/GitGud 仓库根地址，不能填写分支、Release 或下载页");
  return new RepositoryInfo {Url="https://"+uri.Host+"/"+project,Host=uri.Host,Project=project};
 }
 internal static void Branch(string branch){if(System.String.IsNullOrWhiteSpace(branch)||branch.Length>200||branch.Any(Char.IsControl)||branch.StartsWith("-")||branch.Contains(".."))throw Error("game.branch","发布分支无效");}
 static void Identity(Dictionary<string,object> obj,string path,bool strict){
  string id=String(obj,"id",path);if(!Regex.IsMatch(id,@"^[a-zA-Z0-9_-]+$"))throw Error(path+".id","仅允许字母、数字、横线和下划线");
  string repo=String(obj,"repository",path);RepositoryInfo info;
  if(!strict&&!repo.StartsWith("https://",StringComparison.OrdinalIgnoreCase)){
   if(String(obj,"source",path)!="github")throw Error(path+".source","旧格式仅支持 github");
   info=Repository("https://github.com/"+repo);
  }else{
   info=Repository(repo);
   if(obj.ContainsKey("source")&&(strict||!System.String.Equals(Convert.ToString(obj["source"]),info.Host=="github.com"?"github":"gitgud",StringComparison.OrdinalIgnoreCase)))throw Error(path+".source","应省略，或与仓库平台一致");
  }
  // Plugin distribution remains GitHub Releases, as in existing versions.
  if(info.Host!="github.com")throw Error(path+".repository","当前插件下载仅支持 GitHub Releases");
  if(!Regex.IsMatch(info.Project,@"^[a-zA-Z0-9_-]+/[a-zA-Z0-9_.-]+$"))throw Error(path+".repository","插件仓库路径不支持");
  obj["source"]="github";obj["repository"]=info.Project;
 }
 internal static Dictionary<string,object> Game(string json){
  var root=Parse(json);bool strict=Versioned(root);
  if(strict)Keys(root,"声明","schemaVersion","$schema","game","plugins");
  if(root.ContainsKey("game")){
   var game=Object(root["game"],"game");if(strict)Keys(game,"game","repository","version","branch");
   game["repository"]=Repository(String(game,"repository","game")).Url;
   String(game,"version","game");
   if(game.ContainsKey("branch")){if(!strict&&(game["branch"]==null||(game["branch"] is string&&System.String.IsNullOrWhiteSpace((string)game["branch"]))))game.Remove("branch");else {string branch=String(game,"branch","game");Branch(branch);}}
  }
  if(root.ContainsKey("plugins")){
   var plugins=root["plugins"] as object[];if(plugins==null)throw Error("plugins","必须是数组");
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   for(int i=0;i<plugins.Length;i++){
    string path="plugins["+i+"]";var plugin=Object(plugins[i],path);
    if(strict)Keys(plugin,path,"id","repository","fallback");
    object fallback;if(plugin.TryGetValue("fallback",out fallback)&&!(fallback is bool))throw Error(path+".fallback","必须是布尔值");
    Identity(plugin,path,strict);
    if(!seen.Add(Convert.ToString(plugin["repository"])+"/"+Convert.ToString(plugin["id"])))throw Error(path,"重复的插件声明");
   }
  }
  return root;
 }
 internal static Dictionary<string,object> Plugin(string json){
  var plugin=Parse(json);bool strict=Versioned(plugin);
  if(strict){
   Keys(plugin,"插件","schemaVersion","$schema","id","name","repository","version","engineVersions","main","prepend","replacements","files");
   String(plugin,"version","插件");String(plugin,"main","插件");
   if(!plugin.ContainsKey("prepend")||!(plugin["prepend"] is string))throw Error("插件.prepend","必须是字符串");
   foreach(string field in new[]{"files","replacements"}){
    object value;var items=plugin.TryGetValue(field,out value)?value as object[]:null;if(items==null)throw Error("插件."+field,"必须是数组");
    foreach(var item in items){var entry=Object(item,"插件."+field);
     if(field=="files"){
      Keys(entry,"插件.files","source","target","external","sha256");String(entry,"source","插件.files");String(entry,"target","插件.files");
      if(!entry.ContainsKey("external")||!(entry["external"] is bool))throw Error("插件.files.external","必须是布尔值");
      if(!Regex.IsMatch(String(entry,"sha256","插件.files"),"^[a-fA-F0-9]{64}$"))throw Error("插件.files.sha256","必须是64位十六进制摘要");
     }else{Keys(entry,"插件.replacements","find","replace");String(entry,"find","插件.replacements");if(!entry.ContainsKey("replace")||!(entry["replace"] is string))throw Error("插件.replacements.replace","必须是字符串");}
    }
   }
   if(plugin.ContainsKey("name"))String(plugin,"name","插件");
   if(plugin.ContainsKey("engineVersions")&&!(plugin["engineVersions"] is string)&&!(plugin["engineVersions"] is object[]))throw Error("插件.engineVersions","必须是字符串或字符串数组");
   var versions=plugin.ContainsKey("engineVersions")?plugin["engineVersions"] as object[]:null;if(versions!=null&&versions.Any(x=>!(x is string)||System.String.IsNullOrWhiteSpace((string)x)))throw Error("插件.engineVersions","数组成员必须是非空字符串");
  }
  Identity(plugin,"插件",strict);return plugin;
 }
}
