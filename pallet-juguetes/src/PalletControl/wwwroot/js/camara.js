/* Código de barras con la cámara del celular.
   La aplicación se abre en el celular por http://192.168.x.x (sin HTTPS) y ahí el navegador no permite la cámara en vivo
   (getUserMedia exige conexión segura). Lo que sí funciona en cualquier conexión es pedirle al celular su app de Cámara
   (<input type="file" capture="environment">): se toma la foto del código y aquí se lee.
   Lectura: BarcodeDetector del navegador cuando existe (Chrome en Android); si no (iPhone), ZXing incluido en
   vendor/zxing (Apache-2.0), que se carga solo la primera vez que se usa y funciona sin internet. */
(function(){
'use strict';
var cargando=null;
var FORMATOS=['ean_13','ean_8','upc_a','upc_e','code_128'];
function esTactil(){try{return matchMedia('(pointer: coarse)').matches}catch(e){return false}}
function zxing(){
  if(window.ZXing)return Promise.resolve(window.ZXing);
  if(!cargando)cargando=new Promise(function(ok,ko){var s=document.createElement('script');s.src='vendor/zxing/zxing-0.21.3.min.js';
    s.onload=function(){ok(window.ZXing)};s.onerror=function(){cargando=null;ko(new Error('No se pudo cargar el lector de códigos.'))};document.head.appendChild(s)});
  return cargando;
}
function imagen(file){return new Promise(function(ok,ko){var u=URL.createObjectURL(file),i=new Image();
  i.onload=function(){ok(i)};i.onerror=function(){URL.revokeObjectURL(u);ko(new Error('No se pudo abrir la foto.'))};i.src=u})}
/** Lienzo de la foto reducido a `max` px por lado (y girado 90° si `rot`), que es lo que leen los decodificadores. */
function lienzo(img,max,rot){var w=img.naturalWidth,h=img.naturalHeight,k=Math.min(1,max/Math.max(w,h));w=Math.max(1,Math.round(w*k));h=Math.max(1,Math.round(h*k));
  var c=document.createElement('canvas');c.width=rot?h:w;c.height=rot?w:h;var x=c.getContext('2d');
  if(rot){x.translate(h,0);x.rotate(Math.PI/2)}x.drawImage(img,0,0,w,h);return c}
function nativo(img){
  if(!('BarcodeDetector' in window))return Promise.resolve(null);
  return Promise.resolve(window.BarcodeDetector.getSupportedFormats?window.BarcodeDetector.getSupportedFormats():FORMATOS).then(function(sop){
    var f=FORMATOS.filter(function(x){return sop.indexOf(x)>=0});if(!f.length)return null;
    var d=new window.BarcodeDetector({formats:f});return d.detect(lienzo(img,1600,false)).then(function(r){return r&&r[0]?r[0].rawValue:null});
  }).catch(function(){return null});
}
function conZxing(img){return zxing().then(function(Z){
  var hints=new Map();hints.set(Z.DecodeHintType.POSSIBLE_FORMATS,[Z.BarcodeFormat.EAN_13,Z.BarcodeFormat.EAN_8,Z.BarcodeFormat.UPC_A,Z.BarcodeFormat.UPC_E,Z.BarcodeFormat.CODE_128]);
  hints.set(Z.DecodeHintType.TRY_HARDER,true);
  var r=new Z.MultiFormatReader();r.setHints(hints);
  var intentos=[[1600,false],[1000,false],[1600,true],[2400,false],[700,false]];
  for(var i=0;i<intentos.length;i++){
    try{var c=lienzo(img,intentos[i][0],intentos[i][1]);return r.decode(new Z.BinaryBitmap(new Z.HybridBinarizer(new Z.HTMLCanvasElementLuminanceSource(c)))).getText()}catch(e){}
  }
  return null;
})}
/** Lee el código de barras de una foto. Resuelve con el texto o null si no se encontró. */
function leer(file){return imagen(file).then(function(img){return nativo(img).then(function(v){return v||conZxing(img)}).then(function(v){URL.revokeObjectURL(img.src);return v?String(v).trim():null})})}

/** HTML del botón (va junto al campo del código). */
function boton(){
  var t=esTactil();
  return '<button class="btn" type="button" data-cam title="'+(t?'Abre la cámara del celular para tomar la foto del código':'Lee el código de barras desde una foto')+'">'+(t?'Cámara':'Desde foto')+'</button>'+
    '<input type="file" accept="image/*" capture="environment" data-camin hidden>';
}
/** Conecta el botón: abre la cámara, lee la foto y llama a alCodigo(texto). `aviso(html)` muestra el estado. */
function conectar(el,alCodigo,aviso){
  var b=el.querySelector('[data-cam]'),f=el.querySelector('[data-camin]');if(!b||!f)return;
  b.addEventListener('click',function(){f.value='';f.click()});
  f.addEventListener('change',function(){var x=f.files&&f.files[0];if(!x)return;
    b.disabled=true;aviso('<p class="small muted">Leyendo el código de la foto…</p>');
    leer(x).then(function(c){
      if(c){aviso('');alCodigo(c)}
      else aviso('<p class="warnline">No se pudo leer el código en la foto. Toma la foto de cerca, con buena luz, con el código completo, derecho y enfocado; o escribe los números.</p>');
    },function(e){aviso('<p class="warnline bad">'+PC.esc((e&&e.message)||'No se pudo leer la foto.')+'</p>')}).then(function(){b.disabled=false});
  });
}
window.PCCam={boton:boton,conectar:conectar,leer:leer};
})();
