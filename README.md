# TickNote（久坐提醒 + 工作流水记录）

Windows 桌面小工具：每隔 N 分钟（默认 50）全屏提醒起身活动，弹窗里顺手记一句"在做什么"，
一天的工作流水自动串起来；每天/每周一键复制"提示词+数据"给 AI，生成日报/周报后粘贴回来保存。

- 平台：Windows 10/11 x64，单 EXE，免安装，目标机零依赖（运行时已内嵌）
- 技术栈：C# 12 / WinForms / .NET 8（PerMonitorV2 DPI 感知），SQLite 本地存储，不联网

## 环境要求（仅开发/打包的电脑需要）

只需安装 **.NET 8 SDK**（<https://dotnet.microsoft.com/zh-cn/download>，选 SDK / Windows x64）。
不需要 Visual Studio。验证：

```
dotnet --version    :: 显示 8.x.x 即可
```

## 编译（调试用）

```
dotnet build SedentaryReminder.sln
```

回归检查（流水日期、在线数据库导出与迁移、周报期次、报告副本）：

```
dotnet run --project tests\RegressionTests.csproj
```

## 打包（产出单文件 EXE）

双击根目录的 **build.bat**，或手动执行：

```
dotnet publish src\SedentaryReminder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

产物：`dist\TickNote-vX.Y.Z.exe`，例如 `dist\TickNote-v1.0.3.exe`（约 65 MB 单文件，拷到任何电脑双击即用）。
文件名仅使用 ASCII 字母、数字、连字符和点，避免 Windows 路径兼容问题。

## 数据位置

首次运行向导二选一（之后可在"设置 → 数据"里更换，自动迁移）：

- **便携模式**：数据存 `EXE所在目录\data\`，整个文件夹拷走即用
- **自定义目录**：所有数据只放所选目录；EXE 旁的 `boot.config.json` 记录数据目录位置

数据目录结构（自包含，拷走 = 完整迁移）：

```
数据目录\
├── data.db        SQLite 数据库（流水 work_log / 提醒事件 reminder_event / 报告 report）
├── config.json    全部设置
├── exports\       导入的报告 Markdown 副本
└── backup\        自动备份（每天首次启动一份，保留最近 7 份）
```

## 目录结构

```
SedentaryReminder.sln     解决方案
build.bat                 双击打包单文件 EXE 到 dist\
src\
├── SedentaryReminder.csproj
├── Assets\               TickNote.ico / TickNote.png 软件图标（exe 图标 + 嵌入资源）
├── Program.cs            入口（PerMonitorV2、单实例）
├── AppContext.cs         主上下文：引导 + 各模块接线
├── NativeMethods.cs      Win32：空闲检测 / 焦点 / 全屏应用检测
├── Data\                 M0/M6 引导配置、设置、SQLite、备份、迁移
├── Reminder\             M1 定时提醒引擎（状态机）
├── Overlay\              M2 多屏全屏弹窗（状态同步、倒计时、今日流水）
├── Log\                  M3/M4 流水：补记、流水视图
├── Report\               M4 报告：提示词组装、导入、导出、报告视图
└── Settings\             M5 托盘、设置、关于、开机自启、M0 向导
```

UI 全部为手写布局代码（TableLayoutPanel/FlowLayoutPanel/Dock），无设计器文件、无 .resx。

## 使用速览

- 到点全屏弹窗：输入框记一句 → 点「刚活动完 / 跳过 / 推迟X分钟」任一按钮即保存
- 「今天已记录」列表里点「继续」= 一键续记同一件事
- 托盘右键：查看今日 / 补记一条 / 我的报告 / 生成日报·周报（复制提示词）/ 导入报告 / 暂停提醒 / 设置
- 日报流程：托盘「生成日报」→ 粘贴给 AI → 复制 AI 结果 → 托盘「导入报告」粘贴保存
