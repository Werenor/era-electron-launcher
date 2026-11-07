using System;
using System.IO;
using System.Threading.Tasks;
internal static class GameUpdateTests {
 static void Verify(bool ok,string text){if(!ok)throw new Exception(text);}
 internal static void Run(string root) {
  Directory.CreateDirectory(Path.Combine(root,"sav"));string save=Path.Combine(root,"sav","save1.sav"),config=Path.Combine(root,"ere.config.json");
  File.WriteAllText(save,"PLAYER SAVE - DO NOT OVERWRITE");File.WriteAllText(config,"PLAYER SETTINGS");
  string declaration=Path.Combine(root,"ere.launcher.json");
  File.WriteAllText(declaration,"{\"game\":{\"repository\":\"https://github.com/Werenor/game-update-fixture\",\"version\":\"1.0.0\"}}");
  string saveHash=SessionTools.Sha(save),configHash=SessionTools.Sha(config),manifestHash=SessionTools.Sha(declaration);
  Verify(GameInformation.Version(root)=="1.0.0","manifest version displayed");
  Verify(GamePlugins.Missing(root)=="" && GamePlugins.Check(new GameProfile {Directory=root}).Count==0,"game-only declaration does not require plugins");
  Verify(GameUpdates.Compare("v1.2.0","1.1.9")==1 && GameUpdates.Compare("v1.0.0","1.0.0")==0,"release version comparison");
  Verify(GameUpdates.Compare("1.0.0","1.0.0-rc.2")==1 && GameUpdates.Compare("1.0.0-rc.10","1.0.0-rc.2")==1,"prerelease comparison");
  Verify(GameUpdates.Compare("nightly","1.0.0")==null,"unknown versions are not guessed");
  string sha=new string('a',40);
  string remote="{\"game\":{\"repository\":\"https://github.com/Werenor/game-update-fixture\",\"version\":\"1.1.0\"}}";
  Func<string,Task<string>> fetch=url=>{
   Verify(!url.Contains("releases"),"game checks must not require Releases");
   if(url.Contains("/commits/"))return Task.FromResult("{\"sha\":\""+sha+"\"}");
   Verify(url.EndsWith("?ref="+sha),"read manifest pinned to resolved commit");
   return Task.FromResult("{\"encoding\":\"base64\",\"content\":\""+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(remote))+"\"}");
  };
  var result=GameUpdates.Check(root,fetch).GetAwaiter().GetResult();Verify(result.Comparison==1 && result.Version=="1.1.0" && result.Url.Contains(sha),"readonly commit-pinned branch check");
  Verify(SessionTools.Sha(declaration)==manifestHash,"check does not replace local declaration");
  bool rejected=false;try {GameUpdates.Check(root,url=>Task.FromResult("{\"sha\":\"invalid\"}")).GetAwaiter().GetResult();}catch(IOException){rejected=true;}Verify(rejected,"invalid commits rejected");
  remote=remote.Replace("Werenor/game-update-fixture","somebody/other-game");
  rejected=false;try {GameUpdates.Check(root,fetch).GetAwaiter().GetResult();}catch(IOException){rejected=true;}Verify(rejected,"remote repository mismatch rejected");
  File.WriteAllText(declaration,"{\"game\":{\"repository\":\"https://gitgud.io/group/game\",\"version\":\"1.0.0\",\"branch\":\"release/stable\"}}");
  result=GameUpdates.Check(root,url=>{
   Verify(!url.Contains("releases"),"GitGud does not require Releases");
   if(url.Contains("search="))return Task.FromResult("[{\"id\":123,\"path_with_namespace\":\"group/game\"}]");
   if(url.Contains("/commits?")) {Verify(url.Contains("release%2Fstable"),"branch name encoded");return Task.FromResult("[{\"id\":\""+sha+"\"}]");}
   Verify(url.EndsWith("?ref="+sha),"GitGud manifest pinned to SHA");
   return Task.FromResult("{\"game\":{\"repository\":\"https://gitgud.io/group/game\",\"version\":\"1.2.0\"}}");
  }).GetAwaiter().GetResult();Verify(result.Comparison==1 && result.Url.EndsWith("?sha="+sha),"GitGud branch and archive use identical commit");
  Verify(SessionTools.Sha(save)==saveHash && SessionTools.Sha(config)==configHash,"update checks preserve player saves and settings");
 }
}
