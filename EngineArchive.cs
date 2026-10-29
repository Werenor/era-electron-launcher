using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class EngineArchive : IDisposable {
    readonly FileStream stream;
    readonly Dictionary<string, object> header;
    readonly long dataOffset;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
    internal EngineArchive(string path) {
        stream = File.OpenRead(path);
        try {
            var prefix = Read(0, 16); int headerLength = BitConverter.ToInt32(prefix, 4), jsonLength = BitConverter.ToInt32(prefix, 12);
            if (BitConverter.ToInt32(prefix, 0) != 4 || jsonLength < 2 || jsonLength > 32 * 1024 * 1024 || headerLength < jsonLength + 8 || 8L + headerLength > stream.Length) throw new InvalidDataException("ASAR header invalid");
            header = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(Read(16, jsonLength))); dataOffset = 8L + headerLength;
        } catch { stream.Dispose(); throw; }
    }
    byte[] Read(long offset, int count) {
        var data = new byte[count]; stream.Position = offset; int read = 0;
        while (read < count) { int part = stream.Read(data, read, count - read); if (part == 0) throw new EndOfStreamException(); read += part; }
        return data;
    }
    Dictionary<string, object> Entry(string path) {
        var entry = header;
        foreach (string part in path.Split('/')) entry = (Dictionary<string, object>)((Dictionary<string, object>)entry["files"])[part];
        return entry;
    }
    internal byte[] FileBytes(string path) { return Bytes(Entry(path)); }
    byte[] Bytes(Dictionary<string, object> entry) {
        if (entry.ContainsKey("unpacked") || entry.ContainsKey("link")) throw new InvalidDataException("Unsupported packed file entry");
        long offset = Convert.ToInt64(entry["offset"]); int size = Convert.ToInt32(entry["size"]);
        if (offset < 0 || size < 0 || dataOffset + offset + size > stream.Length) throw new InvalidDataException("ASAR file bounds invalid");
        return Read(dataOffset + offset, size);
    }
    static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    void WriteFolder(Dictionary<string, object> folder, Dictionary<string, byte[]> replacements, MemoryStream data, string prefix) {
        foreach (var pair in (Dictionary<string, object>)folder["files"]) {
            var entry = (Dictionary<string, object>)pair.Value; string path = prefix + pair.Key;
            if (entry.ContainsKey("files")) { WriteFolder(entry, replacements, data, path + "/"); continue; }
            byte[] bytes; if (!replacements.TryGetValue(path, out bytes)) bytes = Bytes(entry);
            entry["offset"] = data.Position.ToString(); entry["size"] = bytes.Length;
            var blocks = new List<string>(); const int block = 4194304;
            for (int i = 0; i < bytes.Length; i += block) { int length = Math.Min(block, bytes.Length - i); var part = new byte[length]; Buffer.BlockCopy(bytes, i, part, 0, length); blocks.Add(Hash(part)); }
            entry["integrity"] = new Dictionary<string, object> { {"algorithm", "SHA256"}, {"hash", Hash(bytes)}, {"blockSize", block}, {"blocks", blocks} };
            data.Write(bytes, 0, bytes.Length);
        }
    }
    internal void Write(string output, Dictionary<string, byte[]> replacements) {
        foreach (var pair in replacements) {
            var folder = header; string[] parts = pair.Key.Split('/');
            for (int i = 0; i < parts.Length - 1; i++) {
                var children = (Dictionary<string, object>)folder["files"];
                if (!children.ContainsKey(parts[i])) children[parts[i]] = new Dictionary<string, object> { {"files", new Dictionary<string, object>()} };
                folder = (Dictionary<string, object>)children[parts[i]];
            }
            var files = (Dictionary<string, object>)folder["files"];
            if (!files.ContainsKey(parts[parts.Length - 1])) files[parts[parts.Length - 1]] = new Dictionary<string, object>();
        }
        using (var data = new MemoryStream()) {
            WriteFolder(header, replacements, data, ""); byte[] json = Encoding.UTF8.GetBytes(Json.Serialize(header));
            int payload = 4 + json.Length, padded = (payload + 3) & ~3, headerSize = 4 + padded;
            using (var writer = new BinaryWriter(File.Create(output))) {
                writer.Write(4); writer.Write(headerSize); writer.Write(padded); writer.Write(json.Length); writer.Write(json);
                writer.Write(new byte[padded - payload]); data.Position = 0; data.CopyTo(writer.BaseStream);
            }
        }
    }
    public void Dispose() { stream.Dispose(); }
}

internal static class EngineCatalog {
    internal const string OfficialUrl = "https://gitgud.io/umaera/engine/era-electron/-/releases";
    internal static string Executable(string folder) {
        string expected = Path.Combine(folder, "era-electron.exe"); if (File.Exists(expected)) return expected;
        var files = Directory.GetFiles(folder, "*.exe");
        foreach (string file in files) if (Path.GetFileName(file).IndexOf("era", StringComparison.OrdinalIgnoreCase) >= 0 && Path.GetFileName(file).IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) < 0) return file;
        throw new IOException("目录中没有找到 Era-Electron 的可执行文件。");
    }
    internal static void Inspect(EngineInstall engine) {
        string folder = LauncherPaths.Resolve(engine.Directory); Executable(folder);
        string archive = Path.Combine(folder, "resources", "app.asar");
        string package;
        if (File.Exists(archive)) {
            engine.ArchiveHash = SessionTools.Sha(archive);
            using (var asar = new EngineArchive(archive)) package = Encoding.UTF8.GetString(asar.FileBytes("package.json"));
        } else {
            string unpacked = Path.Combine(folder, "resources", "app", "package.json");
            if (!File.Exists(unpacked)) throw new IOException("引擎缺少 resources/app.asar 或 resources/app/package.json。");
            engine.ArchiveHash = "unpacked"; package = File.ReadAllText(unpacked);
        }
        var meta = LauncherStore.Json.Deserialize<Dictionary<string, object>>(package);
        engine.Version = meta.ContainsKey("version") ? Convert.ToString(meta["version"]) : "未知版本";
    }
    internal static bool Adapted(EngineInstall engine) { return engine != null && engine.ArchiveHash == BuildInfo.BaseHash; }
}
