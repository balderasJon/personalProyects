(function(){
'use strict';
/* ================= datos ================= */
var COLS=['productos','ventas','gastos','semanas','config'];
var D={productos:{},ventas:{},gastos:{},semanas:{},config:{}};
var CFG_DEF={inversion:49000,socioA:'Persona A',socioB:'Persona B',repartoA:50,stockBajo:1,metaMensual:0,tipoCambioUsd:0};
var CANALES=['Facebook Marketplace','WhatsApp Business','Grupos de Facebook','Conocidos','Bazar o escuela','Instagram','MercadoLibre','Otro'];
var ESTADOS_V=['apartado','pagado','entregado','cancelado'];
var EST_V_TXT={apartado:'Apartado',pagado:'Pagado',entregado:'Entregado',cancelado:'Cancelado'};
var EST_P_TXT={nuevo:'Nuevo',danado:'Dañado',incompleto:'Incompleto'};
var TIPOS_G=['Empaque','Traslados y gasolina','Publicidad','Comisión de plataforma','Impuestos','Otro'];
var mode='cargando', LKEY='pallet-control-v1', version=-1;

function cfg(){var c=Object.assign({},CFG_DEF,D.config.general||{});return c}
function uid(){return ''}
function today(){var d=new Date();return d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0')}

/* ---- API del servidor local ---- */
var RUTA={productos:'productos',ventas:'ventas',gastos:'gastos',semanas:'semanas'};
function api(method,url,body){
  return fetch('/api/'+url,{method:method,headers:body!==undefined?{'Content-Type':'application/json'}:{},body:body!==undefined?JSON.stringify(body):undefined})
    .then(function(r){
      if(r.status===204)return null;
      return r.text().then(function(t){var j=null;try{j=t?JSON.parse(t):null}catch(e){}
        if(!r.ok){var m=(j&&(j.mensaje||j.title))||('Error '+r.status);throw {mensaje:m,status:r.status}}
        return j});
    },function(){throw {mensaje:'No hay conexión con el servidor. Revisen que la aplicación siga abierta en la computadora principal.'}});
}
function porId(arr,k){var m={};(arr||[]).forEach(function(o){m[o[k||'id']]=o});return m}
function cargar(){
  return api('GET','estado').then(function(e){
    version=e.version;
    (e.productos||[]).forEach(function(p){p.creado=Date.parse(p.creadoUtc)||0});
    (e.ventas||[]).forEach(function(v){v.creado=Date.parse(v.creadoUtc)||0;if(!v.fechaPago)v.fechaPago=''});
    (e.gastos||[]).forEach(function(g){g.creado=Date.parse(g.creadoUtc)||0});
    (e.semanas||[]).forEach(function(w){w.id=w.inicio;w.semana=w.inicio});
    D.productos=porId(e.productos);D.ventas=porId(e.ventas);D.gastos=porId(e.gastos);D.semanas=porId(e.semanas,'inicio');
    D.config={general:e.configuracion||{}};
    if(e.catalogos){if(e.catalogos.canales)CANALES=e.catalogos.canales;if(e.catalogos.tiposGasto)TIPOS_G=e.catalogos.tiposGasto}
    mode='ok';setStatus();renderAll();
  },function(err){mode='error';setStatus();throw err});
}
function fail(e){toast((e&&e.mensaje)||'No se guardó. Inténtalo de nuevo.');throw e}
function put(col,id,obj){
  var req;
  if(col==='config')req=api('PUT','configuracion',obj);
  else if(col==='semanas')req=api('PUT','semanas/'+id,obj);
  else if(id&&D[col][id])req=api('PUT',RUTA[col]+'/'+id,obj);
  else req=api('POST',RUTA[col],obj);
  return req.then(cargar).catch(fail);
}
function del(col,id){return api('DELETE',RUTA[col]+'/'+id).then(cargar).catch(fail)}

// Sincronización entre socios: se consulta la versión de los datos cada pocos segundos.
function vigilar(){
  setInterval(function(){
    if(document.hidden)return;
    api('GET','version').then(function(r){
      if(mode!=='ok'){mode='ok';setStatus()}
      if(r.version!==version)cargar().catch(function(){});
    },function(){if(mode!=='error'){mode='error';setStatus()}});
  },4000);
}
function setStatus(){
  var el=document.getElementById('status'),t,cl='status';
  if(mode==='cargando'){t='Conectando…'}
  else if(mode==='error'){t='Sin conexión con el servidor';cl+=' warn'}
  else{t='Conectado · red local';cl+=' ok'}
  el.textContent=t;el.className=cl;
  var mt=document.getElementById('modoTxt');
  mt.innerHTML='Los datos se guardan en una base SQLite en la computadora donde corre la aplicación. Los dos socios ven los mismos registros desde su navegador; la pantalla se actualiza sola cuando el otro guarda algo. Cada día al abrir la aplicación se crea una copia automática en la carpeta <span class="mono">datos/respaldos</span>.';
}

/* ================= cálculos ================= */
var mx=new Intl.NumberFormat('es-MX',{style:'currency',currency:'MXN',maximumFractionDigits:0});
var mx2=new Intl.NumberFormat('es-MX',{style:'currency',currency:'MXN',maximumFractionDigits:2});
var nf=new Intl.NumberFormat('es-MX',{maximumFractionDigits:1});
function money(v){return isFinite(v)?mx.format(Math.round(v)):'—'}
function money2(v){return isFinite(v)?mx2.format(v):'—'}
function num(v){return isFinite(v)?nf.format(v):'—'}
function pct(v){return isFinite(v)?Math.round(v)+'%':'—'}
function esc(s){return String(s==null?'':s).replace(/[&<>"']/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]})}
function fdate(s){if(!s)return '';var p=s.split('-');var d=new Date(+p[0],+p[1]-1,+p[2]);return d.toLocaleDateString('es-MX',{day:'numeric',month:'short'})}
function monday(s){var p=s.split('-');var d=new Date(+p[0],+p[1]-1,+p[2]);var w=(d.getDay()+6)%7;d.setDate(d.getDate()-w);
  return d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0')}
function addDays(s,n){var p=s.split('-');var d=new Date(+p[0],+p[1]-1,+p[2]+n);return d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0')}

function list(col){return Object.keys(D[col]).map(function(id){return Object.assign({id:id},D[col][id])})}
function ventasActivas(){return list('ventas').filter(function(v){return v.estado!=='cancelado'})}
function cobrada(v){return v.estado==='pagado'||v.estado==='entregado'}
function totalVenta(v){return (+v.cantidad||0)*(+v.precio||0)}
function vendidasDe(pid,exceptId){var s=0;list('ventas').forEach(function(v){if(v.productoId===pid&&v.estado!=='cancelado'&&v.id!==exceptId)s+=(+v.cantidad||0)});return s}

function stats(){
  var c=cfg(), P=list('productos'), V=list('ventas'), G=list('gastos');
  var piezas=0,valorObj=0,disp=0;
  var sold={};V.forEach(function(v){if(v.estado!=='cancelado')sold[v.productoId]=(sold[v.productoId]||0)+(+v.cantidad||0)});
  P.forEach(function(p){var q=+p.cantidad||0;piezas+=q;var d=Math.max(0,q-(sold[p.id]||0));disp+=d;valorObj+=d*(+p.precioObjetivo||0)});
  var cobrado=0,pzCobradas=0,porCobrar=0,pzApartadas=0,nAp=0,nPag=0;
  V.forEach(function(v){var t=totalVenta(v);
    if(cobrada(v)){cobrado+=t;pzCobradas+=(+v.cantidad||0)}
    else if(v.estado==='apartado'){porCobrar+=t;pzApartadas+=(+v.cantidad||0);nAp++}
    if(v.estado==='pagado')nPag++});
  var gastos=G.reduce(function(s,g){return s+(+g.monto||0)},0);
  var rec=cobrado-gastos, inv=+c.inversion||0;
  var horas=list('semanas').reduce(function(s,w){return s+(+w.horasA||0)+(+w.horasB||0)},0);
  return {c:c,P:P,V:V,G:G,piezas:piezas,disp:disp,valorObj:valorObj,cobrado:cobrado,pzCobradas:pzCobradas,porCobrar:porCobrar,pzApartadas:pzApartadas,nAp:nAp,nPag:nPag,
    gastos:gastos,rec:rec,inv:inv,falta:Math.max(0,inv-rec),util:rec-inv,costoPz:piezas>0?inv/piezas:NaN,
    precioProm:pzCobradas>0?cobrado/pzCobradas:NaN,pctVend:piezas>0?pzCobradas/piezas*100:NaN,horas:horas,sold:sold};
}

/* ================= navegación ================= */
var TAB='resumen';
var VALID=['resumen','inventario','ventas','gastos','semana','ajustes'];
function go(t){
  if(VALID.indexOf(t)<0)t='resumen';TAB=t;
  document.querySelectorAll('.tab').forEach(function(b){b.setAttribute('aria-selected',b.dataset.tab===t?'true':'false')});
  document.querySelectorAll('.view').forEach(function(v){v.hidden=v.dataset.view!==t});
  try{history.replaceState(null,'','#'+t)}catch(e){}
  try{localStorage.setItem(LKEY+'-tab',t)}catch(e){}
  renderAll();window.scrollTo(0,0);
}
document.querySelectorAll('.tab').forEach(function(b){b.addEventListener('click',function(){go(b.dataset.tab)})});

/* ================= render ================= */
function renderAll(){
  var s=stats();
  var ca=document.getElementById('cntApartado');ca.hidden=!s.nAp;ca.textContent=s.nAp;
  if(TAB==='resumen')renderResumen(s);
  else if(TAB==='inventario')renderInv(s);
  else if(TAB==='ventas')renderVentas(s);
  else if(TAB==='gastos')renderGastos(s);
  else if(TAB==='semana')renderSemana(s);
  else if(TAB==='ajustes')renderAjustes(s);
}
function kpi(l,v,n,tone){return '<div class="kpi"><div class="l">'+l+'</div><div class="v'+(tone?' '+tone:'')+'">'+v+'</div><div class="n">'+n+'</div></div>'}

function renderResumen(s){
  document.getElementById('hoyTxt').textContent=new Date().toLocaleDateString('es-MX',{weekday:'long',day:'numeric',month:'long',year:'numeric'});
  var p=s.inv>0?Math.max(0,Math.min(100,s.rec/s.inv*100)):0;
  document.getElementById('hFalta').textContent=s.falta>0?money(s.falta):'Recuperado';
  document.getElementById('hFalta').className='big'+(s.falta>0?'':' tone-good');
  document.getElementById('hFaltaN').textContent=s.falta>0
    ?'Llevan '+pct(p)+' de la inversión. '+(s.porCobrar>0?'Hay '+money(s.porCobrar)+' apartado sin cobrar.':'')
    :'Ya cubrieron la inversión. Ganancia acumulada: '+money(s.util)+'.';
  var m=document.getElementById('hMeter');m.style.width=p+'%';m.className=p>=100?'done':'';
  document.getElementById('hRec').textContent=money(s.rec);document.getElementById('hInv').textContent=money(s.inv);
  drawChart(s);
  var rep=(+s.c.repartoA||0)/100, uh=s.horas>0?s.util/s.horas:NaN;
  document.getElementById('kpis').innerHTML=
    kpi('Ventas cobradas',money(s.cobrado),num(s.pzCobradas)+' piezas pagadas o entregadas')+
    kpi('Gastos de operar',money(s.gastos),'Empaque, traslados, publicidad e impuestos')+
    kpi('Utilidad neta',money(s.util),'Cobrado menos gastos menos inversión',s.util>=0?'tone-good':'tone-bad')+
    kpi('Por socio',esc(money(s.util*rep))+' · '+esc(money(s.util*(1-rep))),esc(s.c.socioA)+' '+Math.round(rep*100)+'% · '+esc(s.c.socioB)+' '+Math.round(100-rep*100)+'%',s.util>=0?'tone-good':'tone-bad')+
    kpi('Porcentaje vendido',pct(s.pctVend),num(s.disp)+' de '+num(s.piezas)+' piezas siguen disponibles')+
    kpi('Precio promedio real',money(s.precioProm),'Costo por pieza: '+money(s.costoPz))+
    kpi('Inventario por vender',money(s.valorObj),'A precio objetivo. Si se vende todo: '+money(s.rec+s.porCobrar+s.valorObj-s.inv)+' de utilidad')+
    kpi('Utilidad por hora',money(uh),num(s.horas)+' h registradas entre los dos',isFinite(uh)?(uh>=0?'tone-good':'tone-bad'):'');
  // alertas
  var A=[];
  if(!s.P.length)A.push(['warn','Todavía no hay juguetes en el inventario. <button class="lnk" data-act="nuevoProducto">Agregar el primero</button> o <button class="lnk" data-act="importar">pegar la hoja desde Excel</button>.']);
  if(s.nAp)A.push(['warn',s.nAp+' venta'+(s.nAp>1?'s':'')+' apartada'+(s.nAp>1?'s':'')+' esperando pago ('+money(s.porCobrar)+'). <button class="lnk" data-go="ventas">Ver ventas</button>']);
  if(s.nPag)A.push(['','' +s.nPag+' venta'+(s.nPag>1?'s':'')+' pagada'+(s.nPag>1?'s':'')+' por entregar.']);
  var bajo=+s.c.stockBajo||0, pocosA=s.P.filter(function(p){var d=(+p.cantidad||0)-(s.sold[p.id]||0);return p.clase==='A'&&d>0&&d<=bajo});
  if(pocosA.length)A.push(['','Quedan pocas piezas de '+pocosA.length+' juguete'+(pocosA.length>1?'s':'')+' clase A: '+pocosA.slice(0,3).map(function(p){return esc(p.nombre)}).join(', ')+(pocosA.length>3?'…':'')+'.']);
  var sinPrecio=s.P.filter(function(p){return !(+p.precioObjetivo>0)}).length;
  if(sinPrecio)A.push(['warn',sinPrecio+' juguete'+(sinPrecio>1?'s':'')+' sin precio objetivo. <button class="lnk" data-go="inventario">Completar</button>']);
  var bajoCosto=s.V.filter(function(v){return v.estado!=='cancelado'&&isFinite(s.costoPz)&&(+v.precio||0)<s.costoPz}).length;
  if(bajoCosto)A.push(['bad',bajoCosto+' venta'+(bajoCosto>1?'s':'')+' por debajo del costo por pieza ('+money(s.costoPz)+'). Revisen si fue un combo.']);
  var danados=s.P.filter(function(p){return p.estado==='danado'||p.estado==='incompleto'}).length;
  if(danados)A.push(['bad',danados+' juguete'+(danados>1?'s':'')+' dañado'+(danados>1?'s':'')+' o incompleto'+(danados>1?'s':'')+'. Reclamen al proveedor lo antes posible.']);
  var wk=monday(today());if(s.P.length&&!list('semanas').some(function(w){return w.semana===wk}))A.push(['','Falta registrar los indicadores de esta semana. <button class="lnk" data-act="nuevaSemana">Registrar</button>']);
  if(!A.length)A.push(['ok','Todo al día. No hay apartados pendientes ni datos incompletos.']);
  document.getElementById('alerts').innerHTML=A.map(function(a){return '<div class="alert '+a[0]+'"><span class="dot"></span><div>'+a[1]+'</div></div>'}).join('');
  // por clase
  var rows=['A','B','C',''].map(function(k){var ps=s.P.filter(function(p){return (p.clase||'')===k});if(!ps.length&&k==='')return '';
    var q=0,v=0,d=0,val=0;ps.forEach(function(p){var qq=+p.cantidad||0,vv=s.sold[p.id]||0;q+=qq;v+=vv;d+=Math.max(0,qq-vv);val+=Math.max(0,qq-vv)*(+p.precioObjetivo||0)});
    return '<tr><td>'+(k?'<span class="pill '+k+'">'+k+'</span>':'<span class="muted">Sin clase</span>')+'</td><td class="r">'+ps.length+'</td><td class="r">'+num(q)+'</td><td class="r">'+num(v)+'</td><td class="r">'+num(d)+'</td><td class="r">'+money(val)+'</td></tr>'}).join('');
  document.getElementById('tblClase').innerHTML='<thead><tr><th>Clase</th><th class="r">Tipos</th><th class="r">Piezas</th><th class="r">Vendidas</th><th class="r">Disp.</th><th class="r">Valor</th></tr></thead><tbody>'+rows+'</tbody>';
}

function drawChart(s){
  var el=document.getElementById('chart');
  var ev=[];s.V.forEach(function(v){if(cobrada(v))ev.push([v.fechaPago||v.fecha||today(),totalVenta(v)])});
  s.G.forEach(function(g){ev.push([g.fecha||today(),-(+g.monto||0)])});
  if(!ev.length){el.innerHTML='<div class="empty" style="padding:20px"><p class="small muted">La línea aparece cuando registren la primera venta cobrada o el primer gasto. Empieza en −'+money(s.inv)+' y sube hasta cruzar el cero.</p></div>';return}
  ev.sort(function(a,b){return a[0]<b[0]?-1:1});
  var w0=monday(ev[0][0]),wN=monday(today());if(monday(ev[ev.length-1][0])>wN)wN=monday(ev[ev.length-1][0]);
  var weeks=[w0];while(weeks[weeks.length-1]<wN&&weeks.length<104)weeks.push(addDays(weeks[weeks.length-1],7));
  var cum=-s.inv,pts=[cum],i=0;
  weeks.forEach(function(w){var end=addDays(w,7);while(i<ev.length&&ev[i][0]<end){cum+=ev[i][1];i++}pts.push(cum)});
  var W=560,H=190,pl=58,pr=12,pt=12,pb=26;
  var mn=Math.min.apply(null,pts.concat([-s.inv])),mxv=Math.max.apply(null,pts.concat([0]));
  if(mxv-mn<1)mxv=mn+1;var pad=(mxv-mn)*.08;mn-=pad;mxv+=pad;
  function X(k){return pl+(W-pl-pr)*(pts.length>1?k/(pts.length-1):0)}
  function Y(v){return pt+(H-pt-pb)*(1-(v-mn)/(mxv-mn))}
  var path=pts.map(function(v,k){return (k?'L':'M')+X(k).toFixed(1)+' '+Y(v).toFixed(1)}).join(' ');
  var area=path+' L'+X(pts.length-1).toFixed(1)+' '+Y(mn).toFixed(1)+' L'+X(0).toFixed(1)+' '+Y(mn).toFixed(1)+' Z';
  var ticks=[mn+pad,(mn+mxv)/2,mxv-pad].map(function(v){return '<line class="ax" x1="'+pl+'" x2="'+(W-pr)+'" y1="'+Y(v)+'" y2="'+Y(v)+'"/><text x="'+(pl-6)+'" y="'+(Y(v)+4)+'" text-anchor="end">'+esc(compact(v))+'</text>'}).join('');
  var zero=(0>=mn&&0<=mxv)?'<line class="z" x1="'+pl+'" x2="'+(W-pr)+'" y1="'+Y(0)+'" y2="'+Y(0)+'"/><text x="'+(W-pr)+'" y="'+(Y(0)-5)+'" text-anchor="end">Inversión recuperada</text>':'';
  var last=pts[pts.length-1];
  var lab='<text x="'+pl+'" y="'+(H-6)+'">Sem. '+esc(fdate(w0))+'</text><text x="'+(W-pr)+'" y="'+(H-6)+'" text-anchor="end">Hoy</text>';
  el.innerHTML='<svg viewBox="0 0 '+W+' '+H+'" width="100%" role="img" aria-label="Dinero acumulado por semana, actualmente '+esc(money(last))+'">'+ticks+'<path class="ar" d="'+area+'"/>'+zero+'<path class="ln" d="'+path+'"/><circle class="pt" r="5" cx="'+X(pts.length-1)+'" cy="'+Y(last)+'"/><text class="strong" x="'+Math.max(pl+40,X(pts.length-1)-8)+'" y="'+Math.max(12,Y(last)-10)+'" text-anchor="end">'+esc(money(last))+'</text>'+lab+'</svg>';
}
function compact(v){var a=Math.abs(v);var s=a>=1000?(Math.round(a/100)/10)+'k':Math.round(a)+'';return (v<0?'−$':'$')+s}

/* ---- inventario ---- */
['fBuscar','fClase','fEstado','fStock'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){renderInv(stats())})});
function renderInv(s){
  document.getElementById('invSub').textContent=s.P.length?num(s.P.length)+' tipos · '+num(s.piezas)+' piezas · '+num(s.disp)+' disponibles · costo promedio '+money(s.costoPz)+' por pieza':'';
  var body=document.getElementById('invBody');
  if(!s.P.length){body.innerHTML='<div class="empty"><h3>Abran el pallet y cuenten</h3><p class="muted">Registren cada juguete con su cantidad, estado y precios. Si ya tienen la hoja en Excel, cópienla y péguenla aquí.</p><div class="row" style="justify-content:center"><button class="btn" data-act="importar">Importar desde Excel</button><button class="btn primary" data-act="nuevoProducto">+ Agregar juguete</button></div></div>';return}
  var q=document.getElementById('fBuscar').value.trim().toLowerCase(),fc=document.getElementById('fClase').value,fe=document.getElementById('fEstado').value,fs=document.getElementById('fStock').checked;
  var bajo=+s.c.stockBajo||0;
  var P=s.P.filter(function(p){
    var d=(+p.cantidad||0)-(s.sold[p.id]||0);
    if(q&&((p.codigo||'')+' '+(p.codigoBarras||'')+' '+(p.nombre||'')+' '+(p.categoria||'')).toLowerCase().indexOf(q)<0)return false;
    if(fc&&p.clase!==fc)return false;if(fe&&p.estado!==fe)return false;if(fs&&d<=0)return false;return true;
  }).sort(function(a,b){var ka=(a.clase||'Z'),kb=(b.clase||'Z');if(ka!==kb)return ka<kb?-1:1;return String(a.codigo||'').localeCompare(String(b.codigo||''),'es',{numeric:true})});
  var tq=0,tv=0,td=0;
  var rows=P.map(function(p){var qq=+p.cantidad||0,v=s.sold[p.id]||0,d=Math.max(0,qq-v);tq+=qq;tv+=v;td+=d;
    var dc=d===0?'zero':(d<=bajo?'low':'');
    return '<tr><td class="mono">'+esc(p.codigo)+(p.codigoBarras?'<span class="sub">'+esc(p.codigoBarras)+'</span>':'')+'</td><td class="wrap"><b>'+esc(p.nombre)+'</b>'+(p.notas?'<span class="sub">'+esc(p.notas)+'</span>':'')+'</td><td>'+esc(p.categoria)+'</td>'+
      '<td>'+(p.clase?'<span class="pill '+p.clase+'">'+p.clase+'</span>':'')+'</td><td><span class="pill '+(p.estado||'nuevo')+'">'+(EST_P_TXT[p.estado]||'Nuevo')+'</span></td>'+
      '<td class="r">'+num(qq)+'</td><td class="r">'+num(v)+'</td><td class="r '+dc+'">'+num(d)+'</td><td class="r">'+money(+p.precioLista)+'</td><td class="r"><b>'+money(+p.precioObjetivo)+'</b></td>'+
      '<td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap"><button class="btn sm" data-sell="'+p.id+'"'+(d<=0?' disabled':'')+'>Vender</button><button class="btn sm" data-editp="'+p.id+'">Editar</button></div></td></tr>'}).join('');
  body.innerHTML=P.length?'<div class="scroll"><table><thead><tr><th>Código</th><th>Juguete</th><th>Categoría</th><th>Clase</th><th>Estado</th><th class="r">Cant.</th><th class="r">Vendidas</th><th class="r">Disp.</th><th class="r">P. lista</th><th class="r">P. objetivo</th><th></th></tr></thead><tbody>'+rows+
    '</tbody><tfoot><tr><td colspan="5">'+P.length+' juguete'+(P.length>1?'s':'')+' en la vista</td><td class="r">'+num(tq)+'</td><td class="r">'+num(tv)+'</td><td class="r">'+num(td)+'</td><td colspan="3"></td></tr></tfoot></table></div>'
    :'<div class="empty"><p class="muted">Ningún juguete coincide con los filtros.</p></div>';
}

/* ---- ventas ---- */
var vMode='board';
document.getElementById('vBoard').addEventListener('click',function(){vMode='board';this.setAttribute('aria-pressed','true');document.getElementById('vList').setAttribute('aria-pressed','false');renderVentas(stats())});
document.getElementById('vList').addEventListener('click',function(){vMode='list';this.setAttribute('aria-pressed','true');document.getElementById('vBoard').setAttribute('aria-pressed','false');renderVentas(stats())});
['vBuscar','vCanal','vResp'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){renderVentas(stats())})});
function fillSel(id,opts,first){var el=document.getElementById(id),cur=el.value;el.innerHTML='<option value="">'+first+'</option>'+opts.map(function(o){return '<option>'+esc(o)+'</option>'}).join('');el.value=cur}
function pname(id){var p=D.productos[id];return p?p.nombre:'(juguete eliminado)'}
function renderVentas(s){
  fillSel('vCanal',CANALES,'Todos los canales');fillSel('vResp',[s.c.socioA,s.c.socioB],'Ambos socios');
  document.getElementById('venSub').textContent=s.V.length?num(s.V.length)+' registros · cobrado '+money(s.cobrado)+' · por cobrar '+money(s.porCobrar):'';
  var body=document.getElementById('venBody');
  if(!s.V.length){body.innerHTML='<div class="empty"><h3>Sin ventas todavía</h3><p class="muted">Cuando un cliente aparte un juguete, regístrenlo aquí como “Apartado”. Al ver el abono, márquenlo “Pagado” y al entregarlo, “Entregado”.</p><button class="btn primary" data-act="nuevaVenta"'+(s.P.length?'':' disabled')+'>+ Registrar venta</button>'+(s.P.length?'':'<p class="small muted">Primero agreguen juguetes al inventario.</p>')+'</div>';return}
  var q=document.getElementById('vBuscar').value.trim().toLowerCase(),fc=document.getElementById('vCanal').value,fr=document.getElementById('vResp').value;
  var V=s.V.filter(function(v){
    if(q&&((v.cliente||'')+' '+pname(v.productoId)+' '+(v.notas||'')).toLowerCase().indexOf(q)<0)return false;
    if(fc&&v.canal!==fc)return false;if(fr&&v.responsable!==fr)return false;return true;
  }).sort(function(a,b){return (b.fecha||'')<(a.fecha||'')?-1:(b.fecha||'')>(a.fecha||'')?1:(b.creado||0)-(a.creado||0)});
  if(vMode==='board'){
    body.innerHTML='<div class="board">'+ESTADOS_V.map(function(e){
      var it=V.filter(function(v){return v.estado===e}),tot=it.reduce(function(a,v){return a+totalVenta(v)},0);
      if(e==='cancelado'&&!it.length)return '';
      var cards=it.slice(0,60).map(function(v){
        var next=e==='apartado'?['pagado','Marcar pagado']:e==='pagado'?['entregado','Marcar entregado']:null;
        return '<div class="sale"><div class="t"><span>'+esc(pname(v.productoId))+(+v.cantidad>1?' ×'+num(+v.cantidad):'')+'</span><b>'+money(totalVenta(v))+'</b></div>'+
          '<div class="m"><span>'+esc(fdate(v.fecha))+'</span>'+(v.cliente?'<span>'+esc(v.cliente)+'</span>':'')+'<span>'+esc(v.canal||'')+'</span>'+(v.responsable?'<span>'+esc(v.responsable)+'</span>':'')+'</div>'+
          '<div class="a">'+(next?'<button class="btn sm primary" data-adv="'+v.id+'" data-to="'+next[0]+'">'+next[1]+'</button>':'')+'<button class="btn sm" data-editv="'+v.id+'">Editar</button></div></div>'}).join('');
      return '<div class="col"><h3>'+EST_V_TXT[e]+' <span>'+it.length+' · '+money(tot)+'</span></h3>'+(cards||'<p class="empty-col">Nada aquí.</p>')+(it.length>60?'<p class="empty-col">Y '+(it.length-60)+' más en la vista de lista.</p>':'')+'</div>'}).join('')+'</div>';
  }else{
    var rows=V.map(function(v){return '<tr><td>'+esc(fdate(v.fecha))+'</td><td class="wrap"><b>'+esc(pname(v.productoId))+'</b>'+(v.notas?'<span class="sub">'+esc(v.notas)+'</span>':'')+'</td><td>'+esc(v.cliente)+'</td><td>'+esc(v.canal)+'</td><td>'+esc(v.responsable)+'</td><td><span class="pill '+v.estado+'">'+EST_V_TXT[v.estado]+'</span></td><td class="r">'+num(+v.cantidad)+'</td><td class="r">'+money2(+v.precio)+'</td><td class="r"><b>'+money(totalVenta(v))+'</b></td><td class="r"><button class="btn sm" data-editv="'+v.id+'">Editar</button></td></tr>'}).join('');
    body.innerHTML=V.length?'<div class="scroll"><table><thead><tr><th>Fecha</th><th>Juguete</th><th>Cliente</th><th>Canal</th><th>Responsable</th><th>Estado</th><th class="r">Cant.</th><th class="r">Precio</th><th class="r">Total</th><th></th></tr></thead><tbody>'+rows+'</tbody></table></div>':'<div class="empty"><p class="muted">Ninguna venta coincide con los filtros.</p></div>';
  }
  // por canal
  var by={};s.V.forEach(function(v){if(!cobrada(v))return;var k=v.canal||'Otro';by[k]=by[k]||[0,0];by[k][0]+=(+v.cantidad||0);by[k][1]+=totalVenta(v)});
  var ks=Object.keys(by).sort(function(a,b){return by[b][1]-by[a][1]});
  if(ks.length)body.insertAdjacentHTML('beforeend','<div class="card"><h3>Qué canal rinde más</h3><div class="scroll" style="border:0"><table><thead><tr><th>Canal</th><th class="r">Piezas cobradas</th><th class="r">Dinero</th><th class="r">Promedio</th></tr></thead><tbody>'+ks.map(function(k){return '<tr><td>'+esc(k)+'</td><td class="r">'+num(by[k][0])+'</td><td class="r">'+money(by[k][1])+'</td><td class="r">'+money(by[k][1]/by[k][0])+'</td></tr>'}).join('')+'</tbody></table></div></div>');
}

/* ---- gastos ---- */
function renderGastos(s){
  var by={};s.G.forEach(function(g){var k=g.tipo||'Otro';by[k]=(by[k]||0)+(+g.monto||0)});
  var top=Object.keys(by).sort(function(a,b){return by[b]-by[a]});
  document.getElementById('gasKpis').innerHTML=kpi('Total de gastos',money(s.gastos),s.G.length+' registros')+
    top.slice(0,3).map(function(k){return kpi(esc(k),money(by[k]),pct(s.gastos?by[k]/s.gastos*100:0)+' del total')}).join('');
  var body=document.getElementById('gasBody');
  if(!s.G.length){body.innerHTML='<div class="empty"><h3>Sin gastos registrados</h3><p class="muted">Anoten bolsas y cajas de empaque, gasolina para entregas, anuncios pagados y comisiones de plataforma.</p><button class="btn primary" data-act="nuevoGasto">+ Registrar gasto</button></div>';return}
  var G=s.G.slice().sort(function(a,b){return (b.fecha||'')<(a.fecha||'')?-1:1});
  body.innerHTML='<div class="scroll"><table><thead><tr><th>Fecha</th><th>Tipo</th><th>Concepto</th><th>Pagó</th><th class="r">Monto</th><th></th></tr></thead><tbody>'+
    G.map(function(g){return '<tr><td>'+esc(fdate(g.fecha))+'</td><td>'+esc(g.tipo)+'</td><td class="wrap">'+esc(g.concepto)+'</td><td>'+esc(g.persona)+'</td><td class="r"><b>'+money2(+g.monto)+'</b></td><td class="r"><button class="btn sm" data-editg="'+g.id+'">Editar</button></td></tr>'}).join('')+
    '</tbody><tfoot><tr><td colspan="4">Total</td><td class="r">'+money2(s.gastos)+'</td><td></td></tr></tfoot></table></div>';
}

/* ---- semana ---- */
function weekStats(w,s){var end=addDays(w,7),n=0,pz=0,din=0;s.V.forEach(function(v){var f=v.fechaPago||v.fecha;if(cobrada(v)&&f>=w&&f<end){n++;pz+=(+v.cantidad||0);din+=totalVenta(v)}});
  var gas=0;s.G.forEach(function(g){if(g.fecha>=w&&g.fecha<end)gas+=(+g.monto||0)});return {n:n,pz:pz,din:din,gas:gas}}
function renderSemana(s){
  var W=list('semanas').sort(function(a,b){return a.semana<b.semana?1:-1});
  var cur=monday(today()),ws=weekStats(cur,s),reg=D.semanas[cur]||{};
  document.getElementById('semKpis').innerHTML=
    kpi('Ventas cerradas esta semana',num(ws.n),num(ws.pz)+' piezas · '+money(ws.din))+
    kpi('Mensajes esta semana',reg.mensajes!=null?num(+reg.mensajes):'—',reg.mensajes!=null&&+reg.mensajes>0?pct(ws.n/(+reg.mensajes)*100)+' se convirtió en venta':'Regístrenlos al cierre de semana')+
    kpi('Porcentaje vendido',pct(s.pctVend),'Del total del pallet')+
    kpi('Dinero recuperado',money(s.rec),'Faltan '+money(s.falta));
  var body=document.getElementById('semBody');
  if(!W.length){body.innerHTML='<div class="empty"><h3>Primera revisión semanal</h3><p class="muted">Cada semana anoten cuántas publicaciones tienen activas, cuántos mensajes recibieron y las horas de cada socio.</p><button class="btn primary" data-act="nuevaSemana">+ Registrar esta semana</button></div>';return}
  var acum=0;var asc=W.slice().reverse().map(function(w){var st=weekStats(w.semana,s);acum+=st.din-st.gas;return [w,st,acum]}).reverse();
  body.innerHTML='<div class="scroll"><table><thead><tr><th>Semana</th><th class="r">Publicadas</th><th class="r">Mensajes</th><th class="r">Ventas</th><th class="r">Piezas</th><th class="r">Cobrado</th><th class="r">Gastos</th><th class="r">Horas '+esc(s.c.socioA)+'</th><th class="r">Horas '+esc(s.c.socioB)+'</th><th>Notas</th><th></th></tr></thead><tbody>'+
    asc.map(function(r){var w=r[0],st=r[1];return '<tr><td>'+esc(fdate(w.semana))+' – '+esc(fdate(addDays(w.semana,6)))+'</td><td class="r">'+num(+w.publicadas||0)+'</td><td class="r">'+num(+w.mensajes||0)+'</td><td class="r">'+num(st.n)+'</td><td class="r">'+num(st.pz)+'</td><td class="r">'+money(st.din)+'</td><td class="r">'+money(st.gas)+'</td><td class="r">'+num(+w.horasA||0)+'</td><td class="r">'+num(+w.horasB||0)+'</td><td class="wrap small">'+esc(w.notas)+'</td><td class="r"><button class="btn sm" data-edits="'+w.id+'">Editar</button></td></tr>'}).join('')+
    '</tbody></table></div>';
}

/* ---- ajustes ---- */
var cfgDirty=false;
['cInv','cA','cB','cRep','cBajo','cMeta','cTc'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){cfgDirty=true})});
function renderAjustes(s){
  if(cfgDirty)return;var c=s.c;
  document.getElementById('cInv').value=c.inversion;document.getElementById('cA').value=c.socioA;document.getElementById('cB').value=c.socioB;
  document.getElementById('cRep').value=c.repartoA;document.getElementById('cBajo').value=c.stockBajo;document.getElementById('cMeta').value=c.metaMensual||'';document.getElementById('cTc').value=c.tipoCambioUsd||'';
}
document.getElementById('cfgForm').addEventListener('submit',function(e){e.preventDefault();
  var c={inversion:Math.max(0,+document.getElementById('cInv').value||0),socioA:document.getElementById('cA').value.trim()||'Persona A',socioB:document.getElementById('cB').value.trim()||'Persona B',
    repartoA:Math.min(100,Math.max(0,+document.getElementById('cRep').value||0)),stockBajo:Math.max(0,+document.getElementById('cBajo').value||0),metaMensual:Math.max(0,+document.getElementById('cMeta').value||0),tipoCambioUsd:Math.max(0,+document.getElementById('cTc').value||0)};
  put('config','general',c).then(function(){cfgDirty=false;toast('Ajustes guardados')},function(){});
});

/* ================= modales ================= */
var modal=document.getElementById('modal'),panel=document.getElementById('mPanel'),lastFocus=null;
function openModal(html,onMount){lastFocus=document.activeElement;panel.innerHTML=html;modal.hidden=false;
  panel.querySelector('[data-close]').addEventListener('click',closeModal);
  if(onMount)onMount(panel);var f=panel.querySelector('input,select,textarea');if(f)f.focus()}
function closeModal(){modal.hidden=true;panel.innerHTML='';if(lastFocus&&lastFocus.focus)lastFocus.focus()}
modal.addEventListener('click',function(e){if(e.target===modal)closeModal()});
document.addEventListener('keydown',function(e){if(e.key==='Escape'&&!modal.hidden)closeModal()});
function head(t){return '<div class="ph"><h2 id="mTitle" style="font-size:1.35rem">'+t+'</h2><button class="x" type="button" data-close aria-label="Cerrar">×</button></div>'}
function fld(id,label,input,hint,full){return '<div class="f'+(full?' full':'')+'"><label for="'+id+'">'+label+'</label>'+input+(hint?'<span class="hint">'+hint+'</span>':'')+'</div>'}
function opt(list,cur,txt){return list.map(function(v){return '<option value="'+esc(v)+'"'+(v===cur?' selected':'')+'>'+esc(txt?txt[v]:v)+'</option>'}).join('')}
function footer(isEdit){return '<div class="pf"><div>'+(isEdit?'<button class="btn danger" type="button" id="mDel">Eliminar</button>':'')+'</div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div>'}
function wireForm(p,onSave,onDelete){
  var form=p.querySelector('form');form.addEventListener('submit',function(e){e.preventDefault();onSave(form)});
  var c2=p.querySelector('[data-close2]');if(c2)c2.addEventListener('click',closeModal);
  var d=p.querySelector('#mDel');if(d)d.addEventListener('click',function(){if(!d.classList.contains('armed')){d.classList.add('armed');d.textContent='Confirmar: eliminar';return}onDelete()});
}
function val(f,n){return f.elements[n].value.trim()}

function nextCode(){var mx=0;list('productos').forEach(function(p){var n=parseInt(p.codigo,10);if(n>mx)mx=n});return String(mx+1).padStart(3,'0')}
function productoForm(id,pre){
  pre=pre||{};
  var p=id?D.productos[id]:{codigo:nextCode(),estado:'nuevo',clase:'B',codigoBarras:pre.codigoBarras||''};var cats={};list('productos').forEach(function(x){if(x.categoria)cats[x.categoria]=1});
  var vend=id?vendidasDe(id):0;
  openModal(head(id?'Editar juguete':'Nuevo juguete')+'<form class="stack" style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('pCod','Código','<input class="inp mono" id="pCod" name="codigo" required maxlength="20" value="'+esc(p.codigo)+'">')+
    fld('pCla','Clase','<select class="sel" id="pCla" name="clase">'+opt(['A','B','C'],p.clase,{A:'A · Muy buscado o caro',B:'B · Demanda normal',C:'C · Barato, para combos'})+'</select>')+
    fld('pBar','Código de barras','<div class="bc"><input class="inp mono" id="pBar" name="codigoBarras" maxlength="20" inputmode="numeric" autocomplete="off" value="'+esc(p.codigoBarras)+'" placeholder="Escaneen o escriban el código"><button class="btn" type="button" id="pBarBtn">Buscar</button></div>','Con el lector: escaneen aquí y se busca solo. A mano: escriban el código y presionen Enter.',true)+
    '<div class="full" id="pBarRes"></div>'+
    fld('pNom','Juguete','<input class="inp" id="pNom" name="nombre" required maxlength="120" value="'+esc(p.nombre)+'" placeholder="Marca y nombre del juguete">',null,true)+
    fld('pCat','Categoría','<input class="inp" id="pCat" name="categoria" list="catList" maxlength="60" value="'+esc(p.categoria)+'" placeholder="Muñecas, autos, didácticos…"><datalist id="catList">'+Object.keys(cats).map(function(c){return '<option value="'+esc(c)+'">'}).join('')+'</datalist>')+
    fld('pEst','Estado','<select class="sel" id="pEst" name="estado">'+opt(['nuevo','danado','incompleto'],p.estado,EST_P_TXT)+'</select>')+
    fld('pCan','Cantidad','<input class="inp num" id="pCan" name="cantidad" type="number" min="'+vend+'" step="1" required value="'+esc(p.cantidad!=null?p.cantidad:1)+'">',vend?'Ya hay '+vend+' vendidas o apartadas.':'Piezas de este juguete en el pallet.')+
    fld('pLis','Precio de lista','<input class="inp num" id="pLis" name="precioLista" type="number" min="0" step="1" value="'+esc(p.precioLista||'')+'">','Precio en tienda. Es su techo.')+
    fld('pObj','Precio objetivo','<input class="inp num" id="pObj" name="precioObjetivo" type="number" min="0" step="1" value="'+esc(p.precioObjetivo||'')+'">','Lo que esperan cobrar. Publiquen un poco arriba para negociar.')+
    fld('pMin','Precio mínimo','<input class="inp num" id="pMin" name="precioMinimo" type="number" min="0" step="1" value="'+esc(p.precioMinimo||'')+'">','Opcional. No bajar de aquí al regatear.')+
    fld('pNot','Notas','<textarea class="inp" id="pNot" name="notas" maxlength="500" placeholder="Daños, piezas faltantes, dónde está guardado…">'+esc(p.notas)+'</textarea>',null,true)+
    '</div><div id="pWarn"></div>'+footer(!!id)+'</form>',function(el){
      var cost=stats().costoPz,w=el.querySelector('#pWarn');
      function chk(){var o=+el.querySelector('#pObj').value||0;w.innerHTML=(o>0&&isFinite(cost)&&o<cost)?'<p class="warnline">El precio objetivo queda debajo del costo promedio por pieza ('+money(cost)+'). Está bien para clase C en combos.</p>':''}
      el.querySelector('#pObj').addEventListener('input',chk);chk();
      wireBarras(el,id,chk,pre.resultado);
      wireForm(el,function(f){
        var o={codigo:val(f,'codigo'),codigoBarras:val(f,'codigoBarras'),nombre:val(f,'nombre'),categoria:val(f,'categoria'),clase:f.elements.clase.value,estado:f.elements.estado.value,
          cantidad:Math.max(0,Math.round(+f.elements.cantidad.value||0)),precioLista:+f.elements.precioLista.value||0,precioObjetivo:+f.elements.precioObjetivo.value||0,
          precioMinimo:+f.elements.precioMinimo.value||0,notas:val(f,'notas'),creado:(p.creado||Date.now())};
        var dup=list('productos').some(function(x){return x.id!==id&&String(x.codigo).toLowerCase()===o.codigo.toLowerCase()});
        if(dup){w.innerHTML='<p class="warnline bad">Ya existe un juguete con el código '+esc(o.codigo)+'. Usen otro código.</p>';return}
        if(o.cantidad<vend){w.innerHTML='<p class="warnline bad">La cantidad no puede ser menor que las '+vend+' piezas ya vendidas o apartadas.</p>';return}
        put('productos',id||uid(),o).then(function(){closeModal();toast(id?'Juguete actualizado':'Juguete agregado')},function(){});
      },function(){
        var nv=list('ventas').filter(function(v){return v.productoId===id}).length;
        if(nv){w.innerHTML='<p class="warnline bad">Este juguete tiene '+nv+' venta'+(nv>1?'s':'')+' registrada'+(nv>1?'s':'')+'. Elimínenlas primero o pongan la cantidad en lo vendido.</p>';return}
        del('productos',id).then(function(){closeModal();toast('Juguete eliminado')},function(){});
      });
    });
}

/* ---- código de barras ---- */
function buscarBarras(code){return api('GET','codigo-barras/'+encodeURIComponent(code))}
function fdt(iso){try{return new Date(iso).toLocaleString('es-MX',{day:'numeric',month:'short',year:'numeric',hour:'2-digit',minute:'2-digit'})}catch(e){return ''}}
function wireBarras(el,id,chk,inicial){
  var inp=el.querySelector('#pBar'),btn=el.querySelector('#pBarBtn'),res=el.querySelector('#pBarRes'),ultimo=null;
  function campo(n){return el.querySelector('[name="'+n+'"]')}
  function aplicar(datos,forzar){
    var llenos=[];
    Object.keys(datos).forEach(function(k){var v=datos[k];if(v==null||v===''||v===0)return;var c=campo(k);if(!c)return;
      if(forzar||!String(c.value).trim()||(+c.value===0&&c.type==='number')){c.value=v;llenos.push(c.closest('.f').querySelector('label').textContent)}});
    chk();return llenos;
  }
  function pinta(r){
    ultimo=r;
    if(r.origen==='inventario'){
      var p=r.producto;
      res.innerHTML=p.id===id?'<p class="small muted">Este código de barras ya pertenece a este juguete.</p>'
        :'<p class="warnline bad">Ya está registrado en el juguete <b>'+esc(p.codigo+' · '+p.nombre)+'</b>. No lo den de alta otra vez: aumenten la cantidad de ese juguete. <button class="lnk" type="button" data-editp="'+esc(p.id)+'">Abrir ese juguete</button></p>';
      return;
    }
    if(r.origen==='ninguno'){res.innerHTML='<p class="warnline">'+esc(r.aviso)+'</p>';return}
    var e=r.externo,sg=r.sugerencia||{};
    var nombre=e.marca&&e.titulo.toLowerCase().indexOf(e.marca.toLowerCase())<0?e.marca+' '+e.titulo:e.titulo;
    var datos={nombre:nombre,categoria:e.categoria,precioLista:sg.precioLista,precioObjetivo:sg.precioObjetivo,precioMinimo:sg.precioMinimo};
    var llenos=aplicar(datos,false);
    var rango=(e.precioBajo!=null||e.precioAlto!=null)?'Precios registrados en tiendas: '+(e.moneda==='USD'?'US$':e.moneda+' ')+[e.precioBajo,e.precioAlto].filter(function(x){return x!=null}).map(function(x){return (+x).toFixed(2)}).join(' – ')+(e.ofertas?' · '+e.ofertas+' oferta'+(e.ofertas>1?'s':''):''):'Sin precios registrados.';
    res.innerHTML='<div class="bcres">'+(e.imagen?'<img class="bcimg" src="'+esc(e.imagen)+'" alt="" referrerpolicy="no-referrer" onerror="this.remove()">':'')+
      '<div class="bcinfo"><p class="small"><span class="pill nuevo">UPCitemdb</span> <b>'+esc(e.titulo)+'</b></p>'+
      '<p class="small muted">'+[e.marca,e.categoria].filter(Boolean).map(esc).join(' · ')+'</p>'+
      '<p class="small muted">'+esc(rango)+'</p>'+
      '<ul class="small sug">'+(sg.explicacion||[]).map(function(x){return '<li>'+esc(x)+'</li>'}).join('')+'</ul>'+
      '<p class="small muted">'+(llenos.length?'Se llenaron: '+esc(llenos.join(', '))+'. ':'No se cambió ningún campo porque ya tenían datos. ')+
      'Son sugerencias: revísenlas antes de guardar. Consultado '+esc(fdt(r.consultadoUtc))+(r.desdeCache?' (guardado, sin gastar consulta)':'')+'.</p>'+
      '<div class="row"><button class="btn sm" type="button" id="pBarForzar">Reemplazar todos los campos con estos datos</button></div></div></div>';
    res.querySelector('#pBarForzar').addEventListener('click',function(){var l=aplicar(datos,true);toast(l.length?'Campos reemplazados':'No hay datos para reemplazar')});
  }
  function buscar(){
    var code=inp.value.trim();if(!code){inp.focus();return}
    btn.disabled=true;res.innerHTML='<p class="small muted">Buscando '+esc(code)+'…</p>';
    buscarBarras(code).then(pinta,function(e){res.innerHTML='<p class="warnline bad">'+esc((e&&e.mensaje)||'No se pudo buscar.')+'</p>'}).then(function(){btn.disabled=false});
  }
  btn.addEventListener('click',buscar);
  inp.addEventListener('keydown',function(ev){if(ev.key==='Enter'){ev.preventDefault();buscar()}});
  if(inicial)pinta(inicial);
}
function escanearForm(){
  openModal(head('Escanear código de barras')+'<form style="display:flex;flex-direction:column;gap:12px">'+
    '<p class="small muted">Escaneen la caja con el lector. Si el juguete ya está en el inventario se abre para aumentar su cantidad; si no, se busca en UPCitemdb y se abre el alta con los datos encontrados.</p>'+
    '<input class="inp mono" id="scCode" inputmode="numeric" autocomplete="off" maxlength="20" placeholder="Código de barras" style="font-size:1.2rem">'+
    '<div id="scRes"></div><div class="pf"><span></span><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit" id="scGo">Buscar</button></div></div></form>',function(el){
      var inp=el.querySelector('#scCode'),r=el.querySelector('#scRes'),go=el.querySelector('#scGo');
      wireForm(el,function(){
        var code=inp.value.trim();if(!code)return;go.disabled=true;r.innerHTML='<p class="small muted">Buscando…</p>';
        buscarBarras(code).then(function(res){
          if(res.origen==='inventario'){productoForm(res.producto.id);toast('Ya está en el inventario: aumenten la cantidad si encontraron más piezas');}
          else productoForm(null,{codigoBarras:res.codigo,resultado:res});
        },function(e){go.disabled=false;r.innerHTML='<p class="warnline bad">'+esc((e&&e.mensaje)||'No se pudo buscar.')+'</p><p class="small"><button class="lnk" type="button" id="scManual">Dar de alta a mano con este código</button></p>';
          r.querySelector('#scManual').addEventListener('click',function(){productoForm(null,{codigoBarras:code})});inp.select()});
      });
    });
}

function ventaForm(id,pid){
  var s=stats(),v=id?D.ventas[id]:{productoId:pid||'',cantidad:1,fecha:today(),estado:'apartado',canal:'Facebook Marketplace',responsable:s.c.socioB};
  var P=s.P.slice().sort(function(a,b){return String(a.nombre).localeCompare(String(b.nombre),'es')});
  var opts=P.map(function(p){var d=(+p.cantidad||0)-vendidasDe(p.id,id);return '<option value="'+p.id+'"'+(p.id===v.productoId?' selected':'')+(d<=0&&p.id!==v.productoId?' disabled':'')+'>'+esc(p.codigo+' · '+p.nombre)+' ('+Math.max(0,d)+' disp.)</option>'}).join('');
  var first=!v.productoId&&P.length?'<option value="" selected disabled>Elige un juguete</option>':'';
  openModal(head(id?'Editar venta':'Nueva venta')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('vPro','Juguete','<select class="sel" id="vPro" name="productoId" required>'+first+opts+'</select>',null,true)+
    fld('vCan2','Cantidad','<input class="inp num" id="vCan2" name="cantidad" type="number" min="1" step="1" required value="'+esc(v.cantidad)+'">')+
    fld('vPre','Precio por pieza','<input class="inp num" id="vPre" name="precio" type="number" min="0" step="1" required value="'+esc(v.precio!=null?v.precio:'')+'">',null)+
    fld('vEst','Estado','<select class="sel" id="vEst" name="estado">'+opt(ESTADOS_V,v.estado,EST_V_TXT)+'</select>','Pagado solo con el abono visible en su banca.')+
    fld('vFec','Fecha','<input class="inp" id="vFec" name="fecha" type="date" required value="'+esc(v.fecha)+'">')+
    fld('vCanal2','Canal','<select class="sel" id="vCanal2" name="canal">'+opt(CANALES,v.canal)+'</select>')+
    fld('vRes','Responsable','<select class="sel" id="vRes" name="responsable">'+opt([s.c.socioA,s.c.socioB],v.responsable)+'</select>')+
    fld('vCli','Cliente','<input class="inp" id="vCli" name="cliente" maxlength="80" value="'+esc(v.cliente)+'" placeholder="Nombre o perfil">',null,true)+
    fld('vNot','Notas','<textarea class="inp" id="vNot" name="notas" maxlength="400" placeholder="Punto y hora de entrega, combo, descuento…">'+esc(v.notas)+'</textarea>',null,true)+
    '</div><div id="vWarn"></div>'+footer(!!id)+'</form>',function(el){
      var w=el.querySelector('#vWarn'),sp=el.querySelector('#vPro'),pr=el.querySelector('#vPre'),cn=el.querySelector('#vCan2');
      var touched=v.precio!=null;pr.addEventListener('input',function(){touched=true;chk()});
      function chk(){var p=D.productos[sp.value];var out='';
        if(p){if(!touched&&+p.precioObjetivo>0)pr.value=p.precioObjetivo;
          var d=(+p.cantidad||0)-vendidasDe(p.id,id),q=+cn.value||0,pp=+pr.value||0;
          out+='<p class="small muted">Disponibles: '+Math.max(0,d)+' · Objetivo '+money(+p.precioObjetivo)+(+p.precioMinimo>0?' · Mínimo '+money(+p.precioMinimo):'')+' · Costo promedio '+money(s.costoPz)+' · Total: <b>'+money(q*pp)+'</b></p>';
          if(q>d)out+='<p class="warnline bad">Solo hay '+Math.max(0,d)+' pieza'+(d===1?'':'s')+' disponible'+(d===1?'':'s')+'.</p>';
          else if(+p.precioMinimo>0&&pp<+p.precioMinimo)out+='<p class="warnline">Precio debajo del mínimo que fijaron para este juguete.</p>';
          else if(isFinite(s.costoPz)&&pp>0&&pp<s.costoPz)out+='<p class="warnline">Precio debajo del costo promedio por pieza.</p>';}
        w.innerHTML=out}
      sp.addEventListener('change',function(){if(!id)touched=false;chk()});cn.addEventListener('input',chk);chk();
      wireForm(el,function(f){
        var p=D.productos[f.elements.productoId.value];if(!p){w.innerHTML='<p class="warnline bad">Elige un juguete.</p>';return}
        var q=Math.max(1,Math.round(+f.elements.cantidad.value||1)),d=(+p.cantidad||0)-vendidasDe(p.id,id);
        var est=f.elements.estado.value;
        if(est!=='cancelado'&&q>d){chk();return}
        var o={productoId:f.elements.productoId.value,cantidad:q,precio:+f.elements.precio.value||0,estado:est,fecha:f.elements.fecha.value||today(),
          canal:f.elements.canal.value,responsable:f.elements.responsable.value,cliente:val(f,'cliente'),notas:val(f,'notas'),creado:v.creado||Date.now()};
        o.fechaPago=!cobrada(o)?'':(id&&cobrada(v)&&v.fechaPago)?v.fechaPago:(id?today():o.fecha);
        put('ventas',id||uid(),o).then(function(){closeModal();toast(id?'Venta actualizada':'Venta registrada')},function(){});
      },function(){del('ventas',id).then(function(){closeModal();toast('Venta eliminada')},function(){})});
    });
}
function advance(id,to){if(!D.ventas[id])return;
  api('PATCH','ventas/'+id+'/estado',{estado:to}).then(cargar).then(function(){toast(to==='pagado'?'Marcada como pagada':'Marcada como entregada')}).catch(function(e){toast((e&&e.mensaje)||'No se guardó')})}

function gastoForm(id){
  var c=cfg(),g=id?D.gastos[id]:{fecha:today(),tipo:'Empaque',persona:c.socioA};
  openModal(head(id?'Editar gasto':'Nuevo gasto')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('gTip','Tipo','<select class="sel" id="gTip" name="tipo">'+opt(TIPOS_G,g.tipo)+'</select>')+
    fld('gMon','Monto (MXN)','<input class="inp num" id="gMon" name="monto" type="number" min="0" step="0.01" required value="'+esc(g.monto!=null?g.monto:'')+'">')+
    fld('gFec','Fecha','<input class="inp" id="gFec" name="fecha" type="date" required value="'+esc(g.fecha)+'">')+
    fld('gPer','Pagó','<select class="sel" id="gPer" name="persona">'+opt([c.socioA,c.socioB,'Fondo común'],g.persona)+'</select>')+
    fld('gCon','Concepto','<input class="inp" id="gCon" name="concepto" maxlength="120" value="'+esc(g.concepto)+'" placeholder="Bolsas, gasolina entrega Centro, anuncio…">',null,true)+
    '</div>'+footer(!!id)+'</form>',function(el){
      wireForm(el,function(f){var o={tipo:f.elements.tipo.value,monto:Math.max(0,+f.elements.monto.value||0),fecha:f.elements.fecha.value||today(),persona:f.elements.persona.value,concepto:val(f,'concepto'),creado:g.creado||Date.now()};
        put('gastos',id||uid(),o).then(function(){closeModal();toast(id?'Gasto actualizado':'Gasto registrado')},function(){})},
      function(){del('gastos',id).then(function(){closeModal();toast('Gasto eliminado')},function(){})});
    });
}

function semanaForm(id){
  var c=cfg(),wk=id||monday(today()),w=D.semanas[wk]||{semana:wk};
  openModal(head('Indicadores de la semana')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('sSem','Semana que inicia el lunes','<input class="inp" id="sSem" name="semana" type="date" required value="'+esc(wk)+'"'+(id?' disabled':'')+'>',id?null:'Cualquier fecha se ajusta a su lunes.',true)+
    fld('sPub','Piezas publicadas','<input class="inp num" id="sPub" name="publicadas" type="number" min="0" step="1" value="'+esc(w.publicadas!=null?w.publicadas:'')+'">','Publicaciones activas.')+
    fld('sMen','Mensajes recibidos','<input class="inp num" id="sMen" name="mensajes" type="number" min="0" step="1" value="'+esc(w.mensajes!=null?w.mensajes:'')+'">','Marketplace y WhatsApp.')+
    fld('sHA','Horas de '+esc(c.socioA),'<input class="inp num" id="sHA" name="horasA" type="number" min="0" step="0.5" value="'+esc(w.horasA!=null?w.horasA:'')+'">')+
    fld('sHB','Horas de '+esc(c.socioB),'<input class="inp num" id="sHB" name="horasB" type="number" min="0" step="0.5" value="'+esc(w.horasB!=null?w.horasB:'')+'">')+
    fld('sNot','Acuerdos de la reunión','<textarea class="inp" id="sNot" name="notas" maxlength="600" placeholder="Precios a ajustar, qué publicar, quién hace qué…">'+esc(w.notas)+'</textarea>',null,true)+
    '</div>'+footer(!!id)+'</form>',function(el){
      wireForm(el,function(f){var k=id||monday(f.elements.semana.value||today());
        var o={semana:k,publicadas:+f.elements.publicadas.value||0,mensajes:+f.elements.mensajes.value||0,horasA:+f.elements.horasA.value||0,horasB:+f.elements.horasB.value||0,notas:val(f,'notas')};
        put('semanas',k,o).then(function(){closeModal();toast('Semana guardada')},function(){})},
      function(){del('semanas',id).then(function(){closeModal();toast('Semana eliminada')},function(){})});
    });
}

/* ---- importar ---- */
function parseRows(txt){
  var lines=txt.replace(/\r/g,'').split('\n').filter(function(l){return l.trim()});if(!lines.length)return [];
  var sep=lines[0].indexOf('\t')>=0?'\t':(lines[0].split(';').length>lines[0].split(',').length?';':',');
  var rows=lines.map(function(l){return l.split(sep).map(function(c){return c.trim().replace(/^"|"$/g,'')})});
  if(/c[oó]digo|juguete|nombre/i.test(rows[0].join(' ')))rows.shift();
  function n(x){return +(String(x||'').replace(/[$\s]/g,'').replace(/,(?=\d{3}\b)/g,'').replace(',','.'))||0}
  function est(x){x=String(x||'').toLowerCase();return x.indexOf('da')===0?'danado':x.indexOf('inc')===0?'incompleto':'nuevo'}
  return rows.filter(function(r){return r[1]}).map(function(r){var cl=String(r[7]||'').trim().toUpperCase();
    return {codigo:r[0]||'',nombre:r[1],categoria:r[2]||'',cantidad:Math.max(0,Math.round(n(r[3]))),estado:est(r[4]),precioLista:n(r[5]),precioObjetivo:n(r[6]),clase:['A','B','C'].indexOf(cl)>=0?cl:'B',codigoBarras:String(r[8]||'').replace(/[^0-9A-Za-z]/g,''),precioMinimo:0,notas:''}});
}
function importForm(){
  openModal(head('Importar desde Excel')+'<form style="display:flex;flex-direction:column;gap:12px">'+
    '<p class="small muted">Copien las filas de su hoja y péguenlas abajo. Columnas en este orden: <b>Código, Juguete, Categoría, Cantidad, Estado, Precio de lista, Precio objetivo, Clase</b> y, opcional, <b>Código de barras</b>. La fila de títulos se ignora.</p>'+
    '<textarea class="inp mono" id="impTxt" style="min-height:180px;font-size:.82rem" placeholder="001&#9;Muñeca Bebé Llorón&#9;Muñecas&#9;12&#9;Nuevo&#9;899&#9;600&#9;A"></textarea>'+
    '<div id="impPrev"></div><div class="pf"><span></span><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit" id="impGo" disabled>Importar</button></div></div></form>',function(el){
      var ta=el.querySelector('#impTxt'),pv=el.querySelector('#impPrev'),go=el.querySelector('#impGo'),rows=[];
      ta.addEventListener('input',function(){rows=parseRows(ta.value);var codes={};list('productos').forEach(function(p){codes[String(p.codigo).toLowerCase()]=p.id});
        var upd=rows.filter(function(r){return r.codigo&&codes[r.codigo.toLowerCase()]}).length;
        go.disabled=!rows.length;go.textContent=rows.length?'Importar '+rows.length+' juguete'+(rows.length>1?'s':''):'Importar';
        pv.innerHTML=rows.length?'<p class="small">'+rows.length+' filas leídas · '+num(rows.reduce(function(a,r){return a+r.cantidad},0))+' piezas'+(upd?' · <b>'+upd+' códigos ya existen y se actualizarán</b>':'')+'.</p>':(ta.value.trim()?'<p class="warnline">No se reconocieron filas. Revisen que la segunda columna tenga el nombre del juguete.</p>':'')});
      wireForm(el,function(){
        if(!rows.length)return;go.disabled=true;
        go.textContent='Importando…';
        api('POST','productos/importar',rows).then(function(r){return cargar().then(function(){closeModal();toast(r.nuevos+' nuevos · '+r.actualizados+' actualizados')})})
          .catch(function(e){go.disabled=false;go.textContent='Reintentar';toast((e&&e.mensaje)||'No se pudo importar')});
      });
    });
}

/* ---- exportar ---- */
function csv(rows){return '﻿'+rows.map(function(r){return r.map(function(c){c=c==null?'':String(c);return /[",\n;]/.test(c)?'"'+c.replace(/"/g,'""')+'"':c}).join(',')}).join('\r\n')}
function exportar(k){
  var s=stats(),rows;
  if(k==='inventario')rows=[['Código','Juguete','Categoría','Cantidad','Estado','Precio de lista','Precio objetivo','Clase','Código de barras','Precio mínimo','Vendidas','Disponibles','Notas']].concat(s.P.map(function(p){var v=s.sold[p.id]||0;return [p.codigo,p.nombre,p.categoria,p.cantidad,EST_P_TXT[p.estado]||'Nuevo',p.precioLista,p.precioObjetivo,p.clase,p.codigoBarras?'\u2060'+p.codigoBarras:'',p.precioMinimo,v,Math.max(0,(+p.cantidad||0)-v),p.notas]}));
  else if(k==='ventas')rows=[['Fecha','Fecha de pago','Código','Juguete','Cantidad','Precio','Total','Estado','Canal','Responsable','Cliente','Notas']].concat(s.V.map(function(v){var p=D.productos[v.productoId]||{};return [v.fecha,v.fechaPago,p.codigo,p.nombre,v.cantidad,v.precio,totalVenta(v),EST_V_TXT[v.estado],v.canal,v.responsable,v.cliente,v.notas]}));
  else if(k==='gastos')rows=[['Fecha','Tipo','Concepto','Pagó','Monto']].concat(s.G.map(function(g){return [g.fecha,g.tipo,g.concepto,g.persona,g.monto]}));
  else rows=[['Semana','Publicadas','Mensajes','Horas '+s.c.socioA,'Horas '+s.c.socioB,'Notas']].concat(list('semanas').map(function(w){return [w.semana,w.publicadas,w.mensajes,w.horasA,w.horasB,w.notas]}));
  var name='pallet-'+k+'-'+today()+'.csv',data=csv(rows);
  try{var a=document.createElement('a');a.href=URL.createObjectURL(new Blob([data],{type:'text/csv'}));a.download=name;document.body.appendChild(a);a.click();a.remove()}catch(e){toast('La descarga no está disponible aquí')}
}

/* ---- delegación de clics ---- */
document.addEventListener('click',function(e){
  var t=e.target.closest('button');if(!t)return;
  var act=t.dataset.act;
  if(act==='nuevoProducto')productoForm();
  else if(act==='nuevaVenta'){if(!list('productos').length){toast('Primero agreguen juguetes al inventario');go('inventario')}else ventaForm()}
  else if(act==='nuevoGasto')gastoForm();
  else if(act==='nuevaSemana')semanaForm(D.semanas[monday(today())]?monday(today()):null);
  else if(act==='importar')importForm();
  else if(act==='escanear')escanearForm();
  else if(t.dataset.go)go(t.dataset.go);
  else if(t.dataset.editp)productoForm(t.dataset.editp);
  else if(t.dataset.sell)ventaForm(null,t.dataset.sell);
  else if(t.dataset.editv)ventaForm(t.dataset.editv);
  else if(t.dataset.adv)advance(t.dataset.adv,t.dataset.to);
  else if(t.dataset.editg)gastoForm(t.dataset.editg);
  else if(t.dataset.edits)semanaForm(t.dataset.edits);
  else if(t.dataset.exp)exportar(t.dataset.exp);
});

var tt=null;function toast(m){var el=document.getElementById('toast');el.textContent=m;el.hidden=false;clearTimeout(tt);tt=setTimeout(function(){el.hidden=true},2600)}

/* ================= arranque ================= */
var start=(location.hash||'').slice(1);if(VALID.indexOf(start)<0){try{start=localStorage.getItem(LKEY+'-tab')||''}catch(e){start=''}}
go(start||'resumen');setStatus();
cargar().catch(function(e){toast(e.mensaje)});vigilar();
})();
