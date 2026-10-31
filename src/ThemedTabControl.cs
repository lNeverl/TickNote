namespace SedentaryReminder;

internal sealed class ThemedTabControl : TabControl
{
    internal ThemedTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        DrawItem += (_, e) =>
        {
            var active = e.Index == SelectedIndex;
            using var background = new SolidBrush(UiLayout.PageBack);
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, e.Bounds,
                active ? UiLayout.Accent : UiLayout.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (active)
            {
                using var pen = new Pen(UiLayout.Accent, Math.Max(2, UiLayout.Scale(this, 2)));
                e.Graphics.DrawLine(pen, e.Bounds.Left + 6, e.Bounds.Bottom - 2, e.Bounds.Right - 6, e.Bounds.Bottom - 2);
            }
        };
        UpdateMetrics();
    }
    private void UpdateMetrics() => ItemSize = new Size(0, Math.Max(UiLayout.Scale(this, 34), Font.Height + UiLayout.Scale(this, 16)));
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateMetrics(); }
}
