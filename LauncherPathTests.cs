using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class LauncherPathTests {
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    static void Engine(string directory,string version) {
        Directory.CreateDirectory(Path.Combine(directory,"resources"));
        File.WriteAllBytes(Path.Combine(directory,"era-electron.exe"),new byte[0]);
        LauncherTests.FakeArchive(Path.Combine(directory,"resources","app.asar"),version);
    }
    static void Game(string directory) {Directory.CreateDirectory(Path.Combine(directory,"ere"));File.WriteAllText(Path.Combine(directory,"ere","main.js"),"module.exports=async()=>{};");}
    static LauncherConfig Config(string engine,string game,string hash) {
        var config=new LauncherConfig(); config.Engines.Add(new EngineInstall {Id="kept-engine",Name="My engine",Directory=engine,ArchiveHash=hash});
        config.Profiles.Add(new GameProfile {Id="kept-game",Name="My game",Directory=game,EngineId="kept-engine"});config.SelectedId="kept-game";return config;
    }
    internal static void Run(string root) {
        var oldHome=LauncherPaths.Home;var oldUser=LauncherPaths.UserRoot;
        try {
            Directory.CreateDirectory(root); string first=Path.Combine(root,"origin"),second=Path.Combine(root,"new-launcher");
            Directory.CreateDirectory(second); Engine(Path.Combine(first,"engine"),"81.1.0");Game(Path.Combine(first,"game"));
            LauncherPaths.Home=first;LauncherPaths.UserRoot=Path.Combine(root,"shared");
            var config=Config("engine","game",null);LauncherStore.Save(config);
            Check(Path.IsPathRooted(config.Engines[0].Directory)&&Path.IsPathRooted(config.Profiles[0].Directory),"persist absolute paths");
            string enginePath=config.Engines[0].Directory,gamePath=config.Profiles[0].Directory;
            LauncherPaths.Home=second; var loaded=LauncherStore.Load();
            Check(loaded.Engines[0].Directory==enginePath&&loaded.Profiles[0].Directory==gamePath,"new launcher folder preserves locations");
            Check(loaded.Profiles[0].EngineId=="kept-engine"&&loaded.SelectedId=="kept-game","preserve profile identity and binding");
            LauncherStore.Save(loaded);Check(File.Exists(LauncherPaths.Config+".bak"),"path migration keeps a configuration backup");
            var legacy=Config("engine","game",null);legacy.PathBaseDirectory=first;
            File.WriteAllText(LauncherPaths.Config,LauncherStore.Json.Serialize(legacy));loaded=LauncherStore.Load();
            Check(loaded.Engines[0].Directory==enginePath&&loaded.Profiles[0].Directory==gamePath,"old relative entries honor recorded origin");

            string movedRoot=Path.Combine(root,"move-case"),origin=Path.Combine(movedRoot,"old","engine"),moved=Path.Combine(movedRoot,"new","engine");
            Engine(origin,"82.2.0");string hash=SessionTools.Sha(Path.Combine(origin,"resources","app.asar"));
            Directory.CreateDirectory(Path.GetDirectoryName(moved));Directory.Move(origin,moved);
            LauncherPaths.Home=Path.Combine(movedRoot,"launcher");Directory.CreateDirectory(LauncherPaths.Home);
            config=Config(origin,gamePath,hash);EnginePaths.Normalize(config,true);
            Check(config.Engines[0].Directory==moved&&config.PathNotices.Count==1,"unique unchanged engine is relocated");
            config.KnownLauncherDirectories.Add(Path.GetDirectoryName(moved)+Path.DirectorySeparatorChar);
            config.KnownLauncherDirectories.Add(Path.GetDirectoryName(moved));
            Check(EnginePaths.Matches(config,config.Engines[0]).Count==1,"trailing separators must not create false ambiguity");
            Check(config.Profiles[0].EngineId=="kept-engine","automatic repair preserves every engine binding");
            string duplicate=Path.Combine(movedRoot,"another","engine");Engine(duplicate,"82.2.0");
            config=Config(origin,gamePath,hash);EnginePaths.Normalize(config,true);
            Check(config.Engines[0].Directory==origin&&EnginePaths.Matches(config,config.Engines[0]).Count==2,"ambiguous matches must not silently choose");
            config=Config(origin,gamePath,null);EnginePaths.Normalize(config,true);
            Check(config.Engines[0].Directory==origin,"missing identity must not infer engine from version or name");

            string legacyRoot=Path.Combine(root,"legacy-no-origin"),legacyHome=Path.Combine(legacyRoot,"old-copy"),freshHome=Path.Combine(legacyRoot,"new-copy");
            Engine(Path.Combine(legacyHome,"engine"),"83.3.0");Directory.CreateDirectory(freshHome);
            string legacyHash=SessionTools.Sha(Path.Combine(legacyHome,"engine","resources","app.asar"));
            LauncherPaths.Home=freshHome;config=Config("engine",gamePath,legacyHash);EnginePaths.Normalize(config,true);
            Check(config.Engines[0].Directory==Path.Combine(legacyHome,"engine"),"old shared relative config can recover unique known engine");

            Engine(moved,"84.4.0");config=Config(moved,gamePath,hash);EnginePaths.Normalize(config,true);
            Check(config.Engines[0].Directory==moved,"in-place update stays in selected installation");
            EngineCatalog.Inspect(config.Engines[0]);Check(config.Engines[0].Version=="84.4.0","updated engine metadata is re-read");
            GameSession.ValidateEngine(config.Engines[0],BuildInfo.Modes[0]);
            bool incompatible=false;try {GameSession.ValidateEngine(config.Engines[0],BuildInfo.Modes[1]);}catch(IOException error){incompatible=error.Message.Contains("标准输入");}
            Check(incompatible,"new engine cannot silently receive an unsupported key-state adapter");
            config.Profiles[0].ModeId="keystate";
            using(var form=new LauncherForm(config)) {
                var modes=(ComboBox)typeof(LauncherForm).GetField("modes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(((EngineMode)modes.SelectedItem).Id=="keystate","UI must not silently downgrade saved input mode");
                var tabs=(TabControl)typeof(LauncherForm).GetField("tabs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(tabs.TabPages.Count==4&&tabs.TabPages[3].Text=="关于","About must be available without selecting a game");
                var license=tabs.TabPages[3].Controls.Find("Launcher.LICENSE",true).OfType<TextBox>().Single();
                Check(license.ReadOnly&&license.Text.StartsWith("                    GNU GENERAL PUBLIC LICENSE")&&license.Text.Contains("END OF TERMS AND CONDITIONS"),"About includes the full read-only embedded license");
                Check(license.Text.Contains("\r\n"),"Windows legal text must preserve visible line breaks");
                Check(tabs.TabPages[3].Controls.Find("Launcher.NOTICES",true).OfType<TextBox>().Single().Text.Contains("gitgud.io/umaera/engine/era-electron"),"third-party attribution is available offline");
            }
            bool actionable=false;try {EngineCatalog.Executable(origin);}catch(IOException error){actionable=error.Message.Contains("重新定位");}
            Check(actionable,"missing directory gets an actionable message before enumeration");
        } finally {LauncherPaths.Home=oldHome;LauncherPaths.UserRoot=oldUser;}
    }
}
