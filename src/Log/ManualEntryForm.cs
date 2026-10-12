using SedentaryReminder.Data;

namespace SedentaryReminder.Log;

/// <summary>
/// 手动补记 / 编辑流水（M3-6）：可补写内容并手改时间段。
/// 传入已有条目时为编辑模式，否则新增一条 source=manual 的记录。
/// </summary>
public sealed class ManualEntryForm : DpiForm
{
    private readonly Database _db;
    private readonly WorkLogEntry? _editing;
    private readonly DateTimePicker _dtDate;
    private readonly DateTimePicker _dtStart;
    private readonly DateTimePicker _dtEnd;
    private readonly ComboBox _cmbTag;
    private readonly TextBox _txtContent;
    private readonly CheckBox _syncRelated;
    private long? _continueFromId;

    public ManualEntryForm(Database db, AppConfig config, WorkLogEntry? editing = null, DateTime? defaultDate = null)
    {
        _db = db;
        _editing = editing;

        Text = editing == null ? "补记一条" : "编辑记录";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 380);
        Font = new Font("Microsoft YaHei UI", 9F);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 7 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _dtDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120 };
        _dtStart = MakeTimePicker();
        _dtEnd = MakeTimePicker();
        _cmbTag = new ComboBox { Dock = DockStyle.Fill };
        foreach (var t in config.Tags) _cmbTag.Items.Add(t);
        _txtContent = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

        grid.Controls.Add(MakeLabel("日期"), 0, 0);
        grid.Controls.Add(_dtDate, 1, 0);
        grid.Controls.Add(MakeLabel("时间段"), 0, 1);
        var timeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; // DPI 切换后按钮尺寸可能短暂失真，允许折行兜底：宁可换行也不裁掉
        timeRow.Controls.Add(_dtStart);
        timeRow.Controls.Add(new Label { Text = "至", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 6, 0) });
        timeRow.Controls.Add(_dtEnd);
        grid.Controls.Add(timeRow, 1, 1);
        grid.Controls.Add(MakeLabel("标签"), 0, 2);
        grid.Controls.Add(_cmbTag, 1, 2);
        grid.Controls.Add(MakeLabel("内容"), 0, 3);
        var chooseHistory = new Button { Text = "从全部历史记录选择…", AutoSize = true, Visible = editing == null };
        chooseHistory.Click += (_, _) =>
        {
            using var picker = new WorkLogPickerForm(_db);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedEntry == null) return;
            _continueFromId = picker.SelectedEntry.Id;
            _txtContent.Text = picker.SelectedEntry.Content ?? "";
            _cmbTag.Text = picker.SelectedEntry.Tag ?? "";
        };
        grid.Controls.Add(chooseHistory, 1, 3);
        grid.Controls.Add(_txtContent, 1, 4);

        var relatedCount = editing == null ? 0 : _db.GetRelatedLogs(editing.Id).Count;
        _syncRelated = new CheckBox
        {
            Text = $"同步修改同一事项（共 {relatedCount} 条，含当前记录）",
            AutoSize = true, Checked = relatedCount > 1, Enabled = relatedCount > 1,
        };
        var scope = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = editing != null };
        scope.Controls.Add(_syncRelated);
        scope.Controls.Add(new Label { Text = "同步内容和标签；日期、时间只修改当前条。", AutoSize = true });
        grid.Controls.Add(scope, 1, 5);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var btnSave = new Button { Text = "保存", AutoSize = true };
        btnSave.Click += OnSave;
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        btnRow.Controls.Add(btnSave);
        btnRow.Controls.Add(btnCancel);
        grid.Controls.Add(btnRow, 1, 6);

        Controls.Add(grid);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        // 初始值
        var date = defaultDate ?? DateTime.Today;
        if (editing != null && DateTime.TryParse(editing.LogDate, out var d)) date = d;
        _dtDate.Value = date;
        _dtStart.Value = ParseTimeOr(editing?.StartTime, DateTime.Now.AddHours(-1));
        _dtEnd.Value = ParseTimeOr(editing?.EndTime, DateTime.Now);
        if (editing?.Tag is { Length: > 0 } tag) _cmbTag.Text = tag;
        _txtContent.Text = editing?.Content ?? "";
    }

    private static DateTimePicker MakeTimePicker() => new()
    {
        Format = DateTimePickerFormat.Time,
        ShowUpDown = true,
        Width = 90,
    };

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 8, 0),
    };

    private static DateTime ParseTimeOr(string? hhmm, DateTime fallback)
        => hhmm != null && TimeSpan.TryParse(hhmm, out var t) ? DateTime.Today + t : fallback;

    private void OnSave(object? sender, EventArgs e)
    {
        var content = _txtContent.Text.Trim();
        if (content.Length == 0)
        {
            MessageBox.Show(this, "内容不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entry = new WorkLogEntry
        {
            Id = _editing?.Id ?? 0,
            Source = _editing?.Source ?? (_continueFromId.HasValue ? "continue" : "manual"),
            ContinueFrom = _editing?.ContinueFrom ?? _continueFromId,
        };
        entry.LogDate = _dtDate.Value.ToString("yyyy-MM-dd");
        entry.StartTime = _dtStart.Value.ToString("HH:mm");
        entry.EndTime = _dtEnd.Value.ToString("HH:mm");
        entry.Tag = _cmbTag.Text.Trim().Length > 0 ? _cmbTag.Text.Trim() : null;
        entry.Content = content;

        try
        {
            if (_editing == null) _db.InsertWorkLog(entry);
            else _db.UpdateWorkLog(entry, _syncRelated.Checked);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
