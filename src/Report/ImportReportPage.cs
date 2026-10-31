using SedentaryReminder.Data;

namespace SedentaryReminder.Report;

/// <summary>
/// 粘贴回导入（M4-5）：把 AI 生成的日报/周报整段粘贴进来，保存到 report 表并写 exports\ 副本；
/// 同期已有报告时提示"覆盖/保留两份"。
/// </summary>
public sealed class ImportReportPage : WorkspacePage
{
    private readonly AppContext _app;
    private readonly RadioButton _rbDaily;
    private readonly RadioButton _rbWeekly;
    private readonly DateTimePicker _dtDate;
    private readonly Label _lblPeriod;
    private readonly TextBox _txtContent;

    public ImportReportPage(AppContext app)
    {
        _app = app;
        Text = "导入报告";
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        // 必须显式给 ColumnStyles：缺省时 DpiForm.Scale 后列宽按内容错乱计算，按钮行溢出窗口右缘
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var typeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; // DPI 切换后按钮尺寸可能短暂失真，允许折行兜底：宁可换行也不裁掉
        _rbDaily = new RadioButton { Text = "日报", Checked = true, AutoSize = true };
        _rbWeekly = new RadioButton { Text = "周报", AutoSize = true };
        _rbDaily.CheckedChanged += (_, _) => UpdatePeriodLabel();
        typeRow.Controls.Add(new Label { Text = "类型：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) });
        typeRow.Controls.Add(_rbDaily);
        typeRow.Controls.Add(_rbWeekly);
        typeRow.Controls.Add(new Label { Text = "　对应日期：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 4, 0) });
        _dtDate = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 136, MinimumSize = new Size(136, 0) };
        _dtDate.ValueChanged += (_, _) => UpdatePeriodLabel();
        typeRow.Controls.Add(_dtDate);
        _lblPeriod = new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 0, 0), ForeColor = Color.Gray };
        typeRow.Controls.Add(_lblPeriod);
        root.Controls.Add(typeRow, 0, 0);

        root.Controls.Add(new Label
        {
            Text = "把 AI 生成的报告整段粘贴到下面：",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        }, 0, 1);

        _txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
        };
        root.Controls.Add(_txtContent, 0, 2);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var btnSave = UiLayout.Button("保存", primary: true);
        btnSave.Click += OnSave;
        var btnPaste = UiLayout.Button("从剪贴板粘贴");
        btnPaste.Click += (_, _) =>
        {
            try
            {
                if (Clipboard.ContainsText()) _txtContent.Text = UiLayout.MultilineText(Clipboard.GetText());
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "读取剪贴板失败，剪贴板被其他程序占用：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        btnRow.Controls.Add(btnSave);
        btnRow.Controls.Add(btnPaste);
        root.Controls.Add(btnRow, 0, 3);

        Controls.Add(root);
        UpdatePeriodLabel();
    }

    private string PeriodKey
    {
        get
        {
            if (_rbDaily.Checked) return _dtDate.Value.ToString("yyyy-MM-dd");
            return _app.Reports.GetWeekRange(_dtDate.Value).Key;
        }
    }

    private void UpdatePeriodLabel()
    {
        _lblPeriod.Text = "期次：" + PeriodKey;
    }

    public override bool HasUnsavedChanges => !string.IsNullOrWhiteSpace(_txtContent.Text);

    private void OnSave(object? sender, EventArgs e) => SaveChanges();

    public override bool SaveChanges()
    {
        var content = _txtContent.Text.Trim();
        if (content.Length == 0)
        {
            MessageBox.Show(FindForm(), "内容为空，请先粘贴 AI 生成的报告。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var type = _rbDaily.Checked ? "daily" : "weekly";
        var key = PeriodKey;

        // 同期已有报告：覆盖 / 保留两份 / 取消
        long? overwriteId = null;
        var existing = _app.Db.GetReports(type, key);
        if (existing.Count > 0)
        {
            var r = MessageBox.Show(FindForm(),
                $"{key} 已有 {existing.Count} 份报告。\n\n是 = 覆盖最新一份\n否 = 保留两份（新增一份）\n取消 = 不保存",
                "同期已有报告", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) overwriteId = existing[0].Id;
        }

        try { _app.Reports.SaveImportedReport(type, key, content, overwriteId); }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "保存报告失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _app.Notify($"已保存{(_rbDaily.Checked ? "日报" : "周报")} {key}，副本见 exports\\ 目录");
        _txtContent.Clear();
        return true;
    }
}
