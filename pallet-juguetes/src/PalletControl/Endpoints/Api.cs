using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Models;

namespace PalletControl.Endpoints;

public static class Api
{
    // Las validaciones de inventario leen y escriben en dos pasos; este candado evita que
    // dos socios aparten la última pieza al mismo tiempo.
    private static readonly SemaphoreSlim Escritura = new(1, 1);

    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    private static string NuevoId() => Guid.NewGuid().ToString("N")[..12];
    private static string T(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static IResult Mal(string m) => Results.BadRequest(new Error(m));
    private static IResult Choque(string m) => Results.Conflict(new Error(m));

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/version", async (AppDbContext db) =>
            Results.Ok(new { version = (await db.Version.FindAsync(1))?.Valor ?? 0 }));

        api.MapGet("/estado", async (AppDbContext db) => Results.Ok(new
        {
            version = (await db.Version.FindAsync(1))?.Valor ?? 0,
            configuracion = await db.Configuracion.FindAsync(1),
            productos = await db.Productos.AsNoTracking().ToListAsync(),
            ventas = await db.Ventas.AsNoTracking().ToListAsync(),
            gastos = await db.Gastos.AsNoTracking().ToListAsync(),
            semanas = await db.Semanas.AsNoTracking().ToListAsync(),
            catalogos = new { Catalogos.Canales, Catalogos.TiposGasto }
        }));

        // ---------- Productos ----------
        api.MapPost("/productos", (ProductoIn i, AppDbContext db) => Bloqueado(() => GuardarProducto(db, null, i)));
        api.MapPut("/productos/{id}", (string id, ProductoIn i, AppDbContext db) => Bloqueado(() => GuardarProducto(db, id, i)));
        api.MapDelete("/productos/{id}", (string id, AppDbContext db) => Bloqueado(async () =>
        {
            var p = await db.Productos.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("El juguete ya no existe."));
            var n = await db.Ventas.CountAsync(v => v.ProductoId == id);
            if (n > 0) return Choque($"Este juguete tiene {n} venta(s) registrada(s). Elimínenlas primero o ajusten la cantidad a lo vendido.");
            db.Productos.Remove(p);
            await db.GuardarAsync();
            return Results.NoContent();
        }));
        api.MapPost("/productos/importar", (List<ProductoIn> filas, AppDbContext db) => Bloqueado(() => Importar(db, filas)));

        // ---------- Ventas ----------
        api.MapPost("/ventas", (VentaIn i, AppDbContext db) => Bloqueado(() => GuardarVenta(db, null, i)));
        api.MapPut("/ventas/{id}", (string id, VentaIn i, AppDbContext db) => Bloqueado(() => GuardarVenta(db, id, i)));
        api.MapPatch("/ventas/{id}/estado", (string id, CambioEstadoIn i, AppDbContext db) => Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return Results.NotFound(new Error("La venta ya no existe."));
            if (i.Estado is not ("pagado" or "entregado")) return Mal("Solo se puede avanzar a pagado o entregado.");
            v.Estado = i.Estado;
            v.FechaPago ??= Hoy;
            v.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(v);
        }));
        api.MapDelete("/ventas/{id}", (string id, AppDbContext db) => Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return Results.NotFound(new Error("La venta ya no existe."));
            db.Ventas.Remove(v);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

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
            s.HorasA = Math.Max(0, i.HorasA);
            s.HorasB = Math.Max(0, i.HorasB);
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
            c.Inversion = Math.Max(0, i.Inversion);
            c.SocioA = string.IsNullOrWhiteSpace(i.SocioA) ? "Persona A" : T(i.SocioA, 40);
            c.SocioB = string.IsNullOrWhiteSpace(i.SocioB) ? "Persona B" : T(i.SocioB, 40);
            if (string.Equals(c.SocioA, c.SocioB, StringComparison.OrdinalIgnoreCase)) return Mal("Los dos socios necesitan nombres distintos.");
            c.RepartoA = Math.Clamp(i.RepartoA, 0, 100);
            c.StockBajo = Math.Max(0, i.StockBajo);
            c.MetaMensual = Math.Max(0, i.MetaMensual);
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
                semanas = await db.Semanas.AsNoTracking().ToListAsync()
            });
        });
    }

    private static async Task<IResult> Bloqueado(Func<Task<IResult>> f)
    {
        await Escritura.WaitAsync();
        try { return await f(); }
        finally { Escritura.Release(); }
    }

    private static Task<int> Vendidas(AppDbContext db, string productoId, string? exceptoVenta) =>
        db.Ventas.Where(v => v.ProductoId == productoId && v.Estado != "cancelado" && v.Id != exceptoVenta)
                 .SumAsync(v => (int?)v.Cantidad).ContinueWith(t => t.Result ?? 0);

    private static string? ValidarProducto(ProductoIn i)
    {
        if (string.IsNullOrWhiteSpace(i.Codigo)) return "El código es obligatorio.";
        if (string.IsNullOrWhiteSpace(i.Nombre)) return "El nombre del juguete es obligatorio.";
        if (i.Cantidad < 0) return "La cantidad no puede ser negativa.";
        if (i.PrecioLista < 0 || i.PrecioObjetivo < 0 || i.PrecioMinimo < 0) return "Los precios no pueden ser negativos.";
        if (i.Clase is not null && !Catalogos.Clases.Contains(i.Clase)) return "La clase debe ser A, B o C.";
        if (i.Estado is not null && !Catalogos.EstadosProducto.Contains(i.Estado)) return "Estado de juguete no válido.";
        return null;
    }

    private static void Aplicar(Producto p, ProductoIn i)
    {
        p.Codigo = T(i.Codigo, 20);
        p.Nombre = T(i.Nombre, 120);
        p.Categoria = T(i.Categoria, 60);
        p.Clase = i.Clase ?? "B";
        p.Estado = i.Estado ?? "nuevo";
        p.Cantidad = i.Cantidad;
        p.PrecioLista = i.PrecioLista;
        p.PrecioObjetivo = i.PrecioObjetivo;
        p.PrecioMinimo = i.PrecioMinimo;
        p.Notas = T(i.Notas, 500);
        p.ActualizadoUtc = DateTime.UtcNow;
    }

    private static async Task<IResult> GuardarProducto(AppDbContext db, string? id, ProductoIn i)
    {
        if (ValidarProducto(i) is { } err) return Mal(err);
        var codigo = T(i.Codigo, 20);
        if (await db.Productos.AnyAsync(x => x.Id != id && x.Codigo == codigo))
            return Choque($"Ya existe un juguete con el código {codigo}. Usen otro código.");

        Producto? p;
        if (id is null) { p = new Producto { Id = NuevoId() }; db.Productos.Add(p); }
        else
        {
            p = await db.Productos.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("El juguete ya no existe."));
            var vend = await Vendidas(db, id, null);
            if (i.Cantidad < vend) return Choque($"La cantidad no puede ser menor que las {vend} piezas ya vendidas o apartadas.");
        }
        Aplicar(p, i);
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/productos/{p.Id}", p) : Results.Ok(p);
    }

    private static async Task<IResult> Importar(AppDbContext db, List<ProductoIn> filas)
    {
        if (filas.Count == 0) return Mal("No llegaron filas para importar.");
        if (filas.Count > 5000) return Mal("Máximo 5,000 filas por importación.");
        var existentes = await db.Productos.ToListAsync();
        var porCodigo = existentes.ToDictionary(p => p.Codigo.ToLowerInvariant());
        // Los códigos automáticos no deben chocar ni con los existentes ni con los que vienen en el mismo lote.
        var usados = existentes.Select(p => p.Codigo)
            .Concat(filas.Where(f => !string.IsNullOrWhiteSpace(f.Codigo)).Select(f => T(f.Codigo, 20)))
            .ToList();
        var siguiente = usados.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        int nuevos = 0, actualizados = 0;

        foreach (var f0 in filas)
        {
            var f = string.IsNullOrWhiteSpace(f0.Codigo) ? f0 with { Codigo = (siguiente++).ToString("000") } : f0;
            if (ValidarProducto(f) is { } err) return Mal($"Fila \"{f.Nombre}\": {err}");
            var key = T(f.Codigo, 20).ToLowerInvariant();
            if (porCodigo.TryGetValue(key, out var p))
            {
                // Al actualizar se conservan notas y precio mínimo, y nunca se baja de lo ya vendido.
                var vend = await Vendidas(db, p.Id, null);
                var notas = p.Notas; var min = p.PrecioMinimo;
                Aplicar(p, f with { Cantidad = Math.Max(f.Cantidad, vend) });
                p.Notas = notas; p.PrecioMinimo = min;
                actualizados++;
            }
            else
            {
                p = new Producto { Id = NuevoId() };
                Aplicar(p, f);
                db.Productos.Add(p);
                porCodigo[key] = p;
                nuevos++;
            }
        }
        await db.GuardarAsync();
        return Results.Ok(new { nuevos, actualizados });
    }

    private static async Task<IResult> GuardarVenta(AppDbContext db, string? id, VentaIn i)
    {
        if (string.IsNullOrWhiteSpace(i.ProductoId)) return Mal("Elige un juguete.");
        var estado = i.Estado ?? "apartado";
        if (!Catalogos.EstadosVenta.Contains(estado)) return Mal("Estado de venta no válido.");
        if (i.Cantidad < 1) return Mal("La cantidad mínima es 1.");
        if (i.Precio < 0) return Mal("El precio no puede ser negativo.");
        var p = await db.Productos.FindAsync(i.ProductoId);
        if (p is null) return Mal("El juguete elegido ya no existe.");

        Venta? v = null;
        if (id is not null)
        {
            v = await db.Ventas.FindAsync(id);
            if (v is null) return Results.NotFound(new Error("La venta ya no existe."));
        }
        if (estado != "cancelado")
        {
            var disp = p.Cantidad - await Vendidas(db, p.Id, id);
            if (i.Cantidad > disp) return Choque($"Solo hay {Math.Max(0, disp)} pieza(s) disponible(s) de {p.Nombre}.");
        }

        var fecha = i.Fecha ?? Hoy;
        DateOnly? fechaPago;
        if (!Catalogos.Cobrada(estado)) fechaPago = null;
        else if (v is not null && Catalogos.Cobrada(v.Estado) && v.FechaPago is not null) fechaPago = v.FechaPago;
        else fechaPago = v is not null ? Hoy : fecha;

        if (v is null) { v = new Venta { Id = NuevoId() }; db.Ventas.Add(v); }
        v.ProductoId = p.Id;
        v.Cantidad = i.Cantidad;
        v.Precio = i.Precio;
        v.Estado = estado;
        v.Fecha = fecha;
        v.FechaPago = fechaPago;
        v.Canal = T(i.Canal, 40);
        v.Responsable = T(i.Responsable, 40);
        v.Cliente = T(i.Cliente, 80);
        v.Notas = T(i.Notas, 400);
        v.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/ventas/{v.Id}", v) : Results.Ok(v);
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
        g.Persona = T(i.Persona, 40);
        g.Concepto = T(i.Concepto, 120);
        g.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/gastos/{g.Id}", g) : Results.Ok(g);
    }
}
