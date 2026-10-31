namespace SedentaryReminder;

/// <summary>按可用逻辑宽度把标签/字段改为上下排列，不依赖固定的标签列宽。</summary>
internal sealed class FormFieldRow : TableLayoutPanel
{
    private readonly Label _label;
    private readonly Control _field;
    private bool _stacked;

    internal FormFieldRow(string caption, Control field)
    {
        _field = field;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        RowCount = 2;
        Padding = new Padding(0, 9, 0, 9);
        Margin = Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _label = new Label { Text = caption, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 16, 4) };
        field.Margin = new Padding(0, 4, 0, 4);
        field.Dock = field is TextBox or Label or FlowLayoutPanel ? DockStyle.Fill : DockStyle.None;
        if (field.Dock == DockStyle.None) field.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Controls.Add(_label, 0, 0);
        Controls.Add(field, 1, 0);
        BackColor = UiLayout.Surface;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_label == null || _field == null) return;
        var stacked = ClientSize.Width < UiLayout.Scale(this, 460);
        if (stacked != _stacked)
        {
            _stacked = stacked;
            SuspendLayout();
            SetColumnSpan(_label, stacked ? 2 : 1);
            SetColumnSpan(_field, stacked ? 2 : 1);
            SetColumn(_field, stacked ? 0 : 1);
            SetRow(_field, stacked ? 1 : 0);
            ResumeLayout(true);
        }
        if (_field is Label label)
            label.MaximumSize = new Size(Math.Max(1, (int)(ClientSize.Width * (stacked ? 1 : .45)) - label.Margin.Horizontal), 0);
    }
}
