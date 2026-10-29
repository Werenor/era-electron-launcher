using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
internal static class LauncherTests {
    static void Verify(bool value, string text) { if (!value) throw new Exception(text); }
    static void FakeArchive(string file, string version) {
        byte[] package = Encoding.UTF8.GetBytes("{\"version\":\"" + version + "\"}");
        byte[] json = Encoding.UTF8.GetBytes(LauncherStore.Json.Serialize(new { files = new Dictionary<string, object> { { "package.json", new { size = package.Length, offset = "0" } } } }));
        int payload = 4 + json.Length, padded = (payload + 3) & ~3;
        using (var w = new BinaryWriter(File.Create(file))) { w.Write(4); w.Write(4 + padded); w.Write(padded); w.Write(json.Length); w.Write(json); w.Write(new byte[padded - payload]); w.Write(package); }
    }
    internal static int Run(string report) {
        string originalRoot = LauncherPaths.UserRoot, originalHome = LauncherPaths.Home;
        string test = Path.Combine(Path.GetTempPath(), "ere-launcher-test-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(test); LauncherPaths.UserRoot = Path.Combine(test, "user"); LauncherPaths.Home = test;
            Directory.CreateDirectory(Path.Combine(test,"game","ere")); File.WriteAllText(Path.Combine(test,"game","ere","main.js"), "module.exports=async()=>{};");
            var config = LauncherStore.Load(); Verify(config.Profiles.Count == 0 && config.Engines.Count == 0, "clean public first run");
            string engineDir = Path.Combine(test,"engine"); Directory.CreateDirectory(Path.Combine(engineDir,"resources")); File.WriteAllBytes(Path.Combine(engineDir,"era-electron.exe"),new byte[0]); FakeArchive(Path.Combine(engineDir,"resources","app.asar"),"88.3.7");
            var engine = new EngineInstall { Id="test", Name="Other version", Directory=engineDir }; EngineCatalog.Inspect(engine); Verify(engine.Version=="88.3.7" && !EngineCatalog.Adapted(engine),"unrestricted engine detection");
            GameSession.ValidateEngine(engine,BuildInfo.Modes[0]); config.Engines.Add(engine);
            config.Profiles.Add(new GameProfile { Id=Guid.NewGuid().ToString("N"), Name="测试游戏", Directory="game", EngineId=engine.Id }); config.SelectedId=config.Profiles[0].Id;
            LauncherStore.Validate(config.Profiles[0],config); LauncherStore.Save(config);
            var roundtrip = LauncherStore.Load(); Verify(roundtrip.Profiles[0].Name=="测试游戏", "profile round trip");
            var copy=roundtrip.Profiles[0].Copy(); copy.Width=1400; Verify(roundtrip.Profiles[0].Width==1200,"copy isolation");
            copy.Width=10; bool rejected=false; try { LauncherStore.Validate(copy,roundtrip); } catch(InvalidDataException) { rejected=true; } Verify(rejected,"reject invalid settings");
            config.Profiles.Add(new GameProfile { Id=Guid.NewGuid().ToString("N"), Name="另一个游戏", Directory="game", EngineId=engine.Id });
            using(var form=new LauncherForm(config)) {
                var name=(TextBox)typeof(LauncherForm).GetField("name",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                name.Text="自动保存测试"; var wait=System.Diagnostics.Stopwatch.StartNew();
                while(wait.ElapsedMilliseconds<700) { Application.DoEvents(); Thread.Sleep(10); }
                Verify(LauncherStore.Load().Profiles[0].Name=="自动保存测试","settings auto-save without a button");
                name.Text="快速切换前的编辑";
                var list=(ListBox)typeof(LauncherForm).GetField("profiles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                list.SelectedIndex=1;
                Verify(((GameProfile)list.Items[0]).Name=="快速切换前的编辑" && ((GameProfile)list.Items[1]).Name=="另一个游戏","fast selection preserves both profiles");
            }
            string output=Path.Combine(test,"repacked.asar");
            using(var archive=new EngineArchive(Path.Combine(engineDir,"resources","app.asar"))) archive.Write(output,new Dictionary<string,byte[]> { {"extra.txt",Encoding.UTF8.GetBytes("roundtrip")} });
            using(var archive=new EngineArchive(output)) Verify(Encoding.UTF8.GetString(archive.FileBytes("extra.txt"))=="roundtrip","ASAR writer integrity");
            string profile=Path.Combine(test,"profile"); EngineAdapter.PreparePreferences(profile,Path.Combine(test,"game"));
            var prefs=LauncherStore.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(profile,"config.json"))); Verify(Convert.ToString(prefs["last"])==Path.Combine(test,"game"),"generic engine game routing");
            LauncherStore.Remove(roundtrip,roundtrip.Profiles[0].Id); Verify(Directory.Exists(Path.Combine(test,"game")) && roundtrip.Profiles.Count==0,"removal preserves game files");
            Verify(GameSession.GameMutexName(test)==GameSession.GameMutexName(test.ToUpperInvariant()),"same game lock normalization");
            SessionTools.CleanupStale(); string session=Path.Combine(LauncherPaths.Sessions,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(session); File.WriteAllText(Path.Combine(session,".owner"),"PortArcade-keystate-session-v1");
            using(File.Open(Path.Combine(session,".session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) Verify(!SessionTools.Cleanup(session),"retain active session");
            string busy=Path.Combine(session,"busy.exe"); File.WriteAllText(busy,"test");
            using(File.Open(busy,FileMode.Open,FileAccess.Read,FileShare.None)) Verify(!SessionTools.Cleanup(session) && SessionTools.Owned(session),"locked payload retains ownership for next cleanup");
            Verify(SessionTools.Cleanup(session),"remove finished session");
            File.WriteAllText(report,"PASS clean first run; unrestricted engine registration; standard launch; config persistence; auto-save; ASAR read/write; game routing; profile copy; non-destructive removal; session ownership\n");
            return 0;
        } catch(Exception e) { File.WriteAllText(report,e.ToString()); return 1; }
        finally {
            LauncherPaths.UserRoot=originalRoot; LauncherPaths.Home=originalHome;
            if(Directory.Exists(test) && Path.GetDirectoryName(test).TrimEnd('\\')==Path.GetTempPath().TrimEnd('\\') && Path.GetFileName(test).StartsWith("ere-launcher-test-")) Directory.Delete(test,true);
        }
    }
}
