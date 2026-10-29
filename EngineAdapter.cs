using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal static class EngineAdapter {
    internal static string ReplaceOnce(string text, string old, string replacement) {
        int index = text.IndexOf(old, StringComparison.Ordinal);
        if (index < 0 || text.IndexOf(old, index + old.Length, StringComparison.Ordinal) >= 0) throw new InvalidDataException("引擎启动通道与适配器不匹配。");
        return text.Substring(0, index) + replacement + text.Substring(index + old.Length);
    }
    internal static void Apply(string runtime, bool keys) {
        string archive = Path.Combine(runtime, "resources", "app.asar"), temporary = archive + ".launcher.tmp";
        if (SessionTools.Sha(archive) != BuildInfo.BaseHash) throw new InvalidDataException("此扩展适配器不适用于所选引擎。");
        const string main = ".webpack/main/index.js";
        using (var source = new EngineArchive(archive)) {
            string text = Encoding.UTF8.GetString(source.FileBytes(main));
            text = ReplaceOnce(text, "let t=u(process.cwd(),\"./game\");", "let t=process.env.PORT_ARCADE_GAME_PATH?f(process.env.PORT_ARCADE_GAME_PATH):u(process.cwd(),\"./game\");");
            text = ReplaceOnce(text, "writeFileSync(join(this.path,\"./ere.config.json\"),JSON.stringify(this.config,null,2)),this.logger.info(`loaded game config in ", "writeFileSync(join(this.path,\"./ere.config.json\"),JSON.stringify(this.config,null,2)),__portArcadeApplyWindow(this),this.logger.info(`loaded game config in ");
            var files = new Dictionary<string, byte[]>();
            if (keys) {
                text = ReplaceOnce(text, "setTrigger(e){this.era.connect(\"setTrigger\",e)}", "enableKeyState(e){return __ereKeyState.enable(e)}getKeyState(e){return __ereKeyState.read(e)}disableKeyState(){return __ereKeyState.disable()}setTrigger(e){this.era.connect(\"setTrigger\",e)}");
                text = ReplaceOnce(text, "resetParams(){this.inputKey&&", "resetParams(){__ereKeyState.disable(\"engine-reset\");this.inputKey&&");
                text = ReplaceOnce(text, "e.on(\"resize\",()=>e.webContents.send(\"engine\",{action:N.resize,arg:e.getContentSize()[1]}));", "__ereKeyState.attach(e);e.on(\"resize\",()=>e.webContents.send(\"engine\",{action:N.resize,arg:e.getContentSize()[1]}));");
                text = "const __ereKeyState = require(\"./keystate.cjs\");\n" + text;
                files[".webpack/main/keystate.cjs"] = File.ReadAllBytes(Path.Combine(LauncherPaths.Home, "patches", "keystate.cjs"));
            }
            text = File.ReadAllText(Path.Combine(LauncherPaths.Home, "patches", "launcher-window.js")) + "\n" + text;
            files[main] = Encoding.UTF8.GetBytes(text); source.Write(temporary, files);
        }
        using (var result = new EngineArchive(temporary)) {
            string text = Encoding.UTF8.GetString(result.FileBytes(main));
            if (!text.Contains("PORT_ARCADE_GAME_PATH") || (keys && !text.Contains("getKeyState(e)"))) throw new InvalidDataException("运行时引擎验证失败。");
        }
        File.Delete(archive); File.Move(temporary, archive); // Only within the launcher's marked temporary runtime.
        if (keys) File.Copy(Path.Combine(LauncherPaths.Home, "KeyStateProbe.exe"), Path.Combine(runtime, "resources", "KeyStateProbe.exe"), true);
    }
    internal static void PreparePreferences(string profileDirectory, string gameDirectory) {
        Directory.CreateDirectory(profileDirectory); string file = Path.Combine(profileDirectory, "config.json");
        var data = File.Exists(file) ? LauncherStore.Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)) : new Dictionary<string, object>();
        data["last"] = gameDirectory;
        File.WriteAllText(file, LauncherStore.Json.Serialize(data), new UTF8Encoding(false));
    }
}
