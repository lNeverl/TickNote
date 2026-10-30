<div align="center">
  <img src="src/Assets/TickNote.png" alt="TickNote 图标" width="96">
  <h1>TickNote</h1>
  <p><strong>久坐提醒、工作流水记录与 AI 日报/周报助手</strong></p>
  <p>Windows 10/11 · .NET 8 · WinForms · SQLite · 本地优先</p>
</div>

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://www.microsoft.com/windows)

TickNote 每隔一段时间提醒你起身活动，并在提醒窗口中顺手记录“这段时间在做什么”。一天的工作流水会自动串起来；需要写日报或周报时，可以一键复制“提示词 + 数据”给任意 AI，再把生成结果粘贴回来保存。

应用不需要账号、不依赖 AI API，也不会主动上传工作数据。

## 功能亮点

- **多屏久坐提醒**：所有显示器同步显示提醒卡片，支持倒计时、跳过、推迟、活动完成和临时收起。
- **灵活提醒规则**：可配置工作与午休时段、首次启动延迟、空闲检测、锁屏暂停和全屏应用勿扰。
- **工作流水**：支持快捷标签、手动补记、跨天续记、全文搜索、历史日历和关联事项同步编辑。
- **日报/周报**：通过剪贴板与任意 AI 协作，无需 API Key；导入的报告独立保存，可随时查看、编辑和导出。
- **系统托盘**：显示提醒状态和倒计时进度，支持暂停提醒、快速补记和开机自启。
- **高 DPI 适配**：支持 PerMonitorV2、多显示器不同缩放比例和窗口布局自动调整。
- **本地存储与迁移**：SQLite 即写即存，自动备份；便携模式可直接复制整个数据目录迁移。

## 下载与使用

### 直接使用

1. 从 [Releases](../../releases) 下载 `TickNote.exe`。
2. 双击运行，无需安装，也无需另装 .NET 运行时。
3. 首次启动时选择数据保存方式：
   - **便携模式**：数据保存在 EXE 旁的 `data\` 目录。
   - **自定义目录**：数据保存在指定目录，EXE 旁的 `boot.config.json` 只记录数据目录位置。
4. 在系统托盘中找到 TickNote，右键打开功能菜单。

如果 Releases 尚未发布，也可以在 GitHub Actions 最近一次成功运行的页面下载 `TickNote-win-x64` 构建产物；下载 Actions 产物需要登录 GitHub。

> 当前发布程序未购买代码签名证书。若 Windows SmartScreen 显示提示，请先确认下载来源及 SHA-256 校验值，再自行决定是否运行。

### 升级

使用新版 `TickNote.exe` 覆盖旧文件即可。固定文件名可以确保开机自启路径不会因版本号变化而失效；数据目录保持不变。

当前版本可在托盘菜单顶部、托盘悬停文字、「关于」窗口或 EXE 文件属性中查看。

## 日报/周报工作流

1. 在提醒窗口或托盘中记录当天工作。
2. 选择「生成日报」或「生成周报」，复制提示词和相关数据。
3. 粘贴给你常用的 AI。
4. 将 AI 生成结果粘贴回 TickNote 的「导入报告」窗口保存。

提示词模板可以在设置中修改。生成后的报告与原始流水独立保存，修改流水不会覆盖已确认的报告。

## 数据与隐私

TickNote 的工作流水、提醒事件、报告和设置均保存在本地：

```text
数据目录\
├── data.db        SQLite 数据库
├── config.json    应用设置与提示词模板
├── exports\       导入报告的 Markdown 副本
└── backup\        自动备份，保留最近 7 份
```

应用本身不联网；只有当你主动把提示词和流水粘贴给 AI 服务时，相关内容才会离开本机。请在提交 Issue、PR 或截图前移除真实工作内容、个人路径和其他隐私信息，不要上传 `data.db`、`config.json` 或真实报告。

## 从源码构建

### 环境要求

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download)
- 不需要 Visual Studio

### 还原与编译

```bat
dotnet restore SedentaryReminder.sln
dotnet build SedentaryReminder.sln
```

### 回归检查

```bat
dotnet run --project tests\RegressionTests.csproj
```

回归范围包括流水日期、数据库导出与迁移、报告、DPI、关联编辑、事务回滚、旧库升级、历史日历、提醒收起和界面布局。

可额外检查 100% DPI 布局：

```bat
dotnet run --project tests\RegressionTests.csproj -- --dpi-unaware
```

### 打包单文件 EXE

直接双击根目录的 `build.bat`，或执行：

```bat
dotnet publish src\SedentaryReminder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist
```

产物为 `dist\TickNote.exe`，已内嵌 .NET 运行时。

## 自动构建

仓库提供 GitHub Actions 工作流：每次 `push`、每个 Pull Request 以及手动触发时，都会在 Windows 环境中依次完成还原、Release 编译、回归检查和单文件发布。

成功后会生成 `TickNote-win-x64` 构建产物，包含：

- `TickNote.exe`
- `TickNote.exe.sha256`

## 项目结构

```text
TickNote\
├── .github\                 Actions、Issue 和 PR 模板
├── src\                     应用源码与图标资源
│   ├── Data\                配置、SQLite、备份与迁移
│   ├── Reminder\            提醒状态机
│   ├── Overlay\             多屏提醒与临时收起
│   ├── Log\                 工作流水与历史查询
│   ├── Report\              报告生成、导入与导出
│   └── Settings\            托盘、设置、向导与关于
├── tests\                   可执行回归检查
├── 需求文档\                功能与模块设计
├── SedentaryReminder.sln    解决方案
└── build.bat                本地单文件打包脚本
```

UI 使用手写 WinForms 布局代码，不依赖设计器文件或 `.resx`。

## 文档

- [逐功能测试清单](测试清单.md)
- [功能需求文档](需求文档/功能需求文档.md)
- [历史流水与关联事项设计](需求文档/历史流水与关联事项设计.md)
- [贡献指南](CONTRIBUTING.md)

## 参与贡献

欢迎提交 Bug、功能建议和 Pull Request。提交前请先阅读[贡献指南](CONTRIBUTING.md)，运行回归检查，并确保日志、截图和示例数据已经脱敏。

## 许可证

TickNote 使用 [MIT License](LICENSE)。你可以自由使用、修改和分发，但需要保留原版权及许可声明；软件按“原样”提供，不附带担保。
