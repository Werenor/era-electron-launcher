using System;
using System.Windows.Forms;
internal static class Entry {
    [STAThread]
    static int Main(string[] args) {
        if (args.Length == 2 && args[0] == "--self-test") return LauncherTests.Run(args[1]);
        if (args.Length == 4 && args[0] == "--apply-update") return LauncherUpdater.Apply(args);
        if (args.Length == 3 && args[0] == "--clean-update") LauncherUpdater.Clean(args);
        if (LauncherUpdater.UpdateInProgress()) {
            MessageBox.Show("正在安装更新，完成后会自动打开 EreDeck。", "EreDeck 更新");
            return 0;
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "EreDeck 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try { SessionTools.CleanupStale(); Application.Run(new LauncherForm(LauncherStore.Load())); return 0; }
        catch (Exception e) { MessageBox.Show(e.Message, "EreDeck", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}
