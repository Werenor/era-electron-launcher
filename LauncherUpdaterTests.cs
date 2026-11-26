using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

internal static class LauncherUpdaterTests {
    internal static void Run(string root) {
        const string archiveName = "EreDeck-v9.9.9-win-x64.zip";
        string hash = new string('a', 64);
        if (LauncherUpdater.ExpectedHash(new[] { hash + "  " + archiveName }, archiveName) != hash)
            throw new Exception("Wrong selected update hash");
        Reject(() => LauncherUpdater.ExpectedHash(new[] { hash + " other.zip" }, archiveName));
        Directory.CreateDirectory(root);
        string archive = Path.Combine(root, "update.zip");
        string input = Path.Combine(root, "input");
        Directory.CreateDirectory(Path.Combine(input, "patches"));
        string[] parts = { "KeyStateProbe.exe", "patches/keystate.cjs", "patches/launcher-window.js" };
        foreach (string part in parts) File.WriteAllText(Path.Combine(input, part), "new");
        File.Copy(Application.ExecutablePath, Path.Combine(input, "StartGame.exe"));
        ZipFile.CreateFromDirectory(input, archive);
        string stage = Path.Combine(root, "stage");
        LauncherUpdater.Extract(archive, stage, typeof(LauncherUpdater).Assembly.GetName().Version);
        Reject(() => LauncherUpdater.Extract(archive, Path.Combine(root, "version"), new Version(99,0,0)));
        string home = Path.Combine(root, "install");
        Directory.CreateDirectory(Path.Combine(home, "patches"));
        var executable = Path.Combine(home, "StartGame.exe");
        File.WriteAllText(executable, "old-exe");
        File.WriteAllText(Path.Combine(home, parts[0]), "old-helper");
        string blocked = Path.Combine(home, parts[1]);
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(home, "player-data"), "preserved");
        Reject(() => LauncherUpdater.ReplacePackage(stage, executable));
        if (File.ReadAllText(Path.Combine(home, parts[0])) != "old-helper" || File.ReadAllText(executable) != "old-exe")
            throw new Exception("Rollback did not restore the original components");
        Directory.Delete(blocked);
        LauncherUpdater.ReplacePackage(stage, executable);
        if (parts.Any(part => File.ReadAllText(Path.Combine(home, part)) != "new")
            || SessionTools.Sha(executable) != SessionTools.Sha(Application.ExecutablePath)
            || File.ReadAllText(Path.Combine(home, "player-data")) != "preserved"
            || Directory.GetFiles(home, "*.eredeck-*", SearchOption.AllDirectories).Length != 0)
            throw new Exception("Update did not install a clean, complete package");
        string bad = Path.Combine(root, "bad.zip");
        using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) zip.CreateEntry("../escape");
        Reject(() => LauncherUpdater.Extract(bad, stage, typeof(LauncherUpdater).Assembly.GetName().Version));
    }

    static void Reject(Action action) {
        try { action(); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        throw new Exception("Invalid or obstructed update was accepted");
    }
}
