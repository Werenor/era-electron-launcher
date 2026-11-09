using System;
using System.IO;
using System.Web.Script.Serialization;
internal static class ManifestFormatTests {
 static string Encode(object value){return new JavaScriptSerializer().Serialize(value);}
 static void Reject(string json,string path){try{ManifestFormat.Game(json);}catch(IOException e){if(e.Message.Contains(path))return;throw;}throw new Exception("Accepted invalid declaration: "+json);}
 internal static void Run(string root){
  Directory.CreateDirectory(root);
  string old="{\"plugins\":[{\"id\":\"keys\",\"source\":\"github\",\"repository\":\"Owner/keys\",\"fallback\":true}]}";
  string modern="{\"schemaVersion\":1,\"game\":{\"repository\":\"https://gitgud.io/group/game\",\"version\":\"0.8.1\",\"branch\":\"release\"},\"plugins\":[{\"id\":\"keys\",\"repository\":\"https://github.com/Owner/keys.git/\",\"fallback\":true}]}";
  File.WriteAllText(Path.Combine(root,"ere.launcher.json"),old);
  var profile=new GameProfile {Directory=root};var before=GamePlugins.Check(profile)[0];
  profile.DisabledPlugins=new System.Collections.Generic.List<string>{before.Key};
  File.WriteAllText(Path.Combine(root,"ere.launcher.json"),modern);
  var after=GamePlugins.Check(profile)[0];
  if(before.Key!=after.Key||GamePlugins.DirectoryOf(before)!=GamePlugins.DirectoryOf(after)||after.Enabled)throw new Exception("Manifest migration lost plugin cache/disabled choice");
  var game=GameUpdates.Read(root);if(game.Branch!="release"||game.Version!="0.8.1"||game.Host!="gitgud.io")throw new Exception("new game declaration");
  string package=Encode(new {schemaVersion=1,id="keys",repository="https://github.com/Owner/keys",version="99.0.0",main="main.js",prepend="",engineVersions=new[]{"any descriptive version"},files=new[]{new {source="keys.js",target="keys.js",external=false,sha256=new string('a',64)}},replacements=new[]{new {find="anchor",replace=""}}});
  var plugin=ManifestFormat.Plugin(package);if((string)plugin["repository"]!="Owner/keys"||(string)plugin["source"]!="github")throw new Exception("new package identity");
  // Mixed new/legacy game and installed-package formats share the same cache.
  string cache=GamePlugins.DirectoryOf(before);Directory.CreateDirectory(cache);
  var body=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(package);
  body["files"]=new object[0];File.WriteAllText(Path.Combine(cache,"plugin.json"),Encode(body));
  File.WriteAllText(Path.Combine(root,"ere.launcher.json"),old);
  if(!GamePlugins.Check(profile)[0].Usable)throw new Exception("old game cannot read new package");
  body.Remove("schemaVersion");body["source"]="github";body["repository"]="Owner/keys";
  File.WriteAllText(Path.Combine(cache,"plugin.json"),Encode(body));
  File.WriteAllText(Path.Combine(root,"ere.launcher.json"),modern);
  if(!GamePlugins.Check(profile)[0].Usable)throw new Exception("new game cannot read old package");

  // New packages can be loaded for games still using old declarations.
  ManifestFormat.Plugin("{\"id\":\"keys\",\"source\":\"github\",\"repository\":\"Owner/keys\",\"version\":\"1.0.0\"}");
  Reject(modern.Replace("\"schemaVersion\":1","\"schemaVersion\":2"),"schemaVersion");
  Reject(modern.Replace("\"schemaVersion\":1","\"schemaVersion\":\"1\""),"schemaVersion");
  Reject(modern.Replace("\"fallback\":true","\"fallback\":\"true\""),"fallback");
  Reject(modern.Replace("\"plugins\":[", "\"typo\":1,\"plugins\":["),"typo");
  Reject(modern.Replace("https://github.com/Owner/keys.git/","Owner/keys"),"repository");
  Reject(modern.Replace("https://github.com/Owner/keys.git/","https://github.com/Owner/keys/releases"),"repository");
  Reject(modern.Replace("https://github.com/Owner/keys.git/","https://github.com/Owner/../keys/repo"),"repository");
  Reject("{\"plugins\":{}}","plugins");
  Reject("{\"plugins\":[{\"id\":\"a\",\"source\":\"github\",\"repository\":\"o/r\"},{\"id\":\"a\",\"source\":\"github\",\"repository\":\"o/r\"}]}","重复");
  try{ManifestFormat.Plugin(package.Replace(new string('a',64),"bad"));throw new Exception("invalid hash accepted");}catch(IOException){}
 }
}
