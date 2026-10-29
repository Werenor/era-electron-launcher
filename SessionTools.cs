using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
internal static class SessionTools {
    const string Owner = "PortArcade-keystate-session-v1";
    static string Root { get { return LauncherPaths.Sessions; } }
    internal static string Sha(string file) { using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    internal static bool Owned(string dir) {
        string full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(full);
        Guid id;
        return String.Equals(parent, Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(Path.GetFileName(full), "N", out id)
            && File.Exists(Path.Combine(full, ".owner")) && File.ReadAllText(Path.Combine(full, ".owner")) == Owner;
    }
    static bool IsRuntime(Process process, string dir) {
        // Store/package redirection can give equivalent paths different LocalAppData prefixes.
        // The marked session GUID and final engine path identify this run uniquely.
        if(process.ProcessName.IndexOf("era",StringComparison.OrdinalIgnoreCase)<0) return false;
        string suffix = "\\PortArcadeLauncher\\sessions\\" + Path.GetFileName(dir) + "\\engine\\";
        string full = process.MainModule.FileName; string parent = Path.GetDirectoryName(full) + "\\";
        return parent.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(full).Equals(".exe",StringComparison.OrdinalIgnoreCase);
    }
    static bool RuntimeAlive(string dir) {
        foreach (var process in Process.GetProcesses()) using (process) {
            try { if (IsRuntime(process, dir)) return true; }
            catch { }
        }
        return false;
    }
    internal static bool RuntimeWindowVisible(string dir) {
        foreach (var process in Process.GetProcesses()) using (process) {
            try { if (IsRuntime(process, dir) && process.MainWindowHandle != IntPtr.Zero) return true; }
            catch { }
        }
        return false;
    }
    static void RejectLinks(string dir) {
        if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing linked directory: " + dir);
        foreach (string child in Directory.GetDirectories(dir)) RejectLinks(child);
        foreach (string child in Directory.GetFiles(dir)) if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing linked file: " + child);
    }
    internal static bool Cleanup(string dir) {
        if (!Owned(dir) || RuntimeAlive(dir)) return false;
        try {
            using (File.Open(Path.Combine(dir, ".session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            RejectLinks(dir);
            // Keep ownership proof until every payload file is gone. Windows/AV may
            // temporarily retain an executable after process exit.
            foreach (string child in Directory.GetDirectories(dir)) Directory.Delete(child, true);
            foreach (string file in Directory.GetFiles(dir)) {
                string name=Path.GetFileName(file);
                if(name!=".owner" && name!=".session.lock") File.Delete(file);
            }
            File.Delete(Path.Combine(dir,".session.lock"));
            File.Delete(Path.Combine(dir,".owner"));
            try { Directory.Delete(dir, false); }
            catch { if(Directory.Exists(dir)) File.WriteAllText(Path.Combine(dir,".owner"),Owner); throw; }
            return true;
        } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
    }
    internal static void CleanupStale() {
        Directory.CreateDirectory(Root);
        foreach (string dir in Directory.GetDirectories(Root)) Cleanup(dir);
    }
    internal static void CopyTree(string source, string destination) {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        foreach (string dir in Directory.GetDirectories(source)) CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
    internal static void KillOwnedProcesses(string dir) {
        foreach (var process in Process.GetProcesses()) using (process) {
            try { if (IsRuntime(process, dir)) { process.Kill(); process.WaitForExit(3000); } }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
    }
    internal static void CloseOwnedWindows(string dir) {
        foreach (var process in Process.GetProcesses()) using (process) {
            try { if (IsRuntime(process, dir) && process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow(); }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
    }
    internal static void WaitForGame(Process process, string dir) {
        bool seenWindow = false;
        DateTime missingSince = DateTime.MaxValue;
        while (!process.WaitForExit(250)) {
            process.Refresh();
            if (RuntimeWindowVisible(dir)) { seenWindow = true; missingSince = DateTime.MaxValue; }
            else if (seenWindow) {
                if (missingSince == DateTime.MaxValue) missingSince = DateTime.UtcNow;
                if ((DateTime.UtcNow - missingSince).TotalSeconds >= 3) { KillOwnedProcesses(dir); break; }
            }
        }
        KillOwnedProcesses(dir); // Close only any remaining children from this unique runtime path.
    }
}
