# PROJECT_STATE

独立产品：NX WebUI 部署器。仓库根 `F:\NxWebUIDeployer`。不依赖 Agent Manager。

## 目标

用 WebUI 一键部署 / 修复 / 卸载 NX 2506 WebUI 插件。

## 已完成

- 部署逻辑在 `src/NxWebUIDeployer.Core`（custom_dirs 合并、官方文件修补、HKCU 环境变量、ugraf 检测）。
- 宿主 `src/NxWebUIDeployer`：WebView2 加载 `webui/`（NX WebUI 同系暖米/珊瑚橙主题）。
- 安装目标：`%LOCALAPPDATA%\NxWebUITool\deploy` + `custom_dirs.dat`。
- 合并时保留 QuickCAM / NXPL001，去掉仓库与 `F:\NxWebUITool\deploy` 旧路径。
- NX 运行中拒绝覆盖 DLL。
- 标记文件 `application/nxwebui-plugin.json`（installedBy = NX WebUI Deployer）。

## 注意

- 改完插件 DLL/webui 后须完全退出 NX，再在本部署器点「修复 / 更新」。
- 不要同步到 `F:\NxWebUITool`。
- Agent Manager 的 `/workbench/plugins` 仍可部署同一目录；两套工具互不引用代码。
