namespace SedentaryReminder;

public sealed class GenerateReportPage : WorkspacePage
{
    private readonly AppContext _app;
    private readonly RadioButton _daily;
    private readonly DateTimePicker _date;
    private readonly TextBox _preview;

    public GenerateReportPage(AppContext app)
    {
        _app = app;
        Text = "生成报告";
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        _daily = new RadioButton { Text = "日报", Checked = true, AutoSize = true };
        var weekly = new RadioButton { Text = "周报", AutoSize = true };
        _date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 136, MinimumSize = new Size(136, 0) };
        filters.Controls.Add(_daily);
        filters.Controls.Add(weekly);
        filters.Controls.Add(_date);
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(new Label { Text = "复制以下提示词和数据给 AI，生成后到“导入报告”保存。", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 12, 0, 8) }, 0, 1);
        _preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, AccessibleName = "生成报告提示词预览" };
        root.Controls.Add(_preview, 0, 2);
        var copy = UiLayout.Button("复制提示词和数据", primary: true);
        copy.Click += (_, _) =>
        {
            UpdatePreview();
            try { Clipboard.SetText(_preview.Text); _app.Notify("提示词和数据已复制，请粘贴给 AI"); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(FindForm(), "剪贴板正被占用，请稍后重试。", "复制失败"); }
        };
        var import = UiLayout.Button("去导入报告");
        import.Click += (_, _) => _app.ShowImportReport();
        root.Controls.Add(UiLayout.ButtonRow(false, copy, import), 0, 3);
        Controls.Add(root);
        _daily.CheckedChanged += (_, _) => UpdatePreview();
        _date.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    public override void OnActivated() => UpdatePreview();
    private void UpdatePreview() => _preview.Text = UiLayout.MultilineText(_daily.Checked ? _app.Reports.BuildDailyPrompt(_date.Value) : _app.Reports.BuildWeeklyPrompt(_date.Value));
}
