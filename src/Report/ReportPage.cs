using SedentaryReminder.Data;

namespace SedentaryReminder.Report;

/// <summary>
/// 报告视图（M4-2/M4-6）：日报、周报按分类归档展示已保存的报告，
/// 可查看、再编辑、一键复制全文去提交，可"重新复制提示词+最新数据"再走一遍 AI 流程。
/// </summary>
public sealed class ReportPage : WorkspacePage
{
    private readonly AppContext _app;
    private readonly ListBox _listDaily;
    private readonly ListBox _listWeekly;
    private readonly TextBox _txtContent;
    private readonly Button _btnSave;
    private readonly TabControl _types;
    private readonly Label _reportTitle;
    private int _activeType;

    private List<ReportEntry> _daily = new();
    private List<ReportEntry> _weekly = new();
    private bool _syncingSelection;
    private long? _loadedId;
    private string _loadedContent = "";

    public override bool HasUnsavedChanges => _loadedId.HasValue && _txtContent.Text != _loadedContent;
    public override void OnActivated() { if (!HasUnsavedChanges) Reload(); }

    public ReportPage(AppContext app)
    {
        _app = app;
        Text = "我的报告";
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _types = new ThemedTabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        var dailyTab = new TabPage("日报") { Padding = new Padding(4, 12, 4, 4) };
        var weeklyTab = new TabPage("周报") { Padding = dailyTab.Padding };
        _types.TabPages.AddRange(new[] { dailyTab, weeklyTab });
        _listDaily = new ListBox { Dock = DockStyle.Fill };
        _listWeekly = new ListBox { Dock = DockStyle.Fill };
        // 分类互斥选中，保存/删除始终作用于右侧正在查看的报告。
        _listDaily.SelectedIndexChanged += (_, _) => OnListSelected(_listDaily, _listWeekly);
        _listWeekly.SelectedIndexChanged += (_, _) => OnListSelected(_listWeekly, _listDaily);
        dailyTab.Controls.Add(_listDaily);
        weeklyTab.Controls.Add(_listWeekly);
        root.Controls.Add(_types, 0, 0);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _reportTitle = new Label { Text = "选择一份报告", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 16) };
        editor.Controls.Add(_reportTitle, 0, 0);

        _txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            PlaceholderText = "选择一份报告查看；新报告请到“导入报告”保存。",
        };
        editor.Controls.Add(_txtContent, 0, 1);
        root.Controls.Add(editor, 1, 0);

        // 窗口最小宽度保证正常情况按钮一行放下；WrapContents 兜底：DPI 切换后按钮尺寸
        // 短暂失真时允许折行，宁可换行也不裁掉
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        root.SetColumnSpan(btnRow, 2);

        // 所有按钮同字体同 Padding，AutoSize 高度一致，风格统一
        _btnSave = UiLayout.Button("保存修改", primary: true);
        _btnSave.Click += (_, _) => SaveEdit();
        var btnCopyAll = UiLayout.Button("复制全文（去提交）");
        btnCopyAll.Click += (_, _) =>
        {
            if (_txtContent.Text.Length == 0) return;
            try
            {
                Clipboard.SetText(_txtContent.Text);
                _app.Notify("报告全文已复制到剪贴板");
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "复制失败，剪贴板被其他程序占用：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        var btnRePrompt = UiLayout.Button("重新复制提示词+最新数据");
        btnRePrompt.Click += (_, _) => ReCopyPrompt();
        var btnDelete = UiLayout.Button("删除");
        btnDelete.Click += (_, _) => DeleteSelected();

        btnRow.Controls.Add(_btnSave);
        btnRow.Controls.Add(btnCopyAll);
        btnRow.Controls.Add(btnRePrompt);
        btnRow.Controls.Add(btnDelete);
        root.Controls.Add(btnRow, 0, 1);

        _types.Selecting += (_, e) => { if (!_syncingSelection && !ConfirmExit()) e.Cancel = true; };
        _types.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingSelection) return;
            _activeType = _types.SelectedIndex;
            _syncingSelection = true;
            try
            {
                var active = _activeType == 0 ? _listDaily : _listWeekly;
                var other = _activeType == 0 ? _listWeekly : _listDaily;
                other.ClearSelected();
                if (active.Items.Count > 0 && active.SelectedIndex < 0) active.SelectedIndex = 0;
            }
            finally { _syncingSelection = false; }
            ShowSelected();
        };

        Controls.Add(root);
        Reload();
    }

    private void OnListSelected(ListBox active, ListBox other)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        other.ClearSelected();
        _syncingSelection = false;
        if (Selected?.Id != _loadedId && !ConfirmExit())
        {
            RestoreSelection(_loadedId);
            return;
        }
        ShowSelected();
    }

    private ReportEntry? Selected =>
        _listDaily.SelectedIndex >= 0 && _listDaily.SelectedIndex < _daily.Count ? _daily[_listDaily.SelectedIndex]
        : _listWeekly.SelectedIndex >= 0 && _listWeekly.SelectedIndex < _weekly.Count ? _weekly[_listWeekly.SelectedIndex]
        : null;

    private void Reload()
    {
        var selectedId = _loadedId;
        _syncingSelection = true;
        try
        {
            var all = _app.Db.GetReports(null, null);
            _daily = all.Where(r => r.ReportType == "daily").ToList();
            _weekly = all.Where(r => r.ReportType != "daily").ToList();
            _listDaily.Items.Clear();
            _listWeekly.Items.Clear();
            foreach (var r in _daily) _listDaily.Items.Add($"{r.PeriodKey}  #{r.Id}");
            foreach (var r in _weekly) _listWeekly.Items.Add($"{r.PeriodKey}  #{r.Id}");
            var dailyIndex = _daily.FindIndex(entry => entry.Id == selectedId);
            var weeklyIndex = _weekly.FindIndex(entry => entry.Id == selectedId);
            if (dailyIndex >= 0) _listDaily.SelectedIndex = dailyIndex;
            else if (weeklyIndex >= 0) _listWeekly.SelectedIndex = weeklyIndex;
            else if (_activeType == 1 && _weekly.Count > 0) _listWeekly.SelectedIndex = 0;
            else if (_daily.Count > 0) _listDaily.SelectedIndex = 0;
            else if (_weekly.Count > 0) _listWeekly.SelectedIndex = 0;
            if (_listDaily.SelectedIndex >= 0) _types.SelectedIndex = 0;
            else if (_listWeekly.SelectedIndex >= 0) _types.SelectedIndex = 1;
            _activeType = _types.SelectedIndex;
        }
        finally { _syncingSelection = false; }
        ShowSelected();
    }

    private void RestoreSelection(long? id)
    {
        _syncingSelection = true;
        try
        {
            _listDaily.SelectedIndex = _daily.FindIndex(entry => entry.Id == id);
            _listWeekly.SelectedIndex = _weekly.FindIndex(entry => entry.Id == id);
            if (_listDaily.SelectedIndex >= 0) _types.SelectedIndex = 0;
            else if (_listWeekly.SelectedIndex >= 0) _types.SelectedIndex = 1;
            _activeType = _types.SelectedIndex;
        }
        finally { _syncingSelection = false; }
    }

    private void ShowSelected()
    {
        _loadedId = Selected?.Id;
        _reportTitle.Text = Selected is { } report ? $"{report.PeriodKey} · {(report.ReportType == "daily" ? "日报" : "周报")}" : "还没有报告，可先生成再导入";
        _loadedContent = UiLayout.MultilineText(Selected?.Content);
        _txtContent.Text = _loadedContent;
        _txtContent.ReadOnly = !_loadedId.HasValue;
        _btnSave.Enabled = _loadedId.HasValue;
    }

    private void SaveEdit() => SaveChanges();

    public override bool SaveChanges()
    {
        var entry = _daily.Concat(_weekly).FirstOrDefault(entry => entry.Id == _loadedId);
        if (entry == null) return !HasUnsavedChanges;
        try
        {
            var updated = new ReportEntry { Id = entry.Id, ReportType = entry.ReportType, PeriodKey = entry.PeriodKey, Content = _txtContent.Text };
            _app.Reports.UpdateReport(updated);
            entry.Content = updated.Content;
            _loadedContent = updated.Content;
            _app.Notify("报告已保存");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "保存报告失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
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
            _app.Notify("提示词+最新数据已复制，请粘贴给 AI 生成后再导入");
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "复制失败，剪贴板被其他程序占用：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelected()
    {
        if (Selected is not ReportEntry entry) return;
        if (!ConfirmExit()) return;
        if (MessageBox.Show(FindForm(), $"确定删除 {entry.PeriodKey} 的这份报告？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _app.Reports.DeleteReport(entry);
        _loadedId = null;
        Reload();
    }
}
