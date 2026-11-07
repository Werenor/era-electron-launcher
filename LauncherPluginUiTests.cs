using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
internal static class LauncherPluginUiTests {
 static object Field(object form,string name){return typeof(LauncherForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
 static void Verify(bool value,string text){if(!value)throw new Exception(text);}
 internal static void Run(string root) {
  Directory.CreateDirectory(Path.Combine(root,"csv"));Directory.CreateDirectory(Path.Combine(root,"ere"));
  File.WriteAllText(Path.Combine(root,"ere","main.js"),"module.exports=async()=>{};");
  File.WriteAllText(Path.Combine(root,"csv","GameBase.csv"),"游戏名称,测试\n版本,0001\n");
  Verify(GameInformation.Version(root)=="0.001","game version follows game base");Verify(GameInformation.Version(Path.Combine(root,"unknown"))=="","unknown version blank");
  File.WriteAllText(Path.Combine(root,"ere.launcher.json"),"{\"plugins\":[{\"id\":\"ui-fixture\",\"source\":\"github\",\"repository\":\"Werenor/ui-fixture\",\"fallback\":true}]}");
  string cache=Path.Combine(LauncherPaths.UserRoot,"plugins","github","Werenor","ui-fixture","ui-fixture");Directory.CreateDirectory(cache);
  File.WriteAllText(Path.Combine(cache,"plugin.json"),"{\"id\":\"ui-fixture\",\"source\":\"github\",\"repository\":\"Werenor/ui-fixture\",\"files\":[]}");
  var config=new LauncherConfig();
  var old=new GameProfile {Id="old",Name="old",Directory=root};var recent=new GameProfile {Id="recent",Name="recent",Directory=root,LastPlayed="2026-11-06 12:00:00"};
  config.Profiles.Add(old);config.Profiles.Add(recent);config.SelectedId=recent.Id;
  using(var form=new LauncherForm(config)) {
   var list=(ListBox)Field(form,"profiles");Verify(((GameProfile)list.Items[0]).Id=="recent","recent games first");
   var tabs=(TabControl)Field(form,"tabs");var plugins=(TabPage)Field(form,"pluginPage");tabs.SelectedTab=plugins;
   typeof(LauncherForm).GetMethod("CheckCurrentPlugins",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{false});
   var view=(ListView)Field(form,"pluginList");var handle=view.Handle;Verify(view.Items.Count==1 && view.Items[0].Checked,"plugin check populates per-game tab");
   view.Items[0].Checked=false;Verify(recent.DisabledPlugins!=null && recent.DisabledPlugins.Count==1 && (old.DisabledPlugins==null||old.DisabledPlugins.Count==0),"per-game disable isolation");
   var saved=LauncherStore.Load();Verify(saved.Profiles.Find(p=>p.Id=="recent").DisabledPlugins!=null && saved.Profiles.Find(p=>p.Id=="recent").DisabledPlugins.Count==1,"plugin choice persists");
   list.SelectedItem=old;Verify(view.Items.Count==1 && view.Items[0].Checked,"switching game restores independent plugin choice");
   var clone=recent.Copy();clone.DisabledPlugins.Clear();Verify(recent.DisabledPlugins!=null && recent.DisabledPlugins.Count==1,"profile copy isolates plugin list");
  }
 }
}
