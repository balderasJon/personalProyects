# Fase 5 — Automatización: integraciones con APIs oficiales

**Entregable:** capacidades de automatización progresiva.
**Fecha de cierre:** 18 de noviembre de 2026.
**Estado:** concluida para Mercado Libre y Meta. Amazon queda pendiente porque no se pudo leer su documentación oficial (ver la sección 10). 89 pruebas automatizadas en verde (9 nuevas); regresión de las fases 1 a 4 sin errores.

**Decisiones del usuario al iniciar la fase:**
- **Plataformas:** Mercado Libre, Amazon México y Facebook/WhatsApp.
- **Credenciales:** aún no hay credenciales de desarrollador.
- **Qué se sincroniza:** existencias y pausas, pedidos → ventas, precios, y cobros y comisiones.
- **Modo:** con botón y confirmación. **Nada cambia en una plataforma ni en la aplicación sin que lo confirmes.**

**Fuentes:** cada conector se programó solo con lo que dice la documentación oficial, consultada el 18-nov-2026:
- Mercado Libre: developers.mercadolibre.com.mx, páginas de autenticación, ítems y búsquedas, sincronizar y modificar publicaciones, órdenes, envíos y pagos.
- Meta: developers.facebook.com, páginas de Commerce Platform, items_batch, inventario y catálogos de WhatsApp.

Lo que no se pudo verificar no se programó.

---

## 1. Alcance

| Plataforma | Existencias y pausas | Precios | Pedidos → ventas | Cobros y comisiones |
|---|---|---|---|---|
| **Mercado Libre** | ✓ Pausar, reactivar y ajustar piezas | ✓ Enviar precio (con aviso si el anuncio tiene automatización de precios) | ✓ Pedidos pagados, con su comisión (`sale_fee`) y tu costo de envío | ✓ Comisión y envío reales; el **neto depositado** se propone como *estimado* (lo informa Mercado Pago, que no está integrado) |
| **Meta** (catálogo de Facebook, Instagram y WhatsApp) | ✓ Agotado/disponible y `quantity_to_sell_on_facebook` | — No se verificó cómo la API devuelve el precio | — El pago ocurre en tu sitio (offsite checkout); los pedidos de WhatsApp llegan por webhooks públicos | — No aplica |
| **Amazon México** | — | — | — | — Documentación no consultada (bloqueada); se trabaja a mano |
| **Facebook Marketplace (publicaciones personales)** | — No se encontró una API para desarrolladores; se administra a mano | | | |

Todo lo que no se automatiza ya tiene su alternativa manual en las fases 3 y 4: ventas, cobros, publicaciones con avisos de doble venta y competencia.

## 2. Requerimientos funcionales

| ID | Requerimiento |
|---|---|
| RF5-01 | Registrar cuentas de plataformas con su configuración y credenciales; las credenciales se guardan cifradas y nunca se muestran. |
| RF5-02 | Conectar Mercado Libre por OAuth 2.0 (Authorization Code, PKCE opcional), sea porque la Redirect URI llega a la aplicación o pegando la dirección de regreso. |
| RF5-03 | Renovar el token de acceso antes de que venza y guardar el nuevo refresh token (es de un solo uso). |
| RF5-04 | Listar los anuncios de la cuenta y vincularlos con juguetes (crea la publicación en la aplicación). |
| RF5-05 | **Leer plataforma:** comparar anuncios y pedidos con el inventario central y generar propuestas de cambio. |
| RF5-06 | Aplicar solo las propuestas marcadas y confirmadas; las demás quedan como *no aplicadas*. |
| RF5-07 | Registrar el resultado de cada propuesta (aplicada, error con motivo, bloqueada) y una bitácora técnica de llamadas sin secretos. |
| RF5-08 | Mostrar por plataforma qué se puede automatizar y por qué no lo demás. |

## 3. Requerimientos no funcionales

- **Seguridad de credenciales:**
  - Se cifran con ASP.NET Core Data Protection. Las claves viven en `datos/claves` (en Windows, además protegidas con DPAPI de la cuenta del usuario).
  - La API solo informa *qué* secretos están capturados, nunca su valor. No se incluyen en el respaldo JSON ni en la auditoría.
  - Solo se capturan desde la computadora principal (`localhost`), salvo `Auth:PermitirConfiguracionRemota`.
- **Protección del flujo OAuth:** se usa un `state` aleatorio de un solo uso, comparado en tiempo constante. La dirección de regreso es pública solo para recibir ese `state`; sin él no conecta nada.
- **Tolerancia a fallas:**
  - Ante un 429 se reintenta según `Retry-After`.
  - Los errores se muestran en español sin exponer datos sensibles.
  - Una propuesta que falla no afecta a las demás: se deshacen sus cambios locales a medias.
- **Consistencia:** las escrituras usan el candado global. La venta importada pasa por las mismas reglas de la Fase 3: lote, costo histórico, existencias y control de doble venta.
- **Sin scraping:** solo APIs oficiales. Las direcciones base son configurables únicamente para pruebas.

## 4. Reglas de negocio

1. **Vínculo:** una publicación de la aplicación queda vinculada al anuncio cuando cumple tres condiciones:
   - su plataforma es la de la cuenta;
   - tiene identificador externo (ID del anuncio de Mercado Libre o `retailer_id` del catálogo de Meta);
   - no está eliminada.
2. **Existencias:** la fuente de verdad es el inventario central (piezas vendibles disponibles).
   - Anuncio activo sin piezas → **pausar** (Meta: «agotado»).
   - Anuncio con más piezas que las disponibles → **ajustar** a las disponibles.
   - Anuncio pausado, publicado en la aplicación y con piezas → **reactivar**.
3. **Precio:** si el precio del anuncio difiere del registrado en la publicación de la aplicación, se propone enviar el de la aplicación. La decisión de precio es tuya.
4. **Pedidos:**
   - Solo se importan pedidos **pagados**.
   - Cada línea del pedido se convierte en una venta (clave `pedido/anuncio`) que nunca se importa dos veces.
   - La venta lleva la comisión y el costo de envío que informa la plataforma, como gastos que descuenta la plataforma.
   - Con varias piezas en una línea, la comisión queda como *estimada*, porque la documentación no aclara si es por pieza o por línea.
   - Si el anuncio no está vinculado, la propuesta queda **bloqueada** hasta vincularlo.
5. **Cancelaciones:** un pedido ya importado que aparece cancelado en la plataforma propone **cancelar la venta** (las piezas regresan al inventario).
6. **Cobro:** para una venta importada sin cobro se propone un **cobro estimado** = precio − comisión − envío. Solo debes confirmarlo cuando el dinero esté disponible. El neto exacto lo informa Mercado Pago, que no está integrado.
7. **Una lectura a la vez:** al leer de nuevo se descartan las lecturas pendientes anteriores. Una lectura aplicada o descartada no se vuelve a aplicar.
8. **Pedidos que se leen:** los creados desde 7 días antes de la última lectura (la primera vez, los últimos 30 días), más los ya importados aún vigentes, para detectar cancelaciones.

## 5. Modelo de datos

| Tabla nueva | Campos principales | Notas |
|---|---|---|
| `integracion_cuentas` | Id, Conector, PlataformaId (FK), Nombre, Estado, CuentaExterna, ConfigJson, SecretosCifrados, TokenExpiraUtc, OAuthState, UltimaLecturaUtc, UltimoError | Secretos y `state` excluidos de la auditoría |
| `sincronizacion_lotes` | Id, CuentaId (FK, cascada), Estado, Resumen, Usuario, CreadoUtc, AplicadoUtc | Una lectura |
| `sincronizacion_propuestas` | Id, LoteId (FK, cascada), Orden, Tipo, Destino, PublicacionId, VentaId, ReferenciaExterna, Descripcion, Antes, Despues, DatosJson, Estado, Resultado, AplicadaUtc | Estados: pendiente, aplicada, rechazada, error, bloqueada |
| `integracion_eventos` | Id, CuentaId, FechaUtc, Nivel, Operacion, Detalle, Usuario | Bitácora técnica sin secretos |

**Columnas nuevas:**
- `publicaciones`: CuentaIntegracionId, EstatusRemoto, ExistenciasRemotas, PrecioRemoto, SincronizadoUtc.
- `ventas`: PedidoExterno, CuentaIntegracionId.

## 6. Diseño técnico

- **Planificador puro** `Dominio/Integracion/Planificador.cs`: compara y propone, sin llamar a ninguna API.
- **Contrato de conector** `Servicios/Integraciones/Conector.cs` (`IConector`): cada plataforma declara sus capacidades, campos, OAuth, lectura, cambios y pedidos. Agregar una plataforma es agregar un conector.
- **Conectores:**
  - `ConectorMercadoLibre`: OAuth, `/users/me`, `/users/{id}/items/search`, `/items/bulk` (máx. 20; reemplaza a `/items?ids=` desde oct-2026), `PUT /items/{id}`, `/orders/search`, `/orders/{id}` y `/shipments/{id}/costs` con `x-format-new`.
  - `ConectorMeta`: `GET /{versión}/{catalog_id}/products` y `POST /{catalog_id}/items_batch` (UPDATE, `allow_upsert=false`).
- **Servicio** `ServicioIntegraciones`: cifrado de credenciales, renovación de token, lectura (genera el lote) y aplicación confirmada.
- **API** `Endpoints/IntegracionesApi.cs`.
- **Interfaz** `wwwroot/js/integraciones.js`: Análisis comercial › Integraciones.

### 6.1 API (todas requieren sesión, salvo el regreso de OAuth)

| Método | Ruta | Uso |
|---|---|---|
| GET | `/api/integraciones` | Conectores (capacidades, campos, documentación), cuentas sin secretos |
| POST · PUT · DELETE | `/api/integraciones/cuentas[/{id}]` | Alta, configuración (secreto vacío = conservar) y baja (conserva ventas y publicaciones) |
| POST | `/api/integraciones/cuentas/{id}/autorizar` | URL de autorización de Mercado Libre (con `state` y, si aplica, PKCE S256) |
| POST | `/api/integraciones/cuentas/{id}/codigo` | Completa la conexión pegando la dirección de regreso |
| GET | `/api/integraciones/mercadolibre/callback` | Regreso automático (si registras esta dirección) |
| POST | `/api/integraciones/cuentas/{id}/probar` | Verifica la conexión |
| GET · POST | `/api/integraciones/cuentas/{id}/anuncios` · `/vincular` | Anuncios de la cuenta y vínculo con un juguete |
| POST | `/api/integraciones/cuentas/{id}/leer` | Lee la plataforma y crea un lote de propuestas |
| GET | `/api/integraciones/cuentas/{id}/lotes` · `/eventos` | Historial y bitácora |
| GET · POST | `/api/integraciones/lotes/{id}` · `/aplicar` · `/descartar` | Revisar, aplicar las marcadas o descartar |

## 7. Historias de usuario y criterios de aceptación

| Historia | Criterio de aceptación |
|---|---|
| Vendí en Facebook la última pieza que también está en Mercado Libre | Al leer Mercado Libre se propone pausar el anuncio; al confirmarlo se pausa por API y la publicación local queda pausada |
| Quiero que mis ventas de Mercado Libre entren solas | Al leer se propone registrar cada pedido pagado con su comisión y envío reales; al confirmarlo se crea la venta con costo histórico |
| No quiero cambios sorpresa | Nada se aplica sin marcarlo y confirmarlo; lo no marcado queda como «no aplicado» |
| Quiero saber qué pasó | Cada propuesta muestra su resultado y la bitácora registra cada llamada |
| Mis credenciales deben estar seguras | Se guardan cifradas, nunca se muestran ni se respaldan en JSON y solo se capturan desde la PC principal |

## 8. Casos de prueba

Plataformas **simuladas** con las formas de respuesta de la documentación oficial. No se llama a internet.

| # | Caso | Resultado esperado | Tipo |
|---|---|---|---|
| — | Sin piezas | Anuncio activo sin piezas → pausar; si el conector no puede pausar → existencias 0 | Unitaria |
| — | Diferencias | 5 pz anunciadas con 2 disponibles → ajustar a 2; $639 vs $649 → enviar $649; pausado con piezas → reactivar; sin capacidad de precio no se propone | Unitaria |
| — | Bloqueos | Anuncio con variaciones o no encontrado → revisar a mano; pedido sin vínculo → bloqueado | Unitaria |
| — | Pedidos | Pagado → nueva venta con comisión 95.85 y envío 120; pendiente no se importa; importado sin cobro → cobro estimado **$423.15**; cancelado → cancelar venta | Unitaria |
| — | OAuth Mercado Libre | URL oficial con `state`; `state` ajeno rechazado; pegar dirección de regreso conecta (cuenta 999); token que vence se renueva con el último refresh token | Integración |
| — | Aplicar solo lo confirmado | Se marcan existencias y venta: un solo `PUT /items/MLM1 {"available_quantity":2}`; el precio queda «no aplicado»; la venta lleva gastos reales y utilidad **$107.25** (639 − 95.85 − 120 − 315.90), sin cobro | Integración |
| — | Segunda lectura | No duplica el pedido; cobro estimado $423.15 aplicado | Integración |
| — | Precio con automatización | Respuesta 200 con aviso → propuesta en error con el motivo; un lote aplicado no se reaplica (409) | Integración |
| — | Meta | Token cifrado (no aparece en la API ni en el respaldo); vincular R-1; 3 pz anunciadas con 1 disponible → `items_batch` UPDATE `{"id":"R-1","quantity_to_sell_on_facebook":1}`, `allow_upsert=false` | Integración |
| — | Regreso OAuth anónimo | `state` inventado → redirige con error; el resto de la API sigue exigiendo sesión | Integración |

**Ejecución:** `dotnet test PalletControl.sln` → **89 de 89** (58 unitarias, 31 de integración).

### 8.1 Pruebas de navegador y migración (fuera del repositorio)

**Flujo Fase 5,** contra un servidor simulado local de Mercado Libre:
1. Capacidades por plataforma (Amazon «No disponible» con su motivo).
2. Alta de cuenta y conexión pegando la dirección de regreso.
3. Anuncios de la cuenta y vínculo con un juguete.
4. Leer plataforma: 3 propuestas.
5. Se marcan 2 y se confirman; solo esas 2 se aplican, y en la plataforma ocurre solo el `PUT` de existencias.
6. La venta V-00001 aparece con utilidad de $107.
7. Bitácora con 7 llamadas.
8. Celular de 390 px sin desbordes. Sin errores en la consola.

**Regresión:** recorridos completos de las fases 1 a 4 sin errores.

**Migración:** base creada con la versión de la Fase 4 (pallet, juguete, publicación con ID externo y venta). Se conservan publicación, venta y folio; las tablas nuevas quedan vacías; se crean las claves de cifrado.

---

## 9. Manual de usuario (Fase 5)

### 1. Prepara tu aplicación de desarrollador
- **Mercado Libre:**
  1. En developers.mercadolibre.com.mx crea una aplicación. Mercado Libre analiza la solicitud.
  2. Anota el **Client ID**, el **Client Secret** y la **Redirect URI**.
  3. Si registras como Redirect URI la dirección que muestra la aplicación, la conexión termina sola. Si Mercado Libre exige https, usa cualquier dirección tuya y pega aquí la dirección de regreso.
- **Meta:** necesitas un catálogo en Commerce Manager, su **ID** y un **token de acceso** con permiso para administrar el catálogo.

### 2. Registra la cuenta
*Análisis comercial › Integraciones › + Cuenta*, **desde la computadora principal** (http://localhost). Elige la plataforma del catálogo a la que se asignan las ventas (por ejemplo, Mercado Libre Clásica, con su tarifa).

### 3. Conecta (solo Mercado Libre)
**Conectar** › *Autorizar en Mercado Libre* con la **cuenta principal**, no un colaborador. Si no regresas solo a la aplicación, copia la dirección completa de la página de regreso (contiene `?code=…&state=…`) y pégala.

### 4. Vincula tus anuncios
**Anuncios** › elige el juguete de cada anuncio › **Vincular**. Revisa después el precio en *Publicaciones*.

### 5. Lee y confirma
**Leer plataforma** › revisa cada propuesta (dónde se aplica, antes y después) › marca las que quieras › **Aplicar seleccionadas** › confirma. Hazlo cada vez que vendas en otro canal o al empezar el día.

---

## 10. Pendientes y riesgos

| # | Tema | Detalle |
|---|---|---|
| 1 | **Amazon México** | Su documentación ahora se publica en `developer-docs.amazon`, y la consulta a ese dominio no se permitió en esta sesión. No se programó ninguna llamada sin verificarla. Para hacerlo: autorizar ese dominio de documentación (o compartir la documentación) y tener la cuenta de vendedor y el registro de desarrollador. |
| 2 | Pruebas reales | Todo se probó con plataformas simuladas. Al tener credenciales, conviene una primera lectura con un anuncio de prueba (Mercado Libre ofrece usuarios de test). |
| 3 | Redirect URI | Mercado Libre exige que coincida exactamente con la registrada. No se verificó si acepta `http://` de red local; por eso existe la opción de pegar la dirección de regreso. |
| 4 | Neto depositado | Lo informa Mercado Pago (otra API). El cobro se propone como estimado (precio − comisión − envío) y lo confirmas contra tu estado de cuenta. |
| 5 | Comisión con varias piezas | La documentación no aclara si `sale_fee` es por pieza o por línea: con más de una pieza el gasto queda como estimado para revisarlo. |
| 6 | Variaciones y pedidos especiales | Los anuncios con variaciones no se ajustan automáticamente. Los pedidos *partially_refunded* o *pending_cancel* se revisan a mano. |
| 7 | Precio en Meta | No se verificó el formato de lectura del precio: no se compara ni se envía automáticamente. |
| 8 | Notificaciones automáticas (webhooks) | Requieren una dirección pública con HTTPS. La aplicación es local, así que la sincronización es por lectura con botón, como pediste. |
| 9 | Inicio de sesión | Al instalar esta versión cambia el almacén de claves de cifrado. Es posible que haya que volver a iniciar sesión una vez. |
| 10 | Respaldo de claves | Si copias la aplicación a otra PC, copia también `datos/claves`. Sin esas claves, las credenciales guardadas no se pueden leer y hay que capturarlas de nuevo. |
