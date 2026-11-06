(function(){
'use strict';
/* ================= datos ================= */
var COLS=['productos','ventas','gastos','semanas','config'];
var D={productos:{},ventas:{},gastos:{},semanas:{},config:{}};
var CFG_DEF={inversion:49000,stockBajo:1,metaMensual:0,tipoCambioUsd:0,descuentoSugerido:35};
var IMG={}, TC={hoy:'',error:null};
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
    IMG={};(e.imagenes||[]).forEach(function(i){(IMG[i.productoId]=IMG[i.productoId]||[]).push(i)});
    TC=e.tipoCambio||{hoy:today(),error:null};renderTc();
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

// Sincronización entre pestañas o dispositivos: se consulta la versión de los datos cada pocos segundos.
function vigilar(){
  setInterval(function(){
    if(document.hidden)return;
    api('GET','version').then(function(r){
      if(mode!=='ok'){mode='ok';setStatus()}
      if(r.version!==version)cargar().catch(function(){});
    },function(){if(mode!=='error'){mode='error';setStatus()}});
  },4000);
}
/* ---- tipo de cambio: leyenda fija en la barra superior ---- */
var tcFmt=new Intl.NumberFormat('es-MX',{minimumFractionDigits:2,maximumFractionDigits:4});
function renderTc(){
  var c=cfg(),b=document.getElementById('tcBtn'),v=+c.tipoCambioUsd||0,hoy=TC.hoy||today();
  if(!b)return;
  var fuente=c.tipoCambioFuente==='DOF'?'DOF'+(c.tipoCambioPublicado?' publicado '+fdate(c.tipoCambioPublicado):''):(c.tipoCambioFuente==='Manual'?'capturado a mano':'');
  if(v>0&&c.tipoCambioFecha===hoy){
    b.className='tcbtn';
    b.innerHTML='Tipo de cambio definido para el día de hoy a: <b>$'+tcFmt.format(v)+'</b><small> · '+esc(fuente)+' · Editar</small>';
  }else if(v>0){
    b.className='tcbtn warn';
    b.innerHTML='Tipo de cambio: <b>$'+tcFmt.format(v)+'</b><small> · definido el '+esc(fdate(c.tipoCambioFecha||''))+'; no se pudo actualizar hoy · Editar</small>';
  }else{
    b.className='tcbtn warn';
    b.innerHTML='Tipo de cambio sin definir<small> · '+(TC.error?'no se pudo consultar el DOF · ':'')+'Definir</small>';
  }
}
function tcForm(){
  var c=cfg();
  openModal(head('Tipo de cambio del día')+'<form style="display:flex;flex-direction:column;gap:12px">'+
    '<p class="small muted">Se consulta automáticamente en <b>dolardof.com</b> (valor oficial publicado en el Diario Oficial de la Federación) cuando aún no hay valor para hoy. Si lo capturas a mano, se usa tu valor durante el día de hoy; mañana se vuelve a consultar el DOF.</p>'+
    (TC.error?'<p class="warnline">'+esc(TC.error)+'</p>':'')+
    fld('tcVal','Pesos por dólar','<input class="inp num" id="tcVal" type="number" min="0.0001" max="1000" step="0.0001" required value="'+esc(c.tipoCambioUsd||'')+'">',
      c.tipoCambioFuente?('Valor actual: '+(c.tipoCambioFuente==='DOF'?'DOF publicado '+fdate(c.tipoCambioPublicado||''):'capturado a mano')+', definido para '+fdate(c.tipoCambioFecha||'')+'.'):null)+
    '<div id="tcMsg"></div><div class="pf"><button class="btn" type="button" id="tcDof">Consultar el DOF ahora</button><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar valor</button></div></div></form>',function(el){
      var msg=el.querySelector('#tcMsg');
      wireForm(el,function(){
        var v=+el.querySelector('#tcVal').value;
        if(!(v>0)){msg.innerHTML='<p class="warnline bad">Captura un valor mayor a cero.</p>';return}
        api('PUT','tipo-cambio',{valor:v}).then(cargar).then(function(){closeModal();toast('Tipo de cambio guardado para hoy')},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'});
      });
      el.querySelector('#tcDof').addEventListener('click',function(){
        var b=this;b.disabled=true;b.textContent='Consultando…';
        api('POST','tipo-cambio/consultar').then(cargar).then(function(){closeModal();toast('Tipo de cambio del DOF: $'+tcFmt.format(cfg().tipoCambioUsd))},
          function(e){b.disabled=false;b.textContent='Consultar el DOF ahora';msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'});
      });
    });
}

function setStatus(){
  var el=document.getElementById('status'),t,cl='status';
  if(mode==='cargando'){t='Conectando…'}
  else if(mode==='error'){t='Sin conexión con el servidor';cl+=' warn'}
  else{t='Conectado · red local';cl+=' ok'}
  el.textContent=t;el.className=cl;
  var mt=document.getElementById('modoTxt');
  mt.innerHTML='Los datos se guardan en una base SQLite en la computadora donde corre la aplicación. Si abres la aplicación en otra pestaña o en el celular (misma red Wi‑Fi), verás los mismos registros; la pantalla se actualiza sola cuando guardas algo en otro lado. Cada día al abrir la aplicación se crea una copia automática en la carpeta <span class="mono">datos/respaldos</span>.';
}

/* ================= cálculos ================= */
var mx=new Intl.NumberFormat('es-MX',{style:'currency',currency:'MXN',maximumFractionDigits:0});
var nf4=new Intl.NumberFormat('es-MX',{minimumFractionDigits:2,maximumFractionDigits:4});
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
  var valorMx=0;
  P.forEach(function(p){var q=+p.cantidad||0;piezas+=q;var d=Math.max(0,q-(sold[p.id]||0));disp+=d;valorObj+=d*(+p.precioObjetivo||0);if(+p.precioMexico>0)valorMx+=q*(+p.precioMexico)});
  var cobrado=0,pzCobradas=0,porCobrar=0,pzApartadas=0,nAp=0,nPag=0;
  V.forEach(function(v){var t=totalVenta(v);
    if(cobrada(v)){cobrado+=t;pzCobradas+=(+v.cantidad||0)}
    else if(v.estado==='apartado'){porCobrar+=t;pzApartadas+=(+v.cantidad||0);nAp++}
    if(v.estado==='pagado')nPag++});
  var gastos=G.reduce(function(s,g){return s+(+g.monto||0)},0);
  var rec=cobrado-gastos, inv=+c.inversion||0;
  var horas=list('semanas').reduce(function(s,w){return s+(+w.horas||0)},0);
  return {c:c,P:P,V:V,G:G,piezas:piezas,disp:disp,valorObj:valorObj,cobrado:cobrado,pzCobradas:pzCobradas,porCobrar:porCobrar,pzApartadas:pzApartadas,nAp:nAp,nPag:nPag,
    gastos:gastos,rec:rec,inv:inv,falta:Math.max(0,inv-rec),util:rec-inv,costoPz:piezas>0?inv/piezas:NaN,
    factor:valorMx>0&&inv>0?inv/valorMx:NaN,valorMx:valorMx,
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
  var uh=s.horas>0?s.util/s.horas:NaN;
  document.getElementById('kpis').innerHTML=
    kpi('Ventas cobradas',money(s.cobrado),num(s.pzCobradas)+' piezas pagadas o entregadas')+
    kpi('Gastos de operar',money(s.gastos),'Empaque, traslados, publicidad e impuestos')+
    kpi('Utilidad neta',money(s.util),'Cobrado menos gastos menos inversión',s.util>=0?'tone-good':'tone-bad')+
    kpi('Porcentaje vendido',pct(s.pctVend),num(s.disp)+' de '+num(s.piezas)+' piezas siguen disponibles')+
    kpi('Precio promedio real',money(s.precioProm),'Costo por pieza: '+money(s.costoPz))+
    kpi('Inventario por vender',money(s.valorObj),'A precio objetivo. Si se vende todo: '+money(s.rec+s.porCobrar+s.valorObj-s.inv)+' de utilidad')+
    kpi('Utilidad por hora',money(uh),num(s.horas)+' h registradas',isFinite(uh)?(uh>=0?'tone-good':'tone-bad'):'');
  // alertas
  var A=[];
  if(!s.P.length)A.push(['warn','Todavía no hay juguetes en el inventario. <button class="lnk" data-act="nuevoProducto">Agregar el primero</button> o <button class="lnk" data-act="importar">pegar la hoja desde Excel</button>.']);
  if(s.nAp)A.push(['warn',s.nAp+' venta'+(s.nAp>1?'s':'')+' apartada'+(s.nAp>1?'s':'')+' esperando pago ('+money(s.porCobrar)+'). <button class="lnk" data-go="ventas">Ver ventas</button>']);
  if(s.nPag)A.push(['','' +s.nPag+' venta'+(s.nPag>1?'s':'')+' pagada'+(s.nPag>1?'s':'')+' por entregar.']);
  var bajo=+s.c.stockBajo||0, pocosA=s.P.filter(function(p){var d=(+p.cantidad||0)-(s.sold[p.id]||0);return p.clase==='A'&&d>0&&d<=bajo});
  if(pocosA.length)A.push(['','Quedan pocas piezas de '+pocosA.length+' juguete'+(pocosA.length>1?'s':'')+' clase A: '+pocosA.slice(0,3).map(function(p){return esc(p.nombre)}).join(', ')+(pocosA.length>3?'…':'')+'.']);
  var sinPrecio=s.P.filter(function(p){return !(+p.precioObjetivo>0)}).length;
  if(sinPrecio)A.push(['warn',sinPrecio+' juguete'+(sinPrecio>1?'s':'')+' sin precio objetivo. <button class="lnk" data-go="inventario">Completar</button>']);
  var bajoCosto=s.V.filter(function(v){var p=D.productos[v.productoId];var c=p?costoDe(p,s):NaN;return v.estado!=='cancelado'&&isFinite(c)&&(+v.precio||0)<Math.round(c)}).length;
  if(bajoCosto)A.push(['bad',bajoCosto+' venta'+(bajoCosto>1?'s':'')+' por debajo de la parte de la inversión que le toca a su juguete (método proporcional). Revisa si fue un combo.']);
  var sinMx=s.P.filter(function(p){return !(+p.precioMexico>0)}).length;
  if(sinMx&&s.P.length)A.push(['warn',sinMx+' juguete'+(sinMx>1?'s':'')+' sin precio en México: sin él no se calcula su precio mínimo. <button class="lnk" data-go="inventario">Completar</button>']);
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
function valorPallet(P,sold){
  var r={piezas:0,disp:0,mxTot:0,mxDisp:0,objTot:0,objDisp:0,sinMx:0,sinMxPz:0,sinObj:0,sinObjPz:0};
  P.forEach(function(p){var q=+p.cantidad||0,d=Math.max(0,q-(sold[p.id]||0)),mxp=+p.precioMexico||0,ob=+p.precioObjetivo||0;
    r.piezas+=q;r.disp+=d;
    if(mxp>0){r.mxTot+=q*mxp;r.mxDisp+=d*mxp}else{r.sinMx++;r.sinMxPz+=q}
    if(ob>0){r.objTot+=q*ob;r.objDisp+=d*ob}else{r.sinObj++;r.sinObjPz+=q}});
  return r;
}
function renderValor(s){
  var el=document.getElementById('invValor');
  if(!s.P.length){el.innerHTML='';return}
  var v=valorPallet(s.P,s.sold),inv=+s.c.inversion||0;
  var veces=inv>0&&v.mxTot>0?(v.mxTot/inv):NaN;
  var faltan=[];
  if(v.sinMx)faltan.push(num(v.sinMx)+' juguete'+(v.sinMx>1?'s':'')+' ('+num(v.sinMxPz)+' piezas) sin precio en México');
  if(v.sinObj)faltan.push(num(v.sinObj)+' juguete'+(v.sinObj>1?'s':'')+' ('+num(v.sinObjPz)+' piezas) sin precio objetivo');
  el.innerHTML='<div class="card"><h3>Valor del pallet</h3><div class="grid g4">'+
    kpi('Piezas',num(v.piezas),num(v.disp)+' disponibles · '+num(v.piezas-v.disp)+' vendidas o apartadas')+
    kpi('Valor en México',money(v.mxTot),'Cantidad × precio en México · disponible: '+money(v.mxDisp))+
    kpi('Valor a precio objetivo',money(v.objTot),'Cantidad × precio objetivo · disponible: '+money(v.objDisp))+
    kpi('Inversión',money(inv),isFinite(veces)?'El valor en México es '+nf.format(Math.round(veces*10)/10)+' veces la inversión':'Captura precios en México para compararlo')+
    '</div>'+(faltan.length?'<p class="small muted">No se incluyen en los valores: '+esc(faltan.join(' y '))+'.</p>':'<p class="small muted">Todos los juguetes tienen precio en México y precio objetivo.</p>')+
    '<div class="factor"><div><p class="small"><b>Precio mínimo proporcional.</b> '+(isFinite(s.factor)
        ?'Factor = inversión '+money(inv)+' ÷ valor en México '+money(v.mxTot)+' = <b>'+nf4.format(s.factor)+'</b>. Para recuperar la inversión, cada juguete debe venderse en promedio a por lo menos el <b>'+pct(s.factor*100)+'</b> de su precio en México.'+(v.sinMx?' Cambiará al capturar los precios que faltan.':'')
        :'Captura el precio en México de los juguetes para calcularlo.')+'</p></div>'+
      '<button class="btn" type="button" id="recalcMin"'+(isFinite(s.factor)?'':' disabled')+'>Recalcular precios mínimos</button></div></div>';
  var rb=el.querySelector('#recalcMin');
  if(rb)rb.addEventListener('click',function(){
    var n=s.P.filter(function(p){return +p.precioMexico>0}).length;
    if(!confirm('Se reemplazará el precio mínimo de '+n+' juguete'+(n>1?'s':'')+' por precio en México × '+nf4.format(s.factor)+'. Los mínimos que capturaste a mano en esos juguetes se perderán. ¿Continuar?'))return;
    rb.disabled=true;
    api('POST','productos/recalcular-minimos').then(function(r){return cargar().then(function(){toast(r.actualizados+' mínimo'+(r.actualizados===1?'':'s')+' actualizado'+(r.actualizados===1?'':'s')+(r.sinPrecioMexico?' · '+r.sinPrecioMexico+' sin precio en México':''))})},function(e){rb.disabled=false;toast(e.mensaje)});
  });
}
function renderInv(s){
  renderValor(s);
  document.getElementById('invSub').textContent=s.P.length?num(s.P.length)+' tipos · '+num(s.piezas)+' piezas · '+num(s.disp)+' disponibles · costo promedio '+money(s.costoPz)+' por pieza':'';
  var body=document.getElementById('invBody');
  if(!s.P.length){body.innerHTML='<div class="empty"><h3>Abran el pallet y cuenten</h3><p class="muted">Registren cada juguete con su cantidad, estado y precios. Si ya tienen la hoja en Excel, cópienla y péguenla aquí.</p><div class="row" style="justify-content:center"><button class="btn" data-act="importar">Importar desde Excel</button><button class="btn primary" data-act="nuevoProducto">+ Agregar juguete</button></div></div>';return}
  var q=document.getElementById('fBuscar').value.trim().toLowerCase(),fc=document.getElementById('fClase').value,fe=document.getElementById('fEstado').value,fs=document.getElementById('fStock').checked;
  var bajo=+s.c.stockBajo||0;
  var P=s.P.filter(function(p){
    var d=(+p.cantidad||0)-(s.sold[p.id]||0);
    if(q&&((p.codigo||'')+' '+(p.codigoBarras||'')+' '+(p.nombre||'')+' '+(p.categoria||'')+' '+(p.caracteristicas||'')).toLowerCase().indexOf(q)<0)return false;
    if(fc&&p.clase!==fc)return false;if(fe&&p.estado!==fe)return false;if(fs&&d<=0)return false;return true;
  }).sort(function(a,b){var ka=(a.clase||'Z'),kb=(b.clase||'Z');if(ka!==kb)return ka<kb?-1:1;return String(a.codigo||'').localeCompare(String(b.codigo||''),'es',{numeric:true})});
  var tq=0,tv=0,td=0,tmx=0,tob=0;
  var rows=P.map(function(p){var qq=+p.cantidad||0,v=s.sold[p.id]||0,d=Math.max(0,qq-v);tq+=qq;tv+=v;td+=d;tmx+=qq*(+p.precioMexico||0);tob+=qq*(+p.precioObjetivo||0);
    var im=imgPrincipal(p.id);
    var dc=d===0?'zero':(d<=bajo?'low':'');
    return '<tr><td class="tdimg">'+(im?'<button class="minib" type="button" data-det="'+p.id+'" aria-label="Ver detalle de '+esc(p.nombre)+'"><img class="mini" src="'+esc(im.url)+'" alt="" loading="lazy"></button>':'<span class="mini vacio" aria-hidden="true"></span>')+'</td><td class="mono">'+esc(p.codigo)+(p.codigoBarras?'<span class="sub">'+esc(p.codigoBarras)+'</span>':'')+'</td><td class="wrap"><button class="lnk nom" type="button" data-det="'+p.id+'">'+esc(p.nombre)+'</button>'+(p.notas?'<span class="sub">'+esc(p.notas)+'</span>':'')+'</td><td>'+esc(p.categoria)+'</td>'+
      '<td>'+(p.clase?'<span class="pill '+p.clase+'">'+p.clase+'</span>':'')+'</td><td><span class="pill '+(p.estado||'nuevo')+'">'+(EST_P_TXT[p.estado]||'Nuevo')+'</span></td>'+
      '<td class="r">'+num(qq)+'</td><td class="r">'+num(v)+'</td><td class="r '+dc+'">'+num(d)+'</td><td class="r">'+(+p.precioMexico>0?money(+p.precioMexico):'<span class="zero">—</span>')+'</td><td class="r"><b>'+money(+p.precioObjetivo)+'</b></td>'+
      '<td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap"><button class="btn sm" data-sell="'+p.id+'"'+(d<=0?' disabled':'')+'>Vender</button><button class="btn sm" data-editp="'+p.id+'">Editar</button></div></td></tr>'}).join('');
  body.innerHTML=P.length?'<div class="scroll"><table><thead><tr><th><span class="sr">Imagen</span></th><th>Código</th><th>Juguete</th><th>Categoría</th><th>Clase</th><th>Estado</th><th class="r">Cant.</th><th class="r">Vendidas</th><th class="r">Disp.</th><th class="r">P. México</th><th class="r">P. objetivo</th><th></th></tr></thead><tbody>'+rows+
    '</tbody><tfoot><tr><td colspan="6">'+P.length+' juguete'+(P.length>1?'s':'')+' en la vista</td><td class="r">'+num(tq)+'</td><td class="r">'+num(tv)+'</td><td class="r">'+num(td)+'</td><td colspan="3"></td></tr>'+
    '<tr class="tot"><td colspan="9">Valor de la vista (cantidad × precio)</td><td class="r">'+money(tmx)+'</td><td class="r">'+money(tob)+'</td><td></td></tr></tfoot></table></div>'
    :'<div class="empty"><p class="muted">Ningún juguete coincide con los filtros.</p></div>';
}

/* ---- ventas ---- */
var vMode='board';
document.getElementById('vBoard').addEventListener('click',function(){vMode='board';this.setAttribute('aria-pressed','true');document.getElementById('vList').setAttribute('aria-pressed','false');renderVentas(stats())});
document.getElementById('vList').addEventListener('click',function(){vMode='list';this.setAttribute('aria-pressed','true');document.getElementById('vBoard').setAttribute('aria-pressed','false');renderVentas(stats())});
['vBuscar','vCanal'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){renderVentas(stats())})});
function fillSel(id,opts,first){var el=document.getElementById(id),cur=el.value;el.innerHTML='<option value="">'+first+'</option>'+opts.map(function(o){return '<option>'+esc(o)+'</option>'}).join('');el.value=cur}
function pname(id){var p=D.productos[id];return p?p.nombre:'(juguete eliminado)'}
function renderVentas(s){
  fillSel('vCanal',CANALES,'Todos los canales');
  document.getElementById('venSub').textContent=s.V.length?num(s.V.length)+' registros · cobrado '+money(s.cobrado)+' · por cobrar '+money(s.porCobrar):'';
  var body=document.getElementById('venBody');
  if(!s.V.length){body.innerHTML='<div class="empty"><h3>Sin ventas todavía</h3><p class="muted">Cuando un cliente aparte un juguete, regístrenlo aquí como “Apartado”. Al ver el abono, márquenlo “Pagado” y al entregarlo, “Entregado”.</p><button class="btn primary" data-act="nuevaVenta"'+(s.P.length?'':' disabled')+'>+ Registrar venta</button>'+(s.P.length?'':'<p class="small muted">Primero agreguen juguetes al inventario.</p>')+'</div>';return}
  var q=document.getElementById('vBuscar').value.trim().toLowerCase(),fc=document.getElementById('vCanal').value;
  var V=s.V.filter(function(v){
    if(q&&((v.cliente||'')+' '+pname(v.productoId)+' '+(v.notas||'')).toLowerCase().indexOf(q)<0)return false;
    if(fc&&v.canal!==fc)return false;return true;
  }).sort(function(a,b){return (b.fecha||'')<(a.fecha||'')?-1:(b.fecha||'')>(a.fecha||'')?1:(b.creado||0)-(a.creado||0)});
  if(vMode==='board'){
    body.innerHTML='<div class="board">'+ESTADOS_V.map(function(e){
      var it=V.filter(function(v){return v.estado===e}),tot=it.reduce(function(a,v){return a+totalVenta(v)},0);
      if(e==='cancelado'&&!it.length)return '';
      var cards=it.slice(0,60).map(function(v){
        var next=e==='apartado'?['pagado','Marcar pagado']:e==='pagado'?['entregado','Marcar entregado']:null;
        return '<div class="sale"><div class="t"><span>'+esc(pname(v.productoId))+(+v.cantidad>1?' ×'+num(+v.cantidad):'')+'</span><b>'+money(totalVenta(v))+'</b></div>'+
          '<div class="m"><span>'+esc(fdate(v.fecha))+'</span>'+(v.cliente?'<span>'+esc(v.cliente)+'</span>':'')+'<span>'+esc(v.canal||'')+'</span>'+'</div>'+
          '<div class="a">'+(next?'<button class="btn sm primary" data-adv="'+v.id+'" data-to="'+next[0]+'">'+next[1]+'</button>':'')+'<button class="btn sm" data-editv="'+v.id+'">Editar</button></div></div>'}).join('');
      return '<div class="col"><h3>'+EST_V_TXT[e]+' <span>'+it.length+' · '+money(tot)+'</span></h3>'+(cards||'<p class="empty-col">Nada aquí.</p>')+(it.length>60?'<p class="empty-col">Y '+(it.length-60)+' más en la vista de lista.</p>':'')+'</div>'}).join('')+'</div>';
  }else{
    var rows=V.map(function(v){return '<tr><td>'+esc(fdate(v.fecha))+'</td><td class="wrap"><b>'+esc(pname(v.productoId))+'</b>'+(v.notas?'<span class="sub">'+esc(v.notas)+'</span>':'')+'</td><td>'+esc(v.cliente)+'</td><td>'+esc(v.canal)+'</td><td><span class="pill '+v.estado+'">'+EST_V_TXT[v.estado]+'</span></td><td class="r">'+num(+v.cantidad)+'</td><td class="r">'+money2(+v.precio)+'</td><td class="r"><b>'+money(totalVenta(v))+'</b></td><td class="r"><button class="btn sm" data-editv="'+v.id+'">Editar</button></td></tr>'}).join('');
    body.innerHTML=V.length?'<div class="scroll"><table><thead><tr><th>Fecha</th><th>Juguete</th><th>Cliente</th><th>Canal</th><th>Estado</th><th class="r">Cant.</th><th class="r">Precio</th><th class="r">Total</th><th></th></tr></thead><tbody>'+rows+'</tbody></table></div>':'<div class="empty"><p class="muted">Ninguna venta coincide con los filtros.</p></div>';
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
  body.innerHTML='<div class="scroll"><table><thead><tr><th>Fecha</th><th>Tipo</th><th>Concepto</th><th class="r">Monto</th><th></th></tr></thead><tbody>'+
    G.map(function(g){return '<tr><td>'+esc(fdate(g.fecha))+'</td><td>'+esc(g.tipo)+'</td><td class="wrap">'+esc(g.concepto)+'</td><td class="r"><b>'+money2(+g.monto)+'</b></td><td class="r"><button class="btn sm" data-editg="'+g.id+'">Editar</button></td></tr>'}).join('')+
    '</tbody><tfoot><tr><td colspan="3">Total</td><td class="r">'+money2(s.gastos)+'</td><td></td></tr></tfoot></table></div>';
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
  if(!W.length){body.innerHTML='<div class="empty"><h3>Primera revisión semanal</h3><p class="muted">Cada semana anoten cuántas publicaciones tienen activas, cuántos mensajes recibieron y las horas que dedicaron.</p><button class="btn primary" data-act="nuevaSemana">+ Registrar esta semana</button></div>';return}
  var acum=0;var asc=W.slice().reverse().map(function(w){var st=weekStats(w.semana,s);acum+=st.din-st.gas;return [w,st,acum]}).reverse();
  body.innerHTML='<div class="scroll"><table><thead><tr><th>Semana</th><th class="r">Publicadas</th><th class="r">Mensajes</th><th class="r">Ventas</th><th class="r">Piezas</th><th class="r">Cobrado</th><th class="r">Gastos</th><th class="r">Horas</th><th>Notas</th><th></th></tr></thead><tbody>'+
    asc.map(function(r){var w=r[0],st=r[1];return '<tr><td>'+esc(fdate(w.semana))+' – '+esc(fdate(addDays(w.semana,6)))+'</td><td class="r">'+num(+w.publicadas||0)+'</td><td class="r">'+num(+w.mensajes||0)+'</td><td class="r">'+num(st.n)+'</td><td class="r">'+num(st.pz)+'</td><td class="r">'+money(st.din)+'</td><td class="r">'+money(st.gas)+'</td><td class="r">'+num(+w.horas||0)+'</td><td class="wrap small">'+esc(w.notas)+'</td><td class="r"><button class="btn sm" data-edits="'+w.id+'">Editar</button></td></tr>'}).join('')+
    '</tbody></table></div>';
}

/* ---- ajustes ---- */
var cfgDirty=false;
['cInv','cBajo','cMeta','cDesc'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){cfgDirty=true})});
function renderAjustes(s){
  if(cfgDirty)return;var c=s.c;
  document.getElementById('cInv').value=c.inversion;
  document.getElementById('cBajo').value=c.stockBajo;document.getElementById('cMeta').value=c.metaMensual||'';document.getElementById('cDesc').value=c.descuentoSugerido!=null?c.descuentoSugerido:35;
}
document.getElementById('cfgForm').addEventListener('submit',function(e){e.preventDefault();
  var c={inversion:Math.max(0,+document.getElementById('cInv').value||0),
    stockBajo:Math.max(0,+document.getElementById('cBajo').value||0),metaMensual:Math.max(0,+document.getElementById('cMeta').value||0),descuentoSugerido:Math.min(90,Math.max(0,Math.round(+document.getElementById('cDesc').value||0)))};
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
  var G={pend:[],upc:[],usarUpc:true};   // fotos por subir e imágenes de UPCitemdb por descargar al guardar
  openModal(head(id?'Editar juguete':'Nuevo juguete')+'<form class="stack" style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('pCod','Código','<input class="inp mono" id="pCod" name="codigo" required maxlength="20" value="'+esc(p.codigo)+'">')+
    fld('pCla','Clase','<select class="sel" id="pCla" name="clase">'+opt(['A','B','C'],p.clase,{A:'A · Muy buscado o caro',B:'B · Demanda normal',C:'C · Barato, para combos'})+'</select>')+
    fld('pBar','Código de barras','<div class="bc"><input class="inp mono" id="pBar" name="codigoBarras" maxlength="20" inputmode="numeric" autocomplete="off" value="'+esc(p.codigoBarras)+'" placeholder="Escanea o escribe el código"><button class="btn" type="button" id="pBarBtn">Buscar</button></div>','Con el lector: escanea aquí y se busca solo. A mano: escribe el código y presiona Enter.',true)+
    '<div class="full" id="pBarRes"></div>'+
    fld('pNom','Juguete','<input class="inp" id="pNom" name="nombre" required maxlength="120" value="'+esc(p.nombre)+'" placeholder="Marca y nombre del juguete">',null,true)+
    fld('pCat','Categoría','<input class="inp" id="pCat" name="categoria" list="catList" maxlength="60" value="'+esc(p.categoria)+'" placeholder="Muñecas, autos, didácticos…"><datalist id="catList">'+Object.keys(cats).map(function(c){return '<option value="'+esc(c)+'">'}).join('')+'</datalist>')+
    fld('pEst','Estado','<select class="sel" id="pEst" name="estado">'+opt(['nuevo','danado','incompleto'],p.estado,EST_P_TXT)+'</select>')+
    fld('pCan','Cantidad','<input class="inp num" id="pCan" name="cantidad" type="number" min="'+vend+'" step="1" required value="'+esc(p.cantidad!=null?p.cantidad:1)+'">',vend?'Ya hay '+vend+' vendidas o apartadas.':'Piezas de este juguete en el pallet.')+
    fld('pMx','Precio en México','<input class="inp num" id="pMx" name="precioMexico" type="number" min="0" step="0.01" value="'+esc(p.precioMexico||'')+'">','<span id="pMxH"></span>')+
    camposTiendas(p)+
    '<div class="full" id="pMxSug"></div>'+
    fld('pObj','Precio objetivo','<input class="inp num" id="pObj" name="precioObjetivo" type="number" min="0" step="1" value="'+esc(p.precioObjetivo||'')+'">','Lo que esperas cobrar. Publica un poco arriba para negociar.')+
    fld('pMin','Precio mínimo','<input class="inp num" id="pMin" name="precioMinimo" type="number" min="0" step="1" value="'+esc(p.precioMinimo||'')+'">','No bajar de aquí al regatear.')+
    fld('pLis','Precio de lista (referencia)','<input class="inp num" id="pLis" name="precioLista" type="number" min="0" step="1" value="'+esc(p.precioLista||'')+'">','Otra referencia, p. ej. el precio en EE. UU. convertido a pesos.',true)+
    fld('pCar','Características','<textarea class="inp" id="pCar" name="caracteristicas" maxlength="2000" rows="5" placeholder="Descripción, marca, modelo, color, medidas, peso, edad recomendada…">'+esc(p.caracteristicas)+'</textarea><div class="row"><button class="btn sm" type="button" id="pTrad">Traducir al español</button><span class="small muted" id="pTradMsg"></span></div>',null,true)+
    '<div class="f full"><span class="lbl2">Imágenes</span><div class="gal" id="pGal"></div>'+
      '<div class="row"><label class="btn sm" for="pFiles" tabindex="0">Subir fotos</label><input type="file" id="pFiles" accept="image/jpeg,image/png,image/webp,image/gif" multiple hidden></div>'+
      '<span class="hint">JPG, PNG, WEBP o GIF de hasta 5 MB, hasta 12 por juguete. La marcada con ★ es la principal y se muestra en el inventario.</span></div>'+
    fld('pNot','Notas','<textarea class="inp" id="pNot" name="notas" maxlength="500" placeholder="Daños, piezas faltantes, dónde está guardado…">'+esc(p.notas)+'</textarea>',null,true)+
    '</div><div id="pWarn"></div>'+footer(!!id)+'</form>',function(el){
      var w=el.querySelector('#pWarn');
      function chk(){
        var o=+el.querySelector('#pObj').value||0,m=+el.querySelector('#pMin').value||0;
        w.innerHTML=(o>0&&m>0&&o<m)?'<p class="warnline">El precio objetivo ('+money(o)+') es menor que el mínimo ('+money(m)+'): a ese precio no se recupera la parte de la inversión de este juguete.</p>':'';
      }
      el.querySelector('#pObj').addEventListener('input',chk);el.querySelector('#pMin').addEventListener('input',chk);chk();
      el.querySelector('#pTrad').addEventListener('click',function(){
        var t=el.querySelector('#pCar'),b=this,msg=el.querySelector('#pTradMsg');if(!t.value.trim()){msg.textContent='No hay texto para traducir.';return}
        b.disabled=true;msg.textContent='Traduciendo…';
        api('POST','traducir',{texto:t.value}).then(function(r){t.value=r.texto;msg.textContent='Traducido con MyMemory. Revisa el texto.'},function(e){msg.textContent=e.mensaje}).then(function(){b.disabled=false});
      });
      // un campo llenado por una sugerencia se marca "auto" hasta que lo edites tú
      ['pObj','pMin','pLis'].forEach(function(k){el.querySelector('#'+k).addEventListener('input',function(e){if(e.isTrusted)delete this.dataset.auto})});
      wireMx(el,chk,id);
      wireGaleria(el,id,G);
      wireBarras(el,id,chk,pre.resultado,G);
      wireForm(el,function(f){
        var o={codigo:val(f,'codigo'),codigoBarras:val(f,'codigoBarras'),nombre:val(f,'nombre'),categoria:val(f,'categoria'),clase:f.elements.clase.value,estado:f.elements.estado.value,
          cantidad:Math.max(0,Math.round(+f.elements.cantidad.value||0)),precioLista:+f.elements.precioLista.value||0,precioObjetivo:+f.elements.precioObjetivo.value||0,
          precioMinimo:+f.elements.precioMinimo.value||0,precioMexico:+f.elements.precioMexico.value||0,caracteristicas:val(f,'caracteristicas'),notas:val(f,'notas'),
          precioLiverpool:+f.elements.precioLiverpool.value||0,precioMercadoLibre:+f.elements.precioMercadoLibre.value||0,precioWalmart:+f.elements.precioWalmart.value||0};
        var dup=list('productos').some(function(x){return x.id!==id&&String(x.codigo).toLowerCase()===o.codigo.toLowerCase()});
        if(dup){w.innerHTML='<p class="warnline bad">Ya existe un juguete con el código '+esc(o.codigo)+'. Usa otro código.</p>';return}
        if(o.cantidad<vend){w.innerHTML='<p class="warnline bad">La cantidad no puede ser menor que las '+vend+' piezas ya vendidas o apartadas.</p>';return}
        var btn=el.querySelector('button[type=submit]');btn.disabled=true;btn.textContent='Guardando…';
        (id?api('PUT','productos/'+id,o):api('POST','productos',o)).then(function(r){
          return guardarImagenes(r.id,G).then(function(avisos){return cargar().then(function(){closeModal();
            toast((id?'Juguete actualizado':'Juguete agregado')+(avisos.length?'. '+avisos.join(' '):''))})});
        }).catch(function(e){btn.disabled=false;btn.textContent='Guardar';w.innerHTML='<p class="warnline bad">'+esc((e&&e.mensaje)||'No se guardó.')+'</p>'});
      },function(){
        var nv=list('ventas').filter(function(v){return v.productoId===id}).length;
        if(nv){w.innerHTML='<p class="warnline bad">Este juguete tiene '+nv+' venta'+(nv>1?'s':'')+' registrada'+(nv>1?'s':'')+'. Elimínalas primero o pon la cantidad en lo vendido.</p>';return}
        del('productos',id).then(function(){closeModal();toast('Juguete eliminado')},function(){});
      });
    });
}

/* ---- sugerencia con el precio en México ---- */
/* ---- precio mínimo: método proporcional ----
   factor = inversión ÷ Σ(cantidad × precio en México) de los juguetes con precio en México.
   mínimo de un juguete = su precio en México × factor (su parte de la inversión por pieza). */
function factorProporcional(extra){
  // extra: {id, cantidad, precioMexico} para incluir el juguete que se está capturando
  var inv=+cfg().inversion||0,valor=0,piezas=0,conMx=0;
  list('productos').forEach(function(p){if(extra&&p.id===extra.id)return;var q=+p.cantidad||0;piezas+=q;if(+p.precioMexico>0){valor+=q*(+p.precioMexico);conMx+=q}});
  if(extra){var q2=+extra.cantidad||0;piezas+=q2;if(+extra.precioMexico>0){valor+=q2*(+extra.precioMexico);conMx+=q2}}
  return {factor:valor>0&&inv>0?inv/valor:NaN,valor:valor,piezas:piezas,conMx:conMx,inv:inv};
}
function costoDe(p,s){
  if(+p.precioMexico>0&&isFinite(s.factor))return +p.precioMexico*s.factor;
  return NaN;
}
function sugerirMx(precioMx,cantidad,id){
  if(!(precioMx>0))return null;
  var d=+cfg().descuentoSugerido||0,fp=factorProporcional({id:id,cantidad:cantidad,precioMexico:precioMx}),r={notas:[]};
  r.objetivo=Math.round(precioMx*(1-d/100));
  r.notas.push('Objetivo: precio en México '+money(precioMx)+' − '+d+'% = '+money(r.objetivo)+'.');
  if(isFinite(fp.factor)){
    r.minimo=Math.round(precioMx*fp.factor);
    r.notas.push('Mínimo (proporcional): '+money(precioMx)+' × factor '+nf4.format(fp.factor)+' = '+money(r.minimo)+'. Factor = inversión '+money(fp.inv)+' ÷ valor en México del pallet '+money(fp.valor)+'.');
    if(fp.conMx<fp.piezas)r.notas.push('Solo '+num(fp.conMx)+' de '+num(fp.piezas)+' piezas tienen precio en México: el factor bajará al capturar el resto y conviene recalcular los mínimos.');
    if(r.objetivo<r.minimo)r.aviso='Con este objetivo no se recupera la parte de la inversión que le toca a este juguete ('+money(r.minimo)+').';
  }else r.notas.push('Mínimo: captura la inversión en Ajustes para calcularlo.');
  return r;
}
var TIENDAS=[
  {k:'precioLiverpool',id:'pLv',n:'Liverpool',url:function(q){return 'https://www.liverpool.com.mx/tienda?s='+encodeURIComponent(q)}},
  {k:'precioMercadoLibre',id:'pMl',n:'Mercado Libre',url:function(q){return 'https://listado.mercadolibre.com.mx/'+encodeURIComponent(q.trim().replace(/\s+/g,'-'))}},
  {k:'precioWalmart',id:'pWm',n:'Walmart',url:function(q){return 'https://www.walmart.com.mx/search?q='+encodeURIComponent(q)}}
];
function promedioTiendas(vals){var v=vals.filter(function(x){return x>0});return v.length?Math.round(v.reduce(function(a,b){return a+b},0)/v.length*100)/100:null}
function camposTiendas(p){
  return '<div class="f full"><span class="lbl2">Precio en tiendas de México</span><div class="tiendas">'+
    TIENDAS.map(function(t){return '<div class="tienda"><label for="'+t.id+'">'+t.n+'</label><div class="bc"><input class="inp num" id="'+t.id+'" name="'+t.k+'" type="number" min="0" step="1" value="'+esc(p[t.k]||'')+'" placeholder="$">'+
      '<a class="btn sm" id="'+t.id+'L" href="#" target="_blank" rel="noopener noreferrer" title="Buscar en '+t.n+' (se abre en otra pestaña)">Buscar ↗</a></div></div>'}).join('')+
    '</div><span class="hint">“Buscar” abre la tienda con el nombre del juguete. Captura el precio que veas; el Precio en México es el promedio de las tiendas capturadas.</span></div>';
}
function wireMx(el,chk,id){
  var mxI=el.querySelector('#pMx'),box=el.querySelector('#pMxSug'),ob=el.querySelector('#pObj'),mn=el.querySelector('#pMin'),cant=el.querySelector('#pCan'),nom=el.querySelector('#pNom');
  function vacioOAuto(c){return !String(c.value).trim()||+c.value===0||c.dataset.auto}
  function poner(c,v){if(v==null)return;c.value=v;c.dataset.auto='1'}
  function enlaces(){var q=nom.value.trim();TIENDAS.forEach(function(t){var a=el.querySelector('#'+t.id+'L');if(q){a.href=t.url(q);a.removeAttribute('aria-disabled')}else{a.href='#';a.setAttribute('aria-disabled','true')}})}
  function promedio(){
    var vals=TIENDAS.map(function(t){return +el.querySelector('#'+t.id).value||0}),pr=promedioTiendas(vals),n=vals.filter(function(x){return x>0}).length;
    var h=el.querySelector('#pMxH');
    if(pr!=null){mxI.value=pr;mxI.readOnly=true;h.textContent='Promedio de '+n+' tienda'+(n>1?'s':'')+'. Para capturarlo a mano, borra los precios por tienda.'}
    else{mxI.readOnly=false;h.textContent='Sin precios por tienda: puedes capturarlo a mano. Es la base de la sugerencia.'}
  }
  function pinta(auto){
    var s=sugerirMx(+mxI.value||0,+cant.value||0,id);
    if(!s){box.innerHTML='';chk();return}
    var aplicados=[];
    if(auto){if(vacioOAuto(ob)){poner(ob,s.objetivo);aplicados.push('objetivo')}if(s.minimo!=null&&vacioOAuto(mn)){poner(mn,s.minimo);aplicados.push('mínimo')}}
    chk();
    box.innerHTML='<div class="sugbox"><p class="small"><b>Sugerencia con el precio en México</b> (descuento de '+(+cfg().descuentoSugerido||0)+'%, se cambia en Ajustes)</p>'+
      '<ul class="small sug">'+s.notas.map(function(x){return '<li>'+esc(x)+'</li>'}).join('')+'</ul>'+
      (s.aviso?'<p class="warnline">'+esc(s.aviso)+'</p>':'')+
      (aplicados.length?'<p class="small muted">Se llenó: precio '+aplicados.join(' y ')+'.</p>':'')+
      '<div class="row"><button class="btn sm" type="button" id="pMxAplicar">Usar estos precios</button></div></div>';
    box.querySelector('#pMxAplicar').addEventListener('click',function(){poner(ob,s.objetivo);poner(mn,s.minimo);chk();toast('Precios sugeridos aplicados')});
  }
  TIENDAS.forEach(function(t){el.querySelector('#'+t.id).addEventListener('input',function(){promedio();pinta(true)});
    el.querySelector('#'+t.id+'L').addEventListener('click',function(e){if(!nom.value.trim()){e.preventDefault();toast('Escribe primero el nombre del juguete');nom.focus()}})});
  mxI.addEventListener('input',function(){pinta(true)});
  cant.addEventListener('input',function(){pinta(true)});
  nom.addEventListener('input',enlaces);
  el.addEventListener('nombre-cambiado',enlaces);
  enlaces();promedio();pinta(false);
}

/* ---- detalle del juguete con carrusel de fotos ---- */
function detalleForm(id){
  var p=D.productos[id];if(!p)return;
  var s=stats(),imgs=imgsDe(id),vend=s.sold[id]||0,disp=Math.max(0,(+p.cantidad||0)-vend),costo=costoDe(p,s),k=0;
  function fila(l,v){return v==null||v===''?'':'<div class="dt"><dt>'+l+'</dt><dd>'+v+'</dd></div>'}
  function pm(v){return +v>0?money(+v):'<span class="zero">—</span>'}
  var tiendas=TIENDAS.map(function(t){return '<li>'+t.n+': '+pm(p[t.k])+(p.nombre?' <a href="'+esc(t.url(p.nombre))+'" target="_blank" rel="noopener noreferrer">ver ↗</a>':'')+'</li>'}).join('');
  openModal(head(esc(p.nombre))+
    '<div class="det">'+
    '<div class="car" id="car" tabindex="0" aria-roledescription="carrusel" aria-label="Fotos del juguete">'+
      (imgs.length?'<div class="car-vp">'+imgs.map(function(i,n){return '<img src="'+esc(i.url)+'" alt="Foto '+(n+1)+' de '+imgs.length+'"'+(n?' hidden':'')+'>'}).join('')+'</div>'+
        (imgs.length>1?'<button class="car-b prev" type="button" aria-label="Foto anterior">‹</button><button class="car-b next" type="button" aria-label="Foto siguiente">›</button>'+
          '<div class="car-dots">'+imgs.map(function(i,n){return '<button type="button" data-k="'+n+'" aria-label="Ver foto '+(n+1)+'"'+(n?'':' aria-current="true"')+'></button>'}).join('')+'</div>'+
          '<p class="car-n small muted" id="carN">1 de '+imgs.length+'</p>':'')
       :'<div class="car-vacio"><p class="small muted">Sin fotos. Agrégalas desde Editar.</p></div>')+
    '</div>'+
    '<dl class="dl">'+
      fila('Código',esc(p.codigo)+(p.codigoBarras?' · <span class="mono">'+esc(p.codigoBarras)+'</span>':''))+
      fila('Categoría',esc(p.categoria)||'<span class="zero">—</span>')+
      fila('Clase y estado',(p.clase?'<span class="pill '+p.clase+'">'+p.clase+'</span> ':'')+'<span class="pill '+(p.estado||'nuevo')+'">'+(EST_P_TXT[p.estado]||'Nuevo')+'</span>')+
      fila('Piezas',num(+p.cantidad||0)+' en total · '+num(vend)+' vendidas o apartadas · <b>'+num(disp)+' disponibles</b>')+
      fila('Precio en México','<b>'+pm(p.precioMexico)+'</b><ul class="small sug">'+tiendas+'</ul>')+
      fila('Precio objetivo',pm(p.precioObjetivo))+
      fila('Precio mínimo',pm(p.precioMinimo)+(isFinite(costo)?' <span class="small muted">· parte de la inversión hoy: '+money(costo)+'</span>':''))+
      (+p.precioLista>0?fila('Precio de lista (ref.)',money(+p.precioLista)):'')+
      fila('Valor en México',+p.precioMexico>0?money((+p.cantidad||0)*(+p.precioMexico))+' <span class="small muted">('+num(disp)+' disponibles: '+money(disp*(+p.precioMexico))+')</span>':'')+
    '</dl></div>'+
    (p.caracteristicas?'<div><h3>Características</h3><p class="pre small">'+esc(p.caracteristicas)+'</p></div>':'')+
    (p.notas?'<div><h3>Notas</h3><p class="pre small">'+esc(p.notas)+'</p></div>':'')+
    '<div class="pf"><span></span><div class="row"><button class="btn" type="button" data-sell="'+esc(id)+'"'+(disp<=0?' disabled':'')+'>Vender</button><button class="btn primary" type="button" data-editp="'+esc(id)+'">Editar</button></div></div>',
  function(el){
    var car=el.querySelector('#car'),fotos=el.querySelectorAll('.car-vp img'),dots=el.querySelectorAll('.car-dots button'),cnt=el.querySelector('#carN');
    function ver(n){if(!fotos.length)return;k=(n+fotos.length)%fotos.length;
      fotos.forEach(function(f,i){f.hidden=i!==k});dots.forEach(function(d,i){if(i===k)d.setAttribute('aria-current','true');else d.removeAttribute('aria-current')});
      if(cnt)cnt.textContent=(k+1)+' de '+fotos.length}
    var pv=el.querySelector('.car-b.prev'),nx=el.querySelector('.car-b.next');
    if(pv)pv.addEventListener('click',function(){ver(k-1)});if(nx)nx.addEventListener('click',function(){ver(k+1)});
    dots.forEach(function(d){d.addEventListener('click',function(){ver(+d.dataset.k)})});
    car.addEventListener('keydown',function(e){if(e.key==='ArrowLeft'){ver(k-1);e.preventDefault()}else if(e.key==='ArrowRight'){ver(k+1);e.preventDefault()}});
    var x0=null;car.addEventListener('touchstart',function(e){x0=e.touches[0].clientX},{passive:true});
    car.addEventListener('touchend',function(e){if(x0==null)return;var dx=e.changedTouches[0].clientX-x0;if(Math.abs(dx)>40)ver(k+(dx<0?1:-1));x0=null});
    car.focus();
  });
}

/* ---- galería de imágenes del juguete ---- */
function imgsDe(pid){return (IMG[pid]||[]).slice().sort(function(a,b){return (b.principal?1:0)-(a.principal?1:0)})}
function imgPrincipal(pid){var l=IMG[pid]||[];return l.filter(function(i){return i.principal})[0]||l[0]||null}
function wireGaleria(el,id,G){
  var gal=el.querySelector('#pGal'),files=el.querySelector('#pFiles');
  G.render=function(){
    var h='';
    if(id)imgsDe(id).forEach(function(i){h+='<figure class="th'+(i.principal?' pri':'')+'"><img src="'+esc(i.url)+'" alt="" loading="lazy"><figcaption>'+
      '<button type="button" class="ic" data-pri="'+esc(i.id)+'" title="Marcar como principal" aria-label="Marcar como principal">'+(i.principal?'★':'☆')+'</button>'+
      '<button type="button" class="ic" data-del="'+esc(i.id)+'" title="Eliminar imagen" aria-label="Eliminar imagen">×</button></figcaption></figure>'});
    if(G.usarUpc)G.upc.forEach(function(u){h+='<figure class="th pend"><img src="'+esc(u)+'" alt="" referrerpolicy="no-referrer" loading="lazy" onerror="this.style.visibility=\'hidden\'"><figcaption><span class="tag2">UPCitemdb</span></figcaption></figure>'});
    G.pend.forEach(function(f,k){h+='<figure class="th pend"><img src="'+f._url+'" alt=""><figcaption><span class="tag2">Por subir</span><button type="button" class="ic" data-quitar="'+k+'" aria-label="Quitar">×</button></figcaption></figure>'});
    gal.innerHTML=h||'<p class="small muted">Sin imágenes todavía.</p>';
    if(G.upc.length)gal.insertAdjacentHTML('beforeend','<label class="chk small upcchk"><input type="checkbox" id="pUsarUpc"'+(G.usarUpc?' checked':'')+'> Guardar '+(G.upc.length>3?'las primeras 3 ':'')+'imágenes de UPCitemdb al guardar el juguete</label>');
    var cb=gal.querySelector('#pUsarUpc');if(cb)cb.addEventListener('change',function(){G.usarUpc=cb.checked;G.render()});
  };
  gal.addEventListener('click',function(e){
    var b=e.target.closest('button');if(!b)return;
    if(b.dataset.quitar!=null){URL.revokeObjectURL(G.pend[+b.dataset.quitar]._url);G.pend.splice(+b.dataset.quitar,1);G.render();return}
    if(b.dataset.pri){api('PUT','imagenes/'+b.dataset.pri+'/principal').then(cargar).then(G.render).catch(function(er){toast(er.mensaje)});return}
    if(b.dataset.del){if(!confirm('¿Eliminar esta imagen?'))return;api('DELETE','imagenes/'+b.dataset.del).then(cargar).then(G.render).catch(function(er){toast(er.mensaje)})}
  });
  files.addEventListener('change',function(){
    var malos=[];
    Array.prototype.forEach.call(files.files,function(f){
      if(!/^image\/(jpeg|png|webp|gif)$/.test(f.type)){malos.push(f.name+' no es JPG, PNG, WEBP o GIF');return}
      if(f.size>5*1024*1024){malos.push(f.name+' pesa más de 5 MB');return}
      f._url=URL.createObjectURL(f);G.pend.push(f)});
    files.value='';G.render();
    if(malos.length)toast(malos.join('. '));
  });
  G.render();
}
function guardarImagenes(pid,G){
  var avisos=[],cadena=Promise.resolve();
  if(G.usarUpc&&G.upc.length)cadena=cadena.then(function(){return api('POST','productos/'+pid+'/imagenes/upcitemdb').then(function(r){if(r&&r.fallidas)avisos.push('No se pudo descargar '+r.fallidas+' imagen'+(r.fallidas>1?'es':'')+' de UPCitemdb.')},function(e){avisos.push(e.mensaje)})});
  if(G.pend.length)cadena=cadena.then(function(){
    var fd=new FormData();G.pend.forEach(function(f){fd.append('fotos',f,f.name)});
    return fetch('/api/productos/'+pid+'/imagenes',{method:'POST',body:fd}).then(function(r){return r.json().then(function(j){
      if(!r.ok)avisos.push(j&&j.mensaje||'No se subieron las fotos.');else if(j.rechazadas&&j.rechazadas.length)avisos.push('No se subieron: '+j.rechazadas.join('; ')+'.')})})
      .catch(function(){avisos.push('No se subieron las fotos.')});
  });
  return cadena.then(function(){G.pend.forEach(function(f){URL.revokeObjectURL(f._url)});return avisos});
}

/* ---- código de barras ---- */
function buscarBarras(code){return api('GET','codigo-barras/'+encodeURIComponent(code))}
function fdt(iso){try{return new Date(iso).toLocaleString('es-MX',{day:'numeric',month:'short',year:'numeric',hour:'2-digit',minute:'2-digit'})}catch(e){return ''}}
function wireBarras(el,id,chk,inicial,G){
  var inp=el.querySelector('#pBar'),btn=el.querySelector('#pBarBtn'),res=el.querySelector('#pBarRes');
  function campo(n){return el.querySelector('[name="'+n+'"]')}
  function aplicar(datos,forzar){
    var llenos=[];
    Object.keys(datos).forEach(function(k){var v=datos[k];if(v==null||v===''||v===0)return;var c=campo(k);if(!c)return;
      if(forzar||!String(c.value).trim()||(+c.value===0&&c.type==='number')||c.dataset.auto){c.value=v;if(c.type==='number')c.dataset.auto='1';llenos.push(c.closest('.f').querySelector('label').textContent)}});
    chk();return llenos;
  }
  function pinta(r){
    if(r.origen==='inventario'){
      var p=r.producto;
      res.innerHTML=p.id===id?'<p class="small muted">Este código de barras ya pertenece a este juguete.</p>'
        :'<p class="warnline bad">Ya está registrado en el juguete <b>'+esc(p.codigo+' · '+p.nombre)+'</b>. No lo des de alta otra vez: aumenta la cantidad de ese juguete. <button class="lnk" type="button" data-editp="'+esc(p.id)+'">Abrir ese juguete</button></p>';
      return;
    }
    if(r.origen==='ninguno'){res.innerHTML='<p class="warnline">'+esc(r.aviso)+'</p>';return}
    var e=r.externo,sg=r.sugerencia||{},tieneMx=+campo('precioMexico').value>0;
    var nombre=e.marca&&e.titulo.toLowerCase().indexOf(e.marca.toLowerCase())<0?e.marca+' '+e.titulo:e.titulo;
    var datos={nombre:nombre,categoria:e.categoria,caracteristicas:e.caracteristicasEs||e.caracteristicas,precioLista:sg.precioLista};
    // Con precio en México capturado, la sugerencia en dólares no reemplaza objetivo ni mínimo.
    if(!tieneMx){datos.precioObjetivo=sg.precioObjetivo}
    var llenos=aplicar(datos,false);el.dispatchEvent(new Event('nombre-cambiado'));
    G.upc=(e.imagenes&&e.imagenes.length?e.imagenes:(e.imagen?[e.imagen]:[])).slice(0,6);G.render();
    var rango=(e.precioBajo!=null||e.precioAlto!=null)?'Precios registrados en tiendas de EE. UU.: '+(e.moneda==='USD'?'US$':e.moneda+' ')+[e.precioBajo,e.precioAlto].filter(function(x){return x!=null}).map(function(x){return (+x).toFixed(2)}).join(' – ')+(e.ofertas?' · '+e.ofertas+' oferta'+(e.ofertas>1?'s':''):''):'Sin precios registrados.';
    res.innerHTML='<div class="bcres">'+(e.imagen?'<img class="bcimg" src="'+esc(e.imagen)+'" alt="" referrerpolicy="no-referrer" onerror="this.remove()">':'')+
      '<div class="bcinfo"><p class="small"><span class="pill nuevo">UPCitemdb</span> <b>'+esc(e.titulo)+'</b></p>'+
      '<p class="small muted">'+[e.marca,e.modelo,e.categoria].filter(Boolean).map(esc).join(' · ')+'</p>'+
      '<p class="small muted">'+esc(rango)+'</p>'+
      '<ul class="small sug">'+(sg.explicacion||[]).map(function(x){return '<li>'+esc(x)+'</li>'}).join('')+'</ul>'+
      (r.avisoTraduccion?'<p class="warnline">'+esc(r.avisoTraduccion)+'</p>':(e.caracteristicasEs?'<p class="small muted">Características traducidas al español con MyMemory: revísalas.</p>':''))+
      '<p class="small"><b>Busca y captura el precio en México</b> (botones “Buscar” de cada tienda) para que el objetivo y el mínimo se sugieran con él.</p>'+
      '<p class="small muted">'+(llenos.length?'Se llenaron: '+esc(llenos.join(', '))+'. ':'No se cambió ningún campo porque ya tenían datos. ')+
      (G.upc.length?'Las imágenes se guardarán en tu computadora al guardar el juguete. ':'UPCitemdb no tiene imágenes de este juguete. ')+
      'Son sugerencias: revísalas antes de guardar. Consultado '+esc(fdt(r.consultadoUtc))+(r.desdeCache?' (guardado, sin gastar consulta)':'')+'.</p>'+
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
  var s=stats(),v=id?D.ventas[id]:{productoId:pid||'',cantidad:1,fecha:today(),estado:'apartado',canal:'Facebook Marketplace'};
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
    fld('vCli','Cliente','<input class="inp" id="vCli" name="cliente" maxlength="80" value="'+esc(v.cliente)+'" placeholder="Nombre o perfil">',null,true)+
    fld('vNot','Notas','<textarea class="inp" id="vNot" name="notas" maxlength="400" placeholder="Punto y hora de entrega, combo, descuento…">'+esc(v.notas)+'</textarea>',null,true)+
    '</div><div id="vWarn"></div>'+footer(!!id)+'</form>',function(el){
      var w=el.querySelector('#vWarn'),sp=el.querySelector('#vPro'),pr=el.querySelector('#vPre'),cn=el.querySelector('#vCan2');
      var touched=v.precio!=null;pr.addEventListener('input',function(){touched=true;chk()});
      function chk(){var p=D.productos[sp.value];var out='';
        if(p){if(!touched&&+p.precioObjetivo>0)pr.value=p.precioObjetivo;
          var d=(+p.cantidad||0)-vendidasDe(p.id,id),q=+cn.value||0,pp=+pr.value||0;
          out+='<p class="small muted">Disponibles: '+Math.max(0,d)+' · Objetivo '+money(+p.precioObjetivo)+(+p.precioMinimo>0?' · Mínimo '+money(+p.precioMinimo):'')+(isFinite(costoDe(p,s))?' · Su parte de la inversión '+money(costoDe(p,s)):'')+' · Total: <b>'+money(q*pp)+'</b></p>';
          if(q>d)out+='<p class="warnline bad">Solo hay '+Math.max(0,d)+' pieza'+(d===1?'':'s')+' disponible'+(d===1?'':'s')+'.</p>';
          else if(+p.precioMinimo>0&&pp<+p.precioMinimo)out+='<p class="warnline">Precio debajo del mínimo que fijaron para este juguete.</p>';
          else if(isFinite(costoDe(p,s))&&pp>0&&pp<Math.round(costoDe(p,s)))out+='<p class="warnline">Precio debajo de la parte de la inversión que le toca a este juguete.</p>';}
        w.innerHTML=out}
      sp.addEventListener('change',function(){if(!id)touched=false;chk()});cn.addEventListener('input',chk);chk();
      wireForm(el,function(f){
        var p=D.productos[f.elements.productoId.value];if(!p){w.innerHTML='<p class="warnline bad">Elige un juguete.</p>';return}
        var q=Math.max(1,Math.round(+f.elements.cantidad.value||1)),d=(+p.cantidad||0)-vendidasDe(p.id,id);
        var est=f.elements.estado.value;
        if(est!=='cancelado'&&q>d){chk();return}
        var o={productoId:f.elements.productoId.value,cantidad:q,precio:+f.elements.precio.value||0,estado:est,fecha:f.elements.fecha.value||today(),
          canal:f.elements.canal.value,cliente:val(f,'cliente'),notas:val(f,'notas'),creado:v.creado||Date.now()};
        o.fechaPago=!cobrada(o)?'':(id&&cobrada(v)&&v.fechaPago)?v.fechaPago:(id?today():o.fecha);
        put('ventas',id||uid(),o).then(function(){closeModal();toast(id?'Venta actualizada':'Venta registrada')},function(){});
      },function(){del('ventas',id).then(function(){closeModal();toast('Venta eliminada')},function(){})});
    });
}
function advance(id,to){if(!D.ventas[id])return;
  api('PATCH','ventas/'+id+'/estado',{estado:to}).then(cargar).then(function(){toast(to==='pagado'?'Marcada como pagada':'Marcada como entregada')}).catch(function(e){toast((e&&e.mensaje)||'No se guardó')})}

function gastoForm(id){
  var g=id?D.gastos[id]:{fecha:today(),tipo:'Empaque'};
  openModal(head(id?'Editar gasto':'Nuevo gasto')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('gTip','Tipo','<select class="sel" id="gTip" name="tipo">'+opt(TIPOS_G,g.tipo)+'</select>')+
    fld('gMon','Monto (MXN)','<input class="inp num" id="gMon" name="monto" type="number" min="0" step="0.01" required value="'+esc(g.monto!=null?g.monto:'')+'">')+
    fld('gFec','Fecha','<input class="inp" id="gFec" name="fecha" type="date" required value="'+esc(g.fecha)+'">',null,true)+
    fld('gCon','Concepto','<input class="inp" id="gCon" name="concepto" maxlength="120" value="'+esc(g.concepto)+'" placeholder="Bolsas, gasolina entrega Centro, anuncio…">',null,true)+
    '</div>'+footer(!!id)+'</form>',function(el){
      wireForm(el,function(f){var o={tipo:f.elements.tipo.value,monto:Math.max(0,+f.elements.monto.value||0),fecha:f.elements.fecha.value||today(),concepto:val(f,'concepto'),creado:g.creado||Date.now()};
        put('gastos',id||uid(),o).then(function(){closeModal();toast(id?'Gasto actualizado':'Gasto registrado')},function(){})},
      function(){del('gastos',id).then(function(){closeModal();toast('Gasto eliminado')},function(){})});
    });
}

function semanaForm(id){
  var wk=id||monday(today()),w=D.semanas[wk]||{semana:wk};
  openModal(head('Indicadores de la semana')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('sSem','Semana que inicia el lunes','<input class="inp" id="sSem" name="semana" type="date" required value="'+esc(wk)+'"'+(id?' disabled':'')+'>',id?null:'Cualquier fecha se ajusta a su lunes.',true)+
    fld('sPub','Piezas publicadas','<input class="inp num" id="sPub" name="publicadas" type="number" min="0" step="1" value="'+esc(w.publicadas!=null?w.publicadas:'')+'">','Publicaciones activas.')+
    fld('sMen','Mensajes recibidos','<input class="inp num" id="sMen" name="mensajes" type="number" min="0" step="1" value="'+esc(w.mensajes!=null?w.mensajes:'')+'">','Marketplace y WhatsApp.')+
    fld('sHor','Horas dedicadas','<input class="inp num" id="sHor" name="horas" type="number" min="0" step="0.5" value="'+esc(w.horas!=null?w.horas:'')+'">','Publicar, responder, entregar y registrar.',true)+
    fld('sNot','Notas de la semana','<textarea class="inp" id="sNot" name="notas" maxlength="600" placeholder="Precios a ajustar, qué publicar la próxima semana…">'+esc(w.notas)+'</textarea>',null,true)+
    '</div>'+footer(!!id)+'</form>',function(el){
      wireForm(el,function(f){var k=id||monday(f.elements.semana.value||today());
        var o={semana:k,publicadas:+f.elements.publicadas.value||0,mensajes:+f.elements.mensajes.value||0,horas:+f.elements.horas.value||0,notas:val(f,'notas')};
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
    return {codigo:r[0]||'',nombre:r[1],categoria:r[2]||'',cantidad:Math.max(0,Math.round(n(r[3]))),estado:est(r[4]),precioLista:n(r[5]),precioObjetivo:n(r[6]),clase:['A','B','C'].indexOf(cl)>=0?cl:'B',codigoBarras:String(r[8]||'').replace(/[^0-9A-Za-z]/g,''),precioMexico:n(r[9]),precioMinimo:0,notas:''}});
}
function importForm(){
  openModal(head('Importar desde Excel')+'<form style="display:flex;flex-direction:column;gap:12px">'+
    '<p class="small muted">Copien las filas de su hoja y péguenlas abajo. Columnas en este orden: <b>Código, Juguete, Categoría, Cantidad, Estado, Precio de lista, Precio objetivo, Clase</b> y, opcionales, <b>Código de barras</b> y <b>Precio en México</b>. La fila de títulos se ignora.</p>'+
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
  if(k==='inventario')rows=[['Código','Juguete','Categoría','Cantidad','Estado','Precio de lista','Precio objetivo','Clase','Código de barras','Precio en México','Precio mínimo','Vendidas','Disponibles','Precio Liverpool','Precio Mercado Libre','Precio Walmart','Características','Notas']].concat(s.P.map(function(p){var v=s.sold[p.id]||0;return [p.codigo,p.nombre,p.categoria,p.cantidad,EST_P_TXT[p.estado]||'Nuevo',p.precioLista,p.precioObjetivo,p.clase,p.codigoBarras?'\u2060'+p.codigoBarras:'',p.precioMexico||'',p.precioMinimo,v,Math.max(0,(+p.cantidad||0)-v),p.precioLiverpool||'',p.precioMercadoLibre||'',p.precioWalmart||'',p.caracteristicas,p.notas]}));
  else if(k==='ventas')rows=[['Fecha','Fecha de pago','Código','Juguete','Cantidad','Precio','Total','Estado','Canal','Cliente','Notas']].concat(s.V.map(function(v){var p=D.productos[v.productoId]||{};return [v.fecha,v.fechaPago,p.codigo,p.nombre,v.cantidad,v.precio,totalVenta(v),EST_V_TXT[v.estado],v.canal,v.cliente,v.notas]}));
  else if(k==='gastos')rows=[['Fecha','Tipo','Concepto','Monto']].concat(s.G.map(function(g){return [g.fecha,g.tipo,g.concepto,g.monto]}));
  else rows=[['Semana','Publicadas','Mensajes','Horas','Notas']].concat(list('semanas').map(function(w){return [w.semana,w.publicadas,w.mensajes,w.horas,w.notas]}));
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
  else if(t.id==='tcBtn')tcForm();
  else if(t.dataset.go)go(t.dataset.go);
  else if(t.dataset.editp)productoForm(t.dataset.editp);
  else if(t.dataset.det)detalleForm(t.dataset.det);
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
