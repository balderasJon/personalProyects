/* Fase 4: Dashboard ejecutivo, Competencia y Reportes.
   Todos los indicadores los calcula el servidor (/api/dashboard); aquí solo se piden, se dibujan y se capturan.
   Gráficas: una sola serie por gráfica (sin leyenda; el título la nombra), barras delgadas con el valor en la punta,
   azul para positivo y rojo para negativo (tokens --chart-pos / --chart-neg, validados para daltonismo en claro y oscuro). */
(function(){
'use strict';
var PC=window.PC,esc=PC.esc,money=PC.money,money2=PC.money2,num=PC.num,fdate=PC.fdate,kpi=PC.kpi,fld=PC.fld,head=PC.head;
var POS={barato:['entregado','Abajo del mercado'],en_mercado:['pagado','En precio de mercado'],caro:['apartado','Arriba del mercado']};
function err(e){PC.toast((e&&e.mensaje)||'No se pudo completar la operación.')}
function d(){return PC.datos()}
function pct(v){return v==null?'—':(+v).toLocaleString('es-MX',{maximumFractionDigits:2})+' %'}
function m2(v){return v==null?'—':money2(v)}
function n(v){var x=+v;return isFinite(x)?x:0}
function pill(p){if(!p)return '<span class="muted small">—</span>';var x=POS[p]||['C',p];return '<span class="pill '+x[0]+'">'+esc(x[1])+'</span>'}
function categorias(){var c={},D=d().D;Object.keys(D.productos).forEach(function(k){var x=D.productos[k].categoria;if(x)c[x]=1});return Object.keys(c).sort()}
function productos(){var D=d().D;return Object.keys(D.productos).map(function(k){return D.productos[k]}).sort(function(a,b){return String(a.nombre).localeCompare(String(b.nombre),'es')})}

/* ---------- tooltip compartido (complementa: todos los valores también están escritos en la gráfica o en tablas) ---------- */
var tip=null;
function verTip(html,x,y){if(!tip){tip=document.createElement('div');tip.className='ch-tip';tip.setAttribute('role','status');document.body.appendChild(tip)}
  tip.innerHTML=html;tip.hidden=false;var w=tip.offsetWidth,h=tip.offsetHeight;tip.style.left=Math.max(8,Math.min(innerWidth-w-8,x+12))+'px';tip.style.top=Math.max(8,y-h-10)+'px'}
function ocultarTip(){if(tip)tip.hidden=true}
function wireTips(root){root.querySelectorAll('[data-tip]').forEach(function(r){
  r.addEventListener('pointermove',function(e){verTip(r.dataset.tip,e.clientX,e.clientY)});r.addEventListener('pointerleave',ocultarTip);
  r.addEventListener('focus',function(){var b=r.getBoundingClientRect();verTip(r.dataset.tip,b.left+b.width/2,b.top)});r.addEventListener('blur',ocultarTip)})}

/* ---------- barras horizontales (una serie; con negativos, base en cero) ---------- */
function barras(items,fmt){
  if(!items.length)return '<p class="small muted">Sin datos con estos filtros.</p>';
  var mn=Math.min(0,Math.min.apply(null,items.map(function(i){return i.v}))),mx=Math.max(0,Math.max.apply(null,items.map(function(i){return i.v})));
  var rango=mx-mn||1,z=(-mn/rango)*100,neg=mn<0;
  // Deja espacio para la etiqueta del valor en la punta.
  var esc_=neg?0.72:0.78;
  return '<div class="hb">'+items.map(function(i){
    var w=Math.abs(i.v)/rango*100*esc_,zz=neg?z*esc_+ (100-100*esc_)/2:0,izq=i.v<0?zz-w:zz;
    var t='<b>'+esc(fmt(i.v))+'</b>'+esc(i.l)+(i.s?'<br>'+esc(i.s):'');
    return '<div class="hb-r" tabindex="0" data-tip="'+esc(t)+'" aria-label="'+esc(i.l+': '+fmt(i.v))+'"><span class="hb-l">'+esc(i.l)+'</span><span class="hb-t">'+
      (neg?'<i class="hb-z" style="left:'+zz+'%"></i>':'')+
      '<i class="hb-b '+(i.v<0?'neg':'pos')+'" style="left:'+izq+'%;width:'+Math.max(w,i.v?0.6:0)+'%"></i>'+
      '<span class="hb-v" style="'+(i.v<0?'right:'+(100-izq+1)+'%':'left:'+(izq+w+1)+'%')+'">'+esc(fmt(i.v))+'</span></span></div>'}).join('')+'</div>';
}

/* ---------- línea: efectivo neto recuperado acumulado vs inversión ---------- */
function linea(serie,inversion){
  if(!serie.length)return '<p class="small muted">Aparece cuando registres el cobro de la primera venta.</p>';
  var W=380,H=230,pl=48,pr=10,pt=16,pb=24;
  var pts=serie.map(function(p){return {f:p.fecha,v:+p.valor}});
  if(pts.length===1)pts.unshift({f:pts[0].f,v:0});
  var mx=Math.max(inversion||0,Math.max.apply(null,pts.map(function(p){return p.v}))),mn=Math.min(0,Math.min.apply(null,pts.map(function(p){return p.v})));
  if(mx-mn<1)mx=mn+1;
  // Marcas del eje en cifras limpias (1, 2 o 5 × 10^n)
  var paso=(function(r){var e=Math.pow(10,Math.floor(Math.log10(r/3))),q=r/3/e;return (q<=1?1:q<=2?2:q<=5?5:10)*e})(mx*1.06-mn);
  mn=Math.floor(mn/paso)*paso;mx=Math.ceil(mx*1.06/paso)*paso;
  function X(k){return pl+(W-pl-pr)*(pts.length>1?k/(pts.length-1):0)}
  function Y(v){return pt+(H-pt-pb)*(1-(v-mn)/(mx-mn))}
  var ticks=[];for(var t=mn;t<=mx+1e-9;t+=paso)ticks.push(t);
  var path=pts.map(function(p,k){return (k?'L':'M')+X(k).toFixed(1)+' '+Y(p.v).toFixed(1)}).join(' ');
  var area=path+' L'+X(pts.length-1).toFixed(1)+' '+Y(Math.max(mn,0)).toFixed(1)+' L'+X(0).toFixed(1)+' '+Y(Math.max(mn,0)).toFixed(1)+' Z';
  var ult=pts[pts.length-1];
  var svg='<svg class="ch-line" viewBox="0 0 '+W+' '+H+'" role="img" aria-label="Efectivo recuperado acumulado: '+esc(money(ult.v))+' de '+esc(money(inversion))+' invertidos">'+
    ticks.map(function(t){return '<line class="grid" x1="'+pl+'" x2="'+(W-pr)+'" y1="'+Y(t).toFixed(1)+'" y2="'+Y(t).toFixed(1)+'"/><text class="ax" x="'+(pl-6)+'" y="'+(Y(t)+4).toFixed(1)+'" text-anchor="end">'+esc(compacto(t))+'</text>'}).join('')+
    (inversion>0?'<line class="ref" x1="'+pl+'" x2="'+(W-pr)+'" y1="'+Y(inversion).toFixed(1)+'" y2="'+Y(inversion).toFixed(1)+'"/><text class="ax" x="'+(W-pr)+'" y="'+(Y(inversion)-5).toFixed(1)+'" text-anchor="end">Inversión '+esc(money(inversion))+'</text>':'')+
    '<path class="ar" d="'+area+'"/><path class="ln" d="'+path+'"/>'+
    '<text class="ax" x="'+pl+'" y="'+(H-6)+'">'+esc(fdate(pts[0].f))+'</text><text class="ax" x="'+(W-pr)+'" y="'+(H-6)+'" text-anchor="end">'+esc(fdate(ult.f))+'</text>'+
    '<line class="cross" x1="0" x2="0" y1="'+pt+'" y2="'+(H-pb)+'" visibility="hidden"/>'+
    '<circle class="dot" r="5" cx="'+X(pts.length-1).toFixed(1)+'" cy="'+Y(ult.v).toFixed(1)+'"/>'+
    '<rect x="'+pl+'" y="'+pt+'" width="'+(W-pl-pr)+'" height="'+(H-pt-pb)+'" fill="transparent" class="hit"/></svg>';
  return {html:svg,pts:pts,X:X,Y:Y,W:W};
}
function compacto(v){var a=Math.abs(v);return (v<0?'−':'')+'$'+(a>=1000?(Math.round(a/100)/10).toLocaleString('es-MX')+'k':Math.round(a).toLocaleString('es-MX'))}
function wireLinea(el,L){var svg=el.querySelector('svg.ch-line');if(!svg||!L.pts)return;var cr=svg.querySelector('.cross'),dot=svg.querySelector('.dot');
  svg.querySelector('.hit').addEventListener('pointermove',function(e){var b=svg.getBoundingClientRect(),x=(e.clientX-b.left)/b.width*L.W,k=0,best=1e9;
    L.pts.forEach(function(p,i){var dd=Math.abs(L.X(i)-x);if(dd<best){best=dd;k=i}});
    cr.setAttribute('x1',L.X(k));cr.setAttribute('x2',L.X(k));cr.setAttribute('visibility','visible');dot.setAttribute('cx',L.X(k));dot.setAttribute('cy',L.Y(L.pts[k].v));
    verTip('<b>'+esc(money2(L.pts[k].v))+'</b>Recuperado al '+esc(fdate(L.pts[k].f)),e.clientX,e.clientY)});
  svg.querySelector('.hit').addEventListener('pointerleave',function(){cr.setAttribute('visibility','hidden');var u=L.pts.length-1;dot.setAttribute('cx',L.X(u));dot.setAttribute('cy',L.Y(L.pts[u].v));ocultarTip()})}

/* ================= Dashboard ================= */
var F={desde:'',hasta:'',palletId:'',categoria:'',productoId:'',plataformaId:''},pide=0;
function query(){return Object.keys(F).filter(function(k){return F[k]}).map(function(k){return k+'='+encodeURIComponent(F[k])}).join('&')}
function filtros(id){
  return '<form class="dash-f" id="'+id+'">'+
    '<label>Desde<input class="inp" type="date" name="desde" value="'+esc(F.desde)+'"></label><label>Hasta<input class="inp" type="date" name="hasta" value="'+esc(F.hasta)+'"></label>'+
    '<label>Pallet<select class="sel" name="palletId"><option value="">Todos</option>'+PC.palletsLista().map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===F.palletId?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')+'</select></label>'+
    '<label>Categoría<select class="sel" name="categoria"><option value="">Todas</option>'+categorias().map(function(c){return '<option'+(c===F.categoria?' selected':'')+'>'+esc(c)+'</option>'}).join('')+'</select></label>'+
    '<label>Juguete<select class="sel" name="productoId"><option value="">Todos</option>'+productos().map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===F.productoId?' selected':'')+'>'+esc(p.codigo+' · '+p.nombre)+'</option>'}).join('')+'</select></label>'+
    '<label>Plataforma<select class="sel" name="plataformaId"><option value="">Todas</option>'+d().PLATS.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===F.plataformaId?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')+'</select></label>'+
    '<button class="btn" type="submit">Aplicar</button><button class="btn" type="button" data-limpiar>Limpiar</button></form>';
}
function wireFiltros(body,id,f){var fo=body.querySelector('#'+id);
  fo.addEventListener('submit',function(e){e.preventDefault();Object.keys(F).forEach(function(k){F[k]=fo.elements[k].value});f()});
  fo.querySelector('[data-limpiar]').addEventListener('click',function(){Object.keys(F).forEach(function(k){F[k]=''});f()})}

function vistaDashboard(body){var k=++pide;
  PC.api('GET','dashboard'+(query()?'?'+query():'')).then(function(r){if(k!==pide)return;pintaDashboard(body,r)},err);
}
function tablaProd(l,col){return l.length?'<div class="scroll" style="border:0"><table><thead><tr><th>Juguete</th><th class="r">'+col[0]+'</th></tr></thead><tbody>'+
  l.map(function(p){return '<tr><td class="wrap">'+esc(p.codigo+' · '+p.nombre)+'</td><td class="r">'+col[1](p)+'</td></tr>'}).join('')+'</tbody></table></div>':'<p class="small muted">Sin datos.</p>'}
function pintaDashboard(body,r){
  var x=r.financieros,inv=r.inventario,a=inv.antiguedad,c=r.competencia,L=linea(r.recuperacion,x.inversion);
  var avisos=[];
  if(r.fiscalPendiente)avisos.push('Régimen fiscal pendiente: la utilidad neta es antes de impuestos.');
  if(x.baseInversion==='costo_asignado')avisos.push('Con filtro de categoría o juguete, la inversión es el costo asignado por el costeo vigente a esas piezas.');
  if(!x.netaIncluyeOperacion)avisos.push('Los gastos generales de operación no se reparten por pallet, categoría, juguete ni plataforma: con esos filtros la utilidad neta no los descuenta.');
  body.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Dashboard</h2>'+
      '<p class="muted small">Ingresos y utilidad con el costo histórico de cada venta. El capital recuperado solo cuenta lo cobrado. Las fechas filtran por fecha de venta.</p></div>'+
      '<button class="btn" type="button" id="dbUmb">Umbrales</button></div>'+filtros('dbF')+
    (avisos.length?'<div class="callout sun small">'+avisos.map(esc).join('<br>')+'</div>':'')+
    '<h3 class="dash-h">Financieros</h3>'+'<div class="grid g4">'+
      kpi('Inversión',m2(x.inversion),r.porPallet.length+' pallet'+(r.porPallet.length===1?'':'s'))+
      kpi('Ingresos',m2(x.ingresos),num(x.ventas)+' ventas · '+num(x.piezas)+' piezas · sin IVA ni reembolsos')+
      kpi('Costo de lo vendido',m2(x.costoVendido),x.merma?'Merma por devoluciones '+m2(x.merma):'Costo histórico de las piezas')+
      kpi('Gastos comerciales',m2(x.gastosComerciales),'Comisiones, envíos, empaque… de cada venta')+
      kpi('Utilidad bruta',m2(x.utilidadBruta),'Ingresos − costo de lo vendido',x.utilidadBruta<0?'tone-bad':'')+
      kpi('Utilidad neta estimada',m2(x.utilidadNetaEstimada),x.netaIncluyeOperacion?'Menos gastos comerciales y '+m2(x.gastosOperacion)+' de operación':'Menos gastos comerciales',x.utilidadNetaEstimada<0?'tone-bad':'tone-good')+
      kpi('Margen promedio',pct(x.margenPct),'Utilidad comercial ÷ ingresos · sobre costo '+pct(x.rendimientoSobreCostoPct))+
      kpi('ROI',pct(x.roiPct),'Utilidad neta estimada ÷ inversión')+
      kpi('Capital recuperado',m2(x.capitalRecuperado),pct(x.recuperadoPct)+' de la inversión · cobrado '+m2(x.cobrado))+
      kpi('Pendiente de recuperar',m2(x.capitalPendiente),'Por cobrar (estimado) '+m2(x.porCobrar))+
    '</div>'+
    '<h3 class="dash-h">Inventario</h3>'+'<div class="grid g4">'+
      kpi('Juguetes',num(inv.productos),num(inv.piezasRecibidas)+' piezas recibidas')+
      kpi('Disponibles',num(inv.piezasDisponibles),num(inv.productosDisponibles)+' juguetes con piezas vendibles')+
      kpi('Publicados',num(inv.productosPublicados),'Juguetes con publicación activa')+
      kpi('Vendidos',num(inv.piezasVendidas),num(inv.productosVendidos)+' juguetes con ventas (netas de devoluciones)')+
      kpi('Dañados e incompletos',num(inv.piezasDanadas+inv.piezasIncompletas),num(inv.piezasDanadas)+' dañados · '+num(inv.piezasIncompletas)+' incompletos · '+num(inv.piezasNoVendibles)+' no vendibles en total',inv.piezasDanadas+inv.piezasIncompletas?'tone-bad':'')+
      kpi('Sin movimiento',num(inv.productosSinMovimiento),num(inv.piezasSinMovimiento)+' piezas · '+r.umbralSinMovimiento+' días o más sin venderse',inv.productosSinMovimiento?'tone-bad':'')+
      kpi('Antigüedad promedio',(+a.diasPromedio).toLocaleString('es-MX')+' días','0–30: '+num(a.piezas0a30)+' · 31–60: '+num(a.piezas31a60)+' · 61–90: '+num(a.piezas61a90)+' · +90: '+num(a.piezasMas90))+
      kpi('Competencia',num(c.conCompetencia)+' juguetes',num(c.barato)+' abajo · '+num(c.en_mercado)+' en mercado · '+num(c.caro)+' arriba (±'+pct(r.bandaCompetenciaPct).replace(' %','')+' %)'+(c.mercadoBajoMinimo?' · '+num(c.mercadoBajoMinimo)+' con mercado bajo tu mínimo':''))+
    '</div>'+
    '<div class="grid g2">'+
      '<div class="card"><h3>Ventas por plataforma</h3><p class="small muted">Ingresos del periodo</p>'+barras(r.porPlataforma.map(function(p){return {l:p.nombre,v:+p.ingresos,s:num(p.ventas)+' ventas · '+num(p.piezas)+' piezas'}}),money)+'</div>'+
      '<div class="card"><h3>Utilidad por plataforma</h3><p class="small muted">Margen por canal en el detalle de cada barra</p>'+barras(r.porPlataforma.map(function(p){return {l:p.nombre,v:+p.utilidad,s:'Margen '+pct(p.margenPct)}}),money)+'</div>'+
      '<div class="card"><h3>Recuperación de la inversión</h3><p class="small muted">Efectivo neto cobrado, acumulado</p><div id="dbLin">'+(L.html||L)+'</div></div>'+
      '<div class="card"><h3>Rentabilidad por categoría</h3><p class="small muted">Utilidad del periodo</p>'+barras(r.porCategoria.map(function(p){return {l:p.nombre,v:+p.utilidad,s:'Ingresos '+money(p.ingresos)+' · margen '+pct(p.margenPct)}}),money)+'</div>'+
      '<div class="card"><h3>Rotación del inventario</h3><p class="small muted">Piezas vendidas ÷ recibidas (los 5 más altos)</p>'+barras(r.mayorRotacion.map(function(p){return {l:p.nombre,v:+p.rotacionPct,s:num(p.vendidas)+' de '+num(p.recibidas)+' piezas'}}),function(v){return pct(v)})+'</div>'+
      '<div class="card"><h3>Inventario por condición</h3><p class="small muted">Piezas en existencia</p>'+barras(inv.porCondicion.map(function(p){return {l:p.nombre,v:p.piezas}}),function(v){return num(v)+' pz'})+'</div>'+
      '<div class="card"><h3>Más rentables</h3>'+barras(r.masRentables.map(function(p){return {l:p.nombre,v:+p.utilidad,s:'Margen '+pct(p.margenPct)}}),money)+'</div>'+
      '<div class="card"><h3>Menos rentables</h3>'+barras(r.menosRentables.map(function(p){return {l:p.nombre,v:+p.utilidad,s:'Margen '+pct(p.margenPct)}}),money)+'</div>'+
      '<div class="card"><h3>Más vendidos</h3><p class="small muted">Piezas del periodo</p>'+tablaProd(r.masVendidos,['Piezas',function(p){return num(p.vendidas)}])+'</div>'+
      '<div class="card wide"><h3>Capital por pallet</h3><div class="scroll" style="border:0"><table><thead><tr><th>Pallet</th><th class="r">Inversión</th><th class="r">Recuperado</th><th>Avance</th><th class="r">Falta por recuperar</th></tr></thead><tbody>'+
        r.porPallet.map(function(p){var w=Math.max(0,Math.min(100,p.recuperadoPct||0));return '<tr><td>'+esc(p.nombre)+'</td><td class="r">'+m2(p.inversion)+'</td><td class="r">'+m2(p.recuperado)+'</td><td style="min-width:140px"><div class="meter" style="height:8px"><i style="width:'+w+'%"></i></div><span class="sub">'+pct(p.recuperadoPct)+'</span></td><td class="r">'+m2(p.pendiente)+'</td></tr>'}).join('')+'</tbody></table></div></div>'+
    '</div>'+
    '<div class="card"><div class="pf"><h3 style="margin:0">Candidatos a liquidación</h3><a class="btn sm" href="/api/reportes/liquidacion'+(query()?'?'+query():'')+'" download>Descargar CSV</a></div>'+
      '<p class="small muted">Juguetes con piezas vendibles y '+r.umbralSinMovimiento+' días o más sin venderse (desde su última venta o su ingreso). Compara su costo y tu mínimo con el mercado antes de rebajar.</p>'+
      (r.candidatosLiquidacion.length?'<div class="scroll" style="border:0"><table><thead><tr><th>Juguete</th><th class="r">Disponibles</th><th class="r">Días sin venderse</th><th class="r">Costo por pieza</th><th class="r">Tu mínimo</th><th class="r">Mediana del mercado</th><th>Posición</th></tr></thead><tbody>'+
        r.candidatosLiquidacion.map(function(p){return '<tr><td class="wrap"><b>'+esc(p.codigo+' · '+p.nombre)+'</b><span class="sub">'+esc(p.categoria||'')+'</span></td><td class="r">'+num(p.disponibles)+'</td><td class="r">'+num(p.diasSinMovimiento)+'</td><td class="r">'+m2(p.costoUnitario)+'</td><td class="r">'+(+p.precioMinimo>0?m2(p.precioMinimo):'—')+'</td><td class="r">'+m2(p.medianaMercado)+'</td><td>'+pill(p.posicionCompetitiva)+'</td></tr>'}).join('')+'</tbody></table></div>'
        :'<p class="small muted">Ninguno: todo lo disponible se ha movido en los últimos '+r.umbralSinMovimiento+' días.</p>')+'</div>';
  wireFiltros(body,'dbF',function(){vistaDashboard(body)});
  wireTips(body);if(L.pts)wireLinea(body.querySelector('#dbLin'),L);
  body.querySelector('#dbUmb').addEventListener('click',function(){umbralesForm(r,function(){vistaDashboard(body)})});
}
function umbralesForm(r,despues){
  PC.openModal(head('Umbrales de análisis')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('uDias','Días sin movimiento','<input class="inp num" id="uDias" name="dias" type="number" min="1" max="730" step="1" required value="'+esc(r.umbralSinMovimiento)+'">','Desde la última venta o el ingreso; marca candidatos a liquidación.')+
    fld('uBan','Banda de precio de mercado (±%)','<input class="inp num" id="uBan" name="banda" type="number" min="0" max="50" step="0.5" required value="'+esc(r.bandaCompetenciaPct)+'">','Alrededor de la mediana de la competencia.')+
    '</div><div class="pf"><div></div><div class="row"><button class="btn" type="button" data-c2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div></form>',function(el){
    el.querySelector('[data-c2]').addEventListener('click',PC.closeModal);
    el.querySelector('form').addEventListener('submit',function(e){e.preventDefault();var f=e.target;
      PC.api('PUT','configuracion/comercial',{diasSinMovimiento:Math.round(n(f.dias.value)),bandaCompetenciaPct:n(f.banda.value)}).then(function(){PC.closeModal();PC.toast('Umbrales guardados');despues()},err)})});
}

/* ================= Competencia ================= */
var CP={sel:null,pide:0};
function vistaCompetencia(body){var k=++CP.pide;
  if(CP.sel)return PC.api('GET','competencia?todas=true&productoId='+encodeURIComponent(CP.sel)).then(function(l){
    return PC.api('GET','competencia/resumen?productoId='+encodeURIComponent(CP.sel)).then(function(rs){if(k!==CP.pide)return;pintaCompProducto(body,rs[0],l)})},err);
  PC.api('GET','competencia/resumen').then(function(rs){if(k!==CP.pide)return;pintaCompLista(body,rs)},err);
}
function cabeceraComp(){return '<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Competencia</h2>'+
  '<p class="muted small">Registra precios de productos similares (con su envío). Un precio publicado no es una venta realizada. Sin código de barras, identifícalos por foto, marca, modelo y características. La captura es manual: no se extraen datos de las plataformas.</p></div>'+
  '<button class="btn primary" type="button" id="cpNuevo">+ Precio de competencia</button></div>'}
function pintaCompLista(body,rs){
  var con=rs.filter(function(r){return r.observaciones>0});
  body.innerHTML=cabeceraComp()+
    (con.length?'<div class="scroll"><table><thead><tr><th>Juguete</th><th class="r">Tu precio</th><th class="r">Precios</th><th class="r">Mínimo</th><th class="r">Mediana</th><th class="r">Promedio</th><th class="r">Máximo</th><th class="r">Diferencia</th><th>Posición</th><th></th></tr></thead><tbody>'+
      con.map(function(r){var m=r.mercado||{},p=r.posicion;return '<tr><td class="wrap"><button class="lnk nom" type="button" data-cp="'+esc(r.productoId)+'">'+esc(r.codigo+' · '+r.nombre)+'</button>'+(r.mercadoBajoTuMinimo?'<span class="sub tone-bad">La mediana está debajo de tu mínimo ('+m2(r.precioMinimo)+')</span>':'')+'</td>'+
        '<td class="r">'+m2(r.precioPropio)+'<span class="sub">'+(r.origenPrecioPropio==='publicacion'?'publicado':r.origenPrecioPropio==='precio_objetivo'?'precio objetivo':'sin precio')+'</span></td><td class="r">'+num(r.observaciones)+'</td>'+
        '<td class="r">'+m2(m.minimo)+'</td><td class="r"><b>'+m2(m.mediana)+'</b></td><td class="r">'+m2(m.promedio)+'</td><td class="r">'+m2(m.maximo)+'</td>'+
        '<td class="r">'+(p?pct(p.diferenciaPct)+'<span class="sub">lugar '+p.lugar+' de '+p.total+'</span>':'—')+'</td><td>'+pill(p&&p.posicion)+'</td>'+
        '<td class="r"><button class="btn sm" type="button" data-cp="'+esc(r.productoId)+'">Ver</button></td></tr>'}).join('')+'</tbody></table></div>'+
      '<p class="small muted">Se compara tu publicación activa más barata (o tu precio objetivo) contra la mediana del precio total de la competencia. «Lugar 1» = el más barato.</p>'
      :'<div class="empty"><p class="muted">Aún no registras precios de competencia.</p></div>');
  body.querySelector('#cpNuevo').addEventListener('click',function(){compForm(null,null,function(){vistaCompetencia(body)})});
  body.querySelectorAll('[data-cp]').forEach(function(b){b.addEventListener('click',function(){CP.sel=b.dataset.cp;vistaCompetencia(body)})});
}
function pintaCompProducto(body,r,l){
  var m=r&&r.mercado||{},p=r&&r.posicion;
  body.innerHTML=cabeceraComp()+'<p><button class="lnk" type="button" id="cpVolver">← Todos los juguetes</button></p>'+
    '<h3>'+esc(r?r.codigo+' · '+r.nombre:'')+'</h3>'+
    '<div class="grid g4">'+kpi('Tu precio',m2(r&&r.precioPropio),r&&r.origenPrecioPropio==='publicacion'?'Publicación activa más barata':r&&r.origenPrecioPropio?'Precio objetivo':'Sin precio publicado ni objetivo')+
      kpi('Mediana del mercado',m2(m.mediana),num(r?r.observaciones:0)+' precios vigentes · promedio '+m2(m.promedio))+
      kpi('Rango',m.minimo!=null?m2(m.minimo)+' – '+m2(m.maximo):'—','Mínimo y máximo')+
      kpi('Posición',p?(POS[p.posicion]||[0,p.posicion])[1]:'—',p?'Diferencia '+pct(p.diferenciaPct)+' · lugar '+p.lugar+' de '+p.total:'Captura tu precio objetivo o publica el juguete',p&&p.posicion==='caro'?'tone-bad':'')+'</div>'+
    (r&&r.mercadoBajoTuMinimo?'<div class="alert bad"><span class="dot"></span><div>La mediana del mercado ('+m2(m.mediana)+') está por debajo de tu precio mínimo ('+m2(r.precioMinimo)+'). Revisa si conviene otro canal o liquidar.</div></div>':'')+
    '<div class="pf"><span></span><button class="btn" type="button" id="cpAgregar">+ Precio para este juguete</button></div>'+
    (l.length?'<div class="scroll"><table><thead><tr><th></th><th>Producto</th><th>Dónde</th><th>Condición</th><th class="r">Publicado</th><th class="r">Envío</th><th class="r">Total</th><th>Consulta</th><th>Estado</th><th></th></tr></thead><tbody>'+
      l.map(function(x){var c=x.competencia;return '<tr'+(c.vigente?'':' class="muted"')+'><td>'+(x.imagen?'<img class="comp-img" src="'+esc(x.imagen)+'" alt="Foto de '+esc(c.productoReferencia)+'" loading="lazy">':'')+'</td>'+
        '<td class="wrap"><b>'+esc(c.productoReferencia)+'</b><span class="sub">'+esc([c.marca,c.modelo,c.codigoBarras].filter(Boolean).join(' · '))+'</span>'+(c.caracteristicas?'<span class="sub">'+esc(c.caracteristicas)+'</span>':'')+'</td>'+
        '<td>'+esc(x.plataforma||'')+(c.vendedor?'<span class="sub">'+esc(c.vendedor)+'</span>':'')+(c.url?' <a class="small" href="'+esc(c.url)+'" target="_blank" rel="noopener noreferrer">ver</a>':'')+'</td>'+
        '<td>'+esc(c.condicion?PC.condNombre(c.condicion):'—')+'</td><td class="r">'+m2(c.precioPublicado)+'</td><td class="r">'+m2(c.precioEnvio)+'</td><td class="r"><b>'+m2(c.precioTotal)+'</b></td>'+
        '<td>'+esc(fdate(c.fechaConsulta))+'</td><td>'+(c.vigente?'<span class="pill entregado">Vigente</span>':'<span class="pill C">Descartado</span>')+'</td>'+
        '<td class="r"><button class="btn sm" type="button" data-ce="'+esc(c.id)+'">Editar</button></td></tr>'}).join('')+'</tbody></table></div>':'<div class="empty"><p class="muted">Sin precios registrados para este juguete.</p></div>')+
    '<p class="small muted">Los precios descartados se conservan en el historial pero no cuentan en la comparación.</p>';
  var otra=function(){vistaCompetencia(body)};
  body.querySelector('#cpVolver').addEventListener('click',function(){CP.sel=null;otra()});
  body.querySelector('#cpNuevo').addEventListener('click',function(){compForm(null,CP.sel,otra)});
  body.querySelector('#cpAgregar').addEventListener('click',function(){compForm(null,CP.sel,otra)});
  body.querySelectorAll('[data-ce]').forEach(function(b){b.addEventListener('click',function(){var x=l.filter(function(y){return y.competencia.id===b.dataset.ce})[0];compForm(x,CP.sel,otra)})});
}
function compForm(x,pid,despues){
  var c=x?x.competencia:{productoId:pid||'',productoReferencia:'',plataformaId:'',plataformaTexto:'',marca:'',modelo:'',condicion:'',codigoBarras:'',caracteristicas:'',precioPublicado:'',precioEnvio:0,url:'',vendedor:'',fechaConsulta:PC.today(),observaciones:'',vigente:true};
  if(!x&&pid){var pr=d().D.productos[pid];if(pr){c.productoReferencia=pr.nombre;c.marca=pr.marca||'';c.modelo=pr.modelo||''}}
  var P=productos();
  PC.openModal(head(x?'Editar precio de competencia':'Nuevo precio de competencia')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('kPro','Tu juguete','<select class="sel" id="kPro" name="productoId" required>'+(c.productoId?'':'<option value="" selected disabled>Elige</option>')+P.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===c.productoId?' selected':'')+'>'+esc(p.codigo+' · '+p.nombre)+'</option>'}).join('')+'</select>',null,true)+
    fld('kRef','Producto de la competencia','<input class="inp" id="kRef" name="ref" maxlength="160" required value="'+esc(c.productoReferencia)+'">',null,true)+
    fld('kPla','Plataforma','<select class="sel" id="kPla" name="plataformaId"><option value="">Otra tienda (escríbela)</option>'+d().PLATS.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===c.plataformaId?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')+'</select>')+
    fld('kTie','Tienda','<input class="inp" id="kTie" name="tienda" maxlength="60" value="'+esc(c.plataformaTexto)+'" placeholder="Ej. Liverpool">','Si no está en el catálogo de plataformas.')+
    fld('kMar','Marca','<input class="inp" id="kMar" name="marca" maxlength="80" value="'+esc(c.marca)+'">')+
    fld('kMod','Modelo','<input class="inp" id="kMod" name="modelo" maxlength="80" value="'+esc(c.modelo)+'">')+
    fld('kCon','Condición','<select class="sel" id="kCon" name="condicion"><option value="">No se indica</option>'+PC.optConds(c.condicion)+'</select>')+
    fld('kCb','Código de barras','<input class="inp" id="kCb" name="cb" maxlength="20" inputmode="numeric" value="'+esc(c.codigoBarras)+'">')+
    fld('kPre','Precio publicado','<input class="inp num" id="kPre" name="precio" type="number" min="0.01" step="0.01" required value="'+esc(c.precioPublicado)+'">')+
    fld('kEnv','Envío','<input class="inp num" id="kEnv" name="envio" type="number" min="0" step="0.01" value="'+esc(c.precioEnvio)+'">','0 si es gratis.')+
    fld('kVen','Vendedor','<input class="inp" id="kVen" name="vendedor" maxlength="100" value="'+esc(c.vendedor)+'">')+
    fld('kFec','Fecha de consulta','<input class="inp" id="kFec" name="fecha" type="date" max="'+PC.today()+'" value="'+esc(c.fechaConsulta)+'">')+
    fld('kUrl','Enlace','<input class="inp" id="kUrl" name="url" type="url" maxlength="500" value="'+esc(c.url)+'" placeholder="https://…">',null,true)+
    fld('kCar','Características','<textarea class="inp" id="kCar" name="car" maxlength="600" placeholder="Piezas, tamaño, accesorios… (sirve para comparar si no hay código de barras)">'+esc(c.caracteristicas)+'</textarea>',null,true)+
    fld('kObs','Observaciones','<textarea class="inp" id="kObs" name="obs" maxlength="500">'+esc(c.observaciones)+'</textarea>',null,true)+
    fld('kImg','Foto','<input class="inp" id="kImg" name="foto" type="file" accept="image/jpeg,image/png,image/webp,image/gif">',x&&x.imagen?'Ya tiene foto; elige otra para reemplazarla.':'Opcional (máx. 5 MB).',true)+
    '<div class="f full"><label><input type="checkbox" name="vigente"'+(c.vigente?' checked':'')+'> Vigente (cuenta en la comparación)</label></div>'+
    '</div><p class="small" id="kTot"></p><div class="pf"><div>'+(x?'<button class="btn danger" type="button" id="kDel">Eliminar</button>':'')+'</div><div class="row"><button class="btn" type="button" data-c2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div></form>',function(el){
    var f=el.querySelector('form');
    function tot(){el.querySelector('#kTot').textContent='Precio total que se compara: '+money2(n(f.precio.value)+n(f.envio.value))}
    f.precio.addEventListener('input',tot);f.envio.addEventListener('input',tot);tot();
    el.querySelector('[data-c2]').addEventListener('click',PC.closeModal);
    var dl=el.querySelector('#kDel');if(dl)dl.addEventListener('click',function(){if(!confirm('¿Eliminar este precio? Si solo ya no aplica, mejor desmarca «Vigente» para conservar el historial.'))return;
      PC.api('DELETE','competencia/'+c.id).then(function(){PC.closeModal();PC.toast('Eliminado');despues()},err)});
    f.addEventListener('submit',function(e){e.preventDefault();var b=f.querySelector('[type=submit]');b.disabled=true;
      var o={productoId:f.productoId.value,productoReferencia:f.ref.value.trim(),plataformaId:f.plataformaId.value||null,plataformaTexto:f.tienda.value.trim(),marca:f.marca.value.trim(),modelo:f.modelo.value.trim(),
        condicion:f.condicion.value,codigoBarras:f.cb.value.trim(),caracteristicas:f.car.value.trim(),precioPublicado:n(f.precio.value),precioEnvio:n(f.envio.value),url:f.url.value.trim(),
        vendedor:f.vendedor.value.trim(),fechaConsulta:f.fecha.value||null,observaciones:f.obs.value.trim(),vigente:f.vigente.checked};
      PC.api(x?'PUT':'POST','competencia'+(x?'/'+c.id:''),o).then(function(g){
        var file=f.foto.files&&f.foto.files[0];if(!file)return g;
        var fd=new FormData();fd.append('imagen',file);
        return fetch('/api/competencia/'+g.id+'/imagen',{method:'POST',body:fd}).then(function(r){return r.ok?g:r.json().then(function(j){PC.toast('Se guardó el precio, pero no la foto: '+((j&&j.mensaje)||'error'));return g})})
      }).then(function(){PC.closeModal();PC.toast('Precio de competencia guardado');despues()},function(e2){err(e2);b.disabled=false});
    });
  });
}
/* Ficha del juguete: resumen de competencia */
function fichaCompetencia(pid,el){if(!el)return;
  PC.api('GET','competencia/resumen?productoId='+encodeURIComponent(pid)).then(function(rs){var r=rs[0];if(!r)return;var m=r.mercado,p=r.posicion;
    el.innerHTML='<h3 style="margin-top:14px">Competencia</h3>'+(m?'<p class="small">Mediana del mercado <b>'+m2(m.mediana)+'</b> ('+num(r.observaciones)+' precios, de '+m2(m.minimo)+' a '+m2(m.maximo)+')'+
      (p?' · tu precio '+m2(p.precioPropio)+' está '+pct(p.diferenciaPct)+' '+pill(p.posicion):'')+(r.mercadoBajoTuMinimo?' <span class="tone-bad">· debajo de tu mínimo</span>':'')+
      ' · <button class="lnk" type="button" data-irc>Ver detalle</button></p>':'<p class="small muted">Sin precios de competencia. <button class="lnk" type="button" data-irc>Registrar</button></p>');
    var b=el.querySelector('[data-irc]');if(b)b.addEventListener('click',function(){PC.closeModal();CP.sel=pid;PC.go('analisis');if(window.PCA)window.PCA.irA('competencia')});
  },function(){});
}

/* ================= Reportes ================= */
var REPORTES=[
  ['ventas','Ventas','Cada venta con ingreso, gastos, costo histórico, utilidad, cobro y efectivo.'],
  ['productos','Rentabilidad por juguete','Piezas, ingresos, utilidad, margen, rotación, días sin movimiento, costo y posición competitiva.'],
  ['plataformas','Por plataforma','Ventas, piezas, ingresos, utilidad y margen por canal.'],
  ['categorias','Por categoría','Ventas, ingresos, utilidad y margen por categoría.'],
  ['pallets','Capital por pallet','Inversión, capital recuperado y pendiente de cada pallet.'],
  ['inventario','Inventario por lote','Recibidas, vendidas, disponibles, antigüedad y costo de lo disponible.'],
  ['competencia','Competencia','Todos los precios registrados con la estadística del mercado y tu posición.'],
  ['liquidacion','Candidatos a liquidación','Juguetes sin movimiento con su costo, tu mínimo y el mercado.']];
function vistaReportes(body){
  body.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Reportes</h2>'+
    '<p class="muted small">Descarga en CSV (se abre en Excel con acentos). Los filtros aplican a ventas e indicadores; inventario y competencia usan juguete, categoría y pallet. Mismos datos y cálculos que el Dashboard.</p></div></div>'+filtros('rpF')+
    '<div class="grid g2">'+REPORTES.map(function(r){return '<div class="card"><h3>'+esc(r[1])+'</h3><p class="small muted">'+esc(r[2])+'</p><div><a class="btn sm primary" href="/api/reportes/'+r[0]+(query()?'?'+query():'')+'" download>Descargar CSV</a></div></div>'}).join('')+'</div>';
  wireFiltros(body,'rpF',function(){vistaReportes(body)});
}

function render(vista,body){ocultarTip();if(vista==='dashboard')return vistaDashboard(body);if(vista==='competencia')return vistaCompetencia(body);return vistaReportes(body)}
window.PCC={render:render,fichaCompetencia:fichaCompetencia};
})();
