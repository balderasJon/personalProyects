# Bitácora de cambios

Formato: fecha · versión · cambios · impacto en datos.

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
