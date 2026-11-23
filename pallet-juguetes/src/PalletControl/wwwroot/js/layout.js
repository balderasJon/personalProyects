/* layout.js — Solo presentación del nuevo diseño (Bootstrap 5.3): barra lateral contraíble, menú en celular,
   migas de pan y título de la sección. No cambia la navegación: observa los atributos que ya pone app.js
   (aria-selected en .tab y aria-current en .subtab) y reacciona a ellos. */
(function(){
'use strict';
var shell=document.getElementById('appShell'),sidebar=document.getElementById('sidebar'),bd=document.getElementById('sbBackdrop'),
    abrir=document.getElementById('sbOpen'),contraer=document.getElementById('sbCollapse'),crumbs=document.getElementById('crumbs'),titulo=document.getElementById('tbTitle');
if(!shell||!sidebar)return;
var KEY='pallet-ui-sidebar';
function guardar(v){try{localStorage.setItem(KEY,v)}catch(e){}}
function leer(){try{return localStorage.getItem(KEY)}catch(e){return null}}

/* Tema de Bootstrap (claro/oscuro) según el sistema, respetando data-theme si se fijó */
var mq=window.matchMedia?matchMedia('(prefers-color-scheme: dark)'):null;
function tema(){var forzado=document.documentElement.getAttribute('data-theme');
  document.documentElement.setAttribute('data-bs-theme',forzado||(mq&&mq.matches?'dark':'light'))}
tema();if(mq&&mq.addEventListener)mq.addEventListener('change',tema);

/* Escritorio: contraer a solo íconos (se recuerda en este equipo) */
if(leer()==='mini')shell.classList.add('sb-mini');
function etiquetaContraer(){var mini=shell.classList.contains('sb-mini');
  contraer.setAttribute('aria-label',mini?'Expandir la barra lateral':'Contraer la barra lateral');
  contraer.setAttribute('aria-expanded',mini?'false':'true');
  var t=contraer.querySelector('.tl');if(t)t.textContent=mini?'Expandir':'Contraer'}
if(contraer){etiquetaContraer();contraer.addEventListener('click',function(){shell.classList.toggle('sb-mini');guardar(shell.classList.contains('sb-mini')?'mini':'');etiquetaContraer()})}

/* Celular y tableta: panel deslizable con fondo */
function movil(){return window.matchMedia&&matchMedia('(max-width: 991.98px)').matches}
function abrirMenu(){shell.classList.add('sb-open');bd.hidden=false;abrir.setAttribute('aria-expanded','true');var a=sidebar.querySelector('.tab[aria-selected="true"]')||sidebar.querySelector('.tab');if(a)a.focus()}
function cerrarMenu(){if(!shell.classList.contains('sb-open'))return;shell.classList.remove('sb-open');bd.hidden=true;abrir.setAttribute('aria-expanded','false')}
if(abrir)abrir.addEventListener('click',function(){shell.classList.contains('sb-open')?cerrarMenu():abrirMenu()});
if(bd)bd.addEventListener('click',cerrarMenu);
document.addEventListener('keydown',function(e){if(e.key==='Escape'&&shell.classList.contains('sb-open'))cerrarMenu()});
sidebar.addEventListener('click',function(e){if(e.target.closest('.tab')&&movil())cerrarMenu()});
window.addEventListener('resize',function(){if(!movil())cerrarMenu()});

/* Migas de pan y título: Control Pallet › Sección [› Subsección] */
function texto(el){var t=el&&el.querySelector('.tl');return ((t||el||{}).textContent||'').trim()}
function actualizar(){
  var tab=sidebar.querySelector('.tab[aria-selected="true"]');if(!tab)return;
  var sec=texto(tab),partes=[sec],vista=document.querySelector('.view[data-view="'+tab.dataset.tab+'"]');
  var sub=vista&&vista.querySelector('.subtab[aria-current="page"]');
  if(sub)partes.push(sub.textContent.trim());
  titulo.textContent=partes[partes.length-1];
  var h='<li class="breadcrumb-item"><button type="button" data-crumb="resumen">Control Pallet</button></li>';
  partes.forEach(function(p,i){var ult=i===partes.length-1;
    h+='<li class="breadcrumb-item'+(ult?' active" aria-current="page':'')+'">'+(ult?esc(p):'<button type="button" data-crumb="'+tab.dataset.tab+'">'+esc(p)+'</button>')+'</li>'});
  crumbs.innerHTML=h;
  document.title=partes[partes.length-1]+' · Control Pallet de Juguetes';
}
function esc(s){return String(s).replace(/[&<>"]/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]})}
crumbs.addEventListener('click',function(e){var b=e.target.closest('[data-crumb]');if(!b)return;
  var t=sidebar.querySelector('.tab[data-tab="'+b.dataset.crumb+'"]');if(t)t.click()});
new MutationObserver(actualizar).observe(document.body,{subtree:true,attributes:true,attributeFilter:['aria-selected','aria-current']});
actualizar();
})();
