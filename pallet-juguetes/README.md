# Control Pallet de Juguetes

Aplicación local para administrar el negocio del pallet de juguetes: pallets y costo real de cada pieza, inventario por lotes, ventas, gastos, indicadores semanales y recuperación de la inversión.

> **Documentación del módulo de costos, precios y rentabilidad:** [`docs/`](docs/README.md) — diagnóstico (Fase 0), Fase 1 (pallets y costeo), Fase 2 (plataformas y precios), Fase 3 (ventas, cobros, devoluciones, publicaciones y rentabilidad real), Fase 4 (competencia, dashboard y reportes), Fase 5 (integraciones con Mercado Libre y Meta) y bitácora de cambios.

Es la versión instalable del artefacto **Control Pallet de Juguetes**. Usa la misma interfaz y los mismos cálculos, pero guarda los datos en una base SQLite en tu computadora. Está pensada para una sola persona; también puedes abrirla desde el celular en la misma red Wi‑Fi.

| | |
|---|---|
| Tecnología | C# · ASP.NET Core 8 (Minimal API) · Entity Framework Core · SQLite |
| Interfaz | HTML, CSS y JavaScript sin dependencias (servida por la misma aplicación) |
| Datos | Un archivo: `datos/pallet.db` |
| Puerto | `5080` |
| Acceso | Usuario y contraseña (el primero se crea en la misma computadora) |
| Pruebas | `dotnet test PalletControl.sln` (xUnit: unitarias e integración) |

---

## 1. Qué hace

| Sección | Funciones |
|---|---|
| **Inicio** | **Guía paso a paso** (qué sigue: registrar pallet, valor de mercado, juguetes, tarifas, costeo, publicar, vender y cobrar), **tu pallet en números** (juguetes y piezas, inversión, costo promedio por pieza, valor de mercado y avance de registro, recuperado, ganancia; filtro por pallet), **precio de cada juguete** (costo por pieza real o estimado, mínimo y sugerido; botón para usar el sugerido en los que no tienen precio), recuperación de la inversión con gráfica, pendientes y un glosario de términos |
| **Inventario** | **Tarjetas con foto** (o tabla) y **etiquetas de estado** por juguete (Disponible, Agotado, Apartado, En Facebook/WhatsApp, Sin publicar, Sin foto, Sin precio, Bajo el mínimo); clic en una etiqueta filtra. Resumen plegable **Valor del pallet** (piezas, valor en México, valor a precio objetivo, inversión y factor del mínimo proporcional). Clic en el nombre o la foto de un juguete abre su **detalle con carrusel de fotos**. Alta, edición y baja de juguetes con código, **código de barras**, clase A/B/C, estado, cantidad, **precio en México**, precios objetivo, mínimo y de lista, **características** e **imágenes**. Escaneo con lector USB/Bluetooth, filtros y búsqueda. Importación pegando filas desde Excel. |
| **Ventas** | Tablero Apartado → Pagado → Entregado (y Cancelado), vista de lista, filtro por canal, rendimiento por canal. |
| **Gastos** | Empaque, traslados, publicidad, comisiones e impuestos; totales por tipo. |
| **Semana** | Publicaciones, mensajes y horas dedicadas; ventas y dinero de la semana se calculan solos. |
| **Análisis comercial › Análisis de productos** | Mejor canal de cada juguete, precio sugerido, mínimo, utilidad y estado (rentable, margen bajo, pérdida), con filtros, orden y paginación. Detalle por canal y precios recomendados guardados. Detalle en [`docs/02-fase2-plataformas-y-precios.md`](docs/02-fase2-plataformas-y-precios.md). |
| **Análisis comercial › Simulador de precios** | Solo el juguete: escribe un precio y cuántas piezas, y te dice si **ganas o pierdes** y cuánto, con su costo, mínimo, sugerido y precio de equilibrio. Sin tarifas de plataforma, peso, categoría ni tipo de publicación. |
| **Análisis comercial › Márgenes** | Objetivos de rentabilidad (general, por categoría o por juguete): margen objetivo, margen mínimo y redondeo. El mínimo y el sugerido se calculan como venta directa (costo + margen), sin tarifas de plataformas y **sin sumar IVA** (lo que costó el pallet ya lo incluye). |
| **Análisis comercial › Pallets y costos** | Pallets (moneda, tipo de cambio, gastos adicionales, inversión total), juguetes por lote y condición, costeo **equitativo, proporcional o manual** con cuadre al centavo, simulación, historial de costeos y costo real por pieza. Detalle en [`docs/01-fase1-pallets-y-costeo.md`](docs/01-fase1-pallets-y-costeo.md). |
| **Ajustes** | Inversión (con pallets, es la suma de sus inversiones), aviso de pocas piezas, meta mensual. Exportación a CSV por hoja y respaldo completo en JSON. |

### Tipo de cambio del día

La barra superior muestra siempre la leyenda **"Tipo de cambio definido para el día de hoy a: $XX.XXXX"** con su origen.

- Si aún no hay valor para hoy, la aplicación lo consulta en la API pública de [dolardof.com](https://dolardof.com/api-docs) (`/api/v1/dof/today`), que da el tipo de cambio oficial publicado en el Diario Oficial de la Federación. En fin de semana o día inhábil es la última publicación del DOF; la leyenda muestra su fecha.
- Al hacer clic en la leyenda puedes **capturarlo a mano** (se usa ese valor durante el día) o **volver a consultar el DOF**.
- Si no hay internet o dolardof.com no responde, se conserva el último valor y la leyenda dice de qué día es; no se presenta como el de hoy.
- Se usa para convertir a pesos los precios en dólares de UPCitemdb.

### Valor del pallet

En Inventario, el recuadro **Valor del pallet** muestra:

| Dato | Cálculo |
|---|---|
| Piezas | Total del pallet y disponibles (sin vendidas ni apartadas) |
| Valor en México | Σ cantidad × precio en México (y lo mismo solo con las piezas disponibles) |
| Valor a precio objetivo | Σ cantidad × precio objetivo (y solo con las disponibles) |
| Inversión | La de Ajustes, y cuántas veces la cubre el valor en México |
| Factor proporcional | Inversión ÷ valor en México (indicador del pallet) |

Los juguetes sin precio en México o sin precio objetivo no suman y se indica cuántos son. Al pie de la tabla aparece además el valor de lo que se ve con los filtros aplicados.

### Reglas que valida el servidor

Estas reglas se revisan en el servidor (no solo en la pantalla), así se respetan aunque tengas la aplicación abierta en varias pestañas o en el celular:

- No se puede vender o apartar más piezas de las disponibles (por lote; sin lote elegido se toman las del pallet más antiguo).
- Una venta solo cuenta como cobrada cuando se registra el importe que llegó a la cuenta.
- No se puede ajustar ni reclasificar un lote por debajo de sus piezas vendidas.
- El código de juguete no se repite (sin distinguir mayúsculas).
- Un código de barras pertenece a un solo juguete; si aparece otra caja igual, se aumenta la cantidad.
- La cantidad de un juguete no puede quedar debajo de lo ya vendido o apartado.
- No se puede eliminar un juguete que tiene ventas registradas.
- La fecha de pago se asigna sola al pasar una venta a Pagado o Entregado.
- Una venta pagada, cobrada o con incidencias no se elimina: se cancela, para conservar el historial.

Si la tienes abierta en varias pestañas o dispositivos, los demás se actualizan solos en unos 4 segundos.

### Preparar publicación (formato, estilo creativo y textos)

En la ficha del juguete (o **Publicar** en el Inventario):

1. **Formato**: historia de WhatsApp (9:16, 1080 × 1920), publicación de Facebook (1:1, 1080 × 1080), grupos de Facebook (1:1, con disponibilidad y «Comenta LO QUIERO o envíame inbox»), Facebook Marketplace (1:1, título, precio, condición y descripción; descarga también las fotos originales) e historia de Instagram (9:16 con franjas libres arriba y abajo).
2. **Estilo creativo**, cada uno con su propia composición: **BOOM** (rayos, estallido y sello de precio), **OFERTA** (precio protagonista; si el juguete tiene «precio en México» mayor, muestra el precio de tiendas tachado y el % de ahorro), **DIVERTIDO** (confeti, foto en círculo, onda y burbuja), **PREMIUM** (fondo oscuro, dorado y tipografía con serifa), **SORPRESA** («¿Adivina qué llegó?», reflector y moño) y **MINIMALISTA** (fondo claro y foto grande). Cada estilo se ve en miniatura con la foto real.
3. **Textos listos para copiar**: 4 o 5 alternativas (curiosidad, descripción comercial, divertido, venta directa, llamado a la acción) adaptadas al tipo de juguete (por categoría y nombre: vehículos, muñecas, peluches, construcción, rompecabezas, juegos de mesa, figuras, bebé, aire libre, electrónicos, arte, educativos), al canal (largo, hashtags) y al estilo (emojis y tono). Solo usan datos capturados del juguete: no inventan características.

La imagen se dibuja en el navegador (sin internet ni servicios externos). Después usa **Ya lo publiqué: registrar** para que la aplicación controle las existencias (se agregó la plataforma «Facebook (perfil o página)» para las publicaciones normales).

### Código de barras

**Cómo se usa con el lector** (USB o Bluetooth en modo teclado):

1. Inventario → **Escanear código** → escaneen la caja.
2. Si el código ya está en el inventario, **se le suma una pieza** (al lote vendible del pallet más reciente, como movimiento «ajuste» en el kárdex) y la ventana queda lista para el siguiente escaneo; hay botones para **Deshacer** y para abrir el juguete.
3. Si no está, se consulta **UPCitemdb** y se abre el alta con nombre, categoría, **características** (descripción, marca, modelo, color, tamaño, dimensiones y peso, las que traiga), **imágenes** y precios sugeridos. Revisa, captura el **precio en México** y guarda.

Al guardar, las imágenes de UPCitemdb (hasta 3) se descargan y quedan en `datos/imagenes`, así se ven aunque la tienda de origen las quite. Algunas tiendas bloquean la descarga; en ese caso se avisa y puedes subir tu propia foto. En el formulario también puedes **subir fotos** (JPG, PNG, WEBP o GIF de hasta 5 MB, hasta 12 por juguete), marcar la principal (★) y borrar.

También se puede escanear o escribir el código en el campo *Código de barras* del formulario (Enter busca, no guarda), y buscarlo en el filtro del inventario.

**De dónde salen los datos:** primero del propio inventario; si no está, de [UPCitemdb](https://www.upcitemdb.com/) (servicio público, plan gratuito sin registro). Solo se consultan códigos EAN/UPC numéricos de 8 a 14 dígitos.

**Cómo se sugieren los precios** (son sugerencias, se pueden cambiar antes de guardar; el formulario muestra el cálculo):

| Campo | Regla |
|---|---|
| Precio en México | Promedio de los precios capturados en **Liverpool, Mercado Libre, Walmart, Juguetron, Juguetibici y Amazon México** (los que tengan valor). Sin precios por tienda, se puede capturar a mano |
| Precio objetivo | **Precio sugerido** del mejor canal (ver «Precio sugerido al escanear») |
| Precio mínimo | **Mínimo** del mejor canal: el precio que cubre el costo de la pieza, las comisiones y envíos de la plataforma y tu margen mínimo (10 %) |
| Precio de lista (referencia) | Precio más alto registrado en UPCitemdb (USD) × tipo de cambio del día. Se llena con el monto en dólares (selector **USD**) y se guarda convertido a pesos |

**Precio sugerido al escanear.** No necesitas saber en cuánto vender: al escanear el código de barras (o al capturar el precio en México, la cantidad o el pallet) la aplicación calcula y llena el precio objetivo y el mínimo, y explica cada paso:

1. **Referencia de mercado por pieza:** el precio en México si lo capturaste; si no, el promedio de los precios bajo y alto de UPCitemdb (tiendas de EE. UU.) × el tipo de cambio del día.
2. **Costo por pieza:** el costo real si el pallet ya tiene costeo; si no, un estimado con el método proporcional: inversión del pallet × referencia del juguete ÷ valor de mercado del pallet. Ese valor es el *valor de mercado estimado* que capturas en el pallet (manifiesto del proveedor); sin él, se usa la suma de los juguetes ya registrados, y el estimado baja conforme registras más.
3. **Motor de precios** con ese costo: mínimo (margen mínimo 10 %) y sugerido (objetivo 30 %, terminación 9) en el mejor canal con tarifa capturada. Sin tarifas, como venta directa sin comisiones.

Si el mínimo queda arriba de la referencia de mercado, la aplicación avisa que el juguete no recupera su costo a precio de mercado. Los precios llenados son sugerencias: puedes cambiarlos antes de guardar. El valor de referencia se guarda con el juguete y también se usa en el costeo proporcional del pallet cuando no hay precio en México.

**Moneda de los precios.** Todos los precios se guardan en **pesos mexicanos**. El precio de lista se puede capturar en dólares con el selector **MXN / USD**: se convierte con el tipo de cambio del día (redondeado al peso) y se guardan también el monto en dólares y el tipo de cambio usado, que aparecen en la ficha del juguete y en el CSV. Si después cambia el tipo de cambio, la referencia no se recalcula sola; se recalcula solo si cambias el monto en dólares. Sin tipo de cambio del día no se acepta un precio en dólares. El precio mínimo, el objetivo, el precio en México y los precios por tienda son siempre en pesos.

**Precios por tienda.** Cada tienda tiene un botón *Buscar ↗* que abre su búsqueda con el nombre del juguete en otra pestaña (Juguetibici no tiene tienda en línea propia verificada: su botón *En Google ↗* busca «Juguetibici + nombre» en Google); capturas el precio que ves. La aplicación no consulta esas tiendas por su cuenta: Liverpool y Walmart no ofrecen una API pública de precios, y la búsqueda de Mercado Libre por API exige registrar una aplicación con token (sin él responde 403).

**Precio mínimo.** Es el **mínimo del mejor canal**, el mismo criterio de la sugerencia al escanear: el precio que cubre el costo de la pieza (real si el pallet ya tiene costeo; si no, estimado con su valor de mercado), las comisiones y envíos de la plataforma y tu margen mínimo (10 %). Sin tarifas capturadas se calcula como venta directa sin comisiones.

- En Inventario → *Valor del pallet* → **Recalcular precios** se recalculan el **precio objetivo y el mínimo** de todos los juguetes con su costo actual y los márgenes vigentes: primero muestra la lista de cambios (antes → después, y si un anuncio publicado tiene otro precio) y luego se aplica. Reemplaza también los precios objetivo escritos a mano. En *Análisis comercial › Márgenes*, al guardar un objetivo se abre esta misma revisión, y el botón **Actualizar precios con estos márgenes** la abre cuando quieras.
- Si el precio objetivo queda por debajo del mínimo, el formulario lo advierte.
- El *factor proporcional* (inversión ÷ valor en México) se sigue mostrando como indicador del pallet y para las alertas de venta "por debajo de su parte de la inversión".

Solo se llenan los campos vacíos o los que llenó una sugerencia y no has editado; *Reemplazar todos los campos* o *Usar estos precios* sobrescriben.

**Características en español.** Al escanear, las características de UPCitemdb se traducen con la API gratuita de [MyMemory](https://mymemory.translated.net/) y se convierten unidades (libras → kg, onzas → g, pulgadas → cm). La traducción se guarda para no repetirla. También hay un botón *Traducir al español* en el formulario. Es traducción automática: revísala (por ejemplo, puede dejar palabras pegadas). Límite aproximado sin cuenta: 5,000 caracteres por día.

**Con el celular (sin lector):** en «Escanear código» o en el alta del juguete, toca **Cámara**: el celular abre su app de cámara, tomas la foto del código y la aplicación lo lee y lo busca. Lee EAN-13, EAN-8, UPC-A, UPC-E y Code 128. Funciona por la dirección de red local (sin HTTPS) y sin internet en el celular; en la computadora el mismo botón se llama «Desde foto». Usa el lector del navegador cuando existe (Chrome en Android) y, si no, la librería ZXing incluida en `wwwroot/vendor/zxing` (Apache-2.0).

**Si UPCitemdb no lo tiene:** se consulta en automático **Open Products Facts** (base abierta y gratuita de productos no alimenticios; tiene pocos juguetes y no trae precios). También se usa si ya se agotaron las 100 consultas diarias de UPCitemdb o no responde. Si ninguna de las dos lo tiene, aparecen botones para buscar el código en **Google** y **Amazon México** y copiar nombre y precio.

**Completar después:** si diste de alta un juguete a mano (por ejemplo, sin consultas ese día), abre su ficha y usa **Completar datos desde UPCitemdb**: consulta su código y llena **solo los campos vacíos** (categoría, marca, modelo, características, peso, medidas, referencia de mercado, precio de lista y fotos si no tiene). No cambia lo que ya capturaste. Gasta una consulta si el código no estaba guardado. Para varios a la vez: **Inventario › Completar datos (N)** recorre todos los juguetes con código y datos vacíos, muestra el avance y se detiene solo si se agotan las consultas del día.

**Limitaciones que hay que conocer:**

- El plan gratuito de UPCitemdb permite **100 consultas por día**. Cada código se guarda 30 días en la base local, así que volver a buscarlo no gasta consulta (`appsettings.json` → `CodigoBarras:DiasCache`).
- El catálogo y los precios de UPCitemdb son de tiendas de EE. UU.: algunos juguetes vendidos solo en México no aparecen y los precios no reflejan el mercado local. Compárenlos con Marketplace antes de publicar.
- Sin **tipo de cambio** (por ejemplo, sin internet el primer día) no se convierten los precios en dólares; el precio en México sigue funcionando.
- La computadora necesita internet para consultar códigos nuevos, descargar imágenes, traducir y el tipo de cambio; el resto de la aplicación funciona sin internet.

---

### Interfaz

Diseño con **Bootstrap 5.3.3** y **Bootstrap Icons 1.11.3** (incluidos en `wwwroot/vendor`, sin internet). Barra lateral con las secciones (contraíble con «Contraer»; en celular se abre con ☰), barra superior con migas de pan, y modo oscuro automático según el sistema. Los estilos propios están en `wwwroot/css/app.css` (módulos) y `wwwroot/css/tema.css` (sistema de diseño y layout).

## 2. Cómo ejecutarla

La aplicación corre en tu computadora y ahí se guardan los datos. Para abrirla desde el celular solo necesitas un navegador.

### Opción A — Ejecutable para Windows (no requiere instalar .NET en la PC principal)

En una computadora con el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0):

```bat
publicar-windows.bat
```

Copien la carpeta `publicado\win-x64` a la computadora principal y ejecuten `PalletControl.exe`. Los datos se crean en `publicado\win-x64\datos`.

### Opción B — Con el SDK de .NET 8

Windows: doble clic en `iniciar.bat`.

Linux o macOS:

```bash
./iniciar.sh
```

### Opción C — Docker

```bash
docker compose up -d --build
```

Los datos quedan en la carpeta `./datos` del proyecto.

**Primer usuario en Docker:** por seguridad, el primer usuario solo se crea desde la misma computadora, y dentro de Docker las peticiones no llegan como locales. En el primer arranque agrega la variable `Auth__PermitirConfiguracionRemota=true` (en `docker-compose.yml`, sección `environment`), crea tu usuario y **quítala** después.

### Sin inicio de sesión

La aplicación **no pide usuario ni contraseña**: al abrirla entra directo a Inicio, en la computadora o en el celular (decisión del usuario, 2026-11-20). Los cambios quedan en la bitácora a nombre de «local».

Para volver a pedir usuario y contraseña, en `appsettings.json` cambia `"Auth": { "Requerido": false }` a `true` y reinicia: la primera vez se crea el usuario desde la computadora donde corre (`http://localhost:5080`).

### Al arrancar

La consola muestra las direcciones:

```
  Control Pallet de Juguetes en ejecución
  En esta computadora:  http://localhost:5080
  Desde la red local:   http://192.168.x.x:5080
```

Para usarla desde el celular, abre la dirección **"Desde la red local"** estando en la misma red Wi‑Fi.

### Permitir el acceso en Windows

Si el celular no puede entrar, Windows Firewall está bloqueando el puerto. En una consola **como administrador**:

```bat
netsh advfirewall firewall add rule name="Control Pallet 5080" dir=in action=allow protocol=TCP localport=5080 profile=private
```

La regla se limita al perfil de red **privada**; la red de la casa debe estar marcada como privada en Windows.

---

## 3. Datos y respaldos

- Base de datos: `datos/pallet.db` (SQLite, un solo archivo).
- **Claves de cifrado:** `datos/claves` guarda las claves con las que se cifran las credenciales de las integraciones. Si mudas la aplicación a otra PC, copia toda la carpeta `datos` (incluida `claves`); sin ellas habrá que volver a capturar las credenciales.
- **Respaldo automático:** cada vez que la aplicación arranca se crea, si aún no existe, una copia del día en `datos/respaldos/pallet-AAAA-MM-DD.db`. Se conservan las últimas 14 (configurable en `appsettings.json` → `Respaldos:Conservar`).
- **Migración a un solo socio:** al abrir por primera vez esta versión con una base anterior, se guarda una copia `datos/respaldos/pallet-antes-de-un-socio-AAAA-MM-DD-HHMMSS.db`, se suman las horas de los dos socios en un solo campo y se eliminan nombres de socios, reparto, responsable de venta y quién pagó cada gasto.
- **Migración a la Fase 1 (pallets):** al abrir por primera vez esta versión con una base anterior, se guarda `datos/respaldos/pallet-antes-de-fase1-AAAA-MM-DD-HHMMSS.db` y se crea **"Pallet 1"** con la inversión de Ajustes; cada juguete queda en un lote con su condición (nuevo → Nuevo sellado, dañado → Dañado, incompleto → Incompleto). No se borra nada.
- **Imágenes:** se guardan como archivos en `datos/imagenes`. Los respaldos `.db` y el JSON **no incluyen las fotos**; respalda la carpeta `datos` completa para conservarlas.
- **Respaldo manual:** Ajustes → *Descargar respaldo* genera un JSON con todos los registros.
- **Exportar a Excel:** Ajustes → botones CSV por hoja.
- Para restaurar un respaldo `.db`: detengan la aplicación, reemplacen `datos/pallet.db` por la copia (renombrada a `pallet.db`) y borren `pallet.db-wal` y `pallet.db-shm` si existen.

Recomendación: copien periódicamente la carpeta `datos/respaldos` a una USB o a la nube; los respaldos automáticos están en el mismo disco que la base.

### Pasar los datos que ya capturaron en el artefacto

1. En el artefacto: Ajustes → Exportar **Inventario** (CSV).
2. Abran el CSV en Excel y copien las columnas en este orden: Código, Juguete, Categoría, Cantidad, Estado, Precio de lista (en pesos), Precio objetivo, Clase y, opcionales, Código de barras y Precio en México. (El CSV que exporta esta aplicación ya trae ese orden en sus primeras 10 columnas.)
3. En esta aplicación: Inventario → *Importar desde Excel* → pegar.

Ventas, gastos y semanas no tienen importación automática; se recapturan.

---

### Borrar todos los datos (empezar de cero)

**Ajustes › Borrar todos los datos…** quita los datos capturados (por ejemplo, los de prueba): pallets, juguetes con sus fotos, lotes y costeos, ventas, gastos, semanas, publicaciones, competencia, integraciones y la bitácora. Se conservan Ajustes, márgenes, plataformas, usuarios y la caché de códigos de barras. Pide escribir **BORRAR** y, antes de borrar, guarda `datos/respaldos/pallet-antes-de-borrar-AAAA-MM-DD-HHMMSS.db` y mueve las fotos a `datos/respaldos/imagenes-antes-de-borrar-…`. Si ya no las necesitas, borra esas copias a mano (los respaldos diarios automáticos anteriores también contienen los datos de prueba).

---

## 4. Seguridad: lo que hay que saber

- **Sin inicio de sesión (configuración actual).** Cualquier persona conectada al mismo Wi‑Fi que conozca la dirección puede abrir la aplicación, ver los datos financieros y modificarlos. Úsala solo en una red de confianza (no en Wi‑Fi público ni de invitados) y no compartas la dirección.
- **Inicio de sesión opcional** (`Auth:Requerido = true`): toda la API y las fotos requieren sesión. Contraseñas con PBKDF2-SHA256 (600,000 iteraciones); cookie HttpOnly y SameSite=Strict; bloqueo de 5 minutos tras 5 intentos fallidos; cambiar la contraseña cierra las demás sesiones.
- **Bitácora:** cada alta, cambio o baja queda en la tabla `auditoria` con usuario, fecha y valores antes y después.
- Funciona por HTTP (sin cifrado), adecuado solo para red local: no abran el puerto 5080 en el router hacia Internet.

---

## 5. Estructura del proyecto

```
pallet-juguetes/
├── src/PalletControl/
│   ├── Program.cs              Arranque, base de datos, respaldo diario
│   ├── appsettings.json        Puerto, ruta de la base, respaldos
│   ├── Models/Entidades.cs     Producto, Venta, Gasto, Semana, Configuración, caché de códigos
│   ├── Data/AppDbContext.cs    Mapeo EF Core → SQLite
│   ├── Data/Esquema.cs         Agrega columnas/tablas nuevas a bases existentes
│   ├── Servicios/BuscadorCodigoBarras.cs  Consulta UPCitemdb y calcula sugerencias
│   ├── Servicios/TipoCambioDof.cs         Tipo de cambio del día desde dolardof.com
│   ├── Servicios/AlmacenImagenes.cs       Guarda y valida las imágenes en datos/imagenes
│   ├── Servicios/Precios.cs               Promedio por tienda y precio mínimo proporcional
│   ├── Servicios/Traductor.cs             Traducción al español (MyMemory) y conversión de unidades
│   ├── Dominio/Calculo/Costeo.cs          Núcleo financiero puro: conversión, inversión y reparto al centavo
│   ├── Servicios/Inventario.cs            Lotes, kárdex, reclasificación y entrada del costeo
│   ├── Servicios/Contrasenas.cs           Hash de contraseñas (PBKDF2)
│   ├── Servicios/Unidades.cs              Libras/onzas → kg, pulgadas → cm
│   ├── Endpoints/Api.cs        API REST y reglas de negocio
│   ├── Endpoints/AuthApi.cs    Inicio de sesión y usuarios
│   ├── Endpoints/PalletsApi.cs Pallets, gastos, lotes y costeo
│   ├── Endpoints/PreciosApi.cs Plataformas, tarifas, objetivos, fiscal y rentabilidad
│   ├── Dominio/Calculo/MotorPrecios.cs    Motor de precios y selección de tarifa (puro)
│   ├── Servicios/Rentabilidad.cs          Costo real + tarifa + objetivo → precios por canal
│   ├── Dominio/Calculo/ResultadoVenta.cs  Resultado real de una venta, asignación de lotes y reparto (puro)
│   ├── Servicios/Ventas.cs                Ventas por lote, gastos, cobros, incidencias y publicaciones
│   ├── Endpoints/VentasApi.cs  Ventas, publicaciones y rentabilidad real
│   ├── Dominio/Calculo/Indicadores.cs     Estadística de precios, posición competitiva, antigüedad y rotación (puro)
│   ├── Servicios/Comercial.cs             Competencia e indicadores del dashboard
│   ├── Endpoints/ComercialApi.cs Competencia, dashboard y reportes CSV
│   ├── Dominio/Integracion/Planificador.cs Propuestas de sincronización (puro)
│   ├── Servicios/Integraciones/           Conectores (Mercado Libre, Meta), credenciales cifradas, lectura y aplicación confirmada
│   ├── Endpoints/IntegracionesApi.cs Cuentas conectadas, lecturas y propuestas
│   ├── Endpoints/Dtos.cs       Contratos de entrada
│   └── wwwroot/                Interfaz (index.html, css/app.css, js/app.js, js/analisis.js, js/precios.js, js/ventas.js, js/comercial.js, js/integraciones.js)
├── tests/PalletControl.Tests/  Pruebas unitarias (Unitarias/) y de integración (Integracion/)
├── docs/                       Documentación del módulo de costos y rentabilidad
├── PalletControl.sln
├── Dockerfile
├── docker-compose.yml
├── iniciar.bat / iniciar.sh
└── publicar-windows.bat
```

### API

| Método | Ruta | Uso |
|---|---|---|
| GET | `/api/estado` | Todos los datos (configuración, productos, ventas, gastos, semanas) |
| GET | `/api/version` | Contador de cambios (sincronización entre navegadores) |
| POST / PUT / DELETE | `/api/productos[/{id}]` | Juguetes |
| POST | `/api/productos/importar` | Alta o actualización masiva por código |
| GET | `/api/codigo-barras/{codigo}` | Busca en el inventario y, si no está, en UPCitemdb; regresa características, imágenes y precios sugeridos |
| PUT | `/api/tipo-cambio` | Captura manual del tipo de cambio de hoy `{ "valor": 18.71 }` |
| POST | `/api/tipo-cambio/consultar` | Vuelve a consultar el DOF en dolardof.com |
| POST | `/api/productos/recalcular-minimos` | Mínimo del mejor canal para todos los juguetes (mismo criterio que la sugerencia al escanear) |
| POST | `/api/productos/sugerir-precio` | Precio sugerido y mínimo de un juguete con su explicación |
| GET | `/api/productos/precios` | Costo por pieza, mínimo y sugerido de todos los juguetes |
| POST | `/api/productos/aplicar-sugeridos` | Pone el precio sugerido y el mínimo en los juguetes sin precio objetivo |
| POST | `/api/traducir` | Traduce texto del inglés al español `{ "texto": "..." }` |
| POST | `/api/productos/{id}/imagenes` | Sube fotos (multipart) |
| POST | `/api/productos/{id}/imagenes/upcitemdb` | Descarga las imágenes de UPCitemdb del código de barras del juguete |
| PUT / DELETE | `/api/imagenes/{id}[/principal]` | Marca como principal o borra una imagen |
| POST / PUT / DELETE | `/api/ventas[/{id}]` | Ventas (ver Fase 3 para los campos nuevos, cobro e incidencias) |
| PATCH | `/api/ventas/{id}/estado` | Avanzar a pagado o entregado (opcional: importe cobrado) |
| POST / PUT / DELETE | `/api/gastos[/{id}]` | Gastos |
| PUT / DELETE | `/api/semanas/{fecha}` | Indicadores semanales (la fecha se ajusta a su lunes) |
| PUT | `/api/configuracion` | Ajustes del negocio |
| GET | `/api/respaldo` | Descarga JSON completo |

Las rutas de pallets, lotes, costeo, usuarios y sesión están en [`docs/01-fase1-pallets-y-costeo.md`](docs/01-fase1-pallets-y-costeo.md#61-api-nueva-todas-requieren-sesión-salvo-apiauth); las de plataformas, tarifas y rentabilidad, en [`docs/02-fase2-plataformas-y-precios.md`](docs/02-fase2-plataformas-y-precios.md#61-api-todas-requieren-sesión); las de ventas, cobros, incidencias, publicaciones y rentabilidad real, en [`docs/03-fase3-ventas-y-rentabilidad-real.md`](docs/03-fase3-ventas-y-rentabilidad-real.md#61-api-todas-requieren-sesión); las de competencia, dashboard y reportes, en [`docs/04-fase4-inteligencia-comercial.md`](docs/04-fase4-inteligencia-comercial.md#61-api-todas-requieren-sesión); las de integraciones, en [`docs/05-fase5-integraciones.md`](docs/05-fase5-integraciones.md#61-api-todas-requieren-sesión-salvo-el-regreso-de-oauth). Todas las rutas requieren sesión, salvo `/api/auth/*`.

Los errores de validación regresan `400` o `409` con `{ "mensaje": "..." }` en español.

### Cambiar el puerto o la ruta de la base

En `appsettings.json` (`Urls` y `ConnectionStrings:Pallet`) o con variables de entorno:

```bash
ASPNETCORE_URLS=http://0.0.0.0:8080 ConnectionStrings__Pallet="Data Source=/ruta/pallet.db" ./iniciar.sh
```
