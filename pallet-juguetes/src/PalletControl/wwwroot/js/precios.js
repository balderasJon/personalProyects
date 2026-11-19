/* Análisis comercial — Fase 2: Márgenes y fiscal (las tarifas por plataforma se retiraron), Análisis de productos y Simulador de precios.
   Todos los cálculos los hace el servidor (motor de precios). Esta capa solo pide, muestra y captura. */
(function(){
'use strict';
var PC=window.PC,esc=PC.esc,money2=PC.money2,num=PC.num,kpi=PC.kpi,fld=PC.fld,head=PC.head;
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
var avisoFiscal='<div class="callout sun small"><b>Régimen fiscal sin definir:</b> los precios no incluyen IVA ni ISR. Defínelo en Análisis comercial › Márgenes y fiscal cuando lo valides con tu contador.</div>';

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
function cargarPlats(){return plats?Promise.resolve(plats):PC.api('GET','plataformas').then(function(r){plats=r.plataformas.map(function(x){return x.plataforma});opciones=r.opciones;return plats})}
function vistaSimulador(n){
  cargarPlats().then(function(){if(n!==st.pide)return;
    var s=st.sim,D=PC.datos().D,prods=Object.keys(D.productos).map(function(k){return D.productos[k]}).sort(function(a,b){return String(a.codigo).localeCompare(String(b.codigo),'es',{numeric:true})});
    function dl(id,arr){return '<datalist id="'+id+'">'+(arr||[]).map(function(v){return '<option value="'+esc(v)+'">'}).join('')+'</datalist>'}
    cuerpo.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Simulador de precios</h2><p class="muted small">Prueba un precio en un canal y ve cuánto te cobra la plataforma, cuánto recibes y cuánto ganas.</p></div></div>'+
      '<form class="card" id="simF"><div class="form">'+
        fld('sProd','Juguete','<select class="sel" id="sProd" name="productoId"><option value="">— Sin juguete: capturar costo —</option>'+prods.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===s.productoId?' selected':'')+'>'+esc(p.codigo+' · '+p.nombre)+'</option>'}).join('')+'</select>')+
        fld('sPlat','Plataforma','<select class="sel" id="sPlat" name="plataformaId" required>'+plats.filter(function(p){return p.activa}).map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===s.plataformaId?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')+'</select>')+
        fld('sCos','Costo por pieza','<input class="inp num" id="sCos" name="costo" type="number" min="0" step="0.01" value="'+esc(s.costo||'')+'" placeholder="Vacío = costo real">','Con juguete elegido, vacío usa su costo real.')+
        fld('sPre','Precio a probar','<input class="inp num" id="sPre" name="precio" type="number" min="0" step="1" value="'+esc(s.precio||'')+'" placeholder="Vacío = sugerido">')+
        fld('sCat','Categoría (sin juguete)','<input class="inp" id="sCat" name="categoria" maxlength="60" value="'+esc(s.categoria||'')+'">')+
        fld('sPes','Peso kg (opcional)','<input class="inp num" id="sPes" name="pesoKg" type="number" min="0" step="0.001" value="'+esc(s.pesoKg||'')+'">')+
        fld('sTip','Tipo de publicación','<input class="inp" id="sTip" name="tipoPublicacion" list="dlTip" value="'+esc(s.tipoPublicacion||'')+'" placeholder="Predeterminada">'+dl('dlTip',opciones.tiposPublicacion))+
        fld('sMod','Modalidad logística','<input class="inp" id="sMod" name="modalidad" list="dlMod" value="'+esc(s.modalidad||'')+'" placeholder="Predeterminada">'+dl('dlMod',opciones.modalidades))+
        fld('sMed','Medio de pago','<input class="inp" id="sMed" name="medioPago" list="dlMed" value="'+esc(s.medioPago||'')+'" placeholder="Cualquiera">'+dl('dlMed',opciones.mediosPago))+
        fld('sFec','Fecha de la tarifa','<input class="inp" id="sFec" name="fecha" type="date" value="'+esc(s.fecha||PC.today())+'">')+
      '</div><div class="row"><button class="btn primary" type="submit">Calcular</button></div></form><div id="simR"></div>';
    var f=cuerpo.querySelector('#simF');
    f.addEventListener('submit',function(e){e.preventDefault();
      var b={};['productoId','plataformaId','categoria','tipoPublicacion','modalidad','medioPago','fecha'].forEach(function(k){var v=f.elements[k].value.trim();if(v)b[k]=v});
      ['costo','precio','pesoKg'].forEach(function(k){var v=f.elements[k].value;if(v!=='')b[k]=+v});
      st.sim=Object.assign({},b);
      PC.api('POST','rentabilidad/simular',b).then(pintaSimulacion,function(er){cuerpo.querySelector('#simR').innerHTML='<p class="warnline bad">'+esc(er.mensaje)+'</p>'});
    });
    if(s.productoId)f.requestSubmit();
  },err);
}
function pintaSimulacion(r){
  var c=r.canal,el=cuerpo.querySelector('#simR'),ap=r.alPrecio;
  if(!c.ok){el.innerHTML=(r.resultado.fiscalPendiente?avisoFiscal:'')+'<p class="warnline bad">'+esc(c.error)+'</p>';return}
  var d=ap&&ap.desglose?ap.desglose:c.alSugerido,al=ap&&ap.alertas?ap.alertas:c.alertas;
  function fila(l,v,cls,nota){return '<tr><td>'+l+(nota?'<span class="sub">'+nota+'</span>':'')+'</td><td class="r '+(cls||'')+'">'+v+'</td></tr>'}
  el.innerHTML=(r.resultado.fiscalPendiente?avisoFiscal:'')+
    '<div class="grid g4">'+kpi('Mínimo',m(c.minimo),'Margen mínimo; nunca se sugiere menos')+kpi('Sugerido',m(c.sugerido),'Objetivo '+m(c.objetivo)+' redondeado')+
      kpi('Promocional',m(c.promocional),c.promocionalLimitado?'Limitado al mínimo':'Con el descuento promocional')+kpi('Equilibrio',m(c.equilibrio),'Ni ganas ni pierdes')+'</div>'+
    (ap&&ap.error?'<p class="warnline">'+esc(ap.error)+'</p>':'')+
    '<div class="grid g2"><div class="card"><h3>Vendiendo a '+money2(d.precio)+'</h3><table class="dtab"><tbody>'+
      fila('Precio de venta',money2(d.precio))+(d.ivaTrasladado?fila('IVA trasladado',money2(d.ivaTrasladado),'','No es ingreso: se entera al SAT'):'')+
      fila('Ingreso sin IVA',money2(d.ingresoSinIva))+fila('Comisiones',' − '+money2(d.comision),'','Plataforma y medio de pago')+
      fila('Cargos de la plataforma',' − '+money2(d.cargosPlataforma),'','Fijo, logística, envío y almacenamiento')+fila('Tus gastos',' − '+money2(d.gastosPropios),'','Preparación, empaque, publicidad y reserva por devoluciones')+
      fila('Costo del juguete',' − '+money2(d.costo))+fila('<b>Utilidad neta estimada</b>','<b>'+money2(d.utilidad)+'</b>',d.utilidad<0?'tone-bad':'tone-good')+
      fila('Margen',pct(d.margenPct))+fila('ROI',d.roiPct!=null?pct(d.roiPct):'—','','Utilidad ÷ costo')+'</tbody></table></div>'+
    '<div class="card"><h3>Dinero que recibes</h3><table class="dtab"><tbody>'+fila('Precio de venta',money2(d.precio))+fila('Comisiones y cargos de la plataforma',' − '+money2(d.comision+d.cargosPlataforma))+
      fila('Retención de ISR',' − '+money2(d.retencionIsr),'','Anticipo de impuesto, no gasto')+fila('Retención de IVA',' − '+money2(d.retencionIva),'','Anticipo de impuesto, no gasto')+
      fila('<b>Depósito estimado de la plataforma</b>','<b>'+money2(d.depositoPlataforma)+'</b>')+'</tbody></table>'+
      '<p class="small muted">Tarifa: '+esc(ap&&ap.tarifa?ap.tarifa:(c.tarifa||{}).nombre||'')+' · objetivo '+esc((c.parametro||{}).ambito||'')+'.</p></div></div>'+
    (al&&al.length?'<div class="card"><h3>Alertas</h3><ul class="clean small">'+al.map(function(x){return '<li class="'+(x.nivel==='error'?'tone-bad':'')+'">'+esc(x.mensaje)+'</li>'}).join('')+'</ul></div>':'<p class="small tone-good">Sin alertas a este precio.</p>');
}

/* ================= Plataformas, tarifas, objetivos y fiscal ================= */
function vistaPlataformas(n){
  Promise.all([PC.api('GET','plataformas'),PC.api('GET','parametros-rentabilidad'),PC.api('GET','fiscal')]).then(function(x){if(n!==st.pide)return;
    plats=x[0].plataformas.map(function(y){return y.plataforma});opciones=x[0].opciones;
    pintaPlataformas(x[0],x[1],x[2]);
  },err);
}
function pintaPlataformas(pl,pars,fis){
  cuerpo.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Márgenes y fiscal</h2>'+
      '<p class="muted small">El precio mínimo y el sugerido de cada juguete se calculan como venta directa: costo de la pieza + el margen que definas aquí. No se usan comisiones ni envíos de plataformas.</p></div></div>'+
    '<div class="grid g2"><div class="card"><div class="pf"><h3>Objetivos de rentabilidad</h3><button class="btn sm" type="button" id="parNuevo">+ Objetivo</button></div>'+
      '<p class="small muted">Gana el más específico: juguete y plataforma › juguete › categoría y plataforma › categoría › plataforma › general.</p>'+
      '<div class="scroll"><table><thead><tr><th>Ámbito</th><th>Objetivo</th><th class="r">Mínimo</th><th class="r">Promoción</th><th>Redondeo</th><th></th></tr></thead><tbody>'+
      pars.map(function(x){var p=x.parametro,amb=[x.producto,p.categoria,x.plataforma].filter(Boolean).join(' · ')||'General';
        return '<tr><td>'+esc(amb)+'<span class="sub">'+esc(x.ambito)+(p.activo?'':' · inactivo')+'</span></td><td>'+esc(ESQ[p.esquema])+': <b>'+(p.esquema==='utilidad_fija'?money2(p.valor):pct(p.valor))+'</b></td>'+
          '<td class="r">'+pct(p.margenMinimoPct)+'</td><td class="r">'+pct(p.descuentoPromocionalPct)+'</td><td>'+esc(RED[p.redondeo])+'</td><td class="r"><button class="btn sm" type="button" data-par="'+esc(p.id)+'">Editar</button></td></tr>'}).join('')+
      '</tbody></table></div></div>'+
    '<form class="card" id="fisF"><h3>Situación fiscal</h3>'+(fis.regimen==='pendiente'?'<p class="warnline small">Pendiente: los precios no incluyen IVA ni ISR hasta que lo definas con tu contador.</p>':'')+
      '<div class="form">'+fld('fReg','Régimen','<select class="sel" id="fReg" name="regimen">'+PC.opt(Object.keys(REG),fis.regimen,REG)+'</select>')+
        fld('fTas','Tasa de IVA (%)','<input class="inp num" id="fTas" name="tasaIvaPct" type="number" min="0" max="100" step="0.01" value="'+esc(fis.tasaIvaPct)+'">')+
        '<div class="f full"><label class="chk"><input type="checkbox" name="cobraIva"'+(fis.cobraIva?' checked':'')+'> Cobro IVA en mis ventas (el precio al cliente lo incluye)</label></div>'+
        fld('fNot','Notas','<input class="inp" id="fNot" name="notas" maxlength="500" value="'+esc(fis.notas||'')+'" placeholder="Lo que te indicó tu contador">',null,true)+
      '</div><p class="small muted">Esta información no es asesoría fiscal.</p><div class="row"><button class="btn" type="submit">Guardar</button></div></form></div>';

  cuerpo.querySelector('#parNuevo').addEventListener('click',function(){parametroForm(null)});
  cuerpo.querySelectorAll('[data-par]').forEach(function(b){b.addEventListener('click',function(){parametroForm(pars.filter(function(x){return x.parametro.id===b.dataset.par})[0])})});
  cuerpo.querySelector('#fisF').addEventListener('submit',function(e){e.preventDefault();var f=this;
    PC.api('PUT','fiscal',{regimen:f.elements.regimen.value,cobraIva:f.elements.cobraIva.checked,tasaIvaPct:+f.elements.tasaIvaPct.value||0,notas:f.elements.notas.value}).then(function(){PC.toast('Situación fiscal guardada');render('plataformas',cuerpo)},err)});
}
function parametroForm(x){
  var p=x?x.parametro:{esquema:'margen',valor:30,margenMinimoPct:10,descuentoPromocionalPct:0,redondeo:'terminacion9',activo:true},general=x&&x.ambito==='general';
  var D=PC.datos().D,prods=Object.keys(D.productos).map(function(k){return D.productos[k]}).sort(function(a,b){return String(a.codigo).localeCompare(String(b.codigo),'es',{numeric:true})});
  PC.openModal(head(x?'Editar objetivo':'Nuevo objetivo')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    (general?'<p class="full small muted">Objetivo general: aplica cuando no hay uno más específico.</p>':
      fld('paPro','Juguete','<select class="sel" id="paPro" name="productoId"><option value="">Todos</option>'+prods.map(function(q){return '<option value="'+esc(q.id)+'"'+(q.id===p.productoId?' selected':'')+'>'+esc(q.codigo+' · '+q.nombre)+'</option>'}).join('')+'</select>')+
      fld('paCat','Categoría','<select class="sel" id="paCat" name="categoria"><option value="">Todas</option>'+categorias().map(function(c){return '<option'+(c===p.categoria?' selected':'')+'>'+esc(c)+'</option>'}).join('')+'</select>','Se ignora si eliges un juguete.')+
      fld('paPla','Plataforma','<select class="sel" id="paPla" name="plataformaId"><option value="">Todas</option>'+(plats||[]).map(function(q){return '<option value="'+esc(q.id)+'"'+(q.id===p.plataformaId?' selected':'')+'>'+esc(q.nombre)+'</option>'}).join('')+'</select>'))+
    fld('paEsq','Esquema','<select class="sel" id="paEsq" name="esquema">'+PC.opt(Object.keys(ESQ),p.esquema,ESQ)+'</select>')+
    fld('paVal','Valor','<input class="inp num" id="paVal" name="valor" type="number" min="0" step="0.01" required value="'+esc(p.valor)+'">','% para margen y markup; pesos para utilidad fija.')+
    fld('paMin','Margen mínimo (%)','<input class="inp num" id="paMin" name="margenMinimoPct" type="number" min="0" max="99" step="0.01" value="'+esc(p.margenMinimoPct)+'">','Nunca se sugiere vender por debajo.')+
    fld('paPro2','Descuento promocional (%)','<input class="inp num" id="paPro2" name="descuentoPromocionalPct" type="number" min="0" max="99" step="0.01" value="'+esc(p.descuentoPromocionalPct)+'">','Se limita al mínimo.')+
    fld('paRed','Redondeo','<select class="sel" id="paRed" name="redondeo">'+PC.opt(Object.keys(RED),p.redondeo,RED)+'</select>','Siempre hacia arriba.')+
    (general?'':'<div class="f"><label class="chk"><input type="checkbox" name="activo"'+(p.activo?' checked':'')+'> Activo</label></div>')+
    '</div><div id="paM"></div><div class="pf"><div>'+(x&&!general?'<button class="btn danger" type="button" id="paDel">Eliminar</button>':'')+'</div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div></form>',
  function(el){var f=el.querySelector('form'),msg=el.querySelector('#paM');
    PC.wireForm(el,function(){
      var b={esquema:f.elements.esquema.value,valor:+f.elements.valor.value||0,margenMinimoPct:+f.elements.margenMinimoPct.value||0,descuentoPromocionalPct:+f.elements.descuentoPromocionalPct.value||0,
        redondeo:f.elements.redondeo.value,activo:general?true:f.elements.activo.checked,
        productoId:general?null:(f.elements.productoId.value||null),categoria:general?null:(f.elements.categoria.value||null),plataformaId:general?null:(f.elements.plataformaId.value||null)};
      (x?PC.api('PUT','parametros-rentabilidad/'+p.id,b):PC.api('POST','parametros-rentabilidad',b)).then(function(){PC.closeModal();PC.toast('Objetivo guardado');render('plataformas',cuerpo)},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'});
    });
    var d=el.querySelector('#paDel');if(d)d.addEventListener('click',function(){if(!d.classList.contains('armed')){d.classList.add('armed');d.textContent='Confirmar: eliminar';return}
      PC.api('DELETE','parametros-rentabilidad/'+p.id).then(function(){PC.closeModal();PC.toast('Objetivo eliminado');render('plataformas',cuerpo)},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})});
  });
}

window.PCP={render:render,reiniciar:reiniciar,detalle:detalle,fichaRentabilidad:fichaRentabilidad};
})();
