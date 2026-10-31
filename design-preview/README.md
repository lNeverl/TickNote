# TickNote 原生界面设计预览

这里保留 WinForms 界面的布局、配色和交互设计参考。对应的原生界面已在 v1.0.4 中实现，没有新增第三方 C# UI 库；实现范围和验证记录见 [WinForms 实现与验证](WinForms实现与验证.md)。

`ticknote-native.html` 是最初的交互预览源文件；`index.html` 是供浏览器打开的完整预览。本地 `.review` 目录只保存浏览器检查脚本和截图，不纳入版本控制。

`ticknote-complete.html` / `complete.html` 为后续补齐的全部界面版本，包含 54 个页面与状态，上方目录可逐项预览。保留已经认可的主界面风格，并补齐完整提醒、选择器、编辑、设置、首次启动、托盘、确认提示、空状态与长内容。

完整覆盖与后续 WinForms 高 DPI 验收要求见 `UI清单与缩放规范.md`。浏览器宽度检查不等同于真实 WinForms 的 DPI 测试。

## 设计范围

- 保留目前的十个功能入口，按工作、报告、工具分组，设置与关于放在底部。
- 保留灰白底色与蓝色强调色，统一字号、按钮层级、表单间距、分割线和留白。
- 预览待办录入与完成、工作流水、补记表单、报告编辑与导入、生成报告、导出选项、提醒控制、设置和提醒弹窗。
- 数据全部是虚构示例，只在当前预览中演示；刷新后恢复。提醒倒计时为示意，设置和导出按钮不操作系统或真实文件。
- 设置页展示主要布局样式；正式实现时仍需呈现当前应用的全部设置项。

## 对照现有 WinForms 实现

| 预览元素 | 现有框架中的实现方式 |
| --- | --- |
| 左侧分组导航 | Panel、FlowLayoutPanel、Label、现有平面 Button |
| 主窗口布局、页眉和页脚 | TableLayoutPanel、Panel、Label、StatusStrip |
| 平面按钮与输入框 | 现有 UiLayout.Button、TextBox、ComboBox |
| 待办与工作记录列表 | 继续改造现有 TodoBrowser、WorkLogBrowser |
| 日期、时间和状态筛选 | DateTimePicker、RadioButton、CheckBox |
| 报告列表与正文编辑 | ListBox、Multiline TextBox |
| 提醒进度、状态与操作 | Label、ProgressBar、现有 Button |
| 编辑和提醒弹窗 | 现有 DpiForm、Panel、TableLayoutPanel |
| 导航图标 | 使用 Windows 图标字体或静态图标资源，无需引入控件框架 |

HTML 是设计参考，后续对照修改现有 C# 布局代码。浏览器与 WinForms 的字体和控件细节会存在差异，最终效果需要在真实窗口及多种 DPI 下核对。窗口沿用系统标题栏。

预览中的示例日期、版本文案和数据保留设计阶段的状态，实际程序版本以项目文件和“关于”页面为准。
