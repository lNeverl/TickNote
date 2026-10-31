namespace SedentaryReminder;

/// <summary>主窗口中的独立页面；保留控件实例即可在切页时保留筛选和未保存输入。</summary>
public abstract class WorkspacePage : UserControl
{
    private bool _surfaceCreated;
    protected WorkspacePage()
    {
        AutoScaleMode = AutoScaleMode.None;
        // 构建控件时提供足够的设计宽度，避免尚未挂到宿主的页面把左对齐输入框永久挤窄。
        Size = new Size(1000, 700);
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = UiLayout.PageBack;
        Font = new Font("Microsoft YaHei UI", 9F);
    }

    protected override void OnLoad(EventArgs e)
    {
        if (!_surfaceCreated && Controls.Count == 1)
        {
            _surfaceCreated = true;
            var content = Controls[0];
            Controls.Remove(content);
            Controls.Add(UiLayout.SurfaceHost(content, padding: 0));
        }
        UiLayout.ApplyTheme(this);
        base.OnLoad(e);
    }

    public virtual bool HasUnsavedChanges => false;
    public virtual bool SaveChanges() => true;
    public virtual void OnActivated() { }

    public bool ConfirmExit(DialogResult? decision = null)
    {
        if (!HasUnsavedChanges) return true;
        var result = decision ?? MessageBox.Show(FindForm(), $"“{Text}”中有未保存的内容。\n\n是：保存并继续\n否：放弃修改\n取消：返回应用",
            "未保存的内容", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result == DialogResult.No || result == DialogResult.Yes && SaveChanges();
    }
}

/// <summary>保留独立展示能力供回归与临时任务使用，主应用直接承载页面控件。</summary>
public class WorkspacePageWindow : DpiForm
{
    protected WorkspacePageWindow(WorkspacePage page, Size clientSize, Size minimumSize)
    {
        Text = page.Text;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = clientSize;
        MinimumSize = minimumSize;
        Font = page.Font;
        Controls.Add(page);
    }
}
