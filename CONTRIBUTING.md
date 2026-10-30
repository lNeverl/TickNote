# 贡献指南

感谢你愿意改进 TickNote。Bug 反馈、功能建议、文档修正和代码贡献都欢迎。

## 反馈问题或提出建议

- 提交前先搜索已有 Issue，避免重复。
- Bug 请使用「Bug 反馈」模板，提供版本、Windows 环境、复现步骤、预期结果和实际结果。
- 功能建议请使用「功能建议」模板，先说明要解决的问题，再描述建议方案。
- 不要上传真实工作流水、数据库、配置、私人路径或未脱敏截图。

## 开发环境

- Windows 10/11 x64
- .NET 8 SDK
- 不需要 Visual Studio

还原、编译和运行回归检查：

```bat
dotnet restore SedentaryReminder.sln
dotnet build SedentaryReminder.sln
dotnet run --project tests\RegressionTests.csproj
```

涉及界面布局时，请同时检查不同 DPI、多显示器和窄窗口；可额外运行：

```bat
dotnet run --project tests\RegressionTests.csproj -- --dpi-unaware
```

## 提交代码前

1. 从最新默认分支创建主题分支，每个 Pull Request 只处理一个主要问题。
2. 保持现有 C# 命名、文件组织和手写 WinForms 布局风格。
3. 修改数据格式时保留旧库升级路径，并补充迁移与回滚验证。
4. 修改提醒、流水、报告或布局行为时，同步更新回归检查和受影响文档。
5. 不提交 `dist/`、`bin/`、`obj/`、运行数据库、个人配置或临时文件。
6. 确认 `git diff --check` 无问题，并运行上述回归检查。

## Pull Request

请说明：

- 变更解决了什么问题；
- 主要实现方式；
- 执行过哪些验证；
- 是否涉及数据迁移、界面布局或使用方式变化。

界面改动可以附截图，但截图必须使用虚构、脱敏的数据。CI 通过并不代表所有多屏和真实 DPI 场景都已覆盖，需要在 PR 中如实说明手工验证范围。

## 许可证

提交贡献即表示你同意相应内容按照本仓库的 [MIT License](LICENSE) 发布。
