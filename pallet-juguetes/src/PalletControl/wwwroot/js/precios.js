/* Análisis comercial — Fase 2: Márgenes (sin tarifas por plataforma, sin IVA ni promoción), Análisis de productos y Simulador de precios.
   Todos los cálculos los hace el servidor (motor de precios). Esta capa solo pide, muestra y captura. */
(function(){
'use strict';
var PC=window.PC,interm=PC.precioIntermediario,esc=PC.esc,money=PC.money,money2=PC.money2,num=PC.num,kpi=PC.kpi,fld=PC.fld,head=PC.head;
var ESTADO={rentable:['entregado','Rentable'],margen_bajo:['apartado','Margen bajo'],bajo_minimo:['apartado','Bajo el mínimo'],perdida:['danado','Pérdida'],
  no_rentable:['danado','No rentable'],sin_costo:['C','Sin costo'],sin_tarifa:['C','Sin tarifa'],sin_objetivo:['C','Sin objetivo']};
var ESQ={margen:'Margen sobre precio',markup:'Markup sobre costo',utilidad_fija:'Utilidad fija ($)'};
var RED={terminacion9:'Terminación 9',diez:'Múltiplo de 10',peso:'Al peso',centavo:'Al centavo'};
var REG={pendiente:'Pendiente de definir',resico:'RESICO',plataformas_tecnologicas:'Plataformas tecnológicas',actividad_empresarial:'Actividad empresarial',persona_moral:'Persona moral'};
var TIPO={marketplace:'Marketplace',local:'Venta local',fisica:'Venta física'};
function pill(e){var x=ESTADO[e]||['C',e];return '<span class="pill '+x[0]+'">'+esc(x[1])+'</span>'}
function err(e){PC.toast((e&&e.mensaje)||'No se pudo completar la operación.')}
function pct(v){return v==null?'—':(+v).toLocaleString('es-MX',{maximumFractionDigits:2})+' %'}
function m(v){return v==null?'—':money2(v)}
var avisoFiscal='';   // sin IVA: lo que costó el pallet ya lo incluye (decisión del usuario, 2026-11-23)

var st={prod:{q:'',categoria:'',palletId:'',estado:'',orden:'codigo',desc:false,pagina:1,tam:25,fecha:''},sim:{},plat:{sel:null},pide:0};
var cuerpo=null;

function reiniciar(vista,op){if(vista==='simulador'&&op&&op.productoId){st.sim={productoId:op.productoId,plataformaId:op.plataformaId||''}}}
function render(vista,body){cuerpo=body;var n=++st.pide;
  if(vista==='productos')return vistaProductos(n);
  if(vista==='simulador')return vistaSimulador(n);
  return vistaPlataformas(n);
}

/* ================= Análisis de productos ================= */
function categorias(){var c={};var D=PC.datos().D;Object.keys(D.productos).forEach(function(k){var x=D.productos[k].categoria;if(x)c[x]=1});return Object.keys(c).sort()}
function vistaProductos(n){
  var f=st.prod,qs=['tam='+f.tam,'pagina='+f.pagina,'orden='+f.orden,'desc='+f.desc];
  ['q','categoria','palletId','estado','fecha'].forEach(function(k){if(f[k])qs.push(k+'='+encodeURIComponent(f[k]))});
  PC.api('GET','rentabilidad/productos?'+qs.join('&')).then(function(r){if(n!==st.pide)return;pintaProductos(r)},err);
}
function pintaProductos(r){
  var f=st.prod,pags=Math.max(1,Math.ceil(r.total/r.tam));
  var filas=r.filas.map(function(x){var p=x.producto,mej=x.canales.filter(function(c){return c.plataformaId===x.mejorCanal})[0];
    var nal=x.canales.reduce(function(a,c){return a+(c.alertas||[]).length},0);
    return '<tr><td class="wrap"><button class="lnk nom" type="button" data-rent="'+esc(p.id)+'">'+esc(p.nombre)+'</button><span class="sub">'+esc(p.codigo+(p.categoria?' · '+p.categoria:''))+'</span></td>'+
      '<td class="r">'+(x.costo.unitario!=null?money2(x.costo.unitario)+(x.costo.desactualizado?' <span class="tag-w" title="Costeo desactualizado">!</span>':''):'<span class="zero">—</span>')+'</td>'+
      '<td class="r">'+(p.precioMexico>0?money2(p.precioMexico):'<span class="zero">—</span>')+'</td>'+
      '<td>'+(mej?esc(mej.plataforma):'<span class="zero">—</span>')+'</td>'+
      '<td class="r">'+(mej?'<b>'+money2(mej.alReferencia.utilidad)+'</b><span class="sub">'+pct(mej.alReferencia.margenPct)+' a '+money2(mej.precioReferencia)+'</span>':'—')+'</td>'+
      '<td class="r">'+(mej?money2(mej.sugerido):'—')+'</td><td class="r">'+(mej?money2(mej.minimo):'—')+'</td>'+
      '<td>'+pill(x.estado)+(nal?' <span class="small muted">'+nal+' alerta'+(nal>1?'s':'')+'</span>':'')+'</td></tr>'}).join('');
  var est=Object.keys(ESTADO).map(function(k){return '<option value="'+k+'"'+(f.estado===k?' selected':'')+'>'+esc(ESTADO[k][1])+'</option>'}).join('');
  cuerpo.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Análisis de productos</h2>'+
      '<p class="muted small">Precio sugerido, mínimo y utilidad de cada juguete en el canal que más deja, vendiendo al precio de referencia (precio en México; si no hay, tu precio objetivo).</p></div></div>'+
    (r.fiscalPendiente?avisoFiscal:'')+
    '<div class="grid g4">'+kpi('Rentables',num(r.resumen.rentables),'A precio de referencia alcanzan el objetivo','tone-good')+kpi('Margen bajo',num(r.resumen.margenBajo),'Entre el mínimo y el objetivo, o debajo del mínimo')+
      kpi('Con pérdida',num(r.resumen.perdida),'No recuperan costo y gastos',r.resumen.perdida?'tone-bad':'')+kpi('Sin datos',num(r.resumen.sinDatos),'Falta costeo, tarifa u objetivo')+'</div>'+
    '<div class="filters"><input class="inp" id="rq" type="search" placeholder="Buscar juguete, código, marca…" value="'+esc(f.q)+'" aria-label="Buscar">'+
      '<select class="sel" id="rcat" aria-label="Categoría"><option value="">Todas las categorías</option>'+categorias().map(function(c){return '<option'+(c===f.categoria?' selected':'')+'>'+esc(c)+'</option>'}).join('')+'</select>'+
      '<select class="sel" id="rpal" aria-label="Pallet"><option value="">Todos los pallets</option>'+PC.optPallets(f.palletId)+'</select>'+
      '<select class="sel" id="rest" aria-label="Estado"><option value="">Todo estado</option>'+est+'</select>'+
      '<select class="sel" id="rord" aria-label="Ordenar">'+PC.opt(['codigo','utilidad','margen','costo'],f.orden,{codigo:'Ordenar por código',utilidad:'Por utilidad',margen:'Por margen',costo:'Por costo'})+'</select>'+
      '<button class="btn sm" type="button" id="rdir" aria-label="Invertir orden">'+(f.desc?'↓ Mayor a menor':'↑ Menor a mayor')+'</button>'+
      '<input class="inp" id="rfec" type="date" value="'+esc(f.fecha)+'" aria-label="Fecha de las tarifas" title="Fecha de las tarifas (vacío = hoy)" style="flex:0 1 160px"></div>'+
    (r.filas.length?'<div class="scroll"><table><thead><tr><th>Juguete</th><th class="r">Costo pieza</th><th class="r">P. México</th><th>Mejor canal</th><th class="r">Utilidad</th><th class="r">Sugerido</th><th class="r">Mínimo</th><th>Estado</th></tr></thead><tbody>'+filas+'</tbody></table></div>'+
      '<div class="row pag"><button class="btn sm" type="button" id="rprev"'+(f.pagina<=1?' disabled':'')+'>← Anterior</button><span class="small muted">Página '+f.pagina+' de '+pags+' · '+num(r.total)+' juguetes</span><button class="btn sm" type="button" id="rnext"'+(f.pagina>=pags?' disabled':'')+'>Siguiente →</button></div>'
     :'<div class="empty"><p class="muted">Ningún juguete coincide con los filtros.</p></div>');
  var t=null;
  function cambia(k,v){f[k]=v;f.pagina=1;vistaProductos(++st.pide)}
  cuerpo.querySelector('#rq').addEventListener('input',function(){var v=this.value;clearTimeout(t);t=setTimeout(function(){cambia('q',v)},300)});
  cuerpo.querySelector('#rcat').addEventListener('change',function(){cambia('categoria',this.value)});
  cuerpo.querySelector('#rpal').addEventListener('change',function(){cambia('palletId',this.value)});
  cuerpo.querySelector('#rest').addEventListener('change',function(){cambia('estado',this.value)});
  cuerpo.querySelector('#rord').addEventListener('change',function(){cambia('orden',this.value)});
  cuerpo.querySelector('#rdir').addEventListener('click',function(){cambia('desc',!f.desc)});
  cuerpo.querySelector('#rfec').addEventListener('change',function(){cambia('fecha',this.value)});
  var pv=cuerpo.querySelector('#rprev'),nx=cuerpo.querySelector('#rnext');
  if(pv)pv.addEventListener('click',function(){f.pagina--;vistaProductos(++st.pide)});
  if(nx)nx.addEventListener('click',function(){f.pagina++;vistaProductos(++st.pide)});
  cuerpo.querySelectorAll('[data-rent]').forEach(function(b){b.addEventListener('click',function(){detalle(b.dataset.rent,f.fecha)})});
}

/* Tabla de canales de un juguete (detalle y ficha). */
function tablaCanales(r,compacta){
  var ok=r.canales.filter(function(c){return c.ok}),no=r.canales.filter(function(c){return !c.ok});
  var h=ok.length?'<div class="scroll"><table><thead><tr><th>Canal</th>'+(compacta?'':'<th class="r">Equilibrio</th>')+'<th class="r">Mínimo</th>'+(compacta?'':'<th class="r">Objetivo</th>')+'<th class="r">Sugerido</th>'+(compacta?'':'<th class="r">Promocional</th><th class="r">Comisión</th><th class="r">Gastos</th>')+
    '<th class="r">Utilidad</th><th class="r">Margen</th><th class="r">A '+money2(ok[0].precioReferencia)+'</th><th>Estado</th></tr></thead><tbody>'+
    ok.map(function(c){var d=c.alSugerido;return '<tr'+(c.plataformaId===r.mejorCanal?' class="mejor"':'')+'><td>'+esc(c.plataforma)+(c.plataformaId===r.mejorCanal?' <span class="pill entregado">Mejor</span>':'')+(compacta?'':'<span class="sub">'+esc(c.tarifa.nombre)+' · objetivo '+esc(c.parametro.ambito)+'</span>')+'</td>'+
      (compacta?'':'<td class="r">'+m(c.equilibrio)+'</td>')+'<td class="r">'+m(c.minimo)+'</td>'+(compacta?'':'<td class="r">'+m(c.objetivo)+'</td>')+'<td class="r"><b>'+m(c.sugerido)+'</b></td>'+
      (compacta?'':'<td class="r">'+m(c.promocional)+(c.promocionalLimitado?'<span class="sub">limitado al mínimo</span>':'')+'</td><td class="r">'+m(d.comision)+'</td><td class="r">'+m(d.cargosPlataforma+d.gastosPropios)+'</td>')+
      '<td class="r">'+m(d.utilidad)+'</td><td class="r">'+pct(d.margenPct)+'</td><td class="r '+(c.alReferencia.utilidad<0?'tone-bad':'')+'">'+m(c.alReferencia.utilidad)+'</td><td>'+pill(c.estado)+'</td></tr>'}).join('')+'</tbody></table></div>'
    :'<p class="small muted">Ningún canal tiene precio calculable todavía.</p>';
  if(no.length)h+='<details class="com"><summary>'+no.length+' canal'+(no.length>1?'es':'')+' sin cálculo</summary><ul class="clean small">'+no.map(function(c){return '<li><b>'+esc(c.plataforma)+':</b> '+esc(c.error)+'</li>'}).join('')+'</ul></details>';
  return h;
}
function alertasDe(r){var a=[];r.canales.forEach(function(c){(c.alertas||[]).forEach(function(x){a.push('<li class="'+(x.nivel==='error'?'tone-bad':'')+'"><b>'+esc(c.plataforma)+':</b> '+esc(x.mensaje)+'</li>')})});
  return a.length?'<div class="card"><h3>Alertas al precio que planeas publicar</h3><ul class="clean small">'+a.join('')+'</ul></div>':''}

function detalle(id,fecha){
  PC.api('GET','rentabilidad/productos/'+id+(fecha?'?fecha='+fecha:'')).then(function(r){var p=r.producto;
    PC.openModal(head('Rentabilidad · '+esc(p.nombre))+'<span class="rentdet" hidden></span>'+
      (r.fiscalPendiente?avisoFiscal:'')+
      '<div class="grid g4">'+kpi('Costo real por pieza',r.costo.unitario!=null?money2(r.costo.unitario):'—',r.costo.desactualizado?'Costeo desactualizado: recalcúlalo':r.costo.completo?'Del costeo vigente del pallet':'Hay lotes sin costear')+
        kpi('Precio en México',p.precioMexico>0?money2(p.precioMexico):'—','Precio de referencia del mercado')+kpi('Tu precio objetivo',p.precioObjetivo>0?money2(p.precioObjetivo):'—','Se usa para las alertas')+
        kpi('Mejor canal',esc((r.canales.filter(function(c){return c.plataformaId===r.mejorCanal})[0]||{}).plataforma||'—'),'Más utilidad al precio de referencia')+'</div>'+
      tablaCanales(r,false)+alertasDe(r)+
      '<p class="small muted">Utilidad = precio sin IVA − comisiones − cargos de la plataforma − tus gastos − costo. Las retenciones no son gasto; se ven en el simulador. Nunca se sugiere un precio por debajo del mínimo.</p>'+
      '<div class="pf"><button class="btn" type="button" id="rdHist">Historial de precios guardados</button><div class="row"><button class="btn" type="button" id="rdSim">Simular</button><button class="btn primary" type="button" id="rdGuardar">Guardar como precios recomendados</button></div></div><div id="rdH"></div>',
    function(el){
      el.querySelector('#rdSim').addEventListener('click',function(){PC.closeModal();PC.go('analisis');window.PCA.irA('simulador',{productoId:id,plataformaId:r.mejorCanal})});
      el.querySelector('#rdGuardar').addEventListener('click',function(){var b=this;b.disabled=true;
        PC.api('POST','rentabilidad/productos/'+id+'/guardar'+(fecha?'?fecha='+fecha:'')).then(function(x){PC.toast(x.guardados+' precio'+(x.guardados>1?'s':'')+' guardado'+(x.guardados>1?'s':''));b.disabled=false},function(e){b.disabled=false;err(e)})});
      el.querySelector('#rdHist').addEventListener('click',function(){PC.api('GET','rentabilidad/productos/'+id+'/historial').then(function(h){
        el.querySelector('#rdH').innerHTML=h.length?'<div class="scroll"><table><thead><tr><th>Fecha</th><th>Canal</th><th class="r">Costo</th><th class="r">Mínimo</th><th class="r">Sugerido</th><th class="r">Utilidad</th><th class="r">Margen</th><th>Usuario</th><th></th></tr></thead><tbody>'+
          h.map(function(x){var p=x.precio;return '<tr><td>'+esc(new Date(p.creadoUtc).toLocaleString('es-MX'))+'</td><td>'+esc(x.plataforma)+'</td><td class="r">'+m(p.costo)+'</td><td class="r">'+m(p.minimo)+'</td><td class="r">'+m(p.sugerido)+'</td><td class="r">'+m(p.utilidad)+'</td><td class="r">'+pct(p.margenPct)+'</td><td>'+esc(p.usuario)+'</td><td>'+(p.vigente?'<span class="pill entregado">Vigente</span>':'')+'</td></tr>'}).join('')+'</tbody></table></div>'
          :'<p class="small muted">Sin precios guardados.</p>'},err)});
    });
  },err);
}

/* Sección compacta para la ficha del juguete. */
function fichaRentabilidad(id,el){
  if(!el)return;el.innerHTML='<p class="small muted">Calculando rentabilidad…</p>';
  PC.api('GET','rentabilidad/productos/'+id).then(function(r){
    el.innerHTML='<div class="pf"><h3>Rentabilidad por canal</h3><button class="lnk" type="button" id="frVer">Ver análisis completo</button></div>'+
      (r.costo.unitario==null?'<p class="small muted">Calcula el costeo de su pallet para ver precios por canal.</p>':tablaCanales(r,true));
    el.querySelector('#frVer').addEventListener('click',function(){detalle(id)});
  },function(e){el.innerHTML='<p class="small muted">'+esc(e.mensaje)+'</p>'});
}

/* ================= Simulador ================= */
var plats=null,opciones={tiposPublicacion:[],modalidades:[],mediosPago:[]};
/* Simulador sencillo: solo el juguete. ¿En cuánto venderlo y si gano o pierdo?
   Usa el costo por pieza (real con el costeo del pallet o estimado) y el mínimo y sugerido de la aplicación
   (venta directa: costo + margen). No usa tarifas de plataformas, peso, categoría ni tipo de publicación. */
function vistaSimulador(n){
  var s=st.sim,D=PC.datos().D,PR=PC.datos().PRECIOS||{};
  var prods=Object.keys(D.productos).map(function(k){return Object.assign({id:k},D.productos[k])}).sort(function(a,b){return String(a.codigo).localeCompare(String(b.codigo),'es',{numeric:true})});
  if(!prods.length){cuerpo.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Simulador de precios</h2></div></div><div class="empty"><p class="muted">Registra juguetes para simular su precio.</p></div>';return}
  if(!s.productoId||!D.productos[s.productoId])s.productoId=prods[0].id;
  if(!Object.keys(PR).length){cuerpo.innerHTML='<p class="small muted">Calculando precios…</p>';setTimeout(function(){if(n===st.pide)vistaSimulador(n)},700);return}
  cuerpo.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Simulador de precios</h2><p class="muted small">Elige un juguete y prueba un precio: te dice en cuánto venderlo y si ganas o pierdes dinero.</p></div></div>'+
    '<form class="card" id="simF"><div class="form">'+
      fld('sProd','Juguete','<select class="sel" id="sProd" name="productoId">'+prods.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===s.productoId?' selected':'')+'>'+esc(p.codigo+' · '+p.nombre)+'</option>'}).join('')+'</select>',null,true)+
      fld('sPre','¿En cuánto lo venderías? (por pieza)','<input class="inp num" id="sPre" name="precio" type="number" min="0" step="1" inputmode="decimal">','Vacío = precio sugerido.')+
      fld('sCan','Piezas','<input class="inp num" id="sCan" name="cantidad" type="number" min="1" step="1" value="'+esc(s.cantidad||1)+'">')+
    '</div></form><div id="simR"></div>';
  var f=cuerpo.querySelector('#simF'),sel=f.elements.productoId,pre=f.elements.precio,can=f.elements.cantidad;
  function datos(){var p=D.productos[sel.value]||{},x=PR[sel.value]||{};return {p:p,x:x}}
  function porDefecto(){var d=datos();pre.value=s.precio&&s.productoId===sel.value?s.precio:(d.x.ok&&d.x.sugerido?d.x.sugerido:(+d.p.precioObjetivo||''))}
  function calcular(){s.productoId=sel.value;s.precio=pre.value;s.cantidad=can.value;pintaSimulacion(datos(),+pre.value||0,Math.max(1,Math.floor(+can.value||1)))}
  sel.addEventListener('change',function(){s.precio='';porDefecto();calcular()});
  [pre,can].forEach(function(e){e.addEventListener('input',calcular)});
  f.addEventListener('submit',function(e){e.preventDefault();calcular()});
  porDefecto();calcular();
}
function pintaSimulacion(d,precio,piezas){
  var el=cuerpo.querySelector('#simR'),p=d.p,x=d.x;
  if(!x.ok){el.innerHTML='<p class="warnline">'+esc(x.falta||'Falta información para calcular el costo de este juguete.')+' Captura su precio en México o escanea su código de barras.</p>';return}
  var costo=+x.costo,real=x.origenCosto==='real',ref=+x.referencia||0,tu=+p.precioObjetivo||0;
  function gan(pr){return Math.round((pr-costo)*100)/100}
  function mg(pr){return pr>0?(pr-costo)/pr*100:null}
  if(!(precio>0)){el.innerHTML='<p class="warnline">Escribe un precio para simular.</p>';return}
  var g=gan(precio),tot=Math.round(g*piezas*100)/100,veredicto,tono;
  if(g<0){veredicto='Pierdes '+money2(-tot);tono='bad'}
  else if(g===0){veredicto='Ni ganas ni pierdes';tono='warn'}
  else if(precio<x.minimo){veredicto='Ganas '+money2(tot)+', pero menos de tu margen mínimo';tono='warn'}
  else{veredicto='Ganas '+money2(tot);tono='good'}
  var col={good:['var(--good-soft)','var(--good)'],warn:['var(--sun-soft)','var(--sun)'],bad:['var(--bad-soft)','var(--bad)']}[tono];
  var filas=[['Tu precio',tu],['Precio sugerido',x.sugerido],['Precio mínimo',x.minimo],['Precio intermediario (mínimo + 50 %)',interm(x.minimo)],['Precio de mercado (referencia)',ref]].filter(function(r){return r[1]>0});
  el.innerHTML=
    '<div class="card" style="background:'+col[0]+';border-color:transparent"><p class="small" style="margin:0;color:'+col[1]+'"><b>'+(piezas>1?'Vendiendo '+piezas+' piezas a '+money2(precio)+' cada una':'Vendiendo a '+money2(precio))+'</b></p>'+
      '<p style="margin:0;font-family:var(--f-display);font-weight:800;font-size:2rem;color:'+col[1]+'">'+esc(veredicto)+'</p>'+
      '<p class="small" style="margin:0">'+(g>=0?'Ganancia':'Pérdida')+' por pieza: <b>'+money2(Math.abs(g))+'</b>'+(mg(precio)!=null?' · margen '+pct(mg(precio)):'')+'</p></div>'+
    '<div class="grid g4">'+
      kpi('Costo por pieza',money2(costo),real?'Real: del costeo del pallet':'Estimado: calcula el costeo del pallet para el real')+
      kpi('Precio mínimo',money(x.minimo),'Lo menos que debes aceptar (margen '+pct(mg(x.minimo))+')')+
      kpi('Precio sugerido',money(x.sugerido),'El precio para publicar (margen '+pct(mg(x.sugerido))+')')+
      kpi('Precio de equilibrio',money2(costo),'A este precio ni ganas ni pierdes')+'</div>'+
    '<div class="card"><h3>Comparación por pieza</h3><div class="scroll"><table><thead><tr><th>Precio</th><th class="r">Monto</th><th class="r">Ganas o pierdes</th><th class="r">Margen</th></tr></thead><tbody>'+
      filas.map(function(r){var gg=gan(r[1]);return '<tr><td>'+esc(r[0])+'</td><td class="r">'+money2(r[1])+'</td><td class="r '+(gg<0?'tone-bad':'tone-good')+'">'+(gg<0?'− ':'+ ')+money2(Math.abs(gg))+'</td><td class="r">'+pct(mg(r[1]))+'</td></tr>'}).join('')+
    '</tbody></table></div><p class="small muted">Ganancia = precio − costo por pieza, antes de impuestos y de los gastos que registres en la venta (envío, empaque). No se usan comisiones ni tarifas de plataformas.</p></div>';
}

/* ================= Márgenes (objetivos de rentabilidad) ================= */
function vistaPlataformas(n){
  Promise.all([PC.api('GET','plataformas'),PC.api('GET','parametros-rentabilidad')]).then(function(x){if(n!==st.pide)return;
    plats=x[0].plataformas.map(function(y){return y.plataforma});opciones=x[0].opciones;
    pintaPlataformas(x[0],x[1]);
  },err);
}
function pintaPlataformas(pl,pars){
  cuerpo.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Márgenes</h2>'+
      '<p class="muted small">El precio mínimo y el sugerido (precio objetivo) de cada juguete se calculan como venta directa: costo de la pieza + el margen que definas aquí. No se suma IVA (lo que costó el pallet ya lo incluye) ni comisiones de plataformas.</p></div>'+
      '<button class="btn primary" type="button" id="mgAplicar" title="Recalcula el precio objetivo y el mínimo de todos los juguetes con estos márgenes">Actualizar precios con estos márgenes</button></div>'+
    '<div class="card"><div class="pf"><h3>Objetivos de rentabilidad</h3><button class="btn sm" type="button" id="parNuevo">+ Objetivo</button></div>'+
      '<p class="small muted">Gana el más específico: juguete › categoría › general. Margen sobre precio: el sugerido deja ese % del precio de venta como ganancia (30 % de margen = costo ÷ 0.70).</p>'+
      '<div class="scroll"><table><thead><tr><th>Ámbito</th><th>Objetivo</th><th class="r">Mínimo</th><th>Redondeo</th><th></th></tr></thead><tbody>'+
      pars.map(function(x){var p=x.parametro,amb=[x.producto,p.categoria,x.plataforma].filter(Boolean).join(' · ')||'General';
        return '<tr><td>'+esc(amb)+'<span class="sub">'+esc(x.ambito)+(p.activo?'':' · inactivo')+'</span></td><td>'+esc(ESQ[p.esquema])+': <b>'+(p.esquema==='utilidad_fija'?money2(p.valor):pct(p.valor))+'</b></td>'+
          '<td class="r">'+pct(p.margenMinimoPct)+'</td><td>'+esc(RED[p.redondeo])+'</td><td class="r"><button class="btn sm" type="button" data-par="'+esc(p.id)+'">Editar</button></td></tr>'}).join('')+
      '</tbody></table></div></div>';

  cuerpo.querySelector('#parNuevo').addEventListener('click',function(){parametroForm(null)});
  cuerpo.querySelector('#mgAplicar').addEventListener('click',function(){PC.recalcularPrecios()});
  cuerpo.querySelectorAll('[data-par]').forEach(function(b){b.addEventListener('click',function(){parametroForm(pars.filter(function(x){return x.parametro.id===b.dataset.par})[0])})});
}
function parametroForm(x){
  var p=x?x.parametro:{esquema:'margen',valor:30,margenMinimoPct:10,descuentoPromocionalPct:0,redondeo:'terminacion9',activo:true},general=x&&x.ambito==='general';
  var D=PC.datos().D,prods=Object.keys(D.productos).map(function(k){return D.productos[k]}).sort(function(a,b){return String(a.codigo).localeCompare(String(b.codigo),'es',{numeric:true})});
  PC.openModal(head(x?'Editar objetivo':'Nuevo objetivo')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    (general?'<p class="full small muted">Objetivo general: aplica cuando no hay uno más específico.</p>':
      fld('paPro','Juguete','<select class="sel" id="paPro" name="productoId"><option value="">Todos</option>'+prods.map(function(q){return '<option value="'+esc(q.id)+'"'+(q.id===p.productoId?' selected':'')+'>'+esc(q.codigo+' · '+q.nombre)+'</option>'}).join('')+'</select>')+
      fld('paCat','Categoría','<select class="sel" id="paCat" name="categoria"><option value="">Todas</option>'+categorias().map(function(c){return '<option'+(c===p.categoria?' selected':'')+'>'+esc(c)+'</option>'}).join('')+'</select>','Se ignora si eliges un juguete.'))+
    fld('paEsq','Esquema','<select class="sel" id="paEsq" name="esquema">'+PC.opt(Object.keys(ESQ),p.esquema,ESQ)+'</select>')+
    fld('paVal','Valor','<input class="inp num" id="paVal" name="valor" type="number" min="0" step="0.01" required value="'+esc(p.valor)+'">','% para margen y markup; pesos para utilidad fija.')+
    fld('paMin','Margen mínimo (%)','<input class="inp num" id="paMin" name="margenMinimoPct" type="number" min="0" max="99" step="0.01" value="'+esc(p.margenMinimoPct)+'">','Nunca se sugiere vender por debajo.')+
    fld('paRed','Redondeo','<select class="sel" id="paRed" name="redondeo">'+PC.opt(Object.keys(RED),p.redondeo,RED)+'</select>','Siempre hacia arriba.')+
    (general?'':'<div class="f"><label class="chk"><input type="checkbox" name="activo"'+(p.activo?' checked':'')+'> Activo</label></div>')+
    '</div><div id="paM"></div><div class="pf"><div>'+(x&&!general?'<button class="btn danger" type="button" id="paDel">Eliminar</button>':'')+'</div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div></form>',
  function(el){var f=el.querySelector('form'),msg=el.querySelector('#paM');
    PC.wireForm(el,function(){
      var b={esquema:f.elements.esquema.value,valor:+f.elements.valor.value||0,margenMinimoPct:+f.elements.margenMinimoPct.value||0,descuentoPromocionalPct:0,
        redondeo:f.elements.redondeo.value,activo:general?true:f.elements.activo.checked,
        productoId:general?null:(f.elements.productoId.value||null),categoria:general?null:(f.elements.categoria.value||null),plataformaId:null};
      (x?PC.api('PUT','parametros-rentabilidad/'+p.id,b):PC.api('POST','parametros-rentabilidad',b)).then(function(){render('plataformas',cuerpo);PC.recalcularPrecios('Guardaste el objetivo: revisa cómo cambian los precios y aplica para actualizarlos (o cancela para dejarlos como están).')},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'});
    });
    var d=el.querySelector('#paDel');if(d)d.addEventListener('click',function(){if(!d.classList.contains('armed')){d.classList.add('armed');d.textContent='Confirmar: eliminar';return}
      PC.api('DELETE','parametros-rentabilidad/'+p.id).then(function(){render('plataformas',cuerpo);PC.recalcularPrecios('Eliminaste un objetivo: revisa cómo cambian los precios y aplica para actualizarlos.')},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})});
  });
}

window.PCP={render:render,reiniciar:reiniciar,detalle:detalle,fichaRentabilidad:fichaRentabilidad};
})();
