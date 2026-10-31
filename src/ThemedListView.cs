namespace SedentaryReminder;

/// <summary>保留原生选择、勾选与键盘行为；统一表头和按实际 DPI 测量的行高。</summary>
internal class ThemedListView : ListView
{
    private readonly ImageList _rowImages = new() { ColorDepth = ColorDepth.Depth32Bit };
    private Bitmap? _rowBitmap;

    internal ThemedListView()
    {
        BorderStyle = BorderStyle.None;
        BackColor = UiLayout.Surface;
        ForeColor = UiLayout.Text;
        DoubleBuffered = true;
        OwnerDraw = true;
        DrawItem += (_, e) => e.DrawDefault = true;
        DrawSubItem += (_, e) => e.DrawDefault = true;
        DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(UiLayout.Sidebar);
            e.Graphics.FillRectangle(brush, e.Bounds);
            using var pen = new Pen(UiLayout.Border);
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var bounds = Rectangle.Inflate(e.Bounds, -UiLayout.Scale(this, 8), 0);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, Font, bounds, UiLayout.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        SmallImageList = _rowImages;
        UpdateRowHeight();
    }

    private void UpdateRowHeight()
    {
        if (_rowImages == null || IsDisposed) return;
        var height = Math.Clamp(Math.Max(UiLayout.Scale(this, 36), Font.Height + UiLayout.Scale(this, 12)), 16, 256);
        if (_rowImages.ImageSize.Height == height && _rowImages.Images.Count > 0) return;
        _rowImages.Images.Clear();
        _rowBitmap?.Dispose();
        _rowImages.ImageSize = new Size(1, height);
        // ImageList 会延迟到创建原生句柄时读取原图，不能在 Add 后立即释放。
        _rowBitmap = new Bitmap(1, height);
        _rowImages.Images.Add(_rowBitmap);
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateRowHeight(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateRowHeight(); }
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { base.ScaleControl(factor, specified); UpdateRowHeight(); }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _rowImages.Dispose(); _rowBitmap?.Dispose(); _rowBitmap = null; }
    }
}
