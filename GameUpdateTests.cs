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
  Func<string,Task<string>> fetch=url=>Task.FromResult("{\"tag_name\":\"v1.1.0\",\"html_url\":\"https://github.com/Werenor/game-update-fixture/releases/tag/v1.1.0\",\"body\":\"Update notes\"}");
  var result=GameUpdates.Check(root,fetch).GetAwaiter().GetResult();Verify(result.Comparison==1 && result.Version=="v1.1.0","readonly release check");
  bool rejected=false;try {GameUpdates.Check(root,url=>Task.FromResult("{\"tag_name\":\"v2.0.0\",\"html_url\":\"https://example.org/download\"}")).GetAwaiter().GetResult();}catch(IOException){rejected=true;}Verify(rejected,"unrelated release links rejected");
  File.WriteAllText(declaration,"{\"game\":{\"repository\":\"https://gitgud.io/group/game\",\"version\":\"1.0.0\"}}");
  result=GameUpdates.Check(root,url=>Task.FromResult(url.Contains("search=")?"[{\"id\":123,\"path_with_namespace\":\"group/game\"}]":"{\"tag_name\":\"1.2.0\",\"_links\":{\"self\":\"https://gitgud.io/group/game/-/releases/1.2.0\"}}")).GetAwaiter().GetResult();Verify(result.Comparison==1,"GitGud project identity and release check");
  Verify(SessionTools.Sha(save)==saveHash && SessionTools.Sha(config)==configHash,"update checks preserve player saves and settings");
 }
}
