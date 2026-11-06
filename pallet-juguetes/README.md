# Control Pallet de Juguetes

Aplicación local para administrar el negocio del pallet de juguetes: inventario, ventas, gastos, indicadores semanales y recuperación de la inversión.

Es la versión instalable del artefacto **Control Pallet de Juguetes**. Usa la misma interfaz y los mismos cálculos, pero guarda los datos en una base SQLite en tu computadora. Está pensada para una sola persona; también puedes abrirla desde el celular en la misma red Wi‑Fi.

| | |
|---|---|
| Tecnología | C# · ASP.NET Core 8 (Minimal API) · Entity Framework Core · SQLite |
| Interfaz | HTML, CSS y JavaScript sin dependencias (servida por la misma aplicación) |
| Datos | Un archivo: `datos/pallet.db` |
| Puerto | `5080` |

---

## 1. Qué hace

| Sección | Funciones |
|---|---|
| **Resumen** | Cuánto falta por recuperar de la inversión, gráfica semanal acumulada, ventas cobradas, gastos, utilidad neta, % vendido, precio promedio real, valor del inventario y utilidad por hora. Lista de pendientes (apartados sin cobrar, piezas dañadas, ventas debajo del costo, semana sin registrar). |
| **Inventario** | Resumen **Valor del pallet** (piezas, valor en México, valor a precio objetivo, inversión y factor del mínimo proporcional). Clic en el nombre o la foto de un juguete abre su **detalle con carrusel de fotos**. Alta, edición y baja de juguetes con código, **código de barras**, clase A/B/C, estado, cantidad, **precio en México**, precios objetivo, mínimo y de lista, **características** e **imágenes**. Escaneo con lector USB/Bluetooth, filtros y búsqueda. Importación pegando filas desde Excel. |
| **Ventas** | Tablero Apartado → Pagado → Entregado (y Cancelado), vista de lista, filtro por canal, rendimiento por canal. |
| **Gastos** | Empaque, traslados, publicidad, comisiones e impuestos; totales por tipo. |
| **Semana** | Publicaciones, mensajes y horas dedicadas; ventas y dinero de la semana se calculan solos. |
| **Ajustes** | Inversión, aviso de pocas piezas, meta mensual, % de descuento sobre el precio en México para sugerir el objetivo. Exportación a CSV por hoja y respaldo completo en JSON. |

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
| Factor proporcional | Inversión ÷ valor en México; con el botón *Recalcular precios mínimos* |

Los juguetes sin precio en México o sin precio objetivo no suman y se indica cuántos son. Al pie de la tabla aparece además el valor de lo que se ve con los filtros aplicados.

### Reglas que valida el servidor

Estas reglas se revisan en el servidor (no solo en la pantalla), así se respetan aunque tengas la aplicación abierta en varias pestañas o en el celular:

- No se puede vender o apartar más piezas de las disponibles.
- El código de juguete no se repite (sin distinguir mayúsculas).
- Un código de barras pertenece a un solo juguete; si aparece otra caja igual, se aumenta la cantidad.
- La cantidad de un juguete no puede quedar debajo de lo ya vendido o apartado.
- No se puede eliminar un juguete que tiene ventas registradas.
- La fecha de pago se asigna sola al pasar una venta a Pagado o Entregado.

Si la tienes abierta en varias pestañas o dispositivos, los demás se actualizan solos en unos 4 segundos.

### Código de barras

**Cómo se usa con el lector** (USB o Bluetooth en modo teclado):

1. Inventario → **Escanear código** → escaneen la caja.
2. Si el código ya está en el inventario, se abre ese juguete para aumentar la cantidad.
3. Si no está, se consulta **UPCitemdb** y se abre el alta con nombre, categoría, **características** (descripción, marca, modelo, color, tamaño, dimensiones y peso, las que traiga), **imágenes** y precios sugeridos. Revisa, captura el **precio en México** y guarda.

Al guardar, las imágenes de UPCitemdb (hasta 3) se descargan y quedan en `datos/imagenes`, así se ven aunque la tienda de origen las quite. Algunas tiendas bloquean la descarga; en ese caso se avisa y puedes subir tu propia foto. En el formulario también puedes **subir fotos** (JPG, PNG, WEBP o GIF de hasta 5 MB, hasta 12 por juguete), marcar la principal (★) y borrar.

También se puede escanear o escribir el código en el campo *Código de barras* del formulario (Enter busca, no guarda), y buscarlo en el filtro del inventario.

**De dónde salen los datos:** primero del propio inventario; si no está, de [UPCitemdb](https://www.upcitemdb.com/) (servicio público, plan gratuito sin registro). Solo se consultan códigos EAN/UPC numéricos de 8 a 14 dígitos.

**Cómo se sugieren los precios** (son sugerencias, se pueden cambiar antes de guardar; el formulario muestra el cálculo):

| Campo | Regla |
|---|---|
| Precio en México | Promedio de los precios capturados en **Liverpool, Mercado Libre y Walmart** (los que tengan valor). Sin precios por tienda, se puede capturar a mano |
| Precio objetivo | Precio en México − % de descuento de Ajustes (35% de inicio, el ejemplo del plan). Sin precio en México: precio más bajo de UPCitemdb (USD) × tipo de cambio |
| Precio mínimo | **Método proporcional** (ver abajo). Requiere el precio en México |
| Precio de lista (referencia) | Precio más alto registrado en UPCitemdb (USD) × tipo de cambio del día |

**Precios por tienda.** Cada tienda tiene un botón *Buscar ↗* que abre su búsqueda con el nombre del juguete en otra pestaña; capturas el precio que ves. La aplicación no consulta esas tiendas por su cuenta: Liverpool y Walmart no ofrecen una API pública de precios, y la búsqueda de Mercado Libre por API exige registrar una aplicación con token (sin él responde 403).

**Precio mínimo proporcional.** La inversión se reparte entre los juguetes según su valor en México:

```
factor = inversión ÷ Σ (cantidad × precio en México)
precio mínimo de un juguete = su precio en México × factor   (redondeado a pesos)
```

Así cada juguete carga la parte de la inversión que le corresponde por su valor: uno de $1,000 en México carga 10 veces más que uno de $100. Si el factor es mayor que 1, el valor del pallet en tiendas mexicanas no alcanza a cubrir la inversión (la pantalla lo indica).

- Los juguetes sin precio en México no entran al cálculo ni reciben mínimo; mientras falten, el factor sale más alto de lo real.
- El factor cambia cada vez que capturas precios o cantidades. En Inventario → *Valor del pallet* → **Recalcular precios mínimos** se actualizan todos los mínimos (reemplaza los capturados a mano en juguetes con precio en México; pide confirmación).
- Si el precio objetivo queda por debajo del mínimo, el formulario lo advierte.
- Las alertas de venta "por debajo de su parte de la inversión" usan este mismo cálculo.

Solo se llenan los campos vacíos o los que llenó una sugerencia y no has editado; *Reemplazar todos los campos* o *Usar estos precios* sobrescriben.

**Características en español.** Al escanear, las características de UPCitemdb se traducen con la API gratuita de [MyMemory](https://mymemory.translated.net/) y se convierten unidades (libras → kg, onzas → g, pulgadas → cm). La traducción se guarda para no repetirla. También hay un botón *Traducir al español* en el formulario. Es traducción automática: revísala (por ejemplo, puede dejar palabras pegadas). Límite aproximado sin cuenta: 5,000 caracteres por día.

**Limitaciones que hay que conocer:**

- El plan gratuito de UPCitemdb permite **100 consultas por día**. Cada código se guarda 30 días en la base local, así que volver a buscarlo no gasta consulta (`appsettings.json` → `CodigoBarras:DiasCache`).
- El catálogo y los precios de UPCitemdb son de tiendas de EE. UU.: algunos juguetes vendidos solo en México no aparecen y los precios no reflejan el mercado local. Compárenlos con Marketplace antes de publicar.
- Sin **tipo de cambio** (por ejemplo, sin internet el primer día) no se convierten los precios en dólares; el precio en México sigue funcionando.
- La computadora necesita internet para consultar códigos nuevos, descargar imágenes, traducir y el tipo de cambio; el resto de la aplicación funciona sin internet.

---

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
- **Respaldo automático:** cada vez que la aplicación arranca se crea, si aún no existe, una copia del día en `datos/respaldos/pallet-AAAA-MM-DD.db`. Se conservan las últimas 14 (configurable en `appsettings.json` → `Respaldos:Conservar`).
- **Migración a un solo socio:** al abrir por primera vez esta versión con una base anterior, se guarda una copia `datos/respaldos/pallet-antes-de-un-socio-AAAA-MM-DD-HHMMSS.db`, se suman las horas de los dos socios en un solo campo y se eliminan nombres de socios, reparto, responsable de venta y quién pagó cada gasto.
- **Imágenes:** se guardan como archivos en `datos/imagenes`. Los respaldos `.db` y el JSON **no incluyen las fotos**; respalda la carpeta `datos` completa para conservarlas.
- **Respaldo manual:** Ajustes → *Descargar respaldo* genera un JSON con todos los registros.
- **Exportar a Excel:** Ajustes → botones CSV por hoja.
- Para restaurar un respaldo `.db`: detengan la aplicación, reemplacen `datos/pallet.db` por la copia (renombrada a `pallet.db`) y borren `pallet.db-wal` y `pallet.db-shm` si existen.

Recomendación: copien periódicamente la carpeta `datos/respaldos` a una USB o a la nube; los respaldos automáticos están en el mismo disco que la base.

### Pasar los datos que ya capturaron en el artefacto

1. En el artefacto: Ajustes → Exportar **Inventario** (CSV).
2. Abran el CSV en Excel y copien las columnas en este orden: Código, Juguete, Categoría, Cantidad, Estado, Precio de lista, Precio objetivo, Clase y, opcionales, Código de barras y Precio en México. (El CSV que exporta esta aplicación ya trae ese orden en sus primeras 10 columnas.)
3. En esta aplicación: Inventario → *Importar desde Excel* → pegar.

Ventas, gastos y semanas no tienen importación automática; se recapturan.

---

## 4. Seguridad: lo que hay que saber

- **No tiene usuario ni contraseña.** Cualquier persona conectada a la misma red que conozca la dirección puede ver y modificar los datos. Úsenla solo en la red de la casa, no en redes públicas, y no abran el puerto 5080 en el router hacia Internet.
- Funciona por HTTP (sin cifrado), adecuado solo para red local.

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
│   ├── Endpoints/Api.cs        API REST y reglas de negocio
│   ├── Endpoints/Dtos.cs       Contratos de entrada
│   └── wwwroot/                Interfaz (index.html, css/app.css, js/app.js)
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
| POST | `/api/productos/recalcular-minimos` | Aplica el método proporcional a todos los juguetes con precio en México |
| POST | `/api/traducir` | Traduce texto del inglés al español `{ "texto": "..." }` |
| POST | `/api/productos/{id}/imagenes` | Sube fotos (multipart) |
| POST | `/api/productos/{id}/imagenes/upcitemdb` | Descarga las imágenes de UPCitemdb del código de barras del juguete |
| PUT / DELETE | `/api/imagenes/{id}[/principal]` | Marca como principal o borra una imagen |
| POST / PUT / DELETE | `/api/ventas[/{id}]` | Ventas |
| PATCH | `/api/ventas/{id}/estado` | Avanzar a pagado o entregado |
| POST / PUT / DELETE | `/api/gastos[/{id}]` | Gastos |
| PUT / DELETE | `/api/semanas/{fecha}` | Indicadores semanales (la fecha se ajusta a su lunes) |
| PUT | `/api/configuracion` | Ajustes del negocio |
| GET | `/api/respaldo` | Descarga JSON completo |

Los errores de validación regresan `400` o `409` con `{ "mensaje": "..." }` en español.

### Cambiar el puerto o la ruta de la base

En `appsettings.json` (`Urls` y `ConnectionStrings:Pallet`) o con variables de entorno:

```bash
ASPNETCORE_URLS=http://0.0.0.0:8080 ConnectionStrings__Pallet="Data Source=/ruta/pallet.db" ./iniciar.sh
```
