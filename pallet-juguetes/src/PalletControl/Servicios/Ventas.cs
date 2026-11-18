using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;

namespace PalletControl.Servicios;

public record GastoVentaIn(string? Tipo, string? Concepto, decimal Monto, bool DeducidoPorPlataforma, bool Estimado = false);

/// <summary>Datos para registrar o modificar una venta.</summary>
public record VentaDatos(string ProductoId, int Cantidad, decimal Precio, string Estado, DateOnly Fecha,
    string Canal, string Cliente, string Notas,
    string? PlataformaId, string? PublicacionId, string? LoteId, decimal? PrecioPublicado, decimal Descuento,
    List<GastoVentaIn>? Gastos, decimal? RetencionIsr, decimal? RetencionIva, decimal? ImporteCobrado, DateOnly? FechaCobro);

/// <summary>
/// Reglas de ventas de la Fase 3.
/// - Cada venta toma piezas de lotes: el lote elegido o, si no, los lotes vendibles del pallet más antiguo primero.
/// - InventarioLote.Cantidad sigue siendo la cantidad que se costea (piezas recibidas); las ventas no la cambian.
///   Existencia de un lote = Cantidad − piezas vendidas netas (vendidas − devueltas) de ventas no canceladas.
///   El kárdex registra las salidas por venta (venta, cancelacion_venta, devolucion) sin tocar Cantidad.
/// - El costo de cada renglón se congela al registrar la venta (costo vigente del lote).
/// - Una venta solo está cobrada cuando se registra el importe que efectivamente llegó.
/// </summary>
public static class Ventas
{
    public static readonly string[] TiposGasto = ["comision", "medio_pago", "cargo_fijo", "logistica", "envio", "almacenamiento",
        "preparacion", "empaque", "publicidad", "devolucion", "reclamacion", "otro"];
    public static readonly string[] EstatusPublicacion = ["pendiente", "publicado", "pausado", "vendido", "cancelado", "eliminado"];
    /// <summary>Movimientos del kárdex que no cambian la cantidad costeable del lote.</summary>
    public static readonly string[] MovimientosDeVenta = ["venta", "cancelacion_venta", "devolucion"];

    private static string T(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static decimal C(decimal v) => Dinero.Centavos(v);
    public static string Folio(Venta v) => $"V-{v.Folio:00000}";

    // ---------------- Existencias ----------------

    /// <summary>Entidades rastreadas (incluye cambios aún no guardados) más las nuevas.</summary>
    private static async Task<List<TEnt>> Vivos<TEnt>(AppDbContext db, IQueryable<TEnt> q, Func<TEnt, bool> filtro) where TEnt : class
    {
        var guardados = await q.ToListAsync();
        return guardados.Concat(db.Set<TEnt>().Local.Where(filtro)).Distinct()
            .Where(x => db.Entry(x).State != EntityState.Deleted && filtro(x)).ToList();
    }

    /// <summary>Piezas vendidas netas por lote (ventas no canceladas).</summary>
    public static async Task<Dictionary<string, int>> VendidasPorLoteAsync(AppDbContext db, string productoId)
    {
        var detalles = await Vivos(db, db.VentaDetalles.Where(d => d.ProductoId == productoId), d => d.ProductoId == productoId);
        var ids = detalles.Select(d => d.VentaId).Distinct().ToList();
        var ventas = (await Vivos(db, db.Ventas.Where(v => ids.Contains(v.Id)), v => ids.Contains(v.Id))).ToDictionary(v => v.Id);
        return detalles.Where(d => ventas.TryGetValue(d.VentaId, out var v) && v.Estado != "cancelado")
            .GroupBy(d => d.LoteId).ToDictionary(g => g.Key, g => g.Sum(d => d.Cantidad - d.Devueltas));
    }

    /// <summary>Existencia por lote, en el orden en que se venden: vendibles del pallet más antiguo primero.</summary>
    public static async Task<List<(InventarioLote Lote, int Disponibles)>> ExistenciasAsync(AppDbContext db, string productoId)
    {
        var lotes = await Inventario.LotesDeAsync(db, productoId);
        var vend = await VendidasPorLoteAsync(db, productoId);
        var ids = lotes.Select(l => l.PalletId).Distinct().ToList();
        var fechas = await db.Pallets.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FechaCompra);
        return lotes.OrderByDescending(l => l.Comercializable).ThenBy(l => fechas.GetValueOrDefault(l.PalletId)).ThenBy(l => l.FechaIngreso).ThenBy(l => l.Id)
            .Select(l => (l, l.Cantidad - vend.GetValueOrDefault(l.Id))).ToList();
    }

    /// <summary>Piezas vendibles disponibles (lotes comercializables).</summary>
    public static async Task<int> DisponibleVentaAsync(AppDbContext db, string productoId) =>
        (await ExistenciasAsync(db, productoId)).Where(x => x.Lote.Comercializable).Sum(x => Math.Max(0, x.Disponibles));

    /// <summary>Ningún lote puede quedar con menos piezas que las ya vendidas de él.</summary>
    public static async Task ValidarLotesAsync(AppDbContext db, string productoId)
    {
        foreach (var (l, d) in await ExistenciasAsync(db, productoId))
            if (d < 0) throw new ErrorNegocio($"El lote quedaría con {l.Cantidad} pieza(s) y ya tiene {l.Cantidad - d} vendida(s) o apartada(s).", 409);
    }

    // ---------------- Costo histórico ----------------

    private sealed class CostosVigentes
    {
        public required Dictionary<string, CostoLote> PorLote;
        public required HashSet<string> Desactualizados;
    }

    private static async Task<CostosVigentes> CostosAsync(AppDbContext db, IEnumerable<string> palletIds)
    {
        var ids = palletIds.Distinct().ToList();
        var corridas = await db.CosteoCorridas.Where(c => c.Vigente && ids.Contains(c.PalletId)).ToListAsync();
        var des = new HashSet<string>();
        foreach (var c in corridas)
            if (c.Huella != (await Inventario.EntradaAsync(db, c.PalletId)).Huella) des.Add(c.PalletId);
        var cids = corridas.Select(c => c.Id).ToList();
        var costos = await db.CostosLote.Where(c => cids.Contains(c.CorridaId)).ToListAsync();
        return new CostosVigentes { PorLote = costos.GroupBy(c => c.LoteId).ToDictionary(g => g.Key, g => g.First()), Desactualizados = des };
    }

    private static string OrigenTotal(IEnumerable<string> origenes)
    {
        var o = origenes.Distinct().ToList();
        return o.Count == 0 ? "sin_costeo" : o.Count == 1 ? o[0] : "mixto";
    }

    // ---------------- Gastos estimados con la tarifa ----------------

    /// <summary>
    /// Gastos de la venta precargados con la tarifa vigente de la plataforma en la fecha de la venta.
    /// Porcentajes sobre el importe cobrado al cliente; cargos fijos por pieza (como en el análisis de la Fase 2).
    /// La reserva para devoluciones no se precarga: es una provisión, no un gasto de la venta.
    /// </summary>
    public static async Task<(TarifaPlataforma? Tarifa, List<VentaGasto> Gastos, decimal RetIsr, decimal RetIva)> EstimarAsync(
        AppDbContext db, Producto p, string plataformaId, DateOnly fecha, decimal precioUnitario, int cantidad, decimal bruto, decimal factor)
    {
        var tarifas = await db.Tarifas.AsNoTracking().Where(t => t.PlataformaId == plataformaId && t.Activa).ToListAsync();
        var lado = new[] { p.LargoCm, p.AnchoCm, p.AltoCm }.Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty().Max();
        var ctx = new ContextoTarifa(fecha, null, null, string.IsNullOrWhiteSpace(p.Categoria) ? null : p.Categoria, null, p.PesoKg, lado > 0 ? lado : null);
        var cand = SelectorTarifa.Candidatas(tarifas.Select(Rentabilidad.Regla), ctx, out _);
        var regla = SelectorTarifa.ParaPrecio(cand, precioUnitario) ?? cand.FirstOrDefault();
        if (regla is null) return (null, [], 0, 0);
        var t = tarifas.First(x => x.Id == regla.Id);
        var gastos = new List<VentaGasto>();
        void G(string tipo, string concepto, decimal monto, bool deducido)
        {
            monto = C(monto);
            if (monto > 0) gastos.Add(new VentaGasto { Id = Inventario.NuevoId(), Tipo = tipo, Concepto = concepto, Monto = monto, DeducidoPorPlataforma = deducido, Estimado = true });
        }
        G("comision", $"Comisión {t.ComisionPct:0.##} %", bruto * t.ComisionPct / 100m, true);
        G("medio_pago", $"Medio de pago {t.ComisionMedioPagoPct:0.##} %", bruto * t.ComisionMedioPagoPct / 100m, true);
        G("cargo_fijo", "Cargo fijo", t.CargoFijo * cantidad, true);
        G("logistica", "Costo logístico", t.CostoLogistico * cantidad, true);
        G("envio", "Envío absorbido", t.EnvioAbsorbido * cantidad, true);
        G("almacenamiento", "Almacenamiento", t.Almacenamiento * cantidad, true);
        G("preparacion", "Preparación", t.Preparacion * cantidad, false);
        G("empaque", "Empaque", t.Empaque * cantidad, false);
        G("publicidad", "Publicidad", t.PublicidadFija * cantidad + bruto * t.PublicidadPct / 100m, false);
        var neto = C(bruto * factor);
        return (t, gastos, C(neto * t.RetencionIsrPct / 100m), C((bruto - neto) * t.RetencionIvaPct / 100m));
    }

    private static VentaGasto NuevoGasto(string ventaId, GastoVentaIn g, string? incidenciaId = null)
    {
        var tipo = Ventas.TiposGasto.Contains(g.Tipo) ? g.Tipo! : "otro";
        if (g.Monto < 0) throw new ErrorNegocio("Los gastos no pueden ser negativos.");
        return new VentaGasto
        {
            Id = Inventario.NuevoId(), VentaId = ventaId, IncidenciaId = incidenciaId, Tipo = tipo,
            Concepto = T(string.IsNullOrWhiteSpace(g.Concepto) ? tipo : g.Concepto, 120), Monto = C(g.Monto),
            DeducidoPorPlataforma = g.DeducidoPorPlataforma, Estimado = g.Estimado,
        };
    }

    // ---------------- Registro y modificación ----------------

    private static async Task<int> SiguienteFolioAsync(AppDbContext db) =>
        Math.Max(await db.Ventas.MaxAsync(v => (int?)v.Folio) ?? 0, db.Ventas.Local.Select(v => v.Folio).DefaultIfEmpty(0).Max()) + 1;

    private static async Task<decimal> FactorAsync(AppDbContext db)
    {
        var f = await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new ConfiguracionFiscal();
        return Rentabilidad.Fiscal(f).FactorIngreso;
    }

    /// <summary>Toma las piezas de los lotes, congela su costo y registra la salida en el kárdex.</summary>
    private static async Task AsignarAsync(AppDbContext db, Venta v, string? loteId, string motivo)
    {
        var exist = await ExistenciasAsync(db, v.ProductoId);
        List<(string LoteId, int Cantidad)>? asign;
        if (loteId is not null)
        {
            var x = exist.FirstOrDefault(e => e.Lote.Id == loteId);
            if (x.Lote is null) throw new ErrorNegocio("El lote elegido no es de este juguete.");
            asign = CalculoVenta.AsignarLotes(v.Cantidad, [(loteId, x.Disponibles)]);
            if (asign is null) throw new ErrorNegocio($"El lote elegido solo tiene {Math.Max(0, x.Disponibles)} pieza(s) disponible(s).", 409);
        }
        else
        {
            var vendibles = exist.Where(e => e.Lote.Comercializable).Select(e => (e.Lote.Id, e.Disponibles)).ToList();
            asign = CalculoVenta.AsignarLotes(v.Cantidad, vendibles);
            if (asign is null)
            {
                var hay = vendibles.Sum(e => Math.Max(0, e.Disponibles));
                var p = await db.Productos.FindAsync(v.ProductoId);
                throw new ErrorNegocio(hay == 0 ? $"No hay existencias vendibles de {p?.Nombre}. Si está publicado en otros canales, páusalo."
                    : $"Solo hay {hay} pieza(s) vendible(s) de {p?.Nombre}.", 409);
            }
        }
        var lotes = exist.Select(e => e.Lote).ToDictionary(l => l.Id);
        var costos = await CostosAsync(db, asign.Select(a => lotes[a.LoteId].PalletId));
        decimal total = 0; var origenes = new List<string>();
        foreach (var (lid, cant) in asign)
        {
            var l = lotes[lid];
            var c = costos.PorLote.GetValueOrDefault(lid);
            var origen = c is null ? "sin_costeo" : costos.Desactualizados.Contains(l.PalletId) ? "desactualizado" : "costeo";
            var cu = c?.CostoUnitario ?? 0m;
            var ct = C(cu * cant);
            total += ct; origenes.Add(origen);
            db.VentaDetalles.Add(new VentaDetalle
            {
                Id = Inventario.NuevoId(), VentaId = v.Id, ProductoId = v.ProductoId, LoteId = lid, PalletId = l.PalletId, Cantidad = cant,
                PrecioUnitario = v.Precio, CostoUnitario = cu, CostoTotal = ct, CostoCorridaId = c?.CorridaId, CostoOrigen = origen,
            });
            Kardex(db, lid, -cant, "venta", motivo, Folio(v));
        }
        v.CostoTotal = total;
        v.CostoOrigen = OrigenTotal(origenes);
    }

    private static void Kardex(AppDbContext db, string loteId, int cantidad, string tipo, string motivo, string referencia)
    {
        if (cantidad == 0) return;
        db.Movimientos.Add(new MovimientoInventario { LoteId = loteId, Tipo = tipo, Cantidad = cantidad, Motivo = T(motivo, 200), Referencia = T(referencia, 60), Usuario = db.UsuarioActual });
    }

    /// <summary>Regresa al inventario las piezas aún vendidas de la venta (cancelación o edición).</summary>
    private static async Task LiberarAsync(AppDbContext db, Venta v, string motivo, bool quitarDetalle)
    {
        var det = await db.VentaDetalles.Where(d => d.VentaId == v.Id).ToListAsync();
        foreach (var d in det)
        {
            Kardex(db, d.LoteId, d.Cantidad - d.Devueltas, "cancelacion_venta", motivo, Folio(v));
            if (quitarDetalle) db.VentaDetalles.Remove(d);
        }
    }

    private static async Task<(Plataforma? Pl, Publicacion? Pub)> ValidarCanalAsync(AppDbContext db, VentaDatos d)
    {
        Publicacion? pub = null;
        var plataformaId = d.PlataformaId;
        if (d.PublicacionId is not null)
        {
            pub = await db.Publicaciones.FindAsync(d.PublicacionId) ?? throw new ErrorNegocio("La publicación elegida no existe.");
            if (pub.ProductoId != d.ProductoId) throw new ErrorNegocio("La publicación elegida es de otro juguete.");
            if (pub.Estatus is "eliminado" or "cancelado") throw new ErrorNegocio("La publicación elegida está cancelada o eliminada.");
            if (plataformaId is not null && plataformaId != pub.PlataformaId) throw new ErrorNegocio("La publicación es de otra plataforma.");
            plataformaId = pub.PlataformaId;
        }
        Plataforma? pl = null;
        if (plataformaId is not null) pl = await db.Plataformas.FindAsync(plataformaId) ?? throw new ErrorNegocio("La plataforma elegida no existe.");
        else if (!string.IsNullOrWhiteSpace(d.Canal))
        {
            // Clientes anteriores mandan el canal como texto: se asigna la plataforma si el nombre coincide.
            var canal = d.Canal.Trim();
            pl = (await db.Plataformas.ToListAsync()).FirstOrDefault(x => string.Equals(x.Nombre, canal, StringComparison.OrdinalIgnoreCase));
        }
        return (pl, pub);
    }

    private static void Validar(VentaDatos d)
    {
        if (!Catalogos.EstadosVenta.Contains(d.Estado)) throw new ErrorNegocio("Estado de venta no válido.");
        if (d.Cantidad < 1) throw new ErrorNegocio("La cantidad mínima es 1.");
        if (d.Precio < 0) throw new ErrorNegocio("El precio no puede ser negativo.");
        if (d.Descuento < 0) throw new ErrorNegocio("El descuento no puede ser negativo.");
        if (d.Descuento > d.Precio * d.Cantidad) throw new ErrorNegocio("El descuento no puede ser mayor que el importe de la venta.");
        if (d.ImporteCobrado is < 0) throw new ErrorNegocio("El importe cobrado no puede ser negativo.");
        if (d.RetencionIsr is < 0 || d.RetencionIva is < 0) throw new ErrorNegocio("Las retenciones no pueden ser negativas.");
    }

    public static async Task<Venta> RegistrarAsync(AppDbContext db, VentaDatos d)
    {
        Validar(d);
        if (d.Estado == "cancelado") throw new ErrorNegocio("Una venta nueva no puede registrarse cancelada.");
        var p = await db.Productos.FindAsync(d.ProductoId) ?? throw new ErrorNegocio("El juguete elegido ya no existe.");
        var (pl, pub) = await ValidarCanalAsync(db, d);
        var v = new Venta
        {
            Id = Inventario.NuevoId(), Folio = await SiguienteFolioAsync(db), ProductoId = p.Id, Cantidad = d.Cantidad, Precio = d.Precio,
            Estado = d.Estado, Fecha = d.Fecha, FechaPago = Catalogos.Cobrada(d.Estado) ? d.Fecha : null,
            Canal = T(pl?.Nombre ?? d.Canal, 40), Cliente = T(d.Cliente, 80), Notas = T(d.Notas, 400),
            PlataformaId = pl?.Id, PublicacionId = pub?.Id, Descuento = C(d.Descuento),
            PrecioPublicado = d.PrecioPublicado ?? pub?.PrecioPublicado ?? d.Precio, FactorIngreso = await FactorAsync(db),
        };
        db.Ventas.Add(v);
        await AsignarAsync(db, v, d.LoteId, "Venta");
        await AplicarGastosYRetencionesAsync(db, v, p, d, nueva: true);
        if (d.ImporteCobrado is { } imp) { v.ImporteCobrado = C(imp); v.FechaCobro = d.FechaCobro ?? d.Fecha; }
        await RevisarPublicacionesAsync(db, p.Id, v, vendidas: v.Cantidad);
        return v;
    }

    private static async Task AplicarGastosYRetencionesAsync(AppDbContext db, Venta v, Producto p, VentaDatos d, bool nueva)
    {
        var bruto = C(v.Precio * v.Cantidad - v.Descuento);
        if (d.Gastos is null && nueva && v.PlataformaId is not null)
        {
            var (t, gastos, isr, iva) = await EstimarAsync(db, p, v.PlataformaId, v.Fecha, v.Precio, v.Cantidad, bruto, v.FactorIngreso);
            v.TarifaId = t?.Id;
            foreach (var g in gastos) { g.VentaId = v.Id; db.VentaGastos.Add(g); }
            v.RetencionIsr = d.RetencionIsr ?? isr;
            v.RetencionIva = d.RetencionIva ?? iva;
            return;
        }
        if (d.Gastos is not null)
        {
            // Se reemplazan los gastos propios de la venta; los de devoluciones o reclamaciones se conservan.
            db.VentaGastos.RemoveRange(await db.VentaGastos.Where(g => g.VentaId == v.Id && g.IncidenciaId == null).ToListAsync());
            foreach (var g in d.Gastos.Where(g => g.Monto > 0)) db.VentaGastos.Add(NuevoGasto(v.Id, g));
        }
        if (d.RetencionIsr is { } ri) v.RetencionIsr = C(ri);
        if (d.RetencionIva is { } rv) v.RetencionIva = C(rv);
    }

    public static async Task<Venta> ModificarAsync(AppDbContext db, Venta v, VentaDatos d)
    {
        Validar(d);
        if (v.Estado == "cancelado")
        {
            if (d.Estado != "cancelado") throw new ErrorNegocio("Una venta cancelada no se reactiva: registra una venta nueva.", 409);
            v.Cliente = T(d.Cliente, 80); v.Notas = T(d.Notas, 400); v.ActualizadoUtc = DateTime.UtcNow;
            return v;
        }
        if (d.Estado == "cancelado") { await CancelarAsync(db, v, d.Fecha, "Cancelada desde la edición de la venta", 0m, []); return v; }

        var p = await db.Productos.FindAsync(d.ProductoId) ?? throw new ErrorNegocio("El juguete elegido ya no existe.");
        var (pl, pub) = await ValidarCanalAsync(db, d);
        var detalle = await db.VentaDetalles.Where(x => x.VentaId == v.Id).ToListAsync();
        var cambiaPiezas = d.ProductoId != v.ProductoId || d.Cantidad != v.Cantidad
                           || (d.LoteId is not null && detalle.Any(x => x.LoteId != d.LoteId));
        if (cambiaPiezas)
        {
            if (v.CantidadDevuelta > 0 || await db.VentaIncidencias.AnyAsync(i => i.VentaId == v.Id))
                throw new ErrorNegocio("La venta tiene devoluciones o incidencias: no se pueden cambiar el juguete ni las piezas.", 409);
            var productoAnterior = v.ProductoId;
            await LiberarAsync(db, v, "Edición de la venta", quitarDetalle: true);
            v.ProductoId = p.Id; v.Cantidad = d.Cantidad; v.Precio = d.Precio;
            await AsignarAsync(db, v, d.LoteId, "Venta (edición)");
            if (productoAnterior != p.Id) await RevisarPublicacionesAsync(db, productoAnterior, null, 0);
        }
        else if (d.Precio != v.Precio)
        {
            v.Precio = d.Precio;
            foreach (var x in detalle) x.PrecioUnitario = d.Precio;
        }
        if (Catalogos.Cobrada(d.Estado) && !Catalogos.Cobrada(v.Estado)) v.FechaPago ??= DateOnly.FromDateTime(DateTime.Now);
        if (!Catalogos.Cobrada(d.Estado)) v.FechaPago = null;
        v.Estado = d.Estado; v.Fecha = d.Fecha;
        v.Canal = T(pl?.Nombre ?? d.Canal, 40); v.Cliente = T(d.Cliente, 80); v.Notas = T(d.Notas, 400);
        v.PlataformaId = pl?.Id; v.PublicacionId = pub?.Id; v.Descuento = C(d.Descuento);
        if (d.PrecioPublicado is { } pp) v.PrecioPublicado = pp;
        await AplicarGastosYRetencionesAsync(db, v, p, d, nueva: false);
        if (d.ImporteCobrado is { } imp) { v.ImporteCobrado = C(imp); v.FechaCobro = d.FechaCobro ?? v.FechaCobro ?? DateOnly.FromDateTime(DateTime.Now); }
        v.ActualizadoUtc = DateTime.UtcNow;
        await RevisarPublicacionesAsync(db, p.Id, null, 0);
        return v;
    }

    // ---------------- Cobro, cancelación, devolución, reembolso, reclamación ----------------

    public static void RegistrarCobro(Venta v, decimal? importe, DateOnly? fecha)
    {
        if (importe is < 0) throw new ErrorNegocio("El importe cobrado no puede ser negativo.");
        v.ImporteCobrado = importe is null ? null : C(importe.Value);
        v.FechaCobro = importe is null ? null : fecha ?? DateOnly.FromDateTime(DateTime.Now);
        v.ActualizadoUtc = DateTime.UtcNow;
    }

    private static async Task<decimal> ReembolsadoAsync(AppDbContext db, string ventaId) =>
        (await Vivos(db, db.VentaIncidencias.Where(i => i.VentaId == ventaId), i => i.VentaId == ventaId)).Sum(i => i.MontoReembolsado);

    private static async Task ValidarReembolsoAsync(AppDbContext db, Venta v, decimal monto)
    {
        if (monto < 0) throw new ErrorNegocio("El reembolso no puede ser negativo.");
        var pagado = C(v.Precio * v.Cantidad - v.Descuento);
        var previo = await ReembolsadoAsync(db, v.Id);
        if (previo + monto > pagado) throw new ErrorNegocio($"Los reembolsos sumarían {previo + monto:N2} y el cliente pagó {pagado:N2}.");
    }

    private static VentaIncidencia Incidencia(AppDbContext db, Venta v, string tipo, DateOnly? fecha, string? motivo, decimal reembolso, int cantidad = 0,
        string? condicion = null, string estado = "cerrada")
    {
        var i = new VentaIncidencia
        {
            Id = Inventario.NuevoId(), VentaId = v.Id, Tipo = tipo, Fecha = fecha ?? DateOnly.FromDateTime(DateTime.Now), Cantidad = cantidad,
            CondicionDestino = condicion, MontoReembolsado = C(reembolso), Estado = estado, Motivo = T(motivo, 300), Usuario = db.UsuarioActual,
        };
        db.VentaIncidencias.Add(i);
        v.ActualizadoUtc = DateTime.UtcNow;
        return i;
    }

    /// <summary>
    /// Cancela la venta: las piezas regresan a su lote, ya no hay ingreso ni costo de venta.
    /// Los gastos estimados (no confirmados) se quitan; los confirmados y los que se indiquen quedan como pérdida.
    /// </summary>
    public static async Task<VentaIncidencia> CancelarAsync(AppDbContext db, Venta v, DateOnly? fecha, string? motivo, decimal reembolso, List<GastoVentaIn> gastos)
    {
        if (v.Estado == "cancelado") throw new ErrorNegocio("La venta ya está cancelada.", 409);
        await ValidarReembolsoAsync(db, v, reembolso);
        if (reembolso > 0 && v.ImporteCobrado is null) throw new ErrorNegocio("La venta no está cobrada: no hay nada que reembolsar.");
        await LiberarAsync(db, v, string.IsNullOrWhiteSpace(motivo) ? "Venta cancelada" : motivo!, quitarDetalle: false);
        db.VentaGastos.RemoveRange(await db.VentaGastos.Where(g => g.VentaId == v.Id && g.Estimado).ToListAsync());
        v.Estado = "cancelado";
        var inc = Incidencia(db, v, "cancelacion", fecha, motivo, reembolso, v.Cantidad - v.CantidadDevuelta);
        foreach (var g in gastos.Where(g => g.Monto > 0)) db.VentaGastos.Add(NuevoGasto(v.Id, g, inc.Id));
        await RevisarPublicacionesAsync(db, v.ProductoId, v, vendidas: -(v.Cantidad - v.CantidadDevuelta));
        return inc;
    }

    /// <summary>
    /// Devolución: las piezas regresan a su lote. Si vuelven en una condición no vendible (dañada, incompleta…)
    /// se reclasifican y su costo queda como merma de la venta.
    /// </summary>
    public static async Task<VentaIncidencia> DevolverAsync(AppDbContext db, Venta v, int cantidad, string? condicion, decimal reembolso,
        List<GastoVentaIn> gastos, DateOnly? fecha, string? motivo)
    {
        if (v.Estado == "cancelado") throw new ErrorNegocio("La venta está cancelada.", 409);
        if (cantidad < 1) throw new ErrorNegocio("Indica cuántas piezas se devolvieron.");
        if (cantidad > v.Cantidad - v.CantidadDevuelta) throw new ErrorNegocio($"Solo quedan {v.Cantidad - v.CantidadDevuelta} pieza(s) sin devolver en esta venta.");
        if (condicion is not null && !Catalogos.CondicionValida(condicion)) throw new ErrorNegocio("Condición no válida.");
        await ValidarReembolsoAsync(db, v, reembolso);
        var det = await db.VentaDetalles.Where(d => d.VentaId == v.Id).ToListAsync();
        var inc = Incidencia(db, v, "devolucion", fecha, motivo, reembolso, cantidad, condicion);
        var falta = cantidad;
        foreach (var d in det.OrderByDescending(d => d.CostoUnitario).ThenBy(d => d.Id))
        {
            var toma = Math.Min(falta, d.Cantidad - d.Devueltas);
            if (toma <= 0) continue;
            falta -= toma;
            d.Devueltas += toma;
            Kardex(db, d.LoteId, toma, "devolucion", T(motivo, 150) is { Length: > 0 } m ? m : "Devolución del cliente", Folio(v));
            var lote = await db.Lotes.FindAsync(d.LoteId) ?? throw new ErrorNegocio("El lote de la venta ya no existe.");
            if (condicion is not null && condicion != lote.Condicion)
            {
                var destino = await Inventario.ReclasificarAsync(db, lote, toma, condicion, $"Devolución {Folio(v)}");
                if (!destino.Comercializable) d.DevueltasAMerma += toma;
            }
            if (falta == 0) break;
        }
        v.CantidadDevuelta += cantidad;
        foreach (var g in gastos.Where(g => g.Monto > 0)) db.VentaGastos.Add(NuevoGasto(v.Id, g with { Tipo = g.Tipo ?? "devolucion" }, inc.Id));
        await Inventario.SincronizarProductoAsync(db, v.ProductoId);
        await RevisarPublicacionesAsync(db, v.ProductoId, v, vendidas: -cantidad);
        return inc;
    }

    public static async Task<VentaIncidencia> ReembolsarAsync(AppDbContext db, Venta v, decimal monto, List<GastoVentaIn> gastos, DateOnly? fecha, string? motivo)
    {
        if (monto <= 0) throw new ErrorNegocio("Indica el monto reembolsado.");
        await ValidarReembolsoAsync(db, v, monto);
        var inc = Incidencia(db, v, "reembolso", fecha, motivo, monto);
        foreach (var g in gastos.Where(g => g.Monto > 0)) db.VentaGastos.Add(NuevoGasto(v.Id, g, inc.Id));
        return inc;
    }

    public static async Task<VentaIncidencia> ReclamarAsync(AppDbContext db, Venta v, DateOnly? fecha, string? motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new ErrorNegocio("Describe la reclamación.");
        await Task.CompletedTask;
        return Incidencia(db, v, "reclamacion", fecha, motivo, 0m, estado: "abierta");
    }

    /// <summary>Cierra una reclamación con lo que costó resolverla (reembolso y gastos).</summary>
    public static async Task CerrarReclamacionAsync(AppDbContext db, Venta v, VentaIncidencia i, decimal reembolso, List<GastoVentaIn> gastos, string? resolucion)
    {
        if (i.Tipo != "reclamacion" || i.Estado != "abierta") throw new ErrorNegocio("Solo se cierran reclamaciones abiertas.", 409);
        await ValidarReembolsoAsync(db, v, reembolso);
        i.MontoReembolsado = C(reembolso);
        i.Estado = "cerrada";
        if (!string.IsNullOrWhiteSpace(resolucion)) i.Motivo = T($"{i.Motivo} · Resolución: {resolucion}", 300);
        foreach (var g in gastos.Where(g => g.Monto > 0)) db.VentaGastos.Add(NuevoGasto(v.Id, g with { Tipo = g.Tipo ?? "reclamacion" }, i.Id));
        v.ActualizadoUtc = DateTime.UtcNow;
    }

    /// <summary>Solo se elimina una venta apartada, sin cobro ni incidencias; si no, se cancela.</summary>
    public static async Task EliminarAsync(AppDbContext db, Venta v)
    {
        if (v.ImporteCobrado is not null || Catalogos.Cobrada(v.Estado))
            throw new ErrorNegocio("La venta ya se pagó o se cobró: cancélala en lugar de eliminarla, así se conserva el historial.", 409);
        if (await db.VentaIncidencias.AnyAsync(i => i.VentaId == v.Id))
            throw new ErrorNegocio("La venta tiene incidencias registradas: no se puede eliminar.", 409);
        if (v.Estado != "cancelado") await LiberarAsync(db, v, "Venta eliminada", quitarDetalle: true);
        else db.VentaDetalles.RemoveRange(await db.VentaDetalles.Where(d => d.VentaId == v.Id).ToListAsync());
        db.VentaGastos.RemoveRange(await db.VentaGastos.Where(g => g.VentaId == v.Id).ToListAsync());
        db.Ventas.Remove(v);
        await RevisarPublicacionesAsync(db, v.ProductoId, null, 0);
    }

    // ---------------- Publicaciones: control central de existencias ----------------

    /// <summary>
    /// Revisa las publicaciones del juguete contra la existencia central (lotes vendibles):
    /// - La publicación con la que se vendió descuenta sus existencias; si se agotó el juguete, queda como vendida.
    /// - Las demás publicaciones activas: si ya no hay piezas, piden pausarse; si anuncian más piezas de las que hay, ajustar existencias.
    /// - Si vuelve a haber piezas (cancelación o devolución), se quita el aviso de pausar y la publicación vendida pide reactivarse.
    /// </summary>
    public static async Task RevisarPublicacionesAsync(AppDbContext db, string productoId, Venta? venta, int vendidas)
    {
        var pubs = await db.Publicaciones.Where(x => x.ProductoId == productoId).ToListAsync();
        if (pubs.Count == 0) return;
        var disp = await DisponibleVentaAsync(db, productoId);
        foreach (var x in pubs)
        {
            var propia = venta?.PublicacionId == x.Id;
            if (propia && vendidas != 0 && x.Estatus is "publicado" or "pausado" or "vendido")
                x.Existencias = vendidas > 0 ? Math.Max(0, x.Existencias - vendidas) : Math.Min(x.Existencias - vendidas, disp);   // al regresar piezas, nunca más de las vendibles
            if (propia && vendidas > 0 && disp == 0) { x.Estatus = "vendido"; x.RequiereAccion = ""; }
            else if (x.Estatus == "publicado")
                x.RequiereAccion = disp == 0 ? "pausar" : x.Existencias > disp ? "ajustar_existencias" : "";
            else if (x.Estatus == "vendido" && disp > 0 && propia && vendidas < 0) x.RequiereAccion = "reactivar";
            else if (x.Estatus is "pausado" && x.RequiereAccion == "pausar") x.RequiereAccion = "";
            x.ActualizadoUtc = DateTime.UtcNow;
        }
    }

    // ---------------- Resultado real ----------------

    public sealed class Paquete
    {
        public required List<Venta> Ventas;
        public required ILookup<string, VentaDetalle> Detalle;
        public required ILookup<string, VentaGasto> Gastos;
        public required ILookup<string, VentaIncidencia> Incidencias;
    }

    public static async Task<Paquete> CargarAsync(AppDbContext db, IQueryable<Venta>? filtro = null)
    {
        var ventas = await (filtro ?? db.Ventas).AsNoTracking().ToListAsync();
        var ids = ventas.Select(v => v.Id).ToHashSet();
        // Con pocas ventas basta leer las tablas hijas completas y filtrar en memoria (evita IN enormes).
        var det = (await db.VentaDetalles.AsNoTracking().ToListAsync()).Where(d => ids.Contains(d.VentaId)).ToList();
        var gas = (await db.VentaGastos.AsNoTracking().ToListAsync()).Where(d => ids.Contains(d.VentaId)).ToList();
        var inc = (await db.VentaIncidencias.AsNoTracking().ToListAsync()).Where(d => ids.Contains(d.VentaId)).ToList();
        return new Paquete { Ventas = ventas, Detalle = det.ToLookup(d => d.VentaId), Gastos = gas.ToLookup(g => g.VentaId), Incidencias = inc.ToLookup(i => i.VentaId) };
    }

    public static ResultadoReal Resultado(Venta v, IEnumerable<VentaDetalle> detalle, IEnumerable<VentaGasto> gastos, IEnumerable<VentaIncidencia> incidencias)
    {
        var det = detalle.ToList();
        // Ventas sin renglones (no debería ocurrir): se usa el encabezado sin costo.
        var lineas = det.Count > 0
            ? det.Select(d => new LineaVenta(d.Cantidad, d.PrecioUnitario, d.CostoUnitario, d.Devueltas, d.DevueltasAMerma)).ToList()
            : [new LineaVenta(v.Cantidad, v.Precio, 0m)];
        return CalculoVenta.Calcular(new DatosVenta(lineas, v.Descuento, v.FactorIngreso,
            gastos.Select(g => new GastoVentaCalc(g.Monto, g.DeducidoPorPlataforma)).ToList(),
            incidencias.Sum(i => i.MontoReembolsado), v.Estado == "cancelado", v.ImporteCobrado, v.RetencionIsr, v.RetencionIva));
    }

    public static ResultadoReal Resultado(Paquete p, Venta v) => Resultado(v, p.Detalle[v.Id], p.Gastos[v.Id], p.Incidencias[v.Id]);
}
