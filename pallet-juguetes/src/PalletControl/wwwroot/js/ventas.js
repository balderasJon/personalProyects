/* Fase 3: ventas con detalle por lote, gastos reales, cobros e incidencias; Publicaciones y Rentabilidad real.
   Los resultados (utilidad, efectivo, capital recuperado) los calcula el servidor; aquí solo se capturan y se muestran.
   La vista previa del formulario es orientativa y se identifica como estimación. */
(function(){
'use strict';
var PC=window.PC,esc=PC.esc,money=PC.money,money2=PC.money2,num=PC.num,fdate=PC.fdate,kpi=PC.kpi,fld=PC.fld,head=PC.head,opt=PC.opt;
var TIPOS={comision:'Comisión',medio_pago:'Medio de pago',cargo_fijo:'Cargo fijo',logistica:'Logística',envio:'Envío',almacenamiento:'Almacenamiento',
  preparacion:'Preparación',empaque:'Empaque',publicidad:'Publicidad',devolucion:'Devolución',reclamacion:'Reclamación',otro:'Otro'};
var DEDUCIBLES=['comision','medio_pago','cargo_fijo','logistica','envio','almacenamiento'];
var INC={cancelacion:'Cancelación',devolucion:'Devolución',reembolso:'Reembolso',reclamacion:'Reclamación'};
var ORIGEN={costeo:'Costeo vigente',desactualizado:'Costeo desactualizado',estimado_migracion:'Estimado (venta anterior)',sin_costeo:'Sin costeo',mixto:'Mixto'};
var PUB_EST={pendiente:'Pendiente',publicado:'Publicado',pausado:'Pausado',vendido:'Vendido',cancelado:'Cancelado',eliminado:'Eliminado'};
var PUB_PILL={pendiente:'apartado',publicado:'entregado',pausado:'C',vendido:'pagado',cancelado:'cancelado',eliminado:'cancelado'};
var ACCION={pausar:'Pausar: ya no hay piezas',ajustar_existencias:'Ajustar existencias',reactivar:'Reactivar: volvió a haber piezas'};
function err(e){PC.toast((e&&e.mensaje)||'No se pudo completar la operación.')}
function d(){return PC.datos()}
function pct(v){return v==null?'—':(+v).toLocaleString('es-MX',{maximumFractionDigits:2})+' %'}
function m2(v){return v==null?'—':money2(v)}
function n(v){var x=+v;return isFinite(x)?x:0}
function plataformas(){return d().PLATS.filter(function(p){return p.activa})}
function optPlats(cur){return '<option value="">Sin plataforma (otro canal)</option>'+d().PLATS.filter(function(p){return p.activa||p.id===cur}).map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===cur?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')}
function recargarY(f){return PC.cargar().then(f)}
function form(html){return '<form style="display:flex;flex-direction:column;gap:14px">'+html+'</form>'}
function pie(txt){return '<div class="pf"><div></div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">'+(txt||'Guardar')+'</button></div></div>'}
function cerrar2(el){el.querySelectorAll('[data-close2]').forEach(function(b){b.addEventListener('click',PC.closeModal)})}
function enviar(el,f){var fo=el.querySelector('form');cerrar2(el);fo.addEventListener('submit',function(e){e.preventDefault();var b=fo.querySelector('[type=submit]');b.disabled=true;
  Promise.resolve(f(fo)).catch(function(x){err(x)}).then(function(){b.disabled=false})})}

/* ================= formulario de venta ================= */
function ventaForm(id,pid){
  var D=d().D,v=id?D.ventas[id]:{productoId:pid||'',cantidad:1,fecha:PC.today(),estado:'apartado',canal:'',descuento:0};
  if(id&&!v)return;
  var s=PC.stats(),P=s.P.slice().sort(function(a,b){return String(a.nombre).localeCompare(String(b.nombre),'es')});
  var opts=P.map(function(p){var dd=(+p.cantidad||0)-PC.vendidasDe(p.id,id);return '<option value="'+p.id+'"'+(p.id===v.productoId?' selected':'')+'>'+esc(p.codigo+' · '+p.nombre)+' ('+Math.max(0,dd)+' sin vender)</option>'}).join('');
  var first=!v.productoId&&P.length?'<option value="" selected disabled>Elige un juguete</option>':'';
  var cancelada=v.estado==='cancelado';
  var ests=PC.ESTADOS_V.filter(function(e){return e!=='cancelado'});
  PC.openModal(head(id?'Editar venta '+esc(PC.folio(v)):'Nueva venta')+form('<div class="form">'+
    fld('vPro','Juguete','<select class="sel" id="vPro" name="productoId" required'+(cancelada?' disabled':'')+'>'+first+opts+'</select>',null,true)+
    fld('vPla','Plataforma','<select class="sel" id="vPla" name="plataformaId">'+optPlats(v.plataformaId)+'</select>','Con plataforma, los gastos se precargan con su tarifa vigente.')+
    fld('vCanT','Canal (si no hay plataforma)','<input class="inp" id="vCanT" name="canal" maxlength="40" list="vCanales" value="'+esc(v.plataformaId?'':v.canal)+'"><datalist id="vCanales">'+PC.CANALES().map(function(c){return '<option value="'+esc(c)+'">'}).join('')+'</datalist>')+
    fld('vPub','Publicación','<select class="sel" id="vPub" name="publicacionId"><option value="">Sin publicación registrada</option></select>','Al vender, las demás publicaciones del juguete se revisan para evitar doble venta.')+
    fld('vLot','Lote','<select class="sel" id="vLot" name="loteId"><option value="">Automático: pallet más antiguo</option></select>')+
    fld('vCan2','Cantidad','<input class="inp num" id="vCan2" name="cantidad" type="number" min="1" step="1" required value="'+esc(v.cantidad)+'">')+
    fld('vPre','Precio por pieza','<input class="inp num" id="vPre" name="precio" type="number" min="0" step="0.01" required value="'+esc(v.precio!=null?v.precio:'')+'">','Lo que realmente pagó el cliente por pieza.')+
    fld('vPub2','Precio publicado','<input class="inp num" id="vPub2" name="precioPublicado" type="number" min="0" step="0.01" value="'+esc(v.precioPublicado||'')+'">','Precio del anuncio (para medir el descuento).')+
    fld('vDes','Descuento adicional','<input class="inp num" id="vDes" name="descuento" type="number" min="0" step="0.01" value="'+esc(v.descuento||0)+'">','Sobre el total (cupón, rebaja por combo…).')+
    fld('vEst','Estado','<select class="sel" id="vEst" name="estado"'+(cancelada?' disabled':'')+'>'+opt(ests,v.estado,PC.EST_V_TXT)+(cancelada?'<option value="cancelado" selected>Cancelado</option>':'')+'</select>','Pagado ≠ cobrado: el cobro se registra con el importe que llegó.')+
    fld('vFec','Fecha','<input class="inp" id="vFec" name="fecha" type="date" required value="'+esc(v.fecha)+'">')+
    fld('vCli','Cliente','<input class="inp" id="vCli" name="cliente" maxlength="80" value="'+esc(v.cliente)+'" placeholder="Nombre o perfil">',null,true)+
    fld('vNot','Notas','<textarea class="inp" id="vNot" name="notas" maxlength="400" placeholder="Punto y hora de entrega, combo…">'+esc(v.notas)+'</textarea>',null,true)+
    '</div>'+
    (id?'<p class="small muted">Los gastos, el cobro, las devoluciones y la cancelación se registran en el detalle de la venta.</p>':
      '<div class="card" style="padding:12px"><div class="pf"><h3 style="margin:0">Gastos de la venta</h3><div class="row"><button class="btn sm" type="button" id="gRe">Recalcular con la tarifa</button><button class="btn sm" type="button" id="gAdd">+ Gasto</button></div></div><div id="gTar" class="small muted"></div><div id="gLista"></div>'+
      '<div class="form">'+fld('vRi','Retención de ISR','<input class="inp num" id="vRi" name="retencionIsr" type="number" min="0" step="0.01" value="0">','La retiene la plataforma; no es gasto.')+
      fld('vRv','Retención de IVA','<input class="inp num" id="vRv" name="retencionIva" type="number" min="0" step="0.01" value="0">')+'</div></div>'+
      '<div class="form">'+fld('vCob','Importe cobrado (si ya llegó)','<input class="inp num" id="vCob" name="importeCobrado" type="number" min="0" step="0.01" placeholder="Vacío = aún no cobrada">','Lo que llegó a tu cuenta, ya sin lo que descontó la plataforma.')+
      fld('vFCob','Fecha de cobro','<input class="inp" id="vFCob" name="fechaCobro" type="date">')+'</div>')+
    '<div id="vWarn"></div><div id="vPrev"></div>'+
    '<div class="pf"><div>'+(id?'<button class="btn danger" type="button" id="mDel">Eliminar</button>':'')+'</div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div>'),
  function(el){
    var g=function(s){return el.querySelector(s)};
    var sp=g('#vPro'),pl=g('#vPla'),pub=g('#vPub'),lot=g('#vLot'),pr=g('#vPre'),cn=g('#vCan2'),de=g('#vDes'),fe=g('#vFec'),pp=g('#vPub2'),w=g('#vWarn'),prev=g('#vPrev');
    var touched=v.precio!=null,gastos=[],sucios=false,est=null,exist=[],pide=0,tEst=null;
    pr.addEventListener('input',function(){touched=true;cambia()});
    function pubs(){var lst=d().PUBS.filter(function(x){return x.productoId===sp.value&&x.estatus!=='cancelado'&&x.estatus!=='eliminado'&&(!pl.value||x.plataformaId===pl.value)});
      var cur=pub.value||v.publicacionId||'';
      pub.innerHTML='<option value="">Sin publicación registrada</option>'+lst.map(function(x){var nm=(d().PLATS.filter(function(p){return p.id===x.plataformaId})[0]||{}).nombre||'';
        return '<option value="'+esc(x.id)+'"'+(x.id===cur?' selected':'')+'>'+esc(nm+(x.idExterno?' · '+x.idExterno:'')+' · '+money2(x.precioPublicado)+' · '+(PUB_EST[x.estatus]||x.estatus))+'</option>'}).join('')}
    function lotes(){if(!sp.value){lot.innerHTML='<option value="">Automático: pallet más antiguo</option>';return}
      PC.api('GET','productos/'+sp.value+'/existencias').then(function(l){exist=l;var cur=lot.value;
        lot.innerHTML='<option value="">Automático: pallet más antiguo</option>'+l.map(function(x){return '<option value="'+esc(x.loteId)+'"'+(x.loteId===cur?' selected':'')+'>'+esc((x.pallet||'')+' · '+PC.condNombre(x.condicion)+' · '+Math.max(0,x.disponibles)+' disp.'+(x.comercializable?'':' (no vendible)'))+'</option>'}).join('');chk()},err)}
    function disponibles(){if(lot.value){var x=exist.filter(function(e){return e.loteId===lot.value})[0];return x?x.disponibles:0}
      return exist.filter(function(e){return e.comercializable}).reduce(function(a,e){return a+Math.max(0,e.disponibles)},0)}
    function bruto(){return Math.max(0,n(cn.value)*n(pr.value)-n(de.value))}
    function pintaGastos(){var L=g('#gLista');if(!L)return;
      L.innerHTML=gastos.length?'<div class="scroll"><table><thead><tr><th>Tipo</th><th>Concepto</th><th class="r">Monto</th><th>Lo descuenta la plataforma</th><th></th></tr></thead><tbody>'+gastos.map(function(x,i){
        return '<tr><td><select class="sel" data-gi="'+i+'" data-k="tipo">'+Object.keys(TIPOS).map(function(t){return '<option value="'+t+'"'+(t===x.tipo?' selected':'')+'>'+TIPOS[t]+'</option>'}).join('')+'</select></td>'+
          '<td><input class="inp" data-gi="'+i+'" data-k="concepto" maxlength="120" value="'+esc(x.concepto)+'"></td>'+
          '<td class="r"><input class="inp num" style="max-width:110px" data-gi="'+i+'" data-k="monto" type="number" min="0" step="0.01" value="'+esc(x.monto)+'"></td>'+
          '<td><input type="checkbox" data-gi="'+i+'" data-k="deducidoPorPlataforma"'+(x.deducidoPorPlataforma?' checked':'')+' aria-label="Lo descuenta la plataforma"></td>'+
          '<td class="r"><button class="btn sm" type="button" data-gq="'+i+'" aria-label="Quitar">×</button></td></tr>'}).join('')+'</tbody></table></div>'
        :'<p class="small muted">Sin gastos. Agrega comisión, envío o empaque si los hubo.</p>';
      L.querySelectorAll('[data-gi]').forEach(function(c){c.addEventListener(c.type==='checkbox'||c.tagName==='SELECT'?'change':'input',function(){var x=gastos[+c.dataset.gi],k=c.dataset.k;
        x[k]=c.type==='checkbox'?c.checked:k==='monto'?n(c.value):c.value;if(k==='tipo')x.deducidoPorPlataforma=DEDUCIBLES.indexOf(c.value)>=0;sucios=true;
        if(k==='tipo')pintaGastos();vista()})});
      L.querySelectorAll('[data-gq]').forEach(function(b){b.addEventListener('click',function(){gastos.splice(+b.dataset.gq,1);sucios=true;pintaGastos();vista()})})}
    function estimar(forzar){if(id)return;var T=g('#gTar');
      if(!sp.value||!pl.value){est=null;if(T)T.textContent=pl.value?'':'Sin plataforma: captura los gastos reales, si los hubo.';if(!sucios||forzar){gastos=[];pintaGastos()}vista();return}
      var q='ventas/estimar?productoId='+encodeURIComponent(sp.value)+'&plataformaId='+encodeURIComponent(pl.value)+'&precio='+n(pr.value)+'&cantidad='+Math.max(1,Math.round(n(cn.value)))+'&descuento='+n(de.value)+(fe.value?'&fecha='+fe.value:'');
      var k=++pide;PC.api('GET',q).then(function(r){if(k!==pide)return;est=r;
        T.innerHTML=r.tarifa?'Tarifa aplicada: <b>'+esc(r.tarifa.nombre)+'</b> (vigente desde '+esc(fdate(r.tarifa.vigenteDesde))+'). Los montos son estimados: corrígelos con lo que cobró la plataforma.':esc(r.aviso||'');
        if(!sucios||forzar){gastos=r.gastos.map(function(x){return {tipo:x.tipo,concepto:x.concepto,monto:x.monto,deducidoPorPlataforma:x.deducidoPorPlataforma}});sucios=false;pintaGastos();
          g('#vRi').value=r.retencionIsr;g('#vRv').value=r.retencionIva}
        vista()},err)}
    function vista(){chk();if(id||!prev)return;
      if(!sp.value){prev.innerHTML='';return}
      var b=bruto(),f=est?est.factorIngreso:1,neto=Math.round(b*f*100)/100,ded=0,pro=0;gastos.forEach(function(x){if(x.deducidoPorPlataforma)ded+=n(x.monto);else pro+=n(x.monto)});
      var cu=est&&est.costoUnitario!=null?est.costoUnitario:NaN,costo=cu*Math.max(1,Math.round(n(cn.value))),u=neto-ded-pro-costo;
      prev.innerHTML='<div class="callout small"><b>Estimación antes de guardar</b> (el resultado oficial lo calcula el servidor con el costo del lote): cobra el cliente '+money2(b)+
        (f<1?' · sin IVA '+money2(neto):'')+' · gastos '+money2(ded+pro)+' · costo '+(isFinite(costo)?money2(costo):'sin costeo')+' · <b>utilidad '+(isFinite(u)?money2(u):'—')+'</b>'+
        ' · depósito esperado '+money2(b-ded-n(g('#vRi').value)-n(g('#vRv').value))+(est&&est.fiscalPendiente?'<br>Régimen fiscal pendiente: sin IVA ni ISR.':'')+'</div>'}
    function chk(){var p=d().D.productos[sp.value],out='';
      if(p){if(!touched&&!id&&+p.precioObjetivo>0)pr.value=p.precioObjetivo;
        var dd=disponibles(),q=n(cn.value),pp0=n(pr.value);
        out+='<p class="small muted">Disponibles para vender'+(lot.value?' en el lote':'')+': '+Math.max(0,dd)+' · Objetivo '+money(+p.precioObjetivo)+(+p.precioMinimo>0?' · Mínimo '+money(+p.precioMinimo):'')+' · Total: <b>'+money2(bruto())+'</b></p>';
        if(!id&&exist.length&&q>dd)out+='<p class="warnline bad">Solo hay '+Math.max(0,dd)+' pieza'+(dd===1?'':'s')+' disponible'+(dd===1?'':'s')+'. Si está publicado en otros canales, revisa Publicaciones.</p>';
        else if(+p.precioMinimo>0&&pp0<+p.precioMinimo)out+='<p class="warnline">Precio debajo del mínimo que fijaron para este juguete.</p>'}
      w.innerHTML=out}
    function cambia(){clearTimeout(tEst);tEst=setTimeout(function(){estimar(false)},250);vista()}
    sp.addEventListener('change',function(){if(!id)touched=false;pubs();lotes();cambia()});
    pl.addEventListener('change',function(){pubs();if(!id){sucios=false}cambia()});
    pub.addEventListener('change',function(){var x=d().PUBS.filter(function(y){return y.id===pub.value})[0];if(x){if(x.plataformaId!==pl.value){pl.value=x.plataformaId;sucios=false}pp.value=x.precioPublicado;if(!touched){pr.value=x.precioPublicado;touched=true}}cambia()});
    [cn,de,fe].forEach(function(c){c.addEventListener('input',cambia)});lot.addEventListener('change',chk);
    if(g('#gAdd')){g('#gAdd').addEventListener('click',function(){gastos.push({tipo:'otro',concepto:'',monto:0,deducidoPorPlataforma:false});sucios=true;pintaGastos()});
      g('#gRe').addEventListener('click',function(){estimar(true)});['#vRi','#vRv'].forEach(function(s){g(s).addEventListener('input',vista)})}
    pubs();lotes();estimar(false);
    cerrar2(el);
    if(g('#mDel'))g('#mDel').addEventListener('click',function(){if(!confirm('¿Eliminar esta venta? Solo se permite si está apartada, sin cobro ni incidencias; si no, cancélala desde su detalle.'))return;
      PC.api('DELETE','ventas/'+id).then(function(){return PC.cargar()}).then(function(){PC.closeModal();PC.toast('Venta eliminada')},err)});
    var fo=el.querySelector('form');
    fo.addEventListener('submit',function(e){e.preventDefault();
      if(!sp.value){w.innerHTML='<p class="warnline bad">Elige un juguete.</p>';return}
      var q=Math.max(1,Math.round(n(cn.value)));
      var o={productoId:sp.value,cantidad:q,precio:n(pr.value),estado:cancelada?'cancelado':g('#vEst').value,fecha:fe.value||PC.today(),
        canal:pl.value?'':g('#vCanT').value.trim(),cliente:g('#vCli').value.trim(),notas:g('#vNot').value.trim(),
        plataformaId:pl.value||null,publicacionId:pub.value||null,loteId:lot.value||null,precioPublicado:pp.value===''?null:n(pp.value),descuento:n(de.value)};
      if(!id){o.gastos=gastos.filter(function(x){return n(x.monto)>0}).map(function(x){return {tipo:x.tipo,concepto:x.concepto,monto:n(x.monto),deducidoPorPlataforma:!!x.deducidoPorPlataforma,estimado:!sucios&&!!est&&!!est.tarifa}});
        o.retencionIsr=n(g('#vRi').value);o.retencionIva=n(g('#vRv').value);
        if(g('#vCob').value!==''){o.importeCobrado=n(g('#vCob').value);o.fechaCobro=g('#vFCob').value||o.fecha}}
      var b=fo.querySelector('[type=submit]');b.disabled=true;
      PC.api(id?'PUT':'POST','ventas'+(id?'/'+id:''),o).then(function(r){return PC.cargar().then(function(){PC.closeModal();PC.toast(id?'Venta actualizada':'Venta '+r.folio+' registrada');detalle(r.venta.id)})},function(x){err(x);b.disabled=false});
    });
  });
}

/* ================= detalle de la venta ================= */
function fila(l,v){return '<div class="dt"><dt>'+l+'</dt><dd>'+v+'</dd></div>'}
function detalle(id){
  PC.api('GET','ventas/'+id).then(function(x){pintaDetalle(x)},err);
}
function pintaDetalle(x){
  var v=x.venta,r=x.resultado,id=v.id,can=v.estado==='cancelado',quedan=v.cantidad-v.cantidadDevuelta;
  var estim=x.gastos.some(function(g){return g.estimado});
  var html=head('Venta '+esc(x.folio)+' <span class="pill '+v.estado+'">'+esc(PC.EST_V_TXT[v.estado])+'</span>')+
    '<div class="grid g2"><dl class="clean" style="display:flex;flex-direction:column;gap:6px;margin:0">'+
      fila('Juguete',x.producto?esc(x.producto.codigo+' · '+x.producto.nombre):'—')+
      fila('Canal',esc(x.plataforma||v.canal||'Sin canal')+(x.publicacion?' <span class="small muted">· publicación '+esc(x.publicacion.idExterno||'registrada')+'</span>':'')+(!v.plataformaId&&v.canal?' <span class="small muted">(sin plataforma asignada)</span>':''))+
      fila('Fecha',esc(fdate(v.fecha))+(v.cliente?' · '+esc(v.cliente):''))+
      fila('Piezas',num(v.cantidad)+' × '+money2(v.precio)+(v.descuento>0?' − descuento '+money2(v.descuento):'')+(v.precioPublicado&&v.precioPublicado!==v.precio?' <span class="small muted">(publicado a '+money2(v.precioPublicado)+')</span>':''))+
      (v.cantidadDevuelta?fila('Devueltas',num(v.cantidadDevuelta)):'')+
      fila('Tarifa',x.tarifa?esc(x.tarifa.nombre):'<span class="muted">—</span>')+
      fila('Cobro',r.cobrada?'<b>'+money2(v.importeCobrado)+'</b> el '+esc(fdate(v.fechaCobro)):'<span class="pill apartado">Sin cobro registrado</span>'+(r.porCobrarEstimado>0?' <span class="small muted">depósito esperado '+money2(r.porCobrarEstimado)+'</span>':''))+
      (v.notas?fila('Notas',esc(v.notas)):'')+
    '</dl>'+
    '<div class="card" style="padding:12px"><h3 style="margin:0">Resultado real</h3><div class="scroll" style="border:0"><table><tbody>'+
      '<tr><td>Cobrado al cliente</td><td class="r">'+m2(r.ingresoBruto)+'</td></tr>'+
      (r.ivaTrasladado?'<tr><td>− IVA trasladado</td><td class="r">'+m2(r.ivaTrasladado)+'</td></tr>':'')+
      '<tr><td><b>Ingreso</b></td><td class="r"><b>'+m2(r.ingresoNeto)+'</b></td></tr>'+
      (r.reembolsos?'<tr><td>− Reembolsos'+(r.reembolsosNetos!==r.reembolsos?' (sin IVA)':'')+'</td><td class="r">'+m2(r.reembolsosNetos)+'</td></tr>':'')+
      '<tr><td>− Gastos que descuenta la plataforma</td><td class="r">'+m2(r.gastosDeducidos)+'</td></tr>'+
      '<tr><td>− Gastos que pagas tú</td><td class="r">'+m2(r.gastosPropios)+'</td></tr>'+
      '<tr><td>− Costo histórico de lo vendido</td><td class="r">'+m2(r.costoVendido)+'</td></tr>'+
      (r.costoMerma?'<tr><td>− Merma por devoluciones</td><td class="r">'+m2(r.costoMerma)+'</td></tr>':'')+
      '<tr><td><b>Utilidad comercial</b></td><td class="r"><b class="'+(r.utilidad<0?'tone-bad':'tone-good')+'">'+m2(r.utilidad)+'</b>'+(r.margenPct!=null?'<span class="sub">margen '+pct(r.margenPct)+'</span>':'')+'</td></tr>'+
      '<tr><td>Efectivo neto (lo cobrado − lo que pagaste − reembolsos)</td><td class="r">'+(r.cobrada?m2(r.efectivoNeto):'<span class="muted">aún sin cobro</span>')+'</td></tr>'+
      (r.retencionIsr||r.retencionIva?'<tr><td>Retenciones (anticipo de impuestos, no gasto)</td><td class="r">ISR '+m2(r.retencionIsr)+' · IVA '+m2(r.retencionIva)+'</td></tr>':'')+
    '</tbody></table></div>'+(estim?'<p class="warnline small">Hay gastos estimados con la tarifa. Confírmalos o corrígelos con el estado de cuenta.</p>':'')+'</div></div>'+
    '<div class="card"><h3>Piezas y costo histórico</h3><div class="scroll" style="border:0"><table><thead><tr><th>Pallet</th><th>Condición</th><th class="r">Piezas</th><th class="r">Devueltas</th><th class="r">Costo por pieza</th><th>Origen del costo</th></tr></thead><tbody>'+
      x.detalle.map(function(l){return '<tr><td>'+esc(l.pallet||'')+'</td><td>'+esc(PC.condNombre(l.condicion||''))+'</td><td class="r">'+num(l.cantidad)+'</td><td class="r">'+num(l.devueltas)+(l.devueltasAMerma?'<span class="sub">'+num(l.devueltasAMerma)+' a merma</span>':'')+'</td><td class="r">'+m2(l.costoUnitario)+'</td><td>'+esc(ORIGEN[l.costoOrigen]||l.costoOrigen)+'</td></tr>'}).join('')+
    '</tbody></table></div><p class="small muted">El costo se congela al vender: recalcular el costeo del pallet no cambia ventas ya registradas.</p></div>'+
    '<div class="card"><div class="pf"><h3 style="margin:0">Gastos de la venta</h3><div class="row">'+(estim?'<button class="btn sm" type="button" id="dConf">Confirmar estimados</button>':'')+'<button class="btn sm" type="button" id="dGAdd">+ Gasto</button></div></div>'+
      (x.gastos.length?'<div class="scroll" style="border:0"><table><thead><tr><th>Tipo</th><th>Concepto</th><th>Quién lo paga</th><th class="r">Monto</th><th></th></tr></thead><tbody>'+x.gastos.map(function(g){
        return '<tr><td>'+esc(TIPOS[g.tipo]||g.tipo)+(g.estimado?' <span class="pill apartado">Estimado</span>':'')+(g.incidenciaId?' <span class="sub">por incidencia</span>':'')+'</td><td class="wrap">'+esc(g.concepto)+'</td><td>'+(g.deducidoPorPlataforma?'Lo descuenta la plataforma':'Tú')+'</td><td class="r">'+m2(g.monto)+'</td>'+
          '<td class="r"><button class="btn sm" type="button" data-ge="'+esc(g.id)+'">Editar</button></td></tr>'}).join('')+'</tbody></table></div>':'<p class="small muted">Sin gastos registrados.</p>')+'</div>'+
    (x.incidencias.length?'<div class="card"><h3>Incidencias</h3><ul class="clean">'+x.incidencias.map(function(i){
      return '<li><b>'+esc(INC[i.tipo]||i.tipo)+'</b> · '+esc(fdate(i.fecha))+(i.cantidad?' · '+num(i.cantidad)+' pieza(s)':'')+(i.condicionDestino?' · regresó como '+esc(PC.condNombre(i.condicionDestino)):'')+(i.montoReembolsado?' · reembolso '+money2(i.montoReembolsado):'')+
        (i.motivo?' <span class="small muted">— '+esc(i.motivo)+'</span>':'')+(i.estado==='abierta'?' <span class="pill apartado">Abierta</span> <button class="btn sm" type="button" data-ic="'+esc(i.id)+'">Cerrar</button>':'')+' <span class="small muted">('+esc(i.usuario)+')</span></li>'}).join('')+'</ul></div>':'')+
    '<div class="pf"><div class="row">'+(can?'':'<button class="btn danger" type="button" id="dCan">Cancelar venta</button>')+'</div><div class="row">'+
      (can?'':'<button class="btn" type="button" id="dEdit">Editar</button>')+
      (!can&&quedan>0?'<button class="btn" type="button" id="dDev">Devolución</button>':'')+
      (r.cobrada?'<button class="btn" type="button" id="dRee">Reembolso</button>':'')+
      '<button class="btn" type="button" id="dRec">Reclamación</button>'+
      (r.cobrada?'<button class="btn" type="button" id="dCob">Corregir cobro</button>':(can?'':'<button class="btn primary" type="button" id="dCob">Registrar cobro</button>'))+
    '</div></div>';
  PC.openModal(html,function(el){
    function on(s,f){var b=el.querySelector(s);if(b)b.addEventListener('click',f)}
    on('#dEdit',function(){ventaForm(id)});
    on('#dCob',function(){cobroForm(id)});
    on('#dCan',function(){cancelarForm(x)});
    on('#dDev',function(){devolucionForm(x)});
    on('#dRee',function(){reembolsoForm(x)});
    on('#dRec',function(){reclamacionForm(x)});
    on('#dGAdd',function(){gastoForm(id,null)});
    on('#dConf',function(){PC.api('POST','ventas/'+id+'/gastos/confirmar').then(function(){return recargarY(function(){detalle(id);PC.toast('Gastos confirmados')})},err)});
    el.querySelectorAll('[data-ge]').forEach(function(b){b.addEventListener('click',function(){gastoForm(id,x.gastos.filter(function(g){return g.id===b.dataset.ge})[0])})});
    el.querySelectorAll('[data-ic]').forEach(function(b){b.addEventListener('click',function(){cerrarReclamacionForm(x,b.dataset.ic)})});
  });
}

function filaGasto(prefijo,tipo){return '<div class="form">'+fld(prefijo+'T','Gasto adicional (opcional)','<select class="sel" id="'+prefijo+'T" name="gTipo">'+Object.keys(TIPOS).map(function(t){return '<option value="'+t+'"'+(t===tipo?' selected':'')+'>'+TIPOS[t]+'</option>'}).join('')+'</select>')+
  fld(prefijo+'M','Monto','<input class="inp num" id="'+prefijo+'M" name="gMonto" type="number" min="0" step="0.01" value="0">','Ej. envío de regreso, guía no reembolsable.')+
  fld(prefijo+'C','Concepto','<input class="inp" id="'+prefijo+'C" name="gConcepto" maxlength="120">',null,true)+
  '<div class="f full"><label><input type="checkbox" name="gDed"> Lo descontó la plataforma (no salió de tu bolsa)</label></div></div>'}
function gastoDe(f){var mto=n(f.elements.gMonto.value);return mto>0?[{tipo:f.elements.gTipo.value,concepto:f.elements.gConcepto.value.trim(),monto:mto,deducidoPorPlataforma:f.elements.gDed.checked}]:[]}
function hecho(id,msg){return PC.cargar().then(function(){PC.toast(msg);detalle(id)})}

function cobroForm(id,avance){
  var v=d().D.ventas[id];if(!v)return;
  var sug=(d().RES[id]||{}).porCobrarEstimado;
  PC.openModal(head((v.importeCobrado!=null?'Corregir cobro · ':'Registrar cobro · ')+esc(PC.folio(v)))+form(
    '<p class="small muted">Registra el importe que efectivamente llegó a tu cuenta (en marketplaces, el depósito ya sin comisiones). Una venta no se considera cobrada solo por registrarla o marcarla pagada.</p><div class="form">'+
    fld('cImp','Importe que llegó','<input class="inp num" id="cImp" name="importe" type="number" min="0" step="0.01" '+(avance?'':'required ')+'value="'+esc(v.importeCobrado!=null?v.importeCobrado:sug!=null?sug:'')+'">',sug!=null&&v.importeCobrado==null?'Depósito esperado según los gastos registrados: '+money2(sug):null)+
    fld('cFec','Fecha del depósito','<input class="inp" id="cFec" name="fecha" type="date" value="'+esc(v.fechaCobro||PC.today())+'">')+'</div>'+
    (avance?'<p class="small muted">Si aún no llega el dinero, deja el importe vacío: la venta quedará pagada, pendiente de cobro.</p>':'')+
    '<div class="pf"><div>'+(v.importeCobrado!=null?'<button class="btn danger" type="button" id="cQuitar">Quitar cobro</button>':'')+'</div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div>'),
  function(el){
    var q=el.querySelector('#cQuitar');if(q)q.addEventListener('click',function(){PC.api('DELETE','ventas/'+id+'/cobro').then(function(){return hecho(id,'Cobro quitado')},err)});
    enviar(el,function(f){var imp=f.elements.importe.value;
      if(avance)return PC.api('PATCH','ventas/'+id+'/estado',imp===''?{estado:avance}:{estado:avance,importeCobrado:n(imp),fechaCobro:f.elements.fecha.value||null}).then(function(){return PC.cargar().then(function(){PC.closeModal();PC.toast(imp===''?'Marcada como pagada (sin cobro registrado)':'Pagada y cobrada')})});
      return PC.api('POST','ventas/'+id+'/cobro',{importe:n(imp),fecha:f.elements.fecha.value||null}).then(function(){return hecho(id,'Cobro registrado')})});
  });
}
function cancelarForm(x){var v=x.venta;
  PC.openModal(head('Cancelar '+esc(x.folio))+form('<p class="small muted">Las piezas regresan a su lote y la venta deja de tener ingreso y costo. Los gastos estimados se quitan; los confirmados y el que indiques quedan como pérdida.</p><div class="form">'+
    fld('kMot','Motivo','<input class="inp" id="kMot" name="motivo" maxlength="300" required>',null,true)+
    fld('kFec','Fecha','<input class="inp" id="kFec" name="fecha" type="date" value="'+PC.today()+'">')+
    (x.resultado.cobrada?fld('kRee','Reembolsado al cliente','<input class="inp num" id="kRee" name="reembolso" type="number" min="0" step="0.01" value="'+esc(Math.max(0,v.precio*v.cantidad-v.descuento))+'">','Ya estaba cobrada.'):'')+'</div>'+
    filaGasto('kG','otro')+pie('Cancelar venta')),function(el){
    enviar(el,function(f){return PC.api('POST','ventas/'+v.id+'/cancelar',{motivo:f.elements.motivo.value.trim(),fecha:f.elements.fecha.value||null,
      montoReembolsado:f.elements.reembolso?n(f.elements.reembolso.value):0,gastos:gastoDe(f)}).then(function(){return hecho(v.id,'Venta cancelada')})})});
}
function devolucionForm(x){var v=x.venta,quedan=v.cantidad-v.cantidadDevuelta,lote=x.detalle[0]||{};
  PC.openModal(head('Devolución · '+esc(x.folio))+form('<p class="small muted">Las piezas regresan a su lote. Si vuelven dañadas o incompletas, se reclasifican como no vendibles y su costo queda como merma de esta venta.</p><div class="form">'+
    fld('dCant','Piezas devueltas','<input class="inp num" id="dCant" name="cantidad" type="number" min="1" max="'+quedan+'" step="1" required value="'+quedan+'">')+
    fld('dCond','Regresan como','<select class="sel" id="dCond" name="condicion">'+PC.optConds(lote.condicion)+'</select>')+
    fld('dRee','Reembolsado al cliente','<input class="inp num" id="dRee" name="reembolso" type="number" min="0" step="0.01" value="'+(x.resultado.cobrada?esc(Math.round((v.precio*quedan)*100)/100):0)+'">','Con IVA, como lo pagó.')+
    fld('dFec','Fecha','<input class="inp" id="dFec" name="fecha" type="date" value="'+PC.today()+'">')+
    fld('dMot','Motivo','<input class="inp" id="dMot" name="motivo" maxlength="300">',null,true)+'</div>'+
    filaGasto('dG','devolucion')+pie('Registrar devolución')),function(el){
    enviar(el,function(f){return PC.api('POST','ventas/'+v.id+'/devolucion',{cantidad:Math.round(n(f.elements.cantidad.value)),condicionDestino:f.elements.condicion.value,
      montoReembolsado:n(f.elements.reembolso.value),fecha:f.elements.fecha.value||null,motivo:f.elements.motivo.value.trim(),gastos:gastoDe(f)}).then(function(){return hecho(v.id,'Devolución registrada')})})});
}
function reembolsoForm(x){var v=x.venta;
  PC.openModal(head('Reembolso · '+esc(x.folio))+form('<p class="small muted">Devolución de dinero sin que regrese el juguete (p. ej. compensación por un detalle).</p><div class="form">'+
    fld('rMon','Monto reembolsado','<input class="inp num" id="rMon" name="monto" type="number" min="0.01" step="0.01" required>')+
    fld('rFec','Fecha','<input class="inp" id="rFec" name="fecha" type="date" value="'+PC.today()+'">')+
    fld('rMot','Motivo','<input class="inp" id="rMot" name="motivo" maxlength="300">',null,true)+'</div>'+filaGasto('rG','otro')+pie('Registrar reembolso')),function(el){
    enviar(el,function(f){return PC.api('POST','ventas/'+v.id+'/reembolso',{monto:n(f.elements.monto.value),fecha:f.elements.fecha.value||null,motivo:f.elements.motivo.value.trim(),gastos:gastoDe(f)}).then(function(){return hecho(v.id,'Reembolso registrado')})})});
}
function reclamacionForm(x){var v=x.venta;
  PC.openModal(head('Reclamación · '+esc(x.folio))+form('<div class="form">'+
    fld('qMot','¿Qué reclama el cliente?','<textarea class="inp" id="qMot" name="motivo" maxlength="300" required></textarea>',null,true)+
    fld('qFec','Fecha','<input class="inp" id="qFec" name="fecha" type="date" value="'+PC.today()+'">')+'</div>'+
    '<p class="small muted">Queda abierta. Al cerrarla registras lo que costó resolverla (reembolso o gastos).</p>'+pie('Registrar reclamación')),function(el){
    enviar(el,function(f){return PC.api('POST','ventas/'+v.id+'/reclamacion',{motivo:f.elements.motivo.value.trim(),fecha:f.elements.fecha.value||null}).then(function(){return hecho(v.id,'Reclamación registrada')})})});
}
function cerrarReclamacionForm(x,iid){
  PC.openModal(head('Cerrar reclamación · '+esc(x.folio))+form('<div class="form">'+
    fld('zRee','Reembolsado al cliente','<input class="inp num" id="zRee" name="reembolso" type="number" min="0" step="0.01" value="0">')+
    fld('zRes','Resolución','<input class="inp" id="zRes" name="resolucion" maxlength="200">',null,true)+'</div>'+filaGasto('zG','reclamacion')+pie('Cerrar')),function(el){
    enviar(el,function(f){return PC.api('POST','incidencias/'+iid+'/cerrar',{montoReembolsado:n(f.elements.reembolso.value),resolucion:f.elements.resolucion.value.trim(),gastos:gastoDe(f)}).then(function(){return hecho(x.venta.id,'Reclamación cerrada')})})});
}
function gastoForm(ventaId,g){
  g=g||{tipo:'envio',concepto:'',monto:'',deducidoPorPlataforma:false};
  PC.openModal(head(g.id?'Editar gasto':'Nuevo gasto de la venta')+form('<div class="form">'+
    fld('xTip','Tipo','<select class="sel" id="xTip" name="tipo">'+Object.keys(TIPOS).map(function(t){return '<option value="'+t+'"'+(t===g.tipo?' selected':'')+'>'+TIPOS[t]+'</option>'}).join('')+'</select>')+
    fld('xMon','Monto','<input class="inp num" id="xMon" name="monto" type="number" min="0.01" step="0.01" required value="'+esc(g.monto)+'">')+
    fld('xCon','Concepto','<input class="inp" id="xCon" name="concepto" maxlength="120" value="'+esc(g.concepto)+'">',null,true)+
    '<div class="f full"><label><input type="checkbox" name="ded"'+(g.deducidoPorPlataforma?' checked':'')+'> Lo descuenta la plataforma del depósito</label></div></div>'+
    (g.estimado?'<p class="small muted">Al guardar, el gasto queda confirmado (deja de ser estimado).</p>':'')+
    '<div class="pf"><div>'+(g.id?'<button class="btn danger" type="button" id="xDel">Eliminar</button>':'')+'</div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div>'),function(el){
    var tp=el.querySelector('#xTip');tp.addEventListener('change',function(){el.querySelector('[name=ded]').checked=DEDUCIBLES.indexOf(tp.value)>=0});
    var dl=el.querySelector('#xDel');if(dl)dl.addEventListener('click',function(){PC.api('DELETE','venta-gastos/'+g.id).then(function(){return hecho(ventaId,'Gasto eliminado')},err)});
    enviar(el,function(f){var o={tipo:f.elements.tipo.value,concepto:f.elements.concepto.value.trim(),monto:n(f.elements.monto.value),deducidoPorPlataforma:f.elements.ded.checked};
      return PC.api(g.id?'PUT':'POST',g.id?'venta-gastos/'+g.id:'ventas/'+ventaId+'/gastos',o).then(function(){return hecho(ventaId,'Gasto guardado')})});
  });
}

/* ================= Publicaciones ================= */
var st={pub:{soloAvisos:false,pide:0},real:{desde:'',hasta:'',plataformaId:'',palletId:'',pide:0}};
function render(vista,body){if(vista==='publicaciones')return vistaPublicaciones(body);return vistaReal(body)}
function vistaPublicaciones(body){var k=++st.pub.pide;
  PC.api('GET','publicaciones').then(function(l){if(k!==st.pub.pide)return;
    var av=l.filter(function(x){return x.publicacion.requiereAccion}).length;
    var L=st.pub.soloAvisos?l.filter(function(x){return x.publicacion.requiereAccion}):l.filter(function(x){return x.publicacion.estatus!=='eliminado'});
    body.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Publicaciones</h2>'+
      '<p class="muted small">Las existencias se controlan de forma central en el inventario. Cuando un juguete se vende en un canal, sus otras publicaciones piden pausarse o ajustar existencias. La aplicación no modifica tus anuncios: confirma aquí cuando lo hayas hecho en la plataforma.</p></div>'+
      '<button class="btn primary" type="button" id="puNueva">+ Publicación</button></div>'+
      (av?'<div class="alert bad"><span class="dot"></span><div><b>'+av+'</b> publicaci'+(av>1?'ones requieren':'ón requiere')+' atención para evitar una doble venta.</div></div>':'')+
      '<div class="row"><label class="small"><input type="checkbox" id="puSolo"'+(st.pub.soloAvisos?' checked':'')+'> Solo las que requieren atención</label></div>'+
      (L.length?'<div class="scroll"><table><thead><tr><th>Juguete</th><th>Plataforma</th><th>Estatus</th><th class="r">Precio</th><th class="r">Anunciadas</th><th class="r">Disponibles</th><th>Atención</th><th></th></tr></thead><tbody>'+L.map(function(x){var p=x.publicacion;
        return '<tr><td class="wrap"><b>'+esc(x.producto)+'</b>'+(p.idExterno?'<span class="sub">'+esc(p.idExterno)+'</span>':'')+'</td><td>'+esc(x.plataforma||'')+(p.url?' <a class="small" href="'+esc(p.url)+'" target="_blank" rel="noopener noreferrer">ver</a>':'')+'</td>'+
          '<td><span class="pill '+(PUB_PILL[p.estatus]||'C')+'">'+esc(PUB_EST[p.estatus]||p.estatus)+'</span></td><td class="r">'+money2(p.precioPublicado)+'</td><td class="r">'+num(p.existencias)+'</td><td class="r">'+num(x.disponibles)+'</td>'+
          '<td>'+(p.requiereAccion?'<span class="pill danado">'+esc(ACCION[p.requiereAccion]||p.requiereAccion)+'</span> <button class="btn sm" type="button" data-pa="'+esc(p.id)+'">Ya lo hice</button>':'')+'</td>'+
          '<td class="r"><button class="btn sm" type="button" data-pe="'+esc(p.id)+'">Editar</button></td></tr>'}).join('')+'</tbody></table></div>'
        :'<div class="empty"><p class="muted">'+(st.pub.soloAvisos?'No hay publicaciones que requieran atención.':'Registra dónde está publicado cada juguete para que la aplicación te avise al venderse en otro canal.')+'</p></div>');
    body.querySelector('#puNueva').addEventListener('click',function(){publicacionForm(null)});
    body.querySelector('#puSolo').addEventListener('change',function(e){st.pub.soloAvisos=e.target.checked;vistaPublicaciones(body)});
    body.querySelectorAll('[data-pe]').forEach(function(b){b.addEventListener('click',function(){publicacionForm(l.filter(function(x){return x.publicacion.id===b.dataset.pe})[0].publicacion)})});
    body.querySelectorAll('[data-pa]').forEach(function(b){b.addEventListener('click',function(){PC.api('POST','publicaciones/'+b.dataset.pa+'/atendida').then(function(){return PC.cargar()}).then(function(){PC.toast('Listo');vistaPublicaciones(body)},err)})});
  },err);
}
function publicacionForm(p){
  var D=d().D,nuevo=!p;p=p||{productoId:'',plataformaId:'',estatus:'publicado',existencias:1,precioPublicado:'',idExterno:'',url:'',observaciones:'',fechaPublicacion:PC.today()};
  var P=Object.keys(D.productos).map(function(k){return D.productos[k]}).sort(function(a,b){return String(a.nombre).localeCompare(String(b.nombre),'es')});
  PC.openModal(head(nuevo?'Nueva publicación':'Editar publicación')+form('<div class="form">'+
    fld('uPro','Juguete','<select class="sel" id="uPro" name="productoId" required>'+(p.productoId?'':'<option value="" selected disabled>Elige un juguete</option>')+P.map(function(x){return '<option value="'+esc(x.id)+'"'+(x.id===p.productoId?' selected':'')+'>'+esc(x.codigo+' · '+x.nombre)+'</option>'}).join('')+'</select>',null,true)+
    fld('uPla','Plataforma','<select class="sel" id="uPla" name="plataformaId" required>'+(p.plataformaId?'':'<option value="" selected disabled>Elige</option>')+d().PLATS.filter(function(x){return x.activa||x.id===p.plataformaId}).map(function(x){return '<option value="'+esc(x.id)+'"'+(x.id===p.plataformaId?' selected':'')+'>'+esc(x.nombre)+'</option>'}).join('')+'</select>')+
    fld('uEst','Estatus','<select class="sel" id="uEst" name="estatus">'+opt(Object.keys(PUB_EST),p.estatus,PUB_EST)+'</select>')+
    fld('uPre','Precio publicado','<input class="inp num" id="uPre" name="precio" type="number" min="0" step="0.01" required value="'+esc(p.precioPublicado)+'">')+
    fld('uExi','Piezas anunciadas','<input class="inp num" id="uExi" name="existencias" type="number" min="0" step="1" value="'+esc(p.existencias)+'">')+
    fld('uIde','Identificador en la plataforma','<input class="inp" id="uIde" name="idExterno" maxlength="80" value="'+esc(p.idExterno)+'" placeholder="Ej. número de publicación">')+
    fld('uFec','Fecha de publicación','<input class="inp" id="uFec" name="fecha" type="date" value="'+esc(p.fechaPublicacion||'')+'">')+
    fld('uUrl','Enlace','<input class="inp" id="uUrl" name="url" type="url" maxlength="500" value="'+esc(p.url)+'" placeholder="https://…">',null,true)+
    fld('uObs','Observaciones','<textarea class="inp" id="uObs" name="obs" maxlength="500">'+esc(p.observaciones)+'</textarea>',null,true)+'</div>'+
    '<div class="pf"><div>'+(nuevo?'':'<button class="btn danger" type="button" id="uDel">Eliminar</button>')+'</div><div class="row"><button class="btn" type="button" data-close2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div>'),function(el){
    var dl=el.querySelector('#uDel');if(dl)dl.addEventListener('click',function(){if(!confirm('¿Eliminar la publicación? Si tiene ventas, se conserva como eliminada.'))return;
      PC.api('DELETE','publicaciones/'+p.id).then(function(){return PC.cargar()}).then(function(){PC.closeModal();PC.toast('Publicación eliminada');rerender()},err)});
    enviar(el,function(f){var o={productoId:f.elements.productoId.value,plataformaId:f.elements.plataformaId.value,estatus:f.elements.estatus.value,precioPublicado:n(f.elements.precio.value),
      existencias:Math.max(0,Math.round(n(f.elements.existencias.value))),idExterno:f.elements.idExterno.value.trim(),url:f.elements.url.value.trim(),fechaPublicacion:f.elements.fecha.value||null,observaciones:f.elements.obs.value.trim()};
      return PC.api(nuevo?'POST':'PUT','publicaciones'+(nuevo?'':'/'+p.id),o).then(function(){return PC.cargar()}).then(function(){PC.closeModal();PC.toast('Publicación guardada');rerender()})});
  });
}
function rerender(){var b=document.getElementById('anBody');var cur=document.querySelector('[data-sub][aria-current="page"]');if(b&&cur&&(cur.dataset.sub==='publicaciones'||cur.dataset.sub==='real'))render(cur.dataset.sub,b)}

/* ================= Rentabilidad real ================= */
function tablaTotales(titulo,filas,clave){
  if(!filas.length)return '';
  return '<div class="card"><h3>'+titulo+'</h3><div class="scroll" style="border:0"><table><thead><tr><th>'+clave+'</th><th class="r">Ventas</th><th class="r">Piezas</th><th class="r">Ingreso</th><th class="r">Gastos</th><th class="r">Costo</th><th class="r">Utilidad</th><th class="r">Margen</th><th class="r">Cobrado</th><th class="r">Por cobrar</th></tr></thead><tbody>'+
    filas.map(function(x){var t=x.totales;return '<tr><td class="wrap">'+esc(x.nombre)+'</td><td class="r">'+num(t.ventas)+'</td><td class="r">'+num(t.piezas)+'</td><td class="r">'+m2(t.ingresoNeto-0)+'</td><td class="r">'+m2(t.gastos)+'</td><td class="r">'+m2(t.costoVendido+t.merma)+'</td>'+
      '<td class="r"><b class="'+(t.utilidad<0?'tone-bad':'')+'">'+m2(t.utilidad)+'</b></td><td class="r">'+pct(t.margenPct)+'</td><td class="r">'+m2(t.cobrado)+'</td><td class="r">'+m2(t.porCobrar)+'</td></tr>'}).join('')+'</tbody></table></div></div>'}
function vistaReal(body){var f=st.real,k=++f.pide;
  var q=['desde','hasta','plataformaId','palletId'].filter(function(x){return f[x]}).map(function(x){return x+'='+encodeURIComponent(f[x])}).join('&');
  PC.api('GET','rentabilidad-real'+(q?'?'+q:'')).then(function(r){if(k!==f.pide)return;var t=r.totales;
    body.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Rentabilidad real</h2>'+
      '<p class="muted small">Con el costo histórico de cada pieza y los gastos registrados en cada venta. Ingresos y utilidad incluyen ventas aún no cobradas; el efectivo y el capital recuperado solo cuentan lo cobrado.</p></div></div>'+
      '<form class="row" id="reF">'+
        '<label class="small">Desde <input class="inp" type="date" name="desde" value="'+esc(f.desde)+'"></label><label class="small">Hasta <input class="inp" type="date" name="hasta" value="'+esc(f.hasta)+'"></label>'+
        '<select class="sel" name="plataformaId" aria-label="Plataforma"><option value="">Todas las plataformas</option>'+d().PLATS.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===f.plataformaId?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')+'</select>'+
        '<select class="sel" name="palletId" aria-label="Pallet"><option value="">Todos los pallets</option>'+PC.palletsLista().map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===f.palletId?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')+'</select>'+
        '<button class="btn" type="submit">Aplicar</button></form>'+
      (r.fiscal.aviso?'<div class="callout sun small">'+esc(r.fiscal.aviso)+'</div>':'')+
      '<div class="grid g4">'+
        kpi('Ingresos',m2(t.ingresoNeto),num(t.ventas)+' ventas · '+num(t.piezas)+' piezas'+(t.ivaTrasladado?' · IVA '+m2(t.ivaTrasladado):''))+
        kpi('Utilidad comercial',m2(t.utilidad),'Margen '+pct(t.margenPct)+' · costo '+m2(t.costoVendido)+(t.merma?' · merma '+m2(t.merma):''),t.utilidad<0?'tone-bad':'tone-good')+
        kpi('Efectivo neto cobrado',m2(t.efectivoNeto),'Llegó '+m2(t.cobrado)+' · gastos de la venta '+m2(t.gastos)+(t.reembolsos?' · reembolsos '+m2(t.reembolsos):''))+
        kpi('Por cobrar (estimado)',m2(t.porCobrar),num(t.sinCobrar)+' venta'+(t.sinCobrar===1?'':'s')+' sin cobro registrado')+
      '</div>'+
      (t.retencionIsr||t.retencionIva?'<p class="small muted">Retenciones registradas (anticipos de impuestos, no gastos): ISR '+m2(t.retencionIsr)+' · IVA '+m2(t.retencionIva)+'.</p>':'')+
      '<div class="card"><h3>Capital recuperado por pallet</h3><p class="small muted">Efectivo neto de las ventas cobradas (acumulado, sin filtro de fechas) contra la inversión total del pallet.</p><div class="scroll" style="border:0"><table><thead><tr><th>Pallet</th><th class="r">Inversión</th><th class="r">Piezas vendidas</th><th class="r">Utilidad (periodo)</th><th class="r">Recuperado</th><th>Avance</th><th class="r">Falta</th></tr></thead><tbody>'+
        r.porPallet.map(function(p){var w=Math.max(0,Math.min(100,p.capitalRecuperadoPct||0));return '<tr><td>'+esc(p.pallet)+'</td><td class="r">'+m2(p.inversion)+'</td><td class="r">'+num(p.piezasVendidas)+'</td><td class="r '+(p.utilidad<0?'tone-bad':'')+'">'+m2(p.utilidad)+'</td><td class="r">'+m2(p.capitalRecuperado)+'</td>'+
          '<td style="min-width:120px"><div class="meter" style="height:8px"><i style="width:'+w+'%"></i></div><span class="sub">'+pct(p.capitalRecuperadoPct)+'</span></td><td class="r">'+m2(p.faltaPorRecuperar)+'</td></tr>'}).join('')+'</tbody></table></div></div>'+
      tablaTotales('Por plataforma',r.porPlataforma.map(function(x){return {nombre:x.plataforma,totales:x.totales}}),'Plataforma')+
      tablaTotales('Por juguete',r.porProducto.map(function(x){return {nombre:x.producto,totales:x.totales}}),'Juguete')+
      (r.ventas.length?'<div class="card"><h3>Ventas del periodo</h3><div class="scroll" style="border:0"><table><thead><tr><th>Folio</th><th>Fecha</th><th>Juguete</th><th>Canal</th><th>Estado</th><th class="r">Ingreso</th><th class="r">Gastos</th><th class="r">Costo</th><th class="r">Utilidad</th><th>Cobro</th></tr></thead><tbody>'+
        r.ventas.map(function(v){var x=v.resultado;return '<tr><td><button class="lnk" type="button" data-vd="'+esc(v.id)+'">'+esc(v.folio)+'</button></td><td>'+esc(fdate(v.fecha))+'</td><td class="wrap">'+esc(v.producto)+(v.costoOrigen!=='costeo'?'<span class="sub">'+esc(ORIGEN[v.costoOrigen]||v.costoOrigen)+'</span>':'')+'</td><td>'+esc(v.plataforma||'—')+'</td><td><span class="pill '+v.estado+'">'+esc(PC.EST_V_TXT[v.estado])+'</span></td>'+
          '<td class="r">'+m2(x.ingresoNeto)+'</td><td class="r">'+m2(x.gastosTotales)+'</td><td class="r">'+m2(x.costoVendido+x.costoMerma)+'</td><td class="r"><b class="'+(x.utilidad<0?'tone-bad':'')+'">'+m2(x.utilidad)+'</b></td><td>'+(v.estado==='cancelado'?'':x.cobrada?'<span class="pill entregado">Cobrada</span>':'<span class="pill apartado">Sin cobro</span>')+'</td></tr>'}).join('')+'</tbody></table></div></div>'
        :'<div class="empty"><p class="muted">No hay ventas con estos filtros.</p></div>');
    body.querySelector('#reF').addEventListener('submit',function(e){e.preventDefault();var fo=e.target;f.desde=fo.desde.value;f.hasta=fo.hasta.value;f.plataformaId=fo.plataformaId.value;f.palletId=fo.palletId.value;vistaReal(body)});
    body.querySelectorAll('[data-vd]').forEach(function(b){b.addEventListener('click',function(){detalle(b.dataset.vd)})});
  },err);
}

window.PCV={ventaForm:ventaForm,detalle:detalle,cobroForm:cobroForm,render:render,plataformas:plataformas};
})();
