using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class GameUpdates {
 internal sealed class Declaration {internal string Repository,Version,Host,Project;}
 internal sealed class Result {internal Declaration Local;internal string Version,Url,Notes;internal int? Comparison;}
 static Dictionary<string,object> Object(object value){return (Dictionary<string,object>)value;}
 static string Text(Dictionary<string,object> data,string key){return data.ContainsKey(key)?Convert.ToString(data[key]):"";}
 internal static Declaration Read(string directory) {
  string path=Path.Combine(directory,"ere.launcher.json");if(!File.Exists(path))return null;
  var data=Object(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)));
  if(!data.ContainsKey("game"))return null;
  var game=Object(data["game"]);string repository=Text(game,"repository"),version=Text(game,"version");
  Uri uri;
  if(!Uri.TryCreate(repository,UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo)||!String.IsNullOrEmpty(uri.Query)||!String.IsNullOrEmpty(uri.Fragment)||!uri.IsDefaultPort)throw new IOException("游戏仓库必须填写完整的 HTTPS 仓库地址。");
  string project=uri.AbsolutePath.Trim('/');if(project.EndsWith(".git"))project=project.Substring(0,project.Length-4);
  if((uri.Host!="github.com"&&uri.Host!="gitgud.io")||!Regex.IsMatch(project,@"^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+$")||project.Split('/').Any(x=>x=="."||x=="..")||(uri.Host=="github.com"&&project.Split('/').Length!=2))throw new IOException("游戏仓库地址仅支持 GitHub 和 GitGud 项目。");
  if(String.IsNullOrWhiteSpace(version))throw new IOException("游戏声明缺少当前版本号。");
  return new Declaration {Repository="https://"+uri.Host+"/"+project,Host=uri.Host,Project=project,Version=version};
 }
 internal static int? Compare(string remote,string local) {
  var regex=new Regex(@"^[vV]?(\d+(?:\.\d+){1,3})(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
  var a=regex.Match(remote??"");var b=regex.Match(local??"");if(!a.Success||!b.Success)return null;
  var x=a.Groups[1].Value.Split('.');var y=b.Groups[1].Value.Split('.');
  for(int i=0;i<Math.Max(x.Length,y.Length);i++) {
   ulong n=0,m=0;if((i<x.Length&&!UInt64.TryParse(x[i],out n))||(i<y.Length&&!UInt64.TryParse(y[i],out m)))return null;
   if(n!=m)return n>m?1:-1;
  }
  string preA=a.Groups[2].Value,preB=b.Groups[2].Value;
  if(preA.Length==0||preB.Length==0)return preA==preB?0:preA.Length==0?1:-1;
  var pa=preA.Split('.');var pb=preB.Split('.');
  for(int i=0;i<Math.Min(pa.Length,pb.Length);i++) {
   ulong n,m;bool na=UInt64.TryParse(pa[i],out n),nb=UInt64.TryParse(pb[i],out m);
   int result=na&&nb?n.CompareTo(m):na!=nb?(na?-1:1):String.CompareOrdinal(pa[i],pb[i]);if(result!=0)return Math.Sign(result);
  }
  return Math.Sign(pa.Length-pb.Length);
 }
 internal static async Task<Result> Check(string directory,Func<string,Task<string>> fetch=null) {
  var game=Read(directory);if(game==null)throw new IOException("当前游戏尚未声明仓库和版本号。");
  if(fetch==null) {
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   using(var client=new HttpClient {Timeout=TimeSpan.FromSeconds(25)}) {
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Era-Electron-Launcher");
    return await Check(directory,endpoint=>client.GetStringAsync(endpoint));
   }
  }
  string url;
  if(game.Host=="github.com")url="https://api.github.com/repos/"+game.Project+"/releases/latest";
  else {
   // Numeric IDs avoid .NET Framework normalizing encoded namespace slashes.
   string id=null,name=game.Project.Split('/').Last();
   for(int page=1;page<=3&&id==null;page++) {
    var projects=(object[])new JavaScriptSerializer().DeserializeObject(await fetch("https://gitgud.io/api/v4/projects?search="+Uri.EscapeDataString(name)+"&simple=true&per_page=100&page="+page));
    foreach(var entry in projects){var project=Object(entry);if(String.Equals(Text(project,"path_with_namespace"),game.Project,StringComparison.OrdinalIgnoreCase)){id=Text(project,"id");break;}}
    if(projects.Length<100)break;
   }
   if(id==null)throw new IOException("未找到游戏的 GitGud 项目。");
   url="https://gitgud.io/api/v4/projects/"+id+"/releases/permalink/latest";
  }
  var release=Object(new JavaScriptSerializer().DeserializeObject(await fetch(url)));
  string version=Text(release,"tag_name"),link=game.Host=="github.com"?Text(release,"html_url"):Text(Object(release["_links"]),"self");
  if(String.IsNullOrEmpty(version)||!link.StartsWith(game.Repository+"/",StringComparison.OrdinalIgnoreCase))throw new IOException("发布信息或发布页地址不属于声明的游戏仓库。");
  string notes=Text(release,game.Host=="github.com"?"body":"description");if(notes.Length>1400)notes=notes.Substring(0,1400)+"…";
  return new Result {Local=game,Version=version,Url=link,Notes=notes,Comparison=Compare(version,game.Version)};
 }
}
