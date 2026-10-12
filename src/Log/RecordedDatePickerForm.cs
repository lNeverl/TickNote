namespace SedentaryReminder.Log;

/// <summary>按钮日历：有记录的日期高亮，其他日期禁用。</summary>
public sealed class RecordedDatePickerForm : DpiForm
{
    private readonly HashSet<DateTime> _dates;
    private readonly List<DateTime> _months;
    private readonly ComboBox _month;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button[] _days = new Button[42];
    public DateTime? SelectedDate { get; private set; }

    public RecordedDatePickerForm(IEnumerable<DateTime> dates, DateTime? selected = null)
    {
        _dates = dates.Select(date => date.Date).ToHashSet();
        _months = _dates.Select(date => new DateTime(date.Year, date.Month, 1)).Distinct().OrderBy(date => date).ToList();
        SelectedDate = selected;
        Text = "选择有记录的日期";
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;
        ClientSize = new Size(350, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Margin = new Padding(0, 0, 0, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _previous = UiLayout.Button("‹");
        _next = UiLayout.Button("›");
        _previous.MinimumSize = _next.MinimumSize = new Size(32, 32);
        _previous.Padding = _next.Padding = Padding.Empty;
        _previous.Margin = new Padding(0, 0, 8, 0);
        _next.Margin = new Padding(8, 0, 0, 0);
        _month = new ComboBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList, Margin = Padding.Empty };
        foreach (var month in _months) _month.Items.Add(month.ToString("yyyy年MM月"));
        _previous.Click += (_, _) => _month.SelectedIndex--;
        _next.Click += (_, _) => _month.SelectedIndex++;
        _month.SelectedIndexChanged += (_, _) => RenderMonth();
        header.Controls.Add(_previous, 0, 0);
        header.Controls.Add(_month, 1, 0);
        header.Controls.Add(_next, 2, 0);
        root.Controls.Add(header, 0, 0);
        var calendar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 7, Margin = Padding.Empty };
        for (var col = 0; col < 7; col++) calendar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 7));
        calendar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var row = 0; row < 6; row++) calendar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6));
        var weekdays = new[] { "一", "二", "三", "四", "五", "六", "日" };
        for (var col = 0; col < 7; col++)
            calendar.Controls.Add(new Label { Text = weekdays[col], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6) }, col, 0);
        for (var i = 0; i < _days.Length; i++)
        {
            var day = new Button { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(2), Enabled = false };
            day.FlatAppearance.BorderSize = 0;
            day.Click += (_, _) =>
            {
                if (day.Tag is DateTime date && _dates.Contains(date))
                {
                    SelectedDate = date;
                    DialogResult = DialogResult.OK;
                }
            };
            _days[i] = day;
            calendar.Controls.Add(day, i % 7, i / 7 + 1);
        }
        root.Controls.Add(calendar, 0, 1);
        root.Controls.Add(new Label
        {
            Text = _dates.Count == 0 ? "还没有流水记录。" : "蓝色日期有记录；灰色日期不可选择。",
            AutoSize = true, Margin = new Padding(0, 8, 0, 8),
        }, 0, 2);
        var cancel = UiLayout.Button("取消");
        cancel.DialogResult = DialogResult.Cancel;
        root.Controls.Add(UiLayout.ButtonRow(true, cancel), 0, 3);
        CancelButton = cancel;
        Controls.Add(root);
        var initialMonth = selected is DateTime initial ? new DateTime(initial.Year, initial.Month, 1) : DateTime.MinValue;
        var index = _months.IndexOf(initialMonth);
        _month.SelectedIndex = _months.Count == 0 ? -1 : index >= 0 ? index : _months.Count - 1;
        RenderMonth();
    }

    private void RenderMonth()
    {
        _previous.Enabled = _month.SelectedIndex > 0;
        _next.Enabled = _month.SelectedIndex >= 0 && _month.SelectedIndex < _months.Count - 1;
        if (_month.SelectedIndex < 0) return;
        var first = _months[_month.SelectedIndex];
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var count = DateTime.DaysInMonth(first.Year, first.Month);
        for (var i = 0; i < _days.Length; i++)
        {
            var button = _days[i];
            var number = i - offset + 1;
            var inMonth = number >= 1 && number <= count;
            var date = inMonth ? first.AddDays(number - 1) : (DateTime?)null;
            button.Text = inMonth ? number.ToString() : "";
            button.Tag = date;
            button.Enabled = date is DateTime day && _dates.Contains(day);
            button.BackColor = button.Enabled
                ? date == SelectedDate?.Date ? Color.FromArgb(40, 90, 160) : Color.FromArgb(220, 237, 255)
                : Color.FromArgb(242, 242, 242);
            button.ForeColor = button.Enabled && date == SelectedDate?.Date ? Color.White : Color.FromArgb(40, 90, 160);
            button.AccessibleName = date?.ToString("yyyy-MM-dd") ?? "空白日期";
        }
    }
}
