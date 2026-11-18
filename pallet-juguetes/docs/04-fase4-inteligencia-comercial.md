# Fase 4 — Inteligencia comercial: competencia, dashboard y reportes

**Entregable:** análisis comercial integral.
**Fecha de cierre:** 18 de noviembre de 2026.
**Estado:** concluida. 80 pruebas automatizadas en verde (14 nuevas); regresión de las fases 1 a 3 y de las funciones anteriores sin errores. Ver la sección 8.

**Decisiones del usuario al iniciar la fase:** se aprueban las decisiones D1–D7 de la Fase 3; un juguete es **sin movimiento** (candidato a liquidación) a los **30 días** sin venderse; la banda de **precio de mercado** es **±5 %** alrededor de la mediana; los reportes son **CSV para Excel**. Ambos umbrales se guardan en la configuración y se pueden cambiar desde el Dashboard (botón **Umbrales**).

---

## 1. Alcance

| Incluido | Fuera de esta fase |
|---|---|
| Registro de precios de competencia (producto propio, producto de referencia, plataforma o tienda, marca, modelo, condición, código de barras, características, foto, precio publicado, envío, total, URL, vendedor, fecha, observaciones) | Búsqueda automática de precios (Fase 5, solo con APIs oficiales o servicios autorizados; queda el campo `Fuente`) |
| Mínimo, máximo, promedio y mediana del mercado; diferencia % y posición competitiva | Reportes en PDF (no se pidieron) |
| Dashboard ejecutivo: indicadores financieros, de inventario y comerciales, con filtros por fecha, pallet, categoría, juguete y plataforma | Resultado fiscal (régimen pendiente) |
| Gráficas: ventas y utilidad por plataforma, recuperación de la inversión, rentabilidad por categoría, rotación, inventario por condición, más y menos rentables | |
| Candidatos a liquidación; competencia en la ficha del juguete | |
| 8 reportes CSV con los mismos filtros | |

## 2. Requerimientos funcionales

| ID | Requerimiento |
|---|---|
| RF4-01 | Registrar, editar, descartar (sin borrar) y eliminar precios de competencia, con foto opcional para identificar el producto sin UPC/EAN. |
| RF4-02 | Calcular por juguete: mínimo, máximo, promedio y mediana del precio total de la competencia vigente; diferencia % de tu precio contra la mediana; posición (abajo, en precio de mercado, arriba) y lugar. |
| RF4-03 | Avisar cuando la mediana del mercado está por debajo de tu precio mínimo. |
| RF4-04 | Dashboard con los indicadores financieros, de inventario y comerciales del requerimiento y sus gráficas. |
| RF4-05 | Filtrar todos los indicadores por fecha, pallet, categoría, juguete y plataforma. |
| RF4-06 | Identificar juguetes sin movimiento y candidatos a liquidación según el umbral configurado. |
| RF4-07 | Descargar reportes CSV: ventas, rentabilidad por juguete, por plataforma, por categoría, capital por pallet, inventario por lote, competencia y candidatos a liquidación. |
| RF4-08 | Consultar la competencia desde la ficha del juguete. |

## 3. Requerimientos no funcionales

- **Una sola fuente de cálculo:** dashboard, reportes y vistas usan el mismo servicio (`Servicios/Comercial.cs`) y el resultado por venta de la Fase 3 (costo histórico). Nada se recalcula en el navegador.
- **Exactitud:** todo en `decimal`; montos al centavo; porcentajes a 2 decimales; reparto entre pallets al centavo (residuo mayor).
- **Sin datos inventados:** sin precios de competencia no hay posición; sin precio propio no hay diferencia; los gastos generales no se reparten por filtros (se indica).
- **Sin scraping:** la competencia se captura a mano; la arquitectura (`Fuente`) permite agregar APIs oficiales en la Fase 5.
- **Seguridad:** todas las rutas requieren sesión; las fotos se validan por contenido (JPG, PNG, WEBP o GIF, máx. 5 MB) y se sirven solo con sesión; el CSV neutraliza fórmulas (texto que empieza con `=`, `+`, `-`, `@`).
- **Accesibilidad de las gráficas:** una serie por gráfica (el título la nombra), valor escrito en la punta de cada barra, tooltip también con teclado, colores validados para daltonismo en modo claro y oscuro (azul positivo, rojo negativo); los datos de cada gráfica también están en un reporte CSV.

## 4. Reglas de negocio

1. **Precio de competencia que se compara:** precio publicado + envío (precio total). Solo cuentan los registros **vigentes**. Un precio publicado no es una venta realizada.
2. **Tu precio:** tu publicación activa más barata; si no hay, tu precio objetivo. Sin ninguno, no se calcula posición.
3. **Mediana:** valor central de los precios ordenados (promedio de los dos centrales si son pares).
4. **Diferencia %** = (tu precio − mediana) ÷ mediana × 100. **Posición:** *abajo del mercado* si es menor que −banda; *arriba* si es mayor que +banda; si no, *en precio de mercado*. Banda configurable (inicial ±5 %).
5. **Lugar:** 1 + número de precios de la competencia menores que el tuyo (1 = el más barato).
6. **Sin movimiento:** juguete con piezas vendibles cuya última venta (o, si nunca se vendió, el ingreso de su lote más antiguo con piezas) tiene **30 días o más** (configurable). Son los **candidatos a liquidación**.
7. **Antigüedad del inventario:** días desde el ingreso de cada lote, ponderados por las piezas vendibles disponibles; rangos 0–30, 31–60, 61–90 y más de 90 días.
8. **Rotación** = piezas vendidas netas ÷ piezas recibidas.
9. **Inversión:** con filtros de fecha, pallet o plataforma, la suma de la inversión total de los pallets; con filtro de categoría o juguete, el costo asignado por el costeo vigente a esas piezas.
10. **Ingresos** = ingreso sin IVA − reembolsos sin IVA. **Utilidad bruta** = ingresos − costo de lo vendido − merma. **Utilidad comercial** = bruta − gastos de las ventas (igual a la Fase 3). **Utilidad neta estimada** = comercial − gastos generales de operación del periodo (pestaña Gastos), solo **sin** filtros de pallet, categoría, juguete o plataforma (esos gastos no se pueden asignar); con esos filtros, la neta es la comercial y se avisa.
11. **Margen promedio** = utilidad comercial ÷ ingresos. **ROI** = utilidad neta estimada ÷ inversión. **Rendimiento sobre costo** = utilidad comercial ÷ costo vendido.
12. **Capital recuperado** = efectivo neto de las ventas cobradas, acumulado hasta la fecha final (no depende de la fecha inicial). **Pendiente** = inversión − recuperado (mínimo 0).
13. **Filtro por pallet:** de una venta con piezas de varios pallets, ingresos, gastos y efectivo se reparten por el importe de cada renglón; el costo es exacto por renglón.
14. **Fechas:** filtran por fecha de la venta; los gastos generales, por su fecha.

## 5. Modelo de datos

| Tabla nueva | Campos | Notas |
|---|---|---|
| `competencia` | Id, ProductoId (FK), ProductoReferencia, PlataformaId (FK, opcional), PlataformaTexto, Marca, Modelo, Condicion, CodigoBarras, Caracteristicas, PrecioPublicado, PrecioEnvio, PrecioTotal, Url, Vendedor, FechaConsulta, Observaciones, ImagenArchivo, Vigente, Fuente, Usuario, CreadoUtc, ActualizadoUtc | Índice (ProductoId, Vigente). CHECK de precios no negativos. `Fuente` = manual (preparado para la Fase 5). Auditoría automática |

**Columnas nuevas en `configuracion`:** `DiasSinMovimiento` (30) y `BandaCompetenciaPct` (5).

```
Producto 1─* Competencia *─0..1 Plataforma
```

Al eliminar un juguete (solo posible sin ventas) se eliminan también sus precios de competencia, sus fotos y sus publicaciones.

## 6. Diseño técnico

- **Dominio puro** `Dominio/Calculo/Indicadores.cs`: estadística de precios, posición competitiva, antigüedad, días sin movimiento, rotación y ROI.
- **Servicio** `Servicios/Comercial.cs`: resumen de competencia y dashboard (reutiliza `Ventas.CargarAsync`/`Resultado`, existencias por lote y `CalculoVenta.RepartirPorPeso`).
- **API** `Endpoints/ComercialApi.cs`.
- **Interfaz** `wwwroot/js/comercial.js`: Dashboard, Competencia, Reportes y la sección de competencia en la ficha del juguete. Gráficas en HTML/SVG, sin librerías externas.

### 6.1 API (todas requieren sesión)

| Método | Ruta | Uso |
|---|---|---|
| GET | `/api/competencia?productoId&todas=true` | Precios registrados (por omisión, solo vigentes) |
| GET | `/api/competencia/resumen?productoId` | Estadística, tu precio y posición por juguete |
| POST · PUT · DELETE | `/api/competencia[/{id}]` | Registrar, editar (o descartar con `vigente=false`) y eliminar |
| POST | `/api/competencia/{id}/imagen` | Foto del producto (multipart; reemplaza la anterior) |
| PUT | `/api/configuracion/comercial` | `{ diasSinMovimiento, bandaCompetenciaPct }` |
| GET | `/api/dashboard?desde&hasta&palletId&categoria&productoId&plataformaId` | Indicadores y datos de las gráficas |
| GET | `/api/reportes/{tipo}?…mismos filtros` | CSV: `ventas`, `productos`, `plataformas`, `categorias`, `pallets`, `inventario`, `competencia`, `liquidacion` |

`/api/respaldo` incluye la tabla `competencia`.

### 6.2 Decisiones técnicas

- El dashboard se calcula al consultarlo: con cientos o pocos miles de ventas responde al instante y nunca queda desfasado.
- CSV con BOM UTF-8, separador coma y punto decimal (formato de México), para que Excel muestre acentos y números correctamente.
- Las gráficas usan tokens de color propios (`--chart-pos`, `--chart-neg`) validados con un verificador de paletas (contraste y separación para daltonismo) en ambos modos; en modo oscuro se usan tonos distintos a los del texto.

## 7. Historias de usuario y criterios de aceptación

| Historia | Criterio de aceptación |
|---|---|
| Quiero saber si mi precio es competitivo | Con 4 precios de competencia veo mínimo, mediana, promedio y máximo, mi diferencia % y si estoy abajo, en mercado o arriba |
| Quiero identificar un producto sin código de barras | Registro marca, modelo, características y una foto |
| Quiero ver la salud del negocio de un vistazo | El Dashboard muestra inversión, ingresos, utilidad bruta y neta, margen, ROI, capital recuperado y pendiente, e indicadores de inventario |
| Quiero analizar un pallet, una categoría o un canal | Los filtros recalculan todos los indicadores y las gráficas |
| Quiero saber qué liquidar | La lista de candidatos muestra días sin venderse, costo, mínimo y mercado |
| Quiero trabajar los datos en Excel | Descargo cualquiera de los 8 reportes con los filtros aplicados |

## 8. Casos de prueba

Precios de competencia de **prueba**: 599 (549 + 50 de envío), 649, 699, 750 (700 + 50).

| # | Caso | Resultado esperado | Tipo |
|---|---|---|---|
| — | Estadística | Mínimo 599, máximo 750, promedio **674.25**, mediana **674** | Unitaria + integración |
| — | Posición (banda ±5 %) | $639 → **−5.19 %**, abajo del mercado, lugar 2 de 5; $659 → −2.23 %, en mercado; $710 → +5.34 %, arriba; con banda 10 %, $639 queda en mercado | Unitaria |
| — | Descartar un precio | Sin el de $750: mediana 649, $639 → −1.54 %, en mercado | Integración |
| — | Antigüedad | 2 pz de 17 días, 3 de 78, 1 de 140 → **68.0 días**; rangos 2 / 0 / 3 / 1 | Unitaria |
| — | Sin movimiento, rotación, ROI | 48 días sin venta; 8 desde la última venta; 1 de 3 = 33.33 %; 72.25 ÷ 631.80 = 11.44 % | Unitaria |
| — | Dashboard sin filtros | Pallets de $631.80 y $300; venta del caso 13 cobrada; gasto general $50 → inversión **$931.80**, ingresos $639, costo $315.90, gastos $250.85, bruta $323.10, comercial $72.25, neta **$22.25**, margen 11.31 %, ROI **2.39 %**, recuperado **$388.15 (41.66 %)**, pendiente $543.65 | Integración |
| — | Inventario | 2 juguetes, 5 recibidas, 4 disponibles, 1 vendida; muñeca sin vender 45 días → candidata; antigüedad **35.0 días** | Integración |
| — | Filtros | Pallet A: inversión $631.80, neta $72.25 (sin gastos generales), ROI 11.44 %, 61.44 % recuperado; categoría: inversión = costo asignado ($300); fecha futura: sin ingresos | Integración |
| — | Umbral configurable | Con 60 días la muñeca (45) deja de ser candidata; 0 días se rechaza | Integración |
| — | Validaciones de competencia | Precio 0, sin plataforma ni tienda o fecha futura → 400; foto no válida → 400 | Integración |
| — | Reportes | Los 8 CSV responden; el de ventas incluye V-00001 y su utilidad 72.25; tipo inexistente → 404 | Integración |

**Ejecución:** `dotnet test PalletControl.sln` → **80 de 80** (52 unitarias, 28 de integración).

### 8.1 Pruebas de navegador y migración (fuera del repositorio)
- **Flujo Fase 4:** 4 juguetes en 2 pallets, 4 ventas en 2 plataformas, gasto general y 5 precios de competencia → Dashboard (indicadores, 8 gráficas, tooltips con mouse y teclado, candidato a liquidación) → filtro por pallet (avisos de inversión y gastos) → umbral 90 y regreso a 30 → Competencia (lista, detalle, alta con foto, total en vivo) → ficha del juguete → 8 reportes (CSV, descarga) → modo oscuro → celular de 390 px sin desbordes. Sin errores en la consola.
- **Regresión:** recorridos completos de las fases 1, 2 y 3 sin errores.
- **Migración:** base creada con la versión de la Fase 3 (pallet, juguete, costeo y venta cobrada) → umbrales 30 días y ±5 %; ventas, cobros y costos intactos; el dashboard muestra 70 % recuperado.

---

## 9. Manual de usuario (Fase 4)

### 1. Dashboard
*Análisis comercial › Dashboard.* Arriba están los filtros (fechas, pallet, categoría, juguete, plataforma). Pasa el mouse (o el foco del teclado) sobre una barra para ver su detalle. **Umbrales** cambia los días sin movimiento y la banda de precio de mercado.

### 2. Competencia
*Análisis comercial › Competencia › + Precio de competencia.* Elige tu juguete, escribe el producto que encontraste, dónde (plataforma o tienda), su precio y su envío. Si no tiene código de barras, agrega marca, modelo, características y una foto. Cuando un precio ya no aplique, desmarca **Vigente**: se conserva en el historial y deja de contar.

### 3. Candidatos a liquidación
Al pie del Dashboard. Antes de rebajar, compara el costo por pieza y tu mínimo con la mediana del mercado.

### 4. Reportes
*Análisis comercial › Reportes.* Aplica filtros y descarga el CSV; se abre directo en Excel.

---

## 10. Pendientes y riesgos

| # | Tema | Detalle |
|---|---|---|
| 1 | Datos de competencia | Las conclusiones dependen de capturar precios recientes y comparables (misma condición y contenido). Descarta los que ya no apliquen. |
| 2 | Régimen fiscal | Sigue pendiente: la utilidad neta es antes de impuestos. |
| 3 | Gastos generales por filtro | No se reparten por pallet, categoría, juguete ni plataforma; con esos filtros la neta no los descuenta (se avisa). Si quieres repartirlos (p. ej. por ingresos), hay que definir la regla. |
| 4 | Ventas anteriores | Su costo es estimado y no tenían gastos (Fase 3): pueden inflar la utilidad histórica. |
| 5 | Comparación con el precio en México | El análisis de la Fase 2 sigue usando el precio en México como referencia; no se reemplazó por la mediana de la competencia para no cambiar resultados anteriores. Puede hacerse si lo decides. |
| 6 | Fase 5 | Búsqueda de precios y sincronización de publicaciones solo con APIs oficiales; requiere credenciales de cada plataforma. |
