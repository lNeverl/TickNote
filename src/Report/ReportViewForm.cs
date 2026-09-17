using SedentaryReminder.Data;

namespace SedentaryReminder.Report;

/// <summary>
/// 报告视图（M4-2/M4-6）：日报、周报分两栏归档展示已保存的报告，
/// 可查看、再编辑、一键复制全文去提交，可"重新复制提示词+最新数据"再走一遍 AI 流程。
/// </summary>
public sealed class ReportViewForm : DpiForm
{
    private readonly AppContext _app;
    private readonly ListBox _listDaily;
    private readonly ListBox _listWeekly;
    private readonly TextBox _txtContent;
    private readonly Button _btnSave;

    private List<ReportEntry> _daily = new();
    private List<ReportEntry> _weekly = new();
    private bool _syncingSelection;

    public ReportViewForm(AppContext app)
    {
        _app = app;
        Text = "我的报告";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 440);
        MinimumSize = new Size(640, 400); // 再小列表和按钮就挤不下了（DpiForm 会按 DPI 比率缩放它）
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 顶部说明：打开就知道这个窗口是干什么的
        var lblHint = new Label
        {
            Text = "日报、周报分两栏保存：左侧选一份，右侧查看或直接修改；改完点“保存修改”，点“复制全文”即可粘贴去提交。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
        };
        root.SetColumnSpan(lblHint, 3);
        root.Controls.Add(lblHint, 0, 0);

        var lblDaily = new Label { Text = "日报", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
        var lblWeekly = new Label { Text = "周报", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
        root.Controls.Add(lblDaily, 0, 1);
        root.Controls.Add(lblWeekly, 1, 1);

        _listDaily = new ListBox { Dock = DockStyle.Fill };
        _listWeekly = new ListBox { Dock = DockStyle.Fill };
        // 两栏互斥选中：点一栏时清掉另一栏，"保存/删除"的作用对象才有唯一答案
        _listDaily.SelectedIndexChanged += (_, _) => OnListSelected(_listDaily, _listWeekly);
        _listWeekly.SelectedIndexChanged += (_, _) => OnListSelected(_listWeekly, _listDaily);
        root.Controls.Add(_listDaily, 0, 2);
        root.Controls.Add(_listWeekly, 1, 2);

        _txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Font = new Font("Microsoft YaHei UI", 9.5F),
        };
        root.SetRowSpan(_txtContent, 2);
        root.Controls.Add(_txtContent, 2, 1);

        // WrapContents=false + 窗口最小宽度：缩放窗口时按钮始终保持一行，不再换行乱跑
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        root.SetColumnSpan(btnRow, 3);

        // 所有按钮同字体同 Padding，AutoSize 高度一致，风格统一
        var btnPadding = new Padding(10, 4, 10, 4);
        _btnSave = new Button { Text = "保存修改", AutoSize = true, Padding = btnPadding };
        _btnSave.Click += (_, _) => SaveEdit();
        var btnCopyAll = new Button { Text = "复制全文（去提交）", AutoSize = true, Padding = btnPadding };
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
        var btnRePrompt = new Button { Text = "重新复制提示词+最新数据", AutoSize = true, Padding = btnPadding };
        btnRePrompt.Click += (_, _) => ReCopyPrompt();
        var btnDelete = new Button { Text = "删除", AutoSize = true, Padding = btnPadding };
        btnDelete.Click += (_, _) => DeleteSelected();
        // 本窗体是 Show() 非模态打开的，DialogResult 不会关窗，必须显式 Close()
        var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = btnPadding };
        btnClose.Click += (_, _) => Close();

        btnRow.Controls.Add(_btnSave);
        btnRow.Controls.Add(btnCopyAll);
        btnRow.Controls.Add(btnRePrompt);
        btnRow.Controls.Add(btnDelete);
        btnRow.Controls.Add(btnClose);
        root.Controls.Add(btnRow, 0, 3);

        Controls.Add(root);
        CancelButton = btnClose;
        Reload();
    }

    private void OnListSelected(ListBox active, ListBox other)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        other.ClearSelected();
        _syncingSelection = false;
        ShowSelected();
    }

    private ReportEntry? Selected =>
        _listDaily.SelectedIndex >= 0 && _listDaily.SelectedIndex < _daily.Count ? _daily[_listDaily.SelectedIndex]
        : _listWeekly.SelectedIndex >= 0 && _listWeekly.SelectedIndex < _weekly.Count ? _weekly[_listWeekly.SelectedIndex]
        : null;

    private void Reload()
    {
        var all = _app.Db.GetReports(null, null);
        _daily = all.Where(r => r.ReportType == "daily").ToList();
        _weekly = all.Where(r => r.ReportType != "daily").ToList();
        _listDaily.Items.Clear();
        _listWeekly.Items.Clear();
        foreach (var r in _daily) _listDaily.Items.Add($"{r.PeriodKey}  #{r.Id}");
        foreach (var r in _weekly) _listWeekly.Items.Add($"{r.PeriodKey}  #{r.Id}");
        if (_daily.Count > 0) _listDaily.SelectedIndex = 0;
        else if (_weekly.Count > 0) _listWeekly.SelectedIndex = 0;
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
