using SedentaryReminder.Data;

namespace SedentaryReminder.Report;

/// <summary>
/// 报告视图（M4-2/M4-6）：按日/周归档展示已保存的日报/周报，
/// 可查看、再编辑、一键复制全文去提交，可"重新复制提示词+最新数据"再走一遍 AI 流程。
/// </summary>
public sealed class ReportViewForm : DpiForm
{
    private readonly AppContext _app;
    private readonly ListBox _list;
    private readonly TextBox _txtContent;
    private readonly Button _btnSave;

    private List<ReportEntry> _reports = new();

    public ReportViewForm(AppContext app)
    {
        _app = app;
        Text = "我的报告";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 520);
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _list = new ListBox { Dock = DockStyle.Fill };
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        root.Controls.Add(_list, 0, 0);

        _txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Font = new Font("Microsoft YaHei UI", 9.5F),
        };
        root.Controls.Add(_txtContent, 1, 0);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        root.SetColumnSpan(btnRow, 2);

        _btnSave = new Button { Text = "保存修改", AutoSize = true };
        _btnSave.Click += (_, _) => SaveEdit();
        var btnCopyAll = new Button { Text = "复制全文（去提交）", AutoSize = true };
        btnCopyAll.Click += (_, _) =>
        {
            if (_txtContent.Text.Length == 0) return;
            try
            {
                Clipboard.SetText(_txtContent.Text);
                _app.Tray.ShowBalloon("报告全文已复制到剪贴板");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败，剪贴板被其他程序占用：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        var btnRePrompt = new Button { Text = "重新复制提示词+最新数据", AutoSize = true };
        btnRePrompt.Click += (_, _) => ReCopyPrompt();
        var btnDelete = new Button { Text = "删除", AutoSize = true };
        btnDelete.Click += (_, _) => DeleteSelected();
        var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true };

        btnRow.Controls.Add(_btnSave);
        btnRow.Controls.Add(btnCopyAll);
        btnRow.Controls.Add(btnRePrompt);
        btnRow.Controls.Add(btnDelete);
        btnRow.Controls.Add(btnClose);
        root.Controls.Add(btnRow, 0, 1);

        Controls.Add(root);
        CancelButton = btnClose;
        Reload();
    }

    private ReportEntry? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _reports.Count ? _reports[_list.SelectedIndex] : null;

    private void Reload()
    {
        _reports = _app.Db.GetReports(null, null);
        _list.Items.Clear();
        foreach (var r in _reports)
            _list.Items.Add($"{(r.ReportType == "daily" ? "日报" : "周报")}  {r.PeriodKey}  #{r.Id}");
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else _txtContent.Clear(); // 删空后清掉残留内容，避免"复制全文"复制到幽灵文本
    }

    private void ShowSelected()
    {
        _txtContent.Text = Selected?.Content ?? "";
    }

    private void SaveEdit()
    {
        if (Selected is not ReportEntry entry) return;
        entry.Content = _txtContent.Text;
        _app.Reports.UpdateReport(entry);
        _app.Tray.ShowBalloon("报告已保存");
    }

    /// <summary>M4-6：流水改过后，重新复制"提示词+最新数据"，再走 AI 流程粘贴回来覆盖。</summary>
    private void ReCopyPrompt()
    {
        if (Selected is not ReportEntry entry) return;
        string prompt;
        if (entry.ReportType == "daily")
        {
            var date = DateTime.TryParse(entry.PeriodKey, out var d) ? d : DateTime.Today;
            prompt = _app.Reports.BuildDailyPrompt(date);
        }
        else
        {
            // PeriodKey 形如 2026-W38，解析回该周中的一天；失败则退到报告创建日期
            var date = _app.Reports.ParseWeekKey(entry.PeriodKey)
                ?? (DateTime.TryParse(entry.CreatedAt, out var cd) ? cd : DateTime.Today);
            prompt = _app.Reports.BuildWeeklyPrompt(date);
        }
        try
        {
            Clipboard.SetText(prompt);
            _app.Tray.ShowBalloon("提示词+最新数据已复制，请粘贴给 AI 生成后再导入");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "复制失败，剪贴板被其他程序占用：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not ReportEntry entry) return;
        if (MessageBox.Show(this, $"确定删除 {entry.PeriodKey} 的这份报告？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _app.Reports.DeleteReport(entry);
        Reload();
    }
}
