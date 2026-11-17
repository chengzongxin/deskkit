#!/bin/bash
# 用本机 Chrome 无界面模式，把原型每个界面状态截成参考图，供 macOS / Windows 界面对照。
# 工作区文件落盘后对 Chrome 不可读，所以由 Node 在 127.0.0.1 上临时提供页面，截完即关。
# 只读 prototype/index.html，输出到 tests-v2/reference/prototype/。
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
OUT="$ROOT/tests-v2/reference/prototype"
PORT=8799
mkdir -p "$OUT"

node -e '
  const fs=require("fs"), http=require("http");
  const html=fs.readFileSync(process.argv[1],"utf8");
  http.createServer((req,res)=>{
    const js=new URL(req.url,"http://x").searchParams.get("js")||"";
    const tail="<script>window.addEventListener(\"load\",function(){setTimeout(function(){try{switchPlatform(\"mac\");"+js+"}catch(e){document.title=\"ERR \"+e.message}},30);});</script></body>";
    res.writeHead(200,{"Content-Type":"text/html; charset=utf-8"});
    res.end(html.replace("</body>",tail));
  }).listen(Number(process.argv[2]),"127.0.0.1");
' "$ROOT/prototype/index.html" "$PORT" &
SERVER=$!
trap 'kill $SERVER 2>/dev/null' EXIT
sleep 1

ONLY="${1:-}"
shot() {
  local name="$1"; local js="$2"
  if [ -n "$ONLY" ] && [[ "$name" != $ONLY ]]; then return; fi
  local q
  q=$(node -e 'process.stdout.write(encodeURIComponent(process.argv[1]))' "$js")
  "$CHROME" --headless=new --disable-gpu --hide-scrollbars --window-size=1440,900 \
    --user-data-dir="$(mktemp -d /tmp/deskkit-chrome.XXXXXX)" \
    --virtual-time-budget=4000 --screenshot="$OUT/$name.png" "http://127.0.0.1:$PORT/?js=$q&n=$name" >/dev/null 2>&1
  echo "$name"
}

shot a1 "openView('A');"
shot a3 "openView('A');setQuery('w');"
shot a3calc "openView('A');setQuery('520+999-1314');"
shot a4 "openView('A');setQuery('zzznomatch');"
shot trig_fy "openView('A');setQuery('fy hello world');"
shot trig_color "openView('A');setQuery('color #3B6CF6');"
shot trig_clip "openView('A');setQuery('clip ');"
shot action "openView('A');setQuery('w');setTimeout(function(){openActionMenu();},400);"
shot cheat "openView('A');openCheat();"
shot b "openView('B');"
shot b_edit "openView('B');store.panel.edit=true;renderAll();"
shot b_menu "openView('B');openCellMenu(document.querySelector('.cell80'),520,320);"
shot c_grid "openView('C');"
shot c_calc "openView('C');openToolPage('calc','C');"
shot c_unit "openView('C');openToolPage('unit','C');"
shot c_translate "openView('C');openToolPage('translate','C');"
shot c_clipboard "openView('C');openToolPage('clipboard','C');"
shot c_color "openView('C');openToolPage('color','C');"
shot c_timestamp "openView('C');openToolPage('timestamp','C');"
shot c_picker "openView('C');openToolPage('picker','C');"
shot c_qr "openView('C');openToolPage('qr','C');"
shot e_general "openView('E');store.settings.tab='general';renderAll();"
shot e_hotkeys "openView('E');store.settings.tab='hotkeys';renderAll();"
shot e_about "openView('E');store.settings.tab='about';renderAll();"
shot f1 "openView('F');"
shot f2 "openView('F');store.guide.step=2;renderAll();"
shot f3 "openView('F');store.guide.step=3;renderAll();"
