using System.Text.Json;
using System.Text.RegularExpressions;

namespace Claupet;

/// <summary>
/// La mesa de diseño de Claupet Canvas: las secciones HTML y SVG de un canvas como pantallas sobre una mesa con
/// puntitos, que se mueve y se acerca (como una mesa de trabajo de diseño). Es una sola página (en CanvasLivePage): cada
/// pantalla va en su iframe aislado, como en la exportación, con un guion que solo avisa a la mesa su alto, la rueda,
/// algunas teclas y el elemento que se señaló. La mesa le habla a Claupet con mensajes cortos ({t:…}) y Claupet a ella.
/// </summary>
static class CanvasDesk
{
    /// <summary>Los anchos de cada dispositivo (escritorio, tableta, móvil).</summary>
    public static readonly int[] Widths = [1280, 820, 390];

    /// <summary>Las secciones que van a la mesa: las HTML y las SVG.</summary>
    public static IEnumerable<CanvasSection> Screens(CanvasDoc doc) => doc.Sections.Where(IsScreen);

    public static bool IsScreen(CanvasSection s) => s.Kind is CanvasKinds.Html or CanvasKinds.Svg && s.Content.Length > 0;

    public static bool HasScreens(CanvasDoc doc) => doc.Sections.Any(IsScreen);

    /// <summary>El nombre de una pantalla: su título, o «Pantalla N».</summary>
    public static string Name(CanvasDoc doc, CanvasSection s) =>
        s.Title is { Length: > 0 } title ? title : L.T("Pantalla {0}", Screens(doc).TakeWhile(x => x != s).Count() + 1);

    /// <summary>Los colores de la mesa (los del tema de la Console), como colores de CSS.</summary>
    public sealed record Theme(string Back, string Surface, string Raised, string Pill, string Line, string Text, string Secondary, string Tertiary, string Accent, string Dots, bool Dark);

    /// <summary>Lo que se le manda a la mesa de cada pantalla: su página (en el iframe), su miniatura y su código.</summary>
    public static object Boards(CanvasDoc doc) => Screens(doc).Select(s => new
    {
        id = s.Id,
        name = Name(doc, s),
        html = CanvasExport.FramePage(s) + CanvasExport.FrameHeight + FrameScript,
        thumb = CanvasExport.FramePage(s),
        code = s.Content.Length > 60_000 ? s.Content[..60_000] : s.Content,
    }).ToArray();

    /// <summary>El mensaje que pone al día las pantallas (sin volver a cargar la mesa, que conserva dónde se miraba).</summary>
    public static string Update(CanvasDoc doc) => JsonSerializer.Serialize(new { t = "boards", boards = Boards(doc) });

    /// <summary>La página de la mesa, con las pantallas de ese momento y la elegida.</summary>
    public static string Page(CanvasDoc doc, Theme theme, string? selected)
    {
        var data = JsonSerializer.Serialize(new { boards = Boards(doc), sel = selected, sandbox = CanvasExport.Sandbox });
        var vars = $":root{{--back:{theme.Back};--surface:{theme.Surface};--raised:{theme.Raised};--pill:{theme.Pill};--line:{theme.Line};"
            + $"--text:{theme.Text};--sec:{theme.Secondary};--ter:{theme.Tertiary};--acc:{theme.Accent};--dots:{theme.Dots};"
            + $"--shadow:{(theme.Dark ? "rgba(0,0,0,.5)" : "rgba(20,20,30,.16)")}}}";
        // El JSON va en un <script type=application/json>: el serializador ya escapa «<», así que «</script>» no lo corta.
        return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"generator\" content=\"Claupet Canvas\"><style>" + vars + Style + "</style></head><body>"
            + Body + "<script type=\"application/json\" id=\"data\">" + data + "</script><script>" + Script + "</script></body></html>";
    }

    // ── El pedido a la sesión ligada ────────────────────────────────────────────────────

    const string Mark = "[Claupet Canvas]";

    /// <summary>
    /// El pedido que va a la sesión: en la primera línea, qué canvas, qué pantalla y (si se señaló) qué elemento, para que
    /// el agente sepa dónde cambiar con canvas_edit; después, lo que escribió la persona.
    /// </summary>
    public static string Prompt(CanvasDoc doc, CanvasSection? screen, string? element, string? path, string text)
    {
        var where = $"{Mark} Canvas «{doc.Title}» (id {doc.Id})";
        if (screen is not null) where += $" · pantalla «{Name(doc, screen)}» (sección {screen.Id})";
        if (element is { Length: > 0 }) where += $" · elemento «{element}»" + (path is { Length: > 0 } ? $" ({path})" : "");
        return where + ". Haz el cambio con canvas_edit.\n" + text.Trim();
    }

    static readonly Regex Head = new(@"^\[Claupet Canvas\] Canvas «.*?» \(id [^)]*\)(?: · pantalla «(?<screen>.*?)» \(sección [^)]*\))?(?: · elemento «(?<el>.*?)»(?: \([^\n]*\))?)?\. Haz el cambio con canvas_edit\.\n", RegexOptions.Compiled);

    /// <summary>Un pedido hecho desde la mesa, separado para mostrarlo en el chat: la pantalla, el elemento y el texto.</summary>
    public static (string? Screen, string? Element, string Text) Parse(string ask)
    {
        var m = Head.Match(ask.ReplaceLineEndings("\n"));
        if (!m.Success) return (null, null, ask);
        return (m.Groups["screen"].Success ? m.Groups["screen"].Value : null, m.Groups["el"].Success ? m.Groups["el"].Value : null,
            ask.ReplaceLineEndings("\n")[m.Length..]);
    }

    // ── La página ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dentro de cada pantalla: señalar un elemento (con un recuadro que lo sigue) y avisar el toque, la rueda que la
    /// pantalla no usa y algunas teclas (fuera de las cajas de texto). No ve nada de la mesa ni de Claupet.
    /// </summary>
    const string FrameScript = """
        <script>(function(){var on=false,box=null,P=parent;
        function mk(){box=document.createElement('div');box.style.cssText='position:fixed;pointer-events:none;z-index:2147483647;border:1.5px solid #7c9cff;background:rgba(124,156,255,.14);border-radius:3px;transition:all .08s;display:none';document.documentElement.appendChild(box)}
        function path(t){var p=[];for(;t&&t.nodeType===1&&t!==document.body&&t!==document.documentElement&&p.length<6;t=t.parentElement){var s=t.tagName.toLowerCase();if(t.id){p.unshift(s+'#'+t.id);break}if(t.classList&&t.classList.length)s+='.'+t.classList[0];p.unshift(s)}return p.join(' > ')}
        function desc(t){var s=t.tagName.toLowerCase();if(t.classList&&t.classList.length)s+='.'+t.classList[0];var x=(t.innerText||t.textContent||'').trim().replace(/\s+/g,' ');if(x)s+=' '+(x.length>32?x.slice(0,32)+'…':x);return s}
        function can(n,dy){for(var e=n&&n.nodeType===1?n:n&&n.parentElement;e;e=e.parentElement){var o=getComputedStyle(e).overflowY,root=e===document.scrollingElement;if(root)continue;if((o==='auto'||o==='scroll'||o==='overlay')&&e.scrollHeight>e.clientHeight+1&&(dy<0?e.scrollTop>0:Math.ceil(e.scrollTop+e.clientHeight)<e.scrollHeight))return true}return false}
        addEventListener('message',function(e){if(e.source!==P)return;var d=e.data;if(d&&typeof d.claupetPick==='boolean'){on=d.claupetPick;document.documentElement.style.cursor=on?'crosshair':'';if(!on&&box)box.style.display='none'}});
        addEventListener('mousemove',function(e){if(!on)return;if(!box)mk();var r=e.target.getBoundingClientRect();box.style.display='block';box.style.left=r.left+'px';box.style.top=r.top+'px';box.style.width=r.width+'px';box.style.height=r.height+'px'},true);
        addEventListener('click',function(e){if(!on)return;e.preventDefault();e.stopPropagation();var t=e.target;on=false;document.documentElement.style.cursor='';if(box)box.style.display='none';P.postMessage({claupetPicked:{el:desc(t),path:path(t)}},'*')},true);
        addEventListener('pointerdown',function(e){P.postMessage({claupetTouch:1},'*')},true);
        addEventListener('wheel',function(e){if(!e.ctrlKey&&can(e.target,e.deltaY))return;e.preventDefault();var k=e.deltaMode===1?40:e.deltaMode===2?800:1;P.postMessage({claupetDeskWheel:{dx:e.deltaX*k,dy:e.deltaY*k,ctrl:e.ctrlKey,shift:e.shiftKey,x:e.clientX,y:e.clientY}},'*')},{passive:false});
        function typing(t){return t&&(t.isContentEditable||/^(input|textarea|select)$/i.test(t.tagName))}
        addEventListener('keydown',function(e){if(typing(e.target)&&e.key!=='Escape')return;if(e.code==='Space'||e.key==='Escape'||(e.key==='e'&&!e.ctrlKey)||(e.shiftKey&&e.code==='Digit1')||(e.ctrlKey&&e.key==='j')){if(e.code==='Space'||e.ctrlKey)e.preventDefault();P.postMessage({claupetKey:{k:e.code==='Space'?' ':e.key,code:e.code,ctrl:e.ctrlKey,shift:e.shiftKey,down:true}},'*')}},true);
        addEventListener('keyup',function(e){if(e.code==='Space')P.postMessage({claupetKey:{k:' ',down:false}},'*')},true);
        })()</script>
        """;

    const string Style = """
        *{box-sizing:border-box}html,body{margin:0;height:100%;overflow:hidden;background:var(--back);color:var(--text);font:13px/1.45 'Segoe UI',system-ui,sans-serif;user-select:none}
        #app{position:absolute;inset:0;display:flex}
        #rail{width:176px;flex:none;background:var(--surface);border-right:1px solid var(--line);padding:10px 8px;display:flex;flex-direction:column;gap:6px;overflow:auto;overflow-x:hidden;transition:width .32s cubic-bezier(.2,.8,.2,1),padding .32s cubic-bezier(.2,.8,.2,1),opacity .25s}
        #rail.off{width:0;padding:10px 0;opacity:0;border:0}
        .thumb{border-radius:10px;padding:6px;cursor:pointer;transition:background .18s,transform .18s,box-shadow .18s;animation:up .4s cubic-bezier(.2,.8,.2,1) both;flex:none}
        .thumb:hover{background:var(--pill);transform:translateY(-1px)}.thumb.on{background:var(--pill);box-shadow:inset 0 0 0 1.5px var(--acc)}
        .thumb .pv{height:96px;border-radius:6px;background:#fff;overflow:hidden;position:relative}
        .thumb .pv iframe{position:absolute;top:0;border:0;transform-origin:0 0;pointer-events:none;background:#fff}
        .thumb .nm{font-size:11.5px;color:var(--sec);margin-top:5px;display:flex;justify-content:space-between;gap:6px}.thumb .nm span:first-child{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.thumb .nm span+span{color:var(--ter)}
        #desk{position:relative;flex:1;overflow:hidden;background-color:var(--back);background-image:radial-gradient(circle,var(--dots) 1.1px,transparent 1.4px)}
        #desk.smooth{transition:background-size .55s cubic-bezier(.2,.8,.2,1),background-position .55s cubic-bezier(.2,.8,.2,1)}
        #world{position:absolute;left:0;top:0;transform-origin:0 0}#world.smooth{transition:transform .55s cubic-bezier(.2,.8,.2,1)}
        #shield{position:absolute;inset:0;display:none;z-index:5;cursor:grab}#desk.pan #shield{display:block}#desk.panning #shield{cursor:grabbing}
        .board{position:absolute;transition:width .42s cubic-bezier(.2,.8,.2,1),height .42s cubic-bezier(.2,.8,.2,1),left .42s cubic-bezier(.2,.8,.2,1);animation:boardIn .6s cubic-bezier(.2,.8,.2,1) both}
        @keyframes boardIn{from{opacity:0;transform:translateY(40px) scale(.97)}to{opacity:1;transform:none}}
        .lbl{position:absolute;bottom:100%;left:0;padding-bottom:8px;font-size:13px;color:var(--ter);display:flex;gap:8px;white-space:nowrap;transform-origin:0 100%;cursor:pointer}
        .lbl b{color:var(--sec);font-weight:500}.board.sel .lbl b{color:var(--acc)}
        .page{position:absolute;inset:0;background:#fff;border-radius:6px;overflow:hidden;box-shadow:0 10px 40px var(--shadow);transition:box-shadow .25s}
        .page iframe{display:block;width:100%;height:100%;border:0;background:#fff}
        .board.sel .page{box-shadow:0 0 0 2px var(--acc),0 10px 40px var(--shadow)}
        .board.busy .page::after{content:'';position:absolute;inset:0;pointer-events:none;background:linear-gradient(110deg,transparent 30%,rgba(217,119,87,.20) 50%,transparent 70%);background-size:250% 100%;animation:shim 1.2s linear infinite}
        @keyframes shim{from{background-position:120% 0}to{background-position:-120% 0}}
        .board.flash .page{animation:flash 1.4s cubic-bezier(.2,.8,.2,1)}
        @keyframes flash{0%{box-shadow:0 0 0 2px var(--acc),0 0 0 0 rgba(217,119,87,.9)}100%{box-shadow:0 0 0 2px var(--acc),0 0 0 26px rgba(217,119,87,0)}}
        #desk.picking .page{box-shadow:0 0 0 1.5px #7c9cff,0 10px 40px var(--shadow)}
        #mini{position:absolute;left:14px;bottom:14px;width:150px;height:90px;background:var(--surface);border:1px solid var(--line);border-radius:10px;overflow:hidden;z-index:6;animation:up .5s .3s cubic-bezier(.2,.8,.2,1) both;cursor:pointer}
        #mini i{position:absolute;background:var(--ter);opacity:.6;border-radius:2px;transition:all .4s cubic-bezier(.2,.8,.2,1)}#mini i.on{background:var(--acc);opacity:1}
        #mini .vp{position:absolute;border:1.5px solid var(--acc);border-radius:3px;background:rgba(217,119,87,.08)}
        #code{width:0;flex:none;background:var(--surface);border-left:1px solid var(--line);overflow:hidden;transition:width .36s cubic-bezier(.2,.8,.2,1);display:flex}
        #code.open{width:42%}#code.full{width:100%;border:0}
        #code pre{margin:0;padding:16px 18px;font:12.5px/20px 'Cascadia Mono',Consolas,monospace;color:var(--text);overflow:auto;flex:1;white-space:pre;user-select:text;min-width:320px}
        #code .t{color:#7c9cff}#code .k{color:#c792ea}#code .s{color:#c3a35e}#code .c{color:var(--ter);font-style:italic}
        #empty{position:absolute;inset:0;display:none;place-items:center;color:var(--ter);font-size:14px}
        @keyframes up{from{opacity:0;transform:translateY(12px)}to{opacity:1;transform:none}}
        """;

    const string Body = """
        <div id="app"><div id="rail"></div><div id="desk"><div id="world"></div><div id="shield"></div><div id="mini"></div><div id="empty"></div></div><div id="code"><pre id="pre"></pre></div></div>
        """;

    /// <summary>La mesa: acomodar las pantallas, moverse, acercarse, elegir, señalar y hablar con Claupet.</summary>
    const string Script = """
        (function(){
        var W=window.chrome&&chrome.webview,D=JSON.parse(document.getElementById('data').textContent),SB=D.sandbox;
        var desk=document.getElementById('desk'),world=document.getElementById('world'),rail=document.getElementById('rail'),mini=document.getElementById('mini'),code=document.getElementById('code'),pre=document.getElementById('pre');
        var boards=[],byId={},z=.4,px=60,py=60,sel=D.sel||null,picking=false,fitted=false,space=false,drag=null,view='desk',GAP=140;
        var MINH={1280:800,820:1080,390:844};
        function post(o){if(W)W.postMessage(o)}
        function esc(s){return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;')}
        function sync(list){var keep={};list.forEach(function(b,i){keep[b.id]=1;var o=byId[b.id];
          if(!o){o=byId[b.id]={id:b.id,w:1280,content:0};make(o,i);world.appendChild(o.el)}
          o.name=b.name;o.thumb=b.thumb;o.code=b.code;o.el.querySelector('.lbl b').textContent=b.name;
          if(o.html!==b.html){var again=o.html!==undefined;o.html=b.html;o.frame.srcdoc=b.html;if(again)flash(o)}});
          Object.keys(byId).forEach(function(id){if(!keep[id]){byId[id].el.remove();delete byId[id]}});
          boards=list.map(function(b){return byId[b.id]});
          if(!sel||!byId[sel])sel=boards.length?boards[0].id:null;
          document.getElementById('empty').style.display=boards.length?'none':'grid';
          layout();drawRail();mark(false);
          if(!fitted&&boards.length&&desk.clientWidth>0){fitted=true;fitAll(false)}}
        function make(o,i){var d=document.createElement('div');d.className='board';d.style.animationDelay=(i*.1)+'s';
          d.innerHTML='<div class="lbl"><b></b><span></span></div><div class="page"><iframe sandbox="'+SB+'"></iframe></div>';
          o.el=d;o.frame=d.querySelector('iframe');d.querySelector('.lbl').addEventListener('mousedown',function(e){e.stopPropagation();select(o.id,true)})}
        function layout(){var x=0;boards.forEach(function(b){b.x=x;b.h=Math.max(MINH[b.w]||600,Math.min(b.content||0,8000));var s=b.el.style;s.left=x+'px';s.top='0px';s.width=b.w+'px';s.height=b.h+'px';
          b.el.querySelector('.lbl span').textContent=b.w+' × '+Math.round(b.h);x+=b.w+GAP});drawMini()}
        function bounds(){if(!boards.length)return{w:1,h:1};var l=boards[boards.length-1];return{w:l.x+l.w,h:Math.max.apply(null,boards.map(function(b){return b.h}))}}
        function smooth(){world.classList.add('smooth');desk.classList.add('smooth');clearTimeout(smooth.t);smooth.t=setTimeout(function(){world.classList.remove('smooth');desk.classList.remove('smooth')},580)}
        function apply(){world.style.transform='translate('+px+'px,'+py+'px) scale('+z+')';var g=22*Math.max(.55,Math.min(2,z*2));desk.style.backgroundSize=g+'px '+g+'px';desk.style.backgroundPosition=px+'px '+py+'px';
          var k=Math.min(3,1/z);boards.forEach(function(b){b.el.querySelector('.lbl').style.transform='scale('+k+')'});drawMini();post({t:'zoom',z:z})}
        function fitAll(anim){if(!boards.length)return;var r=bounds(),dw=desk.clientWidth,dh=desk.clientHeight;z=Math.max(.05,Math.min(1,(dw-120)/r.w,(dh-110)/r.h));px=(dw-r.w*z)/2;py=Math.max(50,(dh-r.h*z)/2+14);if(anim!==false)smooth();apply()}
        function focusOn(id){var b=byId[id];if(!b)return;var dw=desk.clientWidth,dh=desk.clientHeight;z=Math.max(.05,Math.min(1,(dw-120)/b.w,(dh-110)/b.h));px=(dw-b.w*z)/2-b.x*z;py=Math.max(50,(dh-b.h*z)/2+14);smooth();apply()}
        function zoomAt(f,cx,cy){if(cx===undefined){cx=desk.clientWidth/2;cy=desk.clientHeight/2}var nz=Math.min(4,Math.max(.05,z*f));px=cx-(cx-px)*nz/z;py=cy-(cy-py)*nz/z;z=nz;apply()}
        function select(id,tell){if(!byId[id])return;var changed=sel!==id;sel=id;mark(true);if(changed||tell)post({t:'sel',id:id,w:byId[id].w})}
        function mark(){boards.forEach(function(b){b.el.classList.toggle('sel',b.id===sel)});[].forEach.call(rail.querySelectorAll('.thumb'),function(t){t.classList.toggle('on',t.dataset.id===sel)});drawCode();drawMini()}
        function flash(b){b.el.classList.remove('flash');void b.el.offsetWidth;b.el.classList.add('flash');setTimeout(function(){b.el.classList.remove('flash')},1500)}
        function drawRail(){rail.innerHTML='';boards.forEach(function(b,i){var t=document.createElement('div');t.className='thumb';t.dataset.id=b.id;t.style.animationDelay=(i*.05)+'s';
          var s=Math.min(148/b.w,96/(MINH[b.w]||800));t.innerHTML='<div class="pv"><iframe sandbox="" tabindex="-1"></iframe></div><div class="nm"><span></span><span>'+b.w+'</span></div>';
          var f=t.querySelector('iframe');f.style.width=b.w+'px';f.style.height=(96/s)+'px';f.style.transform='scale('+s+')';f.style.left=((148-b.w*s)/2)+'px';f.srcdoc=b.thumb;
          t.querySelector('.nm span').textContent=b.name;t.title=b.name;t.onclick=function(){select(b.id,true);focusOn(b.id)};rail.appendChild(t)});mark()}
        function drawMini(){if(!boards.length){mini.innerHTML='';return}var r=bounds(),s=Math.min(130/r.w,62/r.h),h='';boards.forEach(function(b){h+='<i class="'+(b.id===sel?'on':'')+'" style="left:'+(b.x*s+10)+'px;top:'+(14)+'px;width:'+Math.max(2,b.w*s)+'px;height:'+Math.max(2,b.h*s)+'px"></i>'});
          var vw=desk.clientWidth/z*s,vh=desk.clientHeight/z*s;h+='<div class="vp" style="left:'+(-px/z*s+10)+'px;top:'+(-py/z*s+14)+'px;width:'+vw+'px;height:'+vh+'px"></div>';mini.innerHTML=h}
        function drawCode(){if(view==='desk'||!byId[sel])return;var s=esc(byId[sel].code);
          s=s.replace(/(&lt;!--[\s\S]*?--&gt;)/g,'<span class="c">$1</span>').replace(/(&lt;\/?)([a-zA-Z][\w-]*)/g,'$1<span class="t">$2</span>').replace(/\s([\w-:]+)=("[^"]*")/g,' <span class="k">$1</span>=<span class="s">$2</span>');pre.innerHTML=s}
        function setPick(on){picking=on;desk.classList.toggle('picking',on);boards.forEach(function(b){try{b.frame.contentWindow.postMessage({claupetPick:on},'*')}catch(e){}});post({t:'picking',on:on})}
        function setView(v){view=v;code.classList.toggle('open',v==='split');code.classList.toggle('full',v==='code');drawCode();setTimeout(function(){if(v!=='code'&&sel)focusOn(sel)},380)}
        function key(k){if(k.k===' '){space=k.down;desk.classList.toggle('pan',space||!!drag);return}if(!k.down)return;
          if(k.ctrl&&(k.k==='j'||k.k==='J')){post({t:'key',k:'j'});return}
          if(k.k==='Escape'){if(picking)setPick(false);else post({t:'key',k:'escape'});return}
          if(k.k==='e'||k.k==='E'){setPick(!picking);return}
          if(k.shift&&(k.code==='Digit1'||k.k==='!')){fitAll();return}}
        function wheel(dx,dy,ctrl,shift,cx,cy){if(ctrl){zoomAt(Math.exp(-dy*.0022),cx,cy)}else{px-=shift?dy:dx;py-=shift?0:dy;apply()}}
        addEventListener('message',function(e){var d=e.data;if(!d)return;var b=null;for(var i=0;i<boards.length;i++)if(boards[i].frame.contentWindow===e.source){b=boards[i];break}if(!b)return;
          if(typeof d.claupetHeight==='number'&&isFinite(d.claupetHeight)){var n=Math.ceil(d.claupetHeight);if(Math.abs(n-(b.content||0))>2){b.content=n;layout()}}
          else if(d.claupetTouch){if(sel!==b.id)select(b.id,true)}
          else if(d.claupetPicked){picking=false;desk.classList.remove('picking');boards.forEach(function(o){if(o!==b)try{o.frame.contentWindow.postMessage({claupetPick:false},'*')}catch(x){}});select(b.id,false);
            post({t:'pick',id:b.id,el:String(d.claupetPicked.el||'').slice(0,80),path:String(d.claupetPicked.path||'').slice(0,200)})}
          else if(d.claupetDeskWheel){var w=d.claupetDeskWheel,r=b.frame.getBoundingClientRect(),dr=desk.getBoundingClientRect();wheel(+w.dx||0,+w.dy||0,!!w.ctrl,!!w.shift,r.left-dr.left+(+w.x||0)*z,r.top-dr.top+(+w.y||0)*z)}
          else if(d.claupetKey)key(d.claupetKey)});
        if(W)W.addEventListener('message',function(e){var m=e.data;if(!m||!m.t)return;
          if(m.t==='boards')sync(m.boards);
          else if(m.t==='device'){var b=byId[sel];if(b){b.w=m.w;layout();drawRail();setTimeout(function(){focusOn(b.id)},40)}}
          else if(m.t==='zoom')zoomAt(m.f);
          else if(m.t==='fit')fitAll();
          else if(m.t==='focus'){select(m.id,false);focusOn(m.id)}
          else if(m.t==='view')setView(m.v);
          else if(m.t==='rail'){rail.classList.toggle('off',!m.on);setTimeout(function(){drawMini()},340)}
          else if(m.t==='pick')setPick(m.on);
          else if(m.t==='busy'){var o=byId[m.id];boards.forEach(function(x){x.el.classList.remove('busy')});if(o&&m.on)o.el.classList.add('busy')}});
        desk.addEventListener('wheel',function(e){e.preventDefault();var r=desk.getBoundingClientRect();wheel(e.deltaX,e.deltaY,e.ctrlKey,e.shiftKey,e.clientX-r.left,e.clientY-r.top)},{passive:false});
        desk.addEventListener('mousedown',function(e){if(e.target.closest('#mini'))return;var onBoard=e.target.closest('.board');if(space||e.button===1||!onBoard){drag={x:e.clientX,y:e.clientY,px:px,py:py};desk.classList.add('pan','panning');e.preventDefault()}});
        addEventListener('mousemove',function(e){if(!drag)return;px=drag.px+e.clientX-drag.x;py=drag.py+e.clientY-drag.y;apply()});
        addEventListener('mouseup',function(){if(!drag)return;drag=null;desk.classList.remove('panning');if(!space)desk.classList.remove('pan')});
        mini.addEventListener('mousedown',function(e){e.stopPropagation();fitAll()});
        addEventListener('keydown',function(e){if(e.code==='Space')e.preventDefault();key({k:e.code==='Space'?' ':e.key,code:e.code,ctrl:e.ctrlKey,shift:e.shiftKey,down:true})});
        addEventListener('keyup',function(e){if(e.code==='Space')key({k:' ',down:false})});
        addEventListener('resize',function(){if(!fitted&&boards.length&&desk.clientWidth>0){fitted=true;fitAll(false)}else drawMini()});
        apply();sync(D.boards);post({t:'ready'});
        })();
        """;
}
