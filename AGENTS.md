# NX WebUI 部署器

独立 Windows 程序：用 WebUI（WinForms + WebView2 + HTML）把 NxWebUI 插件装进 Siemens NX 2506。

**与 Agent Manager 完全隔离**：不引用其程序集、IPC、Electron 或仓库路径。运行时只读写 `%LOCALAPPDATA%\NxWebUITool` 与用户环境变量 `UGII_CUSTOM_DIRECTORY_FILE`。

## 规则

正确性 > 验证。最小必要修改。不要把部署器编回 AgentManager。

载荷来源（按顺序）：`NXWEBUI_PAYLOAD` 环境变量 → 可执行文件旁 `payload\` → 构建时若存在则从本仓库 `NxWebUITool\deploy` 同步（先编插件再编部署器）。插件源码在 `NxWebUITool\`，不引用 Agent Manager。

## 命令

```
dotnet build NxWebUIDeployer.slnx -c Release
dotnet run --project tests/NxWebUIDeployer.Tests.csproj -c Release --no-build
dist\NxWebUIDeployer.exe
```
