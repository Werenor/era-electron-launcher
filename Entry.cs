using System;
using System.Windows.Forms;
internal static class Entry {
    [STAThread]
    static int Main(string[] args) {
        if (args.Length == 2 && args[0] == "--self-test") return LauncherTests.Run(args[1]);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "启动器错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try { SessionTools.CleanupStale(); Application.Run(new LauncherForm(LauncherStore.Load())); return 0; }
        catch (Exception e) { MessageBox.Show(e.Message, "Era-Electron启动器", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}
