# Bitácora de cambios

Formato: fecha · versión · cambios · impacto en datos.

## 2026-11-23 · Historia de Instagram en «Preparar publicación»

**Agregado (a petición del usuario)**
- Sección **Historia de Instagram**: imagen vertical 1080×1920 con franjas libres arriba y abajo (12 % cada una) para que el nombre de la cuenta y la barra de respuesta de Instagram no tapen el precio; texto corto para agregar con «Aa»; descargar, copiar, compartir y «Ya lo publiqué: registrar» (plataforma Instagram).
- Como en WhatsApp, la publicación es manual: la aplicación prepara la imagen y el texto.
- El catálogo de plataformas agrega las que falten (p. ej. Instagram en bases creadas antes), sin modificar las existentes.

**Impacto en datos**: puede agregar la plataforma «Instagram» si no existía.

## 2026-11-23 · Sin IVA ni promoción en los precios

**Cambiado (a petición del usuario: lo que costó el pallet ya incluye IVA)**
- El precio mínimo y el sugerido ya no suman IVA aunque haya datos fiscales guardados de antes. Se quita la sección «Situación fiscal»; la pestaña se llama **Márgenes**.
- Se quita el campo «Descuento promocional» (no se usaba en ningún precio que muestre la aplicación) y el campo «Plataforma» de los objetivos (sin tarifas no tenía efecto).
- Prueba nueva: con «cobro IVA» guardado, el sugerido sigue siendo costo ÷ 0.70.

**Impacto en datos**: ninguno; la configuración fiscal y los descuentos guardados se conservan sin uso.

## 2026-11-23 · Textos de precio objetivo y de lotes sin valor

**Cambiado**
- Al escanear, la explicación ya no dice «Objetivo: precio más bajo registrado × tipo de cambio» (texto antiguo, ese valor no se usaba). Ahora explica que el precio objetivo (sugerido) y el mínimo salen del costo por pieza (prorrateo proporcional al valor) y de los márgenes de «Márgenes y fiscal».
- El costeo proporcional ya no reporta «El lote 4b80ffc349ed no tiene valor…»: nombra el juguete, su condición y piezas (p. ej. «El juguete 002 · Peluche (Nuevo sellado, 1 pza.)…») y dice cómo resolverlo. Prueba unitaria nueva.

**Impacto en datos**: ninguno.

## 2026-11-21 · «Completar datos» para todos

**Agregado (a petición del usuario)**
- Botón **Completar datos (N)** en Inventario, visible cuando hay juguetes con código de barras numérico (8 a 14 dígitos) y datos vacíos (categoría, marca, características, foto, o sin referencia de mercado ni precio en México).
- Ventana con avance: consulta uno por uno (`POST /api/productos/{id}/completar-datos`), registra qué se llenó en cada juguete, **se detiene sola al agotarse el límite de UPCitemdb** (429, con la hora de reinicio) y se puede detener a mano. Resumen: completados, sin datos, con error y pendientes.
- Los códigos ya consultados no gastan consultas (caché); los que no están en ninguna base siguen contando como pendientes de datos.

**Impacto en datos**: solo en los juguetes que se completan.

## 2026-11-21 · «Completar datos desde UPCitemdb»

**Agregado (a petición del usuario)**
- Botón en la ficha del juguete (si tiene código de barras): consulta el código (UPCitemdb y, si no, Open Products Facts; respeta la caché y el límite diario) y llena **solo campos vacíos**: nombre (solo si está vacío o es el mismo código), categoría, marca, modelo, características, peso, medidas, referencia de mercado (si no hay precio en México), precio de lista, y hasta 3 fotos si el juguete no tiene ninguna.
- `POST /api/productos/{id}/completar-datos`; la descarga de fotos se comparte con «imágenes de UPCitemdb».
- Sin datos: aviso con botones para buscar el código en Google y Amazon México.
- Nota: llenar la referencia de mercado cambia el reparto del costo estimado; tras completar varios juguetes conviene «Recalcular precios mínimos» o aplicar el costeo.

**Impacto en datos**: solo en el juguete que se completa.

## 2026-11-21 · Respeto del límite de UPCitemdb

**Cambiado**
- Verificado con los encabezados reales de la API: el plan gratuito es de 100 consultas por IP y responde 429 con `X-RateLimit-Reset` (hora de reinicio) y `Retry-After`. Su documentación advierte que seguir consultando estando bloqueado puede alargar el bloqueo.
- Al recibir 429 la aplicación guarda la hora de reinicio y **no vuelve a consultar UPCitemdb hasta entonces**; los códigos nuevos se buscan en Open Products Facts y el mensaje dice a qué hora se reinicia.
- `GET /api/codigo-barras-estado` y aviso en «Escanear código»: «quedan N consultas gratuitas hoy» (de `X-RateLimit-Remaining`) o «sin consultas, se reinicia el …».

**Impacto en datos**: ninguno.

## 2026-11-21 · Segunda fuente para códigos de barras

**Agregado (a petición del usuario: varios juguetes no traían resultados)**
- **Open Products Facts** (API v2 `https://world.openproductsfacts.org/api/v2/product/<código>.json`, gratuita y sin registro) como segunda fuente: se consulta si UPCitemdb no tiene el código, si se agotó su límite diario (429) o si no responde. Llena nombre, marca, categoría, contenido e imágenes; **no trae precios**.
- Verificado el 2026-11-21: la API responde; su categoría de juguetes tenía 101 productos, así que su cobertura es baja.
- Los «no encontrado» guardados antes se vuelven a preguntar solo a Open Products Facts (sin gastar consultas de UPCitemdb); el resultado queda en la caché con su fuente.
- Si ninguna base lo tiene: botones para buscar el código en Google y en Amazon México (búsqueda por código verificada).
- Las pruebas automáticas no salen a internet (`CodigoBarras:UrlOpenProductsFacts` a un puerto cerrado).

**Opciones con más cobertura (no implementadas: requieren registro y pago)**: plan de pago de UPCitemdb, Barcode Lookup, Go-UPC, UPC Database.

**Impacto en datos**: ninguno en el esquema.

## 2026-11-20 · Escanear un código existente suma una pieza

**Cambiado (a petición del usuario)**
- En «Escanear código», si el código ya está en el inventario se suma **una pieza** automáticamente (antes se abría el juguete para editar la cantidad). La ventana se queda abierta y vacía para el siguiente escaneo, con **Deshacer** (ajuste de −1) y **Abrir juguete**.
- `POST /api/productos/{id}/sumar-pieza`: suma al lote vendible del pallet con fecha de compra más reciente; queda como movimiento «ajuste» con motivo «Escaneo de código de barras: +1 pieza» y revisa las publicaciones del juguete.
- Un código nuevo sigue abriendo el alta con los datos de UPCitemdb.
- Prueba automática del +1 y del deshacer.

**Impacto en datos**: ninguno en el esquema.

## 2026-11-20 · Botón «Borrar todos los datos»

**Agregado (a petición del usuario, para quitar datos de prueba)**
- Ajustes › **Borrar todos los datos…**: ventana que dice cuántos pallets, juguetes y ventas se borrarán, ofrece descargar el respaldo JSON y exige escribir **BORRAR**.
- `POST /api/datos/borrar-todo` (`{ confirmacion: "BORRAR" }`): copia la base con `VACUUM INTO` a `datos/respaldos/pallet-antes-de-borrar-<fecha>.db`, borra en una transacción los datos capturados y mueve las fotos a `datos/respaldos/imagenes-antes-de-borrar-<fecha>`. Deja un registro en la bitácora.
- Se conservan: configuración (Ajustes), plataformas, objetivos generales y por categoría, situación fiscal, tarifas, usuarios y caché de códigos de barras.
- Prueba automática: sin la palabra no borra; con ella borra, deja respaldo, conserva la configuración y permite capturar de nuevo.

**Impacto en datos**: solo cuando el usuario lo confirma.

## 2026-11-20 · Se quita «Descuento sobre el precio en México»

**Quitado (a petición del usuario)**
- El campo de Ajustes «Descuento sobre el precio en México (%)» ya no se usaba en ningún cálculo: el precio objetivo y el mínimo salen del costo por pieza más el margen (Márgenes y fiscal). Se quita de la pantalla para no confundir.

**Impacto en datos**: ninguno; la columna `configuracion.DescuentoSugerido` se conserva sin uso.

## 2026-11-20 · Más tiendas en la búsqueda de precios

**Agregado**
- Tiendas nuevas en «Precio en tiendas de México»: **Juguetron**, **Juguetibici** y **Amazon México**, cada una con su campo de precio. El precio en México es el promedio de las seis tiendas que tengan precio.
- Búsquedas verificadas el 2026-11-20 en navegador: Juguetron `https://www.juguetron.mx/<término>?_q=<término>&map=ft` (resultados con precios) y Amazon México `https://www.amazon.com.mx/s?k=<término>`.
- Juguetibici: no se encontró tienda en línea propia (su catálogo en línea aparece en Rappi); por decisión del usuario su botón busca «Juguetibici + nombre» en Google.
- Exportación a Excel (CSV) del inventario con las tres columnas nuevas. Prueba automática del promedio.

**Impacto en datos**: migración no destructiva; columnas `PrecioJuguetron`, `PrecioJuguetibici` y `PrecioAmazon` en `productos` con valor 0. El precio en México de los juguetes existentes no cambia hasta que se editen. Un cliente que no envíe estos campos no los borra.

## 2026-11-20 · Código de barras con la cámara del celular

**Agregado**
- Botón **Cámara** junto al campo del código en «Escanear código» y en el alta del juguete: abre la app de cámara del celular (`<input type="file" capture="environment">`), lee el código de la foto y lo busca como si se hubiera escaneado.
- Por qué con foto y no con video en vivo: la aplicación se abre en el celular por `http://192.168.x.x` (sin HTTPS) y ahí los navegadores no permiten la cámara en vivo (`getUserMedia` exige conexión segura). La cámara del celular sí se puede abrir en cualquier conexión.
- Lectura: `BarcodeDetector` del navegador si existe (Chrome en Android); si no (iPhone), ZXing 0.21.3 incluido en `wwwroot/vendor/zxing` con su licencia Apache-2.0; se carga solo al usarlo y no necesita internet. Prueba girando la foto y con varios tamaños.
- Si la foto no tiene un código legible, avisa y pide repetirla o escribir los números.
- La fila del código se acomoda en dos líneas en pantallas angostas.

**Impacto en datos**: ninguno.

## 2026-11-20 · Pallets en pesos y sin manifiesto

**Cambiado (a petición del usuario: el pallet se compró en pesos y no hay manifiesto)**
- «Nuevo pallet» viene en **Pesos (MXN)** por omisión; el tipo de cambio solo aparece al elegir dólares. País de origen vacío por omisión.
- Ayudas del formulario: el costo es lo que pagaste en total; el valor de mercado se captura solo si hay manifiesto (si no, se deja vacío).
- Guía de Inicio: el paso del valor de mercado es opcional («solo si tienes manifiesto»), no cuenta en el avance y ya no aparece como «siguiente paso»; con los demás pasos hechos muestra «¡Todo listo!».

**Para corregir un pallet capturado en dólares**: Análisis comercial › Pallets y costos › (pallet) › Editar pallet › Moneda: Pesos (MXN) › Guardar. El costeo queda «Desactualizado»: vuelve a aplicarlo y usa «Recalcular precios mínimos».

**Impacto en datos**: ninguno automático; los pallets existentes no se modifican.

## 2026-11-20 · Sin inicio de sesión

**Cambiado (a petición del usuario)**
- La aplicación ya no pide usuario ni contraseña: abre directo en Inicio, en la computadora y en el celular. Se oculta el botón de cuenta.
- Cada petición entra como el usuario «local» (así aparece en la bitácora de auditoría y en el historial de costeos).
- Interruptor en `appsettings.json` → `Auth:Requerido` (predeterminado `false`). Con `true` vuelve el inicio de sesión tal como estaba.
- Prueba nueva del modo sin inicio de sesión; las pruebas de seguridad se ejecutan con el inicio de sesión activado.

**Riesgo aceptado**: cualquier persona en la misma red Wi‑Fi puede abrir la aplicación y modificar los datos.

**Impacto en datos**: ninguno. Los usuarios que ya existían se conservan en la base por si se reactiva el inicio de sesión.

## 2026-11-20 · Simulador de precios sencillo

**Cambiado (a petición del usuario)**
- El simulador ya no pide plataforma, categoría, peso, tipo de publicación, modalidad, medio de pago ni fecha de tarifa.
- Ahora solo se elige el **juguete**, el **precio** a probar (por omisión, el sugerido) y las **piezas**. Muestra en grande si **ganas o pierdes** y cuánto, la ganancia por pieza y el margen.
- Indicadores: costo por pieza (real o estimado), precio mínimo, precio sugerido y precio de equilibrio (= costo).
- Tabla de comparación: tu precio, sugerido, mínimo y precio de mercado de referencia, con lo que ganas o pierdes en cada uno.
- Ganancia = precio − costo por pieza, antes de impuestos y de los gastos de la venta.

**Impacto en datos**: ninguno.

## 2026-11-19 · Inventario en tarjetas con etiquetas; sin tarifas por plataforma

**Cambiado (a petición del usuario)**
- Se eliminó la sección de **tarifas por plataforma**. La pantalla de Análisis comercial ahora se llama **Márgenes y fiscal** y conserva solo los objetivos de rentabilidad y la situación fiscal.
- El precio mínimo y el sugerido (al escanear, «Recalcular mínimos», Inicio e Inventario) se calculan siempre como **venta directa**: costo de la pieza + margen. Las tarifas que ya estuvieran capturadas no se borran de la base, pero ya no se usan en esta sugerencia.
- Se quitó de la guía de Inicio el paso «Captura las tarifas».

**Agregado**
- Inventario en **tarjetas con foto** (vista inicial) y opción **Tabla** simplificada; la vista elegida se recuerda en el equipo.
- **Etiquetas de estado** por juguete: Disponible, Quedan N, Agotado, Apartado, En Facebook / WhatsApp / Mercado Libre, Sin publicar, Sin foto, Sin precio, Bajo el mínimo. Arriba, cada etiqueta muestra cuántos juguetes la tienen y un clic filtra por ella.
- «Valor del pallet» queda plegado en una línea de resumen para dar espacio al inventario.
- El costo promedio por pieza del encabezado ahora usa el costo de cada juguete (real o estimado), no la inversión entre las piezas registradas.

**Impacto en datos**: ninguno en el esquema. Los precios mínimos guardados no cambian hasta que se use «Recalcular precios mínimos».

## 2026-11-19 · Preparar publicación (Facebook Marketplace y WhatsApp)

**Agregado**
- Botón **Preparar publicación** en la ficha del juguete y **Publicar** en cada renglón del Inventario.
- Arma, con los datos del juguete: título, precio (tu precio objetivo o el sugerido; el mínimo solo se muestra como referencia y no se publica), condición del lote con más piezas, descripción (marca, modelo, características, accesorios, edad, medidas, peso, zona de entrega, formas de pago y cierre).
- Imagen cuadrada 1080×1080 para Marketplace e imagen vertical 1080×1920 para historia de WhatsApp, con la foto principal, nombre, condición y precio; descarga de las fotos del juguete; botón «Compartir» cuando el navegador lo permite.
- Zona de entrega, formas de pago y cierre se recuerdan en el equipo (navegador).
- «Ya lo publiqué: registrar» crea la publicación (plataforma, precio, existencias) para que la aplicación avise si se vende por otro canal.
- Aviso si el precio queda debajo del mínimo o si el juguete no tiene fotos.

**Por qué es manual**: Facebook Marketplace (publicaciones personales) y los estados de WhatsApp no ofrecen una API para publicar desde otra aplicación; se copia y pega.

**Impacto en datos**: ninguno en el esquema; solo se agregan registros de publicación cuando el usuario lo confirma.

## 2026-11-19 · Inicio guiado para quien empieza a vender

**Agregado**
- La pestaña «Resumen» ahora es **Inicio**:
  - **Guía paso a paso** con el siguiente paso destacado y un botón que lleva a cada pantalla.
  - **Tu pallet en números**: juguetes y piezas, inversión, costo promedio por pieza (real o estimado), valor de mercado con avance de registro contra el manifiesto, recuperado y ganancia; filtro por pallet.
  - **Precio de cada juguete**: costo por pieza, mínimo, sugerido y tu precio.
  - **Glosario** de términos.
- Botón «Usar el precio sugerido» para los juguetes sin precio de venta (`POST /api/productos/aplicar-sugeridos`).
- Inventario: columna **Mínimo**; el costo por pieza muestra el real o el estimado («est.»).
- `GET /api/productos/precios`: costo, mínimo y sugerido de todos los juguetes con el mismo cálculo del escaneo.

**Cambiado**
- Se quitaron de Inicio indicadores que confundían al empezar: utilidad neta negativa por la inversión aún no recuperada y precio promedio con el costo anterior.
- El aviso de «sin precio en México» se reemplazó por «sin datos para calcular su precio» (el precio ya se calcula también con el código de barras).
- Sin pallet registrado no se muestra una inversión de ejemplo.

## 2026-11-19 · «Recalcular mínimos» con el criterio del mejor canal

**Cambiado**
- **Recalcular precios mínimos** usa el mismo cálculo que la sugerencia al escanear: mínimo del mejor canal (costo real o estimado + comisiones y envíos + margen mínimo; venta directa sin tarifas). Antes: mínimo = costo real (sin comisiones ni margen) o la estimación proporcional con el precio en México.
- Los juguetes sin datos suficientes conservan su mínimo y se listan en el aviso.
- El cálculo vive en un solo servicio (`SugeridorPrecios`) para el escaneo y el recálculo.

**Datos**
- Sin cambios de estructura. Al presionar el botón se reemplazan los mínimos actuales (pide confirmación).

## 2026-11-18 · Precio sugerido y mínimo al escanear

**Agregado**
- Al escanear el código de barras (o al capturar precio en México, cantidad o pallet) se calculan y llenan el **precio objetivo** y el **mínimo**: referencia de mercado (precio en México o promedio de precios de EE. UU. × tipo de cambio) → costo por pieza (real o estimado proporcional) → motor de precios en el mejor canal (o venta directa sin comisiones si no hay tarifas). Con explicación de cada paso y avisos.
- Pallet: campo opcional **valor de mercado estimado** (manifiesto, en la moneda de compra) para que el estimado sea estable desde el primer juguete.
- Juguete: **valor de referencia** guardado; el costeo proporcional lo usa cuando no hay precio en México ni valor relativo del lote.
- 6 pruebas nuevas (5 unitarias, 1 de integración): 96 en total.

**Cambiado**
- El código de barras ya no pone como objetivo el precio más bajo de EE. UU. convertido; el objetivo y el mínimo salen del motor de precios.

**Datos (migración automática, no destructiva)**
- Columnas nuevas `ValorReferencia`, `ValorReferenciaOrigen` (productos) y `ValorMercadoEstimado` (pallets), vacías en los registros existentes.

## 2026-11-18 · Precio de lista en dólares con conversión

**Agregado**
- Selector **MXN / USD** en el precio de lista. En USD se convierte con el tipo de cambio del día (redondeado al peso) y se guardan el monto en dólares y el tipo de cambio usado; se muestran en la ficha y en el CSV del inventario.
- Al escanear un código de barras, el precio de lista se llena con el monto en dólares de UPCitemdb (selector en USD).
- Sin tipo de cambio del día no se acepta un precio en dólares (nunca se guardan dólares como pesos).
- 1 prueba de integración nueva: 90 en total.

**Datos (migración automática, no destructiva)**
- Columnas nuevas `PrecioListaUsd` y `PrecioListaTipoCambio` en `productos` (vacías en los juguetes existentes, que siguen en pesos).

## 2026-11-18 · Fase 5 — Integraciones con APIs oficiales

**Agregado**
- Análisis comercial › Integraciones: cuentas por plataforma con capacidades declaradas según la documentación oficial.
- Mercado Libre: conexión OAuth 2.0 (con PKCE opcional y opción de pegar la dirección de regreso), renovación de token, vínculo de anuncios, pausar/reactivar/ajustar piezas, enviar precio, importar pedidos pagados con comisión y envío reales, detectar cancelaciones y proponer cobros estimados.
- Meta: catálogo de Facebook/Instagram/WhatsApp — agotado/disponible y piezas (`quantity_to_sell_on_facebook`).
- «Leer plataforma» genera propuestas; solo se aplican las marcadas y confirmadas. Resultado por propuesta e historial; bitácora técnica sin secretos.
- Credenciales cifradas (Data Protection; DPAPI en Windows), nunca devueltas ni respaldadas; captura solo desde la PC principal.
- 9 pruebas nuevas (6 unitarias, 3 de integración con plataformas simuladas): 89 en total.

**Pendiente**
- Amazon México: no se pudo consultar su documentación oficial; no se programó sin verificarla.

**Datos (migración automática, no destructiva)**
- Tablas nuevas `integracion_cuentas`, `sincronizacion_lotes`, `sincronizacion_propuestas`, `integracion_eventos`; columnas nuevas en `publicaciones` y `ventas`. Carpeta nueva `datos/claves` (claves de cifrado; no se sube al repositorio).

## 2026-11-18 · Fase 4 — Inteligencia comercial: competencia, dashboard y reportes

**Agregado**
- Competencia: precios de productos similares con plataforma o tienda, marca, modelo, condición, código de barras, características, foto, envío, URL, vendedor y fecha; vigentes o descartados.
- Estadística del mercado (mínimo, máximo, promedio, mediana), diferencia % y posición competitiva con banda configurable (±5 %); aviso si el mercado está debajo de tu mínimo; resumen en la ficha del juguete.
- Dashboard ejecutivo con filtros por fecha, pallet, categoría, juguete y plataforma: inversión, ingresos, costo vendido, gastos comerciales, utilidad bruta y neta estimada, margen, ROI, capital recuperado y pendiente; indicadores de inventario (disponibles, publicados, vendidos, dañados, incompletos, sin movimiento, antigüedad); ventas y utilidad por plataforma, rentabilidad por categoría, rotación, condición, más y menos rentables, más vendidos, capital por pallet, candidatos a liquidación y evolución de la recuperación.
- Reportes CSV para Excel: ventas, rentabilidad por juguete, plataformas, categorías, capital por pallet, inventario por lote, competencia y candidatos a liquidación.
- Umbrales configurables: días sin movimiento (30) y banda de precio de mercado (±5 %).
- 14 pruebas nuevas (9 unitarias, 5 de integración): 80 en total.

**Corregido**
- Eliminar un juguete sin ventas que tenía publicaciones registradas fallaba por la relación entre tablas; ahora se eliminan sus publicaciones y precios de competencia junto con él.

**Datos (migración automática, no destructiva)**
- Tabla nueva `competencia`; columnas nuevas `DiasSinMovimiento` y `BandaCompetenciaPct` en `configuracion`. No se modifica ningún dato anterior.

## 2026-11-18 · Fase 3 — Ventas, cobros, devoluciones, publicaciones y rentabilidad real

**Agregado**
- Ventas con folio, plataforma, publicación, lote, precio publicado, descuento, gastos por venta (precargados con la tarifa vigente y marcados como estimados), retenciones e importe cobrado.
- Costo histórico por pieza congelado al vender, con su origen (costeo vigente, desactualizado, estimado o sin costeo).
- Cobro separado del registro: solo lo cobrado cuenta como efectivo y capital recuperado.
- Cancelaciones, devoluciones (a inventario vendible o a merma), reembolsos y reclamaciones, con movimientos de inventario y efecto en la utilidad.
- Publicaciones por canal con existencias centrales y avisos para pausar, ajustar o reactivar (control de doble venta).
- Rentabilidad real por periodo, plataforma, juguete y pallet; capital recuperado por pallet.
- Ficha de la venta, vistas «Publicaciones» y «Rentabilidad real», avisos nuevos en Resumen y columnas de utilidad y cobro en Ventas y en la exportación.
- 16 pruebas nuevas (9 unitarias, 7 de integración): 66 en total.

**Cambiado**
- «Ventas cobradas», la gráfica de recuperación, la semana y la utilidad neta usan el importe cobrado y el efectivo neto, no el estado «Pagado».
- Las ventas generan movimientos en el kárdex (venta, cancelación, devolución); la cantidad recibida del lote no cambia.
- Lo vendido se cuenta neto de devoluciones.
- Eliminar una venta solo se permite si está apartada, sin cobro ni incidencias; en otro caso se cancela.
- No se puede ajustar ni reclasificar un lote por debajo de sus piezas vendidas.
- El respaldo JSON incluye las tablas de las fases 2 y 3.

**Datos (migración automática, no destructiva)**
- Respaldo `pallet-antes-de-fase3-*.db` antes de migrar.
- Columnas nuevas en `ventas`; tablas nuevas `venta_detalle`, `venta_gastos`, `venta_incidencias` y `publicaciones`.
- Ventas anteriores: folio por fecha de alta, plataforma por nombre de canal (si coincide), renglones por lote, costo estimado con el costeo vigente, salida en el kárdex de las no canceladas y cobro por su total de las pagadas o entregadas. Ningún monto anterior se modifica. Es idempotente.

## 2026-11-18 · Fase 2 — Plataformas, tarifas y motor de precios

**Agregado**
- Catálogo de plataformas (6 del requerimiento + 4 canales que ya se usaban en ventas), sin tarifas precargadas.
- Tarifas con vigencia e historial ("Nueva vigencia"), criterios por tipo de publicación, modalidad, categoría, medio de pago, rango de precio, peso y tamaño; cargos de la plataforma, gastos del vendedor y retenciones por separado.
- Objetivos de rentabilidad (margen, markup, utilidad fija) por ámbito, con margen mínimo, descuento promocional y redondeo. Objetivo general inicial: margen 30 %, mínimo 10 %, terminación 9.
- Situación fiscal (régimen pendiente, IVA) separada de los gastos.
- Motor de precios: equilibrio, mínimo, objetivo, sugerido, promocional, utilidad, margen, ROI, depósito, retenciones y alertas; nunca sugiere menos del mínimo.
- Pantallas Análisis de productos (filtros, orden, paginación), Simulador de precios y Plataformas y tarifas; rentabilidad por canal en la ficha del juguete; historial de precios recomendados.
- 27 pruebas nuevas (19 unitarias, 8 de integración): 50 en total.

**Datos (migración automática, no destructiva)**
- Tablas nuevas: `plataformas`, `tarifas_plataforma`, `parametros_rentabilidad`, `configuracion_fiscal`, `precios_producto`.
- Se siembran las plataformas, el objetivo general y la situación fiscal "pendiente" solo si no existen. No se modifica ningún dato anterior.

## 2026-11-18 · Fase 1 — Pallets, inventario por lotes y costeo

**Agregado**
- Pallets con moneda, tipo de cambio (DOF o manual), costo convertido, gastos adicionales e inversión total.
- Inventario por lotes (juguete × pallet × condición) con kárdex de movimientos; 7 condiciones; tratamiento de no vendibles.
- Costeo equitativo, proporcional y manual; modos *absorber* o *pérdida* para no vendibles; cuadre exacto al centavo (residuo mayor); simulación; historial inmutable; detección de costeos desactualizados.
- Costo real por pieza en inventario, ficha del juguete y pallet; filtros por pallet y condición.
- Datos comerciales del juguete: marca, modelo, subcategoría, descripción comercial, accesorios, edad, peso y medidas (se llenan desde UPCitemdb en kg y cm), estado comercial y fecha de ingreso.
- Inicio de sesión, usuarios, cambio de contraseña y bitácora de auditoría.
- Proyecto de pruebas xUnit (23 pruebas: 15 unitarias, 8 de integración).
- Documentación en `docs/`.

**Cambiado**
- Toda la API requiere sesión, salvo `/api/auth/*`. Las fotos también.
- La cantidad y el estado de un juguete se derivan de sus lotes. Con un solo lote se siguen editando desde la ficha (queda un ajuste en el kárdex).
- La inversión de Ajustes es la suma de los pallets; el campo queda de solo lectura cuando hay pallets.
- "Recalcular precios mínimos": mínimo = costo real por pieza redondeado hacia arriba; sin costeo, la estimación proporcional anterior.
- Importar desde Excel pide el pallet de los juguetes nuevos.

**Datos (migración automática, no destructiva)**
- Respaldo `pallet-antes-de-fase1-*.db` antes de migrar.
- Se crea "Pallet 1" (MXN) con la inversión de Ajustes; un lote por juguete con su cantidad y condición (nuevo → Nuevo sellado, dañado → Dañado, incompleto → Incompleto) y un movimiento "Saldo inicial".
- No se borra ni modifica ningún registro existente; las columnas `productos.Cantidad` y `productos.Estado` se conservan.

## 2026-11-18 · Fase 0 — Diagnóstico
- Documento `docs/00-diagnostico-y-propuesta.md` y decisiones del usuario. Sin cambios de código.

## Versiones anteriores (resumen)
| Fecha | Cambio |
|---|---|
| 2026-11-06 | Precio mínimo proporcional, precios por tienda (Liverpool, Mercado Libre, Walmart), traducción de características, detalle con carrusel |
| 2026-11-06 | Tipo de cambio DOF diario, precio en México, imágenes por juguete, valor del pallet |
| 2026-11-06 | Simplificación a un solo socio (migración con respaldo) |
| 2026-11-05 | Código de barras con UPCitemdb y precios sugeridos |
| 2026-11-05 | Ejecución en .NET 10 y desde cualquier carpeta (VS Code) |
| 2026-11-02 | Versión inicial: inventario, ventas, gastos, semana, ajustes (C# .NET 8 + SQLite) |
