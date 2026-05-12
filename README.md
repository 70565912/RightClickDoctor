# RightClickDoctor

RightClickDoctor 是一个 Windows 资源管理器右键菜单诊断工具，用来找出让“右键”变慢的 Shell 扩展，并提供可恢复的禁用/恢复操作。

## 功能

- 扫描常见右键菜单注册位置：
  - `ContextMenuHandlers`
  - 静态 `shell` verbs
  - `ExplorerCommandHandler`
- 对 64 位 COM 右键扩展进行隔离测速：
  - `CreateInstance`
  - `IShellExtInit.Initialize`
  - `IContextMenu.QueryContextMenu`
- 慢扩展会按 `Watch`、`Slow`、`Severe`、`Timeout` 标记。
- 禁用 COM handler 时只写入当前用户的 `Shell Extensions\Blocked`，不删除第三方注册表项。
- 静态 verb 使用可恢复的 `LegacyDisable` 值。
- 支持导出 JSON/CSV 报告。
- 支持手动重启 Explorer 让变更立即生效。

## 构建

```powershell
dotnet build .\RightClickDoctor\RightClickDoctor.csproj -c Release
```

输出位置：

```text
RightClickDoctor\bin\Release\net8.0-windows\RightClickDoctor.exe
```

## 使用

1. 启动 `RightClickDoctor.exe`。
2. 点击 `Scan` 扫描右键菜单注册项。
3. 点击 `Probe timings` 测试 COM handler 耗时。
4. 优先处理第三方、非 Microsoft、`Slow`/`Severe`/`Timeout` 的项目。
5. 选中问题项，点击 `Disable selected`。
6. 点击 `Restart Explorer` 或重新登录，让 Explorer 重新加载扩展。

## 命令行

导出扫描结果：

```powershell
dotnet .\RightClickDoctor.dll --scan-json .\right-click-report.json
```

扫描并批量测速：

```powershell
dotnet .\RightClickDoctor.dll --probe-report .\right-click-probe-report.json 8
```

使用指定样本文件或文件夹测速：

```powershell
dotnet .\RightClickDoctor.dll --probe-report .\folder-probe-report.json 8 "C:\Users\YourName\Desktop"
```

`--probe` 是程序内部使用的隔离子进程模式，正常使用不需要手动调用。

## 安全策略

- 主 UI 进程不会直接加载第三方 Shell 扩展。
- 每个 COM handler 在子进程里测试，并带超时保护。
- 禁用 COM handler 使用 HKCU Blocked 列表，可恢复。
- 不会自动禁用 Microsoft/Windows 项目。
- 不删除注册表项。

## 参考

- Microsoft Learn: [Creating Shell Extension Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/handlers)
- Microsoft Learn: [Creating Shortcut Menu Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)
- GitHub 参考项目：
  - [moudey/Shell](https://github.com/moudey/Shell)
  - [oleg-shilo/shell-x](https://github.com/oleg-shilo/shell-x)
  - [yanxijian/ShellExtContextMenuHandler](https://github.com/yanxijian/ShellExtContextMenuHandler)

## License

MIT
