namespace SedentaryReminder.Report;

/// <summary>
/// 原始数据导出（M4-7）：流水按日期范围导出 Markdown / CSV（不经过 AI 的原始备份）。
/// </summary>
public sealed class ExportForm : DpiForm
{
    private readonly AppContext _app;
    private readonly DateTimePicker _dtFrom;
    private readonly DateTimePicker _dtTo;
    private readonly RadioButton _rbMd;
    private readonly RadioButton _rbCsv;

    public ExportForm(AppContext app)
    {
        _app = app;
        Text = "导出原始流水";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 190);
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _dtFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Value = DateTime.Today.AddDays(-7) };
        _dtTo = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Value = DateTime.Today };
        root.Controls.Add(new Label { Text = "从", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        root.Controls.Add(_dtFrom, 1, 0);
        root.Controls.Add(new Label { Text = "到", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        root.Controls.Add(_dtTo, 1, 1);

        var fmtRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; // DPI 切换后按钮尺寸可能短暂失真，允许折行兜底：宁可换行也不裁掉
        _rbMd = new RadioButton { Text = "Markdown", Checked = true, AutoSize = true };
        _rbCsv = new RadioButton { Text = "CSV", AutoSize = true };
        fmtRow.Controls.Add(_rbMd);
        fmtRow.Controls.Add(_rbCsv);
        root.Controls.Add(new Label { Text = "格式", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        root.Controls.Add(fmtRow, 1, 2);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var btnExport = new Button { Text = "导出…", AutoSize = true };
        btnExport.Click += OnExport;
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        btnRow.Controls.Add(btnExport);
        btnRow.Controls.Add(btnCancel);
        root.Controls.Add(btnRow, 1, 3);

        Controls.Add(root);
        AcceptButton = btnExport;
        CancelButton = btnCancel;
    }

    private void OnExport(object? sender, EventArgs e)
    {
        if (_dtFrom.Value.Date > _dtTo.Value.Date)
        {
            MessageBox.Show(this, "起始日期不能晚于结束日期。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var format = _rbCsv.Checked ? "csv" : "md";
        using var dlg = new SaveFileDialog
        {
            Filter = format == "csv" ? "CSV 文件|*.csv" : "Markdown 文件|*.md",
            FileName = $"工作流水_{_dtFrom.Value:yyyyMMdd}_{_dtTo.Value:yyyyMMdd}.{format}",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _app.Reports.ExportLogs(
                _dtFrom.Value.ToString("yyyy-MM-dd"),
                _dtTo.Value.ToString("yyyy-MM-dd"),
                format, dlg.FileName);
            MessageBox.Show(this, "已导出到：\n" + dlg.FileName, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
