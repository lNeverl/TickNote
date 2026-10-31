# WinForms 界面实现与验证

2026-10-31。本次沿用 .NET 8 WinForms、SQLite 和现有业务服务，没有新增 NuGet 运行依赖。HTML 保留为设计参考，实际程序使用原生控件。

## 已实现

- 主窗口十个页面共用导航、标题、白色内容面板、蓝色主操作和统一间距；页面按需创建并缓存，切页保留草稿和筛选。
- 待办使用紧凑的日期范围、录入区和列表；历史支持自适应筛选栏、全文预览和空结果说明。
- 报告使用日报/周报分类归档及正文编辑区；分类切换处理未保存修改。提示词、报告、历史预览等多行文本转换为 Windows 换行格式。
- 提醒控制展示计时状态、进度、暂停/恢复和立即活动入口；状态来自同一个提醒引擎。
- 设置保留五个分类，字段根据可用宽度上下排列，长路径换行；保存和还原位于滚动区之外。
- 到时提醒使用浅色卡片；正文在低高度时滚动，底部三个活动操作始终独立。首次向导、选择器、编辑窗口和临时收起窗口复用原生样式。
- 文件夹选择、另存为、确认和错误提示继续使用 Windows 标准对话框，其系统外观由 Windows 决定。

主要共用组件：`UiLayout`、`DpiForm`、`WorkspacePage`、`FormFieldRow`、`ThemedListView`、`ThemedTabControl`。

## 架构判断与针对性修正

现有 `ReminderEngine → AppContext → OverlayManager` 的提醒链路、页面与数据服务的分工可以继续使用，不需要重写框架或引入新的 UI 技术栈。

修正了实际边界问题：系统锁屏/显示器事件统一派发到 UI 线程；提醒窗口显示失败时恢复引擎状态并推迟重试；退出时释放引擎计时器；设置脏状态只比较可编辑值，不受新增容器影响。

### 到时弹窗为何先灰屏

引擎每秒检查工作时段、暂停、空闲、锁屏与勿扰规则，周期到点后发出 `FireRequested`，由应用上下文打开提醒。每块显示器有一层半透明遮罩和一层不透明卡片，输入、标签、活动倒计时和记录状态由管理器共享。

原来先显示遮罩，卡片的原生窗口句柄、字体、布局和首次绘制随后才完成，且卡片另有淡入计时器，因此出现灰屏先到、文字后到的间隔。

现在先在透明状态下准备所有屏幕的句柄、内容、布局和首帧，再先呈现卡片、随后呈现遮罩；移除了独立淡入等待。启动预热仍保留。准备工作本身仍需要时间，但不会先用空遮罩挡住桌面。活动倒计时改用实际经过时间，避免 UI 忙碌时每次只减一秒而变慢。活动时间归零仍不自动关闭或保存。

### 本次 .NET 位图异常

`ImageList` 在原生句柄创建时才读取加入的原图。行高占位位图在加入后立即释放，会在 `ImageList.CreateBitmap` 中触发 `Parameter is not valid`。现改为由列表持有原图，并在清理图片或释放控件时释放；回归覆盖延迟加载、字体变化和句柄重建。测试进程的 UI 异常直接输出堆栈并以失败退出，不再弹出 JIT 调试框。

## 验证记录与边界

- Debug 编译通过，0 警告、0 错误。
- 全量回归已在 PerMonitorV2 和 `--dpi-unaware` 模式下通过，覆盖数据、报告、待办、草稿、页面路由、提醒收起/恢复和显示器集合重建等流程。
- 最终界面修正后，隐藏窗口布局检查在本机实际 192 DPI（4K、200%）和 96 DPI 模式下通过。后者用于 100% 逻辑布局检查，没有更改用户的系统显示设置。
- 程序化 DPI 比例往返检查覆盖 96 / 120 / 144 / 168 / 192 / 240，验证重复缩放及尺寸漂移；这不能替代对应系统设置下的实机截图。
- 截图在布局完成后通过 `DrawToBitmap` 保存，再打开图片核对；已据此修正提醒正文横向偏移、标签区被压为零高度和设置按钮异常拉高等问题。
- 本机只有一个显示器。真实的混合 DPI 跨屏拖动、第二块屏幕上的合成效果、系统单独放大“文本大小”和所有极端小工作区，仍需实机验收。软件状态的屏幕重建回归不能代替这些检查。

## 不打扰桌面的布局复核

```powershell
dotnet run --project tests\RegressionTests.csproj -- --layout-only --screenshots=artifacts\ui-review
dotnet run --project tests\RegressionTests.csproj -- --layout-only --dpi-unaware --screenshots=artifacts\ui-review-100
```

此模式使用透明窗口，不调用全屏遮罩的呈现流程，检查页面、设置分类、选择器、向导、列表图片生命周期和提醒卡片的布局。截图保存在指定目录的 `dialogs` 子目录。它不代替全量业务回归。

完整测试使用 `dotnet run --project tests\RegressionTests.csproj`，其中提醒打开、收起、恢复的流程会短暂显示真实窗口，桌面可能闪动。

本次可运行产物位于 `artifacts/ui-redesign/TickNote.exe`；截图位于 `artifacts/ui-current/` 和 `artifacts/ui-100/`。这些本机构建和测试产物不纳入 Git。

## 2026-11-01：v1.0.4 待办启动修复

修复打开主界面时 `ListView.WmReflectNotify` 抛出 `ArgumentNullException(item)` 的问题。

复现条件是消息循环已运行、今天没有待办、历史日期存在待办，首次进入“全部待办”时原本隐藏的列表创建原生句柄。原来的加载标记在设置列表可见性之前就被清除了，原生控件恢复勾选状态的通知被当成用户操作，导致写库并同步重入 `Reload`，破坏了尚在插入的列表项映射。

现在加载保护覆盖可见性切换和布局，句柄创建/重建期间以及尚无有效窗口时的状态通知不触发业务写入；实际用户勾选仍异步保存。没有用捕获并忽略异常来掩盖问题。

验证使用独立临时数据库和真实 WinForms 消息循环，覆盖今天为空、过去日期有 0 / 1 / 2 / 5 / 60 条待办、混合完成状态、筛选、切页、收起重开和句柄重建。还对现有数据创建只读备份副本，复现旧错误并验证修复。测试在排空消息队列后确认：上述查看操作没有触发 `TodosChanged`，数据库快照也没有变化。原始数据目录未用于写入测试。

针对本次修复执行了 Release 待办功能回归、当前 192 DPI 与 96 DPI 模式的启动回归、隐藏窗口布局检查。发布前再次执行了 Release 编译及两种 DPI 模式下的全量回归，全部通过；全量回归已纳入跨天启动场景。真实混合 DPI 多屏和系统独立文本缩放仍保留为实机验收项。

```powershell
dotnet run --project tests\RegressionTests.csproj -c Release -- --startup-only
dotnet run --project tests\RegressionTests.csproj -c Release -- --startup-only --dpi-unaware
dotnet run --project tests\RegressionTests.csproj -c Release -- --todo-only
```

可选 `--source-data=dist\data` 会以 SQLite 只读连接备份到临时目录，并仅在副本上验证启动。UI 测试的每个 STA 线程都设置了异常输出模式，避免测试失败时弹出 JIT 对话框。

修复版发布目录：`artifacts/ui-fix-1.0.4/`。本机常用的 `dist/TickNote.exe` 和先前的 `artifacts/ui-redesign/TickNote.exe` 同步更新为 1.0.4，旧版程序保存在 `artifacts/previous-builds/`。
