/* «Preparar publicación»: el usuario elige el formato (historia de WhatsApp, publicación de Facebook, grupos de Facebook,
   Facebook Marketplace o historia de Instagram) y el estilo creativo (BOOM, OFERTA, DIVERTIDO, PREMIUM, SORPRESA, MINIMALISTA).
   La aplicación dibuja la imagen en el navegador (canvas) y propone varios textos listos para copiar.
   Estas plataformas no ofrecen API para publicar desde otra aplicación: se copia y se pega, y después se registra la publicación
   para seguir controlando las existencias. Los textos solo usan datos capturados del juguete (nunca inventan características). */
(function(){
'use strict';
var PC=window.PC,esc=PC.esc,money=PC.money,fld=PC.fld,head=PC.head;
var PREF='pallet-publicar-prefs';
function prefs(){try{return JSON.parse(localStorage.getItem(PREF)||'{}')}catch(e){return {}}}
function guardarPrefs(o){try{localStorage.setItem(PREF,JSON.stringify(Object.assign(prefs(),o)))}catch(e){}}
function d(){return PC.datos()}

/* ---------- catálogos ---------- */
var FORMATOS=[
  {k:'wa_historia',n:'Historia de WhatsApp',ic:'whatsapp',W:1080,H:1920,med:'Vertical 9:16 · 1080 × 1920 px',plat:'whatsapp',arch:'historia-whatsapp',
    cta:'Escríbeme para apartarlo',ayuda:'En WhatsApp: Novedades › Mi estado › agrega la imagen descargada y, si quieres, uno de los textos. «Compartir» abre directo el menú del celular cuando el navegador lo permite.'},
  {k:'fb_post',n:'Publicación de Facebook',ic:'facebook',W:1080,H:1080,med:'Cuadrada 1:1 · 1080 × 1080 px',plat:'facebook',arch:'facebook',
    cta:'Envíame mensaje para apartarlo',ayuda:'En Facebook: «¿Qué estás pensando?» › Foto/video › sube la imagen y pega uno de los textos.',link:['https://www.facebook.com/','Abrir Facebook']},
  {k:'fb_grupos',n:'Grupos de Facebook',ic:'people',W:1080,H:1080,med:'Cuadrada 1:1 · 1080 × 1080 px',plat:'grupos_facebook',arch:'grupos-facebook',
    cta:'Comenta «LO QUIERO» o envíame inbox',ayuda:'En el grupo: «Escribe algo…» › Foto › sube la imagen y pega uno de los textos. Revisa las reglas del grupo antes de publicar.',link:['https://www.facebook.com/groups/feed/','Abrir mis grupos']},
  {k:'marketplace',n:'Facebook Marketplace',ic:'shop',W:1080,H:1080,med:'Cuadrada 1:1 · 1080 × 1080 px',plat:'facebook_marketplace',arch:'marketplace',
    cta:'Envíame mensaje para apartarlo',ayuda:'En Facebook: Marketplace › Crear publicación › Artículo en venta. Sube primero las fotos originales (limpias) y, si quieres, la imagen diseñada; pega el título, el precio, la condición y la descripción.',link:['https://www.facebook.com/marketplace/','Abrir Marketplace']},
  {k:'ig_historia',n:'Historia de Instagram',ic:'instagram',W:1080,H:1920,med:'Vertical 9:16 · 1080 × 1920 px',plat:'instagram',arch:'historia-instagram',seguro:true,
    cta:'Envíame DM para apartarlo',ayuda:'En Instagram: «Tu historia» (o + › Historia) › elige la imagen descargada. Deja libres arriba y abajo para que Instagram no tape el precio.',link:['https://www.instagram.com/','Abrir Instagram']}
];
var ESTILOS=[
  {k:'boom',n:'BOOM',d:'Explosivo, colores vibrantes y tipografía de impacto'},
  {k:'oferta',n:'OFERTA',d:'El precio es el protagonista'},
  {k:'divertido',n:'DIVERTIDO',d:'Alegre, colorido y juguetón'},
  {k:'premium',n:'PREMIUM',d:'Limpio, elegante y profesional'},
  {k:'sorpresa',n:'SORPRESA',d:'Despierta curiosidad por descubrirlo'},
  {k:'minimalista',n:'MINIMALISTA',d:'Pocos elementos: todo el foco en el juguete'}
];
function fmtDe(k){return FORMATOS.filter(function(f){return f.k===k})[0]||FORMATOS[0]}
function estDe(k){return ESTILOS.filter(function(f){return f.k===k})[0]||ESTILOS[0]}

/* Tipo de juguete (por categoría y nombre): adapta frases, emojis y hashtags. */
var TIPOS=[
  {k:'vehiculo',re:/\bauto|carro|carrito|coche|veh[ií]cul|pista|cami[oó]n|\bmoto|tren|avi[oó]n|hot ?wheels|helic[oó]pter/i,e:['🚗','🏁'],h:['#carritos','#autosdejuguete'],grito:'¡A toda velocidad!',gancho:'¿Listos para la carrera?',fun:'Arranquen motores y que empiece la carrera'},
  {k:'muneca',re:/mu[ñn]eca|barbie|casa de mu[ñn]ecas/i,e:['👧','💖'],h:['#muñecas'],grito:'¡Mil historias por crear!',gancho:'¿Qué historia van a inventar hoy?',fun:'Aventuras, peinados y mil historias por inventar'},
  {k:'peluche',re:/peluch|felpa|\boso\b/i,e:['🧸','💕'],h:['#peluches'],grito:'¡Listo para abrazar!',gancho:'¿Quién necesita un abrazo?',fun:'Listo para muchos abrazos'},
  {k:'construccion',re:/bloque|construc|\blego\b|ensambl/i,e:['🧱','🏗️'],h:['#bloquesdeconstruccion'],grito:'¡A construir!',gancho:'¿Qué van a construir hoy?',fun:'Imaginar, armar y volver a empezar'},
  {k:'rompecabezas',re:/rompecabez|puzzle/i,e:['🧩','🧠'],h:['#rompecabezas'],grito:'¡Pieza por pieza!',gancho:'¿Aceptas el reto?',fun:'Pieza por pieza hasta completarlo'},
  {k:'mesa',re:/juegos? de mesa|loter[ií]a|domin[oó]|cartas|tablero|memorama/i,e:['🎲','🃏'],h:['#juegosdemesa'],grito:'¡Noche de juegos!',gancho:'¿Quién gana la próxima partida?',fun:'Para reír en familia y con amigos'},
  {k:'electronico',re:/electr[oó]nic|videojuego|consola|control remoto|radio ?control|interactiv|\brc\b/i,e:['🎮','⚡'],h:['#juguetesinteractivos'],grito:'¡Tecnología para jugar!',gancho:'¿Listos para el siguiente nivel?',fun:'Pura acción con un solo botón'},
  {k:'figura',re:/figura|superh[eé]ro|dinosaur|robot|transform|mu[ñn]eco\b|acci[oó]n/i,e:['🦸','⚡'],h:['#figurasdeaccion'],grito:'¡Que empiece la aventura!',gancho:'¿Quién se une a la aventura?',fun:'Batallas épicas y aventuras sin fin'},
  {k:'bebe',re:/beb[eé]|sonaja|estimulaci[oó]n|primera infancia|mordedera/i,e:['👶','🍼'],h:['#juguetesparabebe'],grito:'¡Para los más peques!',gancho:'¿Buscas algo para los más peques?',fun:'Colores y texturas para descubrir'},
  {k:'exterior',re:/pelota|bal[oó]n|bicicl|pat[ií]n|patineta|scooter|alberca|inflable|aire libre|exterior|deport/i,e:['⚽','☀️'],h:['#juegoalairelibre'],grito:'¡A jugar afuera!',gancho:'¿Listos para salir a jugar?',fun:'Energía al aire libre'},
  {k:'arte',re:/\barte\b|manualidad|plastilina|pintur|crayol|dibuj|slime|masa/i,e:['🎨','✂️'],h:['#manualidades'],grito:'¡A crear!',gancho:'¿Qué van a crear hoy?',fun:'Creatividad sin límites'},
  {k:'educativo',re:/educativ|did[aá]ctic|aprend|ciencia|\bstem\b|letras|n[uú]meros/i,e:['📚','🧠'],h:['#juguetesdidacticos'],grito:'¡Aprender jugando!',gancho:'¿Y si aprender fuera un juego?',fun:'Aprender también puede ser un juego'}
];
var TIPO_GEN={k:'general',e:['🎁','🧸'],h:[],grito:'¡A jugar!',gancho:'¿Ya tienes el regalo perfecto?',fun:'Diversión para toda la familia'};
function tipoDe(p){var t=(p.categoria||'')+' '+(p.nombre||'');for(var i=0;i<TIPOS.length;i++)if(TIPOS[i].re.test(t))return TIPOS[i];return TIPO_GEN}

/* ---------- datos del juguete ---------- */
function disponibles(p){return Math.max(0,(+p.cantidad||0)-PC.vendidasDe(p.id))}
/** Condición de las piezas vendibles con existencia (la que tenga más piezas). */
function condicion(p){var ls=PC.lotesDe(p.id).filter(function(l){return l.comercializable&&l.cantidad>0}).sort(function(a,b){return b.cantidad-a.cantidad});
  return ls.length?PC.condNombre(ls[0].condicion):'Nuevo'}
function precioDe(p){var x=d().PRECIOS[p.id]||{};return +p.precioObjetivo>0?{v:+p.precioObjetivo,o:'de tu precio objetivo'}:(x.ok&&x.sugerido?{v:+x.sugerido,o:'del precio sugerido'}:{v:0,o:''})}
function medidas(p){var m=[p.largoCm,p.anchoCm,p.altoCm].filter(function(x){return +x>0});return m.length?m.map(function(x){return (+x).toLocaleString('es-MX')}).join(' × ')+' cm':''}
function recortar(t,n){t=String(t||'').replace(/\s+\n/g,'\n').trim();return t.length>n?t.slice(0,n).replace(/\s+\S*$/,'')+'…':t}
/** Características capturadas (nunca inventadas): renglones cortos de «características», accesorios, edad, marca. */
function caracteristicas(p,max){
  var F=String(p.caracteristicas||'').split(/\r?\n|;|•|·/).map(function(s){return s.replace(/^[\s\-–*✓✔]+/,'').trim()}).filter(function(s){return s.length>2&&s.length<=60});
  if(p.accesoriosIncluidos)F.push('Incluye: '+recortar(p.accesoriosIncluidos,48));
  if(p.edadRecomendada)F.push('Edad: '+p.edadRecomendada);
  if(p.marca&&F.length<max)F.push('Marca: '+p.marca);
  return F.slice(0,max);
}
function descBreve(p){var t=p.descripcionComercial?String(p.descripcionComercial).replace(/\s+/g,' ').trim():String(p.caracteristicas||'').split(/\r?\n|;|•/).map(function(s){return s.trim()}).filter(Boolean).join(' · ');if(!t)return '';
  var s=t.match(/^.{20,140}?[.!?](\s|$)/);return recortar(s?s[0]:t,140)}
/** Precio de referencia en tiendas (precio en México capturado), solo si es mayor que el precio a publicar. */
function referencia(p,precio){var r=+p.precioMexico||0;return r>precio&&precio>0?{v:r,ah:Math.round((1-precio/r)*100)}:null}
function oportunidad(disp){return disp>0&&disp<=3?(disp===1?'¡Última pieza!':'¡Últimas '+disp+' piezas!'):'¡Aprovecha!'}

/* ---------- textos ---------- */
var EMO={boom:['💥','🔥','⚡'],oferta:['🏷️','💸','✅'],divertido:['🎉','😄','🌈'],premium:['✨','',''],sorpresa:['🎁','👀','❓'],minimalista:['','','']};
var RE_EMO=/[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}\u{2B00}-\u{2BFF}\u{FE0F}\u{200D}]/gu;
function hashtags(p,t,fmt){var n={fb_post:6,ig_historia:5,fb_grupos:3}[fmt.k]||0;if(!n)return '';
  var marca=p.marca?'#'+String(p.marca).normalize('NFD').replace(/[̀-ͯ]/g,'').replace(/[^A-Za-z0-9]/g,'').toLowerCase():'';
  var H=['#juguetes'].concat(t.h,[marca,'#jugueteria','#regalos','#ventadejuguetes']).filter(function(x,i,a){return x&&x.length>2&&a.indexOf(x)===i});
  return H.slice(0,n).join(' ')}
function limpiar(s){return s.split('\n').map(function(l){return l.replace(/[ \t]{2,}/g,' ').trim()}).join('\n').replace(/\n{3,}/g,'\n\n').trim()}
/** Alternativas de texto para el canal y el estilo elegidos: [{t:título, v:texto}]. */
function textos(p,o,fmt,est){
  var t=tipoDe(p),E=EMO[est.k],ti=est.k==='minimalista'||est.k==='premium'?['','']:t.e,N=o.nombre,P=money(o.precio),C=o.cond,D=o.disp,CTA=o.cta,
    F=caracteristicas(p,fmt.k==='fb_post'?4:3),Z=o.zona?'📍 Entrega: '+o.zona:'',PG=o.pago?'💳 Pago: '+o.pago:'',H=hashtags(p,t,fmt),
    R=o.ref?'Precio en tiendas: '+money(o.ref.v)+' · aquí '+P+' (ahorras '+o.ref.ah+' %)':'',DESC=descBreve(p),
    DISP=D>0?(D===1?'Queda 1 pieza':'Disponibles: '+D+' piezas'):'',
    TIT={boom:'¡BOOM! '+t.grito,oferta:'¡OFERTA! '+oportunidad(D),divertido:t.grito,premium:'Disponible ahora',sorpresa:'¿Adivina qué llegó?',minimalista:N}[est.k];
  function bullets(arr,ic){return arr.map(function(f){return (ic||'✔️')+' '+f})}
  var A=[];
  if(fmt.k==='wa_historia'||fmt.k==='ig_historia'){
    A.push({t:'Curiosidad',v:[E[2]+' '+t.gancho,ti[0]+' '+N,'💲 '+P,CTA+' '+E[0]]});
    A.push({t:'Descripción comercial',v:[ti[0]+' '+N,C+' · '+P,DESC,Z]});
    A.push({t:'Divertido',v:[E[1]+' '+t.grito+' '+ti[1],t.fun+'.',N+' por '+P,CTA]});
    A.push({t:'Venta directa',v:[N,'💲 '+P+(o.ref?' (en tiendas '+money(o.ref.v)+')':''),DISP,CTA+' 📩']});
    A.push({t:'Llamado a la acción',v:['👉 '+CTA.toUpperCase()+' 👈',N+' · '+P,D>0&&D<=3?'⏳ '+oportunidad(D):'',Z]});
    if(fmt.k==='ig_historia')A.forEach(function(a){a.v.push(H)});
  }else if(fmt.k==='fb_post'){
    A.push({t:'Curiosidad',v:[E[2]+' '+t.gancho,'',ti[0]+' '+N+' ya está disponible.','',bullets(F).join('\n'),'','💲 '+P,'📦 '+C,Z,'',CTA+' 📩','',H]});
    A.push({t:'Descripción comercial',v:[TIT,'',N,DESC,'',bullets(F).join('\n'),'','💲 Precio: '+P,R,'📦 Condición: '+C,Z,PG,'',CTA,'',H]});
    A.push({t:'Divertido',v:[E[1]+' '+t.grito+' '+ti[1],t.fun+' con '+N+'.','',bullets(F,'⭐').join('\n'),'','Todo por '+P+' '+E[0],CTA,'',H]});
    A.push({t:'Venta directa',v:['🛒 '+N,'💲 '+P+(o.ref?' · en tiendas '+money(o.ref.v):''),'📦 '+C,DISP,Z,PG,'',CTA,'',H]});
    A.push({t:'Llamado a la acción',v:['👉 '+CTA,'',ti[0]+' '+N+' por solo '+P+'.',D>0&&D<=3?'⏳ '+oportunidad(D):'','Pregunta sin compromiso, con gusto te mando más fotos.','',H]});
  }else if(fmt.k==='fb_grupos'){
    A.push({t:'Venta directa',v:['💲 '+P+' · '+N,'📦 '+C,DISP,DESC,Z,PG,'',CTA+' 📩',H]});
    A.push({t:'Curiosidad',v:[E[2]+' '+t.gancho,ti[0]+' '+N+' en '+P,DISP,'','Comenta «INFO» y te mando más fotos 📸',H]});
    A.push({t:'Descripción comercial',v:[TIT,N,DESC,bullets(F).join('\n'),'💲 '+P+' · '+C,R,Z,'',CTA,H]});
    A.push({t:'Divertido',v:[E[1]+' '+t.grito+' '+ti[1],t.fun+'.',N+' a '+P,DISP,'','Comenta «LO QUIERO» y es tuyo 🙌',H]});
    A.push({t:'Llamado a la acción',v:['🚨 '+(D>0&&D<=3?oportunidad(D):'Disponible')+' 🚨',N+' · '+P,'','✅ Comenta «LO QUIERO»','✅ O envíame inbox',Z,H]});
  }else{ // marketplace: título + descripción
    var base=[N,'','💲 Precio: '+P,'📦 Condición: '+C,[p.marca?'Marca: '+p.marca:'',p.modelo?'Modelo: '+p.modelo:''].filter(Boolean).join(' · ')];
    var ext=[];if(p.edadRecomendada)ext.push('Edad recomendada: '+p.edadRecomendada);var md=medidas(p);if(md)ext.push('Medidas: '+md);if(+p.pesoKg>0)ext.push('Peso: '+(+p.pesoKg).toLocaleString('es-MX')+' kg');
    A.push({t:'Descripción completa',v:base.concat(['',recortar(p.descripcionComercial||p.caracteristicas,700),p.accesoriosIncluidos?'\nIncluye: '+p.accesoriosIncluidos:'',ext.length?'\n'+ext.join('\n'):'','',Z,PG,CTA])});
    A.push({t:'Venta directa',v:[N+' · '+P,C+(DISP?' · '+DISP:''),'',bullets(F).join('\n'),'',Z,PG,CTA]});
    A.push({t:'Curiosidad',v:[E[2]+' '+t.gancho,'',N+'.',DESC,'','💲 '+P+' · '+C,R,Z,'',CTA]});
    A.push({t:'Divertido',v:[E[1]+' '+t.grito+' '+ti[1],t.fun+'.','',N,'💲 '+P+' · 📦 '+C,Z,'',CTA]});
  }
  return A.map(function(a){var v=limpiar(a.v.filter(function(x){return x!==null&&x!==undefined}).join('\n'));
    if(est.k==='minimalista')v=limpiar(v.replace(RE_EMO,''));
    if(est.k==='premium')v=limpiar(v.replace(RE_EMO,'').replace(/^/, '✨ '));
    if(est.k==='boom'){var L=v.split('\n');L[0]=L[0].toUpperCase();v=L.join('\n')}
    return {t:a.t,v:v}});
}
/* Compatibilidad: textos clásicos (mismas firmas que antes). */
function textoFacebook(p,precio,pr){var o={nombre:p.nombre,precio:precio,cond:condicion(p),disp:disponibles(p),zona:pr.zona,pago:pr.pago,cta:pr.cierre||'Envíame mensaje para apartarlo.',ref:null};return textos(p,o,fmtDe('marketplace'),estDe('minimalista'))[0].v}
function textoWhatsApp(p,precio,pr){var o={nombre:p.nombre,precio:precio,cond:condicion(p),disp:disponibles(p),zona:pr.zona,pago:pr.pago,cta:pr.cierre||'Escríbeme para apartarlo',ref:null};return textos(p,o,fmtDe('wa_historia'),estDe('divertido'))[1].v}
function textoInstagram(p,precio,pr){var o={nombre:p.nombre,precio:precio,cond:condicion(p),disp:disponibles(p),zona:pr.zona,pago:pr.pago,cta:pr.cierre||'Envíame DM para apartarlo',ref:null};return textos(p,o,fmtDe('ig_historia'),estDe('divertido'))[1].v}

/* ---------- dibujo (canvas) ---------- */
var SANS='Inter, "Segoe UI", Roboto, Arial, sans-serif',SERIF='Georgia, "Times New Roman", serif';
function cargarImg(url){return new Promise(function(ok){if(!url)return ok(null);var i=new Image();i.onload=function(){ok(i)};i.onerror=function(){ok(null)};i.src=url})}
function rnd(seed){var s=0;String(seed).split('').forEach(function(c){s=(s*31+c.charCodeAt(0))|0});return function(){s|=0;s=s+0x6D2B79F5|0;var t=Math.imul(s^s>>>15,1|s);t=t+Math.imul(t^t>>>7,61|t)^t;return ((t^t>>>14)>>>0)/4294967296}}
function rr(x,X,Y,w,h,r){r=Math.max(0,Math.min(r,w/2,h/2));x.beginPath();x.moveTo(X+r,Y);x.arcTo(X+w,Y,X+w,Y+h,r);x.arcTo(X+w,Y+h,X,Y+h,r);x.arcTo(X,Y+h,X,Y,r);x.arcTo(X,Y,X+w,Y,r);x.closePath()}
function fuente(x,peso,px,fam,ital){x.font=(ital?'italic ':'')+peso+' '+Math.round(px)+'px '+(fam||SANS)}
function partir(x,t,maxW){var w=String(t||'').split(/\s+/).filter(Boolean),L=[],c='';
  w.forEach(function(s){var pr=c?c+' '+s:s;if(x.measureText(pr).width>maxW&&c){L.push(c);c=s}else c=pr});if(c)L.push(c);return L}
/** Ajusta el tamaño de letra para que el texto quepa en maxL renglones; si aun así no cabe, recorta con «…». */
function ajustar(x,t,maxW,maxL,px,minPx,set){var L;for(var s=px;s>=minPx;s-=2){set(s);L=partir(x,t,maxW);if(L.length<=maxL&&L.every(function(l){return x.measureText(l).width<=maxW}))return {L:L,px:s}}
  set(minPx);L=partir(x,t,maxW);if(L.length>maxL){L=L.slice(0,maxL);L[maxL-1]=L[maxL-1].replace(/\s*\S*$/,'')+'…'}return {L:L,px:minPx}}
function escribir(x,L,X,Y,lh){L.forEach(function(l,i){x.fillText(l,X,Y+i*lh)});return Y+L.length*lh}
/** Foto «contenida» dentro de un rectángulo (con fondo y esquinas redondeadas opcionales). */
function foto(x,img,X,Y,w,h,o){o=o||{};x.save();rr(x,X,Y,w,h,o.r||0);if(o.fondo){x.fillStyle=o.fondo;x.fill()}x.clip();
  var pad=o.pad||0;if(img){var s=Math.min((w-2*pad)/img.width,(h-2*pad)/img.height),iw=img.width*s,ih=img.height*s;x.drawImage(img,X+(w-iw)/2,Y+(h-ih)/2,iw,ih)}
  else{x.fillStyle=o.tinta||'#64748B';fuente(x,600,Math.min(w,h)*0.07);x.textAlign='center';x.textBaseline='middle';x.fillText('Agrega una foto',X+w/2,Y+h/2);x.textAlign='left';x.textBaseline='alphabetic'}
  x.restore()}
function estrella(x,cx,cy,ro,ri,n,rot){x.beginPath();for(var i=0;i<n*2;i++){var r=i%2?ri:ro,a=rot+i*Math.PI/n;x.lineTo(cx+r*Math.cos(a),cy+r*Math.sin(a))}x.closePath()}
function centrado(x,t,cx,y){x.textAlign='center';x.fillText(t,cx,y);x.textAlign='left'}
function pre(o){return o.precio>0?money(o.precio):'$ —'}

/* Cada estilo tiene su propia composición (no es la misma plantilla con otros colores). V = vertical 9:16. */
var DIBUJO={
boom:function(x,W,H,o){var V=H>W*1.3,u=W/1080,pad=60*u,cx=W/2,s=V?Math.min(W*0.7,H*0.4):H*0.4,cy=V?Math.max(H*0.33,250*u+s/2):H*0.37;
  x.fillStyle='#FFC300';x.fillRect(0,0,W,H);
  x.save();x.translate(cx,cy);x.fillStyle='#FF8A00';for(var i=0;i<28;i++){x.rotate(Math.PI/14);if(i%2){x.beginPath();x.moveTo(0,0);x.lineTo(H*1.5,-H*0.17);x.lineTo(H*1.5,H*0.17);x.closePath();x.fill()}}x.restore();
  x.fillStyle='#fff';x.strokeStyle='#111';x.lineWidth=8*u;estrella(x,cx,cy,s*0.74,s*0.6,20,0);x.fill();x.stroke();
  x.save();x.translate(cx,cy);x.rotate(-0.06);x.fillStyle='#111';x.fillRect(-s/2+16*u,-s/2+16*u,s,s);x.fillStyle='#fff';x.fillRect(-s/2,-s/2,s,s);x.lineWidth=8*u;x.strokeRect(-s/2,-s/2,s,s);
  foto(x,o.img,-s/2+14*u,-s/2+14*u,s-28*u,s-28*u,{fondo:'#fff'});x.restore();
  x.save();x.translate(pad,V?200*u:150*u);x.rotate(-0.1);fuente(x,900,V?150*u:118*u,SANS,true);x.lineJoin='round';x.lineWidth=18*u;x.strokeStyle='#111';x.strokeText('¡BOOM!',0,0);x.fillStyle='#fff';x.fillText('¡BOOM!',0,0);x.restore();
  var by=V?cy+s/2+160*u:H*0.66,bh=V?190*u:130*u;x.save();x.translate(W/2,by);x.rotate(-0.035);x.fillStyle='#111';x.fillRect(-W*0.6,-bh/2,W*1.2,bh);
  x.fillStyle='#fff';aj=ajustar(x,String(o.nombre).toUpperCase(),W-2*pad,2,V?78*u:62*u,30*u,function(z){fuente(x,900,z,SANS,true)});x.textBaseline='middle';var lh=aj.px*1.08;aj.L.forEach(function(l,i){centrado(x,l,0,(i-(aj.L.length-1)/2)*lh)});x.restore();x.textBaseline='alphabetic';
  var pr=V?Math.min(W*0.17,s*0.24):W*0.11,px=cx+s*0.44,py=cy+s*0.28;x.fillStyle='#E11D48';x.strokeStyle='#111';x.lineWidth=7*u;estrella(x,px,py,pr,pr*0.8,16,0.1);x.fill();x.stroke();
  x.save();x.translate(px,py);x.rotate(0.12);x.fillStyle='#fff';var aj=ajustar(x,pre(o),pr*1.45,1,pr*0.55,20*u,function(z){fuente(x,900,z)});x.textBaseline='middle';centrado(x,aj.L[0],0,0);x.restore();x.textBaseline='alphabetic';
  var y=by+bh/2+(V?90:56)*u;x.fillStyle='#111';o.info.slice(0,V?(H>1700*u?4:2):2).forEach(function(t){var a=ajustar(x,'★ '+t,W-2*pad,V?2:1,V?44*u:34*u,24*u,function(z){fuente(x,800,z)});y=escribir(x,a.L.map(function(l){return l}),pad,y,a.px*1.2)+8*u});
  var ch=V?120*u:84*u,cy2=H-pad-ch-(V?40*u:0);x.fillStyle='#E11D48';x.strokeStyle='#111';x.lineWidth=7*u;rr(x,pad,cy2,W-2*pad,ch,ch/2);x.fill();x.stroke();
  x.fillStyle='#fff';var c=ajustar(x,String(o.cta).toUpperCase(),W-2*pad-60*u,1,V?52*u:40*u,22*u,function(z){fuente(x,900,z)});x.textBaseline='middle';centrado(x,c.L[0],W/2,cy2+ch/2);x.textBaseline='alphabetic'},

oferta:function(x,W,H,o){var V=H>W*1.3,u=W/1080,pad=56*u,RED='#D90429',YEL='#FFD60A';
  x.fillStyle='#fff';x.fillRect(0,0,W,H);
  var hh=V?H*0.12:H*0.15;x.fillStyle=RED;x.fillRect(0,0,W,hh);x.fillStyle='#fff';fuente(x,900,V?150*u:112*u,SANS,true);x.textBaseline='middle';
  if(V){centrado(x,'OFERTA',W/2,hh/2)}else x.fillText('OFERTA',pad,hh/2);
  var op=oportunidad(o.disp);fuente(x,800,V?44*u:36*u);
  if(V){x.fillStyle=YEL;x.fillRect(0,hh,W,70*u);x.fillStyle=RED;centrado(x,op.toUpperCase(),W/2,hh+35*u)}
  else{var w=x.measureText(op.toUpperCase()).width+50*u;x.fillStyle=YEL;rr(x,W-pad-w,hh/2-34*u,w,68*u,34*u);x.fill();x.fillStyle=RED;centrado(x,op.toUpperCase(),W-pad-w/2,hh/2)}
  x.textBaseline='alphabetic';
  var bloque=function(X,Y,w,h,grande){x.fillStyle=YEL;x.fillRect(X,Y,w,h);x.fillStyle=RED;for(var i=0;i<w;i+=40*u){x.beginPath();x.moveTo(X+i,Y);x.lineTo(X+i+20*u,Y);x.lineTo(X+i+10*u,Y+14*u);x.closePath();x.fill()}
    var yy=Y+h*0.18;if(o.ref){x.fillStyle='#6B7280';fuente(x,700,grande*0.2);var tr='En tiendas '+money(o.ref.v),tw=x.measureText(tr).width;x.textBaseline='middle';centrado(x,tr,X+w/2,yy+14*u);x.strokeStyle=RED;x.lineWidth=5*u;x.beginPath();x.moveTo(X+w/2-tw/2-6*u,yy+14*u);x.lineTo(X+w/2+tw/2+6*u,yy+14*u);x.stroke();x.textBaseline='alphabetic'}
    x.fillStyle=RED;var a=ajustar(x,pre(o),w-40*u,1,grande,40*u,function(z){fuente(x,900,z)});x.textBaseline='middle';centrado(x,a.L[0],X+w/2,Y+h*(o.ref?0.62:0.55));x.textBaseline='alphabetic';
    if(o.ref){var r=grande*0.42,bx=X+w-r*0.75,byy=Y-r*0.35;x.fillStyle=RED;estrella(x,bx,byy,r,r*0.86,18,0);x.fill();x.fillStyle='#fff';fuente(x,900,r*0.3);x.textBaseline='middle';centrado(x,'AHORRA',bx,byy-r*0.2);fuente(x,900,r*0.5);centrado(x,o.ref.ah+'%',bx,byy+r*0.25);x.textBaseline='alphabetic'}};
  if(V){var fy=hh+100*u,fh=Math.min(H*0.38,H-hh-900*u);foto(x,o.img,pad,fy,W-2*pad,fh,{fondo:'#fff'});
    var ny=fy+fh+70*u;x.fillStyle='#111827';var a=ajustar(x,o.nombre,W-2*pad,2,64*u,32*u,function(z){fuente(x,800,z)});a.L.forEach(function(l,i){centrado(x,l,W/2,ny+i*a.px*1.15)});
    var py=ny+a.L.length*a.px*1.15+30*u;bloque(0,py,W,H*0.19,230*u);
    var iy=py+H*0.19+60*u;x.fillStyle='#374151';fuente(x,700,36*u);o.info.slice(0,2).forEach(function(t){var b=ajustar(x,t,W-2*pad,1,36*u,24*u,function(z){fuente(x,700,z)});centrado(x,b.L[0],W/2,iy);iy+=50*u})}
  else{var cw=W*0.52;foto(x,o.img,pad,hh+30*u,cw-pad,H-hh-150*u,{fondo:'#fff'});
    var rx=cw+20*u,rw=W-rx-pad,yy=hh+80*u;x.fillStyle='#111827';var n=ajustar(x,o.nombre,rw,3,54*u,28*u,function(z){fuente(x,800,z)});yy=escribir(x,n.L,rx,yy,n.px*1.15);
    bloque(rx-10*u,yy+(o.ref?70:30)*u,rw+pad+10*u,250*u,150*u);yy+=(o.ref?70:30)*u+300*u;x.fillStyle='#374151';o.info.slice(0,2).forEach(function(t){var b=ajustar(x,t,rw,1,32*u,22*u,function(z){fuente(x,700,z)});x.fillText(b.L[0],rx,yy);yy+=44*u})}
  var ch=V?110*u:96*u;x.fillStyle=RED;x.fillRect(0,H-ch,W,ch);x.fillStyle='#fff';var c=ajustar(x,o.cta,W-2*pad,1,V?48*u:40*u,22*u,function(z){fuente(x,800,z)});x.textBaseline='middle';centrado(x,c.L[0],W/2,H-ch/2);x.textBaseline='alphabetic'},

divertido:function(x,W,H,o){var V=H>W*1.3,u=W/1080,pad=60*u,R=rnd(o.semilla),COL=['#FF5D8F','#FFB703','#06D6A0','#8B5CF6','#FB8500','#118AB2'];
  x.fillStyle='#CFF1FF';x.fillRect(0,0,W,H);
  for(var i=0;i<46;i++){var X=R()*W,Y=R()*H,s=(10+R()*26)*u,k=i%3;x.fillStyle=x.strokeStyle=COL[i%6];x.globalAlpha=0.75;
    if(k===0){x.beginPath();x.arc(X,Y,s/2,0,7);x.fill()}else if(k===1){x.save();x.translate(X,Y);x.rotate(R()*6);x.beginPath();x.moveTo(0,-s/2);x.lineTo(s/2,s/2);x.lineTo(-s/2,s/2);x.closePath();x.fill();x.restore()}
    else{x.lineWidth=6*u;x.lineCap='round';x.beginPath();for(var j=0;j<4;j++)x.lineTo(X+j*s*0.6,Y+(j%2?s*0.4:-s*0.4));x.stroke()}}x.globalAlpha=1;
  var wy=V?H*0.6:H*0.64;x.fillStyle='#FF5D8F';x.beginPath();x.moveTo(0,H);x.lineTo(0,wy);for(var k2=0;k2<=W;k2+=10*u)x.lineTo(k2,wy+Math.sin(k2/W*Math.PI*4)*22*u);x.lineTo(W,H);x.closePath();x.fill();
  var cx=W/2,cy=V?H*0.33:H*0.37,r=V?W*0.36:H*0.25;
  x.fillStyle='#FFB703';x.beginPath();x.arc(cx,cy,r+26*u,0,7);x.fill();x.fillStyle='#fff';x.beginPath();x.arc(cx,cy,r,0,7);x.fill();
  x.save();x.beginPath();x.arc(cx,cy,r,0,7);x.clip();foto(x,o.img,cx-r*0.75,cy-r*0.75,r*1.5,r*1.5,{fondo:'#fff'});x.restore();
  var tg=o.tag;fuente(x,900,V?60*u:46*u);var tw=Math.min(x.measureText(tg).width+60*u,W-2*pad),th=(V?100:78)*u,tx=pad,ty=V?70*u:40*u;
  x.fillStyle='#fff';rr(x,tx,ty,tw,th,th/2);x.fill();x.beginPath();x.moveTo(tx+tw*0.6,ty+th-2);x.lineTo(tx+tw*0.72,ty+th+34*u);x.lineTo(tx+tw*0.78,ty+th-2);x.fill();
  x.fillStyle='#8B5CF6';var ta=ajustar(x,tg,tw-50*u,1,V?60*u:46*u,22*u,function(z){fuente(x,900,z)});x.textBaseline='middle';centrado(x,ta.L[0],tx+tw/2,ty+th/2);
  var sr=V?W*0.15:W*0.12,sx=cx+r*0.82,sy=cy+r*0.62;x.save();x.translate(sx,sy);x.rotate(-0.18);x.fillStyle='#06D6A0';x.beginPath();x.arc(0,0,sr,0,7);x.fill();
  x.setLineDash([12*u,10*u]);x.strokeStyle='#fff';x.lineWidth=6*u;x.beginPath();x.arc(0,0,sr-14*u,0,7);x.stroke();x.setLineDash([]);
  x.fillStyle='#fff';var pa=ajustar(x,pre(o),sr*1.5,1,sr*0.52,18*u,function(z){fuente(x,900,z)});centrado(x,pa.L[0],0,0);x.restore();
  var y=wy+(V?120:86)*u;x.fillStyle='#fff';var n=ajustar(x,o.nombre,W-2*pad,2,V?72*u:54*u,28*u,function(z){fuente(x,900,z)});n.L.forEach(function(l,i){centrado(x,l,W/2,y+i*n.px*1.12)});
  y+=n.L.length*n.px*1.12+(V?30:10)*u;fuente(x,700,V?40*u:30*u);o.info.slice(0,V?3:1).forEach(function(t){var b=ajustar(x,t,W-2*pad,1,V?40*u:30*u,20*u,function(z){fuente(x,700,z)});centrado(x,b.L[0],W/2,y);y+=(V?54:40)*u});
  var ch=V?110*u:80*u,cy2=H-ch-(V?90:30)*u;fuente(x,900,V?46*u:36*u);var cw=Math.min(x.measureText(o.cta).width+90*u,W-2*pad);x.fillStyle='#fff';rr(x,(W-cw)/2,cy2,cw,ch,ch/2);x.fill();
  x.fillStyle='#FF5D8F';var c=ajustar(x,o.cta,cw-60*u,1,V?46*u:36*u,20*u,function(z){fuente(x,900,z)});centrado(x,c.L[0],W/2,cy2+ch/2);x.textBaseline='alphabetic'},

premium:function(x,W,H,o){var V=H>W*1.3,u=W/1080,pad=70*u,GOLD='#C9A96E',g=x.createLinearGradient(0,0,0,H);g.addColorStop(0,'#0B1220');g.addColorStop(1,'#1E293B');
  x.fillStyle=g;x.fillRect(0,0,W,H);x.strokeStyle=GOLD;x.lineWidth=2*u;x.strokeRect(28*u,28*u,W-56*u,H-56*u);
  var sp=function(t,cx,y,px,col,k){fuente(x,600,px);x.fillStyle=col;if('letterSpacing' in x){x.letterSpacing=Math.round(px*(k||0.3))+'px';centrado(x,t,cx,y);x.letterSpacing='0px'}else centrado(x,t.split('').join(' '),cx,y)};
  var marca=String(o.marca||'Disponible').toUpperCase();
  if(V){sp(marca,W/2,120*u,30*u,GOLD);var fy=170*u,fh=Math.max(H*0.28,H-fy-900*u);x.save();x.shadowColor='rgba(0,0,0,.45)';x.shadowBlur=50*u;x.shadowOffsetY=20*u;x.fillStyle='#fff';rr(x,pad,fy,W-2*pad,fh,26*u);x.fill();x.restore();
    foto(x,o.img,pad,fy,W-2*pad,fh,{fondo:'#fff',r:26*u,pad:40*u});
    var y=fy+fh+130*u;x.fillStyle='#F8FAFC';var n=ajustar(x,o.nombre,W-2*pad-40*u,2,64*u,32*u,function(z){fuente(x,400,z,SERIF)});n.L.forEach(function(l,i){centrado(x,l,W/2,y+i*n.px*1.2)});
    y+=n.L.length*n.px*1.2+30*u;x.fillStyle=GOLD;x.fillRect(W/2-70*u,y,140*u,3*u);y+=130*u;fuente(x,400,128*u,SERIF);x.fillStyle=GOLD;centrado(x,pre(o),W/2,y);
    y+=80*u;if(o.ref){fuente(x,400,32*u);x.fillStyle='#94A3B8';centrado(x,'Precio en tiendas '+money(o.ref.v),W/2,y);y+=60*u}
    fuente(x,400,32*u);x.fillStyle='#CBD5E1';o.info.slice(0,o.ref?1:2).forEach(function(t){var b=ajustar(x,t,W-2*pad,1,32*u,22*u,function(z){fuente(x,400,z)});centrado(x,b.L[0],W/2,y);y+=50*u});
    var bw=W*0.7,bh=100*u,by=H-pad-bh-60*u;x.strokeStyle=GOLD;x.lineWidth=3*u;rr(x,(W-bw)/2,by,bw,bh,bh/2);x.stroke();x.textBaseline='middle';
    var c=ajustar(x,String(o.cta).toUpperCase(),(bw-70*u)/1.15,1,32*u,16*u,function(z){fuente(x,600,z)});sp(c.L[0],W/2,by+bh/2,c.px,GOLD,0.12);x.textBaseline='alphabetic'}
  else{var lw=W*0.5;x.fillStyle='#fff';x.fillRect(28*u,28*u,lw-28*u,H-56*u);foto(x,o.img,28*u,28*u,lw-28*u,H-56*u,{fondo:'#fff',pad:40*u});
    var cx=lw+(W-lw)/2,rw=W-lw-2*pad+20*u,yy=H*0.2;sp(marca,cx,yy,24*u,GOLD);yy+=90*u;x.fillStyle='#F8FAFC';var n2=ajustar(x,o.nombre,rw,3,50*u,26*u,function(z){fuente(x,400,z,SERIF)});n2.L.forEach(function(l,i){centrado(x,l,cx,yy+i*n2.px*1.2)});
    yy+=n2.L.length*n2.px*1.2+20*u;x.fillStyle=GOLD;x.fillRect(cx-50*u,yy,100*u,3*u);yy+=110*u;fuente(x,400,96*u,SERIF);centrado(x,pre(o),cx,yy);yy+=60*u;
    if(o.ref){fuente(x,400,26*u);x.fillStyle='#94A3B8';centrado(x,'En tiendas '+money(o.ref.v),cx,yy);yy+=46*u}
    x.fillStyle='#CBD5E1';o.info.slice(0,2).forEach(function(t){var b=ajustar(x,t,rw,1,26*u,18*u,function(z){fuente(x,400,z)});centrado(x,b.L[0],cx,yy);yy+=40*u});
    var bw2=W-lw-60*u,bh2=74*u,by2=H-pad-bh2-20*u;x.strokeStyle=GOLD;x.lineWidth=3*u;rr(x,cx-bw2/2,by2,bw2,bh2,bh2/2);x.stroke();x.textBaseline='middle';
    var c2=ajustar(x,String(o.cta).toUpperCase(),(bw2-50*u)/1.15,1,26*u,14*u,function(z){fuente(x,600,z)});sp(c2.L[0],cx,by2+bh2/2,c2.px,GOLD,0.12);x.textBaseline='alphabetic'}},

sorpresa:function(x,W,H,o){var V=H>W*1.3,u=W/1080,pad=60*u,R=rnd(o.semilla+'s'),cx=W/2,cy=V?H*0.4:H*0.43;
  var g=x.createRadialGradient(cx,cy,0,cx,cy,Math.max(W,H)*0.75);g.addColorStop(0,'#7C3AED');g.addColorStop(1,'#1E0A3C');x.fillStyle=g;x.fillRect(0,0,W,H);
  for(var i=0;i<16;i++){x.save();x.translate(R()*W,R()*H);x.rotate((R()-0.5)*0.8);x.fillStyle='rgba(255,255,255,'+(0.06+R()*0.1)+')';fuente(x,900,(70+R()*130)*u);x.fillText('?',0,0);x.restore()}
  for(i=0;i<14;i++){var sx=R()*W,sy=R()*H,ss=(10+R()*18)*u;x.fillStyle='#FDE047';estrella(x,sx,sy,ss,ss*0.3,4,0);x.fill()}
  x.fillStyle='#FDE047';var h=ajustar(x,'¿ADIVINA QUÉ LLEGÓ?',W-2*pad,V&&H>1700*u?2:1,V?104*u:76*u,30*u,function(z){fuente(x,900,z)});var hy=V?170*u:110*u;h.L.forEach(function(l,k){centrado(x,l,W/2,hy+k*h.px*1.05)});
  var Y=hy+(h.L.length-1)*h.px*1.05+(V?110:80)*u,s=V?Math.min(W*0.66,H-Y-560*u):H*0.44,X=cx-s/2;cy=Y+s/2;var sg=x.createRadialGradient(cx,Y+s/2,0,cx,Y+s/2,s);sg.addColorStop(0,'rgba(255,255,255,.45)');sg.addColorStop(1,'rgba(255,255,255,0)');x.fillStyle=sg;x.fillRect(0,Y-s*0.5,W,s*2);
  x.fillStyle='#fff';rr(x,X,Y,s,s,30*u);x.fill();foto(x,o.img,X,Y,s,s,{fondo:'#fff',r:30*u,pad:24*u});x.strokeStyle='#EC4899';x.lineWidth=14*u;rr(x,X,Y,s,s,30*u);x.stroke();
  x.fillStyle='#EC4899';var bw=s*0.16;x.beginPath();x.ellipse(cx-bw*0.7,Y-bw*0.35,bw,bw*0.55,-0.4,0,7);x.ellipse(cx+bw*0.7,Y-bw*0.35,bw,bw*0.55,0.4,0,7);x.fill();x.beginPath();x.arc(cx,Y-6*u,bw*0.32,0,7);x.fillStyle='#DB2777';x.fill();
  x.save();x.translate(X+s-30*u,Y+s-20*u);x.rotate(-0.12);fuente(x,900,V?84*u:60*u);var pw=x.measureText(pre(o)).width+90*u,ph=(V?130:96)*u;x.fillStyle='#FDE047';rr(x,-pw/2,-ph/2,pw,ph,18*u);x.fill();
  x.fillStyle='#4C1D95';x.beginPath();x.arc(-pw/2+28*u,0,10*u,0,7);x.fill();x.textBaseline='middle';centrado(x,pre(o),18*u,0);x.restore();x.textBaseline='alphabetic';
  var y=Y+s+(V?140:96)*u;fuente(x,700,V?36*u:28*u);x.fillStyle='#F9A8D4';centrado(x,'Es…',W/2,y-(V?60:42)*u);x.fillStyle='#fff';var n=ajustar(x,o.nombre,W-2*pad,2,V?68*u:48*u,26*u,function(z){fuente(x,800,z)});n.L.forEach(function(l,k){centrado(x,l,W/2,y+k*n.px*1.12)});
  y+=n.L.length*n.px*1.12+(V?30:6)*u;x.fillStyle='rgba(255,255,255,.75)';o.info.slice(0,V&&H>1700*u?3:1).forEach(function(t){var b=ajustar(x,t,W-2*pad,1,V?36*u:26*u,18*u,function(z){fuente(x,600,z)});centrado(x,b.L[0],W/2,y);y+=(V?50:36)*u});
  var ch=V?110*u:76*u,cy2=H-ch-(V?100:28)*u;x.fillStyle='#EC4899';rr(x,pad*1.5,cy2,W-3*pad,ch,ch/2);x.fill();x.fillStyle='#fff';var c=ajustar(x,o.cta,W-3*pad-60*u,1,V?44*u:32*u,18*u,function(z){fuente(x,800,z)});x.textBaseline='middle';centrado(x,c.L[0],W/2,cy2+ch/2);x.textBaseline='alphabetic'},

minimalista:function(x,W,H,o){var V=H>W*1.3,u=W/1080,pad=V?90*u:70*u;x.fillStyle='#F7F7F5';x.fillRect(0,0,W,H);
  var fy=V?110*u:60*u,fh=V?H*0.56:H*0.6;foto(x,o.img,pad,fy,W-2*pad,fh,{fondo:'#fff',r:16*u,pad:30*u});
  var y=fy+fh+(V?110:70)*u;x.fillStyle='#D4D4D4';x.fillRect(pad,y-(V?50:34)*u,W-2*pad,2*u);x.fillStyle='#1F2937';
  if(V){var n=ajustar(x,o.nombre,W-2*pad,2,54*u,30*u,function(z){fuente(x,500,z)});y=escribir(x,n.L,pad,y,n.px*1.2)+50*u;x.fillStyle='#111';fuente(x,700,110*u);x.fillText(pre(o),pad,y+40*u);y+=120*u}
  else{fuente(x,700,76*u);var pw=x.measureText(pre(o)).width;x.fillStyle='#111';x.textAlign='right';x.fillText(pre(o),W-pad,y+20*u);x.textAlign='left';
    x.fillStyle='#1F2937';var n2=ajustar(x,o.nombre,W-2*pad-pw-50*u,2,42*u,24*u,function(z){fuente(x,500,z)});escribir(x,n2.L,pad,y,n2.px*1.2);y+=n2.L.length*n2.px*1.2+30*u}
  x.fillStyle='#6B7280';o.info.slice(0,V?3:1).forEach(function(t){var b=ajustar(x,t,W-2*pad,1,V?34*u:28*u,18*u,function(z){fuente(x,400,z)});x.fillText(b.L[0],pad,y);y+=(V?50:40)*u});
  fuente(x,600,V?36*u:30*u);x.fillStyle='#111';var cy=H-(V?130:50)*u,c=ajustar(x,o.cta,W-2*pad,1,V?36*u:30*u,18*u,function(z){fuente(x,600,z)});x.fillText(c.L[0]+'  →',pad,cy);
  var cw=x.measureText(c.L[0]+'  →').width;x.fillRect(pad,cy+12*u,cw,2*u)}
};

var FONDO={boom:'#FFC300',oferta:'#FFFFFF',divertido:'#CFF1FF',premium:'#0B1220',sorpresa:'#1E0A3C',minimalista:'#F7F7F5'};
/** Renglones de información según el formato (solo datos capturados). */
function infoDe(p,fmt,o){var I=[];
  if(fmt.k==='fb_post')I=caracteristicas(p,3);
  if(fmt.k==='fb_grupos'){var dsc=descBreve(p);if(dsc)I.push(dsc);if(o.disp>0)I.push(o.disp===1?'Queda 1 pieza':'Disponibles: '+o.disp+' piezas')}
  if(fmt.k==='marketplace'||fmt.k==='fb_grupos'||!I.length)I.push('Condición: '+o.cond);
  if(o.zona&&fmt.k!=='marketplace')I.push('Entrega: '+o.zona);
  return I}
/** Dibuja el diseño del estilo y formato elegidos. escala < 1 se usa para las miniaturas. */
function dibujar(img,p,o,fmt,est,escala){
  escala=escala||1;var W=Math.round(fmt.W*escala),H=Math.round(fmt.H*escala),c=document.createElement('canvas');c.width=W;c.height=H;var x=c.getContext('2d');
  var oo=Object.assign({img:img,semilla:p.id,marca:p.marca,info:infoDe(p,fmt,o),tag:tipoDe(p).grito},o);
  if(fmt.seguro){var m=Math.round(H*0.12),dentro=document.createElement('canvas');dentro.width=W;dentro.height=H-2*m;DIBUJO[est.k](dentro.getContext('2d'),W,H-2*m,oo);
    x.fillStyle=FONDO[est.k];x.fillRect(0,0,W,H);x.drawImage(dentro,0,m);return c}
  DIBUJO[est.k](x,W,H,oo);return c}

function descargar(canvas,nombre){var a=document.createElement('a');a.download=nombre;a.href=canvas.toDataURL('image/jpeg',0.92);document.body.appendChild(a);a.click();a.remove()}
function archivoDe(n){return String(n||'juguete').normalize('NFD').replace(/[̀-ͯ]/g,'').replace(/[^A-Za-z0-9]+/g,'-').replace(/^-|-$/g,'').toLowerCase().slice(0,40)||'juguete'}
function copiar(t,msg){
  function ok(){PC.toast(msg||'Copiado')}
  if(navigator.clipboard&&window.isSecureContext)navigator.clipboard.writeText(t).then(ok,function(){viejo()});else viejo();
  function viejo(){var ta=document.createElement('textarea');ta.value=t;ta.style.position='fixed';ta.style.opacity='0';document.body.appendChild(ta);ta.select();
    try{document.execCommand('copy');ok()}catch(e){PC.toast('Selecciona el texto y cópialo')}ta.remove()}
}
/** Compartir directo (celular con conexión segura): abre la hoja de compartir del teléfono. */
function compartir(canvas,texto,nombre){
  return new Promise(function(r){canvas.toBlob(r,'image/jpeg',0.92)}).then(function(b){
    var f=new File([b],nombre,{type:'image/jpeg'});
    if(navigator.canShare&&navigator.canShare({files:[f]}))return navigator.share({files:[f],text:texto}).catch(function(){});
    descargar(canvas,nombre);PC.toast('Tu navegador no permite compartir directo: se descargó la imagen.');
  });
}

/* ---------- ventana ---------- */
function abrir(id){
  var p0=d().D.productos[id];if(!p0)return;var p=Object.assign({id:id},p0);
  var pr=prefs(),pc=precioDe(p),fotos=PC.imgsDe(id),disp=disponibles(p),x=d().PRECIOS[id]||{};
  var fmt=fmtDe(pr.formato),est=estDe(pr.estilo),cierre=pr.cierre&&pr.cierre!=='Envíame mensaje para apartarlo.'?pr.cierre:'';
  function yaPub(code){var pl=d().PLATS.filter(function(q){return q.codigo===code})[0];return !!pl&&d().PUBS.some(function(u){return u.productoId===id&&u.plataformaId===pl.id&&u.estatus==='publicado'})}
  PC.openModal(head('Preparar publicación')+
    '<div class="pubx">'+
    '<p class="small muted" style="margin:0"><b>'+esc(p.nombre)+'</b> · elige el formato y el estilo; la imagen y los textos se generan al momento con los datos del juguete. Facebook, WhatsApp e Instagram no permiten publicar desde otras aplicaciones: descarga la imagen, copia el texto y después registra la publicación para controlar tus existencias.</p>'+
    (fotos.length?'':'<p class="warnline">Este juguete no tiene fotos: agrega al menos una (Editar › Imágenes). Las publicaciones con foto se venden mucho mejor.</p>')+
    '<div class="pub-paso"><h3><span class="pub-n">1</span>Formato de publicación</h3><div class="pub-fmts" role="radiogroup" aria-label="Formato de publicación">'+
      FORMATOS.map(function(f){return '<button type="button" class="pub-fmt" role="radio" data-fmt="'+f.k+'" aria-checked="'+(f.k===fmt.k)+'"><i class="bi bi-'+f.ic+'" aria-hidden="true"></i><span><b>'+esc(f.n)+'</b><small>'+esc(f.med)+'</small></span>'+(yaPub(f.plat)?'<span class="pill entregado">Registrada</span>':'')+'</button>'}).join('')+'</div></div>'+
    '<div class="pub-paso"><h3><span class="pub-n">2</span>Estilo creativo</h3><div class="pub-ests" role="radiogroup" aria-label="Estilo creativo">'+
      ESTILOS.map(function(e){return '<button type="button" class="pub-est" role="radio" data-est="'+e.k+'" aria-checked="'+(e.k===est.k)+'" title="'+esc(e.d)+'"><canvas data-mini="'+e.k+'" aria-hidden="true"></canvas><b>'+esc(e.n)+'</b><small>'+esc(e.d)+'</small></button>'}).join('')+'</div></div>'+
    '<div class="pub-paso"><h3><span class="pub-n">3</span>Datos y diseño</h3><div class="pub-cols"><div class="form">'+
      fld('ppPre','Precio a publicar','<input class="inp num" id="ppPre" type="number" min="0" step="1" value="'+esc(pc.v||'')+'">',
        (pc.o?'Tomado '+pc.o+'. ':'Sin precio: captura uno. ')+(x.ok&&x.minimo?'Tu mínimo es '+money(x.minimo)+' (no se publica).':''))+
      fld('ppDis','Piezas disponibles','<input class="inp" id="ppDis" value="'+disp+'" readonly>',disp?'':'No hay piezas disponibles.')+
      fld('ppTit','Nombre a publicar','<input class="inp" id="ppTit" maxlength="150" value="'+esc(p.nombre)+'">','<span id="ppTitN"></span>',true)+
      fld('ppZon','Zona de entrega','<input class="inp" id="ppZon" maxlength="80" value="'+esc(pr.zona||'')+'" placeholder="Ej. Metro Coyoacán o envío por paquetería">','Se guarda en este equipo para la próxima vez.')+
      fld('ppPag','Formas de pago','<input class="inp" id="ppPag" maxlength="80" value="'+esc(pr.pago||'')+'" placeholder="Ej. Efectivo o transferencia">')+
      fld('ppCie','Llamado a la acción','<input class="inp" id="ppCie" maxlength="60" value="'+esc(cierre)+'">','<span id="ppCieN"></span>',true)+
      (o_ref(p,pc.v)?'':'<p class="small muted f full" style="margin:0">Captura el «Precio en México» del juguete para mostrar el precio de tiendas y el ahorro en el estilo OFERTA.</p>')+
      '<p class="warnline bad f full" id="ppBajo" hidden></p>'+
    '</div><div class="pub-prev"><canvas id="ppCanvas" aria-label="Vista previa de la publicación"></canvas><p class="small muted" id="ppMed"></p>'+
      '<div class="row"><button class="btn primary" type="button" id="ppImg"><i class="bi bi-download" aria-hidden="true"></i>Descargar imagen</button><button class="btn" type="button" id="ppShare"><i class="bi bi-share" aria-hidden="true"></i>Compartir</button>'+
      (fotos.length?'<button class="btn" type="button" id="ppFotos"><i class="bi bi-images" aria-hidden="true"></i>Fotos originales ('+fotos.length+')</button>':'')+'</div></div></div></div>'+
    '<div class="pub-paso"><h3><span class="pub-n">4</span>Textos listos para copiar</h3><div id="ppMkt"></div><div class="pub-txts" id="ppTxts"></div></div>'+
    '<div class="pf"><p class="small muted" id="ppAyuda" style="margin:0;flex:1"></p><button class="btn success" type="button" id="ppReg"><i class="bi bi-check2-circle" aria-hidden="true"></i>Ya lo publiqué: registrar</button></div>'+
    '</div>',function(el){
      var g=function(s){return el.querySelector(s)},img=null,timer=0;
      // Empieza arriba, en «Formato» (sin abrir el teclado del celular en el campo de precio)
      setTimeout(function(){var b=el.querySelector('[data-fmt][aria-checked=true]');if(b)b.focus({preventScroll:true});el.scrollTop=0;if(el.parentNode)el.parentNode.scrollTop=0},0);
      function precio(){return Math.max(0,+g('#ppPre').value||0)}
      function prod(){return Object.assign({},p,{nombre:g('#ppTit').value.trim()||p.nombre})}
      function datos(){var pp=precio(),q=prod();return {nombre:q.nombre,precio:pp,ref:referencia(p,pp),cond:condicion(p),disp:disp,zona:g('#ppZon').value.trim(),pago:g('#ppPag').value.trim(),cta:g('#ppCie').value.trim()||fmt.cta}}
      function pintarMinis(){var o=datos(),q=prod();el.querySelectorAll('[data-mini]').forEach(function(cv){var e=estDe(cv.dataset.mini),c=dibujar(img,q,o,fmt,e,fmt.H>fmt.W?0.16:0.22);cv.width=c.width;cv.height=c.height;cv.getContext('2d').drawImage(c,0,0)})}
      function pintar(){var o=datos(),q=prod(),c=dibujar(img,q,o,fmt,est),cv=g('#ppCanvas');cv.width=c.width;cv.height=c.height;cv.getContext('2d').drawImage(c,0,0);cv.className=fmt.H>fmt.W?'v':'s';
        g('#ppMed').textContent=fmt.n+' · '+fmt.med+' · estilo '+est.n;g('#ppTitN').textContent=g('#ppTit').value.length+' caracteres';
        g('#ppCieN').textContent='Vacío = automático para '+fmt.n+': «'+fmt.cta+'»';
        var b=g('#ppBajo');b.hidden=!(o.precio>0&&x.ok&&x.minimo&&o.precio<x.minimo);if(!b.hidden)b.textContent='El precio está debajo de tu mínimo ('+money(x.minimo)+'): perderías dinero en esta venta.';
        var T=textos(q,o,fmt,est);
        g('#ppTxts').innerHTML=T.map(function(t,i){return '<div class="pub-txt"><div class="pf"><b>'+esc(t.t)+'</b><button class="btn sm" type="button" data-cop="'+i+'"><i class="bi bi-clipboard" aria-hidden="true"></i>Copiar</button></div><textarea class="inp" rows="'+Math.min(14,Math.max(4,t.v.split('\n').length+1))+'" data-txt="'+i+'" aria-label="Texto '+esc(t.t)+'">'+esc(t.v)+'</textarea></div>'}).join('');
        g('#ppMkt').innerHTML=fmt.k==='marketplace'?'<div class="callout"><b>Para Marketplace:</b> título «'+esc(q.nombre)+'» · precio '+money(o.precio)+' · condición «'+esc(o.cond)+'». <button class="btn sm" type="button" id="ppCopTit">Copiar título</button></div>':'';
        var ct=g('#ppCopTit');if(ct)ct.addEventListener('click',function(){copiar(g('#ppTit').value,'Título copiado')});
        g('#ppAyuda').innerHTML=esc(fmt.ayuda)+(fmt.link?' <a href="'+fmt.link[0]+'" target="_blank" rel="noopener noreferrer">'+esc(fmt.link[1])+'</a>':'');
        var pl=d().PLATS.filter(function(z){return z.codigo===fmt.plat})[0];g('#ppReg').disabled=!pl;g('#ppReg').title=pl?'Registra la publicación en '+pl.nombre:'Plataforma no disponible'}
      function todo(){pintar();pintarMinis()}
      var listo=(document.fonts&&document.fonts.ready)||Promise.resolve();
      Promise.all([cargarImg(fotos[0]&&fotos[0].url),listo]).then(function(r){img=r[0];todo()});
      ['#ppPre','#ppZon','#ppPag','#ppCie','#ppTit'].forEach(function(s){g(s).addEventListener('input',function(){guardarPrefs({zona:g('#ppZon').value.trim(),pago:g('#ppPag').value.trim(),cierre:g('#ppCie').value.trim()});pintar();clearTimeout(timer);timer=setTimeout(pintarMinis,300)})});
      el.querySelectorAll('[data-fmt]').forEach(function(b){b.addEventListener('click',function(){fmt=fmtDe(b.dataset.fmt);guardarPrefs({formato:fmt.k});el.querySelectorAll('[data-fmt]').forEach(function(z){z.setAttribute('aria-checked',z===b?'true':'false')});todo()})});
      el.querySelectorAll('[data-est]').forEach(function(b){b.addEventListener('click',function(){est=estDe(b.dataset.est);guardarPrefs({estilo:est.k});el.querySelectorAll('[data-est]').forEach(function(z){z.setAttribute('aria-checked',z===b?'true':'false')});pintar()})});
      g('#ppTxts').addEventListener('click',function(e){var b=e.target.closest('[data-cop]');if(!b)return;if(sinPrecio())return;copiar(g('[data-txt="'+b.dataset.cop+'"]').value,'Texto copiado')});
      function sinPrecio(){if(precio()>0)return false;PC.toast('Captura el precio a publicar');g('#ppPre').focus();return true}
      function nombreArchivo(){return archivoDe(p.nombre)+'-'+fmt.arch+'-'+est.k+'.jpg'}
      g('#ppImg').addEventListener('click',function(){if(!sinPrecio())descargar(dibujar(img,prod(),datos(),fmt,est),nombreArchivo())});
      g('#ppShare').addEventListener('click',function(){if(sinPrecio())return;var t=g('[data-txt="0"]');compartir(dibujar(img,prod(),datos(),fmt,est),t?t.value:'',nombreArchivo())});
      var bf=g('#ppFotos');if(bf)bf.addEventListener('click',function(){fotos.forEach(function(f,i){setTimeout(function(){var a=document.createElement('a');a.href=f.url;a.download=archivoDe(p.nombre)+'-'+(i+1);document.body.appendChild(a);a.click();a.remove()},i*400)})});
      g('#ppReg').addEventListener('click',function(){if(sinPrecio())return;var pl=d().PLATS.filter(function(z){return z.codigo===fmt.plat})[0];if(!pl)return;
        PC.api('POST','publicaciones',{productoId:id,plataformaId:pl.id,precioPublicado:precio(),estatus:'publicado',existencias:disp,observaciones:'Preparada con «Preparar publicación» ('+fmt.n+', estilo '+est.n+').'})
          .then(function(){return PC.cargar()}).then(function(){PC.toast('Publicación en '+pl.nombre+' registrada: la aplicación te avisará si se vende en otro canal')},function(e){PC.toast((e&&e.mensaje)||'No se pudo registrar')})});
    });
}
function o_ref(p,precio){return !!referencia(p,precio)||+p.precioMexico>0}
window.PCPub={abrir:abrir,textos:textos,dibujar:dibujar,FORMATOS:FORMATOS,ESTILOS:ESTILOS,textoFacebook:textoFacebook,textoWhatsApp:textoWhatsApp,textoInstagram:textoInstagram};
})();
