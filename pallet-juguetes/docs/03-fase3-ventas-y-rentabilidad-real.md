# Fase 3 — Ventas, cobros, devoluciones, publicaciones y rentabilidad real

**Entregable:** utilidad real por operación.
**Fecha de cierre:** 18 de noviembre de 2026.
**Estado:** concluida. 66 pruebas automatizadas en verde (16 nuevas); regresión de las fases 1 y 2 y de las funciones anteriores sin errores. Ver la sección 8.

**Decisiones vigentes:** inventario por lotes, inicio de sesión local, régimen fiscal **pendiente** (no se calcula IVA ni ISR), objetivo 30 % / mínimo 10 % / terminación 9. Las decisiones nuevas de esta fase que tomé por omisión, para que las confirmes, están en la sección 4.1.

---

## 1. Alcance

| Incluido | Fuera de esta fase |
|---|---|
| Venta con folio, plataforma, publicación, lote, precio publicado, precio real, descuento, gastos (comisión, medio de pago, cargo fijo, logística, envío, almacenamiento, preparación, empaque, publicidad, otros), retenciones e importe cobrado | Sincronización automática con las plataformas (Fase 5) |
| Costo histórico por pieza congelado al vender | Resultado fiscal (requiere definir el régimen) |
| Gastos precargados con la tarifa vigente a la fecha de la venta, editables y marcados como *estimados* hasta confirmarlos | Ventas de varios juguetes en un mismo folio (combos): se registran como ventas separadas |
| Cobro separado del registro: una venta solo está cobrada cuando se captura el importe que llegó | Competencia, dashboard y reportes (Fase 4) |
| Cancelación, devolución (a inventario vendible o a merma), reembolso y reclamación, con sus movimientos de inventario y sus efectos financieros | |
| Publicaciones por canal con control central de existencias y avisos para evitar doble venta | |
| Rentabilidad real por plataforma, juguete y pallet; capital recuperado por pallet | |
| Migración de las ventas anteriores | |

---

## 2. Requerimientos funcionales

| ID | Requerimiento |
|---|---|
| RF3-01 | Registrar una venta con folio consecutivo, plataforma o canal, publicación, juguete, lote (o asignación automática), cantidad, precio publicado, precio real, descuento, cliente, fecha y notas. |
| RF3-02 | Tomar las piezas de lotes vendibles del pallet más antiguo; o del lote elegido. Rechazar la venta si no hay existencias. |
| RF3-03 | Congelar el costo unitario de cada renglón con el costeo vigente de su lote e indicar su origen (vigente, desactualizado, estimado de migración o sin costeo). |
| RF3-04 | Precargar los gastos con la tarifa de la plataforma vigente en la fecha de la venta; permitir editarlos, agregarlos, borrarlos y confirmarlos. |
| RF3-05 | Separar los gastos que descuenta la plataforma de los que paga el vendedor, y las retenciones (anticipo de impuestos) de los gastos. |
| RF3-06 | Registrar, corregir o quitar el cobro (importe y fecha). Marcar *pagado* no equivale a cobrar. |
| RF3-07 | Cancelar una venta: regresar las piezas, quitar los gastos estimados y registrar los gastos no recuperables y el reembolso, si lo hubo. |
| RF3-08 | Registrar devoluciones parciales o totales; si la pieza regresa en una condición no vendible, reclasificarla y reconocer su costo como merma. |
| RF3-09 | Registrar reembolsos sin devolución y reclamaciones (abiertas y cerradas con su costo). |
| RF3-10 | Calcular por venta: ingreso bruto, IVA trasladado, ingreso neto, reembolsos, gastos, costo vendido, merma, utilidad, margen, retenciones, efectivo neto y depósito por cobrar. |
| RF3-11 | Registrar publicaciones por juguete y canal; al cambiar las existencias, avisar qué publicaciones pausar, ajustar o reactivar. |
| RF3-12 | Consultar la rentabilidad real por periodo, plataforma, juguete y pallet, y el capital recuperado de cada pallet. |
| RF3-13 | Migrar las ventas anteriores sin perder información. |

## 3. Requerimientos no funcionales

- **Exactitud:** todo el cálculo en `decimal`, montos al centavo; el reparto entre pallets cuadra al centavo (residuo mayor).
- **Historia inalterable:** el costo, el precio y los gastos de una venta no cambian al recostear un pallet ni al cambiar una tarifa.
- **Consistencia:** todas las escrituras pasan por el candado global y se guardan en una sola transacción; el servidor valida existencias y lotes aunque haya varias pestañas abiertas.
- **Trazabilidad:** cada salida, cancelación y devolución deja su movimiento en el kárdex con el folio; cada incidencia guarda usuario y fecha; la auditoría registra los cambios de ventas, gastos e incidencias.
- **Compatibilidad:** las rutas `/api/ventas` se conservan; los clientes anteriores (sin plataforma ni gastos) siguen funcionando.
- **Sin integraciones no oficiales:** las publicaciones se registran a mano; la aplicación no modifica anuncios en las plataformas.

## 4. Reglas de negocio

1. **Existencia por lote** = piezas recibidas del lote − piezas vendidas netas (vendidas − devueltas) de ventas no canceladas. Las piezas recibidas (`Cantidad` del lote) siguen siendo la base del costeo; las ventas no la cambian.
2. **Asignación de lotes:** sin lote elegido, solo lotes vendibles, del pallet con fecha de compra más antigua primero. Con lote elegido, cualquier lote del juguete (se puede vender una pieza dañada a propósito).
3. **Costo histórico:** costo unitario del costeo vigente del lote al momento de la venta. Si el costeo estaba desactualizado se usa igual y se marca. Sin costeo, costo 0 y marca *sin costeo*.
4. **Gastos estimados:** porcentajes sobre el importe que paga el cliente (precio × piezas − descuento); cargos fijos por pieza. La reserva para devoluciones de la tarifa **no** se precarga (es provisión, no gasto). Retención de ISR sobre el importe sin IVA; de IVA, sobre el IVA trasladado.
5. **Utilidad comercial** = ingreso sin IVA − reembolsos sin IVA − gastos (de la plataforma y propios) − costo de lo vendido − costo de las devoluciones a merma. Las devoluciones que regresan a inventario vendible recuperan su costo.
6. **Cancelada:** sin ingreso ni costo; los gastos estimados se quitan y los confirmados o indicados quedan como pérdida.
7. **Cobro:** una venta está cobrada solo con importe cobrado registrado. **Efectivo neto** = importe cobrado − gastos que pagó el vendedor − reembolsos. **Por cobrar (estimado)** = importe del cliente − gastos que descuenta la plataforma − retenciones.
8. **Reembolsos:** la suma no puede exceder lo que pagó el cliente; una cancelación con reembolso exige que la venta esté cobrada.
9. **Edición:** cambiar juguete, piezas o lote solo sin devoluciones ni incidencias (las piezas se liberan y se vuelven a asignar, con su costo vigente). Una venta cancelada no se reactiva.
10. **Eliminación:** solo ventas sin cobro, no pagadas/entregadas y sin incidencias; en otro caso se cancelan.
11. **Lotes:** no se puede ajustar ni reclasificar un lote por debajo de sus piezas vendidas.
12. **Publicaciones:** la que originó la venta descuenta sus piezas anunciadas; si el juguete se agota queda *vendida*. Las demás publicadas piden **pausar** (sin piezas) o **ajustar existencias** (anuncian más de las disponibles). Si vuelven piezas (cancelación o devolución vendible) se quita el aviso de pausar y la vendida pide **reactivar**. «Ya lo hice» aplica el cambio en el registro.
13. **Capital recuperado por pallet** = efectivo neto de las ventas cobradas, repartido entre los pallets de sus renglones en proporción a su importe; acumulado, sin filtro de fechas; contra la inversión total vigente del pallet.
14. **Resultado fiscal:** no se estima mientras el régimen esté pendiente; se muestran el IVA trasladado y las retenciones registradas.

### 4.1 Decisiones tomadas por omisión (para tu confirmación)

| # | Decisión | Alternativa |
|---|---|---|
| D1 | Lote automático: pallet más antiguo primero (PEPS) entre lotes vendibles | Elegir siempre el lote a mano |
| D2 | Cargos fijos de la tarifa por pieza vendida (igual que en la Fase 2) | Por venta, sin importar las piezas |
| D3 | Al cancelar se quitan los gastos estimados | Conservarlos como pérdida |
| D4 | Ventas anteriores *pagadas* o *entregadas* = cobradas por su total (no tenían gastos) | Dejarlas sin cobro hasta confirmarlas |
| D5 | Costo de ventas anteriores = costeo vigente al migrar, marcado «Estimado (venta anterior)» | — |
| D6 | Canales anteriores sin plataforma equivalente («MercadoLibre», «Instagram», «Otro») quedan como texto, «sin plataforma asignada» | Asignarlos a una plataforma |
| D7 | Capital recuperado sin filtro de fechas | Solo dentro del periodo |

## 5. Modelo de datos

**Cambios en `ventas`** (columnas nuevas): `Folio`, `PlataformaId` (FK), `PublicacionId`, `TarifaId`, `PrecioPublicado`, `Descuento`, `FactorIngreso` (1/(1+IVA) si se cobró IVA; 1 si no), `RetencionIsr`, `RetencionIva`, `ImporteCobrado` (null = no cobrada), `FechaCobro`, `CantidadDevuelta`, `CostoTotal`, `CostoOrigen`, `Migrada`. `ProductoId`, `Cantidad` y `Precio` resumen el renglón; `Estado` (apartado/pagado/entregado/cancelado) es el estado comercial.

| Tabla nueva | Campos | Notas |
|---|---|---|
| `venta_detalle` | Id, VentaId, ProductoId, LoteId, PalletId, Cantidad, Devueltas, DevueltasAMerma, PrecioUnitario, CostoUnitario, CostoTotal, CostoCorridaId, CostoOrigen | FK a venta y lote (restrict). CHECK: 0 ≤ DevueltasAMerma ≤ Devueltas ≤ Cantidad, Cantidad > 0 |
| `venta_gastos` | Id, VentaId, IncidenciaId, Tipo, Concepto, Monto, DeducidoPorPlataforma, Estimado, CreadoUtc | Tipos: comision, medio_pago, cargo_fijo, logistica, envio, almacenamiento, preparacion, empaque, publicidad, devolucion, reclamacion, otro |
| `venta_incidencias` | Id, VentaId, Tipo, Fecha, Cantidad, CondicionDestino, MontoReembolsado, Estado, Motivo, Usuario, CreadoUtc | Tipos: cancelacion, devolucion, reembolso, reclamacion; Estado abierta/cerrada |
| `publicaciones` | Id, ProductoId, PlataformaId, LoteId, IdExterno, Url, PrecioPublicado, FechaPublicacion, Estatus, Existencias, RequiereAccion, Observaciones, Fuente, CreadoUtc, ActualizadoUtc | Estatus: pendiente, publicado, pausado, vendido, cancelado, eliminado. `Fuente` = manual (preparado para la Fase 5) |

**Kárdex:** tipos nuevos `venta` (−), `cancelacion_venta` (+) y `devolucion` (+). Muestran la existencia; no cambian las piezas recibidas del lote.

```
Pallet 1─* InventarioLote 1─* VentaDetalle *─1 Venta *─1 Plataforma
                                               Venta 1─* VentaGasto
                                               Venta 1─* VentaIncidencia 1─* VentaGasto
Producto 1─* Publicacion *─1 Plataforma        Venta *─0..1 Publicacion
```

## 6. Diseño técnico

- **Dominio puro** `Dominio/Calculo/ResultadoVenta.cs`: `CalculoVenta.Calcular` (resultado real), `AsignarLotes`, `RepartirPorPeso`. Sin base de datos; probado con los casos 12–15.
- **Servicio** `Servicios/Ventas.cs`: existencias por lote, asignación, costo congelado, gastos estimados por tarifa (reutiliza `SelectorTarifa` y `Rentabilidad` de la Fase 2), cobro, cancelación, devolución, reembolso, reclamación, eliminación y revisión de publicaciones.
- **API** `Endpoints/VentasApi.cs`; las rutas de ventas anteriores se movieron aquí sin cambiar sus URL.
- **Migración** `Esquema.MigrarVentasFase3`: respaldo `pallet-antes-de-fase3-*.db`, folios por fecha de alta, plataforma por nombre de canal, renglones por lote (PEPS), costo vigente marcado como estimado, salida en el kárdex de las no canceladas y cobro de las pagadas/entregadas. Solo procesa ventas con folio 0: es idempotente.
- **Interfaz** `wwwroot/js/ventas.js`: formulario con estimación previa, ficha de la venta, Publicaciones y Rentabilidad real. `app.js` usa el resultado del servidor para Resumen, Ventas, Semana y exportación.

### 6.1 API (todas requieren sesión)

| Método | Ruta | Uso |
|---|---|---|
| POST / PUT | `/api/ventas[/{id}]` | Registrar o editar. Campos nuevos opcionales: `plataformaId`, `publicacionId`, `loteId`, `precioPublicado`, `descuento`, `gastos[]`, `retencionIsr`, `retencionIva`, `importeCobrado`, `fechaCobro`. Sin `gastos` y con plataforma, se precargan de la tarifa |
| GET | `/api/ventas/{id}` | Venta, renglones, gastos, incidencias y resultado real |
| GET | `/api/ventas/estimar?productoId&plataformaId&precio&cantidad&descuento&fecha` | Gastos y retenciones que precargaría la tarifa |
| PATCH | `/api/ventas/{id}/estado` | Pagado/entregado; opcional `importeCobrado`, `fechaCobro` |
| POST / DELETE | `/api/ventas/{id}/cobro` | Registrar/corregir o quitar el cobro |
| POST | `/api/ventas/{id}/cancelar` · `/devolucion` · `/reembolso` · `/reclamacion` | Incidencias |
| POST | `/api/incidencias/{id}/cerrar` | Cierra una reclamación con su costo |
| POST · PUT · DELETE | `/api/ventas/{id}/gastos` · `/api/venta-gastos/{id}` | Gastos de la venta (editar = confirmar) |
| POST | `/api/ventas/{id}/gastos/confirmar` | Confirma los estimados |
| DELETE | `/api/ventas/{id}` | Solo apartadas sin cobro ni incidencias |
| GET | `/api/productos/{id}/existencias` | Existencia por lote |
| GET · POST · PUT · DELETE | `/api/publicaciones[/{id}]` | Publicaciones (con ventas, se marcan eliminadas) |
| GET | `/api/publicaciones/alertas` | Cuántas requieren atención |
| POST | `/api/publicaciones/{id}/atendida` | Confirma que ya se pausó, ajustó o reactivó |
| GET | `/api/rentabilidad-real?desde&hasta&plataformaId&productoId&palletId` | Totales, por plataforma, juguete y pallet, capital recuperado y ventas |

`/api/estado` agrega `ventaDetalles`, `ventaResultados`, `publicaciones` y `plataformas`; `/api/respaldo` incluye todas las tablas de las fases 2 y 3.

### 6.2 Decisiones técnicas

- Una venta = un juguete (como antes) con uno o varios renglones por lote. Evita reescribir la captura y los reportes existentes.
- La cantidad del lote se mantiene como base del costeo para no alterar costeos ya calculados; la existencia se deriva.
- El resultado se calcula al consultar a partir de datos históricos guardados (precio, costo, gastos, incidencias); no se guardan totales que puedan quedar desfasados.
- El reparto por pallet usa el importe de cada renglón como peso; el costo se asigna exacto por renglón.

## 7. Historias de usuario y criterios de aceptación

| Historia | Criterio de aceptación |
|---|---|
| Como vendedor quiero registrar una venta de Mercado Libre y ver cuánto gané | Al elegir plataforma se precargan los gastos de su tarifa vigente; al guardar veo utilidad, margen y depósito esperado |
| Quiero saber qué dinero ya llegó | La venta muestra «Sin cobro» hasta registrar el importe; Resumen y capital recuperado solo cuentan lo cobrado |
| Quiero registrar una devolución dañada | La pieza pasa a un lote dañado, su costo aparece como merma y el reembolso y el envío de regreso reducen la utilidad |
| Quiero evitar vender dos veces la misma pieza | Al venderse en un canal, las demás publicaciones piden pausarse y una venta sin existencias se rechaza |
| Quiero comparar canales con datos reales | Rentabilidad real muestra utilidad, margen, cobrado y por cobrar por plataforma, juguete y pallet |
| Quiero que la historia no cambie | Recostear el pallet o cambiar la tarifa no altera las ventas registradas |

## 8. Casos de prueba

Tarifa **de prueba** de Mercado Libre Clásica (no es la real): comisión 15 %, cargo fijo $25, envío absorbido $120, empaque $10; desde el 1-dic, 17 %. Pallet de $631.80 con 2 piezas → $315.90 por pieza.

| # | Caso | Datos | Resultado esperado | Tipo |
|---|---|---|---|---|
| 12 | Venta con descuento | 2 pz publicadas a $639, vendidas a $600 c/u, descuento $50; comisión 172.50, cargo fijo 50 (plataforma), empaque 20 | Utilidad **$275.70**, margen 23.97 %; sin cobro: efectivo $0, por cobrar $927.50; con cobro de $927.50: efectivo $907.50 | Unitaria |
| 13 | Venta con comisión y envío | 1 pz a $639 | Gastos precargados 95.85 + 25 + 120 + 10; utilidad **$72.25**, margen 11.31 %; por cobrar **$398.15**; cobrada: efectivo **$388.15** | Unitaria + integración |
| 14 | Devolución | Caso 13 cobrado, devuelta con reembolso $639 y envío de regreso $120 | A inventario vendible: utilidad −$370.85, costo recuperado. Dañada: merma $315.90, utilidad **−$686.75**, pieza en lote «Dañado» | Unitaria + integración |
| 15 | Cancelación | 2 pz canceladas con guía no reembolsable $80 | Ingreso $0, utilidad **−$80**, gastos estimados quitados, 2 piezas disponibles otra vez; no se puede cancelar dos veces ni eliminar | Unitaria + integración |
| 16 | Varios canales | 1 pieza publicada en ML y Amazon; se vende en ML | ML «vendido», Amazon «pausar»; venta en Amazon rechazada (409 «No hay existencias…»); al cancelar, Amazon sin aviso y ML «reactivar» | Integración |
| 19 | Consulta histórica | Venta el 18-nov; después gasto de $400 al pallet, recosteo ($515.90) y venta el 2-dic con tarifa al 17 % | Noviembre conserva costo $315.90 y utilidad **$72.25**; diciembre: comisión $108.63, utilidad **−$140.53**; total −$68.28 | Integración |
| 20 | Recuperación de inversión | Pallet $631.80, venta del caso 13 | Antes del cobro: recuperado $0, falta $631.80. Cobrada en $398.15: recuperado **$388.15 = 61.44 %**, falta $243.65 | Integración |
| — | Consistencia | Venta de 2 pz | No se reclasifica una pieza vendida (409); editar a 1 pz libera una; eliminar regresa ambas con movimiento `cancelacion_venta`; el lote conserva 2 piezas recibidas | Integración |
| — | Cliente anterior | Venta con canal «Mercado Libre Clásica» como texto | Se asigna la plataforma y se precargan sus gastos | Integración |

**Ejecución:** `dotnet test PalletControl.sln` → **66 de 66** (43 unitarias, 23 de integración). Ajuste en una prueba de la Fase 1: el kárdex ahora incluye la salida por venta (cambio de diseño anunciado en la Fase 1).

### 8.1 Pruebas de navegador y migración (fuera del repositorio)
- **Flujo Fase 3:** nueva venta → gastos precargados de la tarifa y estimación ($72.25, depósito $398.15) → ficha de la venta → registrar cobro (sugerido $398.15, efectivo $388.15) → devolución dañada con reembolso y envío (−$686.75) → lista con utilidad y cobro → segunda venta sin publicación → avisos en Resumen → Publicaciones («pausar» y «Ya lo hice») → Rentabilidad real y capital recuperado → celular de 390 px sin desbordes. Sin errores en la consola.
- **Regresión:** recorridos completos de las fases 1 y 2 sin errores.
- **Migración:** base creada con la versión de la Fase 2 (5 ventas: pagada, apartada, cancelada, entregada y una con canal «MercadoLibre») → folios V-00001 a V-00005, plataforma asignada a «Facebook Marketplace», «MercadoLibre» sin asignar, costo estimado, 3 cobradas, 4 salidas en el kárdex (la cancelada no), existencia 1 de 5, respaldo previo creado. Al reiniciar no se duplica nada.

---

## 9. Manual de usuario (Fase 3)

### 1. Registra la venta
*Ventas › + Venta.* Elige el juguete y la **plataforma** (o escribe el canal si no está en el catálogo). Si registraste la **publicación**, elígela: se toma su precio. Deja el lote en *Automático* salvo que quieras vender una pieza en particular. Captura el precio que pagó el cliente y, si hubo, el descuento adicional.

Los **gastos** se precargan con la tarifa vigente y aparecen como *estimados*: corrígelos con lo que realmente cobró la plataforma. La caja azul muestra una estimación; el resultado oficial aparece al guardar.

### 2. Registra el cobro
Cuando el dinero aparezca en tu banca, abre la venta y usa **Registrar cobro** con el importe exacto que llegó (en marketplaces, el depósito ya sin comisiones). Marcar *Pagado* sin importe no cuenta como dinero recuperado.

### 3. Si algo sale mal
En la ficha de la venta: **Cancelar venta**, **Devolución** (indica cómo regresó la pieza: si viene dañada se aparta como no vendible), **Reembolso** (dinero sin devolución) o **Reclamación** (queda abierta hasta cerrarla con lo que costó).

### 4. Evita la doble venta
*Análisis comercial › Publicaciones.* Registra dónde está publicado cada juguete. Cuando se venda, revisa los avisos (también en Resumen), haz el cambio en la plataforma y presiona **Ya lo hice**.

### 5. Mide la rentabilidad real
*Análisis comercial › Rentabilidad real.* Filtra por fechas, plataforma o pallet. Verás ingresos, utilidad con costo histórico, efectivo cobrado, lo que falta por cobrar y cuánto capital has recuperado de cada pallet.

---

## 10. Pendientes y riesgos

| # | Tema | Detalle |
|---|---|---|
| 1 | Confirmar decisiones D1–D7 | Sección 4.1. |
| 2 | Ventas anteriores | Su costo es estimado con el costeo vigente al migrar y no tenían gastos registrados: su utilidad puede estar sobrestimada. Pueden agregarse los gastos desde su ficha. Las de canal «MercadoLibre», «Instagram» u «Otro» quedan sin plataforma. |
| 3 | Tarifas reales | Sin tarifa capturada, los gastos no se precargan: hay que capturarlos a mano. |
| 4 | Régimen fiscal | Pendiente: no hay IVA, ISR ni resultado fiscal. Al definirlo, las ventas nuevas guardan su factor de IVA; las anteriores no cambian. |
| 5 | Devoluciones a merma | Reclasificar piezas cambia el inventario del pallet y marca su costeo como desactualizado (es correcto: hay merma nueva). Las ventas ya registradas conservan su costo. |
| 6 | Recosteo posterior a ventas | Las ventas registradas conservan el costo con el que se vendieron, aunque el nuevo costeo reparta distinto la inversión; la suma de costos vendidos puede no coincidir con el nuevo costeo. Se indica el origen del costo en cada renglón. |
| 7 | Combos | Una venta de varios juguetes se registra como varias ventas; el descuento del combo se reparte a mano. |
| 8 | Publicaciones manuales | La aplicación avisa, pero no pausa anuncios. La sincronización con APIs oficiales es de la Fase 5. |
| 9 | Rendimiento | Los resultados se calculan al consultar; adecuado para miles de ventas. |
