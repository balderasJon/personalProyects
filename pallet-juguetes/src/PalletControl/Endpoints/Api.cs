using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

public static class Api
{
    // Las validaciones de inventario leen y escriben en dos pasos; este candado evita que
    // dos pestañas o dispositivos aparten la última pieza al mismo tiempo.
    private static readonly SemaphoreSlim Escritura = new(1, 1);

    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    private static string NuevoId() => Guid.NewGuid().ToString("N")[..12];
    private static string T(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static IResult Mal(string m) => Results.BadRequest(new Error(m));
    private static IResult Choque(string m) => Results.Conflict(new Error(m));

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/version", async (AppDbContext db, TipoCambioDof tc) =>
        {
            await tc.AsegurarHoyAsync(db);   // al cambiar el día se define el tipo de cambio nuevo
            return Results.Ok(new { version = (await db.Version.FindAsync(1))?.Valor ?? 0 });
        });

        api.MapGet("/estado", async (AppDbContext db, TipoCambioDof tc) =>
        {
            await tc.AsegurarHoyAsync(db);
            return Results.Ok(new
        {
            version = (await db.Version.FindAsync(1))?.Valor ?? 0,
            configuracion = await db.Configuracion.FindAsync(1),
            tipoCambio = new { hoy = Hoy, error = TipoCambioDof.UltimoError },
            productos = await db.Productos.AsNoTracking().ToListAsync(),
            imagenes = (await db.Imagenes.AsNoTracking().OrderBy(i => i.CreadoUtc).ToListAsync())
                .Select(i => new { i.Id, i.ProductoId, i.Origen, i.Principal, url = "/imagenes/" + i.Archivo }),
            ventas = await db.Ventas.AsNoTracking().ToListAsync(),
            // Fase 3: renglones por lote (para existencias por lote), resultado real por venta, publicaciones y plataformas
            ventaDetalles = (await db.VentaDetalles.AsNoTracking().ToListAsync())
                .Select(d => new { d.VentaId, d.ProductoId, d.LoteId, d.PalletId, d.Cantidad, d.Devueltas }),
            ventaResultados = await VentasApi.ResultadosAsync(db),
            publicaciones = await db.Publicaciones.AsNoTracking().Where(p => p.Estatus != "eliminado").ToListAsync(),
            plataformas = await db.Plataformas.AsNoTracking().OrderBy(p => p.Orden).ThenBy(p => p.Nombre).Select(p => new { p.Id, p.Nombre, p.Activa }).ToListAsync(),
            gastos = await db.Gastos.AsNoTracking().ToListAsync(),
            semanas = await db.Semanas.AsNoTracking().ToListAsync(),
            pallets = await db.Pallets.AsNoTracking().OrderBy(p => p.FechaCompra).ToListAsync(),
            lotes = await db.Lotes.AsNoTracking().ToListAsync(),
            costos = await CostosVigentesAsync(db),
            catalogos = new
            {
                Catalogos.Canales, Catalogos.TiposGasto, Catalogos.TiposGastoPallet, Catalogos.EstadosComerciales, Catalogos.Tratamientos,
                condiciones = Catalogos.Condiciones.Select(c => new { c.Codigo, c.Nombre, c.Comercializable }),
            }
        });
        });

        // ---------- Precio mínimo: Endpoints/SugerenciaPrecioApi.cs (mismo criterio que la sugerencia al escanear) ----------

        // ---------- Traducción al español (MyMemory) ----------
        api.MapPost("/traducir", async (TraducirIn i, Traductor t, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(i.Texto)) return Mal("No hay texto para traducir.");
            if (i.Texto.Length > 2000) return Mal("Máximo 2,000 caracteres.");
            try { return Results.Ok(new { texto = await t.TraducirAsync(i.Texto, ct) }); }
            catch (Traductor.ErrorTraduccion e) { return Results.Json(new Error(e.Message), statusCode: 503); }
        });

        // ---------- Tipo de cambio (dolardof.com o manual) ----------
        api.MapPut("/tipo-cambio", (TipoCambioIn i, AppDbContext db) => Bloqueado(async () =>
        {
            if (i.Valor <= 0 || i.Valor > 1000) return Mal("Captura un tipo de cambio válido (pesos por dólar).");
            var c = await db.Configuracion.OrderBy(x => x.Id).FirstAsync();
            c.TipoCambioUsd = Math.Round(i.Valor, 4);
            c.TipoCambioFecha = Hoy;
            c.TipoCambioFuente = "Manual";
            c.TipoCambioPublicado = null;
            c.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            TipoCambioDof.MarcarManual();
            return Results.Ok(c);
        }));
        api.MapPost("/tipo-cambio/consultar", async (AppDbContext db, TipoCambioDof tc) =>
            await tc.ForzarAsync(db) ? Results.Ok(await db.Configuracion.AsNoTracking().OrderBy(x => x.Id).FirstAsync())
                                     : Results.Json(new Error(TipoCambioDof.UltimoError ?? "No se pudo consultar."), statusCode: 503));

        // ---------- Imágenes de juguetes ----------
        api.MapPost("/productos/{id}/imagenes", async (string id, HttpRequest req, AppDbContext db, AlmacenImagenes alm, CancellationToken ct) =>
        {
            if (await db.Productos.FindAsync([id], ct) is null) return Results.NotFound(new Error("El juguete ya no existe."));
            if (!req.HasFormContentType) return Mal("Envía las imágenes como formulario.");
            var form = await req.ReadFormAsync(ct);
            if (form.Files.Count == 0) return Mal("No llegó ninguna imagen.");
            var guardadas = 0; var rechazadas = new List<string>();
            foreach (var f in form.Files)
            {
                if (await db.Imagenes.CountAsync(x => x.ProductoId == id, ct) >= AlmacenImagenes.MaximoPorJuguete)
                { rechazadas.Add($"{f.FileName}: máximo {AlmacenImagenes.MaximoPorJuguete} imágenes por juguete"); continue; }
                if (f.Length > AlmacenImagenes.TamanoMaximo) { rechazadas.Add($"{f.FileName}: pesa más de 5 MB"); continue; }
                await using var st = f.OpenReadStream();
                var datos = await AlmacenImagenes.LeerLimitadoAsync(st, ct);
                var ext = datos is null ? null : AlmacenImagenes.Extension(datos);
                if (datos is null || ext is null) { rechazadas.Add($"{f.FileName}: no es JPG, PNG, WEBP o GIF"); continue; }
                await AgregarImagenAsync(db, alm, id, datos, ext, "subida", ct);
                guardadas++;
            }
            if (guardadas > 0) await db.GuardarAsync();
            return Results.Ok(new { guardadas, rechazadas });
        }).DisableAntiforgery();

        // Descarga a la PC las imágenes que UPCitemdb dio para el código de barras del juguete.
        api.MapPost("/productos/{id}/imagenes/upcitemdb", async (string id, AppDbContext db, AlmacenImagenes alm,
            IHttpClientFactory hf, ILoggerFactory lf, CancellationToken ct) =>
        {
            var p = await db.Productos.FindAsync([id], ct);
            if (p is null) return Results.NotFound(new Error("El juguete ya no existe."));
            var urls = await BuscadorCodigoBarras.ImagenesEnCacheAsync(db, p.CodigoBarras, ct);
            if (urls.Count == 0) return Mal("UPCitemdb no tiene imágenes para este código de barras.");
            var http = hf.CreateClient("imagenes");
            var guardadas = 0; var fallidas = 0;
            foreach (var url in urls.Take(3))
            {
                if (await db.Imagenes.CountAsync(x => x.ProductoId == id, ct) >= AlmacenImagenes.MaximoPorJuguete) break;
                try
                {
                    using var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!r.IsSuccessStatusCode) { fallidas++; continue; }
                    await using var st = await r.Content.ReadAsStreamAsync(ct);
                    var datos = await AlmacenImagenes.LeerLimitadoAsync(st, ct);
                    var ext = datos is null ? null : AlmacenImagenes.Extension(datos);
                    if (datos is null || ext is null) { fallidas++; continue; }
                    await AgregarImagenAsync(db, alm, id, datos, ext, "upcitemdb", ct);
                    guardadas++;
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    lf.CreateLogger("Imagenes").LogWarning(e, "No se pudo descargar {Url}", url);
                    fallidas++;
                }
            }
            if (guardadas > 0) await db.GuardarAsync();
            return guardadas == 0
                ? Results.Json(new Error("No se pudo descargar ninguna imagen de UPCitemdb (la tienda de origen la bloqueó o ya no existe). Puedes subir una foto."), statusCode: 502)
                : Results.Ok(new { guardadas, fallidas });
        });

        api.MapPut("/imagenes/{imgId}/principal", (string imgId, AppDbContext db) => Bloqueado(async () =>
        {
            var img = await db.Imagenes.FindAsync(imgId);
            if (img is null) return Results.NotFound(new Error("La imagen ya no existe."));
            foreach (var x in await db.Imagenes.Where(x => x.ProductoId == img.ProductoId).ToListAsync())
                x.Principal = x.Id == imgId;
            await db.GuardarAsync();
            return Results.NoContent();
        }));
        api.MapDelete("/imagenes/{imgId}", (string imgId, AppDbContext db, AlmacenImagenes alm) => Bloqueado(async () =>
        {
            var img = await db.Imagenes.FindAsync(imgId);
            if (img is null) return Results.NotFound(new Error("La imagen ya no existe."));
            db.Imagenes.Remove(img);
            if (img.Principal && await db.Imagenes.Where(x => x.ProductoId == img.ProductoId && x.Id != imgId)
                                                  .OrderBy(x => x.CreadoUtc).FirstOrDefaultAsync() is { } otra)
                otra.Principal = true;
            await db.GuardarAsync();
            alm.Borrar(img.Archivo);
            return Results.NoContent();
        }));

        // ---------- Productos ----------
        api.MapPost("/productos", (ProductoIn i, AppDbContext db) => Bloqueado(() => GuardarProducto(db, null, i)));
        api.MapPut("/productos/{id}", (string id, ProductoIn i, AppDbContext db) => Bloqueado(() => GuardarProducto(db, id, i)));
        api.MapDelete("/productos/{id}", (string id, AppDbContext db, AlmacenImagenes alm) => Bloqueado(async () =>
        {
            var p = await db.Productos.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("El juguete ya no existe."));
            var n = await db.Ventas.CountAsync(v => v.ProductoId == id);
            if (n > 0) return Choque($"Este juguete tiene {n} venta(s) registrada(s). Elimínenlas primero o ajusten la cantidad a lo vendido.");
            var imgs = await db.Imagenes.Where(x => x.ProductoId == id).ToListAsync();
            db.Imagenes.RemoveRange(imgs);
            var lotes = await db.Lotes.Where(l => l.ProductoId == id).ToListAsync();
            var idsLote = lotes.Select(l => l.Id).ToList();
            db.Movimientos.RemoveRange(await db.Movimientos.Where(m => idsLote.Contains(m.LoteId)).ToListAsync());
            db.Lotes.RemoveRange(lotes);
            var comp = await db.Competencia.Where(c => c.ProductoId == id).ToListAsync();   // Fase 4: sus precios de competencia
            db.Competencia.RemoveRange(comp);
            db.Publicaciones.RemoveRange(await db.Publicaciones.Where(x => x.ProductoId == id).ToListAsync());
            db.Productos.Remove(p);
            await db.GuardarAsync();
            foreach (var img in imgs) alm.Borrar(img.Archivo);
            foreach (var c in comp) if (c.ImagenArchivo is not null) alm.Borrar(c.ImagenArchivo);
            return Results.NoContent();
        }));
        api.MapPost("/productos/importar", (List<ProductoIn> filas, string? palletId, AppDbContext db) => Bloqueado(() => Importar(db, filas, palletId)));

        // ---------- Código de barras: inventario propio y, si no está, UPCitemdb ----------
        api.MapGet("/codigo-barras/{codigo}", async (string codigo, AppDbContext db, BuscadorCodigoBarras b, CancellationToken ct) =>
        {
            try { return Results.Ok(await b.BuscarAsync(db, codigo, ct)); }
            catch (ErrorBusqueda e) { return Results.Json(new Error(e.Message), statusCode: e.Estado); }
        });

        // ---------- Ventas: Endpoints/VentasApi.cs (Fase 3) ----------

        // ---------- Gastos ----------
        api.MapPost("/gastos", (GastoIn i, AppDbContext db) => Bloqueado(() => GuardarGasto(db, null, i)));
        api.MapPut("/gastos/{id}", (string id, GastoIn i, AppDbContext db) => Bloqueado(() => GuardarGasto(db, id, i)));
        api.MapDelete("/gastos/{id}", (string id, AppDbContext db) => Bloqueado(async () =>
        {
            var g = await db.Gastos.FindAsync(id);
            if (g is null) return Results.NotFound(new Error("El gasto ya no existe."));
            db.Gastos.Remove(g);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Semanas (la llave es cualquier fecha; se ajusta a su lunes) ----------
        api.MapPut("/semanas/{fecha}", (DateOnly fecha, SemanaIn i, AppDbContext db) => Bloqueado(async () =>
        {
            var k = Catalogos.Lunes(fecha);
            var s = await db.Semanas.FindAsync(k);
            if (s is null) { s = new Semana { Inicio = k }; db.Semanas.Add(s); }
            s.Publicadas = Math.Max(0, i.Publicadas);
            s.Mensajes = Math.Max(0, i.Mensajes);
            s.Horas = Math.Max(0, i.Horas);
            s.Notas = T(i.Notas, 600);
            s.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(s);
        }));
        api.MapDelete("/semanas/{fecha}", (DateOnly fecha, AppDbContext db) => Bloqueado(async () =>
        {
            var s = await db.Semanas.FindAsync(Catalogos.Lunes(fecha));
            if (s is null) return Results.NotFound(new Error("La semana ya no existe."));
            db.Semanas.Remove(s);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Configuración ----------
        api.MapPut("/configuracion", (ConfiguracionIn i, AppDbContext db) => Bloqueado(async () =>
        {
            var c = await db.Configuracion.FindAsync(1);
            if (c is null) { c = new Configuracion(); db.Configuracion.Add(c); }
            if (!await db.Pallets.AnyAsync()) c.Inversion = Math.Max(0, i.Inversion);   // con pallets, es la suma de sus inversiones
            c.StockBajo = Math.Max(0, i.StockBajo);
            c.MetaMensual = Math.Max(0, i.MetaMensual);
            c.DescuentoSugerido = Math.Clamp(i.DescuentoSugerido, 0, 90);
            c.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(c);
        }));

        // ---------- Respaldo completo en JSON (descarga) ----------
        api.MapGet("/respaldo", async (AppDbContext db, HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"pallet-respaldo-{DateTime.Now:yyyy-MM-dd}.json\"";
            return Results.Json(new
            {
                generado = DateTime.Now,
                configuracion = await db.Configuracion.FindAsync(1),
                productos = await db.Productos.AsNoTracking().ToListAsync(),
                ventas = await db.Ventas.AsNoTracking().ToListAsync(),
                gastos = await db.Gastos.AsNoTracking().ToListAsync(),
                semanas = await db.Semanas.AsNoTracking().ToListAsync(),
                imagenes = await db.Imagenes.AsNoTracking().ToListAsync(),
                pallets = await db.Pallets.AsNoTracking().ToListAsync(),
                palletGastos = await db.PalletGastos.AsNoTracking().ToListAsync(),
                lotes = await db.Lotes.AsNoTracking().ToListAsync(),
                movimientos = await db.Movimientos.AsNoTracking().ToListAsync(),
                costeoCorridas = await db.CosteoCorridas.AsNoTracking().ToListAsync(),
                costosLote = await db.CostosLote.AsNoTracking().ToListAsync(),
                plataformas = await db.Plataformas.AsNoTracking().ToListAsync(),
                tarifas = await db.Tarifas.AsNoTracking().ToListAsync(),
                parametrosRentabilidad = await db.Parametros.AsNoTracking().ToListAsync(),
                fiscal = await db.Fiscal.AsNoTracking().ToListAsync(),
                preciosProducto = await db.PreciosProducto.AsNoTracking().ToListAsync(),
                ventaDetalles = await db.VentaDetalles.AsNoTracking().ToListAsync(),
                ventaGastos = await db.VentaGastos.AsNoTracking().ToListAsync(),
                ventaIncidencias = await db.VentaIncidencias.AsNoTracking().ToListAsync(),
                publicaciones = await db.Publicaciones.AsNoTracking().ToListAsync(),
                competencia = await db.Competencia.AsNoTracking().ToListAsync(),
            });
        });
    }

    /// <summary>Costo vigente por lote (de la corrida vigente de cada pallet) y si ese costeo está desactualizado.</summary>
    private static async Task<object> CostosVigentesAsync(AppDbContext db)
    {
        var corridas = await db.CosteoCorridas.AsNoTracking().Where(c => c.Vigente).ToListAsync();
        var ids = corridas.Select(c => c.Id).ToList();
        var costos = await db.CostosLote.AsNoTracking().Where(c => ids.Contains(c.CorridaId)).ToListAsync();
        var estado = new List<object>();
        foreach (var c in corridas)
            estado.Add(new { c.PalletId, corridaId = c.Id, c.FechaUtc, c.Metodo, desactualizado = c.Huella != (await Inventario.EntradaAsync(db, c.PalletId)).Huella });
        return new
        {
            corridas = estado,
            lotes = costos.Select(x => new { x.LoteId, x.CorridaId, x.Cantidad, x.CostoTotal, x.CostoUnitario, x.EsMerma, x.Comercializable }),
        };
    }

    private static async Task AgregarImagenAsync(AppDbContext db, AlmacenImagenes alm, string productoId,
        byte[] datos, string ext, string origen, CancellationToken ct)
    {
        var id = NuevoId();
        var archivo = await alm.GuardarAsync(id, datos, ext, ct);
        var tienePrincipal = await db.Imagenes.AnyAsync(x => x.ProductoId == productoId && x.Principal, ct)
                             || db.Imagenes.Local.Any(x => x.ProductoId == productoId && x.Principal);
        db.Imagenes.Add(new ProductoImagen { Id = id, ProductoId = productoId, Archivo = archivo, Origen = origen, Principal = !tienePrincipal });
    }

    /// <summary>
    /// Ejecuta una escritura con el candado global (evita carreras en validaciones de inventario).
    /// Los errores de negocio se convierten en respuestas con mensaje en español y no guardan nada.
    /// </summary>
    public static async Task<IResult> Bloqueado(Func<Task<IResult>> f)
    {
        await Escritura.WaitAsync();
        try { return await f(); }
        catch (ErrorNegocio e) { return Results.Json(new Error(e.Message), statusCode: e.Estado); }
        finally { Escritura.Release(); }
    }

    /// <summary>Piezas vendidas netas (vendidas − devueltas) de ventas no canceladas.</summary>
    private static Task<int> Vendidas(AppDbContext db, string productoId, string? exceptoVenta) =>
        db.Ventas.Where(v => v.ProductoId == productoId && v.Estado != "cancelado" && v.Id != exceptoVenta)
                 .SumAsync(v => (int?)(v.Cantidad - v.CantidadDevuelta)).ContinueWith(t => t.Result ?? 0);

    private static string? ValidarProducto(ProductoIn i)
    {
        if (string.IsNullOrWhiteSpace(i.Codigo)) return "El código es obligatorio.";
        if (string.IsNullOrWhiteSpace(i.Nombre)) return "El nombre del juguete es obligatorio.";
        if (BuscadorCodigoBarras.Normalizar(i.CodigoBarras).Length > 20) return "El código de barras admite máximo 20 caracteres.";
        if (i.Cantidad < 0) return "La cantidad no puede ser negativa.";
        if (i.PrecioListaUsd < 0) return "El precio de lista en dólares no puede ser negativo.";
        if (i.ValorReferencia < 0) return "El valor de referencia no puede ser negativo.";
        if (i.PrecioLista < 0 || i.PrecioObjetivo < 0 || i.PrecioMinimo < 0 || i.PrecioMexico < 0
            || i.PrecioLiverpool < 0 || i.PrecioMercadoLibre < 0 || i.PrecioWalmart < 0) return "Los precios no pueden ser negativos.";
        if (i.Clase is not null && !Catalogos.Clases.Contains(i.Clase)) return "La clase debe ser A, B o C.";
        if (i.Estado is not null && !Catalogos.EstadosProducto.Contains(i.Estado)) return "Estado de juguete no válido.";
        if (i.Condicion is not null && !Catalogos.CondicionValida(i.Condicion)) return "Condición no válida.";
        if (i.EstadoComercial is not null && !Catalogos.EstadosComerciales.Contains(i.EstadoComercial)) return "Estado comercial no válido.";
        if (i.PesoKg < 0 || i.LargoCm < 0 || i.AnchoCm < 0 || i.AltoCm < 0) return "El peso y las medidas no pueden ser negativos.";
        return null;
    }

    /// <summary>
    /// El precio de lista siempre se guarda en pesos. Si se captura en dólares, se convierte con el tipo de cambio del día
    /// (redondeado al peso) y se guardan el monto original y el tipo de cambio usado.
    /// </summary>
    private static void AplicarPrecioLista(Producto p, ProductoIn i, decimal tipoCambio)
    {
        if (i.PrecioListaUsd is > 0)
        {
            if (tipoCambio <= 0) throw new ErrorNegocio("No hay tipo de cambio del día para convertir el precio de lista en dólares. Defínelo arriba (Tipo de cambio) o captura el precio en pesos.");
            var usd = Math.Round(i.PrecioListaUsd.Value, 2, MidpointRounding.AwayFromZero);
            // Si es el mismo monto en dólares ya convertido, se conserva el tipo de cambio original (la referencia no cambia sola).
            if (p.PrecioListaUsd == usd && p.PrecioListaTipoCambio is > 0) return;
            p.PrecioListaUsd = usd;
            p.PrecioListaTipoCambio = tipoCambio;
            p.PrecioLista = Math.Round(usd * tipoCambio, 0, MidpointRounding.AwayFromZero);
            return;
        }
        // En pesos: un cliente anterior (null) que no cambia el monto conserva el dato en dólares; si lo cambia, ya es un precio en pesos.
        if (i.PrecioListaUsd == 0 || i.PrecioLista != p.PrecioLista) { p.PrecioListaUsd = null; p.PrecioListaTipoCambio = null; }
        p.PrecioLista = i.PrecioLista;
    }

    private static void Aplicar(Producto p, ProductoIn i, decimal tipoCambio = 0)
    {
        p.Codigo = T(i.Codigo, 20);
        p.CodigoBarras = BuscadorCodigoBarras.Normalizar(i.CodigoBarras);
        p.Nombre = T(i.Nombre, 120);
        p.Categoria = T(i.Categoria, 60);
        p.Clase = i.Clase ?? "B";
        AplicarPrecioLista(p, i, tipoCambio);
        p.PrecioObjetivo = i.PrecioObjetivo;
        p.PrecioMinimo = i.PrecioMinimo;
        p.PrecioLiverpool = Math.Max(0, i.PrecioLiverpool);
        p.PrecioMercadoLibre = Math.Max(0, i.PrecioMercadoLibre);
        p.PrecioWalmart = Math.Max(0, i.PrecioWalmart);
        // Con precios por tienda, el precio en México es su promedio; sin ellos, el capturado a mano.
        p.PrecioMexico = Precios.PromedioTiendas(p.PrecioLiverpool, p.PrecioMercadoLibre, p.PrecioWalmart) ?? i.PrecioMexico;
        if (i.ValorReferencia is { } vr)
        {
            p.ValorReferencia = vr > 0 ? Dominio.Calculo.Dinero.Centavos(vr) : null;
            p.ValorReferenciaOrigen = vr > 0 ? T(i.ValorReferenciaOrigen ?? "upcitemdb", 30) : "";
        }
        p.Caracteristicas = T(i.Caracteristicas, 2000);
        p.Notas = T(i.Notas, 500);
        if (i.Marca is not null) p.Marca = T(i.Marca, 60);
        if (i.Modelo is not null) p.Modelo = T(i.Modelo, 60);
        if (i.Subcategoria is not null) p.Subcategoria = T(i.Subcategoria, 60);
        if (i.DescripcionComercial is not null) p.DescripcionComercial = T(i.DescripcionComercial, 2000);
        if (i.AccesoriosIncluidos is not null) p.AccesoriosIncluidos = T(i.AccesoriosIncluidos, 300);
        if (i.EdadRecomendada is not null) p.EdadRecomendada = T(i.EdadRecomendada, 40);
        if (i.PesoKg is not null) p.PesoKg = i.PesoKg > 0 ? Math.Round(i.PesoKg.Value, 3) : null;   // 0 = borrar
        if (i.LargoCm is not null) p.LargoCm = i.LargoCm > 0 ? Math.Round(i.LargoCm.Value, 2) : null;
        if (i.AnchoCm is not null) p.AnchoCm = i.AnchoCm > 0 ? Math.Round(i.AnchoCm.Value, 2) : null;
        if (i.AltoCm is not null) p.AltoCm = i.AltoCm > 0 ? Math.Round(i.AltoCm.Value, 2) : null;
        if (i.EstadoComercial is not null) p.EstadoComercial = i.EstadoComercial;
        if (i.FechaIngreso is not null) p.FechaIngreso = i.FechaIngreso;
        p.ActualizadoUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Cantidad y condición viven en los lotes. Producto nuevo: crea su lote en el pallet indicado.
    /// Producto con un solo lote activo: la cantidad se ajusta (con movimiento en el kárdex) y la condición se cambia en el lote.
    /// Con varios lotes, la cantidad se administra en cada lote.
    /// </summary>
    private static async Task<bool> AplicarInventarioAsync(AppDbContext db, Producto p, ProductoIn i, bool nuevo, string? palletPorDefecto, bool estricto = true)
    {
        var lotes = nuevo ? [] : await Inventario.LotesDeAsync(db, p.Id);
        if (lotes.Count == 0)
        {
            var palletId = i.PalletId ?? palletPorDefecto ?? await PalletUnicoAsync(db)
                ?? throw new ErrorNegocio("Elige el pallet de origen del juguete (o registra primero un pallet en Análisis comercial › Pallets y costos).");
            var condicion = i.Condicion ?? Catalogos.CondicionDesdeEstado(i.Estado ?? "nuevo");
            var fecha = i.FechaIngreso ?? p.FechaIngreso ?? DateOnly.FromDateTime(DateTime.Now);
            p.FechaIngreso ??= fecha;
            var lote = await Inventario.LoteAsync(db, p.Id, palletId, condicion, fecha);
            Inventario.Mover(db, lote, i.Cantidad, "recepcion", nuevo ? "Alta del juguete" : "Primer lote del juguete");
            return true;
        }
        var activos = lotes.Where(l => l.Cantidad > 0).ToList();
        if (activos.Count > 1)
        {
            if (i.Cantidad != lotes.Sum(l => l.Cantidad))
            {
                if (estricto) throw new ErrorNegocio("Este juguete tiene varios lotes: ajusta la cantidad en cada lote (ficha del juguete › Lotes).");
                return false;
            }
            return true;
        }
        var unico = activos.SingleOrDefault() ?? lotes.OrderByDescending(l => l.ActualizadoUtc).First();
        // Condición: la del formulario o, en clientes anteriores, la equivalente del estado (nuevo/dañado/incompleto).
        var nuevaCond = i.Condicion
            ?? (i.Estado is not null && i.Estado != Catalogos.EstadoDesdeCondicion(unico.Condicion) ? Catalogos.CondicionDesdeEstado(i.Estado) : unico.Condicion);
        if (nuevaCond != unico.Condicion)
        {
            if (lotes.Any(l => l.Id != unico.Id && l.PalletId == unico.PalletId && l.Condicion == nuevaCond))
                throw new ErrorNegocio("Ya existe un lote con esa condición; usa “Cambiar condición” en los lotes del juguete.");
            unico.Condicion = nuevaCond;
            unico.Comercializable = Catalogos.ComercializablePorDefecto(nuevaCond);
            unico.Tratamiento = "";
            unico.ActualizadoUtc = DateTime.UtcNow;
        }
        Inventario.Mover(db, unico, i.Cantidad - unico.Cantidad, "ajuste", "Ajuste desde la ficha del juguete");
        return true;
    }

    private static async Task<string?> PalletUnicoAsync(AppDbContext db)
    {
        var ids = await db.Pallets.Select(p => p.Id).Take(2).ToListAsync();
        return ids.Count == 1 ? ids[0] : null;
    }

    private static async Task<IResult> GuardarProducto(AppDbContext db, string? id, ProductoIn i)
    {
        if (ValidarProducto(i) is { } err) return Mal(err);
        var codigo = T(i.Codigo, 20);
        if (await db.Productos.AnyAsync(x => x.Id != id && x.Codigo == codigo))
            return Choque($"Ya existe un juguete con el código {codigo}. Usen otro código.");
        var barras = BuscadorCodigoBarras.Normalizar(i.CodigoBarras);
        if (barras.Length > 0 && await db.Productos.FirstOrDefaultAsync(x => x.Id != id && x.CodigoBarras == barras) is { } otro)
            return Choque($"El código de barras {barras} ya está registrado en el juguete {otro.Codigo} · {otro.Nombre}. Aumenten la cantidad de ese juguete.");

        Producto? p;
        if (id is null) { p = new Producto { Id = NuevoId(), FechaIngreso = i.FechaIngreso }; db.Productos.Add(p); }
        else
        {
            p = await db.Productos.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("El juguete ya no existe."));
            var vend = await Vendidas(db, id, null);
            if (i.Cantidad < vend) return Choque($"La cantidad no puede ser menor que las {vend} piezas ya vendidas o apartadas.");
        }
        Aplicar(p, i, (await db.Configuracion.OrderBy(x => x.Id).FirstAsync()).TipoCambioUsd);
        await AplicarInventarioAsync(db, p, i, id is null, null);
        await Inventario.SincronizarProductoAsync(db, p.Id);
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/productos/{p.Id}", p) : Results.Ok(p);
    }

    private static async Task<IResult> Importar(AppDbContext db, List<ProductoIn> filas, string? palletId)
    {
        if (filas.Count == 0) return Mal("No llegaron filas para importar.");
        if (filas.Count > 5000) return Mal("Máximo 5,000 filas por importación.");
        if (palletId is not null && await db.Pallets.FindAsync(palletId) is null) return Mal("El pallet elegido no existe.");
        var omitidos = new List<string>();
        var existentes = await db.Productos.ToListAsync();
        var porCodigo = existentes.ToDictionary(p => p.Codigo.ToLowerInvariant());
        // Los códigos automáticos no deben chocar ni con los existentes ni con los que vienen en el mismo lote.
        var usados = existentes.Select(p => p.Codigo)
            .Concat(filas.Where(f => !string.IsNullOrWhiteSpace(f.Codigo)).Select(f => T(f.Codigo, 20)))
            .ToList();
        var siguiente = usados.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        int nuevos = 0, actualizados = 0;

        var barrasUsados = existentes.Where(p => p.CodigoBarras != "").ToDictionary(p => p.CodigoBarras, p => p.Codigo.ToLowerInvariant());
        foreach (var f0 in filas)
        {
            var f = string.IsNullOrWhiteSpace(f0.Codigo) ? f0 with { Codigo = (siguiente++).ToString("000") } : f0;
            if (ValidarProducto(f) is { } err) return Mal($"Fila \"{f.Nombre}\": {err}");
            var key = T(f.Codigo, 20).ToLowerInvariant();
            var cb = BuscadorCodigoBarras.Normalizar(f.CodigoBarras);
            if (cb.Length > 0)
            {
                if (barrasUsados.TryGetValue(cb, out var dueno) && dueno != key)
                    return Choque($"Fila \"{f.Nombre}\": el código de barras {cb} ya pertenece al juguete {dueno.ToUpperInvariant()}.");
                barrasUsados[cb] = key;
            }
            if (porCodigo.TryGetValue(key, out var p))
            {
                // Al actualizar se conservan notas y precio mínimo, y nunca se baja de lo ya vendido.
                var vend = await Vendidas(db, p.Id, null);
                var notas = p.Notas; var min = p.PrecioMinimo; var barrasPrevio = p.CodigoBarras;
                var caract = p.Caracteristicas; var mx = p.PrecioMexico;
                var (lv, ml, wm) = (p.PrecioLiverpool, p.PrecioMercadoLibre, p.PrecioWalmart);
                var fc = f with { Cantidad = Math.Max(f.Cantidad, vend) };
                Aplicar(p, fc);
                if (!await AplicarInventarioAsync(db, p, fc, false, palletId, estricto: false)) omitidos.Add(p.Codigo);
                p.Notas = notas; p.PrecioMinimo = min; p.Caracteristicas = caract;
                if (p.PrecioMexico == 0) p.PrecioMexico = mx;
                (p.PrecioLiverpool, p.PrecioMercadoLibre, p.PrecioWalmart) = (lv, ml, wm);
                p.PrecioMexico = Precios.PromedioTiendas(lv, ml, wm) ?? p.PrecioMexico;
                if (p.CodigoBarras.Length == 0) p.CodigoBarras = barrasPrevio;
                actualizados++;
            }
            else
            {
                p = new Producto { Id = NuevoId() };
                Aplicar(p, f);
                db.Productos.Add(p);
                await AplicarInventarioAsync(db, p, f, true, palletId);
                porCodigo[key] = p;
                nuevos++;
            }
        }
        foreach (var prod in porCodigo.Values) await Inventario.SincronizarProductoAsync(db, prod.Id);
        await db.GuardarAsync();
        return Results.Ok(new { nuevos, actualizados, cantidadNoActualizada = omitidos });
    }

    private static async Task<IResult> GuardarGasto(AppDbContext db, string? id, GastoIn i)
    {
        if (i.Monto < 0) return Mal("El monto no puede ser negativo.");
        Gasto? g;
        if (id is null) { g = new Gasto { Id = NuevoId() }; db.Gastos.Add(g); }
        else
        {
            g = await db.Gastos.FindAsync(id);
            if (g is null) return Results.NotFound(new Error("El gasto ya no existe."));
        }
        g.Tipo = string.IsNullOrWhiteSpace(i.Tipo) ? "Otro" : T(i.Tipo, 40);
        g.Monto = i.Monto;
        g.Fecha = i.Fecha ?? Hoy;
        g.Concepto = T(i.Concepto, 120);
        g.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/gastos/{g.Id}", g) : Results.Ok(g);
    }
}
