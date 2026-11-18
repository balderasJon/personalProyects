# Fase 0 — Diagnóstico técnico y propuesta de integración

**Módulo:** Gestión de Costos, Análisis de Precios y Rentabilidad Multicanal
**Aplicación:** Control Pallet de Juguetes (`pallet-juguetes/`)
**Fecha:** 18 de noviembre de 2026
**Estado:** Diagnóstico concluido y decisiones de la sección 11 tomadas. No se ha modificado código ni base de datos.

---

## 1. Resumen ejecutivo

La aplicación actual es una **aplicación web local monousuario** en **C# / ASP.NET Core 8 (Minimal API) con SQLite** y una interfaz **HTML + JavaScript sin framework**. Controla inventario, ventas, gastos e indicadores semanales de **un solo pallet** cuya inversión vive en la configuración general.

Para el módulo solicitado, la base es reutilizable: catálogo de productos, código de barras, imágenes, tipo de cambio DOF, precio en México y método proporcional ya existen. Las brechas principales son estructurales:

| # | Brecha | Impacto |
|---|---|---|
| 1 | **No existe la entidad Pallet.** La inversión es un único valor en `configuracion`. | Bloquea el costeo por pallet y la recuperación de capital por pallet. |
| 2 | **El inventario es por tipo de producto con un campo `Cantidad`**, sin unidades, lotes, condición por pieza ni movimientos. | Bloquea el kárdex, las devoluciones, el control de publicaciones múltiples y el costo histórico. |
| 3 | **Las ventas no guardan el costo del producto, las comisiones ni los gastos.** | No se puede calcular la utilidad real ni conservar el costo histórico. |
| 4 | **No hay autenticación, autorización ni auditoría.** | El requerimiento pide "reutilizar" la autenticación existente, pero no hay ninguna. |
| 5 | **No hay pruebas automatizadas** ni proyecto de pruebas. | El requerimiento exige pruebas unitarias, de integración y de regresión. |
| 6 | **SQLite no ordena ni suma montos `decimal` en SQL** (comprobado; ver 8.1). | Los cálculos financieros deben agregarse en C#, de forma controlada. |

La propuesta es **extender** la aplicación existente sin reemplazar componentes: nuevas tablas relacionadas con las actuales, migración no destructiva con respaldo previo y un núcleo de cálculo financiero puro (sin acceso a base de datos), probado con casos numéricos verificables.

Las cuatro decisiones del negocio necesarias para implementar ya están tomadas (sección 11).

---

## 2. Alcance y método de la inspección

- Se revisaron los 23 archivos del proyecto (3,066 líneas): código C#, interfaz, configuración, Docker y README.
- Se levantó la aplicación sobre una base vacía y se extrajo el **esquema real** de SQLite (`sqlite_master`), no el supuesto por el código.
- Se comprobó con un programa aislado el comportamiento de EF Core 8 + SQLite con montos `decimal`.
- Se comprobó que `/api/estado` responde **sin credenciales** (HTTP 200).
- **No se tuvo acceso a la base de datos real del usuario** (vive en su computadora). Los volúmenes reales de productos y ventas no se conocen.

---

## 3. Tecnologías

| Aspecto | Hallazgo |
|---|---|
| Lenguaje backend | C# 12, .NET 8 (`net8.0`, `RollForward=Major`: corre también en .NET 10) |
| Framework web | ASP.NET Core 8, **Minimal API** (sin controladores MVC) |
| ORM | Entity Framework Core 8.0.11 con proveedor SQLite |
| Base de datos | SQLite, archivo único `datos/pallet.db`, modo WAL |
| Creación del esquema | `EnsureCreated()` + clase propia `Data/Esquema.cs` que agrega columnas o tablas faltantes (**no usa migraciones de EF**) |
| Frontend | HTML5 + CSS + JavaScript ES5 sin framework ni compilación (`wwwroot/js/app.js`, 853 líneas, 76 funciones) |
| Gráficas | SVG generado a mano (`drawChart`) |
| Tipografías | Google Fonts (sin internet usa las del sistema) |
| Empaquetado | `dotnet run`, ejecutable autocontenido para Windows y Docker / Docker Compose |
| Pruebas | **No existen** |
| Control de versiones | Git, rama `claude/awesome-goldberg-27wexs` |

---

## 4. Arquitectura actual

```
Navegador (PC o celular en la misma red)
  │  HTTP, JSON          index.html + app.js (SPA de una sola página, pestañas)
  ▼
ASP.NET Core 8 — Program.cs
  ├─ Archivos estáticos: wwwroot/ y /imagenes (datos/imagenes)
  ├─ Endpoints/Api.cs ── API REST /api/* (26 rutas), reglas de negocio dentro de cada ruta
  │     └─ Candado global de escritura (SemaphoreSlim) para validar inventario sin carreras
  ├─ Servicios/
  │     ├─ BuscadorCodigoBarras  → UPCitemdb (caché local 30 días)
  │     ├─ TipoCambioDof         → dolardof.com (una vez al día)
  │     ├─ Traductor             → MyMemory (inglés→español + unidades métricas)
  │     ├─ AlmacenImagenes       → archivos en datos/imagenes (validación por contenido)
  │     └─ Precios               → promedio por tienda y factor proporcional
  └─ Data/AppDbContext + Esquema → SQLite (datos/pallet.db)
        └─ Respaldo diario VACUUM INTO datos/respaldos (14 días)
```

**Patrón de sincronización:** cada escritura incrementa `version_datos.Valor`; los navegadores consultan `/api/version` cada 4 segundos y, si cambió, recargan todo con `/api/estado`.

**Características relevantes:**
- Una sola instancia del servidor (el candado vive en memoria).
- Las reglas de negocio están dentro de los endpoints, sin capa de dominio separada. Solo `Servicios/Precios.cs` es lógica pura.
- `/api/estado` envía **todas** las tablas en cada recarga. Funciona con decenas o cientos de registros, pero no escala a miles.

---

## 5. Modelo de datos actual (esquema real)

### 5.1 Tablas

| Tabla | Llave | Propósito | Campos principales |
|---|---|---|---|
| `productos` | `Id` TEXT (12 hex) | Tipo de juguete del pallet | `Codigo` (único, NOCASE), `CodigoBarras` (único si no vacío), `Nombre`, `Categoria`, `Clase` A/B/C, `Estado` nuevo/danado/incompleto, `Cantidad` INT, `PrecioLista`, `PrecioObjetivo`, `PrecioMinimo`, `PrecioMexico`, `PrecioLiverpool`, `PrecioMercadoLibre`, `PrecioWalmart`, `Caracteristicas`, `Notas`, `CreadoUtc`, `ActualizadoUtc` |
| `producto_imagenes` | `Id` | Fotos por producto | `ProductoId` (FK, cascada), `Archivo`, `Origen` subida/upcitemdb, `Principal` |
| `ventas` | `Id` | Venta de N piezas de **un** producto | `ProductoId` (FK, restrict), `Cantidad`, `Precio` (unitario), `Estado` apartado/pagado/entregado/cancelado, `Fecha`, `FechaPago`, `Canal` (texto libre de una lista fija), `Cliente`, `Notas` |
| `gastos` | `Id` | Gastos operativos generales | `Tipo` (lista fija), `Monto`, `Fecha`, `Concepto` |
| `semanas` | `Inicio` (lunes) | Indicadores semanales manuales | `Publicadas`, `Mensajes`, `Horas`, `Notas` |
| `configuracion` | `Id`=1 | Parámetros globales | `Inversion` (**inversión del único pallet**), `StockBajo`, `MetaMensual`, `TipoCambioUsd`, `TipoCambioFecha`, `TipoCambioFuente`, `TipoCambioPublicado`, `DescuentoSugerido` |
| `consultas_codigo_barras` | `Codigo` | Caché de UPCitemdb | `Fuente`, `Encontrado`, `Respuesta` (JSON), `ConsultadoUtc` |
| `version_datos` | `Id`=1 | Contador de cambios | `Valor` |

### 5.2 Relaciones e índices

```
productos 1 ──< producto_imagenes   (ON DELETE CASCADE)
productos 1 ──< ventas              (ON DELETE RESTRICT)
gastos, semanas, configuracion, consultas_codigo_barras: sin relaciones
```

Índices: `IX_productos_Codigo` (único), `IX_productos_CodigoBarras` (único filtrado), `IX_ventas_ProductoId`, `IX_producto_imagenes_ProductoId`.

### 5.3 Tipos de datos

- Los montos son `decimal` en C# y **TEXT en SQLite** (ej. `"10.25"`). Se guardan **exactos**, sin error de punto flotante. Esto cumple el requisito de evitar flotantes, con la limitación de la sección 8.1.
- Los identificadores son TEXT (12 caracteres hexadecimales); las fechas son TEXT ISO (`DateOnly` y `DateTime` UTC).

### 5.4 Hallazgos del modelo

1. **Un solo pallet implícito.** Todo el costeo depende de `configuracion.Inversion`.
2. **`productos.Cantidad` mezcla catálogo e inventario.** El `Estado` (nuevo/dañado/incompleto) aplica a todas las piezas del tipo: no se puede tener 10 nuevas y 2 dañadas del mismo juguete.
3. **No hay movimientos de inventario.** El disponible se calcula como `Cantidad − Σ ventas no canceladas`.
4. **Las ventas no guardan costo, comisión, envío, impuestos ni importe cobrado.** Tampoco tienen folio.
5. **`Canal` es texto libre** tomado de una lista fija en código (`Catalogos.Canales`); no es un catálogo administrable.
6. **Sin historial.** Solo `ActualizadoUtc`; los cambios de precio o costo se sobrescriben.
7. **Marca y modelo no son campos.** Vienen de UPCitemdb pero se guardan dentro del texto de `Caracteristicas`.
8. `PrecioLiverpool`, `PrecioMercadoLibre` y `PrecioWalmart` son una forma básica de **investigación de competencia** (un precio por tienda, sin fecha, URL ni condición).

---

## 6. Funcionalidades existentes

| Módulo | Qué hace hoy |
|---|---|
| Resumen | Falta por recuperar, gráfica semanal acumulada, ventas cobradas, gastos, utilidad neta, % vendido, precio promedio, utilidad por hora, alertas |
| Inventario | Alta, edición, baja, importación desde Excel y exportación CSV; escaneo de código de barras (UPCitemdb); características traducidas; galería y carrusel de fotos; precios por tienda; **valor del pallet** (valor en México, a precio objetivo, factor proporcional); recálculo de mínimos |
| Ventas | Tablero apartado → pagado → entregado/cancelado, lista, filtro por canal, rendimiento por canal; valida existencias en servidor |
| Gastos | Registro por tipo con totales |
| Semana | Publicaciones, mensajes y horas |
| Ajustes | Inversión, aviso de pocas piezas, meta mensual, % de descuento sugerido, exportación y respaldo |
| Transversal | Tipo de cambio DOF diario (leyenda fija), respaldo diario, sincronización entre pestañas |

### 6.1 Registro actual de productos e inventario

1. Alta manual, importación pegando filas desde Excel o escaneo de código de barras.
2. El escaneo busca primero en el inventario; si no está, consulta UPCitemdb, traduce las características y propone precios.
3. Una fila de `productos` representa un tipo de juguete con su cantidad total recibida.
4. Las ventas descuentan del disponible calculado; el servidor impide vender más de lo disponible.

### 6.2 Costeo actual

Ya existe una versión del **método proporcional** (`Servicios/Precios.cs`):
`factor = inversión ÷ Σ(cantidad × precio en México)` y `mínimo = precio en México × factor`.

Limitaciones frente al requerimiento: un solo pallet; sin gastos adicionales del pallet; sin merma; sin métodos equitativo ni manual; sin control de redondeo para cuadrar al centavo; sin historial.

---

## 7. Componentes reutilizables

| Componente | Dónde | Uso en el nuevo módulo |
|---|---|---|
| `AppDbContext` + `Esquema.Actualizar` | `Data/` | Agregar tablas y columnas con el mismo mecanismo no destructivo y respaldo previo |
| Candado de escritura `Bloqueado()` | `Endpoints/Api.cs` | Ventas, publicaciones y movimientos de inventario sin sobreventa |
| `GuardarAsync()` + `version_datos` | `AppDbContext` | Sincronización de pantallas |
| Respuesta de error `{mensaje}` en español | `Endpoints/Dtos.cs` | Mismo contrato para el módulo nuevo |
| `TipoCambioDof` | `Servicios/` | Conversión USD→MXN al registrar pallets |
| `BuscadorCodigoBarras` | `Servicios/` | Llenar marca, modelo, peso y dimensiones estructurados |
| `Traductor` (incluye conversión a kg y cm) | `Servicios/` | Descripción comercial, peso y medidas |
| `AlmacenImagenes` + galería y carrusel | `Servicios/`, `app.js` | Fotos de producto y de competencia |
| `Precios` (factor proporcional) | `Servicios/` | Base del método proporcional, generalizado por pallet |
| UI: `openModal`, `fld`, `kpi`, `toast`, `api()`, `drawChart`, tablas y estilos | `app.js`, `app.css` | Misma identidad visual en las pantallas nuevas |
| Exportación CSV e importación Excel | `app.js` | Reportes y carga masiva |
| Respaldo diario y JSON | `Program.cs`, `Api.cs` | Incluir las tablas nuevas |

---

## 8. Riesgos técnicos

### 8.1 Montos `decimal` en SQLite (comprobado)

Resultado de la prueba aislada con EF Core 8.0.11:

| Operación | Resultado |
|---|---|
| Guardar `9.5`, `10.25`, `0.1` | Exacto (`"9.5"`, `"10.25"`, `"0.1"`) |
| `WHERE monto > 10` en SQL | Funciona |
| `ORDER BY monto` en SQL | **Error: no soportado** |
| `SUM(monto)` en SQL | **Error: no soportado** |
| `0.1 + 0.2 == 0.3` en C# `decimal` | Verdadero |

**Mitigación propuesta:** mantener `decimal` (consistente con lo existente y exacto). Los totales, ordenamientos y agregados se calculan en C# sobre conjuntos filtrados. El volumen esperado (cientos o pocos miles de piezas por pallet) lo permite. Alternativa descartada por ahora: guardar centavos como enteros, lo que obligaría a convertir todas las columnas existentes.

### 8.2 Otros riesgos

| Riesgo | Nivel | Mitigación |
|---|---|---|
| **Sin autenticación**, servidor escuchando en toda la red local (`0.0.0.0:5080`) | Alto | Decisión 11.2 |
| Granularidad del inventario (tipo con cantidad, sin unidades ni lotes) | Alto | Decisión 11.1 |
| Ventas existentes sin costo histórico | Medio | Al migrar, guardar el costo calculado en ese momento **marcado como "estimado en migración"**; las ventas nuevas guardan el costo real del momento |
| Reglas fiscales desconocidas (régimen, IVA, retenciones) | Alto para la Fase 3 | Decisión 11.3; motor fiscal configurable separado de los gastos comerciales |
| Tarifas de plataformas desconocidas | Medio | Catálogo sin valores precargados; captura manual con vigencias. **No se inventan tarifas.** |
| `app.js` monolítico (853 líneas) crecerá con 10 pantallas | Medio | Separar en archivos por sección (sin framework ni compilación) |
| `/api/estado` envía todo en cada cambio | Medio | El módulo nuevo usará endpoints paginados y filtrados |
| Sin pruebas automatizadas | Alto | Crear proyecto xUnit; núcleo de cálculo puro y probado antes de exponerlo en pantallas |
| Cambios de esquema sin migraciones de EF | Medio | Mantener `Esquema.Actualizar` (idempotente, con respaldo previo); cada cambio con prueba de migración desde la versión anterior |
| Una sola instancia (candado en memoria) | Bajo | Aceptable para uso local; documentado |
| Las fotos no van en los respaldos `.db` ni JSON | Bajo | Ya documentado; considerar un respaldo zip en una fase posterior |
| Integraciones externas no disponibles (precios en Liverpool o Walmart, búsqueda de Mercado Libre sin token) | Medio | Captura manual; interfaces preparadas para APIs oficiales (Fase 5). **Sin scraping.** |

---

## 9. Requerimiento contra situación actual

| Requerimiento | Situación | Acción |
|---|---|---|
| Pallets con moneda, tipo de cambio y gastos | No existe | Nueva entidad y pantalla |
| Costeo equitativo, proporcional y manual con historial | Parcial (proporcional sin historial, un solo pallet) | Motor de costeo nuevo; reutiliza la lógica proporcional |
| Merma (dañados e incompletos) | Parcial (estado por tipo) | Condición por lote o unidad (11.1); costo reconocido como merma |
| Catálogo comercial (SKU, marca, modelo, medidas, condición) | Parcial | Extender `productos`; `Codigo` actual como SKU |
| Plataformas y tarifas con vigencia | No existe (lista fija en código) | Catálogo y tarifas configurables |
| Motor de precios por canal | No existe | Núcleo de cálculo puro y simulador |
| Competencia | Básico (3 precios por tienda) | Nueva entidad; migrar los 3 precios como registros |
| Publicaciones multicanal sin doble venta | No existe | Nueva entidad con control de disponibles |
| Ventas con comisiones, gastos, devoluciones y costo histórico | Parcial (precio y estado) | Extender ventas; gastos por venta; devoluciones con movimientos |
| Dashboard con filtros | Parcial (resumen general) | Dashboard nuevo |
| Autenticación, autorización y auditoría | No existe | Decisión 11.2; bitácora de cambios |
| Pruebas unitarias, de integración y de regresión | No existe | Proyecto de pruebas |
| Documentación | Solo README | Carpeta `docs/` (este documento es el primero) |

---

## 10. Propuesta de integración

### 10.1 Principios

1. **Extender, no reemplazar.** Las pestañas actuales siguen funcionando y la nueva sección "Análisis comercial" se agrega a la navegación.
2. **Migración no destructiva.** Respaldo automático antes de cada cambio de esquema; sin borrar columnas ni datos. Las columnas que queden obsoletas se conservan y se documentan.
3. **Núcleo financiero puro.** Costeo, distribución con redondeo al centavo, precios y rentabilidad en clases sin acceso a base de datos, probadas con resultados numéricos exactos.
4. **Nada hardcodeado.** Tarifas, impuestos, márgenes y métodos se guardan como datos con vigencia.
5. **Historia inmutable.** Costos, tarifas y precios con versión y vigencia; cada venta guarda una copia de su costo y sus cargos al momento de registrarse.

### 10.2 Entidades conceptuales: existentes y nuevas

| Entidad del requerimiento | Decisión | Tabla propuesta |
|---|---|---|
| PALLET | **Nueva.** Se crea "Pallet 1" con la inversión actual y se le asignan todos los productos existentes. | `pallets` |
| PALLET_GASTO | Nueva | `pallet_gastos` |
| PRODUCTO | **Existe.** Se extiende: `PalletId`, `Marca`, `Modelo`, `Subcategoria`, `DescripcionComercial`, `Condicion`, `AccesoriosIncluidos`, `EdadRecomendada`, `PesoKg`, `LargoCm`, `AnchoCm`, `AltoCm`, `EstadoComercial`, `FechaIngreso`. `Codigo` se usa como SKU. | `productos` |
| INVENTARIO | Nueva; forma según la decisión 11.1 | `inventario_lotes` (propuesta) |
| MOVIMIENTO_INVENTARIO | Nueva (kárdex). Saldo inicial generado desde las cantidades actuales. | `movimientos_inventario` |
| METODO_COSTEO | Catálogo de 3 valores; el método se elige por pallet | columna en `pallets` y en cada corrida |
| COSTO_PRODUCTO | Nueva, con historial por corrida de distribución | `costeo_corridas` + `costos_producto` |
| PLATAFORMA | **Nueva.** Sustituye la lista fija `Catalogos.Canales`. Los canales ya usados en ventas se conservan como plataformas. | `plataformas` |
| TARIFA_PLATAFORMA | Nueva: reglas por tipo de publicación, modalidad logística, categoría y rangos de precio o peso, con vigencia | `tarifas_plataforma` |
| PARAMETRO_RENTABILIDAD | Nueva; prioridad producto > categoría > plataforma > global | `parametros_rentabilidad` |
| PRECIO_PRODUCTO | Nueva: precios calculados o autorizados por producto y plataforma, con historial | `precios_producto` |
| COMPETENCIA | Nueva; los 3 precios por tienda existentes se migran como registros | `competencia` |
| PUBLICACION | Nueva | `publicaciones` |
| VENTA | **Existe.** Se extiende como encabezado: folio, plataforma, importes, cobrado, costo histórico. | `ventas` |
| DETALLE_VENTA | Nueva; cada venta existente se migra a un renglón | `venta_detalle` |
| GASTO_VENTA | Nueva: comisión, envío, empaque, etc. por venta | `venta_gastos` |
| (devoluciones) | Nueva; genera movimiento de inventario y movimiento financiero | `devoluciones` |
| (auditoría) | Nueva; bitácora de altas, cambios y bajas en tablas financieras | `auditoria` |
| Gastos generales | **Existe.** Se conserva como gastos operativos; se agrega un vínculo opcional a pallet. | `gastos` |

### 10.3 Organización del código

```
src/PalletControl/
  Dominio/Calculo/        ← NUEVO: lógica financiera pura (sin base de datos)
      DistribucionCostos.cs   equitativa, proporcional y manual; redondeo por residuo mayor
      MotorPrecios.cs         equilibrio, mínimo, objetivo, sugerido y promocional
      MotorRentabilidad.cs    utilidad, margen y ROI por canal
      MotorFiscal.cs          IVA, ISR y retenciones, separados de los gastos comerciales
  Endpoints/              ← Api.cs se conserva; se agrega un archivo por módulo
  Servicios/              ← existentes + integraciones futuras (interfaces IProveedorTarifas, IProveedorCompetencia)
  wwwroot/js/             ← app.js se conserva; se agrega un archivo por sección del módulo nuevo
tests/PalletControl.Tests/  ← NUEVO: xUnit (unitarias del núcleo, integración de API y regresión)
docs/                       ← NUEVO: documentación del módulo
```

### 10.4 Reglas financieras clave (a validar en la Fase 1)

- **Costo distribuible del pallet** = costo convertido a MXN + gastos adicionales no incluidos en el precio de compra. El costo de importación **no se vuelve a sumar**.
- **Distribución con cuadre exacto:** la suma de los costos asignados más la merma es igual a la inversión total, **al centavo**. Las diferencias de redondeo se reparten con el método del residuo mayor y se registran.
- **Merma:** los productos no vendibles permanecen en inventario. Su costo se reconoce como merma, liquidación o recuperación parcial, según la regla elegida para el pallet.
- **Costo histórico:** cada venta guarda una copia del costo vigente; recalcular la distribución no altera ventas ya registradas.
- **Cobro real:** una venta solo cuenta como capital recuperado cuando se registra el cobro, no al registrarse.
- **Precio sugerido:** nunca menor al mínimo autorizado.

### 10.5 Plan por fases

| Fase | Contenido | Entregable | Criterio para avanzar |
|---|---|---|---|
| 0 | Diagnóstico (este documento) | Diagnóstico y propuesta | Decisiones de la sección 11 |
| 1 | Pallets, gastos de pallet, catálogo comercial ampliado, inventario y movimientos, costeo de 3 métodos con historial, migración del pallet actual | Costo real por producto | Pruebas 1–7, 18 y 20 en verde; cuadre al centavo; regresión de lo existente |
| 2 | Plataformas, tarifas con vigencia, parámetros de rentabilidad, motor de precios y simulador, rentabilidad en la ficha del producto | Precio recomendado por canal | Pruebas 8–11 y 17; nunca sugerir menos del mínimo |
| 3 | Ventas ampliadas, gastos por venta, cobros, devoluciones, cancelaciones, reembolsos, publicaciones con control de doble venta | Utilidad real por operación | Pruebas 12–16 y 19; inventario y ventas consistentes |
| 4 | Competencia, dashboard con filtros, reportes y gráficas | Análisis comercial integral | Indicadores reconciliados con los datos fuente |
| 5 | Interfaces para APIs oficiales (Mercado Libre, Amazon) y sincronización de publicaciones | Automatización progresiva | Limitaciones documentadas por plataforma |

En cada fase: pruebas, documentación (`docs/`), bitácora de cambios y verificación de que las funciones actuales siguen operando.

### 10.6 Dependencias

| Dependencia | Uso | Estado |
|---|---|---|
| xUnit + Microsoft.AspNetCore.Mvc.Testing (NuGet) | Pruebas unitarias e integración | Por agregar; NuGet accesible |
| UPCitemdb, dolardof.com, MyMemory | Ya integradas | Límites gratuitos documentados |
| Mercado Libre y Amazon (APIs oficiales) | Fase 5 | Requieren registro de aplicación y credenciales del vendedor |
| Liverpool y Walmart | Competencia | Sin API pública: captura manual con enlace |

---

## 11. Decisiones

Decisiones tomadas por el usuario el 18 de noviembre de 2026, después de revisar este diagnóstico:

| # | Tema | Decisión | Efecto en el diseño |
|---|---|---|---|
| 11.1 | Granularidad del inventario | **Por lotes** | Tabla `inventario_lotes` (producto × pallet × condición, con cantidad) y kárdex `movimientos_inventario`. La captura sigue siendo por cantidad. |
| 11.2 | Autenticación | **Inicio de sesión local** | Usuario y contraseña con hash, sesión por cookie, todos los endpoints `/api/*` protegidos y bitácora de cambios con el usuario. Se sigue pudiendo usar desde el celular en la red local. |
| 11.3 | Régimen fiscal | **Aún no definido** | Motor fiscal configurable con conceptos en cero y una advertencia visible en pantallas y reportes hasta que se capture el régimen. Se recomienda validarlo con un contador. No bloquea las fases 1 y 2. |
| 11.4 | Condición de los productos existentes | **nuevo → Nuevo sellado**, dañado → Dañado, incompleto → Incompleto | Conversión automática al migrar, corregible después lote por lote. |

### 11.5 Definiciones por defecto (pendientes de confirmar; se aplican si no se indica otra cosa)
- **Canales ya usados en ventas** ("Grupos de Facebook", "Conocidos", "Bazar o escuela", "Instagram"): se conservan como plataformas adicionales, sin reasignarlos.
- **Merma:** su costo se absorbe proporcionalmente en los productos vendibles del mismo pallet y además se reporta por separado como merma. Alternativa: tratarla como pérdida aparte.
- **Moneda del pallet actual:** se registra como MXN con la inversión que tengan capturada en Ajustes. No conozco su valor real; el predeterminado de la aplicación es $49,000.

---

## 12. Lo que no se hizo en esta fase

- No se modificó código, esquema ni datos.
- No se revisó la base de datos real del usuario.
- No se consultaron ni se supusieron tarifas de plataformas.
