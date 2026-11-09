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
 internal sealed class Declaration {internal string Repository,Version,Host,Project,Branch;}
 internal sealed class Result {internal Declaration Local;internal string Version,Url,Notes,Commit;internal int? Comparison;}
 static Dictionary<string,object> Object(object value){return (Dictionary<string,object>)value;}
 static string Text(Dictionary<string,object> data,string key){return data.ContainsKey(key)?Convert.ToString(data[key]):"";}
 internal static Declaration Read(string directory) {
  string path=Path.Combine(directory,"ere.launcher.json");if(!File.Exists(path))return null;
  return Parse(File.ReadAllText(path));
 }
 static Declaration Parse(string json) {
  var data=ManifestFormat.Game(json);
  if(!data.ContainsKey("game"))return null;
  var game=Object(data["game"]);string repository=Text(game,"repository"),version=Text(game,"version");
  var repo=ManifestFormat.Repository(repository);string project=repo.Project;
  if(String.IsNullOrWhiteSpace(version))throw new IOException("游戏声明缺少当前版本号。");
  string branch=Text(game,"branch");if(String.IsNullOrWhiteSpace(branch))branch="main";
  if(branch.Length>200||branch.Any(Char.IsControl)||branch.StartsWith("-")||branch.Contains(".."))throw new IOException("游戏发布分支无效。");
  return new Declaration {Branch=branch,Repository=repo.Url,Host=repo.Host,Project=project,Version=version};
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
  string sha,download,remoteJson,message="";
  if(game.Host=="github.com") {
   string api="https://api.github.com/repos/"+game.Project;
   var commit=Object(new JavaScriptSerializer().DeserializeObject(await fetch(api+"/commits/"+Uri.EscapeDataString(game.Branch))));
   sha=Text(commit,"sha");ValidateCommit(sha);
   var file=Object(new JavaScriptSerializer().DeserializeObject(await fetch(api+"/contents/ere.launcher.json?ref="+sha)));
   if(Text(file,"encoding")!="base64")throw new IOException("远程游戏声明格式不支持。");
   remoteJson=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Text(file,"content")));
   download=game.Repository+"/archive/"+sha+".zip";
  } else {
   string id=null,name=game.Project.Split('/').Last();
   for(int page=1;page<=3&&id==null;page++) {
    var projects=(object[])new JavaScriptSerializer().DeserializeObject(await fetch("https://gitgud.io/api/v4/projects?search="+Uri.EscapeDataString(name)+"&simple=true&per_page=100&page="+page));
    foreach(var entry in projects){var project=Object(entry);if(String.Equals(Text(project,"path_with_namespace"),game.Project,StringComparison.OrdinalIgnoreCase)){id=Text(project,"id");break;}}
    if(projects.Length<100)break;
   }
   if(id==null)throw new IOException("未找到游戏的 GitGud 项目。");
   string api="https://gitgud.io/api/v4/projects/"+id;
   var commits=(object[])new JavaScriptSerializer().DeserializeObject(await fetch(api+"/repository/commits?ref_name="+Uri.EscapeDataString(game.Branch)+"&per_page=1"));
   if(commits.Length==0)throw new IOException("发布分支没有可用提交。");
   var commit=Object(commits[0]);sha=Text(commit,"id");ValidateCommit(sha);message=Text(commit,"title");
   remoteJson=await fetch(api+"/repository/files/ere.launcher.json/raw?ref="+sha);
   download=api+"/repository/archive.zip?sha="+sha;
  }
  var remote=Parse(remoteJson.TrimStart('\uFEFF'));
  if(remote==null || !String.Equals(remote.Repository,game.Repository,StringComparison.OrdinalIgnoreCase))throw new IOException("远程声明的仓库身份与本地不一致。");
  return new Result {Local=game,Version=remote.Version,Commit=sha,Url=download,Notes="发布分支："+game.Branch+"\n提交："+sha.Substring(0,12)+(message.Length>0?"\n"+message:""),Comparison=Compare(remote.Version,game.Version)};
 }
 static void ValidateCommit(string sha) {if(!Regex.IsMatch(sha??"",@"^[0-9a-fA-F]{40}(?:[0-9a-fA-F]{24})?$"))throw new IOException("发布分支提交标识无效。");}
}
