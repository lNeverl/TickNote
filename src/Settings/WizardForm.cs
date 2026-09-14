using SedentaryReminder.Data;

namespace SedentaryReminder.Settings;

/// <summary>
/// 首次运行向导（M0）：选择便携模式或自定义数据目录；识别已有数据目录并接管（M0-4）。
/// 继承 DpiForm：任意缩放比（含 200%/4K）下布局不被裁。
/// </summary>
public sealed class WizardForm : DpiForm
{
    private readonly RadioButton _rbPortable;
    private readonly RadioButton _rbCustom;
    private readonly TextBox _txtCustom;
    private readonly Button _btnBrowse;

    /// <summary>用户确认后的数据目录；取消时为 null。</summary>
    public string? SelectedDataDir { get; private set; }

    public WizardForm()
    {
        Text = "久坐提醒 - 首次运行设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 330);
        // 高度由内容决定：任何 DPI/缩放下按钮行都不可能被挤出可视区
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(560, 0); // 宽度下限（DpiForm 会按 DPI 比率缩放它）
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 8,
            AutoSize = true,
        };
        for (var i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblTitle = new Label
        {
            Text = "欢迎使用 久坐提醒 + 工作流水记录",
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        var lblDesc = new Label
        {
            Text = "请选择数据（记录、设置、报告）的保存位置。以后可在“设置 → 数据”里更换。",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        _rbPortable = new RadioButton
        {
            Text = "便携模式：保存在软件目录下的 data 文件夹（整个文件夹拷到 U 盘/别的电脑即可继续使用）",
            AutoSize = true,
            Checked = true,
            MaximumSize = new Size(500, 0), // 超长自动换行，不被右边缘裁掉
            Margin = new Padding(0, 0, 0, 2),
        };

        // 完整路径用只读文本框展示：再长的路径也不会被裁，还可以复制
        var txtPortablePath = new TextBox
        {
            Text = BootConfig.PortableDataDir,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Fill,
            Margin = new Padding(26, 0, 0, 10),
        };

        _rbCustom = new RadioButton
        {
            Text = "自定义目录：自己选择一个文件夹（如 D:\\我的数据 或 %APPDATA%）",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(0, 0, 0, 4),
        };

        var customRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(26, 0, 0, 0),
        };
        _txtCustom = new TextBox { Width = 340, Enabled = false };
        _btnBrowse = new Button { Text = "浏览…", Enabled = false, AutoSize = true };
        customRow.Controls.Add(_txtCustom);
        customRow.Controls.Add(_btnBrowse);

        var hint = new Label
        {
            Text = "直接关闭本窗口 = 不保存任何选择并退出程序。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 12, 0, 4),
        };

        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };
        var btnOk = new Button { Text = "开始使用", AutoSize = true, Padding = new Padding(10, 3, 10, 3) };
        btnOk.Click += OnOk;
        btnRow.Controls.Add(btnOk);

        root.Controls.Add(lblTitle, 0, 0);
        root.Controls.Add(lblDesc, 0, 1);
        root.Controls.Add(_rbPortable, 0, 2);
        root.Controls.Add(txtPortablePath, 0, 3);
        root.Controls.Add(_rbCustom, 0, 4);
        root.Controls.Add(customRow, 0, 5);
        root.Controls.Add(hint, 0, 6);
        root.Controls.Add(btnRow, 0, 7);
        Controls.Add(root);
        AcceptButton = btnOk;

        _rbPortable.CheckedChanged += (_, _) =>
        {
            _txtCustom.Enabled = _rbCustom.Checked;
            _btnBrowse.Enabled = _rbCustom.Checked;
        };
        _btnBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "选择数据保存目录" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _txtCustom.Text = dlg.SelectedPath;
        };
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var dir = _rbPortable.Checked ? BootConfig.PortableDataDir : _txtCustom.Text.Trim();
        if (dir.Length == 0)
        {
            MessageBox.Show(this, "请选择或填写一个目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法创建该目录：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // M0-4：目录里已有本软件数据 → 提示接管
        if (File.Exists(Path.Combine(dir, "data.db")))
        {
            var r = MessageBox.Show(this,
                "检测到该目录已有本软件的数据，将直接接管使用（设置/流水/报告全部恢复）。\n是否继续？",
                "检测到已有数据", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r != DialogResult.Yes) return;
        }

        SelectedDataDir = dir;
        DialogResult = DialogResult.OK;
        Close();
    }
}
