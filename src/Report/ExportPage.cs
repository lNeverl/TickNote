namespace SedentaryReminder.Report;

/// <summary>
/// 原始数据导出（M4-7）：流水按日期范围导出 Markdown / CSV（不经过 AI 的原始备份）。
/// </summary>
public sealed class ExportPage : WorkspacePage
{
    private readonly AppContext _app;
    private readonly DateTimePicker _dtFrom;
    private readonly DateTimePicker _dtTo;
    private readonly RadioButton _rbMd;
    private readonly RadioButton _rbCsv;

    public ExportPage(AppContext app)
    {
        _app = app;
        Text = "导出原始流水";
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14), ColumnCount = 2, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _dtFrom = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 136, MinimumSize = new Size(136, 0), Value = DateTime.Today.AddDays(-7) };
        _dtTo = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 136, MinimumSize = new Size(136, 0), Value = DateTime.Today };
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

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var btnExport = UiLayout.Button("导出…", primary: true);
        btnExport.Click += OnExport;
        btnRow.Controls.Add(btnExport);
        root.Controls.Add(btnRow, 1, 3);

        Controls.Add(root);
    }

    private void OnExport(object? sender, EventArgs e)
    {
        if (_dtFrom.Value.Date > _dtTo.Value.Date)
        {
            MessageBox.Show(FindForm(), "起始日期不能晚于结束日期。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var format = _rbCsv.Checked ? "csv" : "md";
        using var dlg = new SaveFileDialog
        {
            Filter = format == "csv" ? "CSV 文件|*.csv" : "Markdown 文件|*.md",
            FileName = $"工作流水_{_dtFrom.Value:yyyyMMdd}_{_dtTo.Value:yyyyMMdd}.{format}",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            _app.Reports.ExportLogs(
                _dtFrom.Value.ToString("yyyy-MM-dd"),
                _dtTo.Value.ToString("yyyy-MM-dd"),
                format, dlg.FileName);
            MessageBox.Show(FindForm(), "已导出到：\n" + dlg.FileName, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
