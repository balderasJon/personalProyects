using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

public record PalletIn(string? Nombre, string? Proveedor, DateOnly? FechaCompra, string? PaisOrigen, string? Moneda,
    decimal CostoOriginal, decimal TipoCambio, string? TipoCambioFuente, string? Estatus, string? MetodoCosteo, string? ModoMerma, string? Notas,
    decimal? ValorMercadoEstimado = null);
public record PalletGastoIn(DateOnly? Fecha, string? Tipo, string? Concepto, decimal MontoMxn, bool IncluidoEnCosto);
public record LoteIn(string? PalletId, string? Condicion, int Cantidad, bool? Comercializable, string? Tratamiento, decimal? ValorRelativoUnitario, string? Notas);
public record LoteActualizarIn(bool Comercializable, string? Tratamiento, decimal? ValorRelativoUnitario, string? Notas);
public record AjusteIn(int Cantidad, string? Motivo);
public record ReclasificarIn(int Cantidad, string? Condicion, string? Motivo);
public record CosteoIn(string? Metodo, string? ModoMerma, Dictionary<string, decimal>? CostosManuales, string? Notas);

/// <summary>Fase 1: pallets, gastos de pallet, lotes de inventario y costeo.</summary>
public static class PalletsApi
{
    private static string T(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);

    public static void MapPallets(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---------- Pallets ----------
        api.MapGet("/pallets", async (AppDbContext db) =>
        {
            var pallets = await db.Pallets.AsNoTracking().OrderByDescending(p => p.FechaCompra).ToListAsync();
            var lotes = await db.Lotes.AsNoTracking().ToListAsync();
            var vigentes = await db.CosteoCorridas.AsNoTracking().Where(c => c.Vigente).ToListAsync();
            var res = new List<object>();
            foreach (var p in pallets)
            {
                var ls = lotes.Where(l => l.PalletId == p.Id).ToList();
                var c = vigentes.FirstOrDefault(x => x.PalletId == p.Id);
                var huella = c is null ? null : (await Inventario.EntradaAsync(db, p.Id)).Huella;
                res.Add(new
                {
                    pallet = p,
                    piezas = ls.Sum(l => l.Cantidad),
                    comercializables = ls.Where(l => l.Comercializable).Sum(l => l.Cantidad),
                    porCondicion = Catalogos.Condiciones.ToDictionary(x => x.Codigo, x => ls.Where(l => l.Condicion == x.Codigo).Sum(l => l.Cantidad)),
                    productos = ls.Select(l => l.ProductoId).Distinct().Count(),
                    costeo = c is null ? null : new { c.Id, c.FechaUtc, c.Metodo, c.ModoMerma, c.TotalComercializable, c.TotalMerma, desactualizado = c.Huella != huella },
                });
            }
            return Results.Ok(res);
        });

        api.MapGet("/pallets/{id}", async (string id, AppDbContext db) =>
        {
            var p = await db.Pallets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound(new Error("El pallet no existe."));
            var entrada = await Inventario.EntradaAsync(db, id);
            var corridas = await db.CosteoCorridas.AsNoTracking().Where(c => c.PalletId == id).ToListAsync();
            var vigente = corridas.FirstOrDefault(c => c.Vigente);
            var costos = vigente is null ? [] : await db.CostosLote.AsNoTracking().Where(c => c.CorridaId == vigente.Id).ToListAsync();
            return Results.Ok(new
            {
                pallet = p,
                gastos = await db.PalletGastos.AsNoTracking().Where(g => g.PalletId == id).OrderBy(g => g.Fecha).ToListAsync(),
                lotes = entrada.Lotes.Select(x => new
                {
                    lote = x.Lote,
                    producto = new { x.Producto.Id, x.Producto.Codigo, x.Producto.Nombre, x.Producto.Categoria, x.Producto.PrecioMexico },
                    valorRelativo = x.ValorRelativo,
                    costo = costos.FirstOrDefault(c => c.LoteId == x.Lote.Id),
                }),
                costeo = vigente is null ? null : new { corrida = vigente, desactualizado = vigente.Huella != entrada.Huella },
                historial = corridas.OrderByDescending(c => c.FechaUtc).Select(c => new { c.Id, c.FechaUtc, c.Usuario, c.Metodo, c.ModoMerma, c.TotalDistribuible, c.TotalComercializable, c.TotalMerma, c.Vigente, c.Notas }),
            });
        });

        api.MapPost("/pallets", (PalletIn i, AppDbContext db) => Api.Bloqueado(() => GuardarPallet(db, null, i)));
        api.MapPut("/pallets/{id}", (string id, PalletIn i, AppDbContext db) => Api.Bloqueado(() => GuardarPallet(db, id, i)));
        api.MapDelete("/pallets/{id}", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var p = await db.Pallets.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("El pallet no existe."));
            if (await db.Lotes.AnyAsync(l => l.PalletId == id)) return Results.Conflict(new Error("El pallet tiene juguetes registrados; no se puede eliminar."));
            if (await db.CosteoCorridas.AnyAsync(c => c.PalletId == id)) return Results.Conflict(new Error("El pallet tiene costeos en su historial; no se puede eliminar."));
            db.PalletGastos.RemoveRange(await db.PalletGastos.Where(g => g.PalletId == id).ToListAsync());
            db.Pallets.Remove(p);
            await Inventario.SincronizarInversionGlobalAsync(db);   // excluye el pallet borrado; todo se guarda en una transacción
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Gastos del pallet ----------
        api.MapPost("/pallets/{id}/gastos", (string id, PalletGastoIn i, AppDbContext db) => Api.Bloqueado(() => GuardarGasto(db, id, null, i)));
        api.MapPut("/pallet-gastos/{gid}", (string gid, PalletGastoIn i, AppDbContext db) => Api.Bloqueado(() => GuardarGasto(db, null, gid, i)));
        api.MapDelete("/pallet-gastos/{gid}", (string gid, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var g = await db.PalletGastos.FindAsync(gid);
            if (g is null) return Results.NotFound(new Error("El gasto ya no existe."));
            db.PalletGastos.Remove(g);
            var p = (await db.Pallets.FindAsync(g.PalletId))!;
            await Inventario.RecalcularPalletAsync(db, p);
            await Inventario.SincronizarInversionGlobalAsync(db);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Lotes ----------
        api.MapGet("/productos/{id}/lotes", async (string id, AppDbContext db) =>
        {
            var lotes = await db.Lotes.AsNoTracking().Where(l => l.ProductoId == id).ToListAsync();
            var ids = lotes.Select(l => l.Id).ToList();
            var vigentes = await db.CosteoCorridas.AsNoTracking().Where(c => c.Vigente).Select(c => c.Id).ToListAsync();
            var costos = await db.CostosLote.AsNoTracking().Where(c => ids.Contains(c.LoteId) && vigentes.Contains(c.CorridaId)).ToListAsync();
            var movs = await db.Movimientos.AsNoTracking().Where(m => ids.Contains(m.LoteId)).OrderByDescending(m => m.Id).Take(200).ToListAsync();
            var pallets = await db.Pallets.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            return Results.Ok(new
            {
                lotes = lotes.Select(l => new { lote = l, pallet = pallets.GetValueOrDefault(l.PalletId), costo = costos.FirstOrDefault(c => c.LoteId == l.Id) }),
                movimientos = movs,
            });
        });

        api.MapPost("/productos/{id}/lotes", (string id, LoteIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            if (await db.Productos.FindAsync(id) is null) return Results.NotFound(new Error("El juguete ya no existe."));
            if (string.IsNullOrEmpty(i.PalletId)) return Results.BadRequest(new Error("Elige el pallet."));
            if (i.Cantidad <= 0) return Results.BadRequest(new Error("La cantidad debe ser mayor a cero."));
            var cond = i.Condicion ?? "nuevo_sellado";
            if ((await Inventario.LotesDeAsync(db, id)).Any(l => l.PalletId == i.PalletId && l.Condicion == cond))
                return Results.Conflict(new Error("Ya existe un lote de este juguete con ese pallet y condición. Usa “Ajustar” para sumar piezas."));
            var lote = await Inventario.LoteAsync(db, id, i.PalletId, cond, Hoy);
            AplicarDatosLote(lote, i.Comercializable ?? lote.Comercializable, i.Tratamiento, i.ValorRelativoUnitario, i.Notas);
            Inventario.Mover(db, lote, i.Cantidad, "recepcion", "Alta de lote");
            await Inventario.SincronizarProductoAsync(db, id);
            await db.GuardarAsync();
            return Results.Ok(lote);
        }));

        api.MapPut("/lotes/{id}", (string id, LoteActualizarIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var lote = await db.Lotes.FindAsync(id);
            if (lote is null) return Results.NotFound(new Error("El lote ya no existe."));
            AplicarDatosLote(lote, i.Comercializable, i.Tratamiento, i.ValorRelativoUnitario, i.Notas);
            await Inventario.SincronizarProductoAsync(db, lote.ProductoId);
            await db.GuardarAsync();
            return Results.Ok(lote);
        }));

        api.MapPost("/lotes/{id}/ajuste", (string id, AjusteIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var lote = await db.Lotes.FindAsync(id);
            if (lote is null) return Results.NotFound(new Error("El lote ya no existe."));
            if (i.Cantidad == 0) return Results.BadRequest(new Error("Indica cuántas piezas suman (positivo) o restan (negativo)."));
            if (string.IsNullOrWhiteSpace(i.Motivo)) return Results.BadRequest(new Error("Indica el motivo del ajuste."));
            Inventario.Mover(db, lote, i.Cantidad, "ajuste", i.Motivo!);
            await Inventario.SincronizarProductoAsync(db, lote.ProductoId);
            await ValidarNoMenosQueVendidasAsync(db, lote.ProductoId);
            await Ventas.RevisarPublicacionesAsync(db, lote.ProductoId, null, 0);
            await db.GuardarAsync();
            return Results.Ok(lote);
        }));

        api.MapPost("/lotes/{id}/reclasificar", (string id, ReclasificarIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var lote = await db.Lotes.FindAsync(id);
            if (lote is null) return Results.NotFound(new Error("El lote ya no existe."));
            var destino = await Inventario.ReclasificarAsync(db, lote, i.Cantidad, i.Condicion ?? "", string.IsNullOrWhiteSpace(i.Motivo) ? "Cambio de condición" : i.Motivo!);
            await Inventario.SincronizarProductoAsync(db, lote.ProductoId);
            await Ventas.ValidarLotesAsync(db, lote.ProductoId);   // no se reclasifican piezas ya vendidas
            await Ventas.RevisarPublicacionesAsync(db, lote.ProductoId, null, 0);
            await db.GuardarAsync();
            return Results.Ok(new { origen = lote, destino });
        }));

        // ---------- Costeo ----------
        api.MapPost("/pallets/{id}/costeo/simular", async (string id, CosteoIn i, AppDbContext db) =>
        {
            try
            {
                var (entrada, r, metodo, modo) = await CalcularAsync(db, id, i);
                return Results.Ok(Respuesta(entrada, r, metodo, modo));
            }
            catch (ErrorNegocio e) { return Results.Json(new Error(e.Message), statusCode: e.Estado); }
        });

        api.MapPost("/pallets/{id}/costeo/aplicar", (string id, CosteoIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var (entrada, r, metodo, modo) = await CalcularAsync(db, id, i);
            if (!r.Ok) return Results.BadRequest(new { mensaje = string.Join(" ", r.Errores), errores = r.Errores });
            foreach (var c in await db.CosteoCorridas.Where(c => c.PalletId == id && c.Vigente).ToListAsync()) c.Vigente = false;
            var corrida = new CosteoCorrida
            {
                Id = Inventario.NuevoId(), PalletId = id, Usuario = db.UsuarioActual, Metodo = metodo, ModoMerma = modo,
                TotalDistribuible = r.TotalDistribuible, TotalComercializable = r.TotalComercializable, TotalMerma = r.TotalMerma,
                CostoAbsorbidoPorMerma = r.CostoAbsorbidoPorMerma, CentavosAjustados = r.CentavosAjustados,
                Vigente = true, Huella = entrada.Huella, Notas = T(i.Notas, 300),
            };
            db.CosteoCorridas.Add(corrida);
            foreach (var (l, p, v) in entrada.Lotes)
            {
                var c = r.Lotes.Single(x => x.LoteId == l.Id);
                db.CostosLote.Add(new CostoLote
                {
                    CorridaId = corrida.Id, LoteId = l.Id, ProductoId = p.Id, Condicion = l.Condicion, Comercializable = l.Comercializable,
                    Cantidad = l.Cantidad, ValorRelativoUnitario = v, Peso = c.Peso, CostoTotal = c.CostoTotal, CostoUnitario = c.CostoUnitario, EsMerma = c.EsMerma,
                });
            }
            var pallet = (await db.Pallets.FindAsync(id))!;
            pallet.MetodoCosteo = metodo;
            pallet.ModoMerma = modo;
            await db.GuardarAsync();
            return Results.Ok(new { corrida, resultado = Respuesta(entrada, r, metodo, modo) });
        }));

        api.MapGet("/costeo/corridas/{id}", async (string id, AppDbContext db) =>
        {
            var c = await db.CosteoCorridas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound(new Error("El costeo no existe."));
            var costos = await db.CostosLote.AsNoTracking().Where(x => x.CorridaId == id).ToListAsync();
            var ids = costos.Select(x => x.ProductoId).Distinct().ToList();
            var productos = await db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Codigo, p.Nombre });
            return Results.Ok(new { corrida = c, costos = costos.Select(x => new { costo = x, producto = productos.GetValueOrDefault(x.ProductoId) }) });
        });
    }

    private static async Task<IResult> GuardarPallet(AppDbContext db, string? id, PalletIn i)
    {
        var nombre = T(i.Nombre, 80);
        if (nombre.Length == 0) return Results.BadRequest(new Error("El nombre del pallet es obligatorio."));
        var moneda = (i.Moneda ?? "MXN").ToUpperInvariant();
        if (!Catalogos.Monedas.Contains(moneda)) return Results.BadRequest(new Error("Moneda no válida (MXN o USD)."));
        if (i.CostoOriginal < 0) return Results.BadRequest(new Error("El costo no puede ser negativo."));
        if (moneda != "MXN" && (i.TipoCambio <= 0 || i.TipoCambio > 1000)) return Results.BadRequest(new Error("Captura el tipo de cambio aplicado (pesos por dólar)."));
        var estatus = i.Estatus ?? "recepcion";
        if (!Catalogos.EstatusPallet.Contains(estatus)) return Results.BadRequest(new Error("Estatus no válido."));
        var metodo = i.MetodoCosteo ?? "proporcional";
        if (!Catalogos.MetodosCosteo.Contains(metodo)) return Results.BadRequest(new Error("Método de costeo no válido."));
        var modo = i.ModoMerma ?? "absorber";
        if (!Catalogos.ModosMerma.Contains(modo)) return Results.BadRequest(new Error("Modo de merma no válido."));
        if (await db.Pallets.AnyAsync(p => p.Id != id && p.Nombre == nombre)) return Results.Conflict(new Error($"Ya existe un pallet llamado {nombre}."));

        Pallet? p;
        if (id is null) { p = new Pallet { Id = Inventario.NuevoId() }; db.Pallets.Add(p); }
        else if ((p = await db.Pallets.FindAsync(id)) is null) return Results.NotFound(new Error("El pallet no existe."));
        p.Nombre = nombre;
        p.Proveedor = T(i.Proveedor, 80);
        p.FechaCompra = i.FechaCompra ?? Hoy;
        p.PaisOrigen = T(i.PaisOrigen, 40);
        p.Moneda = moneda;
        p.CostoOriginal = Dinero.Centavos(i.CostoOriginal);
        p.TipoCambio = moneda == "MXN" ? 1m : Math.Round(i.TipoCambio, 4, MidpointRounding.AwayFromZero);
        p.TipoCambioFuente = moneda == "MXN" ? "" : (i.TipoCambioFuente is "DOF" or "Manual" ? i.TipoCambioFuente : "Manual");
        p.Estatus = estatus;
        p.MetodoCosteo = metodo;
        p.ModoMerma = modo;
        p.Notas = T(i.Notas, 500);
        if (i.ValorMercadoEstimado < 0) return Results.BadRequest(new Error("El valor de mercado estimado no puede ser negativo."));
        p.ValorMercadoEstimado = i.ValorMercadoEstimado is > 0 ? Dinero.Centavos(i.ValorMercadoEstimado.Value) : null;
        await Inventario.RecalcularPalletAsync(db, p);
        await Inventario.SincronizarInversionGlobalAsync(db);
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/pallets/{p.Id}", p) : Results.Ok(p);
    }

    private static async Task<IResult> GuardarGasto(AppDbContext db, string? palletId, string? gastoId, PalletGastoIn i)
    {
        if (i.MontoMxn < 0) return Results.BadRequest(new Error("El monto no puede ser negativo."));
        if (string.IsNullOrWhiteSpace(i.Concepto)) return Results.BadRequest(new Error("Indica el concepto del gasto."));
        PalletGasto? g;
        if (gastoId is null)
        {
            if (await db.Pallets.FindAsync(palletId) is null) return Results.NotFound(new Error("El pallet no existe."));
            g = new PalletGasto { Id = Inventario.NuevoId(), PalletId = palletId! };
            db.PalletGastos.Add(g);
        }
        else if ((g = await db.PalletGastos.FindAsync(gastoId)) is null) return Results.NotFound(new Error("El gasto ya no existe."));
        g.Fecha = i.Fecha ?? Hoy;
        g.Tipo = Catalogos.TiposGastoPallet.Contains(i.Tipo) ? i.Tipo! : "Otro";
        g.Concepto = T(i.Concepto, 120);
        g.MontoMxn = Dinero.Centavos(i.MontoMxn);
        g.IncluidoEnCosto = i.IncluidoEnCosto;
        var p = (await db.Pallets.FindAsync(g.PalletId))!;
        await Inventario.RecalcularPalletAsync(db, p);
        await Inventario.SincronizarInversionGlobalAsync(db);
        await db.GuardarAsync();
        return Results.Ok(new { gasto = g, pallet = p });
    }

    private static void AplicarDatosLote(InventarioLote lote, bool comercializable, string? tratamiento, decimal? valorRelativo, string? notas)
    {
        if (lote.Condicion == "no_comercializable" && comercializable)
            throw new ErrorNegocio("Un lote “No comercializable” no puede marcarse como comercializable. Cambia primero su condición.");
        if (valorRelativo is < 0) throw new ErrorNegocio("El valor relativo no puede ser negativo.");
        lote.Comercializable = comercializable;
        lote.Tratamiento = comercializable ? "" : (Catalogos.Tratamientos.Contains(tratamiento) ? tratamiento! : "");
        lote.ValorRelativoUnitario = valorRelativo is > 0 ? Math.Round(valorRelativo.Value, 2, MidpointRounding.AwayFromZero) : null;
        lote.Notas = T(notas, 300);
        lote.ActualizadoUtc = DateTime.UtcNow;
    }

    public static async Task ValidarNoMenosQueVendidasAsync(AppDbContext db, string productoId)
    {
        var p = (await db.Productos.FindAsync(productoId))!;
        var vend = await Inventario.VendidasAsync(db, productoId);
        if (p.Cantidad < vend) throw new ErrorNegocio($"El juguete quedaría con {p.Cantidad} pieza(s) y ya tiene {vend} vendida(s) o apartada(s).", 409);
        await Ventas.ValidarLotesAsync(db, productoId);
    }

    private static async Task<(Inventario.EntradaCosteo, ResultadoCosteo, string, string)> CalcularAsync(AppDbContext db, string palletId, CosteoIn i)
    {
        var entrada = await Inventario.EntradaAsync(db, palletId);
        var metodo = i.Metodo ?? entrada.Pallet.MetodoCosteo;
        var modo = i.ModoMerma ?? entrada.Pallet.ModoMerma;
        var lotes = entrada.Lotes.Select(x => new LoteCosteo(x.Lote.Id, x.Lote.Cantidad, x.Lote.Comercializable, x.ValorRelativo,
            i.CostosManuales is not null && i.CostosManuales.TryGetValue(x.Lote.Id, out var m) ? m : null)).ToList();
        var r = DistribucionCostos.Calcular(entrada.Pallet.InversionTotal, lotes, Inventario.Metodo(metodo), Inventario.Modo(modo));
        return (entrada, r, metodo, modo);
    }

    private static object Respuesta(Inventario.EntradaCosteo e, ResultadoCosteo r, string metodo, string modo) => new
    {
        ok = r.Ok, errores = r.Errores, metodo, modoMerma = modo, huella = e.Huella,
        r.TotalDistribuible, r.TotalComercializable, r.TotalMerma, r.CostoAbsorbidoPorMerma, r.CentavosAjustados, r.DiferenciaCuadre,
        lotes = e.Lotes.Select(x => new
        {
            loteId = x.Lote.Id, producto = new { x.Producto.Id, x.Producto.Codigo, x.Producto.Nombre },
            x.Lote.Condicion, x.Lote.Comercializable, x.Lote.Cantidad, valorRelativo = x.ValorRelativo,
            resultado = r.Lotes.FirstOrDefault(l => l.LoteId == x.Lote.Id),
        }),
    };
}
