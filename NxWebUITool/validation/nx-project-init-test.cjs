// Source-level contract for 初始化项目 (no NX runtime).
const fs = require('node:fs')
const path = require('node:path')

const src = fs.readFileSync(
  path.resolve(__dirname, '../NxCommandSearch/ProjectInit.cs'),
  'utf8')
const btn = fs.readFileSync(
  path.resolve(__dirname, '../plugin/startup/NxWebUI.btn'),
  'utf8')
const men = fs.readFileSync(
  path.resolve(__dirname, '../plugin/startup/NxWebUI.men'),
  'utf8')
const rtb = fs.readFileSync(
  path.resolve(__dirname, '../plugin/application/profiles/All/NxWebUI.rtb'),
  'utf8')
const csproj = fs.readFileSync(
  path.resolve(__dirname, '../NxCommandSearch/NxProjectInit.csproj'),
  'utf8')

let fail = 0
function check(name, cond) {
  console.log((cond ? 'PASS' : 'FAIL') + '  ' + name)
  if (!cond) fail++
}

check('thickness name', src.includes('ThicknessName = "厚度"'))
check('thickness value', src.includes('ThicknessValue = "5"'))
check('parent 前排', src.includes('"前排"'))
check('parent 后排', src.includes('"后排"'))
check('front children', src.includes('"主驾驶建模"') && src.includes('"副驾驶建模"'))
check('shared child names', src.includes('"花纹"') && src.includes('"画框"'))
check('rear 建模', src.includes('"建模"'))
check('do not reuse other parent child', src.includes('FindMember(parent') && src.includes('FindUnownedGroup'))
check('idempotent find member', src.includes('FindMember(parent'))
check('hide_state embed members', src.includes('HideMembers = 1') && src.includes('EditSetHideState'))
check('parent created with children hidden', src.includes('CreateGroup(uf, spec.Name, children, HideMembers)'))
check('delete empty stray parents', src.includes('AddObjectsToDeleteList'))
check('edit members', src.includes('EditSetMembers'))
check('adopt unowned children', src.includes('FindUnownedGroup'))
check('expression mm', src.includes('CreateWithUnits') && src.includes('MilliMeter'))
check('undo mark', src.includes('SetUndoMark') && src.includes('UndoToMark'))
check('UF lock', src.includes('LockUgAccess') && src.includes('UnlockUgAccess'))
check('btn action', btn.includes('ACTIONS NxProjectInit.dll'))
check('men button', men.includes('BUTTON NXWEBUI_PROJECT_INIT'))
check('men after feature group', men.includes('AFTER UG_MODELING_GROUP_FEATURE'))
check('rtb label 初始化项目', rtb.includes('初始化项目'))
check('rtb button', rtb.includes('BUTTON NXWEBUI_PROJECT_INIT'))
check('csproj assembly', csproj.includes('NxProjectInit'))

console.log(fail === 0 ? 'ALL PASS' : fail + ' FAILURE(S)')
process.exit(fail === 0 ? 0 : 1)
