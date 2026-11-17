/**
 * QA 补充核查：工程师「文案微调」后的双端同期复验
 * ---------------------------------------------------------------
 * 背景：工程师在 QA 报告出具后，对 prototype/index.html 的
 *       设置 →「快捷键」Tab →「被系统或其他应用占用」提示区做过一次
 *       平台无关文案统一。本脚本复验当前文件是否仍与最终验证基线一致，
 *       并对 19 个界面状态逐状态做 Win/mac 的「结构 + 归一化文案」比对。
 *
 * 运行：node deskkit/tests/parity-supplement.js
 * 依赖：零第三方依赖，仅需 Node >= 22（内置 fetch / WebSocket）与系统 Chrome headless。
 * 说明：与 prototype-smoke.js 一样，用 CDP Fetch 注入解码后的 HTML，
 *       保持 location.href 为 file:// 原始路径。
 */
const fs = require('fs'), path = require('path'), os = require('os'), cp = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const SRC = path.join(ROOT, 'prototype', 'index.html');
const HTML = fs.readFileSync(SRC, 'utf8');
const FILE_URL = 'file://' + SRC;
const CHROME = '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const PORT = 9448;

(async () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'dktabs-'));
  const chrome = cp.spawn(CHROME, [
    '--headless=new', '--no-sandbox', '--disable-gpu',
    '--remote-debugging-port=' + PORT, '--user-data-dir=' + tmp, 'about:blank'
  ], { stdio: 'ignore' });

  for (let i = 0; i < 50; i++) {
    try { const r = await fetch('http://127.0.0.1:' + PORT + '/json/version'); if (r.ok) break; } catch (e) {}
    await new Promise(r => setTimeout(r, 200));
  }
  const list = await (await fetch('http://127.0.0.1:' + PORT + '/json/list')).json();
  const ws = new WebSocket(list.find(t => t.type === 'page').webSocketDebuggerUrl);
  let id = 0; const pend = new Map();
  ws.onmessage = m => {
    const d = JSON.parse(m.data);
    if (d.id && pend.has(d.id)) { pend.get(d.id)(d); pend.delete(d.id); return; }
    if (d.method === 'Fetch.requestPaused') {
      if (d.params.request.url === FILE_URL) {
        send('Fetch.fulfillRequest', {
          requestId: d.params.requestId, responseCode: 200,
          responseHeaders: [{ name: 'Content-Type', value: 'text/html; charset=utf-8' }],
          body: Buffer.from(HTML, 'utf8').toString('base64')
        });
      } else {
        send('Fetch.failRequest', { requestId: d.params.requestId, errorReason: 'Failed' });
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
  await new Promise(r => setTimeout(r, 1000));
  async function ex(code) {
    const r = await send('Runtime.evaluate', { expression: code, returnByValue: true });
    if (r.result.exceptionDetails) throw new Error(r.result.exceptionDetails.text);
    return r.result.result.value;
  }
  async function J(e) { return JSON.parse(await ex('JSON.stringify(' + e + ')')); }

  await ex(String.raw`(function(){
    window.norm=function(s){var t=String(s||'');
      t=t.replace(/Alt \+ Space/g,'KD').replace(/⌥ \+ Space/g,'KD')
         .replace(/Ctrl \+ Shift \+ Space/g,'KB').replace(/⌘ \+ ⇧ \+ Space/g,'KB');
      return t.replace(/⌥/g,'ALT').replace(/⌘/g,'CMD').replace(/⇧/g,'SHIFT').replace(/⏎/g,'ENTER')
        .replace(/\bCtrl\b/g,'CMD').replace(/\bAlt\b/g,'ALT').replace(/\bShift\b/g,'SHIFT')
        .replace(/\bEnter\b/g,'ENTER').replace(/\bSpace\b/g,'SPACE')
        .replace(/Windows|macOS/g,'PLATFORM')
        .replace(/Ctrl \+ [A-Z,]/g,'KK').replace(/⌘[A-Z,]/g,'KK')
        .replace(/[\s+\/]/g,'').trim();};
    window.qaReset=function(){
      store.demo.timeout=false;store.demo.offline=false;store.market.flow=null;
      store.modalOpen=false;store.menu.open=false;store.recording=null;
      store.panel.edit=false;store.guide.gallery=false;store.guide.step=1;
      store.tools.activeTool=null;store.tools.mode='grid';
      store.settings.tab='general';store.market.tab='discover';store.market.search='';store.market.cat='全部';
      store.launcher.raw='';store.launcher.trigger='';store.launcher.payload='';
      try{closeModal();}catch(e){} try{closePopup();}catch(e){}
      renderAll();};
    window.PLABELS=['辅助功能','输入监控','屏幕录制','完全磁盘访问','开机自启','文件索引'];
  })();`);

  // 19 个界面状态：E 四个 Tab / F 三步+画廊 / B 两态 / C 两态 / D 三个 Tab / A 四态+触发词态
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

  let total = 0, fail = 0;
  for (const [name, setup] of Object.entries(setups)) {
    const view = 'view-' + name[0].toLowerCase();
    const snap = {};
    for (const plat of ['win', 'mac']) {
      await ex(`(function(){qaReset();switchPlatform('${plat}');${setup}return 1;})()`);
      snap[plat] = await J(`(function(){
        var r=document.getElementById('${view}').cloneNode(true);
        // 白名单 #5：移除按平台数据渲染的系统权限卡片（关于页 / 引导 Step3）
        var cards=r.querySelectorAll('.card');
        for(var i=cards.length-1;i>=0;i--){
          var bs=cards[i].querySelectorAll('.r-label b'),hit=false;
          for(var k=0;k<bs.length;k++){ if(PLABELS.indexOf(bs[k].textContent.trim())>=0) hit=true; }
          if(hit&&cards[i].parentNode) cards[i].parentNode.removeChild(cards[i]);
        }
        var tr=r.querySelector('#trig-out'); if(tr&&tr.parentNode) tr.parentNode.removeChild(tr);
        var st=[];(function walk(x){for(var k=0;k<x.children.length;k++){var e=x.children[k];
          st.push(e.tagName+'.'+(e.className||'')+(e.hasAttribute('data-act')?'@'+e.getAttribute('data-act'):''));walk(e);}})(r);
        return {text:norm(r.textContent),struct:st.join('|')};})()`);
    }
    total++;
    const okText = snap.win.text === snap.mac.text, okStruct = snap.win.struct === snap.mac.struct;
    if (!okText || !okStruct) {
      fail++;
      console.log('DIFF', name, 'text', okText, 'struct', okStruct);
      let i = 0; const a = snap.win.text, b = snap.mac.text;
      while (i < Math.min(a.length, b.length) && a[i] === b[i]) i++;
      console.log('  win@' + i + ' …' + a.slice(Math.max(0, i - 40), i + 90) + '…');
      console.log('  mac@' + i + ' …' + b.slice(Math.max(0, i - 40), i + 90) + '…');
    } else console.log('OK  ', name);
  }

  console.log('\n=== E 快捷键 Tab「被系统或其他应用占用」原文（两端）===');
  for (const plat of ['win', 'mac']) {
    await ex(`(function(){qaReset();switchPlatform('${plat}');store.settings.tab='hotkeys';openView('E');renderAll();return 1;})()`);
    const t = await J(`(function(){var cards=document.querySelectorAll('#e-content .card');var c=cards[cards.length-1];return c?c.textContent.trim():'NOT FOUND';})()`);
    console.log(plat + ': ' + t);
  }
  console.log('\n=== 汇总 === ' + total + ' 个界面状态，白名单外差异 ' + fail);
  ws.close(); chrome.kill('SIGKILL');
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch (e) {}
  process.exit(fail === 0 ? 0 : 1);
})().catch(e => { console.error('ERR', e); process.exit(1); });
