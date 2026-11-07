using System;
using System.IO;
internal static class GamePluginTests {
 internal static void Run(string root) {
  Directory.CreateDirectory(root);string file=Path.Combine(root,"ere.launcher.json");
  string manifest="{\"plugins\":[{\"id\":\"ere-keystate\",\"source\":\"github\",\"repository\":\"Werenor/ere-keystate\"SUFFIX}]}";
  File.WriteAllText(file,manifest.Replace("SUFFIX",""));
  if(GamePlugins.AllowsFallback(root)||GamePlugins.Missing(root).Length==0)throw new Exception("Required plugin must not offer fallback");
  bool blocked=false;try {GamePlugins.Apply(root,Path.Combine(root,"runtime"),s=>{});}catch(IOException){blocked=true;}if(!blocked)throw new Exception("Required plugin did not block");
  File.WriteAllText(file,manifest.Replace("SUFFIX",",\"fallback\":true"));
  if(!GamePlugins.AllowsFallback(root))throw new Exception("Optional fallback missing");GamePlugins.Apply(root,Path.Combine(root,"runtime"),s=>{});
  File.WriteAllText(file,manifest.Replace("SUFFIX",",\"fallback\":true").Replace("Werenor/ere-keystate","../escape"));
  blocked=false;try {GamePlugins.Missing(root);}catch(IOException){blocked=true;}if(!blocked)throw new Exception("Plugin identity traversal not rejected");
 }
}
