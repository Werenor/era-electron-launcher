using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Shared configuration must not interpret old relative paths against whichever
// copy of the launcher happens to be running today.
internal static class EnginePaths {
    static string Canonical(string value) {
        string full=Path.GetFullPath(value);
        return full==Path.GetPathRoot(full) ? full : full.TrimEnd('\\','/');
    }
    internal static string Absolute(string value, string origin) {
        if (String.IsNullOrWhiteSpace(value)) return value;
        return Canonical(Path.IsPathRooted(value) ? value : Path.Combine(origin, value));
    }
    internal static string Display(string value) {
        try { return LauncherPaths.Resolve(value); } catch { return value ?? "未设置目录"; }
    }
    internal static bool Present(string directory) {
        try {
            if (!Directory.Exists(directory)) return false;
            EngineCatalog.Executable(directory);
            return (File.Exists(Path.Combine(directory,"resources","app.asar")) ||
                 File.Exists(Path.Combine(directory,"resources","app","package.json")));
        } catch { return false; }
    }
    static bool Fingerprint(string value) {
        return value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
    }
    static IEnumerable<string> Roots(LauncherConfig config) {
        var roots = new List<string> { LauncherPaths.Home, config.PathBaseDirectory };
        roots.AddRange(config.KnownLauncherDirectories ?? new List<string>());
        foreach (var path in config.Engines.Select(e=>e.Directory).Concat(config.Profiles.Select(p=>p.Directory))) {
            try { if (Path.IsPathRooted(path)) roots.Add(Path.GetDirectoryName(path)); } catch { }
        }
        try { roots.Add(Path.GetDirectoryName(Path.GetFullPath(LauncherPaths.Home.TrimEnd(Path.DirectorySeparatorChar)))); } catch { }
        return roots.Where(r=>!String.IsNullOrWhiteSpace(r)).Select(r=>Path.GetFullPath(r))
            .Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
    }
    internal static List<string> Matches(LauncherConfig config, EngineInstall engine) {
        string hash = Fingerprint(engine.ArchiveHash) ? engine.ArchiveHash :
            engine.Id == "bundled-510" ? BuildInfo.BaseHash : null;
        var matches = new List<string>();
        if (hash == null) return matches; // Version or folder name alone is not identity.
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots(config)) {
            candidates.Add(root); candidates.Add(Path.Combine(root,"engine"));
            try {
                // One bounded level in known locations; never scan an entire disk.
                if (!Directory.Exists(root) || root.TrimEnd('\\') == Path.GetPathRoot(root).TrimEnd('\\')) continue;
                foreach (string child in Directory.EnumerateDirectories(root).Take(64)) {
                    candidates.Add(child); candidates.Add(Path.Combine(child,"engine"));
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        foreach (var candidate in candidates.Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)) {
            if (!Present(candidate)) continue;
            try {
                string archive = Path.Combine(candidate,"resources","app.asar");
                if (!File.Exists(archive) || !String.Equals(SessionTools.Sha(archive),hash,StringComparison.OrdinalIgnoreCase)) continue;
                var found = new EngineInstall { Directory=candidate }; EngineCatalog.Inspect(found);
                matches.Add(Path.GetFullPath(candidate));
            } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        }
        return matches;
    }
    internal static bool Recover(LauncherConfig config, EngineInstall engine) {
        if (Present(engine.Directory)) return true;
        var matches = Matches(config,engine);
        if (matches.Count != 1) return false;
        engine.Directory = matches[0]; EngineCatalog.Inspect(engine);
        config.PathNotices.Add("已恢复引擎路径：" + engine.Directory);
        return true;
    }
    internal static void Normalize(LauncherConfig config, bool recover) {
        string origin = String.IsNullOrWhiteSpace(config.PathBaseDirectory) ? LauncherPaths.Home : config.PathBaseDirectory;
        var history = new List<string>(config.KnownLauncherDirectories ?? new List<string>());
        history.Insert(0,Canonical(origin)); history.Insert(0,Canonical(LauncherPaths.Home));
        config.KnownLauncherDirectories = history.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        foreach (var engine in config.Engines) engine.Directory = Absolute(engine.Directory,origin);
        foreach (var profile in config.Profiles) profile.Directory = Absolute(profile.Directory,origin);
        config.PathBaseDirectory = Canonical(LauncherPaths.Home);
        if (recover) foreach (var engine in config.Engines) if (!Present(engine.Directory)) Recover(config,engine);
    }
}
