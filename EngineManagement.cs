using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

internal static class EngineManagement {
    internal static bool Missing(EngineInstall engine) {
        try {
            if(String.IsNullOrWhiteSpace(engine.Directory))return true;
            return (File.GetAttributes(LauncherPaths.Resolve(engine.Directory)) & FileAttributes.Directory)==0;
        } catch(FileNotFoundException) {return true;} catch(DirectoryNotFoundException){return true;}
        catch(ArgumentException){return true;} catch(NotSupportedException){return true;}
        catch {return false;} // Access-denied and transient IO failures are not missing paths.
    }
    internal static void Remove(LauncherConfig config,IEnumerable<EngineInstall> engines) {
        var ids=new HashSet<string>(engines.Select(e=>e.Id));
        var before=config.Engines.ToList(); var bindings=config.Profiles.Select(p=>p.EngineId).ToArray();
        config.Engines.RemoveAll(e=>ids.Contains(e.Id));
        foreach(var profile in config.Profiles)if(ids.Contains(profile.EngineId))profile.EngineId=null;
        try {LauncherStore.Save(config);}
        catch {config.Engines=before;for(int i=0;i<bindings.Length;i++)config.Profiles[i].EngineId=bindings[i];throw;}
    }
    static string Full(string path){string full=Path.GetFullPath(path);return full==Path.GetPathRoot(full)?full:full.TrimEnd('\\','/');}
    static bool Contains(string parent,string child) {return String.Equals(Full(parent),Full(child),StringComparison.OrdinalIgnoreCase)||Full(child).StartsWith(Full(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
    internal static string ValidateDelete(LauncherConfig config,EngineInstall engine) {
        string path=Full(LauncherPaths.Resolve(engine.Directory));
        if(path.Length==0 || path==Full(Path.GetPathRoot(path)))throw new IOException("不能删除磁盘根目录。");
        if(Contains(path,LauncherPaths.Home)||Contains(path,LauncherPaths.UserRoot))throw new IOException("目录包含启动器或启动器数据，不能删除。");
        foreach(var game in config.Profiles) {
            if(String.IsNullOrWhiteSpace(game.Directory))continue;
            string gamePath=LauncherPaths.Resolve(game.Directory);
            if(Contains(path,gamePath)||Contains(gamePath,path))throw new IOException("目录与游戏目录重叠，请仅移出登记。");
        }
        foreach(var other in config.Engines)if(other.Id!=engine.Id && !String.IsNullOrWhiteSpace(other.Directory)) {
            string otherPath=LauncherPaths.Resolve(other.Directory);
            if(Contains(path,otherPath)||Contains(otherPath,path))throw new IOException("目录与另一条引擎登记重叠，请先处理重复登记。");
        }
        for(string parent=path;!String.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent))
            if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new IOException("不能删除链接目录中的引擎。");
        InspectTree(path);EngineCatalog.Inspect(engine);
        foreach(var process in Process.GetProcesses())using(process) {
            string executable=null;try {executable=process.MainModule.FileName;}catch{}
            if(executable!=null && Contains(path,executable))throw new IOException("该目录中有程序正在运行，请先关闭。");
        }
        return path;
    }
    static void InspectTree(string path) {
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("目录包含链接，请仅移出登记。");
        if(File.Exists(Path.Combine(path,"ere","main.js"))||File.Exists(Path.Combine(path,"main.bundle.js")))throw new IOException("引擎目录中包含游戏，请仅移出登记。");
        foreach(var file in Directory.GetFiles(path))if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("目录包含链接文件，请仅移出登记。");
        foreach(var child in Directory.GetDirectories(path))InspectTree(child);
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct Operation { public IntPtr Window;public uint Function;[MarshalAs(UnmanagedType.LPWStr)]public string From;[MarshalAs(UnmanagedType.LPWStr)]public string To;public ushort Flags;[MarshalAs(UnmanagedType.Bool)]public bool Aborted;public IntPtr Mappings;[MarshalAs(UnmanagedType.LPWStr)]public string Title; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHFileOperation(ref Operation operation);
    internal static void Recycle(LauncherConfig config,EngineInstall engine,IntPtr window) {
        string path=ValidateDelete(config,engine);
        var operation=new Operation {Window=window,Function=3,From=path+"\0\0",Flags=0x40|0x100,Title="删除引擎"};
        int result=SHFileOperation(ref operation);
        if(operation.Aborted)throw new OperationCanceledException("删除已取消，登记保留。");
        if(result!=0 || Directory.Exists(path))throw new IOException("引擎目录未完全删除，登记保留。错误码："+result);
    }
}
