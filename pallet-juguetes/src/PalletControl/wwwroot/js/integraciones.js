/* Fase 5: Integraciones por API oficial. Leer la plataforma genera propuestas; nada se aplica sin confirmarlo aquí. */
(function(){
'use strict';
var PC=window.PC,esc=PC.esc,money2=PC.money2,num=PC.num,fld=PC.fld,head=PC.head;
var EST={sin_configurar:['C','Sin configurar'],pendiente_autorizacion:['apartado','Por conectar'],conectada:['entregado','Conectada'],error:['danado','Error']};
var TIPO={pausar:'Pausar anuncio',activar:'Reactivar anuncio',existencias:'Ajustar piezas',precio:'Enviar precio',nueva_venta:'Registrar venta',cancelar_venta:'Cancelar venta',registrar_cobro:'Registrar cobro',informativa:'Revisar'};
var PEST={pendiente:['apartado','Pendiente'],aplicada:['entregado','Aplicada'],rechazada:['C','No aplicada'],error:['danado','Error'],bloqueada:['C','Revisar a mano']};
var LEST={pendiente:'Pendiente de confirmar',aplicado:'Aplicado',parcial:'Aplicado con errores',error:'Con errores',descartado:'Descartado'};
var BLOQ={no_encontrada:'No se encontró el anuncio',variaciones:'Tiene variaciones',sin_vinculo:'Anuncio sin vincular'};
function err(e){PC.toast((e&&e.mensaje)||'No se pudo completar la operación.')}
function pill(m,k){var x=m[k]||['C',k];return '<span class="pill '+x[0]+'">'+esc(x[1])+'</span>'}
function fdt(s){if(!s)return '—';try{return new Date(s).toLocaleString('es-MX',{day:'numeric',month:'short',hour:'2-digit',minute:'2-digit'})}catch(e){return s}}
var st={datos:null,lote:null,cuerpo:null};
// Regreso de la autorización (Redirect URI apuntando a esta aplicación): abrir directamente Integraciones.
var REGRESO=(/[?&]integracion=(ok|error)/.exec(location.search)||[])[1]||null;
if(REGRESO){try{localStorage.setItem('pallet-analisis-vista','integraciones')}catch(e){}}

function render(body){st.cuerpo=body;
  if(REGRESO){PC.toast(REGRESO==='ok'?'Cuenta de Mercado Libre conectada':'No se pudo completar la conexión: pega la dirección de regreso en «Conectar».');
    try{history.replaceState(null,'',location.pathname+'#analisis')}catch(e){}REGRESO=null}
  PC.api('GET','integraciones').then(function(d){st.datos=d;pinta()},err);
}
function recargar(){render(st.cuerpo)}
function conector(c){return st.datos.conectores.filter(function(x){return x.codigo===c})[0]}

function pinta(){var d=st.datos,b=st.cuerpo;
  b.innerHTML='<div class="head"><div><p class="eyebrow">Análisis comercial</p><h2>Integraciones</h2>'+
    '<p class="muted small">Conexión con las APIs oficiales de cada plataforma. «Leer plataforma» compara tus anuncios y pedidos con el inventario y propone cambios; <b>nada se modifica en la plataforma ni en la aplicación hasta que marques y confirmes cada cambio</b>.</p></div></div>'+
    (d.puedeCapturarSecretos?'':'<div class="callout sun small">Por seguridad, las credenciales (Client Secret, tokens) solo se capturan desde la computadora principal, abriendo la aplicación en http://localhost.</div>')+
    '<div class="grid g2">'+d.conectores.map(function(c){
      return '<div class="card"><div class="pf"><h3 style="margin:0">'+esc(c.nombre)+'</h3>'+(c.disponible?'<button class="btn sm primary" type="button" data-nueva="'+esc(c.codigo)+'">+ Cuenta</button>':'<span class="pill C">No disponible</span>')+'</div>'+
        '<ul class="clean small">'+c.capacidades.map(function(k){return '<li>'+(k.disponible?'<b class="tone-good">✓</b> ':'<b class="muted">—</b> ')+'<b>'+esc(k.nombre)+':</b> <span class="muted">'+esc(k.nota)+'</span></li>'}).join('')+'</ul>'+
        '<p class="small"><a href="'+esc(c.documentacion)+'" target="_blank" rel="noopener noreferrer">Documentación oficial</a></p></div>'}).join('')+'</div>'+
    '<h3 class="dash-h">Cuentas</h3>'+
    (d.cuentas.length?'<div class="scroll"><table><thead><tr><th>Cuenta</th><th>Plataforma</th><th>Estado</th><th>Última lectura</th><th></th></tr></thead><tbody>'+d.cuentas.map(function(c){
      var con=conector(c.conector)||{};
      return '<tr><td class="wrap"><b>'+esc(c.nombre)+'</b><span class="sub">'+esc(con.nombre||c.conector)+(c.cuentaExterna?' · '+esc(c.cuentaExterna):'')+'</span>'+(c.ultimoError?'<span class="sub tone-bad">'+esc(c.ultimoError)+'</span>':'')+'</td>'+
        '<td>'+esc(c.plataforma||'')+'</td><td>'+pill(EST,c.estado)+'</td><td>'+esc(fdt(c.ultimaLecturaUtc))+'</td>'+
        '<td class="r"><div class="row" style="justify-content:flex-end">'+
          (con.usaOAuth?'<button class="btn sm" type="button" data-con="'+esc(c.id)+'">Conectar</button>':'<button class="btn sm" type="button" data-pro="'+esc(c.id)+'">Probar</button>')+
          '<button class="btn sm primary" type="button" data-leer="'+esc(c.id)+'"'+(c.estado==='conectada'?'':' disabled')+'>Leer plataforma</button>'+
          '<button class="btn sm" type="button" data-anu="'+esc(c.id)+'"'+(c.estado==='conectada'?'':' disabled')+'>Anuncios</button>'+
          '<button class="btn sm" type="button" data-his="'+esc(c.id)+'">Historial</button>'+
          '<button class="btn sm" type="button" data-cfg="'+esc(c.id)+'">Configurar</button></div></td></tr>'}).join('')+'</tbody></table></div>'
      :'<div class="empty"><p class="muted">Aún no conectas ninguna cuenta. Necesitas una aplicación registrada como desarrollador en la plataforma.</p></div>')+
    '<div id="intLote"></div>';
  function on(sel,f){b.querySelectorAll(sel).forEach(function(x){x.addEventListener('click',function(){f(x)})})}
  on('[data-nueva]',function(x){cuentaForm(null,x.dataset.nueva)});
  on('[data-cfg]',function(x){cuentaForm(d.cuentas.filter(function(c){return c.id===x.dataset.cfg})[0])});
  on('[data-con]',function(x){conectarForm(x.dataset.con)});
  on('[data-pro]',function(x){x.disabled=true;PC.api('POST','integraciones/cuentas/'+x.dataset.pro+'/probar').then(function(){PC.toast('Conexión correcta');recargar()},function(e){err(e);recargar()})});
  on('[data-leer]',function(x){x.disabled=true;x.textContent='Leyendo…';PC.api('POST','integraciones/cuentas/'+x.dataset.leer+'/leer').then(function(r){st.lote=r;recargar();setTimeout(function(){pintaLote(r)},50)},function(e){err(e);recargar()})});
  on('[data-anu]',function(x){anuncios(x.dataset.anu)});
  on('[data-his]',function(x){historial(x.dataset.his)});
  if(st.lote)pintaLote(st.lote);
}

function cuentaForm(c,codigo){var con=conector(c?c.conector:codigo),nuevo=!c;c=c||{config:{},secretosCapturados:[],plataformaId:con.plataformaSugeridaId,nombre:''};
  var plats=PC.datos().PLATS;
  PC.openModal(head((nuevo?'Nueva cuenta · ':'Configurar · ')+esc(con.nombre))+'<form style="display:flex;flex-direction:column;gap:14px"><div class="form">'+
    fld('icNom','Nombre','<input class="inp" id="icNom" name="nombre" maxlength="80" value="'+esc(c.nombre||con.nombre)+'">')+
    fld('icPla','Plataforma de las ventas','<select class="sel" id="icPla" name="plataformaId" required>'+plats.map(function(p){return '<option value="'+esc(p.id)+'"'+(p.id===c.plataformaId?' selected':'')+'>'+esc(p.nombre)+'</option>'}).join('')+'</select>','Las ventas importadas y las publicaciones vinculadas usan esta plataforma (y su tarifa).')+
    con.campos.map(function(k){var cap=c.secretosCapturados.indexOf(k.clave)>=0,v=k.secreto?'':(c.config[k.clave]!=null?c.config[k.clave]:(k.porDefecto||''));
      return fld('ic_'+k.clave,esc(k.etiqueta)+(k.requerido?'':' (opcional)'),'<input class="inp" id="ic_'+k.clave+'" data-k="'+esc(k.clave)+'" data-s="'+(k.secreto?1:0)+'"'+(k.secreto?' type="password" autocomplete="new-password" placeholder="'+(cap?'Capturado · déjalo vacío para conservarlo':'')+'"':'')+' maxlength="300" value="'+esc(v)+'">',
        esc(k.ayuda)+(k.clave==='redirectUri'?' Si registras esta dirección, la conexión termina sola: '+esc(st.datos.redirectSugerida)+' (Mercado Libre puede exigir https; si no la acepta, usa cualquier dirección tuya y pega aquí la dirección de regreso).':''),true)}).join('')+
    '</div><div class="pf"><div>'+(nuevo?'':'<button class="btn danger" type="button" id="icDel">Eliminar cuenta</button>')+'</div><div class="row"><button class="btn" type="button" data-c2>Cancelar</button><button class="btn primary" type="submit">Guardar</button></div></div></form>',function(el){
    el.querySelector('[data-c2]').addEventListener('click',PC.closeModal);
    var dl=el.querySelector('#icDel');if(dl)dl.addEventListener('click',function(){if(!confirm('¿Eliminar la cuenta y sus credenciales? Tus ventas y publicaciones se conservan.'))return;
      PC.api('DELETE','integraciones/cuentas/'+c.id).then(function(){PC.closeModal();st.lote=null;recargar()},err)});
    el.querySelector('form').addEventListener('submit',function(e){e.preventDefault();var f=e.target,cfg={},sec={};
      f.querySelectorAll('[data-k]').forEach(function(i){if(i.dataset.s==='1'){if(i.value.trim())sec[i.dataset.k]=i.value.trim()}else cfg[i.dataset.k]=i.value.trim()});
      PC.api(nuevo?'POST':'PUT','integraciones/cuentas'+(nuevo?'':'/'+c.id),{conector:con.codigo,plataformaId:f.plataformaId.value,nombre:f.nombre.value.trim(),config:cfg,secretos:sec})
        .then(function(){PC.closeModal();PC.toast('Cuenta guardada');recargar()},err)})});
}

function conectarForm(id){
  PC.api('POST','integraciones/cuentas/'+id+'/autorizar').then(function(r){
    PC.openModal(head('Conectar con Mercado Libre')+'<ol class="small" style="padding-left:18px;display:flex;flex-direction:column;gap:6px">'+
      '<li>Abre la página de autorización e inicia sesión con la <b>cuenta principal</b> (no un colaborador): <a href="'+esc(r.url)+'" target="_blank" rel="noopener noreferrer"><b>Autorizar en Mercado Libre</b></a>.</li>'+
      '<li>Al aceptar, Mercado Libre te lleva a tu Redirect URI. Si esa dirección es esta aplicación, la conexión termina sola.</li>'+
      '<li>Si no, copia la dirección completa de esa página (contiene <code>?code=…&state=…</code>) y pégala aquí.</li></ol>'+
      '<form style="display:flex;flex-direction:column;gap:10px">'+fld('icUrl','Dirección de regreso','<input class="inp" id="icUrl" name="url" required placeholder="https://…?code=TG-…&state=…">',null,true)+
      '<div class="pf"><div></div><div class="row"><button class="btn" type="button" data-c2>Cerrar</button><button class="btn primary" type="submit">Completar conexión</button></div></div></form>',function(el){
      el.querySelector('[data-c2]').addEventListener('click',function(){PC.closeModal();recargar()});
      el.querySelector('form').addEventListener('submit',function(e){e.preventDefault();
        PC.api('POST','integraciones/cuentas/'+id+'/codigo',{urlOCodigo:e.target.url.value.trim()}).then(function(){PC.closeModal();PC.toast('Cuenta conectada');recargar()},err)})});
  },err);
}

function pintaLote(r){var el=st.cuerpo.querySelector('#intLote');if(!el)return;var l=r.lote,ps=r.propuestas,pend=l.estado==='pendiente';
  var aplic=ps.filter(function(p){return p.estado==='pendiente'});
  el.innerHTML='<div class="card"><div class="pf"><h3 style="margin:0">Lectura del '+esc(fdt(l.creadoUtc))+' · '+esc(LEST[l.estado]||l.estado)+'</h3>'+
    (pend&&aplic.length?'<div class="row"><button class="btn sm" type="button" id="ilTodas">Marcar todas</button><button class="btn sm" type="button" id="ilDesc">Descartar</button><button class="btn sm primary" type="button" id="ilAplicar">Aplicar seleccionadas</button></div>':'')+'</div>'+
    '<p class="small muted">'+esc(l.resumen)+'</p>'+
    (ps.length?'<div class="scroll" style="border:0"><table><thead><tr><th></th><th>Cambio</th><th>Dónde</th><th>Detalle</th><th>Antes</th><th>Después</th><th>Estado</th></tr></thead><tbody>'+ps.map(function(p){
      return '<tr><td>'+(pend&&p.estado==='pendiente'?'<input type="checkbox" data-p="'+esc(p.id)+'" aria-label="Aplicar este cambio">':'')+'</td><td><b>'+esc(TIPO[p.tipo]||p.tipo)+'</b><span class="sub">'+esc(p.referenciaExterna)+'</span></td>'+
        '<td>'+(p.destino==='plataforma'?'En la plataforma':'En la aplicación')+'</td><td class="wrap small">'+esc(p.descripcion)+'</td><td>'+esc(p.antes||'')+'</td><td><b>'+esc(p.despues||'')+'</b></td>'+
        '<td>'+pill(PEST,p.estado)+(p.resultado&&p.resultado!=='Listo'?'<span class="sub'+(p.estado==='error'?' tone-bad':'')+'">'+esc(BLOQ[p.resultado]||p.resultado)+'</span>':'')+'</td></tr>'}).join('')+'</tbody></table></div>'
      :'<p class="small muted">Todo coincide: no hay cambios que proponer.</p>')+
    (r.sinVincular&&r.sinVincular.length?'<p class="small muted">'+r.sinVincular.length+' producto(s) del catálogo sin vincular a un juguete. Usa «Anuncios» para vincularlos.</p>':'')+'</div>';
  var t=el.querySelector('#ilTodas');if(t)t.addEventListener('click',function(){el.querySelectorAll('[data-p]').forEach(function(c){c.checked=true})});
  var d=el.querySelector('#ilDesc');if(d)d.addEventListener('click',function(){PC.api('POST','integraciones/lotes/'+l.id+'/descartar').then(function(){st.lote=null;recargar()},err)});
  var a=el.querySelector('#ilAplicar');if(a)a.addEventListener('click',function(){
    var sel=[].slice.call(el.querySelectorAll('[data-p]:checked')).map(function(c){return c.dataset.p});
    if(!sel.length){PC.toast('Marca los cambios que quieres aplicar');return}
    var enPlat=ps.filter(function(p){return sel.indexOf(p.id)>=0&&p.destino==='plataforma'}).length;
    if(!confirm('Se aplicarán '+sel.length+' cambio(s)'+(enPlat?', '+enPlat+' directamente en la plataforma':'')+'. Los no marcados quedarán como no aplicados. ¿Continuar?'))return;
    a.disabled=true;a.textContent='Aplicando…';
    PC.api('POST','integraciones/lotes/'+l.id+'/aplicar',{propuestas:sel}).then(function(r2){st.lote=r2;PC.cargar().then(function(){recargar();PC.toast(r2.lote.estado==='aplicado'?'Cambios aplicados':'Algunos cambios tuvieron error: revisa la tabla')})},function(e){err(e);a.disabled=false;a.textContent='Aplicar seleccionadas'});
  });
}

function anuncios(id){
  PC.openModal(head('Anuncios de la plataforma')+'<p class="small muted">Leyendo…</p>',function(){});
  PC.api('GET','integraciones/cuentas/'+id+'/anuncios').then(function(l){
    var D=PC.datos().D,P=Object.keys(D.productos).map(function(k){return D.productos[k]}).sort(function(a,b){return String(a.nombre).localeCompare(String(b.nombre),'es')});
    var opts='<option value="">Elige un juguete</option>'+P.map(function(p){return '<option value="'+esc(p.id)+'">'+esc(p.codigo+' · '+p.nombre)+'</option>'}).join('');
    PC.openModal(head('Anuncios de la plataforma')+'<p class="small muted">Vincula cada anuncio con su juguete: así la aplicación puede pausar o ajustar piezas cuando se venda en otro canal e importar sus pedidos.</p>'+
      (l.length?'<div class="scroll"><table><thead><tr><th>Anuncio</th><th>Estado</th><th class="r">Precio</th><th class="r">Piezas</th><th>Juguete</th></tr></thead><tbody>'+l.map(function(x){var a=x.anuncio;
        return '<tr><td class="wrap"><b>'+esc(a.titulo)+'</b><span class="sub">'+esc(a.idExterno)+(a.conVariaciones?' · con variaciones':'')+'</span></td><td>'+esc(a.estatusOriginal||a.estatus)+'</td><td class="r">'+(a.precio!=null?money2(a.precio):'—')+'</td><td class="r">'+(a.existencias!=null?num(a.existencias):'—')+'</td>'+
          '<td>'+(x.vinculadoA?esc(x.vinculadoA):'<div class="row"><select class="sel" data-sel="'+esc(a.idExterno)+'">'+opts+'</select><button class="btn sm" type="button" data-vin="'+esc(a.idExterno)+'">Vincular</button></div>')+'</td></tr>'}).join('')+'</tbody></table></div>'
        :'<p class="small muted">La cuenta no tiene anuncios.</p>'),function(el){
      el.querySelectorAll('[data-vin]').forEach(function(b){b.addEventListener('click',function(){var s=el.querySelector('[data-sel="'+CSS.escape(b.dataset.vin)+'"]');
        if(!s.value){PC.toast('Elige el juguete');return}
        PC.api('POST','integraciones/cuentas/'+id+'/vincular',{idExterno:b.dataset.vin,productoId:s.value}).then(function(){PC.cargar();b.closest('td').textContent='Vinculado';PC.toast('Vinculado. Revisa el precio en Publicaciones.')},err)})});
    });
  },function(e){PC.closeModal();err(e)});
}

function historial(id){
  Promise.all([PC.api('GET','integraciones/cuentas/'+id+'/lotes'),PC.api('GET','integraciones/cuentas/'+id+'/eventos')]).then(function(r){
    PC.openModal(head('Historial de la cuenta')+
      '<h3>Lecturas</h3>'+(r[0].length?'<ul class="clean small">'+r[0].map(function(l){return '<li><button class="lnk" type="button" data-lote="'+esc(l.id)+'">'+esc(fdt(l.creadoUtc))+'</button> · '+esc(LEST[l.estado]||l.estado)+' · <span class="muted">'+esc(l.resumen)+'</span></li>'}).join('')+'</ul>':'<p class="small muted">Sin lecturas.</p>')+
      '<h3>Bitácora técnica</h3>'+(r[1].length?'<div class="scroll"><table><thead><tr><th>Fecha</th><th>Nivel</th><th>Operación</th><th>Detalle</th></tr></thead><tbody>'+r[1].map(function(e){return '<tr><td>'+esc(fdt(e.fechaUtc))+'</td><td>'+esc(e.nivel)+'</td><td>'+esc(e.operacion)+'</td><td class="wrap small">'+esc(e.detalle)+'</td></tr>'}).join('')+'</tbody></table></div>':'<p class="small muted">Sin llamadas registradas.</p>'),function(el){
      el.querySelectorAll('[data-lote]').forEach(function(b){b.addEventListener('click',function(){PC.api('GET','integraciones/lotes/'+b.dataset.lote).then(function(x){PC.closeModal();st.lote=x;pintaLote(x)},err)})});
    });
  },err);
}

window.PCI={render:render};
})();
