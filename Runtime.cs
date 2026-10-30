using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

internal sealed class RunResult {
    internal bool Started, Stopped, CleanupComplete;
    internal int ExitCode;
    internal string Error, LogFile;
}
internal sealed class GameSession {
    internal event Action<string> Message;
    internal event Action<string> State;
    internal event Action<RunResult> Finished;
    volatile bool stop;
    StreamWriter writer;
    void Log(string value) {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + value;
        if (writer != null) { writer.WriteLine(line); writer.Flush(); }
        if (Message != null) Message(line);
    }
    void Status(string text) { Log(text); if (State != null) State(text); }
    internal void Stop() { stop = true; }
    void CheckStop() { if (stop) throw new OperationCanceledException(); }
    static void ValidateComponent(string relative,string expected) {
        string path=Path.Combine(LauncherPaths.Home,relative);
        if(!File.Exists(path))throw new IOException("启动组件缺失："+relative+"。请完整解压同一版本的启动器发布包。");
        if(SessionTools.Sha(path)!=expected)throw new IOException("启动组件校验失败："+relative+"。请使用同一完整发布包中的启动器及组件，勿混用不同构建。");
    }
    internal void Start(GameProfile profile, EngineInstall install, EngineMode mode) {
        new Thread(() => Run(profile.Copy(), install, mode)) { IsBackground = false, Name = "game-session" }.Start();
    }
    internal static void ValidateEngine(EngineInstall install, EngineMode mode) {
        string engine = LauncherPaths.Resolve(install.Directory);
        EngineCatalog.Inspect(install);
        if (mode.KeyState && !EngineCatalog.Adapted(install)) throw new IOException("该版本尚未适配 keystate 扩展，请选择标准输入模式。");
        if (EngineCatalog.Adapted(install)) {
            ValidateComponent(Path.Combine("patches","launcher-window.js"),BuildInfo.BridgeHash);
            if (mode.KeyState) {ValidateComponent(Path.Combine("patches","keystate.cjs"),BuildInfo.KeyStateHash);ValidateComponent("KeyStateProbe.exe",BuildInfo.HelperHash);}
        }
    }
    internal static string GameMutexName(string directory) {
        using (var sha = SHA256.Create()) return "Local\\PortArcade.Game." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\').ToLowerInvariant()))).Replace("-", "");
    }
    void Run(GameProfile profile, EngineInstall install, EngineMode mode) {
        var result = new RunResult { ExitCode = -1, CleanupComplete = true };
        string session = null; FileStream sessionLock = null; Mutex gameLock = null; bool acquired = false;
        Process process = null;
        try {
            string logs = Path.Combine(LauncherPaths.UserRoot, "logs"); Directory.CreateDirectory(logs);
            result.LogFile = Path.Combine(logs, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + profile.Id + ".log");
            writer = new StreamWriter(result.LogFile, true, new UTF8Encoding(false));
            string gameDirectory = LauncherPaths.Resolve(profile.Directory);
            if (!LauncherStore.IsGame(gameDirectory)) throw new IOException("游戏目录无效：" + gameDirectory);
            gameLock = new Mutex(false, GameMutexName(gameDirectory));
            try { acquired = gameLock.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("这个游戏目录已有运行中的实例。请先关闭它，避免同时写入同一份存档。");
            Status("正在校验引擎与启动组件…"); ValidateEngine(install, mode); CheckStop();
            SessionTools.CleanupStale();
            session = Path.Combine(LauncherPaths.Sessions, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(session);
            File.WriteAllText(Path.Combine(session, ".owner"), "PortArcade-keystate-session-v1");
            sessionLock = File.Open(Path.Combine(session, ".session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string runtime = Path.Combine(session, "engine");
            string source = LauncherPaths.Resolve(install.Directory);
            Status("正在准备独立引擎副本…");
            CopyTree(source, runtime); CheckStop();
            bool adapted = EngineCatalog.Adapted(install);
            if (adapted) { Status("正在准备运行时扩展…"); EngineAdapter.Apply(runtime, mode.KeyState); }
            else Log("使用原版标准启动；窗口设置由游戏配置决定。");
            string profileDirectory = Path.Combine(LauncherPaths.UserRoot,"instances",profile.Id,"profile");
            EngineAdapter.PreparePreferences(profileDirectory, gameDirectory);
            string working = Path.Combine(session,"work"); Directory.CreateDirectory(working);
            var start = new ProcessStartInfo(EngineCatalog.Executable(runtime)) { WorkingDirectory=working, UseShellExecute=false,
                Arguments="--user-data-dir=\"" + profileDirectory + "\"" };
            start.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
            start.EnvironmentVariables["PORT_ARCADE_GAME_PATH"] = gameDirectory;
            start.EnvironmentVariables.Remove("PORT_ARCADE_WINDOW_JSON");
            if(profile.CustomWindow) start.EnvironmentVariables["PORT_ARCADE_WINDOW_JSON"] = LauncherStore.Json.Serialize(new { width = profile.Width, height = profile.Height, fontSize = profile.FontSize, autoMax = profile.AutoMax });
            Log("游戏：" + gameDirectory); Log("引擎：" + install.Name + " / " + mode.Name); Log("临时会话：" + session);
            process = Process.Start(start); result.Started = true; Status("运行中 · " + profile.Name);
            bool seenWindow = false; DateTime missingSince = DateTime.MaxValue, stoppingSince = DateTime.MaxValue;
            while (!process.WaitForExit(250)) {
                bool visible = SessionTools.RuntimeWindowVisible(session);
                if (visible) { seenWindow = true; missingSince = DateTime.MaxValue; }
                else if (seenWindow) { if (missingSince == DateTime.MaxValue) missingSince = DateTime.UtcNow; if ((DateTime.UtcNow - missingSince).TotalSeconds >= 3) { SessionTools.KillOwnedProcesses(session); break; } }
                if (stop) {
                    if (stoppingSince == DateTime.MaxValue) { stoppingSince = DateTime.UtcNow; Status("正在停止游戏…"); SessionTools.CloseOwnedWindows(session); }
                    if ((DateTime.UtcNow - stoppingSince).TotalSeconds >= 5) { SessionTools.KillOwnedProcesses(session); break; }
                }
            }
            if (process.HasExited) result.ExitCode = process.ExitCode;
            result.Stopped = stop;
        } catch (OperationCanceledException) { result.Stopped = true; Log("启动已取消。"); }
        catch (Exception e) { result.Error = e.Message; Log("错误：" + e); }
        finally {
            Status("正在清理临时会话…");
            if (session != null) {
                SessionTools.KillOwnedProcesses(session);
                string engineLog = Path.Combine(LauncherPaths.UserRoot, "instances", profile.Id, "profile", "logs", "main.log");
                try { if (File.Exists(engineLog)) File.Copy(engineLog, result.LogFile + ".engine.log", true); } catch (IOException) { }
                if (sessionLock != null) sessionLock.Dispose();
                for (int i = 0; i < 20 && Directory.Exists(session); i++) { if (SessionTools.Cleanup(session)) break; Thread.Sleep(250); }
                result.CleanupComplete = !Directory.Exists(session);
                Log(result.CleanupComplete ? "临时会话已清理。" : "文件仍被占用，保留到下次启动清理：" + session);
            }
            if (process != null) process.Dispose();
            if (acquired) gameLock.ReleaseMutex(); if (gameLock != null) gameLock.Dispose();
            Log("游戏会话结束，退出码：" + result.ExitCode);
            if (writer != null) writer.Dispose(); writer = null;
            if (Finished != null) Finished(result);
        }
    }
    void CopyTree(string source, string target) {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("不支持链接形式的引擎目录：" + source);
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) {
            CheckStop(); if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("引擎包含链接文件。");
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
        }
        foreach (string dir in Directory.GetDirectories(source)) CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
