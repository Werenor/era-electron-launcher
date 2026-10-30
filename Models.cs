using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal static class LauncherPaths {
    internal static string Home = AppDomain.CurrentDomain.BaseDirectory;
    internal static string UserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortArcadeLauncher");
    internal static string Sessions { get { return Path.Combine(UserRoot, "sessions"); } }
    internal static string Config { get { return Path.Combine(UserRoot, "launcher.json"); } }
    internal static string Resolve(string path) { return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Home, path)); }
}
public sealed class EngineInstall {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Directory { get; set; }
    public string Version { get; set; }
    public string ArchiveHash { get; set; }
    public override string ToString() { return Name + (String.IsNullOrEmpty(Version) ? "" : " · " + Version); }
}
public sealed class GameProfile {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Directory { get; set; }
    public string EngineId { get; set; }
    public string ModeId { get; set; }
    public bool CustomWindow { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int FontSize { get; set; }
    public bool AutoMax { get; set; }
    public string LastPlayed { get; set; }
    public GameProfile() { Width = 1200; Height = 1000; FontSize = 16; ModeId = "standard"; }
    public override string ToString() { return Name; }
    public GameProfile Copy() { return (GameProfile)MemberwiseClone(); }
}
public sealed class LauncherConfig {
    public int Schema { get; set; }
    public string SelectedId { get; set; }
    public List<GameProfile> Profiles { get; set; }
    public List<EngineInstall> Engines { get; set; }
    public string PathBaseDirectory { get; set; }
    public List<string> KnownLauncherDirectories { get; set; }
    [ScriptIgnore] public List<string> PathNotices { get; set; }
    public LauncherConfig() { Schema = 1; Profiles = new List<GameProfile>(); Engines = new List<EngineInstall>();
        KnownLauncherDirectories = new List<string>(); PathNotices = new List<string>(); }
}
internal sealed class EngineMode {
    internal string Id, Name;
    internal bool KeyState;
    public override string ToString() { return Name; }
}
internal static class LauncherStore {
    internal static JavaScriptSerializer Json = new JavaScriptSerializer();
    internal static bool IsGame(string path) {
        return System.IO.Directory.Exists(path) && (File.Exists(Path.Combine(path, "ere", "main.js")) ||
            (File.Exists(Path.Combine(path, "era.bundle.js")) && File.Exists(Path.Combine(path, "main.bundle.js")) && File.Exists(Path.Combine(path, "static.json"))));
    }
    internal static void Validate(GameProfile profile, LauncherConfig config) {
        if (profile == null || String.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("游戏名称不能为空。");
        if (!IsGame(LauncherPaths.Resolve(profile.Directory))) throw new InvalidDataException("游戏目录不存在，或缺少 ere/main.js / 打包入口。");
        
        if (profile.CustomWindow && (profile.Width < 640 || profile.Width > 3840 || profile.Height < 480 || profile.Height > 2160 || profile.FontSize < 10 || profile.FontSize > 32)) throw new InvalidDataException("窗口尺寸或字号超出范围。");
        if (profile.ModeId != "standard" && profile.ModeId != "keystate") throw new InvalidDataException("未知的启动模式。");
    }
    internal static LauncherConfig Load() {
        if (File.Exists(LauncherPaths.Config)) {
            var config = Json.Deserialize<LauncherConfig>(File.ReadAllText(LauncherPaths.Config));
            if (config == null || config.Schema != 1 || config.Profiles == null || config.Engines == null) throw new InvalidDataException("启动器配置格式不支持。原文件未改动：" + LauncherPaths.Config);
            EnginePaths.Normalize(config,true); return config;
        }
        var result = new LauncherConfig();
        if (File.Exists(Path.Combine(LauncherPaths.Home, "engine", "resources", "app.asar"))) {
            var engine = new EngineInstall { Id = "bundled-510", Name = "随包引擎", Directory = Path.Combine(LauncherPaths.Home,"engine") };
            EngineCatalog.Inspect(engine); result.Engines.Add(engine);
            if (IsGame(Path.Combine(LauncherPaths.Home, "game"))) result.Profiles.Add(new GameProfile { Id=Guid.NewGuid().ToString("N"), Name="按键状态验证", Directory=Path.Combine(LauncherPaths.Home,"game"), EngineId=engine.Id, ModeId=EngineCatalog.Adapted(engine)?"keystate":"standard" });
        }
        if (result.Profiles.Count > 0) result.SelectedId = result.Profiles[0].Id;
        EnginePaths.Normalize(result,false); return result;
    }
    internal static void Save(LauncherConfig config) {
        EnginePaths.Normalize(config,false);
        System.IO.Directory.CreateDirectory(LauncherPaths.UserRoot);
        string temp = LauncherPaths.Config + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, Json.Serialize(config), new UTF8Encoding(false));
        if (File.Exists(LauncherPaths.Config)) File.Replace(temp, LauncherPaths.Config, LauncherPaths.Config + ".bak");
        else File.Move(temp, LauncherPaths.Config);
    }
    internal static void Remove(LauncherConfig config, string id) { config.Profiles.RemoveAll(p => p.Id == id); }
}
