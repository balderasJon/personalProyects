/* Análisis comercial — Fase 1: Pallets y costos.
   Usa las funciones compartidas de app.js (window.PC). Los cálculos los hace el servidor;
   esta pantalla solo los muestra y nunca redondea ni suma montos por su cuenta para decidir. */
(function(){
'use strict';
var PC=window.PC,esc=PC.esc,money=PC.money,money2=PC.money2,num=PC.num,fdate=PC.fdate,kpi=PC.kpi,fld=PC.fld,head=PC.head;
var EST={recepcion:'En recepción',en_venta:'En venta',liquidado:'Liquidado',cerrado:'Cerrado'};
var MET={proporcional:'Proporcional al valor comercial',equitativa:'Equitativa por pieza',manual:'Manual'};
var MERMA={absorber:'Absorber en las piezas vendibles',perdida:'Reconocer como merma (pérdida)'};
var TRAT={'':'Sin definir',merma:'Merma (pérdida)',liquidacion:'Liquidación',recuperacion_parcial:'Recuperación parcial'};
var GASTOS=['Flete adicional','Maniobras y descarga','Almacenaje','Reparación o limpieza','Impuestos o trámites','Otro'];

var st={sel:null,lista:null,det:null,sim:null,met:null,modo:null,manual:{},pide:0};
var body=document.getElementById('anBody');

function pillCosteo(c){
  if(!c)return '<span class="pill apartado">Sin calcular</span>';
  return c.desactualizado?'<span class="pill danado">Desactualizado</span>':'<span class="pill entregado">Vigente</span>';
}
function err(e){PC.toast((e&&e.mensaje)||'No se pudo completar la operación.')}

/* ---------- subnavegación de Análisis comercial ---------- */
var SUBS=['dashboard','productos','simulador','pallets','plataformas','competencia','publicaciones','real','reportes','integraciones'],vista='pallets';
try{var v0=localStorage.getItem('pallet-analisis-vista');if(SUBS.indexOf(v0)>=0)vista=v0}catch(e){}
function marcarSub(){document.querySelectorAll('[data-sub]').forEach(function(b){if(b.dataset.sub===vista)b.setAttribute('aria-current','page');else b.removeAttribute('aria-current')})}
document.querySelectorAll('[data-sub]').forEach(function(b){b.addEventListener('click',function(){vista=b.dataset.sub;try{localStorage.setItem('pallet-analisis-vista',vista)}catch(e){}
  if(window.PCP)window.PCP.reiniciar(vista);render()})});
function irA(v,opciones){vista=v;try{localStorage.setItem('pallet-analisis-vista',v)}catch(e){}if(window.PCP)window.PCP.reiniciar(v,opciones);render()}

/* ---------- render ---------- */
function render(){
  marcarSub();
  if((vista==='publicaciones'||vista==='real')&&window.PCV){window.PCV.render(vista,body);return}
  if((vista==='dashboard'||vista==='competencia'||vista==='reportes')&&window.PCC){window.PCC.render(vista,body);return}
  if(vista==='integraciones'&&window.PCI){window.PCI.render(body);return}
  if(vista!=='pallets'&&window.PCP){window.PCP.render(vista,body);return}
  var n=++st.pide;
  if(st.sel){
    PC.api('GET','pallets/'+st.sel).then(function(d){if(n!==st.pide)return;st.det=d;pintaDetalle()},function(e){if(e.status===404){st.sel=null;render()}else err(e)});
  }else{
    PC.api('GET','pallets').then(function(l){if(n!==st.pide)return;st.lista=l;pintaLista()},err);
  }
}

function pintaLista(){
  var l=st.lista||[],inv=0,pz=0,pend=0;
  l.forEach(function(x){inv+=x.pallet.inversionTotal;pz+=x.piezas;if(!x.costeo||x.costeo.desactualizado)pend++});
  body.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Pallets y costos</h2>'+
    '<p class="muted small">Registra cada pallet, sus gastos y reparte su inversión entre los juguetes para conocer el costo real de cada pieza.</p></div>'+
    '<button class="btn primary" type="button" id="anNuevo">+ Pallet</button></div>'+
    '<div class="grid g4">'+kpi('Pallets',num(l.length),'Registrados')+kpi('Inversión total',money2(inv),'Costo en MXN + gastos no incluidos')+
      kpi('Piezas',num(pz),'En todos los pallets')+kpi('Costeos por hacer',num(pend),pend?'Sin calcular o desactualizados':'Todos al día',pend?'tone-bad':'tone-good')+'</div>'+
    (l.length?'<div class="scroll"><table><thead><tr><th>Pallet</th><th>Compra</th><th>Proveedor</th><th class="r">Costo original</th><th class="r">Inversión total</th><th class="r">Piezas</th><th class="r">No vendibles</th><th>Costeo</th><th>Estatus</th></tr></thead><tbody>'+
      l.map(function(x){var p=x.pallet;return '<tr><td><button class="lnk nom" type="button" data-pal="'+esc(p.id)+'">'+esc(p.nombre)+'</button></td><td>'+esc(fdate(p.fechaCompra))+'</td><td>'+esc(p.proveedor||'—')+'</td>'+
        '<td class="r">'+(p.moneda==='MXN'?money2(p.costoOriginal):'US$'+num(p.costoOriginal))+'</td><td class="r"><b>'+money2(p.inversionTotal)+'</b></td><td class="r">'+num(x.piezas)+'</td><td class="r">'+num(x.piezas-x.comercializables)+'</td>'+
        '<td>'+pillCosteo(x.costeo)+'</td><td>'+esc(EST[p.estatus]||p.estatus)+'</td></tr>'}).join('')+'</tbody></table></div>'
     :'<div class="empty"><h3>Registra tu primer pallet</h3><p class="muted">Con su costo de compra, moneda y tipo de cambio. Después agrega sus juguetes y calcula el costo de cada uno.</p></div>');
  body.querySelector('#anNuevo').addEventListener('click',function(){palletForm(null)});
  body.querySelectorAll('[data-pal]').forEach(function(b){b.addEventListener('click',function(){st.sel=b.dataset.pal;st.sim=null;st.met=null;st.modo=null;st.manual={};render()})});
}

function pintaDetalle(){
  var d=st.det,p=d.pallet,ls=d.lotes,pz=0,com=0;
  ls.forEach(function(x){pz+=x.lote.cantidad;if(x.lote.comercializable)com+=x.lote.cantidad});
  var met=st.met||p.metodoCosteo,modo=st.modo||p.modoMerma;
  var gastos=d.gastos.map(function(g){return '<tr><td>'+esc(fdate(g.fecha))+'</td><td>'+esc(g.tipo)+'</td><td class="wrap">'+esc(g.concepto)+'</td><td class="r">'+money2(g.montoMxn)+'</td>'+
    '<td>'+(g.incluidoEnCosto?'<span class="small muted">Ya incluido (no se suma)</span>':'Se suma a la inversión')+'</td><td class="r"><button class="btn sm" type="button" data-gas="'+esc(g.id)+'">Editar</button></td></tr>'}).join('');
  var filas=ls.map(function(x){var l=x.lote,c=x.costo;
    return '<tr><td class="mono">'+esc(x.producto.codigo)+'</td><td class="wrap"><button class="lnk nom" type="button" data-det="'+esc(x.producto.id)+'">'+esc(x.producto.nombre)+'</button></td>'+
      '<td>'+esc(PC.condNombre(l.condicion))+'</td><td>'+(l.comercializable?'Sí':'<span class="pill danado">No</span> <span class="small muted">'+esc(TRAT[l.tratamiento||''])+'</span>')+'</td>'+
      '<td class="r">'+num(l.cantidad)+'</td><td class="r">'+(x.valorRelativo!=null?money2(x.valorRelativo)+(l.valorRelativoUnitario==null?'<span class="sub">precio en México</span>':'<span class="sub">capturado</span>'):'<span class="zero">—</span>')+'</td>'+
      '<td class="r">'+(c?money2(c.costoUnitario):'—')+'</td><td class="r">'+(c?money2(c.costoTotal)+(c.esMerma?'<span class="sub">merma</span>':''):'—')+'</td>'+
      '<td class="r"><div class="row" style="justify-content:flex-end;flex-wrap:nowrap"><button class="btn sm" type="button" data-lote-ed="'+esc(l.id)+'">Editar</button><button class="btn sm" type="button" data-lote-aj="'+esc(l.id)+'">Ajustar</button><button class="btn sm" type="button" data-lote-rc="'+esc(l.id)+'"'+(l.cantidad?'':' disabled')+'>Condición</button></div></td></tr>'}).join('');
  var vig=d.costeo;
  body.innerHTML='<div class="head"><div><p class="eyebrow"><button class="lnk" type="button" id="anVolver">← Pallets</button></p><h2>'+esc(p.nombre)+'</h2>'+
      '<p class="muted small">'+esc([p.proveedor,p.paisOrigen,'Compra '+fdate(p.fechaCompra),EST[p.estatus]].filter(Boolean).join(' · '))+'</p></div>'+
      '<div class="row"><button class="btn" type="button" id="anEditar">Editar pallet</button><button class="btn primary" type="button" id="anJuguete">+ Juguete en este pallet</button></div></div>'+
    '<div class="grid g4">'+
      kpi('Costo de compra',p.moneda==='MXN'?money2(p.costoOriginal):'US$'+num(p.costoOriginal),p.moneda==='MXN'?'En pesos':'Tipo de cambio '+PC.nf4.format(p.tipoCambio)+(p.tipoCambioFuente?' ('+p.tipoCambioFuente+')':'')+' = '+money2(p.costoMxn))+
      kpi('Gastos adicionales',money2(p.gastosAdicionales),'Solo los no incluidos en el costo')+
      kpi('Inversión total',money2(p.inversionTotal),'Lo que se reparte entre los juguetes')+
      kpi('Piezas',num(pz),num(com)+' vendibles · '+num(pz-com)+' no vendibles')+'</div>'+
    // costeo
    '<div class="card"><div class="pf"><h3>Costeo</h3>'+pillCosteo(vig?{desactualizado:vig.desactualizado}:null)+'</div>'+
      (vig?'<p class="small muted">Vigente desde '+esc(new Date(vig.corrida.fechaUtc).toLocaleString('es-MX'))+' · '+esc(MET[vig.corrida.metodo])+(vig.corrida.metodo!=='manual'?' · '+esc(MERMA[vig.corrida.modoMerma]):'')+'.'+
        (vig.desactualizado?' <b>Cambió la inversión, los juguetes o sus valores desde entonces: vuelve a calcular.</b>':'')+'</p>':'<p class="small muted">Aún no se reparte la inversión de este pallet.</p>')+
      '<div class="form">'+fld('anMet','Método','<select class="sel" id="anMet">'+PC.opt(['proporcional','equitativa','manual'],met,MET)+'</select>',
          met==='proporcional'?'Reparte según el valor comercial de cada pieza (precio en México o valor capturado en el lote).':met==='equitativa'?'Cada pieza vendible carga lo mismo.':'Captura el costo de cada lote; debe sumar exacto la inversión.')+
        (met==='manual'?'':fld('anModo','Piezas no vendibles','<select class="sel" id="anModo">'+PC.opt(['absorber','perdida'],modo,MERMA)+'</select>',
          modo==='absorber'?'Su costo lo cargan las vendibles; se informa aparte cuánto absorbieron.':'Reciben su parte y esa parte se reporta como merma.'))+'</div>'+
      (met==='manual'?'<div class="scroll"><table><thead><tr><th>Juguete</th><th>Condición</th><th class="r">Piezas</th><th class="r">Costo del lote</th></tr></thead><tbody>'+
        ls.map(function(x){var v=st.manual[x.lote.id];if(v==null&&x.costo)v=x.costo.costoTotal;return '<tr><td>'+esc(x.producto.codigo+' · '+x.producto.nombre)+'</td><td>'+esc(PC.condNombre(x.lote.condicion))+'</td><td class="r">'+num(x.lote.cantidad)+'</td>'+
          '<td class="r"><input class="inp num man" type="number" min="0" step="0.01" data-man="'+esc(x.lote.id)+'" value="'+(v!=null?v:'')+'" aria-label="Costo del lote de '+esc(x.producto.nombre)+'" style="max-width:140px"></td></tr>'}).join('')+
        '</tbody><tfoot><tr><td colspan="3">Suma capturada / inversión</td><td class="r" id="anManSum"></td></tr></tfoot></table></div>':'')+
      '<div class="row"><button class="btn" type="button" id="anSim"'+(ls.length?'':' disabled')+'>Simular</button><button class="btn primary" type="button" id="anApl"'+(ls.length?'':' disabled')+'>Aplicar costeo</button>'+
        '<span class="small muted">Simular no guarda nada. Aplicar guarda un costeo nuevo y conserva los anteriores en el historial.</span></div>'+
      '<div id="anRes"></div></div>'+
    // lotes
    '<div class="card"><div class="pf"><h3>Juguetes del pallet</h3><span class="small muted">'+num(ls.length)+' lote'+(ls.length===1?'':'s')+'</span></div>'+
      (ls.length?'<div class="scroll"><table><thead><tr><th>Código</th><th>Juguete</th><th>Condición</th><th>Vendible</th><th class="r">Piezas</th><th class="r">Valor relativo</th><th class="r">Costo pieza</th><th class="r">Costo lote</th><th></th></tr></thead><tbody>'+filas+'</tbody></table></div>'
        :'<p class="small muted">Agrega juguetes con “+ Juguete en este pallet”, escaneando su código o importando desde Excel.</p>')+'</div>'+
    // gastos
    '<div class="card"><div class="pf"><h3>Gastos del pallet</h3><button class="btn sm" type="button" id="anGasto">+ Gasto</button></div>'+
      (d.gastos.length?'<div class="scroll"><table><thead><tr><th>Fecha</th><th>Tipo</th><th>Concepto</th><th class="r">Monto</th><th>Efecto</th><th></th></tr></thead><tbody>'+gastos+'</tbody></table></div>'
        :'<p class="small muted">El costo de importación ya viene en el costo del pallet. Registra aquí solo gastos que no estén incluidos (flete adicional, maniobras, reparación…).</p>')+'</div>'+
    // historial
    '<div class="card"><h3>Historial de costeos</h3>'+(d.historial.length?'<div class="scroll"><table><thead><tr><th>Fecha</th><th>Usuario</th><th>Método</th><th class="r">Inversión</th><th class="r">Vendibles</th><th class="r">Merma</th><th></th><th></th></tr></thead><tbody>'+
      d.historial.map(function(h){return '<tr><td>'+esc(new Date(h.fechaUtc).toLocaleString('es-MX'))+'</td><td>'+esc(h.usuario)+'</td><td>'+esc(MET[h.metodo])+'</td><td class="r">'+money2(h.totalDistribuible)+'</td><td class="r">'+money2(h.totalComercializable)+'</td><td class="r">'+money2(h.totalMerma)+'</td>'+
        '<td>'+(h.vigente?'<span class="pill entregado">Vigente</span>':'')+'</td><td class="r"><button class="btn sm" type="button" data-cor="'+esc(h.id)+'">Ver</button></td></tr>'}).join('')+'</tbody></table></div>'
      :'<p class="small muted">Sin costeos todavía.</p>')+'</div>';

  body.querySelector('#anVolver').addEventListener('click',function(){st.sel=null;st.det=null;render()});
  body.querySelector('#anEditar').addEventListener('click',function(){palletForm(p)});
  body.querySelector('#anJuguete').addEventListener('click',function(){PC.productoForm(null,{palletId:p.id})});
  body.querySelector('#anGasto').addEventListener('click',function(){gastoForm(p.id,null)});
  body.querySelectorAll('[data-gas]').forEach(function(b){b.addEventListener('click',function(){gastoForm(p.id,d.gastos.filter(function(g){return g.id===b.dataset.gas})[0])})});
  body.querySelectorAll('[data-cor]').forEach(function(b){b.addEventListener('click',function(){verCorrida(b.dataset.cor)})});
  body.querySelector('#anMet').addEventListener('change',function(){st.met=this.value;st.sim=null;pintaDetalle()});
  var mo=body.querySelector('#anModo');if(mo)mo.addEventListener('change',function(){st.modo=this.value;st.sim=null;pintaDetalle()});
  function sumaManual(){var t=0;body.querySelectorAll('[data-man]').forEach(function(i){st.manual[i.dataset.man]=i.value===''?null:+i.value;t+=+i.value||0});
    var el=body.querySelector('#anManSum');if(el){var dif=Math.round((p.inversionTotal-t)*100)/100;el.innerHTML=money2(t)+' / '+money2(p.inversionTotal)+(dif===0?' <span class="tone-good">✓</span>':' <span class="tone-bad">faltan '+money2(dif)+'</span>')}}
  body.querySelectorAll('[data-man]').forEach(function(i){i.addEventListener('input',sumaManual)});sumaManual();
  body.querySelector('#anSim').addEventListener('click',function(){costeo(false)});
  body.querySelector('#anApl').addEventListener('click',function(){costeo(true)});
  if(st.sim)pintaResultado(st.sim);
}

function cuerpoCosteo(){
  var met=st.met||st.det.pallet.metodoCosteo,b={metodo:met,modoMerma:st.modo||st.det.pallet.modoMerma};
  if(met==='manual'){b.costosManuales={};Object.keys(st.manual).forEach(function(k){if(st.manual[k]!=null)b.costosManuales[k]=st.manual[k]})}
  return b;
}
function costeo(aplicar){
  var b=cuerpoCosteo();
  if(aplicar){var v=st.det.costeo;if(v&&!confirm('Se guardará un costeo nuevo y el actual pasará al historial (no se borra). Las ventas futuras usarán el costo nuevo. ¿Continuar?'))return;
    var nota=prompt('Nota para el historial (opcional):','')||'';b.notas=nota}
  PC.api('POST','pallets/'+st.sel+'/costeo/'+(aplicar?'aplicar':'simular'),b).then(function(r){
    var res=aplicar?r.resultado:r;st.sim=res;
    if(aplicar){PC.toast('Costeo aplicado');st.sim=null;PC.cargar()}else pintaResultado(res);
  },function(e){var el=body.querySelector('#anRes');if(el)el.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'});
}
function pintaResultado(r){
  var el=body.querySelector('#anRes');if(!el)return;
  if(!r.ok){el.innerHTML='<div class="warnline bad"><b>No se puede calcular:</b><ul class="clean">'+r.errores.map(function(e){return '<li>'+esc(e)+'</li>'}).join('')+'</ul></div>';return}
  var cuadra=Math.round((r.totalComercializable+r.totalMerma)*100)===Math.round(r.totalDistribuible*100);
  el.innerHTML='<h3 style="margin-top:6px">Simulación</h3><div class="scroll"><table><thead><tr><th>Juguete</th><th>Condición</th><th class="r">Piezas</th><th class="r">Valor relativo</th><th class="r">Costo pieza</th><th class="r">Costo lote</th></tr></thead><tbody>'+
    r.lotes.map(function(x){var o=x.resultado||{};return '<tr><td>'+esc(x.producto.codigo+' · '+x.producto.nombre)+'</td><td>'+esc(PC.condNombre(x.condicion))+(x.comercializable?'':' <span class="pill danado">no vendible</span>')+'</td><td class="r">'+num(x.cantidad)+'</td>'+
      '<td class="r">'+(x.valorRelativo!=null?money2(x.valorRelativo):'—')+'</td><td class="r">'+money2(o.costoUnitario||0)+'</td><td class="r"><b>'+money2(o.costoTotal||0)+'</b>'+(o.esMerma?'<span class="sub">merma</span>':'')+'</td></tr>'}).join('')+
    '</tbody></table></div>'+
    '<div class="cuadre"><div>Inversión total<b>'+money2(r.totalDistribuible)+'</b></div><div>Asignado a vendibles<b>'+money2(r.totalComercializable)+'</b></div><div>Merma reconocida<b>'+money2(r.totalMerma)+'</b></div>'+
      '<div>Cuadre<b class="'+(cuadra?'tone-good':'tone-bad')+'">'+(cuadra?'✓ Exacto al centavo':'✗ No cuadra')+'</b></div></div>'+
    '<p class="small muted">'+(r.costoAbsorbidoPorMerma!=null&&r.costoAbsorbidoPorMerma>0?'Las piezas no vendibles habrían cargado '+money2(r.costoAbsorbidoPorMerma)+'; ese monto lo absorben las vendibles. ':'')+
      (r.centavosAjustados?r.centavosAjustados+' centavo'+(r.centavosAjustados>1?'s':'')+' de redondeo asignado'+(r.centavosAjustados>1?'s':'')+' a los lotes con mayor residuo. ':'')+'Revisa y presiona “Aplicar costeo” para guardarlo.</p>';
}

/* ---------- formularios ---------- */
function palletForm(p){
  var nuevo=!p,tc=PC.cfg(),hoy=PC.TC().hoy||PC.today();
  var tcDef=p&&p.moneda==='USD'?p.tipoCambio:(tc.tipoCambioUsd||''),fuenteDef=p&&p.moneda==='USD'?(p.tipoCambioFuente||'Manual'):(tc.tipoCambioFuente==='DOF'&&tc.tipoCambioFecha===hoy?'DOF':'Manual');
  p=p||{nombre:'Pallet '+(((st.lista||[]).length)+1),fechaCompra:PC.today(),paisOrigen:'Estados Unidos',moneda:'USD',costoOriginal:'',estatus:'recepcion',metodoCosteo:'proporcional',modoMerma:'absorber'};
  PC.openModal(head(nuevo?'Nuevo pallet':'Editar pallet')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('plNom','Nombre o descripción','<input class="inp" id="plNom" name="nombre" required maxlength="80" value="'+esc(p.nombre)+'">',null,true)+
    fld('plPro','Proveedor','<input class="inp" id="plPro" name="proveedor" maxlength="80" value="'+esc(p.proveedor||'')+'">')+
    fld('plFec','Fecha de compra','<input class="inp" id="plFec" name="fechaCompra" type="date" required value="'+esc(p.fechaCompra)+'">')+
    fld('plPai','País de origen','<input class="inp" id="plPai" name="paisOrigen" maxlength="40" value="'+esc(p.paisOrigen||'')+'">')+
    fld('plMon','Moneda de compra','<select class="sel" id="plMon" name="moneda">'+PC.opt(['USD','MXN'],p.moneda,{USD:'Dólares (USD)',MXN:'Pesos (MXN)'})+'</select>')+
    fld('plCos','Costo de compra','<input class="inp num" id="plCos" name="costoOriginal" type="number" min="0" step="0.01" required value="'+esc(p.costoOriginal)+'">','Ya incluye la importación; no la sumes de nuevo.')+
    '<div class="f" id="plTcF">'+'<label for="plTc">Tipo de cambio aplicado</label><input class="inp num" id="plTc" name="tipoCambio" type="number" min="0.0001" step="0.0001" value="'+esc(tcDef)+'">'+
      '<select class="sel" name="tipoCambioFuente" aria-label="Fuente del tipo de cambio">'+PC.opt(['DOF','Manual'],fuenteDef,{DOF:'DOF',Manual:'Capturado a mano'})+'</select><span class="hint">Pesos por dólar. El valor sugerido es el del día.</span></div>'+
    '<div class="full"><p class="small" id="plPrev"></p></div>'+
    fld('plEst','Estatus','<select class="sel" id="plEst" name="estatus">'+PC.opt(Object.keys(EST),p.estatus,EST)+'</select>')+
    fld('plMet','Método de costeo predeterminado','<select class="sel" id="plMet" name="metodoCosteo">'+PC.opt(Object.keys(MET),p.metodoCosteo,MET)+'</select>')+
    fld('plMod','Piezas no vendibles','<select class="sel" id="plMod" name="modoMerma">'+PC.opt(Object.keys(MERMA),p.modoMerma,MERMA)+'</select>',null,true)+
    fld('plNot','Notas','<textarea class="inp" id="plNot" name="notas" maxlength="500">'+esc(p.notas||'')+'</textarea>',null,true)+
    '</div><div id="plMsg"></div>'+PC.footer(!nuevo)+'</form>',function(el){
      var f=el.querySelector('form'),msg=el.querySelector('#plMsg');
      function prev(){var usd=f.elements.moneda.value==='USD',c=+f.elements.costoOriginal.value||0,t=+f.elements.tipoCambio.value||0;
        el.querySelector('#plTcF').hidden=!usd;
        el.querySelector('#plPrev').innerHTML=usd?(t>0?'Costo en MXN: <b>'+money2(Math.round(c*t*100)/100)+'</b> (el servidor lo redondea al centavo).':'<span class="tone-bad">Captura el tipo de cambio.</span>'):''}
      ['moneda','costoOriginal','tipoCambio'].forEach(function(n){f.elements[n].addEventListener('input',prev)});prev();
      PC.wireForm(el,function(){
        var b={nombre:f.elements.nombre.value.trim(),proveedor:f.elements.proveedor.value.trim(),fechaCompra:f.elements.fechaCompra.value,paisOrigen:f.elements.paisOrigen.value.trim(),
          moneda:f.elements.moneda.value,costoOriginal:+f.elements.costoOriginal.value||0,tipoCambio:+f.elements.tipoCambio.value||0,tipoCambioFuente:f.elements.tipoCambioFuente.value,
          estatus:f.elements.estatus.value,metodoCosteo:f.elements.metodoCosteo.value,modoMerma:f.elements.modoMerma.value,notas:f.elements.notas.value};
        (nuevo?PC.api('POST','pallets',b):PC.api('PUT','pallets/'+p.id,b)).then(function(r){PC.closeModal();PC.toast(nuevo?'Pallet registrado':'Pallet actualizado');if(nuevo)st.sel=r.id;PC.cargar()},
          function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'});
      },function(){PC.api('DELETE','pallets/'+p.id).then(function(){PC.closeModal();PC.toast('Pallet eliminado');st.sel=null;PC.cargar()},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})});
    });
}

function gastoForm(palletId,g){
  var nuevo=!g;g=g||{fecha:PC.today(),tipo:'Flete adicional',incluidoEnCosto:false};
  PC.openModal(head(nuevo?'Gasto del pallet':'Editar gasto')+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('ggTip','Tipo','<select class="sel" id="ggTip" name="tipo">'+PC.opt(GASTOS,g.tipo)+'</select>')+
    fld('ggFec','Fecha','<input class="inp" id="ggFec" name="fecha" type="date" required value="'+esc(g.fecha)+'">')+
    fld('ggCon','Concepto','<input class="inp" id="ggCon" name="concepto" required maxlength="120" value="'+esc(g.concepto||'')+'">',null,true)+
    fld('ggMon','Monto (MXN)','<input class="inp num" id="ggMon" name="montoMxn" type="number" min="0" step="0.01" required value="'+esc(g.montoMxn!=null?g.montoMxn:'')+'">')+
    '<div class="f"><label class="chk"><input type="checkbox" name="incluidoEnCosto"'+(g.incluidoEnCosto?' checked':'')+'> Ya está incluido en el costo de compra</label><span class="hint">Si lo marcas, solo se registra y no se suma a la inversión.</span></div>'+
    '</div><div id="ggMsg"></div>'+PC.footer(!nuevo)+'</form>',function(el){
      var f=el.querySelector('form'),msg=el.querySelector('#ggMsg');
      PC.wireForm(el,function(){
        var b={tipo:f.elements.tipo.value,fecha:f.elements.fecha.value,concepto:f.elements.concepto.value.trim(),montoMxn:+f.elements.montoMxn.value||0,incluidoEnCosto:f.elements.incluidoEnCosto.checked};
        (nuevo?PC.api('POST','pallets/'+palletId+'/gastos',b):PC.api('PUT','pallet-gastos/'+g.id,b)).then(function(){PC.closeModal();PC.toast('Gasto guardado');PC.cargar()},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'});
      },function(){PC.api('DELETE','pallet-gastos/'+g.id).then(function(){PC.closeModal();PC.toast('Gasto eliminado');PC.cargar()},function(e){msg.innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})});
    });
}

function verCorrida(id){
  PC.api('GET','costeo/corridas/'+id).then(function(r){var c=r.corrida;
    PC.openModal(head('Costeo del '+esc(new Date(c.fechaUtc).toLocaleString('es-MX')))+
      '<p class="small muted">'+esc(MET[c.metodo])+(c.metodo!=='manual'?' · '+esc(MERMA[c.modoMerma]):'')+' · por '+esc(c.usuario)+(c.vigente?' · <b>vigente</b>':' · histórico (no se modifica)')+(c.notas?' · '+esc(c.notas):'')+'</p>'+
      '<div class="scroll"><table><thead><tr><th>Juguete</th><th>Condición</th><th class="r">Piezas</th><th class="r">Valor relativo</th><th class="r">Costo pieza</th><th class="r">Costo lote</th></tr></thead><tbody>'+
      r.costos.map(function(x){var k=x.costo;return '<tr><td>'+esc(x.producto?x.producto.codigo+' · '+x.producto.nombre:'(juguete eliminado)')+'</td><td>'+esc(PC.condNombre(k.condicion))+'</td><td class="r">'+num(k.cantidad)+'</td><td class="r">'+(k.valorRelativoUnitario!=null?money2(k.valorRelativoUnitario):'—')+'</td><td class="r">'+money2(k.costoUnitario)+'</td><td class="r">'+money2(k.costoTotal)+(k.esMerma?'<span class="sub">merma</span>':'')+'</td></tr>'}).join('')+
      '</tbody><tfoot><tr><td colspan="5">Inversión '+money2(c.totalDistribuible)+' = vendibles '+money2(c.totalComercializable)+' + merma '+money2(c.totalMerma)+'</td><td class="r">'+money2(c.totalComercializable+c.totalMerma)+'</td></tr></tfoot></table></div>');
  },err);
}

/* ---------- acciones sobre lotes (también desde la ficha del juguete) ---------- */
function buscarLote(id){var L=PC.datos().LOTES,r=null;Object.keys(L).forEach(function(k){L[k].forEach(function(l){if(l.id===id)r=l})});return r}
function nombreProd(pid){var p=PC.datos().D.productos[pid];return p?p.codigo+' · '+p.nombre:''}
function trasLote(msg){PC.closeModal();PC.toast(msg);PC.cargar()}
function loteAccion(ds){
  if(ds.loteNuevo)return loteNuevo(ds.loteNuevo);
  if(ds.loteMov)return movimientos(ds.loteMov);
  var l=buscarLote(ds.loteAj||ds.loteRc||ds.loteEd);if(!l)return PC.toast('El lote ya no existe');
  var t=esc(nombreProd(l.productoId))+' <span class="small muted">· '+esc(PC.condNombre(l.condicion))+' · '+num(l.cantidad)+' pzas</span>';
  if(ds.loteAj){
    PC.openModal(head('Ajustar piezas')+'<p class="small">'+t+'</p><form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
      fld('laCan','Piezas a sumar o restar','<input class="inp num" id="laCan" name="cantidad" type="number" step="1" required placeholder="p. ej. 3 o -2">','Positivo si encontraste más; negativo si faltan.')+
      fld('laMot','Motivo','<input class="inp" id="laMot" name="motivo" required maxlength="200" placeholder="Conteo físico, extravío…">')+
      '</div><div id="laMsg"></div>'+PC.footer(false)+'</form>',function(el){var f=el.querySelector('form');
        PC.wireForm(el,function(){PC.api('POST','lotes/'+l.id+'/ajuste',{cantidad:Math.round(+f.elements.cantidad.value||0),motivo:f.elements.motivo.value}).then(function(){trasLote('Ajuste registrado')},function(e){el.querySelector('#laMsg').innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})})});
  }else if(ds.loteRc){
    PC.openModal(head('Cambiar condición')+'<p class="small">'+t+'</p><form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
      fld('lrCan','Piezas','<input class="inp num" id="lrCan" name="cantidad" type="number" min="1" max="'+l.cantidad+'" step="1" required value="'+l.cantidad+'">')+
      fld('lrCon','Nueva condición','<select class="sel" id="lrCon" name="condicion">'+PC.optConds(l.condicion==='nuevo_sellado'?'nuevo_caja_abierta':'nuevo_sellado')+'</select>')+
      fld('lrMot','Motivo','<input class="inp" id="lrMot" name="motivo" maxlength="200" placeholder="Caja rota, falta una pieza…">',null,true)+
      '</div><div id="lrMsg"></div>'+PC.footer(false)+'</form>',function(el){var f=el.querySelector('form');
        PC.wireForm(el,function(){PC.api('POST','lotes/'+l.id+'/reclasificar',{cantidad:Math.round(+f.elements.cantidad.value||0),condicion:f.elements.condicion.value,motivo:f.elements.motivo.value}).then(function(){trasLote('Condición actualizada')},function(e){el.querySelector('#lrMsg').innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})})});
  }else{
    PC.openModal(head('Editar lote')+'<p class="small">'+t+'</p><form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
      '<div class="f full"><label class="chk"><input type="checkbox" name="comercializable"'+(l.comercializable?' checked':'')+(l.condicion==='no_comercializable'?' disabled':'')+'> Se puede vender</label><span class="hint">Por ejemplo, un incompleto que vas a liquidar.</span></div>'+
      fld('leTra','Tratamiento si no se vende','<select class="sel" id="leTra" name="tratamiento">'+PC.opt(Object.keys(TRAT),l.tratamiento||'',TRAT)+'</select>')+
      fld('leVal','Valor relativo por pieza','<input class="inp num" id="leVal" name="valorRelativo" type="number" min="0" step="0.01" value="'+(l.valorRelativoUnitario!=null?l.valorRelativoUnitario:'')+'" placeholder="Vacío = precio en México">','Base del costeo proporcional. Déjalo vacío para usar el precio en México del juguete.')+
      fld('leNot','Notas','<input class="inp" id="leNot" name="notas" maxlength="300" value="'+esc(l.notas||'')+'">',null,true)+
      '</div><div id="leMsg"></div>'+PC.footer(false)+'</form>',function(el){var f=el.querySelector('form');
        PC.wireForm(el,function(){var v=f.elements.valorRelativo.value;
          PC.api('PUT','lotes/'+l.id,{comercializable:f.elements.comercializable.checked,tratamiento:f.elements.tratamiento.value,valorRelativoUnitario:v===''?null:+v,notas:f.elements.notas.value})
            .then(function(){trasLote('Lote actualizado')},function(e){el.querySelector('#leMsg').innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})})});
  }
}
function loteNuevo(pid){
  var pls=PC.palletsLista();
  PC.openModal(head('Nuevo lote')+'<p class="small">'+esc(nombreProd(pid))+'</p><form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('lnPal','Pallet','<select class="sel" id="lnPal" name="palletId" required>'+PC.optPallets(pls.length?pls[0].id:'')+'</select>')+
    fld('lnCon','Condición','<select class="sel" id="lnCon" name="condicion">'+PC.optConds('nuevo_sellado')+'</select>')+
    fld('lnCan','Piezas','<input class="inp num" id="lnCan" name="cantidad" type="number" min="1" step="1" required value="1">')+
    fld('lnVal','Valor relativo por pieza','<input class="inp num" id="lnVal" name="valor" type="number" min="0" step="0.01" placeholder="Vacío = precio en México">')+
    '</div><div id="lnMsg"></div>'+PC.footer(false)+'</form>',function(el){var f=el.querySelector('form');
      PC.wireForm(el,function(){var v=f.elements.valor.value;
        PC.api('POST','productos/'+pid+'/lotes',{palletId:f.elements.palletId.value,condicion:f.elements.condicion.value,cantidad:Math.round(+f.elements.cantidad.value||0),valorRelativoUnitario:v===''?null:+v})
          .then(function(){trasLote('Lote agregado')},function(e){el.querySelector('#lnMsg').innerHTML='<p class="warnline bad">'+esc(e.mensaje)+'</p>'})})});
}
var TIPOS_MOV={inventario_inicial:'Saldo inicial',recepcion:'Recepción',ajuste:'Ajuste',reclasificacion_salida:'Cambio de condición (sale)',reclasificacion_entrada:'Cambio de condición (entra)',venta:'Venta',cancelacion_venta:'Venta cancelada o editada (regresa)',devolucion:'Devolución del cliente'};
function movimientos(pid){
  PC.api('GET','productos/'+pid+'/lotes').then(function(r){
    var lotes={};r.lotes.forEach(function(x){lotes[x.lote.id]=x});
    PC.openModal(head('Movimientos de inventario')+'<p class="small">'+esc(nombreProd(pid))+'</p>'+
      (r.movimientos.length?'<div class="scroll"><table><thead><tr><th>Fecha</th><th>Movimiento</th><th>Lote</th><th class="r">Piezas</th><th>Motivo</th><th>Usuario</th></tr></thead><tbody>'+
        r.movimientos.map(function(m){var x=lotes[m.loteId];return '<tr><td>'+esc(new Date(m.fechaUtc).toLocaleString('es-MX'))+'</td><td>'+esc(TIPOS_MOV[m.tipo]||m.tipo)+'</td><td>'+(x?esc((x.pallet||'')+' · '+PC.condNombre(x.lote.condicion)):'')+'</td>'+
          '<td class="r '+(m.cantidad<0?'tone-bad':'tone-good')+'">'+(m.cantidad>0?'+':'')+num(m.cantidad)+'</td><td class="wrap small">'+esc(m.motivo)+'</td><td>'+esc(m.usuario)+'</td></tr>'}).join('')+'</tbody></table></div>'
        :'<p class="small muted">Sin movimientos.</p>')+
      '<p class="small muted">Las salidas por venta, cancelación y devolución muestran la existencia; no cambian las piezas recibidas (la cantidad que se costea).</p>');
  },err);
}

window.PCA={render:render,loteAccion:loteAccion,irA:irA};
})();
