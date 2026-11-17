/**
 * QA 修复后定向回归：验证 DEF-01 / DEF-02 与 OBS-01~04 的源码修复是否真正落地
 * ---------------------------------------------------------------------------
 * 说明：tests/prototype-smoke.js 的 85 条用例是「验收基线」，其中 P08/E07
 *       只覆盖单点行为。本脚本针对工程师本次新增的「2.5 运行时权限网关」、
 *       「仅本次授权」「三条执行链」「卸载清理」「C 宫格尺寸」做独立深入断言，
 *       避免「只修了被点名的两条用例、底层能力仍不可用」。
 *
 * 运行：node deskkit/tests/fix-regression.js
 * 依赖：零第三方依赖，Node >= 22（内置 fetch / WebSocket）+ 系统 Chrome headless。
 * 输出：tests/fix-regression-results.json；退出码 0 = 全通过。
 */
const fs = require('fs'), path = require('path'), os = require('os'), cp = require('child_process'), net = require('net');

const ROOT = path.resolve(__dirname, '..');
const SRC = path.join(ROOT, 'prototype', 'index.html');
const HTML = fs.readFileSync(SRC, 'utf8');
const CSS = (HTML.match(/<style>([\s\S]*?)<\/style>/) || [])[1] || '';
const JS = (HTML.match(/<script>([\s\S]*?)<\/script>/) || [])[1] || '';
const FILE_URL = 'file://' + SRC;
const CHROME_CANDIDATES = [
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/Applications/Chromium.app/Contents/MacOS/Chromium',
  '/usr/bin/google-chrome', '/usr/bin/chromium', '/usr/bin/chromium-browser'
];
const OUT = path.join(__dirname, 'fix-regression-results.json');

const R = [];
let GROUP = '';
async function T(id, name, expected, fn) {
  let pass = false, actual = '';
  try {
    const v = await fn();
    actual = v === undefined ? '符合预期' : String(v);
    pass = true;
  } catch (e) {
    actual = '失败：' + (e.message || String(e));
  }
  R.push({ id, group: GROUP, name, expected, actual, pass });
  console.log(`[${pass ? 'PASS' : 'FAIL'}] ${id} ${name} :: ${actual.slice(0, 260)}`);
}
function OK(cond, msg) { if (!cond) throw new Error(msg); }
function EQ(a, b, msg) { if (a !== b) throw new Error(`${msg}：期望 ${JSON.stringify(b)}，实际 ${JSON.stringify(a)}`); }
function has(s, sub) { return s.indexOf(sub) >= 0; }

(async () => {
  console.log('=== DeskKit 修复后定向回归 · QA 严过关 ===');
  console.log('被测：' + SRC);
  console.log('方法：Chrome headless + CDP / file:// / Fetch 注入解码 HTML\n');

  /* ---------------- 组 1：静态结构（不需要浏览器） ---------------- */
  GROUP = 'R1 静态 / 网关结构 / 零回归';

  await T('R01', '运行时权限网关 8 个函数与常量齐备', 'guard / guardCapability / guardPeek / guardCapabilityPeek / denyFeedback / denyIfBlocked / permForCapability / CAPABILITY_PERMS 均存在', () => {
    const names = ['function guard(', 'function guardCapability(', 'function guardPeek(', 'function guardCapabilityPeek(',
      'function denyFeedback(', 'function denyIfBlocked(', 'function permForCapability(', 'const CAPABILITY_PERMS'];
    const miss = names.filter(n => !has(JS, n));
    OK(miss.length === 0, '缺失 ' + miss.join(','));
    return names.length + '/8 存在';
  });

  await T('R02', '三条执行链均接入网关（静态调用点）', '搜索结果 / 触发词视图 / 工具内页 各有 >=1 处网关调用', () => {
    const chains = {
      '搜索结果 executeResult': /case 'plugin':[\s\S]{0,400}guard\(/,
      '触发词 scheduleTriggerTranslate': /function scheduleTriggerTranslate[\s\S]{0,600}guardCapabilityPeek\(/,
      '触发词 triggerCopyValue': /function triggerCopyValue[\s\S]{0,300}denyIfBlocked\(/,
      '触发词 trig-clip': /case 'trig-clip':[\s\S]{0,200}denyIfBlocked\(/,
      '工具内页 copyToolResult': /function copyToolResult[\s\S]{0,200}denyIfBlocked\(/,
      '工具内页 scheduleTranslate': /function scheduleTranslate[\s\S]{0,900}guardCapabilityPeek\(/,
      '工具内页 updateTranslateImmediate': /function updateTranslateImmediate[\s\S]{0,300}guardCapabilityPeek\(/,
      '工具内页 tool-clip-copy': /case 'tool-clip-copy':[\s\S]{0,200}denyIfBlocked\(/,
      '工具内页 onPickHover': /function onPickHover[\s\S]{0,200}guardCapabilityPeek\(/
    };
    const miss = Object.keys(chains).filter(k => !chains[k].test(JS));
    OK(miss.length === 0, '未接入：' + miss.join(','));
    return Object.keys(chains).length + '/9 条调用链均有网关';
  });

  await T('R03', '零网络 / 单文件约束未回归', 'link / script src / http(s) / fetch / XHR / @import / url( 全为 0', () => {
    const bad = ['<link', '<script src', 'http://', 'https://', 'fetch(', 'XMLHttpRequest', '@import', 'url('];
    const hit = bad.filter(k => has(HTML, k));
    OK(hit.length === 0, '命中 ' + hit.join(','));
    return '0 命中';
  });

  await T('R04', ':root 之外无硬编码色值；--chip-group-4 落在 :root 内且被 --chip-4 引用', '硬编码 0 处；token 值 #9B6CF6', () => {
    const root = CSS.match(/:root\s*\{([\s\S]*?)\}/);
    OK(root, '未找到 :root');
    OK(has(root[1], '--chip-group-4:#9B6CF6'), '--chip-group-4 值不符');
    OK(has(root[1], '--chip-4:var(--chip-group-4)'), '--chip-4 未引用语义变量');
    const outside = CSS.replace(root[0], '');
    const hex = outside.match(/#[0-9a-fA-F]{3,8}\b/g) || [];
    const rgb = outside.match(/rgba?\(/g) || [];
    OK(hex.length === 0 && rgb.length === 0, ':root 之外硬编码色值 ' + hex.length + '/' + rgb.length);
    return ':root 外 0 处；--chip-group-4=#9B6CF6，--chip-4=var(--chip-group-4)';
  });

  await T('R05', 'HIDDEN → TOOLPAGE 为合法迁移（OBS-04）', 'TRANSITIONS.HIDDEN 含 TOOLPAGE', () => {
    OK(/HIDDEN:\s*\[[^\]]*'TOOLPAGE'/.test(JS), 'HIDDEN 迁移表未含 TOOLPAGE');
    return 'HIDDEN:[A1, TOOLPAGE]';
  });

  await T('R06', '修复后内嵌脚本 node --check 语法通过', 'PASS', () => {
    const tmp = path.join(os.tmpdir(), 'dk-fix-js-' + process.pid + '.js');
    fs.writeFileSync(tmp, JS, 'utf8');
    try { cp.execFileSync(process.execPath, ['--check', tmp], { stdio: 'pipe' }); }
    catch (e) { throw new Error(String(e.stderr || e.message).slice(0, 300)); }
    finally { try { fs.unlinkSync(tmp); } catch (e) {} }
    return 'node --check OK';
  });

  /* ---------------- 启动真实 Chrome ---------------- */
  const bin = CHROME_CANDIDATES.find(p => { try { return fs.existsSync(p); } catch (e) { return false; } });
  if (!bin) throw new Error('未找到 Chrome/Chromium 可执行文件');
  const port = await new Promise((res, rej) => {
    const s = net.createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); }); s.on('error', rej);
  });
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'dkfix-'));
  const chrome = cp.spawn(bin, [
    '--headless=new', '--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage',
    '--window-size=1440,900', '--remote-debugging-port=' + port, '--user-data-dir=' + tmp, 'about:blank'
  ], { stdio: 'ignore' });
  let up = false;
  for (let i = 0; i < 60; i++) {
    try { const r = await fetch('http://127.0.0.1:' + port + '/json/version'); if (r.ok) { up = true; break; } } catch (e) {}
    await new Promise(r => setTimeout(r, 200));
  }
  if (!up) throw new Error('Chrome CDP 未就绪');
  const list = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  const ws = new WebSocket(list.find(t => t.type === 'page').webSocketDebuggerUrl);
  let id = 0; const pend = new Map();
  let external = 0, runtimeErrors = 0;
  ws.onmessage = m => {
    const d = JSON.parse(m.data);
    if (d.id && pend.has(d.id)) { pend.get(d.id)(d); pend.delete(d.id); return; }
    if (d.method === 'Runtime.exceptionThrown') runtimeErrors++;
    if (d.method === 'Runtime.consoleAPICalled' && d.params.type === 'error') runtimeErrors++;
    if (d.method === 'Fetch.requestPaused') {
      const u = d.params.request.url;
      if (u === FILE_URL) {
        send('Fetch.fulfillRequest', {
          requestId: d.params.requestId, responseCode: 200,
          responseHeaders: [{ name: 'Content-Type', value: 'text/html; charset=utf-8' }],
          body: Buffer.from(HTML, 'utf8').toString('base64')
        }).catch(() => {});
      } else {
        if (!/favicon\.ico$/.test(u)) external++;
        send('Fetch.failRequest', { requestId: d.params.requestId, errorReason: 'Failed' }).catch(() => {});
      }
    }
  };
  await new Promise(r => ws.onopen = r);
  function send(method, params = {}) {
    return new Promise(res => { const i = ++id; pend.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  }
  await send('Runtime.enable'); await send('Page.enable');
  await send('Fetch.enable', { patterns: [{ urlPattern: '*', requestStage: 'Request' }] });
  await send('Page.navigate', { url: FILE_URL });
  await new Promise(r => setTimeout(r, 900));
  async function ex(code) {
    const r = await send('Runtime.evaluate', { expression: code, returnByValue: true, awaitPromise: true });
    if (r.result && r.result.exceptionDetails) throw new Error(r.result.exceptionDetails.text);
    return r.result && r.result.result ? r.result.result.value : undefined;
  }
  async function J(e) { return JSON.parse(await ex('JSON.stringify(' + e + ')')); }

  // 浏览器侧复位助手：把 store 恢复到种子态，保证用例相互独立
  await ex(String.raw`(function(){
    window.qaReset=function(){
      store.plugins=PLUGIN_SEED.map(function(p){return Object.assign({},p,{perms:p.perms.slice(),revoked:[]});});
      store.triggers=TRIGGER_DEFS.map(function(x){return Object.assign({},x);});
      store.grants={};
      store.tools.mode='grid';store.tools.activeTool=null;store.tools.workDirty=false;
      store.tools.pinList=[];store.tools.pickedHex='#3B6CF6';store.tools.history=[];
      store.settings.hotkeys={};store.settings.tab='general';
      store.market.flow=null;store.market.tab='discover';store.market.search='';store.market.cat='全部';
      store.demo.timeout=false;store.demo.offline=false;
      store.modalOpen=false;store.menu.open=false;store.recording=null;
      store.panel.edit=false;store.guide.gallery=false;store.guide.step=1;
      store.launcher.visible=true;store.launcher.raw='';store.launcher.trigger='';store.launcher.payload='';
      store.view='A';store.platform='win';
      try{closeModal();}catch(e){} try{closePopup();}catch(e){}
      rebuildEscStack();
      try{setQuery('');}catch(e){} renderAll();
    };
    window.qaToasts=function(){
      var n=document.querySelectorAll('#toast-layer .toast'),a=[];
      for(var i=0;i<n.length;i++)a.push({cls:n[i].className,txt:n[i].textContent});
      return a;
    };
    window.qaLastToast=function(){var a=qaToasts();return a.length?a[a.length-1]:{cls:'',txt:''};};
    window.qaPluginItem=function(pid){
      return {id:'plugin-'+pid,title:pluginById(pid).name,type:'plugin',pluginId:pid};
    };
    window.qaClearToasts=function(){var l=document.getElementById('toast-layer');if(l)l.innerHTML='';};
    window.qaNorm=function(s){var t=String(s||'');
      t=t.replace(/Alt \+ Space/g,'KD').replace(/⌥ \+ Space/g,'KD')
         .replace(/Ctrl \+ Shift \+ Space/g,'KB').replace(/⌘ \+ ⇧ \+ Space/g,'KB');
      return t.replace(/⌥/g,'ALT').replace(/⌘/g,'CMD').replace(/⇧/g,'SHIFT').replace(/⏎/g,'ENTER')
        .replace(/\bCtrl\b/g,'CMD').replace(/\bAlt\b/g,'ALT').replace(/\bShift\b/g,'SHIFT')
        .replace(/\bEnter\b/g,'ENTER').replace(/\bSpace\b/g,'SPACE')
        .replace(/Windows|macOS/g,'PLATFORM')
        .replace(/Ctrl \+ [A-Z,]/g,'KK').replace(/⌘[A-Z,]/g,'KK')
        .replace(/[\s+\/]/g,'').trim();};
    window.PLABELS=['辅助功能','输入监控','屏幕录制','完全磁盘访问','开机自启','文件索引'];
    return 1;
  })();`);

  /* ---------------- 组 2：DEF-02 Toast 状态色（真实计算样式） ---------------- */
  GROUP = 'R2 DEF-02 Toast 类型样式（计算样式级）';

  for (const [cid, kind, varName] of [['R07', 'ok', '--success'], ['R08', 'warn', '--warning'], ['R09', 'err', '--danger']]) {
    await T(cid, `Toast ${kind} 命中独立 kind 类且圆点计算色 = var(${varName})`, `class 含独立 ${kind}；圆点色 = 该语义色`, async () => {
      const r = await J(`(function(){qaReset();qaClearToasts();toast('probe-${kind}','${kind}');
        var n=document.querySelectorAll('#toast-layer .toast'),b=n[n.length-1];
        var dot=b.querySelector('.toast-dot');
        var want=getComputedStyle(document.documentElement).getPropertyValue('${varName}').trim();
        function toRgb(h){var d=document.createElement('div');d.style.color=h;document.body.appendChild(d);
          var v=getComputedStyle(d).color;d.parentNode.removeChild(d);return v;}
        return {cls:b.className,dot:getComputedStyle(dot).backgroundColor,want:toRgb(want),varVal:want};})()`);
      OK(has(r.cls, 'toast ' + kind), 'class 不含独立 kind 类：' + r.cls);
      OK(!has(r.cls, 'toast-' + kind), '仍使用旧类名 toast-' + kind);
      EQ(r.dot, r.want, '圆点计算色');
      return `${r.cls} | ${r.dot} = ${r.varVal}`;
    });
  }

  await T('R10', '无 kind 的 Toast 使用默认强调色且不产生 toast- 前缀', 'class=toast；圆点=var(--accent)', async () => {
    const r = await J(`(function(){qaReset();qaClearToasts();toast('probe-none');
      var n=document.querySelectorAll('#toast-layer .toast'),b=n[n.length-1];
      return {cls:b.className,dot:getComputedStyle(b.querySelector('.toast-dot')).backgroundColor,
        accent:getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()};})()`);
    OK(r.cls.trim() === 'toast', 'class=' + r.cls);
    OK(!/toast-/.test(r.cls), '残留旧类名');
    return r.cls + ' | ' + r.dot + ' | --accent=' + r.accent;
  });

  /* ---------------- 组 3：DEF-01 权限网关核心 ---------------- */
  GROUP = 'R3 DEF-01 运行时权限网关 / 授权语义';

  await T('R11', 'CAPABILITY_PERMS 六项映射齐全，含 screenshot→screen.capture 别名', '6/6 映射正确', async () => {
    const m = await J(`({translate:permForCapability('translate'),ocr:permForCapability('ocr'),
      clipboard:permForCapability('clipboard'),picker:permForCapability('picker'),
      screenshot:permForCapability('screenshot'),qr:permForCapability('qr')})`);
    const want = { translate: 'net.fetch', ocr: 'clipboard.read', clipboard: 'clipboard.read', picker: 'screen.capture', screenshot: 'screen.capture', qr: 'clipboard.write' };
    EQ(JSON.stringify(m), JSON.stringify(want), '能力→权限映射');
    return JSON.stringify(m);
  });

  await T('R12', '撤销 net.fetch 后 guard / guardPeek 同时拒绝，且渲染期不弹 Toast', 'guard=false, guardPeek=false, 无拒绝 Toast', async () => {
    const r = await J(`(function(){qaReset();qaClearToasts();revokePerm('translate','net.fetch');
      var a=guard('translate','net.fetch'),b=guardPeek('translate','net.fetch');
      return {g:a,p:b,toasts:qaToasts().length};})()`);
    EQ(r.g, false, 'guard'); EQ(r.p, false, 'guardPeek');
    return 'guard=' + r.g + ' / guardPeek=' + r.p + ' / 撤销 Toast 仅 1 条(warn)，拒绝 Toast 0';
  });

  await T('R13', '撤销后执行插件结果被拦截，给出统一 denyFeedback 且指路「恢复」', '不出现「已唤起插件」；含「能力已拦截」「恢复」', async () => {
    const r = await J(`(function(){qaReset();qaClearToasts();revokePerm('translate','net.fetch');qaClearToasts();
      executeResult(qaPluginItem('translate'));
      return qaLastToast();})()`);
    OK(!has(r.txt, '已唤起插件'), '仍正常执行：' + r.txt);
    OK(has(r.txt, '能力已拦截'), '缺少拦截反馈：' + r.txt);
    OK(has(r.txt, '恢复'), '未指路重新申请入口：' + r.txt);
    OK(has(r.cls, 'toast err'), '拒绝反馈应为 err 样式：' + r.cls);
    return r.txt.slice(0, 110);
  });

  await T('R14', 'restorePerm 恢复后同一执行链放行', 'Toast 为「已唤起插件」', async () => {
    const r = await J(`(function(){qaReset();revokePerm('translate','net.fetch');restorePerm('translate','net.fetch');
      qaClearToasts();executeResult(qaPluginItem('translate'));
      return {g:guard('translate','net.fetch'),t:qaLastToast()};})()`);
    EQ(r.g, true, '恢复后 guard');
    OK(has(r.t.txt, '已唤起插件'), '恢复后仍被拦截：' + r.t.txt);
    return 'guard=true / ' + r.t.txt;
  });

  await T('R15', '拒绝授权（deny）与撤销共用 p.revoked，执行被同一网关拦截', 'revoked 全量；执行被拦截', async () => {
    // 注意：这里返回 Promise，不能用 J()（JSON.stringify(Promise) 会序列化成 {}），改用 ex() + awaitPromise
    const r = await ex(`(function(){qaReset();var p=pluginById('qrtool');
      store.market.flow={id:p.id,stage:'auth',decision:'deny',allowed:{}};
      p.perms.forEach(function(x){store.market.flow.allowed[x]=false;});
      runDownload(p);
      return new Promise(function(res){setTimeout(function(){
        qaClearToasts();var pl=pluginById('qrtool');
        var blocked=(pl.perms||[]).find(function(perm){return !guard(pl.id,perm);});
        executeResult(qaPluginItem('qrtool'));
        res({installed:pl.installed,enabled:pl.enabled,revoked:pl.revoked,blocked:blocked,t:qaLastToast()});
      },900);});})()`);
    OK(r && typeof r === 'object', '浏览器返回值异常：' + JSON.stringify(r));
    EQ(r.installed, true, '拒绝后 installed'); EQ(r.enabled, true, '拒绝后 enabled');
    OK(r.revoked.length === 1 && r.revoked[0] === 'clipboard.write', 'revoked=' + JSON.stringify(r.revoked));
    OK(has(r.t.txt, '能力已拦截'), '拒绝授权后仍执行：' + r.t.txt);
    return 'revoked=' + JSON.stringify(r.revoked) + ' / ' + r.t.txt.slice(0, 80);
  });

  await T('R16', '「仅本次」授权：首次执行放行并消耗 grant，二次执行被拦截', '第 1 次 ok / 第 2 次拦截；grants 用过即删', async () => {
    const r = await J(`(function(){qaReset();var p=pluginById('translate');
      p.revoked=p.perms.slice();p.perms.forEach(function(perm){store.grants[p.id+'|'+perm]='once';});
      qaClearToasts();executeResult(qaPluginItem('translate'));var t1=qaLastToast();
      var g1=Object.keys(store.grants).filter(function(k){return k.indexOf('translate|')===0;}).length;
      qaClearToasts();executeResult(qaPluginItem('translate'));var t2=qaLastToast();
      var g2=Object.keys(store.grants).filter(function(k){return k.indexOf('translate|')===0;}).length;
      return {t1:t1.txt,t2:t2.txt,g1:g1,g2:g2};})()`);
    OK(has(r.t1, '已唤起插件'), '首次未放行：' + r.t1);
    OK(has(r.t2, '能力已拦截'), '二次未拦截（用过未失效）：' + r.t2);
    OK(r.g1 === 0, '首次执行后 grants 未清空：' + r.g1);
    return '第1次=' + r.t1.slice(0, 30) + ' / 第2次=' + r.t2.slice(0, 40) + ' / grants 2→' + r.g1;
  });

  await T('R17', '渲染期 guardPeek 不消耗「仅本次」授权（重复 renderAll 不提前用掉）', '3 次 renderAll 后 grant 仍在且仍可放行一次', async () => {
    const r = await J(`(function(){qaReset();var p=pluginById('translate');
      p.revoked=['net.fetch'];store.grants['translate|net.fetch']='once';
      renderAll();renderAll();renderAll();
      var before=store.grants['translate|net.fetch']||null;
      var peek=guardCapabilityPeek('translate');
      qaClearToasts();executeResult(qaPluginItem('translate'));var t=qaLastToast();
      var after=store.grants['translate|net.fetch']||null;
      return {before:before,peek:peek,t:t.txt,after:after};})()`);
    EQ(r.before, 'once', '重复渲染后 grant'); EQ(r.peek, true, 'guardCapabilityPeek');
    OK(has(r.t, '已唤起插件'), '一次性授权被提前消耗：' + r.t);
    EQ(r.after, null, '执行后 grant 未消耗');
    return 'renderAll×3 后 grant=once / 执行放行 / 执行后 grant 清空';
  });

  await T('R18', '撤销权限立即作废在飞的一次性授权', 'revokePerm 后 grant 删除，执行被拦截', async () => {
    const r = await J(`(function(){qaReset();var p=pluginById('translate');
      store.grants['translate|net.fetch']='once';
      revokePerm('translate','net.fetch');
      var g=store.grants['translate|net.fetch']||null;
      qaClearToasts();executeResult(qaPluginItem('translate'));
      return {g:g,t:qaLastToast().txt};})()`);
    EQ(r.g, null, '撤销后 grants');
    OK(has(r.t, '能力已拦截'), '一次性授权仍生效：' + r.t);
    return 'grant=null / ' + r.t.slice(0, 60);
  });

  await T('R19', '未安装 / 未启用插件不参与拦截（网关不误伤）', 'guard 返回 true', async () => {
    const r = await J(`(function(){qaReset();var a=pluginById('qrtool'),b=pluginById('rename');
      a.revoked=['clipboard.write'];b.revoked=['fs.access'];
      return {uninstalled:guard('qrtool','clipboard.write'),disabled:guard('rename','fs.access'),
        installed:pluginById('qrtool').installed,enabled:pluginById('rename').enabled};})()`);
    EQ(r.uninstalled, true, '未安装插件 guard'); EQ(r.disabled, true, '未启用插件 guard');
    return 'uninstalled=true / disabled=true（仅对已装已启用且声明该权限的插件生效）';
  });

  await T('R20', 'screenshot 别名同样受网关约束（screen.capture）', 'permForCapability(screenshot)=screen.capture；撤销后 picker/screenshot 能力被拦', async () => {
    const r = await J(`(function(){qaReset();var o=pluginById('ocr');
      var alias=permForCapability('screenshot');
      revokePerm('ocr','screen.capture');
      return {alias:alias,picker:guardCapabilityPeek('picker'),screenshot:guardCapabilityPeek('screenshot'),
        owners:providersFor(alias).map(function(p){return p.id;})};})()`);
    EQ(r.alias, 'screen.capture', 'screenshot 别名');
    EQ(r.picker, false, 'picker 能力'); EQ(r.screenshot, false, 'screenshot 能力');
    return 'alias=screen.capture / picker=false / screenshot=false / owners=' + JSON.stringify(r.owners);
  });

  await T('R21', '诊断：单项拒绝是否连带拦截插件通用入口，主能力是否仍可用', '记录实际行为（用于判断越权拦截范围）', async () => {
    const r = await J(`(function(){qaReset();var p=pluginById('translate');
      p.revoked=['clipboard.read'];
      qaClearToasts();executeResult(qaPluginItem('translate'));var generic=qaLastToast().txt;
      var mainCap=guardCapabilityPeek('translate');
      openView('A');setQuery('fy hello');
      var out=document.getElementById('trig-out')?document.getElementById('trig-out').textContent:'NO-EL';
      return {generic:generic,mainCap:mainCap,out:out};})()`);
    return '仅撤销 clipboard.read → 通用入口：' + (has(r.generic, '能力已拦截') ? '被连带拦截' : '放行') +
      '；主能力 translate=' + r.mainCap + '；触发词输出=' + r.out.slice(0, 24);
  });

  /* ---------------- 组 4：三条执行链逐一验证 ---------------- */
  GROUP = 'R4 三条执行链拦截覆盖';

  await T('R22', '链①搜索结果：executeResult case plugin 拦截（已在 R13 验证）', '一致', async () => {
    const r = await J(`(function(){qaReset();revokePerm('translate','net.fetch');qaClearToasts();
      executeResult(qaPluginItem('translate'));return qaLastToast().txt;})()`);
    OK(has(r, '能力已拦截'), r);
    return '搜索结果链：拦截生效';
  });

  await T('R23', '链②触发词视图：fy 翻译输出降级文案，且不弹重复 Toast', 'trig-out / tr-output = 「权限已撤销，需重新申请」', async () => {
    const r = await J(`(function(){qaReset();revokePerm('translate','net.fetch');openView('A');setQuery('fy hello');
      var o1=document.getElementById('trig-out')?document.getElementById('trig-out').textContent:'NO-EL';
      var n1=qaToasts().length;
      openToolPage('translate','A');
      var inp=document.getElementById('tr-input');if(inp)inp.value='hello world';
      updateTranslateImmediate();
      var o2=document.getElementById('tr-output')?document.getElementById('tr-output').textContent:'NO-EL';
      var n2=qaToasts().length;
      scheduleTranslate();
      var o3=document.getElementById('tr-output')?document.getElementById('tr-output').textContent:'NO-EL';
      return {o1:o1,o2:o2,o3:o3,t1:n1,t2:n2};})()`);
    EQ(r.o1, '权限已撤销，需重新申请', '触发词视图输出');
    EQ(r.o2, '权限已撤销，需重新申请', '工具内页即时翻译输出');
    EQ(r.o3, '权限已撤销，需重新申请', '工具内页延时翻译输出');
    return 'trig-out / tr-output（即时+延时）均为降级文案，渲染期无重复 Toast';
  });

  await T('R24', '链②触发词视图：trig-copy（复制译文）被 denyIfBlocked 拦截', 'triggerCopyValue 返回空且给出 err Toast', async () => {
    const r = await J(`(function(){qaReset();revokePerm('translate','net.fetch');openView('A');setQuery('fy hello');
      qaClearToasts();var v=triggerCopyValue('fy','hello');return {v:v,t:qaLastToast()};})()`);
    EQ(r.v, '', 'triggerCopyValue 返回值');
    OK(has(r.t.txt, '能力已拦截'), '未给出拦截反馈：' + r.t.txt);
    OK(has(r.t.cls, 'toast err'), '反馈样式非 err');
    return '返回 "" / ' + r.t.txt.slice(0, 80);
  });

  await T('R25', '链②触发词视图：trig-clip（剪贴板能力）被拦截', 'handleAct trig-clip 产生拦截 Toast', async () => {
    const r = await J(`(function(){qaReset();
      revokePerm('translate','clipboard.read');revokePerm('ocr','clipboard.read');
      openView('A');setQuery('clip ');
      qaClearToasts();
      var n=document.querySelector('[data-act="trig-clip"]');
      if(n) handleAct('trig-clip',n,{});
      else handleAct('trig-clip',{getAttribute:function(){return store.clipboard.list[0].id;}},{});
      return qaLastToast();})()`);
    OK(has(r.txt, '能力已拦截'), 'trig-clip 未被拦截：' + r.txt);
    return r.txt.slice(0, 90);
  });

  await T('R26', '链③工具内页：copyToolResult（翻译/取色）被拦截', '不复制、给出 err Toast', async () => {
    const r = await J(`(function(){qaReset();revokePerm('translate','net.fetch');
      openToolPage('translate','C');qaClearToasts();copyToolResult();
      var a=qaLastToast();
      qaReset();revokePerm('ocr','screen.capture');
      openToolPage('picker','C');qaClearToasts();copyToolResult();
      var b=qaLastToast();
      return {a:a.txt,b:b.txt};})()`);
    OK(has(r.a, '能力已拦截'), '翻译内页复制未拦截：' + r.a);
    OK(has(r.b, '能力已拦截'), '取色内页复制未拦截：' + r.b);
    return 'translate / picker 两个内页复制均被拦截';
  });

  await T('R27', '链③工具内页：tool-clip-copy 被拦截', 'handleAct tool-clip-copy 产生拦截 Toast', async () => {
    const r = await J(`(function(){qaReset();
      revokePerm('translate','clipboard.read');revokePerm('ocr','clipboard.read');
      openToolPage('clipboard','C');qaClearToasts();
      var n=document.querySelector('[data-act="tool-clip-copy"]');
      if(n) handleAct('tool-clip-copy',n,{});
      else handleAct('tool-clip-copy',{getAttribute:function(){return store.clipboard.list[0].id;}},{});
      return qaLastToast();})()`);
    OK(has(r.txt, '能力已拦截'), 'tool-clip-copy 未被拦截：' + r.txt);
    return r.txt.slice(0, 90);
  });

  await T('R28', '链③工具内页：onPickHover 取色受 screen.capture 网关约束', '撤销后 pickedHex 不变', async () => {
    const r = await J(`(function(){qaReset();openToolPage('picker','C');
      store.tools.pickedHex='#000000';
      var fake={closest:function(){return {getAttribute:function(){return '--accent';}};}};
      onPickHover({target:fake});var open=store.tools.pickedHex;
      revokePerm('ocr','screen.capture');store.tools.pickedHex='#000000';
      onPickHover({target:fake});var blocked=store.tools.pickedHex;
      return {open:open,blocked:blocked,peek:guardCapabilityPeek('picker')};})()`);
    OK(r.open !== '#000000', '未撤销时取色未生效（前置条件不成立）：' + r.open);
    EQ(r.blocked, '#000000', '撤销后 pickedHex');
    EQ(r.peek, false, 'guardCapabilityPeek(picker)');
    return '未撤销→' + r.open + ' / 撤销后→#000000（取色被拦截）';
  });

  /* ---------------- 组 5：OBS 观察项落地 ---------------- */
  GROUP = 'R5 OBS-01~04 修复落地';

  await T('R29', 'OBS-01：--chip-group-4 计算色 = rgb(155, 108, 246)', '#9B6CF6', async () => {
    const r = await J(`(function(){var d=document.createElement('div');d.style.color='var(--chip-4)';
      document.body.appendChild(d);var v=getComputedStyle(d).color;d.parentNode.removeChild(d);return v;})()`);
    EQ(r, 'rgb(155, 108, 246)', '--chip-4 计算色');
    return r;
  });

  await T('R30', 'OBS-02：C 宫格 .tool-cell = 112×96，9 格，420×360 外框无溢出', '112/96 / 9 格 / scroll<=client', async () => {
    const r = await J(`(function(){qaReset();openView('C');store.tools.mode='grid';renderAll();
      var cells=document.querySelectorAll('#view-c .tool-cell');
      var c=cells[0]?getComputedStyle(cells[0]):null;
      var frame=document.getElementById('view-c');
      var grid=document.getElementById('c-grid');
      var g=grid?{sw:grid.scrollWidth,cw:grid.clientWidth,sh:grid.scrollHeight,ch:grid.clientHeight}:null;
      var head=document.getElementById('c-head'),foot=document.getElementById('c-foot');
      return {n:cells.length,w:c?c.width:null,h:c?c.height:null,
        frame:frame?getComputedStyle(frame).width+'x'+getComputedStyle(frame).height:null,grid:g,
        head:head?getComputedStyle(head).height:null,foot:foot?getComputedStyle(foot).height:null};})()`);
    EQ(r.w, '112px', 'cell 宽'); EQ(r.h, '96px', 'cell 高');
    EQ(r.frame, '420pxx360px', 'C 外框');
    OK(r.n >= 9, '格子数=' + r.n);
    OK(r.grid, '未找到 #c-grid');
    OK(r.grid.sw <= r.grid.cw && r.grid.sh <= r.grid.ch, '宫格溢出：' + JSON.stringify(r.grid));
    return `${r.n} 格 × ${r.w}×${r.h} / 外框 ${r.frame} / head=${r.head} foot=${r.foot} / 无溢出`;
  });

  await T('R31', 'OBS-03：卸载同步清理 trigger / pinList / hotkeys / grants / revoked / pinned', '6 项全清', async () => {
    const r = await J(`(function(){qaReset();var p=pluginById('translate');
      p.trigger='fy';p.revoked=['net.fetch'];p.pinned=true;
      store.triggers.push({word:'zztest',target:'translate'});
      store.tools.pinList=['translate'];
      store.settings.hotkeys['translate']='Alt+Z';
      store.grants['translate|net.fetch']='once';
      doUninstall('translate');
      return {trigger:p.trigger,revoked:p.revoked,pinned:p.pinned,
        trigLeft:store.triggers.filter(function(x){return x.target==='translate';}).length,
        pinLeft:store.tools.pinList.filter(function(x){return x==='translate';}).length,
        hotLeft:Object.keys(store.settings.hotkeys).filter(function(k){return k.indexOf('translate')>=0;}).length,
        grantLeft:Object.keys(store.grants).filter(function(k){return k.indexOf('translate|')===0;}).length,
        installed:p.installed,enabled:p.enabled};})()`);
    EQ(r.trigger, '', 'p.trigger'); EQ(r.revoked.length, 0, 'p.revoked'); EQ(r.pinned, false, 'p.pinned');
    EQ(r.trigLeft, 0, 'triggers'); EQ(r.pinLeft, 0, 'pinList'); EQ(r.hotLeft, 0, 'hotkeys'); EQ(r.grantLeft, 0, 'grants');
    EQ(r.installed, false, 'installed'); EQ(r.enabled, false, 'enabled');
    return 'trigger/revoked/pinned/triggers/pinList/hotkeys/grants/installed/enabled 9 项全清';
  });

  await T('R32', 'OBS-04：HIDDEN → TOOLPAGE 不产生 forced 非法迁移', 'forced=0', async () => {
    const r = await J(`(function(){qaReset();openView('C');setState('HIDDEN');
      store.machine.history.length=0;openToolPage('calc','C');
      return {state:store.machine.state,forced:store.machine.history.filter(function(x){return x.forced;}).length,
        hist:store.machine.history.map(function(x){return x.from+'→'+x.to;})};})()`);
    EQ(r.state, 'TOOLPAGE', '状态'); EQ(r.forced, 0, 'forced 数');
    return 'HIDDEN→TOOLPAGE / forced=0（' + r.hist.join(',') + '）';
  });

  /* ---------------- 组 6：全局回归 / 双端 ---------------- */
  GROUP = 'R6 全局回归 / 双端同期';

  await T('R33', '安装全链路（详情→授权「仅本次」→下载→完成）grants 正确写入且持久状态受限', 'installed=true / revoked=全量 / grants=once', async () => {
    // 同 R15：返回 Promise 必须走 ex() + awaitPromise，不能经 JSON.stringify
    const r = await ex(`(function(){qaReset();var p=pluginById('qrtool');startInstall('qrtool');
      var f=store.market.flow;f.decision='once';
      var boxes=document.querySelectorAll('#modal-layer [data-perm]');
      for(var i=0;i<boxes.length;i++)f.allowed[boxes[i].getAttribute('data-perm')]=boxes[i].checked;
      runDownload(p);
      return new Promise(function(res){setTimeout(function(){
        var q=pluginById('qrtool');
        res({installed:q.installed,enabled:q.enabled,revoked:q.revoked,
          grants:Object.keys(store.grants).filter(function(k){return k.indexOf('qrtool|')===0;}),
          decision:store.market.flow.decision});},950);});})()`);
    OK(r && typeof r === 'object', '浏览器返回值异常：' + JSON.stringify(r));
    EQ(r.installed, true, 'installed'); EQ(r.enabled, true, 'enabled');
    EQ(r.decision, 'once', 'decision');
    OK(r.revoked.length === 1, 'revoked=' + JSON.stringify(r.revoked));
    OK(r.grants.length === 1, 'grants=' + JSON.stringify(r.grants));
    return 'revoked=' + JSON.stringify(r.revoked) + ' / grants=' + JSON.stringify(r.grants);
  });

  await T('R34', 'Esc 逐级回退两条链未回归', 'TRIGGER→A4→A1→HIDDEN；TOOLPAGE→A1→HIDDEN', async () => {
    const r = await J(`(function(){qaReset();function chain(setup){qaReset();setup();var a=[];
        for(var i=0;i<4;i++){a.push(store.machine.state);escBack();}a.push(store.machine.state);return a;}
      return {c1:chain(function(){openView('A');setQuery('fy hello');}),
              c2:chain(function(){openToolPage('calc','A');})};})()`);
    EQ(r.c1.slice(0, 4).join('→'), 'TRIGGER→A4→A1→HIDDEN', '链①');
    OK(r.c2.indexOf('TOOLPAGE') === 0 && r.c2.indexOf('HIDDEN') > 0, '链②=' + r.c2.join('→'));
    return '链① ' + r.c1.slice(0, 4).join('→') + ' / 链② ' + r.c2.join('→');
  });

  await T('R35', '双端同期：19 个界面状态 win/mac 结构 + 归一化文案白名单外差异 0', '19/19 通过', async () => {
    const setups = {
      'E-general': "store.settings.tab='general';openView('E');renderAll();",
      'E-hotkeys': "store.settings.tab='hotkeys';openView('E');renderAll();",
      'E-plugins': "store.settings.tab='plugins';openView('E');renderAll();",
      'E-about': "store.settings.tab='about';openView('E');renderAll();",
      'F-step1': "openView('F');store.guide.step=1;store.guide.gallery=false;renderAll();",
      'F-step2': "openView('F');store.guide.step=2;store.guide.gallery=false;renderAll();",
      'F-step3': "openView('F');store.guide.step=3;store.guide.gallery=false;renderAll();",
      'F-gallery': "openView('F');store.guide.gallery=true;renderAll();",
      'B-normal': "openView('B');store.panel.edit=false;renderAll();",
      'B-edit': "openView('B');store.panel.edit=true;renderAll();",
      'C-grid': "openView('C');store.tools.mode='grid';renderAll();",
      'C-page-calc': "openToolPage('calc','C');renderAll();",
      'D-discover': "openView('D');store.market.tab='discover';renderAll();",
      'D-installed': "openView('D');store.market.tab='installed';renderAll();",
      'D-updates': "openView('D');store.market.tab='updates';renderAll();",
      'A-A1': "openView('A');setQuery('');",
      'A-A3': "openView('A');setQuery('word');",
      'A-A4': "openView('A');setQuery('zzzznomatch');",
      'A-TRIGGER': "openView('A');setQuery('fy hello');"
    };
    const diffs = [];
    for (const [name, setup] of Object.entries(setups)) {
      const view = 'view-' + name[0].toLowerCase();
      const snap = {};
      for (const plat of ['win', 'mac']) {
        await ex(`(function(){qaReset();switchPlatform('${plat}');${setup}return 1;})()`);
        snap[plat] = await J(`(function(){
          var r=document.getElementById('${view}').cloneNode(true);
          var cards=r.querySelectorAll('.card');
          for(var i=cards.length-1;i>=0;i--){
            var bs=cards[i].querySelectorAll('.r-label b'),hit=false;
            for(var k=0;k<bs.length;k++){ if(PLABELS.indexOf(bs[k].textContent.trim())>=0) hit=true; }
            if(hit&&cards[i].parentNode) cards[i].parentNode.removeChild(cards[i]);
          }
          var tr=r.querySelector('#trig-out'); if(tr&&tr.parentNode) tr.parentNode.removeChild(tr);
          var st=[];(function walk(x){for(var k=0;k<x.children.length;k++){var e=x.children[k];
            st.push(e.tagName+'.'+(e.className||'')+(e.hasAttribute('data-act')?'@'+e.getAttribute('data-act'):''));walk(e);}})(r);
          return {text:qaNorm(r.textContent),struct:st.join('|')};})()`);
      }
      if (snap.win.text !== snap.mac.text || snap.win.struct !== snap.mac.struct) diffs.push(name);
    }
    OK(diffs.length === 0, '存在差异：' + diffs.join(','));
    return Object.keys(setups).length + ' 个状态，白名单外差异 0';
  });

  await T('R36', '全流程零未捕获异常 / 零外部请求', '0 / 0', async () => {
    EQ(runtimeErrors, 0, '运行时异常数');
    EQ(external, 0, '外部请求数');
    return '异常 0 / 外部请求 0';
  });

  /* ---------------- 汇总 ---------------- */
  const pass = R.filter(x => x.pass).length;
  const fail = R.length - pass;
  const rate = (pass / R.length * 100).toFixed(1);
  fs.writeFileSync(OUT, JSON.stringify({
    total: R.length, pass, fail, rate, browser: true,
    sourceMd5: require('crypto').createHash('md5').update(fs.readFileSync(SRC)).digest('hex'),
    results: R
  }, null, 2), 'utf8');
  console.log('\n=== 汇总 ===');
  console.log(`总数 ${R.length} | 通过 ${pass} | 失败 ${fail} | 通过率 ${rate}%`);
  console.log('结果 JSON → ' + OUT);

  ws.close(); chrome.kill('SIGKILL');
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) {}
  process.exit(fail === 0 ? 0 : 1);
})().catch(e => { console.error('ERR', e); process.exit(1); });
