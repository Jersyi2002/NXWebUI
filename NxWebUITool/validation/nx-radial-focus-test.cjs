// Compiles NxUiFocus.cs (no NX refs) and checks Space-vs-typing / IME helpers.
const { spawnSync } = require('node:child_process')
const fs = require('node:fs')
const path = require('node:path')
const os = require('node:os')

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'nx-radial-focus-'))
const mainCs = path.join(tmp, 'Main.cs')
const exe = path.join(tmp, 'focus-test.exe')
const focusCs = path.resolve(__dirname, '../NxCommandSearch/NxUiFocus.cs')

fs.writeFileSync(mainCs, `
using System;
using NxWebUITool;
public static class Program {
  static int fail;
  static void Check(string name, bool cond) {
    Console.WriteLine((cond ? "PASS" : "FAIL") + "  " + name);
    if (!cond) fail++;
  }
  public static int Main() {
    Check("slots title", NxUiFocus.IsSlotsEditorTitle("NxWebUITool.Slots"));
    Check("ugraf title is not slots", !NxUiFocus.IsSlotsEditorTitle("ugraf"));
    Check("empty title is not slots", !NxUiFocus.IsSlotsEditorTitle(""));
    Check("Edit", NxUiFocus.IsEditClassName("Edit"));
    Check("WindowsForms10.EDIT", NxUiFocus.IsEditClassName("WindowsForms10.EDIT.app.0.141b42a_r11_ad1"));
    Check("RICHEDIT50W", NxUiFocus.IsEditClassName("RICHEDIT50W"));
    Check("QLineEdit", NxUiFocus.IsEditClassName("QLineEdit"));
    Check("QComboBox", NxUiFocus.IsEditClassName("QComboBox"));
    Check("ComboBox", NxUiFocus.IsEditClassName("ComboBox"));
    Check("ugraf is not an editor", !NxUiFocus.IsEditClassName("ugraf"));
    Check("Button is not an editor", !NxUiFocus.IsEditClassName("Button"));
    Check("Qt5QWindowIcon is not an editor", !NxUiFocus.IsEditClassName("Qt5QWindowIcon"));
    Check("empty is not an editor", !NxUiFocus.IsEditClassName(""));

    Check("IME class", NxUiFocus.IsImeUiClassName("IME"));
    Check("MSCTFIME UI", NxUiFocus.IsImeUiClassName("MSCTFIME UI"));
    Check("IME candidate class", NxUiFocus.IsImeUiClassName("CandidateWindow"));
    Check("Sogou class", NxUiFocus.IsImeUiClassName("Sogou_Pinyin_Candidate"));
    Check("QQPinyin class", NxUiFocus.IsImeUiClassName("QQPinyinCompWnd"));
    Check("ugraf is not IME UI", !NxUiFocus.IsImeUiClassName("ugraf"));
    Check("InputSite is not IME UI", !NxUiFocus.IsImeUiClassName("Windows.UI.Input.InputSite.WindowClass"));

    Check("TextInputHost process", NxUiFocus.IsImeProcessName("TextInputHost"));
    Check("Sogou process", NxUiFocus.IsImeProcessName("SogouCloud.exe"));
    Check("ugraf process is not IME", !NxUiFocus.IsImeProcessName("ugraf"));

    const int DLGC_WANTCHARS = 0x0080;
    const int DLGC_HASSETSEL = 0x0008;
    const int DLGC_BUTTON = 0x2000;
    Check("WANTCHARS is text", NxUiFocus.WantsTextInputDlgCode(DLGC_WANTCHARS));
    Check("HASSETSEL is text", NxUiFocus.WantsTextInputDlgCode(DLGC_HASSETSEL));
    Check("button dlgcode is not text", !NxUiFocus.WantsTextInputDlgCode(DLGC_BUTTON));
    Check("zero dlgcode is not text", !NxUiFocus.WantsTextInputDlgCode(0));
    Check("ROLE_SYSTEM_TEXT", NxUiFocus.IsTextAccRole(42));
    Check("ROLE_SYSTEM_COMBOBOX", NxUiFocus.IsTextAccRole(46));
    Check("ROLE_SYSTEM_CLIENT is not text", !NxUiFocus.IsTextAccRole(10));

    Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILURE(S)");
    return fail == 0 ? 0 : 1;
  }
}
`)

const hostCs = fs.readFileSync(path.resolve(__dirname, '../NxCommandSearch/SearchHost.cs'), 'utf8')
const radialCs = fs.readFileSync(path.resolve(__dirname, '../NxCommandSearch/RadialForm.cs'), 'utf8')
const hookCs = fs.readFileSync(path.resolve(__dirname, '../NxCommandSearch/RadialStartupPlugin.cs'), 'utf8')
const srcFail = []
function srcCheck(name, cond) {
  console.log((cond ? 'PASS' : 'FAIL') + '  ' + name)
  if (!cond) srcFail.push(name)
}
srcCheck('radial not ShowLocked', !/ShowLocked\(\s*_radial\s*\)/.test(hostCs))
srcCheck('radial uses modeless overlay', hostCs.includes('ShowRadialOverlay') && hostCs.includes('form.Show()') && !hostCs.includes('PumpUntilHidden') && !hostCs.includes('Application.DoEvents'))
srcCheck('DA2 probe before invoke', hostCs.includes('AskLockStatus') && hostCs.includes('IsNxCommandBusy'))
srcCheck('hold overlay ShowWithoutActivation', radialCs.includes('ShowWithoutActivation'))
srcCheck('hold overlay Dismiss', radialCs.includes('void Dismiss('))
srcCheck('HUD uses ShouldSuppressRadialHud', hookCs.includes('ShouldSuppressRadialHud') && hostCs.includes('ShouldSuppressRadialHud'))
srcCheck('hold uses Hud not full ShouldSuppressRadial()', hookCs.includes('ShouldSuppressRadialHud') && !/ShouldSuppressRadial\(\s*\)/.test(hookCs))
srcCheck('fast path still skips caret-only', fs.readFileSync(focusCs, 'utf8').includes('IsEditClassFocused') && fs.readFileSync(focusCs, 'utf8').includes('ShouldSuppressRadialHud'))
srcCheck('hook swallows space for input-box popup', hookCs.includes('_consumed') && hookCs.includes('ReinjectSpace'))
srcCheck('hold uses captured space not GetAsyncKeyState', hookCs.includes('IsCapturedDown()') && !/_holdTimer\.Tick[\s\S]*IsSpaceDown\(\)/.test(hookCs))
srcCheck('hook IME self-heal skips swallowed keys', hookCs.includes('_spaceDown && !_consumed && !NxUiFocus.IsSpaceDown()'))
srcCheck('hotkey show does not require IsSpaceDown', !/fromHold && !NxUiFocus\.IsSpaceDown\(\)/.test(hostCs))
srcCheck('hold release notified by hook', hostCs.includes('OnHoldSpaceUp') && radialCs.includes('NotifyHoldSpaceUp') && hookCs.includes('OnHoldSpaceUp'))
srcCheck('composing is GCS_COMPSTR only', fs.readFileSync(focusCs, 'utf8').includes('GcsCompStr') && !fs.readFileSync(focusCs, 'utf8').includes('ImmGetCandidateListCountW'))
srcCheck('restore focus before command', hostCs.includes('RestoreFocus(_radialPrevForeground'))
srcCheck('thread timer not NX hwnd', hostCs.includes('SetTimer(IntPtr.Zero'))
srcCheck('no Escape cancel storm', !hostCs.includes('RequestCancelActiveCommand') && !hostCs.includes('VkEscape'))
srcCheck('AskLockStatus probe', hostCs.includes('AskLockStatus'))
srcCheck('space-up posts spaceup', radialCs.includes('type = "spaceup"'))
srcCheck('busy command waits not drop', hostCs.includes('if (IsNxCommandBusy())') && hostCs.includes('return;') && !/IsNxCommandBusy\(\)\s*\{\s*StopCommandTimer/.test(hostCs))
srcCheck('interrupt replace without Escape', hostCs.includes('TryInterruptNxModal') && hostCs.includes('WmClose'))
srcCheck('usage recorded on invoke', hostCs.includes('RadialUsage.Record'))
srcCheck('radial page v=22', radialCs.includes('?v=22'))
srcCheck('radial host 640 DIP', radialCs.includes('FormDip = 640'))
srcCheck('slots editor suppresses space radial', fs.readFileSync(focusCs, 'utf8').includes('IsSlotsEditorForeground') && hookCs.includes('IsSlotsEditorForeground') && fs.readFileSync(path.resolve(__dirname, '../NxCommandSearch/SlotsForm.cs'), 'utf8').includes('SlotsWindowTitle'))
srcCheck('slots editor can save radial style', fs.readFileSync(path.resolve(__dirname, '../NxCommandSearch/SlotsForm.cs'), 'utf8').includes('"saveUi"') && fs.readFileSync(path.resolve(__dirname, '../NxCommandSearch/RadialUi.cs'), 'utf8').includes('WriteFromJson'))
srcCheck('loadSlots slim payload', radialCs.includes('LoadForRadial'))
srcCheck('prewarm after idle', hookCs.includes('PrewarmRadial') && hostCs.includes('EnsureWebViewAsync'))
srcCheck('prewarm does not deadlock Load', radialCs.includes('_webInitStarted') && /if \(_webInitStarted\) return/.test(radialCs))
srcCheck('stale showing recovered', hostCs.includes('_radialShowing = false'))
srcCheck('post shown on visible', radialCs.includes('if (_webInited) PostShown()'))
srcCheck('no live deploy sync target', !fs.readFileSync(path.resolve(__dirname, '../NxCommandSearch/NxCommandSearch.UI.csproj'), 'utf8').includes('SyncLiveDeploy'))
if (srcFail.length) {
  console.error(srcFail.length + ' SOURCE CHECK FAILURE(S)')
  process.exit(1)
}

const csc = path.join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319', 'csc.exe')
const compiled = spawnSync(csc, ['/nologo', '/t:exe', '/out:' + exe, mainCs, focusCs], { encoding: 'utf8' })
if (compiled.status !== 0) {
  console.error(compiled.stdout || compiled.stderr)
  process.exit(1)
}
const ran = spawnSync(exe, { encoding: 'utf8' })
process.stdout.write(ran.stdout || '')
process.stderr.write(ran.stderr || '')
process.exit(ran.status === 0 ? 0 : 1)
