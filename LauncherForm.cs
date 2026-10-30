using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class LauncherForm : Form {
    readonly LauncherConfig config;
    readonly ListBox profiles = new ListBox();
    readonly TextBox search = new TextBox(), name = new TextBox(), directory = new TextBox();
    readonly ComboBox engines = new ComboBox(), modes = new ComboBox();
    readonly NumericUpDown width = new NumericUpDown(), height = new NumericUpDown(), font = new NumericUpDown();
    readonly CheckBox maximize = new CheckBox(), customWindow = new CheckBox { Text="自定义窗口", AutoSize=true };
    readonly Timer autoSave = new Timer { Interval = 350 };
    readonly Label heading = new Label(), summary = new Label(), status = new Label(), saveHint = new Label();
    readonly Button launch = new Button(), stop = new Button();
    readonly RichTextBox output = new RichTextBox();
    readonly ListView engineList = new ListView();
    readonly TabControl tabs = new TabControl();
    readonly TabPage settings = new TabPage("实例设置"), logs = new TabPage("运行日志"), enginePage = new TabPage("引擎管理");
    readonly TabPage about = new TabPage("关于");
    readonly Panel sidebar = new Panel();
    GameProfile current;
    GameSession session;
    string lastLog;
    bool loading, dirty, closeAfterRun, installing;
    internal LauncherForm(LauncherConfig data) {
        config = data; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "Era-Electron启动器 v0.4.0"; ClientSize = new Size(1100, 730); MinimumSize = new Size(980, 690);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = Color.FromArgb(244, 246, 250); AutoScaleMode = AutoScaleMode.Font;
        BuildSidebar(); BuildContent(); RefreshEngines(); RefreshProfiles(config.SelectedId);
        autoSave.Tick += (s, e) => { autoSave.Stop(); if (dirty) SaveEditor(false); };
        FormClosing += OnLauncherClosing;
        if (config.PathNotices.Count > 0) {
            output.AppendText(String.Join(Environment.NewLine,config.PathNotices) + Environment.NewLine);
            status.Text = "已自动恢复引擎路径，详情见运行日志。";
        }
    }
    Button Button(string text, EventHandler action, bool primary = false) {
        var button = new Button { Text = text, Height = 35, Width = 116, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
        button.FlatAppearance.BorderColor = Color.FromArgb(209, 216, 226);
        button.BackColor = primary ? Color.FromArgb(49, 104, 218) : Color.White; button.ForeColor = primary ? Color.White : Color.FromArgb(38, 50, 69);
        button.Click += action; return button;
    }
    void BuildSidebar() {
        sidebar.Dock = DockStyle.Left; sidebar.Width = 260; sidebar.Padding = new Padding(18); sidebar.BackColor = Color.FromArgb(24, 36, 57);
        var title = new Label { Text = "Era-Electron", ForeColor = Color.White, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), Dock = DockStyle.Top, Height = 42 };
        var sub = new Label { Text = "游戏列表", ForeColor = Color.FromArgb(153, 175, 207), Dock = DockStyle.Top, Height = 32 };
        var top = new Panel { Dock = DockStyle.Top, Height = 122 }; search.Dock = DockStyle.Bottom; search.Height = 28;
        var searchLabel = new Label { Text = "搜索游戏", ForeColor = Color.LightSteelBlue, Top = 76, Height = 22 };
        top.Controls.Add(search); top.Controls.Add(searchLabel); top.Controls.Add(sub); top.Controls.Add(title);
        profiles.Dock = DockStyle.Fill; profiles.BorderStyle = BorderStyle.None; profiles.BackColor = sidebar.BackColor; profiles.ForeColor = Color.White;
        profiles.DrawMode = DrawMode.OwnerDrawFixed; profiles.ItemHeight = 58;
        profiles.DrawItem += (s, e) => {
            if (e.Index < 0) return;
            var profile = (GameProfile)profiles.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(selected ? Color.FromArgb(47, 73, 111) : sidebar.BackColor)) e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, profile.Name, Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 9, e.Bounds.Width - 18, 23), Color.White, TextFormatFlags.EndEllipsis);
            using (var small = new Font(Font.FontFamily, 8)) TextRenderer.DrawText(e.Graphics, (config.Engines.Find(i => i.Id == profile.EngineId) == null ? "未选择引擎" : (config.Engines.Find(i => i.Id == profile.EngineId).Version ?? "未知版本")) + (profile.ModeId == "keystate" ? " · 实时按键" : " · 标准输入"), small, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 32, e.Bounds.Width - 18, 20), Color.LightSteelBlue, TextFormatFlags.EndEllipsis);
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 126, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
        foreach (var button in new[] { Button("＋ 导入游戏", ImportGame, true), Button("复制启动配置", Duplicate), Button("移出游戏列表", Remove) }) { button.Width = 220; button.Height = 30; button.Margin = new Padding(0, 0, 0, 6); actions.Controls.Add(button); }
        sidebar.Controls.Add(profiles); sidebar.Controls.Add(top); sidebar.Controls.Add(actions); Controls.Add(sidebar);
        profiles.SelectedIndexChanged += (s, e) => SelectProfile(); search.TextChanged += (s, e) => RefreshProfiles(current == null ? null : current.Id);
    }
    void BuildContent() {
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 18) };
        var header = new Panel { Dock = DockStyle.Top, Height = 96 };
        heading.Text = "选择一个游戏实例"; heading.Font = new Font(Font.FontFamily, 19, FontStyle.Bold); heading.Dock = DockStyle.Top; heading.Height = 43;
        summary.Dock = DockStyle.Fill; summary.ForeColor = Color.FromArgb(105, 119, 139); summary.AutoEllipsis = true;
        header.Controls.Add(summary); header.Controls.Add(heading);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 92 };
        status.Dock = DockStyle.Top; status.Height = 30; status.Text = ""; status.ForeColor = Color.FromArgb(69, 89, 116);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44 };
        SetupButton(launch, "启动游戏", Launch, true); launch.Width = 160;
        SetupButton(stop, "停止游戏", StopGame, false); stop.Enabled = false;
        buttons.Controls.Add(Button("检查启动器更新", CheckUpdate)); buttons.Controls.Add(launch); buttons.Controls.Add(stop); buttons.Controls.Add(Button("游戏目录", OpenGame)); buttons.Controls.Add(Button("日志目录", OpenLogs));
        bottom.Controls.Add(buttons); bottom.Controls.Add(status);
        tabs.Dock = DockStyle.Fill; tabs.Padding = new Point(16, 8); tabs.TabPages.Add(settings); tabs.TabPages.Add(logs); tabs.TabPages.Add(enginePage);
        settings.Padding = new Padding(18); settings.BackColor = logs.BackColor = enginePage.BackColor = Color.White;
        tabs.TabPages.Add(about); about.BackColor=Color.White;
        BuildSettings(); BuildLogs(); BuildEngines(); BuildAbout();
        content.Controls.Add(tabs); content.Controls.Add(header); content.Controls.Add(bottom); Controls.Add(content); content.BringToFront();
    }
    void SetupButton(Button button, string text, EventHandler handler, bool primary) {
        button.Text = text; button.Width = 110; button.Height = 38; button.Margin = new Padding(0, 0, 8, 0); button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Color.FromArgb(49, 104, 218) : Color.White; button.ForeColor = primary ? Color.White : Color.FromArgb(38, 50, 69); button.Click += handler;
    }
    void Row(TableLayoutPanel table, string label, Control control, int row) {
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 7, 0, 5); table.Controls.Add(control, 1, row);
    }
    void BuildSettings() {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, Height = 307, ColumnCount = 2, RowCount = 7 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        directory.ReadOnly = true; engines.DropDownStyle = modes.DropDownStyle = ComboBoxStyle.DropDownList;
        modes.Items.AddRange(BuildInfo.Modes);
        var dimensions = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        width.Minimum = 640; width.Maximum = 3840; height.Minimum = 480; height.Maximum = 2160; width.Width = height.Width = 102;
        dimensions.Controls.Add(customWindow); dimensions.Controls.Add(width); dimensions.Controls.Add(new Label { Text = "×", Width = 25, TextAlign = ContentAlignment.MiddleCenter }); dimensions.Controls.Add(height);
        dimensions.Controls.Add(new Label { Text = "  宽 × 高", Width = 90, TextAlign = ContentAlignment.MiddleCenter });
        font.Minimum = 10; font.Maximum = 32; font.Width = 95;
        maximize.Text = "启动时最大化"; maximize.AutoSize = true;
        Row(table, "游戏名称", name, 0); Row(table, "游戏目录", directory, 1); Row(table, "引擎安装", engines, 2); Row(table, "输入模式", modes, 3); Row(table, "窗口尺寸", dimensions, 4); Row(table, "文字字号", font, 5); Row(table, "窗口行为", maximize, 6);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45 };
        actions.Controls.Add(Button("使用游戏原始窗口", (s, e) => { customWindow.Checked=false; width.Value = 1200; height.Value = 1000; font.Value = 16; maximize.Checked = false; }));
        saveHint.AutoSize = true; saveHint.ForeColor = Color.FromArgb(160, 110, 32); saveHint.Padding = new Padding(0, 9, 0, 0); actions.Controls.Add(saveHint);
        settings.Controls.Add(actions); settings.Controls.Add(table);
        customWindow.CheckedChanged += (s,e)=>{ UpdateWindowControls(); Edited(s,e); };
        name.TextChanged += Edited; engines.SelectedIndexChanged += (s,e) => { if (loading) return; UpdateModes(modes.SelectedItem == null ? "standard" : ((EngineMode)modes.SelectedItem).Id); Edited(s,e); }; modes.SelectedIndexChanged += Edited; width.ValueChanged += Edited; height.ValueChanged += Edited; font.ValueChanged += Edited; maximize.CheckedChanged += Edited;
    }
    void BuildLogs() {
        logs.Padding = new Padding(12); output.Dock = DockStyle.Fill; output.ReadOnly = true; output.BackColor = Color.FromArgb(24, 32, 45); output.ForeColor = Color.FromArgb(190, 207, 229); output.Font = new Font("Consolas", 10); output.BorderStyle = BorderStyle.None;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 43, Padding = new Padding(0, 7, 0, 0) };
        actions.Controls.Add(Button("打开本次日志", (s, e) => { if (lastLog != null && File.Exists(lastLog)) Process.Start(new ProcessStartInfo(lastLog) { UseShellExecute = true }); else Info("还没有运行日志。"); }));
        actions.Controls.Add(Button("打开日志目录", OpenLogs));
        logs.Controls.Add(output); logs.Controls.Add(actions);
    }
    internal static string LegalText(string resource) {
        using(var stream=typeof(LauncherForm).Assembly.GetManifestResourceStream(resource)) {
            if(stream==null)throw new InvalidDataException("启动器缺少许可证资源，请使用完整发布包。");
            using(var reader=new StreamReader(stream))return reader.ReadToEnd();
        }
    }
    void BuildAbout() {
        about.Padding=new Padding(16);
        var title=new Label {Dock=DockStyle.Top,Height=76,Text="Era-Electron 启动器 v0.4.0\r\n独立第三方启动器 · GPL-2.0 许可证",Font=new Font(Font.FontFamily,12),Padding=new Padding(0,5,0,0)};
        var documents=new TabControl {Dock=DockStyle.Fill};
        foreach(var entry in new[] {new[]{"许可证","Launcher.LICENSE"},new[]{"第三方声明","Launcher.NOTICES"}}) {
            var page=new TabPage(entry[0]) {Padding=new Padding(10)};
            page.Controls.Add(new TextBox {Name=entry[1],Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,WordWrap=entry[1]!="Launcher.LICENSE",ScrollBars=ScrollBars.Both,
                BorderStyle=BorderStyle.None,BackColor=Color.White,Font=new Font("Consolas",9.5f),
                Text=LegalText(entry[1]).Replace("\r\n","\n").Replace("\r","\n").Replace("\n",Environment.NewLine)});
            documents.TabPages.Add(page);
        }
        about.Controls.Add(documents); about.Controls.Add(title);
    }
    void BuildEngines() {
        enginePage.Padding = new Padding(12); engineList.Dock = DockStyle.Fill; engineList.View = View.Details; engineList.FullRowSelect = true; engineList.MultiSelect = false;
        engineList.Columns.Add("引擎安装", 180); engineList.Columns.Add("版本", 100); engineList.Columns.Add("目录", 380);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 85, Padding = new Padding(0, 7, 0, 0) };
        actions.Controls.Add(Button("下载引擎", DownloadEngine)); actions.Controls.Add(Button("安装下载的 ZIP", InstallEngineZip)); actions.Controls.Add(Button("登记引擎目录", AddEngine)); actions.Controls.Add(Button("校验引擎", CheckEngine)); actions.Controls.Add(Button("打开目录", (s, e) => { if (engineList.SelectedItems.Count > 0) OpenFolder(LauncherPaths.Resolve(((EngineInstall)engineList.SelectedItems[0].Tag).Directory)); }));
        actions.Controls.Add(Button("重新定位引擎", RelocateEngine));
        actions.Controls.Add(Button("引擎官方发布页", (s,e) => Process.Start(new ProcessStartInfo(EngineCatalog.OfficialUrl) { UseShellExecute=true })));
        enginePage.Controls.Add(engineList); enginePage.Controls.Add(actions);
    }
    void UpdateModes(string desired) {
        bool old=loading; loading=true;
        var engine=engines.SelectedItem as EngineInstall;
        bool adapted=EngineCatalog.Adapted(engine);
        modes.Items.Clear(); modes.Items.Add(BuildInfo.Modes[0]);
        if(adapted) modes.Items.Add(BuildInfo.Modes[1]);
        else if(desired=="keystate") modes.Items.Add(new EngineMode {Id="keystate",Name="实时按键（当前引擎不兼容，请切换标准输入）",KeyState=true});
        modes.SelectedIndex=desired=="keystate" && modes.Items.Count>1 ? 1 : 0;
        customWindow.Enabled=adapted; UpdateWindowControls();
        loading=old;
    }
    void UpdateWindowControls() { width.Enabled=height.Enabled=font.Enabled=maximize.Enabled=customWindow.Checked && EngineCatalog.Adapted(engines.SelectedItem as EngineInstall); }
    void Edited(object sender, EventArgs e) { if (!loading && current != null) { dirty=true; saveHint.Text=""; autoSave.Stop(); autoSave.Start(); } }
    void RefreshProfiles(string selected) {
        loading = true; profiles.Items.Clear();
        foreach (var p in config.Profiles.Where(p => String.IsNullOrEmpty(search.Text) || p.Name.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)) profiles.Items.Add(p);
        if (selected != null) profiles.SelectedItem = config.Profiles.Find(p => p.Id == selected);
        if (profiles.SelectedIndex < 0 && profiles.Items.Count > 0) profiles.SelectedIndex = 0;
        loading = false; SelectProfile();
    }
    void SelectProfile() {
        if (loading) return;
        var next = profiles.SelectedItem as GameProfile;
        if (next == current && next != null) return;
        if (dirty && current != null && !SaveEditor(false)) { loading=true; profiles.SelectedItem=current; loading=false; return; }
        current = next; loading = true;
        settings.Enabled = launch.Enabled = next != null;
        if (next != null) {
            heading.Text = next.Name; summary.Text = "上次启动：" + (String.IsNullOrEmpty(next.LastPlayed) ? "尚未启动" : next.LastPlayed) + "\n" + EnginePaths.Display(next.Directory);
            customWindow.Checked=next.CustomWindow; name.Text = next.Name; directory.Text = EnginePaths.Display(next.Directory); engines.SelectedItem = config.Engines.Find(e => e.Id == next.EngineId); UpdateModes(next.ModeId);
            width.Value = Math.Max(640, Math.Min(3840, next.Width)); height.Value = Math.Max(480, Math.Min(2160, next.Height)); font.Value = Math.Max(10, Math.Min(32, next.FontSize)); maximize.Checked = next.AutoMax;
            config.SelectedId = next.Id;
        } else { heading.Text = "尚未添加游戏"; summary.Text = "可以导入一个本地游戏目录。"; }
        dirty = false; saveHint.Text = ""; loading = false;
    }
    bool SaveEditor(bool showErrors = false) {
        autoSave.Stop();
        if (current == null) return false;
        try {
            var copy = current.Copy(); copy.Name = name.Text.Trim(); copy.EngineId = engines.SelectedItem == null ? null : ((EngineInstall)engines.SelectedItem).Id; copy.ModeId = modes.SelectedItem == null ? null : ((EngineMode)modes.SelectedItem).Id;
            copy.CustomWindow=customWindow.Checked; copy.Width = (int)width.Value; copy.Height = (int)height.Value; copy.FontSize = (int)font.Value; copy.AutoMax = maximize.Checked;
            LauncherStore.Validate(copy, config);
            int index = config.Profiles.FindIndex(p => p.Id == current.Id); config.Profiles[index] = copy;
            try { LauncherStore.Save(config); } catch { config.Profiles[index] = current; throw; }
            current = copy; loading = true; for (int item=0; item<profiles.Items.Count; item++) if (((GameProfile)profiles.Items[item]).Id==copy.Id) { profiles.Items[item]=copy; break; } loading = false;
            heading.Text = copy.Name; dirty = false; saveHint.Text = ""; profiles.Invalidate(); return true;
        } catch (Exception e) { saveHint.Text=e.Message; if(showErrors) Error(e.Message); return false; }
    }
    void ImportGame(object sender, EventArgs e) {
        using (var dialog = new FolderBrowserDialog { Description = "选择包含 ere/main.js 的游戏根目录", SelectedPath = LauncherPaths.Home }) if (dialog.ShowDialog(this) == DialogResult.OK) {
            if (!LauncherStore.IsGame(dialog.SelectedPath)) { Error("这个目录中没有 ere/main.js 或打包入口。"); return; }
            var p = new GameProfile { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileName(dialog.SelectedPath), Directory = dialog.SelectedPath, EngineId = config.Engines.Count == 0 ? null : config.Engines[0].Id };
            config.Profiles.Add(p); Persist(); RefreshProfiles(p.Id);
        }
    }
    void Duplicate(object sender, EventArgs e) { if (current == null || !SaveEditor()) return; var p = current.Copy(); p.Id = Guid.NewGuid().ToString("N"); p.Name += " · 副本"; p.LastPlayed = null; config.Profiles.Add(p); Persist(); RefreshProfiles(p.Id); }
    void Remove(object sender, EventArgs e) {
        if (current == null || MessageBox.Show("仅移出启动器列表，游戏和存档都保留。继续？", "移出实例", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        LauncherStore.Remove(config, current.Id); current = null; dirty = false; Persist(); RefreshProfiles(null);
    }
    void RefreshEngines() {
        var selected=engines.SelectedItem as EngineInstall;
        loading = true; engines.Items.Clear(); engineList.Items.Clear();
        foreach (var install in config.Engines) { engines.Items.Add(install); var item = new ListViewItem(install.Name) { Tag = install }; string label;
            try { EngineCatalog.Inspect(install); label=install.Version; } catch { label=(install.Version ?? "未知版本")+"（目录不可用）"; }
            item.SubItems.Add(label); item.SubItems.Add(EnginePaths.Display(install.Directory)); engineList.Items.Add(item); }
        if(selected!=null) engines.SelectedItem=config.Engines.Find(e=>e.Id==selected.Id);
        loading = false;
    }
    bool PickEngineLocation(EngineInstall install) {
        using(var dialog=new FolderBrowserDialog {Description="重新选择该引擎目录；保留现有游戏绑定"}) {
            if(dialog.ShowDialog(this)!=DialogResult.OK)return false;
            try {
                var candidate=new EngineInstall {Directory=dialog.SelectedPath}; EngineCatalog.Inspect(candidate);
                install.Directory=Path.GetFullPath(candidate.Directory); install.Version=candidate.Version; install.ArchiveHash=candidate.ArchiveHash;
                if(!Persist())return false;
                RefreshEngines(); if(current!=null) UpdateModes(current.ModeId);
                status.Text="已更新引擎路径，原有游戏绑定保持。"; return true;
            } catch(Exception error) {Error(error.Message);return false;}
        }
    }
    bool EnsureEngineLocation(EngineInstall install) {
        if(EnginePaths.Present(install.Directory))return true;
        if(EnginePaths.Recover(config,install)) {if(!Persist())return false;RefreshEngines();status.Text="已找到并恢复原引擎。";return true;}
        if(MessageBox.Show(this,"引擎目录已失效：\n"+EnginePaths.Display(install.Directory)+
            "\n\n无法唯一确认原引擎。是否重新选择引擎目录？取消不会删除登记或游戏。","重新定位引擎",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return false;
        return PickEngineLocation(install);
    }
    void RelocateEngine(object sender,EventArgs e) {
        if(engineList.SelectedItems.Count==0) {Info("请先选中一个引擎。");return;}
        PickEngineLocation((EngineInstall)engineList.SelectedItems[0].Tag);
    }
    bool EnsureGameLocation() {
        try {if(LauncherStore.IsGame(LauncherPaths.Resolve(current.Directory)))return true;}catch { }
        if(MessageBox.Show(this,"原游戏目录不可用。是否重新选择包含游戏入口及存档的原游戏目录？",
            "重新定位游戏",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return false;
        using(var dialog=new FolderBrowserDialog {Description="选择原游戏目录；不会创建或复制存档"}) {
            if(dialog.ShowDialog(this)!=DialogResult.OK)return false;
            if(!LauncherStore.IsGame(dialog.SelectedPath)) {Error("该目录缺少游戏入口，请选择游戏根目录。");return false;}
            current.Directory=Path.GetFullPath(dialog.SelectedPath); directory.Text=current.Directory; return true;
        }
    }
    void AddEngine(object sender, EventArgs e) {
        using (var dialog = new FolderBrowserDialog { Description = "选择任意版本的 Era-Electron 引擎目录" }) if (dialog.ShowDialog(this) == DialogResult.OK) {
            try {
                var install = new EngineInstall { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileName(dialog.SelectedPath), Directory = dialog.SelectedPath };
                GameSession.ValidateEngine(install, BuildInfo.Modes[0]); config.Engines.Add(install); Persist(); RefreshEngines(); if (current != null) engines.SelectedItem = String.IsNullOrEmpty(current.EngineId) ? install : config.Engines.Find(i => i.Id == current.EngineId);
            } catch (Exception error) { Error(error.Message); }
        }
    }
    async void DownloadEngine(object sender, EventArgs e) {
        try {

            using(var dialog=new ReleaseDialog()) {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                var asset=dialog.Selected;
                if(!asset.Direct) { Process.Start(new ProcessStartInfo(asset.Url){UseShellExecute=true}); Info("官方链接为网盘页面。请在浏览器下载 Windows ZIP，再点击‘安装下载的 ZIP’完成安装。"); return; }
                using(var folder=new FolderBrowserDialog {Description="选择引擎安装父目录"}) if(folder.ShowDialog(this)==DialogResult.OK) {
                    var button=sender as Button; if(button!=null)button.Enabled=false;
                    try {
                        installing=true; string path=await Downloads.DownloadAndInstall(asset,folder.SelectedPath,text=>UI(()=>status.Text=text));
                        RegisterDownloaded(path);
                    } finally {installing=false;if(button!=null)button.Enabled=true;}
                }
            }
        } catch(Exception error) {Error(error.Message);}
    }
    async void InstallEngineZip(object sender, EventArgs e) {
        try {
            using(var file=new OpenFileDialog {Filter="ZIP 引擎包|*.zip",Title="选择已下载的 Windows 引擎 ZIP"})
            using(var folder=new FolderBrowserDialog {Description="选择引擎安装父目录"}) {
                if(file.ShowDialog(this)!=DialogResult.OK || folder.ShowDialog(this)!=DialogResult.OK)return;
                var button=sender as Button;if(button!=null)button.Enabled=false;
                try { installing=true; string path=await System.Threading.Tasks.Task.Run(()=>Downloads.InstallZip(file.FileName,folder.SelectedPath)); RegisterDownloaded(path);
                    try {File.Delete(file.FileName);}catch(Exception cleanup){Info("引擎已安装，但安装包清理失败："+cleanup.Message);} }
                finally {installing=false;if(button!=null)button.Enabled=true;}
            }
        } catch(Exception error) {Error(error.Message);}
    }
    void RegisterDownloaded(string path) {
        var install=new EngineInstall {Id=Guid.NewGuid().ToString("N"),Name=Path.GetFileName(path),Directory=path};
        EngineCatalog.Inspect(install); config.Engines.Add(install); Persist(); RefreshEngines();
        if(current!=null) engines.SelectedItem=config.Engines.Find(x=>x.Id==current.EngineId);
        status.Text="引擎已安装并登记："+install.Version;
    }
    async void CheckUpdate(object sender, EventArgs e) {
        var button=sender as Button;if(button!=null)button.Enabled=false;
        try {
            var release=await Downloads.LauncherLatest();
            Version latest; if(!Version.TryParse(release.Tag.TrimStart('v','V'),out latest))throw new IOException("发布版本号无法识别。");
            if(latest<=new Version("0.4.0")) {Info("当前已是最新版本（v0.4.0）。");return;}
            if(MessageBox.Show(this,"发现新版 "+release.Tag+"\n\n"+release.Notes+"\n\n打开发布页下载？","启动器更新",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes)
                Process.Start(new ProcessStartInfo(release.Url){UseShellExecute=true});
        } catch(Exception error) {Error("更新检查失败："+error.Message);}
        finally {installing=false;if(button!=null)button.Enabled=true;}
    }
    void CheckEngine(object sender, EventArgs e) {
        if (engineList.SelectedItems.Count == 0) { Info("请先选中一个引擎。"); return; }
        var install = (EngineInstall)engineList.SelectedItems[0].Tag; Cursor = Cursors.WaitCursor;
        if(!EnsureEngineLocation(install)) {Cursor=Cursors.Default;return;}
        try { GameSession.ValidateEngine(install, BuildInfo.Modes[0]); if(EngineCatalog.Adapted(install)) GameSession.ValidateEngine(install,BuildInfo.Modes[1]); Info("校验通过："+install.Version+(EngineCatalog.Adapted(install) ? "，支持实时按键扩展。" : "，使用原版标准启动。")); }
        catch (Exception error) { Error(error.Message); } finally { Cursor = Cursors.Default; }
    }
    void Launch(object sender, EventArgs e) {
        if (session != null || current==null || !EnsureGameLocation()) return;
        var selectedEngine=engines.SelectedItem as EngineInstall;
        if(selectedEngine==null) {Error("请先登记并选择一个引擎版本。");return;}
        if(!EnsureEngineLocation(selectedEngine) || !SaveEditor(true))return;
        var profile = current.Copy(); var engine = config.Engines.Find(x => x.Id == profile.EngineId); var mode = BuildInfo.Modes.First(x => x.Id == profile.ModeId);
        if(engine == null) { Error("请先登记并选择一个引擎版本。"); return; }
        try {GameSession.ValidateEngine(engine,mode);}catch(Exception error) {Error(error.Message);return;}
        output.Clear(); tabs.SelectedTab = logs; sidebar.Enabled = settings.Enabled = enginePage.Enabled = launch.Enabled = false; stop.Enabled = true;
        session = new GameSession();
        session.Message += line => UI(() => { if (output.TextLength > 150000) output.Text = output.Text.Substring(output.TextLength - 100000); output.AppendText(line + Environment.NewLine); output.ScrollToCaret(); });
        session.State += text => UI(() => status.Text = text.StartsWith("运行中") || text.StartsWith("正在停止") ? text : "正在处理…");
        session.Finished += result => UI(() => {
            session = null; sidebar.Enabled = settings.Enabled = enginePage.Enabled = launch.Enabled = true; stop.Enabled = false; lastLog = result.LogFile;
            status.Text = result.Error != null ? "启动失败 · 查看运行日志" : result.CleanupComplete ? "已结束" : "已结束 · 部分文件留待下次清理";
            if (result.Started) { var item = config.Profiles.Find(x => x.Id == profile.Id); item.LastPlayed = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); Persist(); summary.Text = "上次启动：" + item.LastPlayed + "\n" + LauncherPaths.Resolve(item.Directory); }
            if (closeAfterRun) Close();
        });
        session.Start(profile, engine, mode);
    }
    void StopGame(object sender, EventArgs e) { if (session != null && MessageBox.Show("停止游戏可能丢失未保存的进度。确定停止？", "停止游戏", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { session.Stop(); stop.Enabled = false; } }
    void OnLauncherClosing(object sender, FormClosingEventArgs e) {
        if(installing) {e.Cancel=true;Info("引擎正在下载或安装，请完成后再退出，确保安装包和暂存得到清理。");return;}
        if (session != null) { e.Cancel = true; if (!closeAfterRun && MessageBox.Show("游戏仍在运行。停止并清理后退出启动器？", "退出启动器", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { closeAfterRun = true; session.Stop(); } }
        else { autoSave.Stop(); if(dirty && current != null) SaveEditor(false); Persist(); }
    }
    protected override void Dispose(bool disposing) { if(disposing) { autoSave.Stop(); autoSave.Dispose(); } base.Dispose(disposing); }
    void UI(Action action) { if (!IsDisposed && IsHandleCreated) { try { BeginInvoke(action); } catch (InvalidOperationException) { } } }
    bool Persist() { try { LauncherStore.Save(config); return true; } catch (Exception e) { Error("无法保存启动器配置：" + e.Message); return false; } }
    void OpenGame(object sender, EventArgs e) { if (current != null) OpenFolder(LauncherPaths.Resolve(current.Directory)); }
    void OpenLogs(object sender, EventArgs e) { string folder = Path.Combine(LauncherPaths.UserRoot, "logs"); Directory.CreateDirectory(folder); OpenFolder(folder); }
    void OpenFolder(string folder) { try { if (!Directory.Exists(folder)) { Error("目录不存在：" + folder); return; } Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); } catch (Exception e) { Error(e.Message); } }
    void Error(string message) { MessageBox.Show(this, message, "Era-Electron启动器", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    void Info(string message) { MessageBox.Show(this, message, "Era-Electron启动器", MessageBoxButtons.OK, MessageBoxIcon.Information); }
}
