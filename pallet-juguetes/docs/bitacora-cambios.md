# Bitácora de cambios

Formato: fecha · versión · cambios · impacto en datos.

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
