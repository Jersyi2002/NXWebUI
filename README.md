<div align="center">

# NX WebUI

**One-click install for the Siemens NX 2506 WebUI plugin**

Quit NX, run the deployer, restart NX. Command search, the spacebar radial menu, and project init load without Agent Manager.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/header/gradient.svg?title=NX+WebUI&subtitle=Siemens+NX+2506+plugin+deployer&theme=orange&mode=dark&align=center&font=space-grotesk" />
    <img alt="NX WebUI" src="https://shieldcn.dev/header/gradient.svg?title=NX+WebUI&subtitle=Siemens+NX+2506+plugin+deployer&theme=orange&mode=light&align=center&font=space-grotesk" />
  </picture>
</p>

<p align="center">
  <a href="https://github.com/LiarCN001/NXWebUI/blob/main/LICENSE"><img src="https://shieldcn.dev/github/license/LiarCN001/NXWebUI.svg?variant=secondary&size=sm&theme=orange" alt="license" /></a>
  <a href="https://github.com/LiarCN001/NXWebUI/stargazers"><img src="https://shieldcn.dev/github/stars/LiarCN001/NXWebUI.svg?variant=secondary&size=sm&theme=orange" alt="GitHub stars" /></a>
  <a href="https://www.plm.automation.siemens.com/"><img src="https://shieldcn.dev/badge/NX-2506-D97757.svg?variant=secondary&size=sm&theme=orange" alt="NX 2506" /></a>
</p>

</div>

## Install

```powershell
git clone https://github.com/LiarCN001/NXWebUI.git
cd NXWebUI
dotnet build NxWebUIDeployer.slnx -c Release
```

Windows x64, .NET SDK, and [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). Set `NXWEBUI_PAYLOAD` to an `NxWebUITool\deploy` folder when `dist\payload` is empty.

## Quickstart

Fully quit Siemens NX (`ugraf.exe`), then:

```powershell
.\dist\NxWebUIDeployer.exe
```

Click 安装. The deployer copies the plugin to `%LOCALAPPDATA%\NxWebUITool\deploy`, writes `custom_dirs.dat`, and sets user env `UGII_CUSTOM_DIRECTORY_FILE`. Restart NX 2506 (a full quit, not File then New). Alt+Q search, the spacebar radial menu, and project init are then available.

`启动部署器.bat` builds if needed, then starts the same exe.

## What you can do

- **Install or repair:** copies `startup\` and `application\` and registers them for desktop NX.
- **Keep other products:** merges custom directories and leaves QuickCAM and NXPL001 in place.
- **Refuse a live session:** blocks install and uninstall while `ugraf.exe` is running so DLLs are not overwritten.
- **Uninstall cleanly:** removes the LocalAppData copy and restores the previous environment variable.

## Requirements

- **Windows x64:** the UI is WinForms plus WebView2, not a web server.
- **Siemens NX 2506:** plugin binaries and menus target that release.
- **Plugin payload:** `dist\payload` beside the exe, or `NXWEBUI_PAYLOAD` pointing at a folder that already contains `startup\` and `application\`.

## Notes

- After any plugin DLL or webui change, fully quit NX, then click 修复 / 更新.
- Official `UGII\menus\custom_dirs.dat` is patched when writable. User-env registration still works if that file is locked.
- Discovery matches Agent Manager: `UGII_BASE_DIR`, `reg.exe` `UGII_BASE_DIR` values, then `NXBIN\ugraf.exe`.

## License

MIT
