(function(){
'use strict';
/* ================= datos ================= */
var COLS=['productos','ventas','gastos','semanas','config'];
var D={productos:{},ventas:{},gastos:{},semanas:{},config:{}};
var CFG_DEF={inversion:49000,stockBajo:1,metaMensual:0,tipoCambioUsd:0,descuentoSugerido:35};
var IMG={}, TC={hoy:'',error:null};
// Fase 1: lotes por juguete, pallets, costo vigente por lote y estado del costeo por pallet
var LOTES={}, PALLETS={}, COSTOS={}, CORRIDAS={}, SESION=null;
// Fase 3: resultado real por venta, renglones por lote, publicaciones y plataformas
var RES={}, VDET=[], PUBS=[], PLATS=[];
// Costo por pieza, mínimo y sugerido de cada juguete (mismo cálculo del servidor que la sugerencia al escanear)
var PRECIOS={}, TARIFAS=null;
var CONDS=[{codigo:'nuevo_sellado',nombre:'Nuevo sellado',comercializable:true},{codigo:'nuevo_caja_abierta',nombre:'Nuevo con caja abierta',comercializable:true},{codigo:'usado_bueno',nombre:'Usado en buenas condiciones',comercializable:true},{codigo:'usado_detalles',nombre:'Usado con detalles',comercializable:true},{codigo:'incompleto',nombre:'Incompleto',comercializable:false},{codigo:'danado',nombre:'Dañado',comercializable:false},{codigo:'no_comercializable',nombre:'No comercializable',comercializable:false}];
var EST_COM={en_revision:'En revisión',listo:'Listo para publicar',liquidacion:'En liquidación',retirado:'Retirado'};
function condNombre(c){var x=CONDS.filter(function(k){return k.codigo===c})[0];return x?x.nombre:c}
function lotesDe(pid){return LOTES[pid]||[]}
/** Costo real por pieza: costo vigente de los lotes comercializables ÷ sus piezas. */
function costoReal(pid){
  var ls=lotesDe(pid).filter(function(l){return l.comercializable&&l.cantidad>0}),tot=0,pz=0,falta=false,desact=false;
  ls.forEach(function(l){var c=COSTOS[l.id];if(c&&c.cantidad===l.cantidad){tot+=c.costoTotal;pz+=l.cantidad}else falta=true;
    var cr=CORRIDAS[l.palletId];if(cr&&cr.desactualizado)desact=true});
  return {unit:pz>0?tot/pz:NaN,completo:!falta&&pz>0,desactualizado:desact};
}
function condicionesDe(pid){var ls=lotesDe(pid).filter(function(l){return l.cantidad>0});
  if(!ls.length)return lotesDe(pid).length?condNombre(lotesDe(pid)[0].condicion):'—';
  return ls.length===1?condNombre(ls[0].condicion):ls.map(function(l){return condNombre(l.condicion)+' ('+l.cantidad+')'}).join(', ')}
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
        if(r.status===401&&url.indexOf('auth/')!==0){mostrarAcceso();throw {mensaje:'Tu sesión terminó. Inicia sesión de nuevo.',status:401}}
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
    if(e.catalogos){if(e.catalogos.canales)CANALES=e.catalogos.canales;if(e.catalogos.tiposGasto)TIPOS_G=e.catalogos.tiposGasto;if(e.catalogos.condiciones)CONDS=e.catalogos.condiciones}
    LOTES={};(e.lotes||[]).forEach(function(l){(LOTES[l.productoId]=LOTES[l.productoId]||[]).push(l)});
    PALLETS=porId(e.pallets);COSTOS={};CORRIDAS={};
    if(e.costos){(e.costos.lotes||[]).forEach(function(c){COSTOS[c.loteId]=c});(e.costos.corridas||[]).forEach(function(c){CORRIDAS[c.palletId]=c})}
    RES=porId(e.ventaResultados,'ventaId');VDET=e.ventaDetalles||[];PUBS=e.publicaciones||[];PLATS=e.plataformas||[];
    mode='ok';setStatus();renderAll();cargarPrecios();
  },function(err){mode='error';setStatus();throw err});
}
var pidePrecios=0;
function cargarPrecios(){var k=++pidePrecios;
  return api('GET','productos/precios').then(function(r){if(k!==pidePrecios)return;PRECIOS=porId(r.productos,'productoId');TARIFAS=r.tarifasCapturadas;
    if(TAB==='resumen'||TAB==='inventario')renderAll()},function(){});
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
    if(document.hidden||!SESION)return;
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
/** Fase 3: una venta está cobrada solo cuando se registró el importe que llegó a la cuenta (no basta con marcarla pagada). */
function cobrada(v){return v.importeCobrado!=null}
function totalVenta(v){return (+v.cantidad||0)*(+v.precio||0)-(+v.descuento||0)}
function netas(v){return (+v.cantidad||0)-(+v.cantidadDevuelta||0)}
function res(v){return RES[v.id]||{}}
function vendidasDe(pid,exceptId){var s=0;list('ventas').forEach(function(v){if(v.productoId===pid&&v.estado!=='cancelado'&&v.id!==exceptId)s+=netas(v)});return s}
function platNombre(id){var p=PLATS.filter(function(x){return x.id===id})[0];return p?p.nombre:''}
function canalDe(v){return v.plataformaId?platNombre(v.plataformaId):(v.canal||'')}
function folio(v){return v.folio?'V-'+String(v.folio).padStart(5,'0'):''}

function stats(){
  var c=cfg(), P=list('productos'), V=list('ventas'), G=list('gastos');
  var piezas=0,valorObj=0,disp=0;
  var sold={};V.forEach(function(v){if(v.estado!=='cancelado')sold[v.productoId]=(sold[v.productoId]||0)+netas(v)});
  var valorMx=0;
  P.forEach(function(p){var q=+p.cantidad||0;piezas+=q;var d=Math.max(0,q-(sold[p.id]||0));disp+=d;valorObj+=d*(+p.precioObjetivo||0);if(+p.precioMexico>0)valorMx+=q*(+p.precioMexico)});
  // Fase 3: cobrado = importe que llegó; efectivo = cobrado − gastos que pagaste tú − reembolsos; por cobrar = estimado de lo que falta depositar.
  var apartado=0,cobrado=0,efectivo=0,pzCobradas=0,porCobrar=0,pzApartadas=0,nAp=0,nPag=0,nSinCobro=0,utilVentas=0,nEstim=0,nAbiertas=0;
  V.forEach(function(v){var r=res(v);
    if(cobrada(v)){cobrado+=+v.importeCobrado||0;efectivo+=+r.efectivoNeto||0;if(v.estado!=='cancelado')pzCobradas+=netas(v)}
    else if(v.estado!=='cancelado'){porCobrar+=+r.porCobrarEstimado||0;if(v.estado!=='apartado')nSinCobro++}
    if(v.estado==='apartado'&&!cobrada(v)){pzApartadas+=(+v.cantidad||0);nAp++;apartado+=totalVenta(v)}
    if(v.estado==='pagado')nPag++;
    utilVentas+=+r.utilidad||0;if(r.estimados&&v.estado!=='cancelado')nEstim++;nAbiertas+=+r.abiertas||0});
  var gastos=G.reduce(function(s,g){return s+(+g.monto||0)},0);
  var rec=efectivo-gastos, inv=+c.inversion||0;
  var horas=list('semanas').reduce(function(s,w){return s+(+w.horas||0)},0);
  return {c:c,P:P,V:V,G:G,piezas:piezas,disp:disp,valorObj:valorObj,cobrado:cobrado,pzCobradas:pzCobradas,porCobrar:porCobrar,pzApartadas:pzApartadas,nAp:nAp,nPag:nPag,
    gastos:gastos,rec:rec,inv:inv,falta:Math.max(0,inv-rec),util:rec-inv,costoPz:piezas>0?inv/piezas:NaN,
    factor:valorMx>0&&inv>0?inv/valorMx:NaN,valorMx:valorMx,
    precioProm:pzCobradas>0?cobrado/pzCobradas:NaN,pctVend:piezas>0?pzCobradas/piezas*100:NaN,horas:horas,sold:sold,
    apartado:apartado,efectivo:efectivo,nSinCobro:nSinCobro,utilVentas:utilVentas,nEstim:nEstim,nAbiertas:nAbiertas,
    pubAccion:PUBS.filter(function(x){return x.requiereAccion}).length};
}

/* ================= navegación ================= */
var TAB='resumen';
var VALID=['resumen','inventario','ventas','gastos','semana','ajustes','analisis'];
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
  else if(TAB==='analisis'&&window.PCA)window.PCA.render(s);
}
function kpi(l,v,n,tone){return '<div class="kpi"><div class="l">'+l+'</div><div class="v'+(tone?' '+tone:'')+'">'+v+'</div><div class="n">'+n+'</div></div>'}

/* ================= Inicio guiado ================= */
function palletsConLotes(){var k={};Object.keys(LOTES).forEach(function(pid){LOTES[pid].forEach(function(l){k[l.palletId]=1})});return Object.keys(k)}
function pasosGuia(s){
  var pal=palletsLista(),conLotes=palletsConLotes();
  var costeoOk=conLotes.length>0&&conLotes.every(function(id){var c=CORRIDAS[id];return c&&!c.desactualizado});
  return [
    {k:'pallet',ok:pal.length>0,t:'Registra tu pallet',d:'Captura cuánto pagaste por él (en pesos o en dólares). Es tu inversión: lo que tienes que recuperar.',b:'Registrar pallet'},
    {k:'mercado',ok:pal.some(function(p){return +p.valorMercadoEstimado>0}),opc:true,t:'Captura el valor de mercado del pallet',d:'El «retail value» del manifiesto del proveedor. Con él, el precio sugerido de cada juguete es confiable desde el primero que escanees.',b:'Capturar valor'},
    {k:'juguetes',ok:s.P.length>0,t:'Registra tus juguetes',d:'Escanea el código de barras de cada caja: la aplicación llena el nombre, las fotos y te sugiere precio y mínimo.',b:'Escanear juguete'},
    {k:'costeo',ok:costeoOk,t:'Calcula el costeo del pallet',d:'Cuando termines de registrar los juguetes: reparte la inversión entre todos y el costo de cada uno pasa de estimado a real.',b:'Calcular costeo'},
    {k:'publicar',ok:PUBS.length>0,t:'Publica tus juguetes',d:'Anúncialos al precio sugerido y registra dónde los publicaste para evitar vender dos veces la misma pieza.',b:'Registrar publicación'},
    {k:'vender',ok:s.V.some(function(v){return cobrada(v)}),t:'Registra ventas y su cobro',d:'Al vender, registra la venta; cuando el dinero llegue a tu cuenta, registra el cobro. Así ves cuánto has recuperado.',b:'Registrar venta'}];
}
function irGuia(k){
  if(k==='juguetes')return escanearForm();
  if(k==='vender'){go('ventas');return ventaForm()}
  var sub={pallet:'pallets',mercado:'pallets',costeo:'pallets',publicar:'publicaciones'}[k];
  go('analisis');if(window.PCA)window.PCA.irA(sub);
}
function renderGuia(s){
  var el=document.getElementById('guia');if(!el)return;
  var P=pasosGuia(s),obl=P.filter(function(x){return !x.opc}),hechos=obl.filter(function(x){return x.ok}).length,sig=obl.filter(function(x){return !x.ok})[0];
  var oculto=false;try{oculto=localStorage.getItem(LKEY+'-guia')==='oculta'}catch(e){}
  if(!sig&&oculto){el.innerHTML='';return}
  el.innerHTML='<div class="guia"><div class="pf"><h3 style="margin:0">Guía paso a paso · '+hechos+' de '+obl.length+'</h3>'+(sig?'':'<button class="lnk" type="button" id="guiaOcultar">Ocultar guía</button>')+'</div>'+
    (sig?'<div class="sig"><div><span class="small muted">Siguiente paso</span><br><b>'+esc(sig.t)+'</b><br><span class="small">'+esc(sig.d)+'</span></div><button class="btn primary" type="button" data-guia="'+sig.k+'">'+esc(sig.b)+'</button></div>'
      :'<div class="sig"><div><b>¡Todo listo!</b><br><span class="small">Sigue registrando ventas y cobros. Revisa los pendientes de abajo cada día.</span></div></div>')+
    '<ol class="pasos">'+P.map(function(x,i){return '<li class="'+(x.ok?'ok':'')+(sig&&x.k===sig.k?' act':'')+'"><span class="n">'+(x.ok?'✓':(i+1))+'</span><div><b>'+esc(x.t)+'</b>'+(x.opc?' <span class="small muted">(opcional: solo si tienes manifiesto)</span>':'')+
      (x.ok?'':'<br><button class="lnk small" type="button" data-guia="'+x.k+'">'+esc(x.b)+'</button>')+'</div></li>'}).join('')+'</ol></div>';
  el.querySelectorAll('[data-guia]').forEach(function(b){b.addEventListener('click',function(){irGuia(b.dataset.guia)})});
  var oc=el.querySelector('#guiaOcultar');if(oc)oc.addEventListener('click',function(){try{localStorage.setItem(LKEY+'-guia','oculta')}catch(e){}renderGuia(s)});
}
var iniPal='';
function enPallet(pid){if(!iniPal)return true;return lotesDe(pid).some(function(l){return l.palletId===iniPal})}
function renderNumeros(s){
  var el=document.getElementById('iniNum');if(!el)return;
  var pals=palletsLista(),w=document.getElementById('iniPalWrap'),sel=document.getElementById('iniPal');
  w.hidden=pals.length<2;
  if(pals.length>=2){sel.innerHTML='<option value="">Todos los pallets</option>'+pals.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===iniPal?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('');
    sel.onchange=function(){iniPal=sel.value;renderAll()}}else iniPal='';
  var P=s.P.filter(function(p){return enPallet(p.id)}),Ps=iniPal?pals.filter(function(p){return p.id===iniPal}):pals;
  var piezas=0,disp=0,vendidas=0,valor=0,sinValor=0,costoTot=0,pzCosto=0,estimado=false;
  P.forEach(function(p){
    var ls=lotesDe(p.id).filter(function(l){return !iniPal||l.palletId===iniPal}),q=ls.reduce(function(a,l){return a+l.cantidad},0);
    var parte=(+p.cantidad||0)>0?q/(+p.cantidad):1,vend=Math.round((s.sold[p.id]||0)*parte);
    piezas+=q;vendidas+=vend;disp+=Math.max(0,q-vend);
    var ref=+p.precioMexico>0?+p.precioMexico:(+p.valorReferencia||0);if(ref>0)valor+=q*ref;else sinValor+=q;
    var x=PRECIOS[p.id];if(x&&x.ok&&x.costo){costoTot+=q*x.costo;pzCosto+=q;if(x.origenCosto!=='real')estimado=true}});
  var inv=Ps.reduce(function(a,p){return a+(+p.inversionTotal||0)},0);
  var sinPallet=!pals.length;
  var manif=Ps.reduce(function(a,p){return a+(+p.valorMercadoEstimado>0?(+p.valorMercadoEstimado)*(p.moneda==='MXN'?1:(+p.tipoCambio||0)):0)},0);
  // Recuperado: efectivo neto de las ventas cobradas (repartido por piezas si una venta trae varios pallets)
  var rec=0,util=0;
  s.V.forEach(function(v){var r=res(v),dets=VDET.filter(function(d){return d.ventaId===v.id}),tot=dets.reduce(function(a,d){return a+d.cantidad},0)||1,
    mio=iniPal?dets.filter(function(d){return d.palletId===iniPal}).reduce(function(a,d){return a+d.cantidad},0)/tot:1;
    if(!mio)return;if(cobrada(v))rec+=(+r.efectivoNeto||0)*mio;util+=(+r.utilidad||0)*mio});
  var pct=inv>0?Math.max(0,Math.min(100,rec/inv*100)):0;
  el.innerHTML=
    kpi('Juguetes',num(P.length),num(piezas)+' piezas en total · '+num(disp)+' disponibles · '+num(vendidas)+' vendidas')+
    kpi('Inversión'+(iniPal?' del pallet':''),sinPallet?'—':money(inv),Ps.length?Ps.length+' pallet'+(Ps.length>1?'s':'')+(Ps.some(function(p){return p.moneda!=='MXN'})?' · convertido a pesos':''):'Registra tu pallet para capturarla')+
    kpi('Costo promedio por pieza',pzCosto?money2(costoTot/pzCosto):'—',pzCosto?(estimado?'Estimado (aún sin costeo del pallet). ':'Real, del costeo del pallet. ')+'Cada juguete carga más o menos según su valor: ve la tabla de abajo.':'Se calcula al registrar juguetes con su precio de referencia.')+
    kpi('Valor de mercado',manif?money(manif):(valor?money(valor):'—'),manif
      ?'Según el manifiesto. Llevas registrado '+money(valor)+' ('+Math.min(100,valor/manif*100).toFixed(0)+' %)'+(valor<manif?': registra los demás juguetes y luego calcula el costeo.':'.')
      :(valor?'Suma de los juguetes registrados'+(inv?' · '+(valor/inv).toLocaleString('es-MX',{maximumFractionDigits:1})+' veces lo invertido':'')+'. Captura el valor del manifiesto en el pallet para comparar.':'Precio en México o de EE. UU. de cada juguete.')+(sinValor?' '+num(sinValor)+' piezas sin precio de referencia.':''))+
    kpi('Recuperado',money(rec),sinPallet?'Dinero ya cobrado de tus ventas':'Dinero ya cobrado · '+pct.toFixed(0)+' % de la inversión · falta '+money(Math.max(0,inv-rec)),rec>=inv&&inv>0?'tone-good':'')+
    kpi('Ganancia de tus ventas',money(util),'Con el costo de cada pieza vendida y los gastos de cada venta',util<0?'tone-bad':(util>0?'tone-good':''));
}
function renderPreciosInicio(s){
  var el=document.getElementById('iniPrecios');if(!el)return;
  var P=s.P.filter(function(p){return enPallet(p.id)});
  if(!P.length){el.innerHTML='<div class="empty"><p class="muted">Aún no registras juguetes. Escanea el código de barras de la primera caja y la aplicación te dirá en cuánto venderla.</p><button class="btn primary" data-act="escanear">Escanear juguete</button></div>';return}
  if(TARIFAS===null&&!Object.keys(PRECIOS).length){el.innerHTML='<p class="small muted">Calculando precios…</p>';return}
  var filas=P.map(function(p){return {p:p,d:Math.max(0,(+p.cantidad||0)-(s.sold[p.id]||0)),x:PRECIOS[p.id]||{}}}).sort(function(a,b){return b.d-a.d||String(a.p.nombre).localeCompare(String(b.p.nombre),'es')});
  var max=12,ver=filas.slice(0,max);
  el.innerHTML='<div class="scroll" style="border:0"><table><thead><tr><th><span class="sr">Imagen</span></th><th>Juguete</th><th class="r">Disponibles</th><th class="r">Costo por pieza</th><th class="r">Mínimo</th><th class="r">Sugerido</th><th class="r">Tu precio</th></tr></thead><tbody>'+
    ver.map(function(f){var p=f.p,x=f.x,im=imgPrincipal(p.id),obj=+p.precioObjetivo||0,bajo=obj>0&&x.minimo&&obj<x.minimo;
      return '<tr><td class="tdimg">'+(im?'<img class="mini" src="'+esc(im.url)+'" alt="" loading="lazy">':'<span class="mini vacio" aria-hidden="true"></span>')+'</td>'+
        '<td class="wrap"><button class="lnk nom" type="button" data-det="'+p.id+'">'+esc(p.nombre)+'</button><span class="sub">'+esc(p.codigo)+(x.canal?' · '+esc(x.canal):'')+'</span></td>'+
        '<td class="r">'+num(f.d)+'</td>'+
        (x.ok?'<td class="r">'+money2(x.costo)+' <span class="pill '+(x.origenCosto==='real'?'entregado':'apartado')+'">'+(x.origenCosto==='real'?'Real':'Estimado')+'</span></td><td class="r"><b>'+money(x.minimo)+'</b></td><td class="r"><b>'+money(x.sugerido)+'</b></td>'
          :'<td class="small muted" colspan="3">'+esc(x.falta||'Falta información para calcularlo.')+' <button class="lnk" type="button" data-editp="'+p.id+'">Completar</button></td>')+
        '<td class="r">'+(obj>0?money(obj)+(bajo?' <span class="tag-w" title="Debajo del mínimo">bajo</span>':''):'<span class="zero">—</span>')+'</td></tr>'}).join('')+
    '</tbody></table></div>'+(filas.length>max?'<p class="small muted">Y '+(filas.length-max)+' juguete(s) más en <button class="lnk" type="button" data-go="inventario">Inventario</button>.</p>':'');
}
function renderResumen(s){
  renderGuia(s);renderNumeros(s);renderPreciosInicio(s);
  document.getElementById('hoyTxt').textContent=new Date().toLocaleDateString('es-MX',{weekday:'long',day:'numeric',month:'long',year:'numeric'});
  var p=s.inv>0?Math.max(0,Math.min(100,s.rec/s.inv*100)):0;
  document.getElementById('hFalta').textContent=s.falta>0?money(s.falta):'Recuperado';
  document.getElementById('hFalta').className='big'+(s.falta>0?'':' tone-good');
  document.getElementById('hFaltaN').textContent=s.falta>0
    ?'Llevan '+pct(p)+' de la inversión. '+(s.porCobrar>0?'Hay '+money(s.porCobrar)+' por cobrar (estimado).':'')
    :'Ya cubrieron la inversión. Ganancia acumulada: '+money(s.util)+'.';
  if(!palletsLista().length&&!s.V.length){   // sin pallet todavía: no se muestra una inversión de ejemplo
    document.getElementById('hFalta').textContent='—';document.getElementById('hFalta').className='big';
    document.getElementById('hFaltaN').textContent='Registra tu pallet (paso 1 de la guía) para ver cuánto te falta recuperar.';p=0}
  var m=document.getElementById('hMeter');m.style.width=p+'%';m.className=p>=100?'done':'';
  document.getElementById('hRec').textContent=money(s.rec);document.getElementById('hInv').textContent=palletsLista().length||s.V.length?money(s.inv):'—';
  drawChart(s);
  var uh=s.horas>0?s.util/s.horas:NaN;
  document.getElementById('kpis').innerHTML=
    kpi('Ventas cobradas',money(s.cobrado),'Lo que llegó a tu cuenta · '+num(s.pzCobradas)+' piezas'+(s.porCobrar>0?' · por cobrar '+money(s.porCobrar):''))+
    kpi('Gastos de operar',money(s.gastos),'Empaque, traslados, publicidad… (los de cada venta ya se restan en su ganancia)')+
    kpi('Piezas vendidas',pct(s.pctVend),num(s.piezas-s.disp)+' vendidas · '+num(s.disp)+' de '+num(s.piezas)+' siguen disponibles')+
    kpi('Si vendes lo que queda',money(s.valorObj),'Valor de las piezas disponibles a tu precio objetivo'+(s.horas>0&&isFinite(uh)?' · utilidad por hora '+money(uh):''));
  // alertas
  var A=[];
  if(!s.P.length)A.push(['warn','Todavía no hay juguetes en el inventario. <button class="lnk" data-act="nuevoProducto">Agregar el primero</button> o <button class="lnk" data-act="importar">pegar la hoja desde Excel</button>.']);
  if(s.nAp)A.push(['warn',s.nAp+' venta'+(s.nAp>1?'s':'')+' apartada'+(s.nAp>1?'s':'')+' esperando pago ('+money(s.apartado)+'). <button class="lnk" data-go="ventas">Ver ventas</button>']);
  if(s.nPag)A.push(['','' +s.nPag+' venta'+(s.nPag>1?'s':'')+' pagada'+(s.nPag>1?'s':'')+' por entregar.']);
  if(s.pubAccion)A.push(['bad',s.pubAccion+' publicaci'+(s.pubAccion>1?'ones requieren':'ón requiere')+' atención en su plataforma (pausar, ajustar existencias o reactivar) para evitar una doble venta. <button class="lnk" data-act="verPublicaciones">Ver publicaciones</button>']);
  if(s.nSinCobro)A.push(['warn',s.nSinCobro+' venta'+(s.nSinCobro>1?'s':'')+' pagada'+(s.nSinCobro>1?'s':'')+' o entregada'+(s.nSinCobro>1?'s':'')+' sin cobro registrado: no cuentan como dinero recuperado hasta registrar el depósito. <button class="lnk" data-go="ventas">Ver ventas</button>']);
  if(s.nEstim)A.push(['',s.nEstim+' venta'+(s.nEstim>1?'s':'')+' con gastos estimados por la tarifa. Confírmalos con el estado de cuenta de la plataforma.']);
  if(s.nAbiertas)A.push(['warn',s.nAbiertas+' reclamaci'+(s.nAbiertas>1?'ones abiertas':'ón abierta')+'.']);
  var bajo=+s.c.stockBajo||0, pocosA=s.P.filter(function(p){var d=(+p.cantidad||0)-(s.sold[p.id]||0);return p.clase==='A'&&d>0&&d<=bajo});
  if(pocosA.length)A.push(['','Quedan pocas piezas de '+pocosA.length+' juguete'+(pocosA.length>1?'s':'')+' clase A: '+pocosA.slice(0,3).map(function(p){return esc(p.nombre)}).join(', ')+(pocosA.length>3?'…':'')+'.']);
  var sinPrecio=s.P.filter(function(p){return !(+p.precioObjetivo>0)}),conSug=sinPrecio.filter(function(p){return (PRECIOS[p.id]||{}).ok}).length;
  if(sinPrecio.length)A.push(['warn',sinPrecio.length+' juguete'+(sinPrecio.length>1?'s':'')+' sin precio de venta (precio objetivo). '+(conSug?'<button class="lnk" data-act="usarSugeridos">Usar el precio sugerido en '+conSug+'</button>':'<button class="lnk" data-go="inventario">Completar</button>')]);
  var bajoCosto=s.V.filter(function(v){var p=D.productos[v.productoId];var c=p?costoDe(p,s):NaN;return v.estado!=='cancelado'&&isFinite(c)&&(+v.precio||0)<Math.round(c)}).length;
  if(bajoCosto)A.push(['bad',bajoCosto+' venta'+(bajoCosto>1?'s':'')+' por debajo de la parte de la inversión que le toca a su juguete (método proporcional). Revisa si fue un combo.']);
  var sinDatos=s.P.filter(function(p){var x=PRECIOS[p.id];return x&&!x.ok}).length;
  if(sinDatos)A.push(['warn',sinDatos+' juguete'+(sinDatos>1?'s':'')+' sin datos para calcular su precio: escanea su código de barras o captura su precio en México. <button class="lnk" data-go="inventario">Completar</button>']);
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
  var ev=[];s.V.forEach(function(v){if(cobrada(v))ev.push([v.fechaCobro||v.fechaPago||v.fecha||today(),+res(v).efectivoNeto||0])});
  s.G.forEach(function(g){ev.push([g.fecha||today(),-(+g.monto||0)])});
  if(!ev.length){el.innerHTML='<div class="empty" style="padding:20px"><p class="small muted">La línea aparece cuando registren la primera venta cobrada o el primer gasto. '+(palletsLista().length?'Empieza en −'+money(s.inv)+' y sube hasta cruzar el cero.':'Empieza en −(tu inversión) y sube hasta cruzar el cero.')+'</p></div>';return}
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
function palletsLista(){return Object.keys(PALLETS).map(function(k){return PALLETS[k]}).sort(function(a,b){return a.fechaCompra<b.fechaCompra?1:-1})}
function fillPallets(id,primero){var el=document.getElementById(id);if(!el)return;var cur=el.value;
  el.innerHTML='<option value="">'+primero+'</option>'+palletsLista().map(function(p){return '<option value="'+esc(p.id)+'">'+esc(p.nombre)+'</option>'}).join('');el.value=cur}
function optPallets(cur){return palletsLista().map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===cur?' selected':'')+'>'+esc(p.nombre)+(p.estatus==='cerrado'?' (cerrado)':'')+'</option>'}).join('')}
function optConds(cur){return CONDS.map(function(c){return '<option value="'+c.codigo+'"'+(c.codigo===cur?' selected':'')+'>'+esc(c.nombre)+'</option>'}).join('')}
['fBuscar','fClase','fCond','fPallet','fStock'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){renderInv(stats())})});
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
  var abierto=false;try{abierto=localStorage.getItem(LKEY+'-invvalor')==='1'}catch(e){}
  el.innerHTML='<details class="card valorp"'+(abierto?' open':'')+'><summary><b>Valor del pallet</b><span class="small muted"> · '+num(v.disp)+' piezas disponibles · inversión '+money(inv)+' · a tu precio '+money(v.objTot)+'</span></summary><div class="grid g4">'+
    kpi('Piezas',num(v.piezas),num(v.disp)+' disponibles · '+num(v.piezas-v.disp)+' vendidas o apartadas')+
    kpi('Valor en México',money(v.mxTot),'Cantidad × precio en México · disponible: '+money(v.mxDisp))+
    kpi('Valor a precio objetivo',money(v.objTot),'Cantidad × precio objetivo · disponible: '+money(v.objDisp))+
    kpi('Inversión',money(inv),isFinite(veces)?'El valor en México es '+nf.format(Math.round(veces*10)/10)+' veces la inversión':'Captura precios en México para compararlo')+
    '</div>'+(faltan.length?'<p class="small muted">No se incluyen en los valores: '+esc(faltan.join(' y '))+'.</p>':'<p class="small muted">Todos los juguetes tienen precio en México y precio objetivo.</p>')+
    '<div class="factor"><div><p class="small"><b>Precio mínimo.</b> '+(function(){
        var con=s.P.filter(function(p){return isFinite(costoReal(p.id).unit)}).length;
        return 'Es lo menos que debes aceptar: cubre el costo de la pieza y tu margen mínimo (igual que la sugerencia al escanear). '+
          (con?'<b>'+num(con)+' de '+num(s.P.length)+'</b> juguetes tienen costo real (costeo de su pallet); los demás usan un costo estimado con su valor de mercado.':'Aún no hay costeos aplicados: se usa un costo estimado con el valor de mercado de cada juguete. Calcula el costeo de cada pallet en Análisis comercial › Pallets y costos.');
      })()+'</p></div>'+
      '<button class="btn" type="button" id="recalcMin">Recalcular precios mínimos</button></div></details>';
  el.querySelector('details').addEventListener('toggle',function(){try{localStorage.setItem(LKEY+'-invvalor',this.open?'1':'0')}catch(e){}});
  var rb=el.querySelector('#recalcMin');
  if(rb)rb.addEventListener('click',function(){
    if(!confirm('Se reemplazará el precio mínimo de todos los juguetes con el mismo criterio de la sugerencia al escanear: costo de la pieza (real o estimado) + margen mínimo, como venta directa. Los mínimos que capturaste a mano se perderán; los juguetes sin datos suficientes conservan el suyo. ¿Continuar?'))return;
    rb.disabled=true;
    api('POST','productos/recalcular-minimos').then(function(r){return cargar().then(function(){
      toast(r.actualizados+' mínimo'+(r.actualizados===1?'':'s')+' actualizado'+(r.actualizados===1?'':'s')+' · '+r.conCostoReal+' con costo real · '+r.conCostoEstimado+' con costo estimado'+
        (r.sinDatos?' · '+r.sinDatos+' sin datos suficientes (captura el precio en México o escanea su código): '+r.sinDatosCodigos.join(', '):''))})},function(e){toast((e&&e.mensaje)||'No se pudo recalcular')}).then(function(){rb.disabled=false});
  });
}
/* ---- inventario: tarjetas con foto y etiquetas de estado ---- */
var INV_VISTA='tarjetas',INV_ETQ='';
try{if(localStorage.getItem(LKEY+'-invvista')==='tabla')INV_VISTA='tabla'}catch(e){}
var ETQ_DEF=[['disponible','Disponible','ok'],['pocas','Pocas piezas','warn'],['agotado','Agotado','gris'],['apartado','Apartado','warn'],
  ['publicado','Publicado','info'],['sinpub','Sin publicar','gris'],['sinfoto','Sin foto','warn'],['sinprecio','Sin precio','warn'],['bajo','Bajo el mínimo','bad']];
function etq(t,c,title){return '<span class="etq '+c+'"'+(title?' title="'+esc(title)+'"':'')+'>'+esc(t)+'</span>'}
/** Etiquetas de estado de un juguete: [{k,t,c,title}] (k = clave para filtrar). */
function etiquetasDe(p,d,bajo){
  var E=[],x=PRECIOS[p.id]||{},po=+p.precioObjetivo||0;
  if(d<=0)E.push({k:'agotado',t:'Agotado',c:'gris'});
  else if(bajo>0&&d<=bajo)E.push({k:'pocas',t:'Quedan '+d,c:'warn',title:'Pocas piezas disponibles'});
  else E.push({k:'disponible',t:'Disponible',c:'ok'});
  var ap=list('ventas').filter(function(v){return v.productoId===p.id&&v.estado==='apartado'}).reduce(function(a,v){return a+netas(v)},0);
  if(ap>0)E.push({k:'apartado',t:'Apartado ('+ap+')',c:'warn',title:'Piezas apartadas sin entregar'});
  var pubs=PUBS.filter(function(u){return u.productoId===p.id&&u.estatus==='publicado'}),vistos={};
  pubs.forEach(function(u){var pl=PLATS.filter(function(q){return q.id===u.plataformaId})[0];if(!pl||vistos[pl.id])return;vistos[pl.id]=1;
    var cod=pl.codigo||'',n=cod.indexOf('facebook')===0?'Facebook':cod==='whatsapp'?'WhatsApp':cod.indexOf('ml_')===0?'Mercado Libre':pl.nombre;
    E.push({k:'publicado',t:'En '+n,c:'info',title:'Publicado en '+pl.nombre})});
  if(!pubs.length&&d>0)E.push({k:'sinpub',t:'Sin publicar',c:'gris'});
  if(!imgPrincipal(p.id))E.push({k:'sinfoto',t:'Sin foto',c:'warn',title:'Agrega una foto: se vende mejor'});
  if(po<=0)E.push({k:'sinprecio',t:'Sin precio',c:'warn',title:x.ok&&x.sugerido?'Sugerido: '+money(x.sugerido):'Captura tu precio'});
  else if(x.ok&&x.minimo&&po<x.minimo)E.push({k:'bajo',t:'Bajo el mínimo',c:'bad',title:'Tu precio ('+money(po)+') está debajo del mínimo ('+money(x.minimo)+')'});
  return E;
}
function renderInv(s){
  renderValor(s);
  var bajo=+s.c.stockBajo||0;
  var cq=0,cc=0,est=false;s.P.forEach(function(p){var x=PRECIOS[p.id]||{},q=+p.cantidad||0;if(x.ok&&x.costo){cq+=q;cc+=q*x.costo;if(x.origenCosto!=='real')est=true}});
  document.getElementById('invSub').textContent=s.P.length?num(s.P.length)+' juguetes · '+num(s.piezas)+' piezas · '+num(s.disp)+' disponibles'+(cq?' · costo promedio '+money(cc/cq)+' por pieza'+(est?' (estimado)':''):''):'';
  var body=document.getElementById('invBody'),ley=document.getElementById('invLeyenda');
  document.getElementById('invCards').setAttribute('aria-pressed',INV_VISTA==='tarjetas'?'true':'false');
  document.getElementById('invTabla').setAttribute('aria-pressed',INV_VISTA==='tabla'?'true':'false');
  if(!s.P.length){ley.innerHTML='';body.innerHTML='<div class="empty"><h3>Abran el pallet y cuenten</h3><p class="muted">Registren cada juguete con su cantidad, estado y precios. Si ya tienen la hoja en Excel, cópienla y péguenla aquí.</p><div class="row" style="justify-content:center"><button class="btn" data-act="importar">Importar desde Excel</button><button class="btn primary" data-act="nuevoProducto">+ Agregar juguete</button></div></div>';return}
  var q=document.getElementById('fBuscar').value.trim().toLowerCase(),fc=document.getElementById('fClase').value,fe=document.getElementById('fCond').value,fp=document.getElementById('fPallet').value,fs=document.getElementById('fStock').checked;
  fillPallets('fPallet','Todos los pallets');
  var base=s.P.filter(function(p){
    var d=(+p.cantidad||0)-(s.sold[p.id]||0);
    if(q&&((p.codigo||'')+' '+(p.codigoBarras||'')+' '+(p.nombre||'')+' '+(p.categoria||'')+' '+(p.caracteristicas||'')).toLowerCase().indexOf(q)<0)return false;
    if(fc&&p.clase!==fc)return false;if(fs&&d<=0)return false;
    if(fe&&!lotesDe(p.id).some(function(l){return l.condicion===fe&&l.cantidad>0}))return false;
    if(fp&&!lotesDe(p.id).some(function(l){return l.palletId===fp}))return false;return true;
  }).sort(function(a,b){var ka=(a.clase||'Z'),kb=(b.clase||'Z');if(ka!==kb)return ka<kb?-1:1;return String(a.codigo||'').localeCompare(String(b.codigo||''),'es',{numeric:true})})
  .map(function(p){var d=Math.max(0,(+p.cantidad||0)-(s.sold[p.id]||0));return {p:p,d:d,E:etiquetasDe(p,d,bajo)}});
  // Leyenda: cada etiqueta con su conteo; un clic filtra por ella
  var cuenta={};base.forEach(function(r){var k={};r.E.forEach(function(e){k[e.k]=1});Object.keys(k).forEach(function(x){cuenta[x]=(cuenta[x]||0)+1})});
  if(INV_ETQ&&!cuenta[INV_ETQ])INV_ETQ='';
  ley.innerHTML='<span>Filtrar por etiqueta:</span><button type="button" class="etq gris" data-etq="" aria-pressed="'+(!INV_ETQ)+'">Todos · '+base.length+'</button>'+
    ETQ_DEF.filter(function(e){return cuenta[e[0]]}).map(function(e){return '<button type="button" class="etq '+e[2]+'" data-etq="'+e[0]+'" aria-pressed="'+(INV_ETQ===e[0])+'">'+esc(e[1])+' · '+cuenta[e[0]]+'</button>'}).join('');
  var R=INV_ETQ?base.filter(function(r){return r.E.some(function(e){return e.k===INV_ETQ})}):base;
  if(!R.length){body.innerHTML='<div class="empty"><p class="muted">Ningún juguete coincide con los filtros.</p></div>';return}
  function acciones(p,d,cls){return '<button class="btn '+cls+'" type="button" data-publicar="'+p.id+'"'+(d<=0?' disabled':'')+' title="Texto e imágenes listos para Facebook Marketplace y WhatsApp">Publicar</button><button class="btn '+cls+'" type="button" data-sell="'+p.id+'"'+(d<=0?' disabled':'')+'>Vender</button><button class="btn '+cls+'" type="button" data-editp="'+p.id+'">Editar</button>'}
  function etqs(E){return '<div class="etqs">'+E.map(function(e){return etq(e.t,e.c,e.title)}).join('')+'</div>'}
  if(INV_VISTA==='tarjetas'){
    body.innerHTML='<div class="inv-grid">'+R.map(function(r){var p=r.p,d=r.d,x=PRECIOS[p.id]||{},im=imgPrincipal(p.id),po=+p.precioObjetivo||0;
      return '<article class="jc'+(d<=0?' agotado':'')+'">'+
        '<button class="jc-foto" type="button" data-det="'+p.id+'" aria-label="Ver ficha de '+esc(p.nombre)+'">'+(im?'<img src="'+esc(im.url)+'" alt="" loading="lazy">':'<span class="sinfoto">Sin foto</span>')+
          '<span class="jc-disp">'+num(d)+' de '+num(+p.cantidad||0)+' disp.</span></button>'+
        '<div class="jc-body"><button class="jc-nom" type="button" data-det="'+p.id+'">'+esc(p.nombre)+'</button>'+
          '<span class="jc-sub">'+esc([p.codigo,p.categoria,condicionesDe(p.id)].filter(Boolean).join(' · '))+'</span>'+
          '<div class="jc-precio">'+(po>0?'<b>'+money(po)+'</b><span>tu precio</span>':(x.ok&&x.sugerido?'<b>'+money(x.sugerido)+'</b><span>sugerido</span>':'<b class="zero">—</b><span>sin precio</span>'))+'</div>'+
          '<span class="jc-sub">'+(x.ok?'Mínimo <b>'+money(x.minimo)+'</b> · costo '+money2(x.costo)+(x.origenCosto==='real'?'':' (est.)'):esc(x.falta||'Sin datos para calcular el mínimo'))+'</span>'+
          etqs(r.E)+
          '<div class="jc-acc">'+acciones(p,d,'sm')+'</div></div></article>'}).join('')+'</div>';
    return;
  }
  var td=0,tob=0;
  var rows=R.map(function(r){var p=r.p,d=r.d,x=PRECIOS[p.id]||{},im=imgPrincipal(p.id),po=+p.precioObjetivo||0;td+=d;tob+=d*po;
    return '<tr><td class="tdimg">'+(im?'<button class="minib" type="button" data-det="'+p.id+'" aria-label="Ver detalle de '+esc(p.nombre)+'"><img class="mini" src="'+esc(im.url)+'" alt="" loading="lazy"></button>':'<span class="mini vacio" aria-hidden="true"></span>')+'</td>'+
      '<td class="wrap"><button class="lnk nom" type="button" data-det="'+p.id+'">'+esc(p.nombre)+'</button><span class="sub">'+esc([p.codigo,p.categoria].filter(Boolean).join(' · '))+'</span></td>'+
      '<td class="wrap">'+etqs(r.E)+'</td>'+
      '<td class="r">'+num(d)+' <span class="sub">de '+num(+p.cantidad||0)+'</span></td>'+
      '<td class="r">'+(x.ok?money2(x.costo)+(x.origenCosto==='real'?'':' <span class="tag-w" title="Costo estimado: calcula el costeo del pallet para el real">est.</span>'):'<span class="zero" title="'+esc(x.falta||'Falta información')+'">—</span>')+'</td>'+
      '<td class="r">'+(x.ok?money(x.minimo):'<span class="zero">—</span>')+'</td>'+
      '<td class="r"><b>'+(po>0?money(po):'<span class="zero">—</span>')+'</b></td>'+
      '<td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap">'+acciones(p,d,'sm')+'</div></td></tr>'}).join('');
  body.innerHTML='<div class="scroll"><table><thead><tr><th><span class="sr">Imagen</span></th><th>Juguete</th><th>Etiquetas</th><th class="r">Disponibles</th><th class="r" title="Tu parte de la inversión por pieza: real con el costeo del pallet; «est.» = estimado">Costo pieza</th><th class="r" title="Lo menos que debes aceptar (costo + margen mínimo)">Mínimo</th><th class="r">Tu precio</th><th></th></tr></thead><tbody>'+rows+
    '</tbody><tfoot><tr class="tot"><td colspan="3">'+R.length+' juguete'+(R.length>1?'s':'')+' en la vista</td><td class="r">'+num(td)+'</td><td colspan="2" class="r small">Disponible × tu precio</td><td class="r">'+money(tob)+'</td><td></td></tr></tfoot></table></div>';
}
document.getElementById('invLeyenda').addEventListener('click',function(e){var b=e.target.closest('[data-etq]');if(!b)return;INV_ETQ=b.dataset.etq;renderInv(stats())});
['invCards','invTabla'].forEach(function(id){document.getElementById(id).addEventListener('click',function(){INV_VISTA=id==='invTabla'?'tabla':'tarjetas';try{localStorage.setItem(LKEY+'-invvista',INV_VISTA)}catch(e){}renderInv(stats())})});

/* ---- ventas ---- */
var vMode='board';
document.getElementById('vBoard').addEventListener('click',function(){vMode='board';this.setAttribute('aria-pressed','true');document.getElementById('vList').setAttribute('aria-pressed','false');renderVentas(stats())});
document.getElementById('vList').addEventListener('click',function(){vMode='list';this.setAttribute('aria-pressed','true');document.getElementById('vBoard').setAttribute('aria-pressed','false');renderVentas(stats())});
['vBuscar','vCanal'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){renderVentas(stats())})});
function fillSel(id,opts,first){var el=document.getElementById(id),cur=el.value;el.innerHTML='<option value="">'+first+'</option>'+opts.map(function(o){return '<option>'+esc(o)+'</option>'}).join('');el.value=cur}
function pname(id){var p=D.productos[id];return p?p.nombre:'(juguete eliminado)'}
function renderVentas(s){
  fillSel('vCanal',(function(){var k={};s.V.forEach(function(v){var c=canalDe(v);if(c)k[c]=1});return Object.keys(k).sort()})(),'Todos los canales');
  document.getElementById('venSub').textContent=s.V.length?num(s.V.length)+' registros · cobrado '+money(s.cobrado)+' · por cobrar '+money(s.porCobrar):'';
  var body=document.getElementById('venBody');
  if(!s.V.length){body.innerHTML='<div class="empty"><h3>Sin ventas todavía</h3><p class="muted">Cuando un cliente aparte un juguete, regístrenlo aquí como “Apartado”. Al ver el abono, márquenlo “Pagado” y al entregarlo, “Entregado”.</p><button class="btn primary" data-act="nuevaVenta"'+(s.P.length?'':' disabled')+'>+ Registrar venta</button>'+(s.P.length?'':'<p class="small muted">Primero agreguen juguetes al inventario.</p>')+'</div>';return}
  var q=document.getElementById('vBuscar').value.trim().toLowerCase(),fc=document.getElementById('vCanal').value;
  var V=s.V.filter(function(v){
    if(q&&((v.cliente||'')+' '+pname(v.productoId)+' '+(v.notas||'')+' '+folio(v)).toLowerCase().indexOf(q)<0)return false;
    if(fc&&canalDe(v)!==fc)return false;return true;
  }).sort(function(a,b){return (b.fecha||'')<(a.fecha||'')?-1:(b.fecha||'')>(a.fecha||'')?1:(b.creado||0)-(a.creado||0)});
  if(vMode==='board'){
    body.innerHTML='<div class="board">'+ESTADOS_V.map(function(e){
      var it=V.filter(function(v){return v.estado===e}),tot=it.reduce(function(a,v){return a+totalVenta(v)},0);
      if(e==='cancelado'&&!it.length)return '';
      var cards=it.slice(0,60).map(function(v){
        var next=e==='apartado'?['pagado','Marcar pagado']:e==='pagado'?['entregado','Marcar entregado']:null;
        var r=res(v),cob=e==='cancelado'?'':cobrada(v)?'<span class="pill entregado">Cobrada</span>':'<span class="pill apartado">Sin cobro</span>';
        return '<div class="sale"><div class="t"><span>'+esc(pname(v.productoId))+(+v.cantidad>1?' ×'+num(+v.cantidad):'')+'</span><b>'+money(totalVenta(v))+'</b></div>'+
          '<div class="m"><span>'+esc(folio(v))+'</span><span>'+esc(fdate(v.fecha))+'</span>'+(v.cliente?'<span>'+esc(v.cliente)+'</span>':'')+'<span>'+esc(canalDe(v))+'</span>'+'</div>'+
          '<div class="m">'+cob+(r.utilidad!=null?'<span class="'+(r.utilidad<0?'tone-bad':'')+'">Utilidad '+money(r.utilidad)+'</span>':'')+(+v.cantidadDevuelta?'<span>'+num(+v.cantidadDevuelta)+' devuelta(s)</span>':'')+'</div>'+
          '<div class="a">'+(next?'<button class="btn sm primary" data-adv="'+v.id+'" data-to="'+next[0]+'">'+next[1]+'</button>':'')+(e!=='cancelado'&&!cobrada(v)?'<button class="btn sm" data-cobro="'+v.id+'">Registrar cobro</button>':'')+'<button class="btn sm" data-editv="'+v.id+'">Detalle</button></div></div>'}).join('');
      return '<div class="col"><h3>'+EST_V_TXT[e]+' <span>'+it.length+' · '+money(tot)+'</span></h3>'+(cards||'<p class="empty-col">Nada aquí.</p>')+(it.length>60?'<p class="empty-col">Y '+(it.length-60)+' más en la vista de lista.</p>':'')+'</div>'}).join('')+'</div>';
  }else{
    var rows=V.map(function(v){var r=res(v);return '<tr><td>'+esc(folio(v))+'</td><td>'+esc(fdate(v.fecha))+'</td><td class="wrap"><b>'+esc(pname(v.productoId))+'</b>'+(v.notas?'<span class="sub">'+esc(v.notas)+'</span>':'')+'</td><td>'+esc(v.cliente)+'</td><td>'+esc(canalDe(v))+'</td><td><span class="pill '+v.estado+'">'+EST_V_TXT[v.estado]+'</span></td><td class="r">'+num(+v.cantidad)+(+v.cantidadDevuelta?'<span class="sub">'+num(+v.cantidadDevuelta)+' dev.</span>':'')+'</td><td class="r"><b>'+money(totalVenta(v))+'</b></td><td class="r'+(r.utilidad<0?' tone-bad':'')+'">'+money(r.utilidad)+'</td><td>'+(v.estado==='cancelado'?'':cobrada(v)?'<span class="pill entregado">Cobrada</span>':'<span class="pill apartado">Sin cobro</span>')+'</td><td class="r"><button class="btn sm" data-editv="'+v.id+'">Detalle</button></td></tr>'}).join('');
    body.innerHTML=V.length?'<div class="scroll"><table><thead><tr><th>Folio</th><th>Fecha</th><th>Juguete</th><th>Cliente</th><th>Canal</th><th>Estado</th><th class="r">Cant.</th><th class="r">Total</th><th class="r">Utilidad</th><th>Cobro</th><th></th></tr></thead><tbody>'+rows+'</tbody></table></div>':'<div class="empty"><p class="muted">Ninguna venta coincide con los filtros.</p></div>';
  }
  // por canal
  // Por canal: utilidad real de las ventas no canceladas (cobradas o no) y lo efectivamente cobrado.
  var by={};s.V.forEach(function(v){if(v.estado==='cancelado')return;var k=canalDe(v)||'Sin canal';by[k]=by[k]||[0,0,0];by[k][0]+=netas(v);by[k][1]+=+res(v).utilidad||0;by[k][2]+=cobrada(v)?+v.importeCobrado||0:0});
  var ks=Object.keys(by).sort(function(a,b){return by[b][1]-by[a][1]});
  if(ks.length)body.insertAdjacentHTML('beforeend','<div class="card"><h3>Qué canal rinde más</h3><div class="scroll" style="border:0"><table><thead><tr><th>Canal</th><th class="r">Piezas</th><th class="r">Utilidad real</th><th class="r">Utilidad por pieza</th><th class="r">Cobrado</th></tr></thead><tbody>'+ks.map(function(k){return '<tr><td>'+esc(k)+'</td><td class="r">'+num(by[k][0])+'</td><td class="r">'+money(by[k][1])+'</td><td class="r">'+(by[k][0]?money(by[k][1]/by[k][0]):'—')+'</td><td class="r">'+money(by[k][2])+'</td></tr>'}).join('')+'</tbody></table></div><p class="small muted">Detalle por plataforma, juguete y pallet en Análisis comercial › Rentabilidad real.</p></div>');
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
function weekStats(w,s){var end=addDays(w,7),n=0,pz=0,din=0;s.V.forEach(function(v){var f=v.fechaCobro||v.fechaPago||v.fecha;if(cobrada(v)&&f>=w&&f<end){n++;pz+=netas(v);din+=+v.importeCobrado||0}});
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
['cInv','cBajo','cMeta'].forEach(function(id){document.getElementById(id).addEventListener('input',function(){cfgDirty=true})});
function renderAjustes(s){
  if(cfgDirty)return;var c=s.c;
  var ci=document.getElementById('cInv'),hayP=palletsLista().length>0;ci.value=c.inversion;ci.readOnly=hayP;
  document.getElementById('cInvH').textContent=hayP?'Es la suma de la inversión de tus pallets; se modifica en Análisis comercial › Pallets y costos.':'Incluye flete e impuestos de compra.';
  document.getElementById('cBajo').value=c.stockBajo;document.getElementById('cMeta').value=c.metaMensual||'';
}
document.getElementById('cfgForm').addEventListener('submit',function(e){e.preventDefault();
  var c={inversion:Math.max(0,+document.getElementById('cInv').value||0),
    stockBajo:Math.max(0,+document.getElementById('cBajo').value||0),metaMensual:Math.max(0,+document.getElementById('cMeta').value||0)};
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
  // Inventario por lotes: un juguete nuevo nace en un pallet y una condición; con varios lotes, la cantidad se maneja en cada lote.
  var ls=id?lotesDe(id):[],activos=ls.filter(function(l){return l.cantidad>0}),multi=activos.length>1,loteU=activos[0]||ls[0]||null;
  var pls=palletsLista(),palDef=pre.palletId||(pls.length===1?pls[0].id:'');
  var bloqueInv=!id
    ?(pls.length?fld('pPal','Pallet de origen','<select class="sel" id="pPal" name="palletId" required>'+(palDef?'':'<option value="" selected disabled>Elige el pallet</option>')+optPallets(palDef)+'</select>')
       :'<div class="full"><p class="warnline bad">Registra primero un pallet en <b>Análisis comercial › Pallets y costos</b>; cada juguete pertenece a un pallet.</p></div>')+
     fld('pCond','Condición','<select class="sel" id="pCond" name="condicion">'+optConds('nuevo_sellado')+'</select>')
    :multi?'<div class="full"><p class="small muted">Este juguete tiene <b>'+activos.length+' lotes</b> ('+esc(condicionesDe(id))+'). La cantidad y la condición se manejan en cada lote: ficha del juguete › Lotes.</p></div>'
    :fld('pCond','Condición','<select class="sel" id="pCond" name="condicion">'+optConds(loteU?loteU.condicion:'nuevo_sellado')+'</select>',loteU&&PALLETS[loteU.palletId]?'Pallet: '+esc(PALLETS[loteU.palletId].nombre):null);
  function dec(v){return v!=null&&v!==''?esc(v):''}
  var hayCom=id&&(p.marca||p.modelo||p.subcategoria||p.descripcionComercial||p.accesoriosIncluidos||p.edadRecomendada||p.pesoKg||p.largoCm);
  var bloqueCom='<details class="full com" id="pCom"'+(hayCom?' open':'')+'><summary>Datos comerciales <span class="small muted">(marca, modelo, medidas, edad…)</span></summary><div class="form">'+
    fld('pMar','Marca','<input class="inp" id="pMar" name="marca" maxlength="60" value="'+esc(p.marca)+'">')+
    fld('pMod','Modelo','<input class="inp" id="pMod" name="modelo" maxlength="60" value="'+esc(p.modelo)+'">')+
    fld('pSub','Subcategoría','<input class="inp" id="pSub" name="subcategoria" maxlength="60" value="'+esc(p.subcategoria)+'">')+
    fld('pEdad','Edad recomendada','<input class="inp" id="pEdad" name="edadRecomendada" maxlength="40" value="'+esc(p.edadRecomendada)+'" placeholder="p. ej. 3+ años">')+
    fld('pPeso','Peso (kg)','<input class="inp num" id="pPeso" name="pesoKg" type="number" min="0" step="0.001" value="'+dec(p.pesoKg)+'">')+
    fld('pLar','Largo (cm)','<input class="inp num" id="pLar" name="largoCm" type="number" min="0" step="0.01" value="'+dec(p.largoCm)+'">')+
    fld('pAnc','Ancho (cm)','<input class="inp num" id="pAnc" name="anchoCm" type="number" min="0" step="0.01" value="'+dec(p.anchoCm)+'">')+
    fld('pAlt','Alto (cm)','<input class="inp num" id="pAlt" name="altoCm" type="number" min="0" step="0.01" value="'+dec(p.altoCm)+'">')+
    fld('pAcc','Accesorios incluidos','<input class="inp" id="pAcc" name="accesoriosIncluidos" maxlength="300" value="'+esc(p.accesoriosIncluidos)+'">',null,true)+
    fld('pDes','Descripción comercial','<textarea class="inp" id="pDes" name="descripcionComercial" maxlength="2000" rows="3" placeholder="Texto para publicar">'+esc(p.descripcionComercial)+'</textarea>',null,true)+
    fld('pEcom','Estado comercial','<select class="sel" id="pEcom" name="estadoComercial">'+opt(Object.keys(EST_COM),p.estadoComercial||'en_revision',EST_COM)+'</select>')+
    fld('pFin','Fecha de ingreso','<input class="inp" id="pFin" name="fechaIngreso" type="date" value="'+esc(p.fechaIngreso||(id?'':today()))+'">')+
    '</div></details>';
  var G={pend:[],upc:[],usarUpc:true,valorRef:null};   // fotos por subir e imágenes de UPCitemdb por descargar al guardar
  openModal(head(id?'Editar juguete':'Nuevo juguete')+'<form class="stack" style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('pCod','Código','<input class="inp mono" id="pCod" name="codigo" required maxlength="20" value="'+esc(p.codigo)+'">')+
    fld('pCla','Clase','<select class="sel" id="pCla" name="clase">'+opt(['A','B','C'],p.clase,{A:'A · Muy buscado o caro',B:'B · Demanda normal',C:'C · Barato, para combos'})+'</select>')+
    fld('pBar','Código de barras','<div class="bc"><input class="inp mono" id="pBar" name="codigoBarras" maxlength="20" inputmode="numeric" autocomplete="off" value="'+esc(p.codigoBarras)+'" placeholder="Escanea o escribe el código"><button class="btn" type="button" id="pBarBtn">Buscar</button>'+(window.PCCam?PCCam.boton():'')+'</div>','Con el lector: escanea aquí y se busca solo. Con el celular: «Cámara» y toma la foto del código. A mano: escribe el código y presiona Enter.',true)+
    '<div class="full" id="pBarRes"></div>'+
    fld('pNom','Juguete','<input class="inp" id="pNom" name="nombre" required maxlength="120" value="'+esc(p.nombre)+'" placeholder="Marca y nombre del juguete">',null,true)+
    fld('pCat','Categoría','<input class="inp" id="pCat" name="categoria" list="catList" maxlength="60" value="'+esc(p.categoria)+'" placeholder="Muñecas, autos, didácticos…"><datalist id="catList">'+Object.keys(cats).map(function(c){return '<option value="'+esc(c)+'">'}).join('')+'</datalist>')+
    bloqueInv+
    fld('pCan','Cantidad','<input class="inp num" id="pCan" name="cantidad" type="number" min="'+vend+'" step="1" required value="'+esc(p.cantidad!=null?p.cantidad:1)+'"'+(multi?' readonly':'')+'>',vend?'Ya hay '+vend+' vendidas o apartadas.':'Piezas de este juguete en el pallet.')+
    fld('pMx','Precio en México','<input class="inp num" id="pMx" name="precioMexico" type="number" min="0" step="0.01" value="'+esc(p.precioMexico||'')+'">','<span id="pMxH"></span>')+
    camposTiendas(p)+
    '<div class="full" id="pMxSug"></div>'+
    fld('pObj','Precio objetivo','<input class="inp num" id="pObj" name="precioObjetivo" type="number" min="0" step="1" value="'+esc(p.precioObjetivo||'')+'">','Lo que esperas cobrar. Publica un poco arriba para negociar.')+
    fld('pMin','Precio mínimo','<input class="inp num" id="pMin" name="precioMinimo" type="number" min="0" step="1" value="'+esc(p.precioMinimo||'')+'">','No bajar de aquí al regatear.')+
    fld('pLis','Precio de lista (referencia)','<div class="row" style="flex-wrap:nowrap"><select class="sel" id="pLisMon" aria-label="Moneda del precio de lista" style="max-width:96px"><option value="MXN"'+(+p.precioListaUsd>0?'':' selected')+'>MXN</option><option value="USD"'+(+p.precioListaUsd>0?' selected':'')+'>USD</option></select>'+
      '<input class="inp num" id="pLis" name="precioLista" type="number" min="0" step="0.01" value="'+esc(+p.precioListaUsd>0?p.precioListaUsd:(p.precioLista||''))+'"></div>','<span id="pLisH"></span>',true)+
    fld('pCar','Características','<textarea class="inp" id="pCar" name="caracteristicas" maxlength="2000" rows="5" placeholder="Descripción, marca, modelo, color, medidas, peso, edad recomendada…">'+esc(p.caracteristicas)+'</textarea><div class="row"><button class="btn sm" type="button" id="pTrad">Traducir al español</button><span class="small muted" id="pTradMsg"></span></div>',null,true)+
    '<div class="f full"><span class="lbl2">Imágenes</span><div class="gal" id="pGal"></div>'+
      '<div class="row"><label class="btn sm" for="pFiles" tabindex="0">Subir fotos</label><input type="file" id="pFiles" accept="image/jpeg,image/png,image/webp,image/gif" multiple hidden></div>'+
      '<span class="hint">JPG, PNG, WEBP o GIF de hasta 5 MB, hasta 12 por juguete. La marcada con ★ es la principal y se muestra en el inventario.</span></div>'+
    bloqueCom+
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
      // Precio de lista: siempre se guarda en pesos. En USD se convierte con el tipo de cambio del día (el servidor hace la conversión final).
      var lisMon=el.querySelector('#pLisMon'),lisIn=el.querySelector('#pLis'),lisH=el.querySelector('#pLisH');
      function lisHint(){var v=+lisIn.value||0,tc=+cfg().tipoCambioUsd||0;
        if(lisMon.value!=='USD'){lisH.textContent='En pesos mexicanos. Si lo tienes en dólares, elige USD y se convierte solo.';return}
        if(!(tc>0)){lisH.innerHTML='<span class="tone-bad">No hay tipo de cambio del día: defínelo arriba para convertir, o captura el precio en pesos.</span>';return}
        if(+p.precioListaUsd>0&&v===+p.precioListaUsd&&+p.precioListaTipoCambio>0){lisH.textContent='Guardado en pesos: '+money(+p.precioLista)+' (US$'+v.toFixed(2)+' × '+(+p.precioListaTipoCambio).toFixed(4)+', tipo de cambio con que se convirtió).';return}
        lisH.textContent=v>0?'Se guardará en pesos: '+money(Math.round(v*tc))+' (US$'+v.toFixed(2)+' × '+tc.toFixed(4)+', tipo de cambio de hoy).':'Captura el precio en dólares; se guardará convertido a pesos.'}
      lisMon.addEventListener('change',lisHint);lisIn.addEventListener('input',lisHint);el.addEventListener('lista-moneda',lisHint);lisHint();
      wireMx(el,chk,id,G);
      wireGaleria(el,id,G);
      wireBarras(el,id,chk,pre.resultado,G);
      wireForm(el,function(f){
        var o={codigo:val(f,'codigo'),codigoBarras:val(f,'codigoBarras'),nombre:val(f,'nombre'),categoria:val(f,'categoria'),clase:f.elements.clase.value,
          cantidad:Math.max(0,Math.round(+f.elements.cantidad.value||0)),
          precioLista:f.querySelector('#pLisMon').value==='USD'?Math.round((+f.elements.precioLista.value||0)*(+cfg().tipoCambioUsd||0)):(+f.elements.precioLista.value||0),
          precioListaUsd:f.querySelector('#pLisMon').value==='USD'?(+f.elements.precioLista.value||0):0,
          precioObjetivo:+f.elements.precioObjetivo.value||0,
          precioMinimo:+f.elements.precioMinimo.value||0,precioMexico:+f.elements.precioMexico.value||0,caracteristicas:val(f,'caracteristicas'),notas:val(f,'notas'),
          precioLiverpool:+f.elements.precioLiverpool.value||0,precioMercadoLibre:+f.elements.precioMercadoLibre.value||0,precioWalmart:+f.elements.precioWalmart.value||0,
          precioJuguetron:+f.elements.precioJuguetron.value||0,precioJuguetibici:+f.elements.precioJuguetibici.value||0,precioAmazon:+f.elements.precioAmazon.value||0,
          marca:val(f,'marca'),modelo:val(f,'modelo'),subcategoria:val(f,'subcategoria'),edadRecomendada:val(f,'edadRecomendada'),
          accesoriosIncluidos:val(f,'accesoriosIncluidos'),descripcionComercial:val(f,'descripcionComercial'),estadoComercial:f.elements.estadoComercial.value,
          pesoKg:+f.elements.pesoKg.value||0,largoCm:+f.elements.largoCm.value||0,anchoCm:+f.elements.anchoCm.value||0,altoCm:+f.elements.altoCm.value||0,
          fechaIngreso:f.elements.fechaIngreso.value||null};
        if(f.elements.palletId)o.palletId=f.elements.palletId.value||null;
        if(G.valorRef>0){o.valorReferencia=G.valorRef;o.valorReferenciaOrigen='upcitemdb'}
        if(f.elements.condicion)o.condicion=f.elements.condicion.value;
        if(!id&&!o.palletId){w.innerHTML='<p class="warnline bad">Elige el pallet de origen.</p>';return}
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
/** Costo por pieza: el real del costeo del pallet; si aún no hay costeo, la estimación proporcional anterior. */
function costoDe(p,s){
  var cr=costoReal(p.id);if(isFinite(cr.unit))return cr.unit;
  if(+p.precioMexico>0&&isFinite(s.factor))return +p.precioMexico*s.factor;
  return NaN;
}
var TIENDAS=[
  {k:'precioLiverpool',id:'pLv',n:'Liverpool',url:function(q){return 'https://www.liverpool.com.mx/tienda?s='+encodeURIComponent(q)}},
  {k:'precioMercadoLibre',id:'pMl',n:'Mercado Libre',url:function(q){return 'https://listado.mercadolibre.com.mx/'+encodeURIComponent(q.trim().replace(/\s+/g,'-'))}},
  {k:'precioWalmart',id:'pWm',n:'Walmart',url:function(q){return 'https://www.walmart.com.mx/search?q='+encodeURIComponent(q)}},
  // Juguetron (VTEX): búsqueda verificada el 2026-11-20 con /<término>?_q=<término>&map=ft
  {k:'precioJuguetron',id:'pJt',n:'Juguetron',url:function(q){var t=encodeURIComponent(q.trim());return 'https://www.juguetron.mx/'+t+'?_q='+t+'&map=ft'}},
  // Juguetibici no tiene tienda en línea propia verificada (su catálogo está en Rappi): se busca en Google.
  {k:'precioJuguetibici',id:'pJb',n:'Juguetibici',via:'Google',url:function(q){return 'https://www.google.com/search?q='+encodeURIComponent('Juguetibici '+q.trim())}},
  {k:'precioAmazon',id:'pAz',n:'Amazon México',url:function(q){return 'https://www.amazon.com.mx/s?k='+encodeURIComponent(q.trim())}}
];
function promedioTiendas(vals){var v=vals.filter(function(x){return x>0});return v.length?Math.round(v.reduce(function(a,b){return a+b},0)/v.length*100)/100:null}
function camposTiendas(p){
  return '<div class="f full"><span class="lbl2">Precio en tiendas de México</span><div class="tiendas">'+
    TIENDAS.map(function(t){return '<div class="tienda"><label for="'+t.id+'">'+t.n+'</label><div class="bc"><input class="inp num" id="'+t.id+'" name="'+t.k+'" type="number" min="0" step="1" value="'+esc(p[t.k]||'')+'" placeholder="$">'+
      '<a class="btn sm" id="'+t.id+'L" href="#" target="_blank" rel="noopener noreferrer" title="'+(t.via?'Buscar '+t.n+' en '+t.via:'Buscar en '+t.n)+' (se abre en otra pestaña)">'+(t.via?'En '+t.via:'Buscar')+' ↗</a></div></div>'}).join('')+
    '</div><span class="hint">“Buscar” abre la tienda con el nombre del juguete (Juguetibici se busca en Google porque no tiene tienda en línea propia). Captura el precio que veas; el Precio en México es el promedio de las tiendas capturadas.</span></div>';
}
function wireMx(el,chk,id,G){
  var mxI=el.querySelector('#pMx'),box=el.querySelector('#pMxSug'),ob=el.querySelector('#pObj'),mn=el.querySelector('#pMin'),cant=el.querySelector('#pCan'),nom=el.querySelector('#pNom');
  var p0=id?D.productos[id]:null;if(p0&&+p0.valorReferencia>0&&G&&!G.valorRef)G.valorRef=+p0.valorReferencia;
  function vacioOAuto(c){return !String(c.value).trim()||+c.value===0||c.dataset.auto}
  function poner(c,v){if(v==null)return;c.value=v;c.dataset.auto='1'}
  function enlaces(){var q=nom.value.trim();TIENDAS.forEach(function(t){var a=el.querySelector('#'+t.id+'L');if(q){a.href=t.url(q);a.removeAttribute('aria-disabled')}else{a.href='#';a.setAttribute('aria-disabled','true')}})}
  function promedio(){
    var vals=TIENDAS.map(function(t){return +el.querySelector('#'+t.id).value||0}),pr=promedioTiendas(vals),n=vals.filter(function(x){return x>0}).length;
    var h=el.querySelector('#pMxH');
    if(pr!=null){mxI.value=pr;mxI.readOnly=true;h.textContent='Promedio de '+n+' tienda'+(n>1?'s':'')+'. Para capturarlo a mano, borra los precios por tienda.'}
    else{mxI.readOnly=false;h.textContent='Sin precios por tienda: puedes capturarlo a mano. Si lo dejas vacío, se usan los precios de EE. UU. del código de barras.'}
  }
  function v(n){var c=el.querySelector('[name="'+n+'"]');return c?(+c.value||0):0}
  // Precio sugerido y mínimo: referencia de mercado → costo por pieza (real o estimado) → motor de precios (venta directa).
  var t=null,pide=0;
  function pinta(auto){clearTimeout(t);t=setTimeout(function(){if(box.isConnected)pedir(auto)},300)}
  function pedir(auto){
    var pal=el.querySelector('#pPal'),k=++pide;
    var body={productoId:id||null,palletId:pal?(pal.value||null):null,cantidad:Math.max(1,Math.round(+cant.value||1)),precioMexico:+mxI.value||0,
      valorReferencia:G&&G.valorRef>0?G.valorRef:null,categoria:(el.querySelector('#pCat')||{}).value||'',
      pesoKg:v('pesoKg')||null,largoCm:v('largoCm')||null,anchoCm:v('anchoCm')||null,altoCm:v('altoCm')||null};
    if(!(body.precioMexico>0)&&!(body.valorReferencia>0)&&!id){box.innerHTML='';return}
    api('POST','productos/sugerir-precio',body).then(function(r){if(k!==pide)return;pintaSug(r,auto)},function(){if(k===pide)box.innerHTML=''});
  }
  function pintaSug(r,auto){
    if(!box.isConnected)return;   // el formulario ya se cerró
    var av=(r.avisos||[]).map(function(x){return '<p class="warnline">'+esc(x)+'</p>'}).join('');
    if(!r.ok){box.innerHTML=av?'<div class="sugbox"><p class="small"><b>Precio sugerido</b></p>'+av+'</div>':'';chk();return}
    var c=r.costo,e=c.estimacion,ca=r.canal,aplicados=[];
    if(auto){if(vacioOAuto(ob)){poner(ob,r.precioObjetivo);aplicados.push('objetivo')}if(vacioOAuto(mn)){poner(mn,r.precioMinimo);aplicados.push('mínimo')}}
    chk();
    var notas=[
      'Referencia de mercado: '+money2(r.referencia)+' por pieza ('+(r.origenReferencia==='precio_mexico'?'precio en México':'promedio de precios en EE. UU. del código de barras, convertido a pesos')+').',
      c.origen==='real'?'Costo por pieza: '+money2(c.unitario)+' (costo real del costeo del pallet).'
        :'Costo por pieza estimado: inversión del pallet '+money2(e.inversion)+' × '+money2(e.valorPieza)+' ÷ '+money2(e.baseReparto)+' = '+money2(c.unitario)+' ('+(e.usaValorMercadoPallet?'valor de mercado estimado del pallet':'valor de los juguetes registrados en el pallet')+'). Se vuelve real al calcular el costeo del pallet.',
      'Canal: '+ca.plataforma+(ca.utilidad!=null?' · al precio sugerido ganas '+money2(ca.utilidad)+' ('+ca.margenPct+' % de margen)':'')+'.'];
    var otros=(r.canales||[]).filter(function(x){return x.plataformaId!==ca.plataformaId}).slice(0,3);
    box.innerHTML='<div class="sugbox"><p><b>Precio sugerido: '+money(r.precioObjetivo)+'</b> · <b>Precio mínimo: '+money(r.precioMinimo)+'</b> <span class="small muted">('+esc(ca.plataforma)+')</span></p>'+
      '<ul class="small sug">'+notas.map(function(x){return '<li>'+esc(x)+'</li>'}).join('')+'</ul>'+
      (otros.length?'<p class="small muted">Otros canales: '+otros.map(function(x){return esc(x.plataforma)+' '+money(x.sugerido)+' (mín. '+money(x.minimo)+')'}).join(' · ')+'</p>':'')+av+
      (r.fiscalPendiente?'<p class="small muted">Régimen fiscal pendiente: sin IVA ni ISR.</p>':'')+
      (aplicados.length?'<p class="small muted">Se llenó: precio '+aplicados.join(' y ')+'.</p>':'')+
      '<div class="row"><button class="btn sm" type="button" id="pMxAplicar">Usar estos precios</button></div></div>';
    box.querySelector('#pMxAplicar').addEventListener('click',function(){poner(ob,r.precioObjetivo);poner(mn,r.precioMinimo);chk();toast('Precios sugeridos aplicados')});
  }
  TIENDAS.forEach(function(t){el.querySelector('#'+t.id).addEventListener('input',function(){promedio();pinta(true)});
    el.querySelector('#'+t.id+'L').addEventListener('click',function(e){if(!nom.value.trim()){e.preventDefault();toast('Escribe primero el nombre del juguete');nom.focus()}})});
  mxI.addEventListener('input',function(){pinta(true)});
  cant.addEventListener('input',function(){pinta(true)});
  var pal=el.querySelector('#pPal');if(pal)pal.addEventListener('change',function(){pinta(true)});
  el.addEventListener('sugerir-precio',function(){pinta(true)});
  nom.addEventListener('input',enlaces);
  el.addEventListener('nombre-cambiado',enlaces);
  enlaces();promedio();pinta(false);
}

/* ---- detalle del juguete con carrusel de fotos ---- */
function detalleForm(id){
  var p=D.productos[id];if(!p)return;
  var s=stats(),imgs=imgsDe(id),vend=s.sold[id]||0,disp=Math.max(0,(+p.cantidad||0)-vend),costo=costoDe(p,s),k=0,cr=costoReal(id);
  var tablaLotes='<div><div class="pf"><h3>Lotes</h3><button class="btn sm" type="button" data-lote-nuevo="'+esc(id)+'">+ Lote</button></div>'+
    '<div class="scroll"><table><thead><tr><th>Pallet</th><th>Condición</th><th class="r">Piezas</th><th class="r">Costo pieza</th><th class="r">Costo lote</th><th></th></tr></thead><tbody>'+
    lotesDe(id).map(function(l){var c=COSTOS[l.id];return '<tr><td>'+esc((PALLETS[l.palletId]||{}).nombre||'')+'</td><td>'+esc(condNombre(l.condicion))+(l.comercializable?'':' <span class="pill danado">'+(l.tratamiento?esc(l.tratamiento.replace('_',' ')):'no comercializable')+'</span>')+'</td>'+
      '<td class="r">'+num(l.cantidad)+'</td><td class="r">'+(c?money2(c.costoUnitario):'—')+'</td><td class="r">'+(c?money2(c.costoTotal):'—')+'</td>'+
      '<td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap"><button class="btn sm" type="button" data-lote-aj="'+esc(l.id)+'">Ajustar</button><button class="btn sm" type="button" data-lote-rc="'+esc(l.id)+'"'+(l.cantidad?'':' disabled')+'>Condición</button><button class="btn sm" type="button" data-lote-ed="'+esc(l.id)+'">Editar</button></div></td></tr>'}).join('')+
    '</tbody></table></div><p class="small muted"><button class="lnk" type="button" data-lote-mov="'+esc(id)+'">Ver movimientos (kárdex)</button></p></div>';
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
      fila('Clase',(p.clase?'<span class="pill '+p.clase+'">'+p.clase+'</span> ':'')+'<span class="small muted">'+esc(EST_COM[p.estadoComercial]||'')+'</span>')+
      fila('Marca y modelo',esc([p.marca,p.modelo].filter(Boolean).join(' · ')))+
      fila('Edad y medidas',esc([p.edadRecomendada,p.pesoKg?num(+p.pesoKg)+' kg':'',p.largoCm?[p.largoCm,p.anchoCm,p.altoCm].map(function(x){return x?num(+x):'?'}).join(' × ')+' cm':''].filter(Boolean).join(' · ')))+
      fila('Costo real por pieza',isFinite(cr.unit)?'<b>'+money2(cr.unit)+'</b>'+(cr.desactualizado?' <span class="tag-w">costeo desactualizado</span>':cr.completo?'':' <span class="tag-w">hay lotes sin costear</span>'):'<span class="small muted">Pendiente: calcula el costeo del pallet</span>')+
      fila('Piezas',num(+p.cantidad||0)+' en total · '+num(vend)+' vendidas o apartadas · <b>'+num(disp)+' disponibles</b>'+(function(){var nv=lotesDe(id).filter(function(l){return !l.comercializable}).reduce(function(a,l){return a+l.cantidad},0);return nv?' <span class="small muted">(incluye '+num(nv)+' no vendible'+(nv>1?'s':'')+')</span>':''})())+
      fila('Precio en México','<b>'+pm(p.precioMexico)+'</b><ul class="small sug">'+tiendas+'</ul>')+
      fila('Precio objetivo',pm(p.precioObjetivo))+
      fila('Precio mínimo',pm(p.precioMinimo)+(!isFinite(cr.unit)&&isFinite(costo)?' <span class="small muted">· estimación sin costeo: '+money(costo)+'</span>':''))+
      (+p.precioLista>0?fila('Precio de lista (ref.)',money(+p.precioLista)+(+p.precioListaUsd>0?' <span class="small muted">(US$'+(+p.precioListaUsd).toFixed(2)+' × '+(+p.precioListaTipoCambio).toFixed(4)+')</span>':'')):'')+
      fila('Valor en México',+p.precioMexico>0?money((+p.cantidad||0)*(+p.precioMexico))+' <span class="small muted">('+num(disp)+' disponibles: '+money(disp*(+p.precioMexico))+')</span>':'')+
    '</dl></div>'+
    tablaLotes+
    '<div id="detRent"></div><div id="detComp"></div>'+
    (p.descripcionComercial?'<div><h3>Descripción comercial</h3><p class="pre small">'+esc(p.descripcionComercial)+'</p></div>':'')+
    (p.accesoriosIncluidos?'<div><h3>Accesorios incluidos</h3><p class="small">'+esc(p.accesoriosIncluidos)+'</p></div>':'')+
    (p.caracteristicas?'<div><h3>Características</h3><p class="pre small">'+esc(p.caracteristicas)+'</p></div>':'')+
    (p.notas?'<div><h3>Notas</h3><p class="pre small">'+esc(p.notas)+'</p></div>':'')+
    '<div class="pf"><span></span><div class="row"><button class="btn" type="button" data-publicar="'+esc(id)+'"'+(disp<=0?' disabled':'')+'>Preparar publicación</button><button class="btn" type="button" data-sell="'+esc(id)+'"'+(disp<=0?' disabled':'')+'>Vender</button><button class="btn primary" type="button" data-editp="'+esc(id)+'">Editar</button></div></div>',
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
    if(window.PCP)window.PCP.fichaRentabilidad(id,el.querySelector('#detRent'));
    if(window.PCC)window.PCC.fichaCompetencia(id,el.querySelector('#detComp'));
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
/** Botones para buscar el código en la web cuando ninguna base lo tiene (búsquedas verificadas el 2026-11-20). */
function buscarCodigoEnWeb(code){if(!code)return '';var q=encodeURIComponent(code);
  return '<p class="small">Búscalo por su código: <a class="btn sm" href="https://www.google.com/search?q='+q+'" target="_blank" rel="noopener noreferrer">Google ↗</a> '+
    '<a class="btn sm" href="https://www.amazon.com.mx/s?k='+q+'" target="_blank" rel="noopener noreferrer">Amazon México ↗</a> y copia el nombre y el precio que encuentres.</p>'}
function fdt(iso){try{return new Date(iso).toLocaleString('es-MX',{day:'numeric',month:'short',year:'numeric',hour:'2-digit',minute:'2-digit'})}catch(e){return ''}}
function wireBarras(el,id,chk,inicial,G){
  var inp=el.querySelector('#pBar'),btn=el.querySelector('#pBarBtn'),res=el.querySelector('#pBarRes');
  if(window.PCCam&&res)PCCam.conectar(el.querySelector('.bc'),function(c){inp.value=c;btn.click()},function(h){res.innerHTML=h});
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
    if(r.origen==='ninguno'){res.innerHTML='<p class="warnline">'+esc(r.aviso)+'</p>'+buscarCodigoEnWeb(r.codigo);return}
    var fuenteN=r.origen==='openproductsfacts'?'Open Products Facts':'UPCitemdb';
    var e=r.externo,sg=r.sugerencia||{},tieneMx=+campo('precioMexico').value>0;
    var nombre=e.marca&&e.titulo.toLowerCase().indexOf(e.marca.toLowerCase())<0?e.marca+' '+e.titulo:e.titulo;
    var datos={nombre:nombre,categoria:e.categoria,caracteristicas:e.caracteristicasEs||e.caracteristicas,precioLista:sg.precioLista,
      marca:e.marca,modelo:e.modelo,pesoKg:e.pesoKg,largoCm:e.largoCm,anchoCm:e.anchoCm,altoCm:e.altoCm};
    if(e.marca||e.modelo||e.pesoKg||e.largoCm){var dc=el.querySelector('#pCom');if(dc)dc.open=true}
    // El precio de lista se llena con el monto original en dólares (selector USD); se guarda convertido a pesos.
    var lisC=campo('precioLista'),lisM=el.querySelector('#pLisMon');
    if(sg.precioListaUsd&&lisC&&lisM&&(!String(lisC.value).trim()||+lisC.value===0||lisC.dataset.auto)){lisM.value='USD';datos.precioLista=sg.precioListaUsd}
    // Referencia de mercado (promedio de precios de EE. UU. × tipo de cambio): base del precio sugerido y del costo estimado.
    if(sg.valorReferencia>0)G.valorRef=+sg.valorReferencia;
    var llenos=aplicar(datos,false);el.dispatchEvent(new Event('nombre-cambiado'));el.dispatchEvent(new Event('lista-moneda'));el.dispatchEvent(new Event('sugerir-precio'));
    G.upc=(e.imagenes&&e.imagenes.length?e.imagenes:(e.imagen?[e.imagen]:[])).slice(0,6);G.render();
    var rango=(e.precioBajo!=null||e.precioAlto!=null)?'Precios registrados en tiendas de EE. UU.: '+(e.moneda==='USD'?'US$':e.moneda+' ')+[e.precioBajo,e.precioAlto].filter(function(x){return x!=null}).map(function(x){return (+x).toFixed(2)}).join(' – ')+(e.ofertas?' · '+e.ofertas+' oferta'+(e.ofertas>1?'s':''):''):'Sin precios registrados.';
    res.innerHTML='<div class="bcres">'+(e.imagen?'<img class="bcimg" src="'+esc(e.imagen)+'" alt="" referrerpolicy="no-referrer" onerror="this.remove()">':'')+
      '<div class="bcinfo"><p class="small"><span class="pill nuevo">'+esc(fuenteN)+'</span> <b>'+esc(e.titulo)+'</b></p>'+(r.aviso?'<p class="warnline small">'+esc(r.aviso)+'</p>':'')+
      '<p class="small muted">'+[e.marca,e.modelo,e.categoria].filter(Boolean).map(esc).join(' · ')+'</p>'+
      '<p class="small muted">'+esc(rango)+'</p>'+
      '<ul class="small sug">'+(sg.explicacion||[]).map(function(x){return '<li>'+esc(x)+'</li>'}).join('')+'</ul>'+
      (r.avisoTraduccion?'<p class="warnline">'+esc(r.avisoTraduccion)+'</p>':(e.caracteristicasEs?'<p class="small muted">Características traducidas al español con MyMemory: revísalas.</p>':''))+
      '<p class="small"><b>Busca y captura el precio en México</b> (botones “Buscar” de cada tienda) para que el objetivo y el mínimo se sugieran con él.</p>'+
      '<p class="small muted">'+(llenos.length?'Se llenaron: '+esc(llenos.join(', '))+'. ':'No se cambió ningún campo porque ya tenían datos. ')+
      (G.upc.length?'Las imágenes se guardarán en tu computadora al guardar el juguete. ':esc(fuenteN)+' no tiene imágenes de este juguete. ')+
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
    '<p class="small muted">Escaneen la caja con el lector o, en el celular, con <b>Cámara</b> (toma la foto del código). Si el juguete ya está en el inventario <b>se le suma una pieza</b> y puedes seguir escaneando; si no, se busca en UPCitemdb y se abre el alta con los datos encontrados.</p>'+
    '<div class="bc"><input class="inp mono" id="scCode" inputmode="numeric" autocomplete="off" maxlength="20" placeholder="Código de barras" style="font-size:1.2rem">'+(window.PCCam?PCCam.boton():'')+'</div>'+
    '<p class="small muted" id="scCupo"></p><div id="scRes"></div><div class="pf"><span></span><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit" id="scGo">Buscar</button></div></div></form>',function(el){
      var inp=el.querySelector('#scCode'),r=el.querySelector('#scRes'),go=el.querySelector('#scGo'),cupo=el.querySelector('#scCupo');
      function verCupo(){api('GET','codigo-barras-estado').then(function(x){if(!cupo.isConnected)return;
        cupo.innerHTML=x.agotadoHasta?'<span class="tag-w">UPCitemdb sin consultas</span> se reinicia el '+esc(new Date(x.agotadoHasta).toLocaleString('es-MX',{day:'numeric',month:'short',hour:'2-digit',minute:'2-digit',hour12:false}))+' h. Los juguetes ya registrados se siguen sumando sin consultas; los nuevos se buscan en Open Products Facts.'
          :(x.restantes!=null?'UPCitemdb: quedan <b>'+num(x.restantes)+'</b> consultas gratuitas hoy (solo gastan los códigos nuevos).':'')},function(){})}
      verCupo();
      if(window.PCCam)PCCam.conectar(el,function(c){inp.value=c;go.disabled=false;el.querySelector('form').requestSubmit()},function(h){r.innerHTML=h});
      wireForm(el,function(){
        var code=inp.value.trim();if(!code)return;go.disabled=true;r.innerHTML='<p class="small muted">Buscando…</p>';
        buscarBarras(code).then(function(res){verCupo();
          if(res.origen==='inventario')return sumarPieza(res.producto,el,inp,r,go);
          productoForm(null,{codigoBarras:res.codigo,resultado:res});
        },function(e){verCupo();go.disabled=false;r.innerHTML='<p class="warnline bad">'+esc((e&&e.mensaje)||'No se pudo buscar.')+'</p>'+buscarCodigoEnWeb(code)+'<p class="small"><button class="lnk" type="button" id="scManual">Dar de alta a mano con este código</button></p>';
          r.querySelector('#scManual').addEventListener('click',function(){productoForm(null,{codigoBarras:code})});inp.select()});
      });
    });
}

/* Código que ya está en el inventario: suma una pieza (movimiento «ajuste» en el kárdex) y deja la ventana lista para
   el siguiente escaneo, con opción de deshacer y de abrir el juguete. */
function sumarPieza(prod,el,inp,r,go){
  return api('POST','productos/'+encodeURIComponent(prod.id)+'/sumar-pieza').then(function(x){
    return cargar().then(function(){
      r.innerHTML='<div class="alert ok"><span class="dot"></span><div><b>+1 '+esc(x.nombre)+'</b><br><span class="small">Ahora hay <b>'+num(x.cantidad)+'</b> pieza'+(x.cantidad===1?'':'s')+(x.pallet?' · lote de '+esc(x.pallet):'')+'. Escanea el siguiente código.</span>'+
        '<div class="row" style="margin-top:6px"><button class="btn sm" type="button" id="scUndo">Deshacer</button><button class="btn sm" type="button" id="scOpen">Abrir juguete</button></div></div></div>';
      toast('+1 '+x.nombre+' · ahora '+x.cantidad+' piezas');
      inp.value='';go.disabled=false;inp.focus();
      r.querySelector('#scOpen').addEventListener('click',function(){productoForm(x.productoId)});
      r.querySelector('#scUndo').addEventListener('click',function(){var b=this;b.disabled=true;
        api('POST','lotes/'+encodeURIComponent(x.loteId)+'/ajuste',{cantidad:-1,motivo:'Deshacer escaneo de código de barras'}).then(cargar).then(function(){
          r.innerHTML='<p class="small muted">Se deshizo: '+esc(x.nombre)+' vuelve a '+num(x.cantidad-1)+' pieza'+(x.cantidad-1===1?'':'s')+'.</p>';toast('Escaneo deshecho');inp.focus()},
          function(e){b.disabled=false;toast((e&&e.mensaje)||'No se pudo deshacer')})});
    });
  },function(e){go.disabled=false;r.innerHTML='<p class="warnline bad">'+esc((e&&e.mensaje)||'No se pudo sumar la pieza.')+'</p><p class="small"><button class="lnk" type="button" id="scOpen2">Abrir el juguete</button></p>';
    r.querySelector('#scOpen2').addEventListener('click',function(){productoForm(prod.id)});inp.select()});
}

/* ---- borrar todos los datos (Ajustes) ---- */
function borrarTodoForm(){
  var s=stats(),nP=palletsLista().length,nV=s.V.length;
  openModal(head('Borrar todos los datos')+'<form style="display:flex;flex-direction:column;gap:14px">'+
    '<p class="warnline bad">Se borrarán <b>'+num(nP)+' pallet'+(nP===1?'':'s')+'</b>, <b>'+num(s.P.length)+' juguete'+(s.P.length===1?'':'s')+'</b> con sus fotos, lotes y costeos, <b>'+num(nV)+' venta'+(nV===1?'':'s')+'</b>, gastos, semanas, publicaciones, competencia y la bitácora.</p>'+
    '<p class="small">Se conservan: Ajustes, márgenes y situación fiscal, plataformas y los códigos de barras ya consultados (para no gastar consultas otra vez).</p>'+
    '<p class="small"><b>1.</b> Si quieres guardar una copia fuera de esta computadora, descárgala primero: <a class="btn sm" href="/api/respaldo" download>Descargar respaldo</a></p>'+
    '<p class="small"><b>2.</b> La aplicación también guarda una copia de la base y de las fotos en la carpeta <span class="mono">datos/respaldos</span> antes de borrar.</p>'+
    fld('btConf','<b>3.</b> Para confirmar escribe BORRAR','<input class="inp mono" id="btConf" autocomplete="off" autocapitalize="characters" placeholder="BORRAR">',null,true)+
    '<div id="btMsg"></div><div class="pf"><span></span><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn danger" type="submit" id="btGo" disabled>Borrar todo</button></div></div></form>',function(el){
      var inp=el.querySelector('#btConf'),bGo=el.querySelector('#btGo'),msg=el.querySelector('#btMsg');
      inp.addEventListener('input',function(){bGo.disabled=inp.value.trim()!=='BORRAR'});
      wireForm(el,function(){if(inp.value.trim()!=='BORRAR')return;bGo.disabled=true;msg.innerHTML='<p class="small muted">Borrando…</p>';
        api('POST','datos/borrar-todo',{confirmacion:inp.value.trim()}).then(function(r){
          closeModal();return cargar().then(function(){go('resumen');toast('Listo: se borraron los datos ('+num(r.fotos)+' foto'+(r.fotos===1?'':'s')+'). La copia quedó en '+r.respaldo)})},
          function(e){bGo.disabled=false;msg.innerHTML='<p class="warnline bad">'+esc((e&&e.mensaje)||'No se pudo borrar.')+'</p>'});
      });
      setTimeout(function(){inp.focus()},50);
    });
}

/* Formulario y ficha de la venta: js/ventas.js (Fase 3). */
function ventaForm(id,pid){if(window.PCV)window.PCV.ventaForm(id,pid)}
function advance(id,to){if(!D.ventas[id])return;
  // Marcar pagada no es cobrarla: se ofrece registrar el importe que llegó.
  if(to==='pagado'&&!cobrada(D.ventas[id])&&window.PCV){window.PCV.cobroForm(id,'pagado');return}
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
    (palletsLista().length?fld('impPal','Pallet de los juguetes nuevos','<select class="sel" id="impPal">'+optPallets(palletsLista().length===1?palletsLista()[0].id:'')+'</select>','Las filas cuyo código ya existe solo actualizan sus datos.'):'<p class="warnline bad">Registra primero un pallet en Análisis comercial › Pallets y costos.</p>')+
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
        var ip=el.querySelector('#impPal');
        api('POST','productos/importar'+(ip&&ip.value?'?palletId='+encodeURIComponent(ip.value):''),rows).then(function(r){return cargar().then(function(){closeModal();
          toast(r.nuevos+' nuevos · '+r.actualizados+' actualizados'+(r.cantidadNoActualizada&&r.cantidadNoActualizada.length?' · cantidad sin cambiar en '+r.cantidadNoActualizada.join(', ')+' (tienen varios lotes)':''))})})
          .catch(function(e){go.disabled=false;go.textContent='Reintentar';toast((e&&e.mensaje)||'No se pudo importar')});
      });
    });
}

/* ---- exportar ---- */
function csv(rows){return '﻿'+rows.map(function(r){return r.map(function(c){c=c==null?'':String(c);return /[",\n;]/.test(c)?'"'+c.replace(/"/g,'""')+'"':c}).join(',')}).join('\r\n')}
function exportar(k){
  var s=stats(),rows;
  if(k==='inventario')rows=[['Código','Juguete','Categoría','Cantidad','Estado','Precio de lista','Precio objetivo','Clase','Código de barras','Precio en México','Precio mínimo','Vendidas','Disponibles','Precio Liverpool','Precio Mercado Libre','Precio Walmart','Precio Juguetron','Precio Juguetibici','Precio Amazon México','Características','Notas','Pallets','Condición','Marca','Modelo','Costo real por pieza','Precio de lista en USD','Tipo de cambio del precio de lista']].concat(s.P.map(function(p){var v=s.sold[p.id]||0;return [p.codigo,p.nombre,p.categoria,p.cantidad,EST_P_TXT[p.estado]||'Nuevo',p.precioLista,p.precioObjetivo,p.clase,p.codigoBarras?'\u2060'+p.codigoBarras:'',p.precioMexico||'',p.precioMinimo,v,Math.max(0,(+p.cantidad||0)-v),p.precioLiverpool||'',p.precioMercadoLibre||'',p.precioWalmart||'',p.precioJuguetron||'',p.precioJuguetibici||'',p.precioAmazon||'',p.caracteristicas,p.notas,
    lotesDe(p.id).map(function(l){return (PALLETS[l.palletId]||{}).nombre}).filter(function(x,i,a){return x&&a.indexOf(x)===i}).join(' / '),condicionesDe(p.id),p.marca,p.modelo,(function(){var c=costoReal(p.id);return isFinite(c.unit)?c.unit.toFixed(2):''})(),p.precioListaUsd||'',p.precioListaTipoCambio||'']}));
  else if(k==='ventas')rows=[['Folio','Fecha','Fecha de pago','Código','Juguete','Cantidad','Devueltas','Precio','Descuento','Total','Estado','Canal','Cliente','Notas','Importe cobrado','Fecha de cobro','Gastos de la venta','Costo histórico','Utilidad real','Origen del costo']].concat(s.V.map(function(v){var p=D.productos[v.productoId]||{},r=res(v);return [folio(v),v.fecha,v.fechaPago,p.codigo,p.nombre,v.cantidad,v.cantidadDevuelta||0,v.precio,v.descuento||0,totalVenta(v),EST_V_TXT[v.estado],canalDe(v),v.cliente,v.notas,v.importeCobrado==null?'':v.importeCobrado,v.fechaCobro||'',r.gastosTotales,r.costoVendido,r.utilidad,v.costoOrigen]}));
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
  else if(act==='borrarTodo')borrarTodoForm();
  else if(act==='nuevaVenta'){if(!list('productos').length){toast('Primero agreguen juguetes al inventario');go('inventario')}else ventaForm()}
  else if(act==='nuevoGasto')gastoForm();
  else if(act==='nuevaSemana')semanaForm(D.semanas[monday(today())]?monday(today()):null);
  else if(act==='importar')importForm();
  else if(act==='usarSugeridos'){
    if(!confirm('Se pondrá el precio sugerido como precio objetivo (y el mínimo calculado) en los juguetes que aún no tienen precio de venta. Después puedes cambiarlos uno por uno. ¿Continuar?'))return;
    api('POST','productos/aplicar-sugeridos').then(function(r){return cargar().then(function(){toast(r.actualizados+' juguete'+(r.actualizados===1?'':'s')+' con precio sugerido')})}).catch(function(e){toast((e&&e.mensaje)||'No se pudo aplicar')});
  }
  else if(act==='verPublicaciones'){if(window.PCA){go('analisis');window.PCA.irA('publicaciones')}}
  else if(act==='escanear')escanearForm();
  else if(t.id==='tcBtn')tcForm();
  else if(t.dataset.go)go(t.dataset.go);
  else if(t.dataset.publicar){if(window.PCPub)window.PCPub.abrir(t.dataset.publicar)}
  else if(t.dataset.editp)productoForm(t.dataset.editp);
  else if(t.dataset.det)detalleForm(t.dataset.det);
  else if(t.dataset.sell)ventaForm(null,t.dataset.sell);
  else if(t.dataset.editv){if(window.PCV)window.PCV.detalle(t.dataset.editv)}
  else if(t.dataset.cobro){if(window.PCV)window.PCV.cobroForm(t.dataset.cobro)}
  else if(t.dataset.adv)advance(t.dataset.adv,t.dataset.to);
  else if(t.dataset.editg)gastoForm(t.dataset.editg);
  else if(t.dataset.edits)semanaForm(t.dataset.edits);
  else if(t.dataset.exp)exportar(t.dataset.exp);
  else if(t.id==='userBtn')menuUsuario();
  else if(window.PCA&&(t.dataset.loteNuevo||t.dataset.loteAj||t.dataset.loteRc||t.dataset.loteEd||t.dataset.loteMov))window.PCA.loteAccion(t.dataset);
});

var tt=null;function toast(m){var el=document.getElementById('toast');el.textContent=m;el.hidden=false;clearTimeout(tt);tt=setTimeout(function(){el.hidden=true},2600)}

/* ================= acceso ================= */
var acceso=document.getElementById('acceso');
function mostrarAcceso(){
  SESION=null;closeModal();
  api('GET','auth/estado').then(function(e){
    if(e.autenticado){SESION=e;acceso.hidden=true;return}
    var cfgInicial=e.requiereConfiguracion;
    acceso.hidden=false;
    acceso.innerHTML='<form class="panel acc" id="accForm" autocomplete="on"><div class="brand"><span class="logo" aria-hidden="true"><i></i><i></i><i></i><i></i></span><b>Control Pallet de Juguetes</b></div>'+
      (cfgInicial
        ?(e.configuracionPermitida
          ?'<h2>Crea tu usuario</h2><p class="small muted">Es la primera vez que se abre la aplicación. Este usuario protegerá tu información financiera.</p>'+
            fld('aNom','Tu nombre','<input class="inp" id="aNom" name="nombreCompleto" maxlength="80" autocomplete="name">')+
            fld('aUsu','Usuario','<input class="inp" id="aUsu" name="usuario" required minlength="3" maxlength="40" autocomplete="username" pattern="[A-Za-z0-9._\\-]+">','Letras, números, punto, guion o guion bajo.')+
            fld('aCon','Contraseña','<input class="inp" id="aCon" name="contrasena" type="password" required minlength="8" autocomplete="new-password">','Al menos 8 caracteres.')+
            fld('aCon2','Repite la contraseña','<input class="inp" id="aCon2" type="password" required minlength="8" autocomplete="new-password">')
          :'<h2>Configuración pendiente</h2><p class="warnline">El primer usuario solo se puede crear desde la computadora donde corre la aplicación. Ábrela ahí en <b>http://localhost:5080</b>.</p>')
        :'<h2>Inicia sesión</h2>'+
          fld('aUsu','Usuario','<input class="inp" id="aUsu" name="usuario" required autocomplete="username">')+
          fld('aCon','Contraseña','<input class="inp" id="aCon" name="contrasena" type="password" required autocomplete="current-password">'))+
      '<div id="aMsg"></div>'+(cfgInicial&&!e.configuracionPermitida?'':'<button class="btn primary" type="submit">'+(cfgInicial?'Crear usuario y entrar':'Entrar')+'</button>')+'</form>';
    var f=acceso.querySelector('#accForm'),msg=acceso.querySelector('#aMsg');var u=acceso.querySelector('#aUsu');if(u)u.focus();
    f.addEventListener('submit',function(ev){ev.preventDefault();
      var b={usuario:f.elements.usuario.value.trim(),contrasena:f.elements.contrasena.value};
      if(cfgInicial){if(b.contrasena!==acceso.querySelector('#aCon2').value){msg.innerHTML='<p class="warnline bad">Las contraseñas no coinciden.</p>';return}b.nombreCompleto=f.elements.nombreCompleto.value.trim()}
      var btn=f.querySelector('button[type=submit]');btn.disabled=true;msg.innerHTML='';
      api('POST',cfgInicial?'auth/configurar':'auth/entrar',b).then(iniciarSesion,function(er){btn.disabled=false;msg.innerHTML='<p class="warnline bad">'+esc(er.mensaje)+'</p>'});
    });
  },function(er){acceso.hidden=false;acceso.innerHTML='<div class="panel acc"><p class="warnline bad">'+esc(er.mensaje)+'</p></div>'});
}
function iniciarSesion(){
  return api('GET','auth/estado').then(function(e){
    if(!e.autenticado){mostrarAcceso();return}
    SESION=e;acceso.hidden=true;acceso.innerHTML='';
    document.getElementById('userBtn').textContent=e.nombreCompleto||e.usuario;document.getElementById('userBtn').hidden=!!e.sinLogin;
    return cargar();
  });
}
function menuUsuario(){
  openModal(head('Tu cuenta')+'<div style="display:flex;flex-direction:column;gap:14px">'+
    '<p class="small muted">Sesión iniciada como <b>'+esc(SESION?SESION.usuario:'')+'</b>.</p>'+
    '<form class="card" id="uPass"><h3>Cambiar contraseña</h3><div class="form">'+
      fld('uAct','Contraseña actual','<input class="inp" id="uAct" name="actual" type="password" required autocomplete="current-password">')+
      fld('uNue','Nueva contraseña','<input class="inp" id="uNue" name="nueva" type="password" required minlength="8" autocomplete="new-password">','Al menos 8 caracteres. Se cerrarán tus otras sesiones.')+
    '</div><div class="row"><button class="btn" type="submit">Cambiar</button><span class="small" id="uPassMsg"></span></div></form>'+
    '<div class="card"><h3>Usuarios</h3><div id="uLista" class="small muted">Cargando…</div>'+
    '<form id="uNuevo" class="form">'+fld('nUsu','Usuario nuevo','<input class="inp" id="nUsu" name="usuario" required minlength="3" maxlength="40" pattern="[A-Za-z0-9._\\-]+">')+
      fld('nNom','Nombre','<input class="inp" id="nNom" name="nombreCompleto" maxlength="80">')+
      fld('nCon','Contraseña','<input class="inp" id="nCon" name="contrasena" type="password" required minlength="8" autocomplete="new-password">')+
      '<div class="f" style="justify-content:flex-end"><button class="btn" type="submit">Agregar usuario</button></div></form><span class="small" id="uNuevoMsg"></span></div>'+
    '<div class="pf"><span></span><button class="btn danger" type="button" id="uSalir">Cerrar sesión</button></div></div>',function(el){
      function lista(){api('GET','usuarios').then(function(us){el.querySelector('#uLista').innerHTML='<ul class="clean">'+us.map(function(u){
        return '<li><b>'+esc(u.nombre)+'</b> '+esc(u.nombreCompleto||'')+(u.activo?'':' <span class="pill cancelado">inactivo</span>')+
          (SESION&&u.nombre!==SESION.usuario?' <button class="lnk" type="button" data-uact="'+esc(u.id)+'" data-val="'+(!u.activo)+'">'+(u.activo?'Desactivar':'Activar')+'</button>':'')+'</li>'}).join('')+'</ul>'})}
      lista();
      el.querySelector('#uLista').addEventListener('click',function(e){var b=e.target.closest('[data-uact]');if(!b)return;
        api('POST','usuarios/'+b.dataset.uact+'/activo?activo='+b.dataset.val).then(lista,function(er){toast(er.mensaje)})});
      el.querySelector('#uPass').addEventListener('submit',function(e){e.preventDefault();var m=el.querySelector('#uPassMsg');
        api('POST','auth/contrasena',{actual:this.elements.actual.value,nueva:this.elements.nueva.value}).then(function(){m.textContent='Contraseña cambiada.';e.target.reset()},function(er){m.textContent=er.mensaje})});
      el.querySelector('#uNuevo').addEventListener('submit',function(e){e.preventDefault();var m=el.querySelector('#uNuevoMsg'),fm=this;
        api('POST','usuarios',{usuario:fm.elements.usuario.value.trim(),nombreCompleto:fm.elements.nombreCompleto.value.trim(),contrasena:fm.elements.contrasena.value})
          .then(function(){m.textContent='Usuario agregado.';fm.reset();lista()},function(er){m.textContent=er.mensaje})});
      el.querySelector('#uSalir').addEventListener('click',function(){api('POST','auth/salir').then(function(){closeModal();document.getElementById('userBtn').hidden=true;mostrarAcceso()})});
    });
}

/* Funciones compartidas con js/analisis.js (módulo de Análisis comercial). */
window.PC={api:api,go:go,cargar:function(){return cargar()},openModal:openModal,closeModal:closeModal,head:head,fld:fld,opt:opt,footer:footer,wireForm:wireForm,
  toast:toast,money:money,money2:money2,num:num,pct:pct,esc:esc,fdate:fdate,kpi:kpi,today:today,nf4:nf4,cfg:cfg,TC:function(){return TC},
  productoForm:productoForm,detalleForm:detalleForm,condNombre:condNombre,optConds:optConds,optPallets:optPallets,palletsLista:palletsLista,
  datos:function(){return {D:D,LOTES:LOTES,PALLETS:PALLETS,COSTOS:COSTOS,CORRIDAS:CORRIDAS,CONDS:CONDS,RES:RES,VDET:VDET,PUBS:PUBS,PLATS:PLATS,PRECIOS:PRECIOS}},
  imgsDe:imgsDe,lotesDe:lotesDe,
  stats:stats,vendidasDe:vendidasDe,pname:pname,folio:folio,canalDe:canalDe,cobrada:cobrada,totalVenta:totalVenta,netas:netas,costoDe:costoDe,
  ESTADOS_V:ESTADOS_V,EST_V_TXT:EST_V_TXT,CANALES:function(){return CANALES},val:val};

/* ================= arranque ================= */
var start=(location.hash||'').slice(1);if(VALID.indexOf(start)<0){try{start=localStorage.getItem(LKEY+'-tab')||''}catch(e){start=''}}
go(start||'resumen');setStatus();
iniciarSesion().catch(function(e){if(e&&e.status!==401)toast(e.mensaje)});vigilar();
})();
