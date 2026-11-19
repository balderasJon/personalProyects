/* «Preparar publicación»: texto e imágenes listos para Facebook Marketplace y para historias de WhatsApp.
   Facebook Marketplace (publicaciones personales) y las historias de WhatsApp no ofrecen API para publicar: la aplicación prepara
   todo para copiar y pegar, y después registra la publicación para seguir controlando las existencias. */
(function(){
'use strict';
var PC=window.PC,esc=PC.esc,money=PC.money,fld=PC.fld,head=PC.head;
var PREF='pallet-publicar-prefs';
function prefs(){try{return JSON.parse(localStorage.getItem(PREF)||'{}')}catch(e){return {}}}
function guardarPrefs(o){try{localStorage.setItem(PREF,JSON.stringify(o))}catch(e){}}
function d(){return PC.datos()}

function disponibles(p){return Math.max(0,(+p.cantidad||0)-PC.vendidasDe(p.id))}
/** Condición de las piezas vendibles con existencia (la que tenga más piezas). */
function condicion(p){var ls=PC.lotesDe(p.id).filter(function(l){return l.comercializable&&l.cantidad>0}).sort(function(a,b){return b.cantidad-a.cantidad});
  return ls.length?PC.condNombre(ls[0].condicion):'Nuevo'}
function precioDe(p){var x=d().PRECIOS[p.id]||{};return +p.precioObjetivo>0?{v:+p.precioObjetivo,o:'de tu precio objetivo'}:(x.ok&&x.sugerido?{v:+x.sugerido,o:'del precio sugerido'}:{v:0,o:''})}
function medidas(p){var m=[p.largoCm,p.anchoCm,p.altoCm].filter(function(x){return +x>0});return m.length?m.map(function(x){return (+x).toLocaleString('es-MX')}).join(' × ')+' cm':''}
function recortar(t,n){t=String(t||'').replace(/\s+\n/g,'\n').trim();return t.length>n?t.slice(0,n).replace(/\s+\S*$/,'')+'…':t}

function textoFacebook(p,precio,pr){
  var L=[];
  L.push(p.nombre);
  L.push('');
  L.push('💲 Precio: '+money(precio));
  L.push('📦 Condición: '+condicion(p));
  var mm=[p.marca?'Marca: '+p.marca:'',p.modelo?'Modelo: '+p.modelo:''].filter(Boolean).join(' · ');if(mm)L.push(mm);
  var desc=p.descripcionComercial||p.caracteristicas;if(desc){L.push('');L.push(recortar(desc,700))}
  var extra=[];if(p.accesoriosIncluidos)extra.push('Incluye: '+p.accesoriosIncluidos);if(p.edadRecomendada)extra.push('Edad recomendada: '+p.edadRecomendada);
  var md=medidas(p);if(md)extra.push('Medidas: '+md);if(+p.pesoKg>0)extra.push('Peso: '+(+p.pesoKg).toLocaleString('es-MX')+' kg');
  if(extra.length){L.push('');extra.forEach(function(x){L.push(x)})}
  L.push('');
  if(pr.zona)L.push('📍 Entrega: '+pr.zona);
  if(pr.pago)L.push('💳 Pago: '+pr.pago);
  L.push(pr.cierre||'Envíame mensaje para apartarlo.');
  return L.join('\n');
}
function textoWhatsApp(p,precio,pr){
  return '🧸 '+p.nombre+'\n'+condicion(p)+' · '+money(precio)+(pr.zona?'\n📍 '+pr.zona:'')+'\n'+(pr.cierre||'Escríbeme para apartarlo 📩');
}

/* ---------- imágenes (canvas): historia 1080×1920 y cuadrada 1080×1080 ---------- */
function cargarImg(url){return new Promise(function(ok){if(!url)return ok(null);var i=new Image();i.onload=function(){ok(i)};i.onerror=function(){ok(null)};i.src=url})}
function lineas(ctx,t,maxW,maxL){var w=String(t).split(/\s+/),L=[],c='';
  w.forEach(function(x){var pr=c?c+' '+x:x;if(ctx.measureText(pr).width>maxW&&c){L.push(c);c=x}else c=pr});if(c)L.push(c);
  if(L.length>maxL){L=L.slice(0,maxL);L[maxL-1]=L[maxL-1].replace(/\s*\S*$/,'')+'…'}return L}
function dibujar(img,p,precio,pr,W,H){
  var c=document.createElement('canvas');c.width=W;c.height=H;var x=c.getContext('2d');
  x.fillStyle='#F3F5F9';x.fillRect(0,0,W,H);
  var V=H>W,pad=Math.round(W*0.06),fotoH=Math.round(H*(V?0.58:0.46));
  x.fillStyle='#FFFFFF';x.fillRect(pad,pad,W-2*pad,fotoH);
  if(img){var r=Math.min((W-2*pad)/img.width,fotoH/img.height),iw=img.width*r,ih=img.height*r;x.drawImage(img,pad+((W-2*pad)-iw)/2,pad+(fotoH-ih)/2,iw,ih)}
  else{x.fillStyle='#4A5673';x.font='600 '+Math.round(W*0.04)+'px Figtree, sans-serif';x.textAlign='center';x.fillText('Agrega una foto del juguete',W/2,pad+fotoH/2);x.textAlign='left'}
  var y=pad+fotoH+Math.round(W*(V?0.08:0.065));
  x.fillStyle='#101A33';x.font='800 '+Math.round(W*(V?0.062:0.052))+'px "Bricolage Grotesque", Figtree, sans-serif';
  lineas(x,p.nombre,W-2*pad,V?3:2).forEach(function(l){x.fillText(l,pad,y);y+=Math.round(W*(V?0.072:0.06))});
  y+=Math.round(W*0.02);
  // condición (pastilla)
  var cond=condicion(p);x.font='600 '+Math.round(W*0.034)+'px Figtree, sans-serif';var cw=x.measureText(cond).width+Math.round(W*0.05),ch=Math.round(W*0.06);
  x.fillStyle='#D6EFE2';x.beginPath();if(x.roundRect)x.roundRect(pad,y-ch*0.72,cw,ch,ch/2);else x.rect(pad,y-ch*0.72,cw,ch);x.fill();
  x.fillStyle='#12754A';x.fillText(cond,pad+Math.round(W*0.025),y);
  y+=Math.round(W*(V?0.16:0.1));
  x.fillStyle='#2750D8';x.font='800 '+Math.round(W*(V?0.13:0.085))+'px "Bricolage Grotesque", Figtree, sans-serif';x.fillText(money(precio),pad,y);
  // pie
  x.fillStyle='#4A5673';x.font='600 '+Math.round(W*0.036)+'px Figtree, sans-serif';
  var pie=[pr.cierre||'Escríbeme para apartarlo',pr.zona?'Entrega: '+pr.zona:''].filter(Boolean);
  var yp=H-pad-(pie.length-1)*Math.round(W*0.048);pie.forEach(function(t){lineas(x,t,W-2*pad,1).forEach(function(l){x.fillText(l,pad,yp)});yp+=Math.round(W*0.048)});
  return c;
}
function descargar(canvas,nombre){var a=document.createElement('a');a.download=nombre;a.href=canvas.toDataURL('image/jpeg',0.9);document.body.appendChild(a);a.click();a.remove()}
function archivoDe(n){return String(n||'juguete').normalize('NFD').replace(/[̀-ͯ]/g,'').replace(/[^A-Za-z0-9]+/g,'-').replace(/^-|-$/g,'').toLowerCase().slice(0,40)||'juguete'}
function copiar(t,msg){
  function ok(){PC.toast(msg||'Copiado')}
  if(navigator.clipboard&&window.isSecureContext)navigator.clipboard.writeText(t).then(ok,function(){viejo()});else viejo();
  function viejo(){var ta=document.createElement('textarea');ta.value=t;ta.style.position='fixed';ta.style.opacity='0';document.body.appendChild(ta);ta.select();
    try{document.execCommand('copy');ok()}catch(e){PC.toast('Selecciona el texto y cópialo')}ta.remove()}
}
/** Compartir directo (celular con conexión segura): abre la hoja de compartir del teléfono, desde donde se elige WhatsApp. */
function compartir(canvas,texto,nombre){
  return new Promise(function(r){canvas.toBlob(r,'image/jpeg',0.9)}).then(function(b){
    var f=new File([b],nombre,{type:'image/jpeg'});
    if(navigator.canShare&&navigator.canShare({files:[f]}))return navigator.share({files:[f],text:texto}).catch(function(){});
    descargar(canvas,nombre);PC.toast('Tu navegador no permite compartir directo: se descargó la imagen.');
  });
}

/* ---------- ventana ---------- */
function abrir(id){
  var p0=d().D.productos[id];if(!p0)return;var p=Object.assign({id:id},p0);
  var pr=prefs(),pc=precioDe(p),fotos=PC.imgsDe(id),disp=disponibles(p),x=d().PRECIOS[id]||{};
  var plats=d().PLATS,fb=plats.filter(function(q){return q.codigo==='facebook_marketplace'})[0],wa=plats.filter(function(q){return q.codigo==='whatsapp'})[0];
  var yaFb=d().PUBS.some(function(u){return u.productoId===id&&fb&&u.plataformaId===fb.id&&u.estatus==='publicado'}),yaWa=d().PUBS.some(function(u){return u.productoId===id&&wa&&u.plataformaId===wa.id&&u.estatus==='publicado'});
  PC.openModal(head('Preparar publicación')+
    '<p class="small muted">Para <b>Facebook Marketplace</b> y <b>historias de WhatsApp</b>. Estas plataformas no permiten publicar desde otras aplicaciones: copia el texto, descarga las imágenes y pégalos en la app. Después registra la publicación para que la aplicación controle tus existencias.</p>'+
    '<div class="form">'+
      fld('ppPre','Precio a publicar','<input class="inp num" id="ppPre" type="number" min="0" step="1" value="'+esc(pc.v||'')+'">',
        (pc.o?'Tomado '+pc.o+'. ':'Sin precio: captura uno. ')+(x.ok&&x.minimo?'Tu mínimo es '+money(x.minimo)+' (no se publica).':''))+
      fld('ppDis','Piezas disponibles','<input class="inp" id="ppDis" value="'+disp+'" readonly>',disp?'':'No hay piezas disponibles.')+
      fld('ppZon','Zona de entrega','<input class="inp" id="ppZon" maxlength="80" value="'+esc(pr.zona||'')+'" placeholder="Ej. Metro Coyoacán o envío por paquetería">','Se guarda en este equipo para la próxima vez.')+
      fld('ppPag','Formas de pago','<input class="inp" id="ppPag" maxlength="80" value="'+esc(pr.pago||'')+'" placeholder="Ej. Efectivo o transferencia">')+
      fld('ppCie','Cierre del mensaje','<input class="inp" id="ppCie" maxlength="80" value="'+esc(pr.cierre||'Envíame mensaje para apartarlo.')+'">',null,true)+
    '</div>'+
    (pc.v&&x.ok&&x.minimo&&pc.v<x.minimo?'<p class="warnline bad">El precio está debajo de tu mínimo ('+money(x.minimo)+'): perderías dinero en esta venta.</p>':'')+
    (fotos.length?'':'<p class="warnline">Este juguete no tiene fotos: agrega al menos una (Editar › Imágenes). Las publicaciones con foto se venden mucho mejor.</p>')+
    '<div class="grid g2">'+
      '<div class="card"><div class="pf"><h3 style="margin:0">Facebook Marketplace</h3>'+(yaFb?'<span class="pill entregado">Ya registrada</span>':'')+'</div>'+
        fld('ppTit','Título','<input class="inp" id="ppTit" maxlength="150" value="'+esc(p.nombre)+'">','<span id="ppTitN"></span>',true)+
        fld('ppFb','Descripción','<textarea class="inp" id="ppFb" rows="11"></textarea>','Puedes editarla antes de copiar.',true)+
        '<div class="row"><button class="btn sm" type="button" id="ppCopTit">Copiar título</button><button class="btn sm primary" type="button" id="ppCopFb">Copiar descripción</button>'+
          '<button class="btn sm" type="button" id="ppImgFb">Descargar imagen con precio</button>'+(fotos.length?'<button class="btn sm" type="button" id="ppFotos">Descargar fotos ('+fotos.length+')</button>':'')+'</div>'+
        '<p class="small muted">En Facebook: Marketplace › Crear publicación › Artículo en venta. Sube las fotos, pega el título, el precio, la condición «'+esc(condicion(p))+'» y la descripción. <a href="https://www.facebook.com/marketplace/" target="_blank" rel="noopener noreferrer">Abrir Marketplace</a></p>'+
        '<button class="btn sm" type="button" id="ppRegFb"'+(fb?'':' disabled')+'>Ya lo publiqué: registrar</button></div>'+
      '<div class="card"><div class="pf"><h3 style="margin:0">Historia de WhatsApp</h3>'+(yaWa?'<span class="pill entregado">Ya registrada</span>':'')+'</div>'+
        '<div style="display:flex;gap:12px;flex-wrap:wrap;align-items:flex-start"><canvas id="ppHist" width="1080" height="1920" style="width:150px;height:auto;border:1px solid var(--line);border-radius:10px" aria-label="Vista previa de la historia"></canvas>'+
        '<div style="flex:1;min-width:180px">'+fld('ppWa','Texto para acompañarla','<textarea class="inp" id="ppWa" rows="5"></textarea>',null,true)+'</div></div>'+
        '<div class="row"><button class="btn sm primary" type="button" id="ppImgWa">Descargar historia</button><button class="btn sm" type="button" id="ppCopWa">Copiar texto</button><button class="btn sm" type="button" id="ppShare">Compartir</button></div>'+
        '<p class="small muted">En WhatsApp: Novedades › Mi estado › agrega la imagen descargada (y el texto si quieres). «Compartir» abre directo el menú del celular cuando el navegador lo permite.</p>'+
        '<button class="btn sm" type="button" id="ppRegWa"'+(wa?'':' disabled')+'>Ya lo publiqué: registrar</button></div>'+
    '</div>',function(el){
      var g=function(s){return el.querySelector(s)},img=null;
      function datosPrefs(){return {zona:g('#ppZon').value.trim(),pago:g('#ppPag').value.trim(),cierre:g('#ppCie').value.trim()}}
      function precio(){return Math.max(0,+g('#ppPre').value||0)}
      function generar(soloImagen){var q=datosPrefs(),pp=precio(),prod=Object.assign({},p,{nombre:g('#ppTit').value.trim()||p.nombre});
        if(!soloImagen){g('#ppFb').value=textoFacebook(prod,pp,q);g('#ppWa').value=textoWhatsApp(prod,pp,q)}
        var h=dibujar(img,prod,pp,q,1080,1920),cv=g('#ppHist');cv.getContext('2d').drawImage(h,0,0);
        g('#ppTitN').textContent=g('#ppTit').value.length+' caracteres';}
      cargarImg(fotos[0]&&fotos[0].url).then(function(i){img=i;generar(false)});
      ['#ppPre','#ppZon','#ppPag','#ppCie','#ppTit'].forEach(function(s){g(s).addEventListener('input',function(){guardarPrefs(datosPrefs());generar(false)})});
      function prod(){return Object.assign({},p,{nombre:g('#ppTit').value.trim()||p.nombre})}
      function sinPrecio(){if(precio()>0)return false;PC.toast('Captura el precio a publicar');g('#ppPre').focus();return true}
      g('#ppCopTit').addEventListener('click',function(){copiar(g('#ppTit').value,'Título copiado')});
      g('#ppCopFb').addEventListener('click',function(){if(!sinPrecio())copiar(g('#ppFb').value,'Descripción copiada')});
      g('#ppCopWa').addEventListener('click',function(){if(!sinPrecio())copiar(g('#ppWa').value,'Texto copiado')});
      g('#ppImgFb').addEventListener('click',function(){if(!sinPrecio())descargar(dibujar(img,prod(),precio(),datosPrefs(),1080,1080),archivoDe(p.nombre)+'-marketplace.jpg')});
      g('#ppImgWa').addEventListener('click',function(){if(!sinPrecio())descargar(dibujar(img,prod(),precio(),datosPrefs(),1080,1920),archivoDe(p.nombre)+'-historia.jpg')});
      g('#ppShare').addEventListener('click',function(){if(!sinPrecio())compartir(dibujar(img,prod(),precio(),datosPrefs(),1080,1920),g('#ppWa').value,archivoDe(p.nombre)+'-historia.jpg')});
      var bf=g('#ppFotos');if(bf)bf.addEventListener('click',function(){fotos.forEach(function(f,i){setTimeout(function(){var a=document.createElement('a');a.href=f.url;a.download=archivoDe(p.nombre)+'-'+(i+1);document.body.appendChild(a);a.click();a.remove()},i*400)})});
      function registrar(pl,nombre){if(sinPrecio())return;
        PC.api('POST','publicaciones',{productoId:id,plataformaId:pl.id,precioPublicado:precio(),estatus:'publicado',existencias:disp,observaciones:'Preparada con «Preparar publicación».'})
          .then(function(){return PC.cargar()}).then(function(){PC.toast('Publicación en '+nombre+' registrada: la aplicación te avisará si se vende en otro canal')},function(e){PC.toast((e&&e.mensaje)||'No se pudo registrar')})}
      g('#ppRegFb').addEventListener('click',function(){if(fb)registrar(fb,'Facebook Marketplace')});
      g('#ppRegWa').addEventListener('click',function(){if(wa)registrar(wa,'WhatsApp')});
    });
}
window.PCPub={abrir:abrir,textoFacebook:textoFacebook,textoWhatsApp:textoWhatsApp};
})();
