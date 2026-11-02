# Control Pallet de Juguetes

Aplicación local para administrar el negocio del pallet de juguetes: inventario, ventas, gastos, indicadores semanales y recuperación de la inversión.

Es la versión instalable del artefacto **Control Pallet de Juguetes**. Usa la misma interfaz y los mismos cálculos, pero guarda los datos en una base SQLite en una computadora de la casa. Los dos socios la usan desde el navegador en la misma red Wi‑Fi.

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
| **Resumen** | Cuánto falta por recuperar de la inversión, gráfica semanal acumulada, ventas cobradas, gastos, utilidad neta, reparto por socio, % vendido, precio promedio real, valor del inventario y utilidad por hora. Lista de pendientes (apartados sin cobrar, piezas dañadas, ventas debajo del costo, semana sin registrar). |
| **Inventario** | Alta, edición y baja de juguetes con código, clase A/B/C, estado, cantidad y precios de lista, objetivo y mínimo. Filtros y búsqueda. Importación pegando filas desde Excel. |
| **Ventas** | Tablero Apartado → Pagado → Entregado (y Cancelado), vista de lista, filtros por canal y responsable, rendimiento por canal. |
| **Gastos** | Empaque, traslados, publicidad, comisiones e impuestos; totales por tipo. |
| **Semana** | Publicaciones, mensajes y horas de cada socio; ventas y dinero de la semana se calculan solos. |
| **Ajustes** | Inversión, nombres de los socios, reparto de utilidad, aviso de pocas piezas, meta mensual. Exportación a CSV por hoja y respaldo completo en JSON. |

### Reglas que valida el servidor

Como los dos socios pueden registrar al mismo tiempo, estas reglas se revisan en el servidor (no solo en la pantalla):

- No se puede vender o apartar más piezas de las disponibles.
- El código de juguete no se repite (sin distinguir mayúsculas).
- La cantidad de un juguete no puede quedar debajo de lo ya vendido o apartado.
- No se puede eliminar un juguete que tiene ventas registradas.
- La fecha de pago se asigna sola al pasar una venta a Pagado o Entregado.

Cuando un socio guarda algo, la pantalla del otro se actualiza sola en unos 4 segundos.

---

## 2. Cómo ejecutarla

Elijan **una** computadora como principal (la que guardará los datos). Las demás solo necesitan un navegador.

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

El otro socio abre la dirección **"Desde la red local"** en su navegador o celular.

### Permitir el acceso en Windows

Si el otro socio no puede entrar, Windows Firewall está bloqueando el puerto. En una consola **como administrador**:

```bat
netsh advfirewall firewall add rule name="Control Pallet 5080" dir=in action=allow protocol=TCP localport=5080 profile=private
```

La regla se limita al perfil de red **privada**; la red de la casa debe estar marcada como privada en Windows.

---

## 3. Datos y respaldos

- Base de datos: `datos/pallet.db` (SQLite, un solo archivo).
- **Respaldo automático:** cada vez que la aplicación arranca se crea, si aún no existe, una copia del día en `datos/respaldos/pallet-AAAA-MM-DD.db`. Se conservan las últimas 14 (configurable en `appsettings.json` → `Respaldos:Conservar`).
- **Respaldo manual:** Ajustes → *Descargar respaldo* genera un JSON con toda la información.
- **Exportar a Excel:** Ajustes → botones CSV por hoja.
- Para restaurar un respaldo `.db`: detengan la aplicación, reemplacen `datos/pallet.db` por la copia (renombrada a `pallet.db`) y borren `pallet.db-wal` y `pallet.db-shm` si existen.

Recomendación: copien periódicamente la carpeta `datos/respaldos` a una USB o a la nube; los respaldos automáticos están en el mismo disco que la base.

### Pasar los datos que ya capturaron en el artefacto

1. En el artefacto: Ajustes → Exportar **Inventario** (CSV).
2. Abran el CSV en Excel y copien las columnas en este orden: Código, Juguete, Categoría, Cantidad, Estado, Precio de lista, Precio objetivo, Clase.
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
│   ├── Models/Entidades.cs     Producto, Venta, Gasto, Semana, Configuración
│   ├── Data/AppDbContext.cs    Mapeo EF Core → SQLite
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
