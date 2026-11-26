using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class LauncherUpdater {
    const string WorkerName = "EreDeck-update.exe";
    const long PackageLimit = 16 * 1024 * 1024;
    static readonly string[] Components = {
        "KeyStateProbe.exe", "patches/keystate.cjs", "patches/launcher-window.js", "StartGame.exe"
    };

    static string UpdatesRoot { get { return Path.Combine(LauncherPaths.UserRoot, "launcher-updates"); } }
    static string SidePath(string file) { return file + ".eredeck-" + Guid.NewGuid().ToString("N"); }
    static string Quote(string value) { return "\"" + value + "\""; }
    sealed class RollbackException : IOException {
        internal RollbackException(string message, Exception cause) : base(message, cause) {}
    }

    internal static async Task PrepareAndStart(LauncherRelease release, Version version) {
        string stage = Path.Combine(UpdatesRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try {
            string checksum = Path.Combine(stage, "SHA256SUMS.txt");
            string archive = Path.Combine(stage, "package.zip");
            await Downloads.DownloadLauncherFile(release.ChecksumUrl, checksum, 65536);
            string expected = ExpectedHash(File.ReadAllLines(checksum), release.PackageName);
            await Downloads.DownloadLauncherFile(release.PackageUrl, archive, PackageLimit);
            await Task.Run(() => {
                if (!String.Equals(SessionTools.Sha(archive), expected, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("更新包 SHA256 校验失败。");
                Extract(archive, stage, version);
            });
            string worker = Path.Combine(stage, WorkerName);
            File.Copy(Application.ExecutablePath, worker);
            using (var parent = Process.GetCurrentProcess())
            using (var child = Process.Start(new ProcessStartInfo(worker) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage,
                Arguments = "--apply-update " + parent.Id + " " + parent.StartTime.ToUniversalTime().Ticks
                    + " " + Quote(Application.ExecutablePath)
            })) {
                if (child == null) throw new IOException("无法启动更新程序。");
            }
        } catch {
            try { Directory.Delete(stage, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static string ExpectedHash(string[] lines, string name) {
        var matches = lines.Select(line => line.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1].Trim().TrimStart('*') == name).ToArray();
        if (matches.Length != 1 || matches[0][0].Length != 64 || !matches[0][0].All(Uri.IsHexDigit))
            throw new IOException("发布附件缺少更新包的有效 SHA256。");
        return matches[0][0];
    }

    internal static void Extract(string archive, string stage, Version version) {
        using (var zip = ZipFile.OpenRead(archive)) {
            long total = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries) {
                string name = entry.FullName.Replace('\\', '/');
                if (!Components.Contains(name) || !seen.Add(name)
                    || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                    throw new IOException("更新包包含未预期或重复的文件。");
                total += entry.Length;
                if (total > PackageLimit) throw new IOException("解压后的更新包超过大小限制。");
                string file = Path.Combine(stage, name);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                entry.ExtractToFile(file);
            }
            if (seen.Count != Components.Length) throw new IOException("更新包缺少启动组件。");
        }
        var exe = FileVersionInfo.GetVersionInfo(Path.Combine(stage, "StartGame.exe"));
        if (exe.FileMajorPart != version.Major || exe.FileMinorPart != version.Minor ||
            exe.FileBuildPart != version.Build)
            throw new IOException("更新包中的程序版本与发布版本不一致。");
    }

    // Keep each backup beside its destination so File.Replace stays on one volume.
    sealed class PendingFile {
        internal string Target, Candidate, Backup;
        internal bool Installed, Replaced;
    }

    internal static void ReplacePackage(string stage, string executable) {
        string home = Path.GetDirectoryName(executable);
        var pending = new List<PendingFile>();
        bool complete = false;
        try {
            foreach (string name in Components) {
                string target = name == "StartGame.exe" ? executable : Path.Combine(home, name);
                var file = new PendingFile { Target = target, Candidate = SidePath(target), Backup = SidePath(target) };
                pending.Add(file);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(stage, name), file.Candidate);
            }
            foreach (var file in pending) {
                if (File.Exists(file.Target)) {
                    File.Replace(file.Candidate, file.Target, file.Backup);
                    file.Replaced = true;
                } else {
                    File.Move(file.Candidate, file.Target);
                }
                file.Installed = true;
            }
            complete = true;
        } catch (Exception error) {
            var failed = new List<string>();
            foreach (var file in pending.AsEnumerable().Reverse()) {
                if (!file.Installed && !File.Exists(file.Backup)) continue;
                try {
                    if (file.Replaced || File.Exists(file.Backup)) {
                        if (File.Exists(file.Target)) File.Replace(file.Backup, file.Target, null);
                        else File.Move(file.Backup, file.Target);
                    }
                    else File.Delete(file.Target);
                } catch {
                    failed.Add(File.Exists(file.Backup) ? file.Backup : file.Target);
                }
            }
            if (failed.Count != 0)
                throw new RollbackException("部分文件无法恢复。请保留备份并手动修复：\n" + String.Join("\n", failed), error);
            throw;
        } finally {
            foreach (var file in pending) {
                TryDelete(file.Candidate);
                if (complete) TryDelete(file.Backup);
            }
        }
    }

    static void TryDelete(string file) {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static bool OwnedStage(string stage) {
        Guid id;
        return Guid.TryParseExact(Path.GetFileName(stage), "N", out id)
            && String.Equals(Path.GetDirectoryName(stage), UpdatesRoot, StringComparison.OrdinalIgnoreCase);
    }

    static Mutex UpdateGate(string executable) {
        using (var hash = System.Security.Cryptography.SHA256.Create()) {
            byte[] key = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(executable.ToUpperInvariant()));
            return new Mutex(false, @"Local\EreDeck.Update." + Convert.ToBase64String(key).Replace('/', '_'));
        }
    }

    static bool TryEnter(Mutex gate) {
        try { return gate.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    internal static bool UpdateInProgress() {
        using (var gate = UpdateGate(Application.ExecutablePath)) {
            if (!TryEnter(gate)) return true;
            gate.ReleaseMutex();
            return false;
        }
    }

    internal static int Apply(string[] args) {
        string stage = Path.GetDirectoryName(Application.ExecutablePath);
        if (!OwnedStage(stage) || Path.GetFileName(Application.ExecutablePath) != WorkerName) return 1;
        string executable = Path.GetFullPath(args[3]);
        try {
            try {
                using (var parent = Process.GetProcessById(Int32.Parse(args[1]))) {
                    if (parent.StartTime.ToUniversalTime().Ticks == Int64.Parse(args[2])
                        && !parent.WaitForExit(30000))
                        throw new IOException("启动器尚未退出，更新已停止。");
                }
            } catch (ArgumentException) { } // The parent already exited.

            using (var gate = UpdateGate(executable)) {
                if (!TryEnter(gate)) throw new IOException("此目录已有更新正在安装。");
                try {
                    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable))) {
                        using (process) {
                            string path;
                            try { path = process.MainModule.FileName; }
                            catch (System.ComponentModel.Win32Exception) { continue; }
                            catch (InvalidOperationException) { continue; }
                            if (String.Equals(path, executable, StringComparison.OrdinalIgnoreCase))
                                throw new IOException("还有一个 EreDeck 窗口正在运行，请关闭后重试。");
                        }
                    }
                    ReplacePackage(stage, executable);
                } finally { gate.ReleaseMutex(); }
            }
        } catch (Exception error) {
            MessageBox.Show("安装更新失败：\n" + error.Message, "EreDeck 更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (!(error is RollbackException)) Restart(executable, stage);
            return 1;
        }
        return Restart(executable, stage) ? 0 : 1;
    }

    static bool Restart(string executable, string stage) {
        try {
            using (var worker = Process.GetCurrentProcess())
            using (var restarted = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable),
                Arguments = "--clean-update " + worker.Id + " " + Quote(stage)
            })) { return restarted != null; }
        } catch (Exception error) {
            MessageBox.Show("请手动打开 EreDeck：\n" + error.Message, "EreDeck 更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    internal static void Clean(string[] args) {
        string stage = Path.GetFullPath(args[2]);
        if (!OwnedStage(stage)) return;
        try {
            try {
                using (var worker = Process.GetProcessById(Int32.Parse(args[1]))) {
                    if (!String.Equals(worker.MainModule.FileName, Path.Combine(stage, WorkerName), StringComparison.OrdinalIgnoreCase)
                        || !worker.WaitForExit(5000)) return;
                }
            } catch (ArgumentException) { }
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
          catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
    }
}
