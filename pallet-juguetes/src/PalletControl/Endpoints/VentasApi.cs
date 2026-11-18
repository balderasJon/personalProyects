using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

public record VentaIn(string? ProductoId, int Cantidad, decimal Precio, string? Estado, DateOnly? Fecha,
    string? Canal, string? Cliente, string? Notas,
    // Fase 3 (opcionales: los clientes anteriores siguen funcionando)
    string? PlataformaId = null, string? PublicacionId = null, string? LoteId = null, decimal? PrecioPublicado = null, decimal Descuento = 0,
    List<GastoVentaIn>? Gastos = null, decimal? RetencionIsr = null, decimal? RetencionIva = null, decimal? ImporteCobrado = null, DateOnly? FechaCobro = null);

public record CambioEstadoIn(string? Estado, decimal? ImporteCobrado = null, DateOnly? FechaCobro = null);
public record CobroIn(decimal? Importe, DateOnly? Fecha);
public record CancelacionIn(DateOnly? Fecha, string? Motivo, decimal MontoReembolsado = 0, List<GastoVentaIn>? Gastos = null);
public record DevolucionIn(int Cantidad, string? CondicionDestino, decimal MontoReembolsado, List<GastoVentaIn>? Gastos, DateOnly? Fecha, string? Motivo);
public record ReembolsoIn(decimal Monto, List<GastoVentaIn>? Gastos, DateOnly? Fecha, string? Motivo);
public record ReclamacionIn(DateOnly? Fecha, string? Motivo);
public record CierreReclamacionIn(decimal MontoReembolsado, List<GastoVentaIn>? Gastos, string? Resolucion);
public record PublicacionIn(string? ProductoId, string? PlataformaId, string? LoteId, string? IdExterno, string? Url, decimal PrecioPublicado,
    DateOnly? FechaPublicacion, string? Estatus, int Existencias = 1, string? Observaciones = null);

/// <summary>Fase 3: ventas con detalle por lote, gastos reales, cobros, incidencias, publicaciones y rentabilidad real.</summary>
public static class VentasApi
{
    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    private static string T(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static IResult NoExiste() => Results.NotFound(new Error("La venta ya no existe."));

    private static VentaDatos Datos(VentaIn i) => new(
        N(i.ProductoId) ?? throw new ErrorNegocio("Elige un juguete."), i.Cantidad, i.Precio, i.Estado ?? "apartado", i.Fecha ?? Hoy,
        i.Canal ?? "", i.Cliente ?? "", i.Notas ?? "", N(i.PlataformaId), N(i.PublicacionId), N(i.LoteId), i.PrecioPublicado, i.Descuento,
        i.Gastos, i.RetencionIsr, i.RetencionIva, i.ImporteCobrado, i.FechaCobro);

    public static void MapVentas(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---------- Ventas ----------
        api.MapPost("/ventas", (VentaIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await Ventas.RegistrarAsync(db, Datos(i));
            await db.GuardarAsync();
            return Results.Created($"/api/ventas/{v.Id}", await DetalleAsync(db, v.Id));
        }));
        api.MapPut("/ventas/{id}", (string id, VentaIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            await Ventas.ModificarAsync(db, v, Datos(i));
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapGet("/ventas/{id}", async (string id, AppDbContext db) =>
            await db.Ventas.AnyAsync(v => v.Id == id) ? Results.Ok(await DetalleAsync(db, id)) : NoExiste());

        // Estado comercial (pagado / entregado). El cobro es aparte: solo cuenta lo que realmente llegó.
        api.MapPatch("/ventas/{id}/estado", (string id, CambioEstadoIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            if (i.Estado is not ("pagado" or "entregado")) return Results.BadRequest(new Error("Solo se puede avanzar a pagado o entregado."));
            if (v.Estado == "cancelado") return Results.Conflict(new Error("La venta está cancelada."));
            v.Estado = i.Estado;
            v.FechaPago ??= Hoy;
            if (i.ImporteCobrado is not null) Ventas.RegistrarCobro(v, i.ImporteCobrado, i.FechaCobro);
            v.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(v);
        }));
        api.MapPost("/ventas/{id}/cobro", (string id, CobroIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            if (i.Importe is null) return Results.BadRequest(new Error("Indica el importe que llegó a tu cuenta."));
            Ventas.RegistrarCobro(v, i.Importe, i.Fecha);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapDelete("/ventas/{id}/cobro", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            Ventas.RegistrarCobro(v, null, null);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapPost("/ventas/{id}/cancelar", (string id, CancelacionIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            await Ventas.CancelarAsync(db, v, i.Fecha, i.Motivo, i.MontoReembolsado, i.Gastos ?? []);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapPost("/ventas/{id}/devolucion", (string id, DevolucionIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            await Ventas.DevolverAsync(db, v, i.Cantidad, N(i.CondicionDestino), i.MontoReembolsado, i.Gastos ?? [], i.Fecha, i.Motivo);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapPost("/ventas/{id}/reembolso", (string id, ReembolsoIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            await Ventas.ReembolsarAsync(db, v, i.Monto, i.Gastos ?? [], i.Fecha, i.Motivo);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapPost("/ventas/{id}/reclamacion", (string id, ReclamacionIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            await Ventas.ReclamarAsync(db, v, i.Fecha, i.Motivo);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapPost("/incidencias/{iid}/cerrar", (string iid, CierreReclamacionIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var inc = await db.VentaIncidencias.FindAsync(iid);
            if (inc is null) return Results.NotFound(new Error("La incidencia ya no existe."));
            var v = (await db.Ventas.FindAsync(inc.VentaId))!;
            await Ventas.CerrarReclamacionAsync(db, v, inc, i.MontoReembolsado, i.Gastos ?? [], i.Resolucion);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, v.Id));
        }));
        api.MapDelete("/ventas/{id}", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var v = await db.Ventas.FindAsync(id);
            if (v is null) return NoExiste();
            await Ventas.EliminarAsync(db, v);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Gastos de una venta ----------
        api.MapPost("/ventas/{id}/gastos", (string id, GastoVentaIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            if (await db.Ventas.FindAsync(id) is null) return NoExiste();
            var g = GastoValido(i);
            db.VentaGastos.Add(new VentaGasto { Id = Inventario.NuevoId(), VentaId = id, Tipo = g.Tipo!, Concepto = T(g.Concepto ?? g.Tipo, 120), Monto = Dinero.Centavos(g.Monto), DeducidoPorPlataforma = g.DeducidoPorPlataforma });
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));
        api.MapPut("/venta-gastos/{gid}", (string gid, GastoVentaIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var x = await db.VentaGastos.FindAsync(gid);
            if (x is null) return Results.NotFound(new Error("El gasto ya no existe."));
            var g = GastoValido(i);
            x.Tipo = g.Tipo!; x.Concepto = T(g.Concepto ?? g.Tipo, 120); x.Monto = Dinero.Centavos(g.Monto);
            x.DeducidoPorPlataforma = g.DeducidoPorPlataforma; x.Estimado = false;   // al editarlo queda confirmado
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, x.VentaId));
        }));
        api.MapDelete("/venta-gastos/{gid}", (string gid, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var x = await db.VentaGastos.FindAsync(gid);
            if (x is null) return Results.NotFound(new Error("El gasto ya no existe."));
            db.VentaGastos.Remove(x);
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, x.VentaId));
        }));
        api.MapPost("/ventas/{id}/gastos/confirmar", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            if (await db.Ventas.FindAsync(id) is null) return NoExiste();
            foreach (var g in await db.VentaGastos.Where(g => g.VentaId == id && g.Estimado).ToListAsync()) g.Estimado = false;
            await db.GuardarAsync();
            return Results.Ok(await DetalleAsync(db, id));
        }));

        // Gastos que precargaría la tarifa (para el formulario, antes de guardar).
        api.MapGet("/ventas/estimar", async (string productoId, string plataformaId, decimal precio, int? cantidad, decimal? descuento, DateOnly? fecha, AppDbContext db) =>
        {
            var p = await db.Productos.FindAsync(productoId);
            if (p is null) return Results.NotFound(new Error("El juguete ya no existe."));
            var n = Math.Max(1, cantidad ?? 1);
            var bruto = Dinero.Centavos(precio * n - (descuento ?? 0));
            var fi = await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new ConfiguracionFiscal();
            var factor = Rentabilidad.Fiscal(fi).FactorIngreso;
            var (t, gastos, isr, iva) = await Ventas.EstimarAsync(db, p, plataformaId, fecha ?? Hoy, precio, n, bruto, factor);
            var costo = await new Rentabilidad(db).CostoDeAsync(p.Id);
            return Results.Ok(new
            {
                tarifa = t is null ? null : new { t.Id, t.Nombre, t.VigenteDesde, t.VigenteHasta },
                aviso = t is null ? "No hay tarifa vigente para esta plataforma en la fecha: captura los gastos reales." : null,
                gastos = gastos.Select(g => new { g.Tipo, g.Concepto, g.Monto, g.DeducidoPorPlataforma, estimado = true }),
                retencionIsr = isr, retencionIva = iva, factorIngreso = factor, fiscalPendiente = fi.Regimen == "pendiente",
                costoUnitario = costo.Unitario, disponibles = await Ventas.DisponibleVentaAsync(db, p.Id),
            });
        });

        // Existencias por lote de un juguete (para elegir el lote de la venta).
        api.MapGet("/productos/{id}/existencias", async (string id, AppDbContext db) =>
        {
            var ex = await Ventas.ExistenciasAsync(db, id);
            var pallets = await db.Pallets.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            return Results.Ok(ex.Select(x => new { loteId = x.Lote.Id, pallet = pallets.GetValueOrDefault(x.Lote.PalletId), x.Lote.Condicion, x.Lote.Comercializable, recibidas = x.Lote.Cantidad, disponibles = x.Disponibles }));
        });

        // ---------- Publicaciones ----------
        api.MapGet("/publicaciones", async (string? productoId, AppDbContext db) =>
        {
            var q = db.Publicaciones.AsNoTracking();
            if (productoId is not null) q = q.Where(x => x.ProductoId == productoId);
            var pubs = await q.ToListAsync();
            var prods = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Id);
            var pls = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            var disp = new Dictionary<string, int>();
            foreach (var pid in pubs.Select(p => p.ProductoId).Distinct()) disp[pid] = await Ventas.DisponibleVentaAsync(db, pid);
            return Results.Ok(pubs.OrderBy(p => p.RequiereAccion == "" ? 1 : 0).ThenByDescending(p => p.ActualizadoUtc).Select(p => new
            {
                publicacion = p, producto = prods.TryGetValue(p.ProductoId, out var pr) ? pr.Codigo + " · " + pr.Nombre : "",
                plataforma = pls.GetValueOrDefault(p.PlataformaId), disponibles = disp.GetValueOrDefault(p.ProductoId),
            }));
        });
        api.MapGet("/publicaciones/alertas", async (AppDbContext db) =>
            Results.Ok(await db.Publicaciones.AsNoTracking().Where(p => p.RequiereAccion != "").CountAsync()));
        api.MapPost("/publicaciones", (PublicacionIn i, AppDbContext db) => Api.Bloqueado(() => GuardarPublicacion(db, null, i)));
        api.MapPut("/publicaciones/{id}", (string id, PublicacionIn i, AppDbContext db) => Api.Bloqueado(() => GuardarPublicacion(db, id, i)));
        // Confirmas que ya hiciste en la plataforma lo que pedía el aviso (pausar, ajustar o reactivar).
        api.MapPost("/publicaciones/{id}/atendida", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var p = await db.Publicaciones.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("La publicación ya no existe."));
            var disp = await Ventas.DisponibleVentaAsync(db, p.ProductoId);
            switch (p.RequiereAccion)
            {
                case "pausar": p.Estatus = "pausado"; break;
                case "ajustar_existencias": p.Existencias = disp; break;
                case "reactivar": p.Estatus = "publicado"; p.Existencias = disp; break;
            }
            p.RequiereAccion = "";
            p.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(p);
        }));
        api.MapDelete("/publicaciones/{id}", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var p = await db.Publicaciones.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("La publicación ya no existe."));
            // Con ventas asociadas se conserva como eliminada para no perder el historial.
            if (await db.Ventas.AnyAsync(v => v.PublicacionId == id)) { p.Estatus = "eliminado"; p.RequiereAccion = ""; p.ActualizadoUtc = DateTime.UtcNow; }
            else db.Publicaciones.Remove(p);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Rentabilidad real ----------
        api.MapGet("/rentabilidad-real", async (DateOnly? desde, DateOnly? hasta, string? plataformaId, string? productoId, string? palletId, AppDbContext db) =>
            Results.Ok(await RentabilidadRealAsync(db, desde, hasta, plataformaId, productoId, palletId)));
    }

    private static GastoVentaIn GastoValido(GastoVentaIn g)
    {
        if (g.Monto < 0) throw new ErrorNegocio("El gasto no puede ser negativo.");
        if (g.Monto == 0) throw new ErrorNegocio("Indica el monto del gasto.");
        return g with { Tipo = Ventas.TiposGasto.Contains(g.Tipo) ? g.Tipo : "otro" };
    }

    private static async Task<IResult> GuardarPublicacion(AppDbContext db, string? id, PublicacionIn i)
    {
        var estatus = i.Estatus ?? "publicado";
        if (!Ventas.EstatusPublicacion.Contains(estatus)) return Results.BadRequest(new Error("Estatus de publicación no válido."));
        if (i.PrecioPublicado < 0) return Results.BadRequest(new Error("El precio no puede ser negativo."));
        if (i.Existencias < 0) return Results.BadRequest(new Error("Las existencias no pueden ser negativas."));
        var prod = N(i.ProductoId) is { } pid ? await db.Productos.FindAsync(pid) : null;
        if (prod is null) return Results.BadRequest(new Error("Elige el juguete publicado."));
        if (N(i.PlataformaId) is not { } plid || await db.Plataformas.FindAsync(plid) is null) return Results.BadRequest(new Error("Elige la plataforma."));
        if (N(i.LoteId) is { } lid && await db.Lotes.FirstOrDefaultAsync(l => l.Id == lid && l.ProductoId == prod.Id) is null)
            return Results.BadRequest(new Error("El lote no es de este juguete."));
        var url = T(i.Url, 500);
        if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
            return Results.BadRequest(new Error("La URL debe empezar con http:// o https://."));
        Publicacion? p;
        if (id is null) { p = new Publicacion { Id = Inventario.NuevoId() }; db.Publicaciones.Add(p); }
        else
        {
            p = await db.Publicaciones.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("La publicación ya no existe."));
        }
        p.ProductoId = prod.Id; p.PlataformaId = plid; p.LoteId = N(i.LoteId); p.IdExterno = T(i.IdExterno, 80); p.Url = url;
        p.PrecioPublicado = Dinero.Centavos(i.PrecioPublicado); p.FechaPublicacion = i.FechaPublicacion ?? (estatus == "publicado" ? Hoy : null);
        p.Estatus = estatus; p.Existencias = i.Existencias; p.Observaciones = T(i.Observaciones, 500);
        p.RequiereAccion = ""; p.ActualizadoUtc = DateTime.UtcNow;
        await Ventas.RevisarPublicacionesAsync(db, prod.Id, null, 0);
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/publicaciones/{p.Id}", p) : Results.Ok(p);
    }

    // ---------------- Consultas ----------------

    public static async Task<object> DetalleAsync(AppDbContext db, string id)
    {
        var pq = await Ventas.CargarAsync(db, db.Ventas.Where(v => v.Id == id));
        var v = pq.Ventas.Single();
        var p = await db.Productos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == v.ProductoId);
        var pallets = await db.Pallets.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Nombre);
        var lotes = await db.Lotes.AsNoTracking().Where(l => l.ProductoId == v.ProductoId).ToDictionaryAsync(l => l.Id, l => l.Condicion);
        var pl = v.PlataformaId is null ? null : await db.Plataformas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == v.PlataformaId);
        var tarifa = v.TarifaId is null ? null : await db.Tarifas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == v.TarifaId);
        var pub = v.PublicacionId is null ? null : await db.Publicaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == v.PublicacionId);
        return new
        {
            venta = v, folio = Ventas.Folio(v), producto = p is null ? null : new { p.Id, p.Codigo, p.Nombre },
            plataforma = pl?.Nombre, tarifa = tarifa is null ? null : new { tarifa.Id, tarifa.Nombre }, publicacion = pub,
            detalle = pq.Detalle[id].Select(d => new { d.Id, d.LoteId, d.PalletId, pallet = pallets.GetValueOrDefault(d.PalletId), condicion = lotes.GetValueOrDefault(d.LoteId),
                d.Cantidad, d.Devueltas, d.DevueltasAMerma, d.PrecioUnitario, d.CostoUnitario, d.CostoTotal, d.CostoOrigen }),
            gastos = pq.Gastos[id].OrderBy(g => g.CreadoUtc),
            incidencias = pq.Incidencias[id].OrderBy(i => i.CreadoUtc),
            resultado = Ventas.Resultado(pq, v),
        };
    }

    /// <summary>Resumen compacto por venta para las listas del cliente.</summary>
    public static async Task<object> ResultadosAsync(AppDbContext db)
    {
        var pq = await Ventas.CargarAsync(db);
        return pq.Ventas.Select(v =>
        {
            var r = Ventas.Resultado(pq, v);
            return new { ventaId = v.Id, r.IngresoBruto, r.IngresoNeto, r.GastosTotales, r.CostoVendido, r.Utilidad, r.MargenPct, r.Cobrada, r.EfectivoNeto, r.PorCobrarEstimado,
                estimados = pq.Gastos[v.Id].Any(g => g.Estimado), incidencias = pq.Incidencias[v.Id].Count(), abiertas = pq.Incidencias[v.Id].Count(i => i.Estado == "abierta") };
        }).ToList();
    }

    public record Totales(int Ventas, int Piezas, decimal IngresoBruto, decimal IvaTrasladado, decimal IngresoNeto, decimal Reembolsos,
        decimal Gastos, decimal CostoVendido, decimal Merma, decimal Utilidad, decimal? MargenPct,
        decimal Cobrado, decimal EfectivoNeto, decimal PorCobrar, int SinCobrar, decimal RetencionIsr, decimal RetencionIva);

    private static Totales Sumar(IEnumerable<(Venta V, ResultadoReal R)> xs)
    {
        var l = xs.ToList();
        var activas = l.Where(x => x.V.Estado != "cancelado").ToList();
        var neto = l.Sum(x => x.R.IngresoNeto - x.R.ReembolsosNetos);
        var util = l.Sum(x => x.R.Utilidad);
        return new Totales(activas.Count, activas.Sum(x => x.V.Cantidad - x.V.CantidadDevuelta), l.Sum(x => x.R.IngresoBruto), l.Sum(x => x.R.IvaTrasladado),
            l.Sum(x => x.R.IngresoNeto), l.Sum(x => x.R.Reembolsos), l.Sum(x => x.R.GastosTotales), l.Sum(x => x.R.CostoVendido), l.Sum(x => x.R.CostoMerma),
            util, neto > 0 ? Math.Round(util / neto * 100m, 2, MidpointRounding.AwayFromZero) : null,
            l.Sum(x => x.R.ImporteCobrado), l.Sum(x => x.R.EfectivoNeto), l.Sum(x => x.R.PorCobrarEstimado), activas.Count(x => !x.R.Cobrada),
            l.Sum(x => x.R.RetencionIsr), l.Sum(x => x.R.RetencionIva));
    }

    /// <summary>
    /// Rentabilidad real con costos históricos. Separa: ingresos, utilidad comercial, flujo de efectivo (solo lo cobrado)
    /// y capital recuperado por pallet. El resultado fiscal no se calcula mientras el régimen esté pendiente.
    /// </summary>
    public static async Task<object> RentabilidadRealAsync(AppDbContext db, DateOnly? desde, DateOnly? hasta, string? plataformaId, string? productoId, string? palletId)
    {
        var pq = await Ventas.CargarAsync(db);
        var todos = pq.Ventas.Select(v => (V: v, R: Ventas.Resultado(pq, v))).ToList();
        var filtradas = todos.Where(x => (desde is null || x.V.Fecha >= desde) && (hasta is null || x.V.Fecha <= hasta)
            && (plataformaId is null || x.V.PlataformaId == plataformaId) && (productoId is null || x.V.ProductoId == productoId)
            && (palletId is null || pq.Detalle[x.V.Id].Any(d => d.PalletId == palletId))).ToList();
        var pls = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
        var prods = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var pallets = await db.Pallets.AsNoTracking().OrderBy(p => p.FechaCompra).ToListAsync();
        var fiscal = await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new ConfiguracionFiscal();

        // Por pallet: cada venta se reparte entre sus renglones en proporción a su importe; el costo es exacto por renglón.
        var porPallet = new Dictionary<string, (List<decimal> Util, decimal Efectivo, decimal Costo, int Piezas)>();
        Dictionary<string, decimal> Reparto(Venta v, decimal monto) =>
            CalculoVenta.RepartirPorPeso(monto, pq.Detalle[v.Id].Select(d => (d.PalletId, d.Cantidad * d.PrecioUnitario)).ToList());
        var capital = new Dictionary<string, decimal>();
        foreach (var (v, r) in todos.Where(x => x.R.Cobrada))   // capital recuperado: acumulado, solo lo cobrado
            foreach (var kv in Reparto(v, r.EfectivoNeto)) capital[kv.Key] = capital.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (var (v, r) in filtradas)
        {
            var det = pq.Detalle[v.Id].ToList();
            if (det.Count == 0) continue;
            var cancelada = v.Estado == "cancelado";
            var costoPorPallet = det.GroupBy(d => d.PalletId).ToDictionary(g => g.Key,
                g => cancelada ? 0m : g.Sum(d => Dinero.Centavos((d.Cantidad - d.Devueltas + d.DevueltasAMerma) * d.CostoUnitario)));
            var sinCosto = Reparto(v, r.Utilidad + r.CostoVendido + r.CostoMerma);
            var efectivo = Reparto(v, r.EfectivoNeto);
            foreach (var g in det.GroupBy(d => d.PalletId))
            {
                var a = porPallet.GetValueOrDefault(g.Key, ([], 0m, 0m, 0));
                a.Util.Add(sinCosto.GetValueOrDefault(g.Key) - costoPorPallet[g.Key]);
                porPallet[g.Key] = (a.Util, a.Efectivo + efectivo.GetValueOrDefault(g.Key), a.Costo + costoPorPallet[g.Key],
                    a.Piezas + (cancelada ? 0 : g.Sum(d => d.Cantidad - d.Devueltas)));
            }
        }

        return new
        {
            filtros = new { desde, hasta, plataformaId, productoId, palletId },
            totales = Sumar(filtradas),
            fiscal = new
            {
                regimen = fiscal.Regimen, pendiente = fiscal.Regimen == "pendiente",
                aviso = fiscal.Regimen == "pendiente" ? "Régimen fiscal pendiente: no se estima el resultado fiscal; se muestran el IVA y las retenciones registrados." : null,
            },
            porPlataforma = filtradas.GroupBy(x => x.V.PlataformaId ?? "").Select(g => new
            {
                plataformaId = g.Key == "" ? null : g.Key,
                plataforma = g.Key == "" ? "Sin plataforma asignada" : pls.GetValueOrDefault(g.Key) ?? g.Key,
                totales = Sumar(g),
            }).OrderByDescending(x => x.totales.Utilidad).ToList(),
            porProducto = filtradas.GroupBy(x => x.V.ProductoId).Select(g => new
            {
                productoId = g.Key, producto = prods.TryGetValue(g.Key, out var p) ? p.Codigo + " · " + p.Nombre : g.Key, totales = Sumar(g),
            }).OrderByDescending(x => x.totales.Utilidad).ToList(),
            porPallet = pallets.Where(p => palletId is null || p.Id == palletId).Select(p =>
            {
                var a = porPallet.GetValueOrDefault(p.Id, ([], 0m, 0m, 0));
                var rec = capital.GetValueOrDefault(p.Id);
                return new
                {
                    palletId = p.Id, pallet = p.Nombre, inversion = p.InversionTotal, piezasVendidas = a.Piezas, costoVendido = a.Costo,
                    utilidad = a.Util.Sum(), efectivoPeriodo = a.Efectivo,
                    capitalRecuperado = rec,
                    capitalRecuperadoPct = p.InversionTotal > 0 ? Math.Round(rec / p.InversionTotal * 100m, 2, MidpointRounding.AwayFromZero) : (decimal?)null,
                    faltaPorRecuperar = Math.Max(0m, p.InversionTotal - rec),
                };
            }).ToList(),
            ventas = filtradas.OrderByDescending(x => x.V.Fecha).ThenByDescending(x => x.V.Folio).Select(x => new
            {
                x.V.Id, folio = Ventas.Folio(x.V), x.V.Fecha, x.V.Estado, plataforma = x.V.PlataformaId is null ? (x.V.Canal == "" ? null : x.V.Canal) : pls.GetValueOrDefault(x.V.PlataformaId),
                producto = prods.TryGetValue(x.V.ProductoId, out var p) ? p.Nombre : "", x.V.Cantidad, x.V.CostoOrigen, resultado = x.R,
            }).ToList(),
        };
    }
}
