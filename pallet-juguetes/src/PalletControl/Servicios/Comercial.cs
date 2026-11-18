using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;

namespace PalletControl.Servicios;

/// <summary>Filtros comunes del dashboard y los reportes. Las fechas filtran ventas (fecha de la venta).</summary>
public record FiltroComercial(DateOnly? Desde, DateOnly? Hasta, string? PalletId, string? Categoria, string? ProductoId, string? PlataformaId)
{
    public bool SoloPallets => Categoria is null && ProductoId is null;
    public bool SinFiltroDeAlcance => PalletId is null && Categoria is null && ProductoId is null && PlataformaId is null;
}

/// <summary>
/// Fase 4 — inteligencia comercial: competencia, indicadores del dashboard y datos de reportes.
/// Reutiliza el resultado real por venta de la Fase 3 (costo histórico) y la existencia por lote.
/// </summary>
public class Comercial(AppDbContext db)
{
    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    private static decimal C(decimal v) => Dinero.Centavos(v);
    private static decimal? Pct(decimal a, decimal b) => b > 0 ? Math.Round(a / b * 100m, 2, MidpointRounding.AwayFromZero) : null;

    // ======================= Competencia =======================

    public record ResumenCompetencia(string ProductoId, string Codigo, string Nombre, string Categoria, int Observaciones,
        EstadisticaPrecios? Mercado, decimal? PrecioPropio, string? OrigenPrecioPropio, PosicionCompetitiva? Posicion,
        decimal PrecioMinimo, bool MercadoBajoTuMinimo, DateOnly? UltimaConsulta);

    /// <summary>
    /// Precio propio que se compara: la publicación activa más barata; si no hay, el precio objetivo. Sin ninguno no hay posición.
    /// Se comparan solo las observaciones vigentes, por su precio total (publicado + envío).
    /// </summary>
    public async Task<List<ResumenCompetencia>> CompetenciaAsync(string? productoId = null)
    {
        var cfg = await db.Configuracion.AsNoTracking().OrderBy(x => x.Id).FirstAsync();
        var obs = await db.Competencia.AsNoTracking().Where(c => c.Vigente && (productoId == null || c.ProductoId == productoId)).ToListAsync();
        var ids = obs.Select(o => o.ProductoId).Append(productoId).Where(x => x is not null).Distinct().ToList();
        var prods = await db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync();
        var pubs = await db.Publicaciones.AsNoTracking().Where(p => ids.Contains(p.ProductoId) && p.Estatus == "publicado").ToListAsync();
        return prods.Select(p =>
        {
            var mias = obs.Where(o => o.ProductoId == p.Id).ToList();
            var precios = mias.Select(o => o.PrecioTotal).ToList();
            var pub = pubs.Where(x => x.ProductoId == p.Id && x.PrecioPublicado > 0).Select(x => (decimal?)x.PrecioPublicado).Min();
            decimal? propio = pub ?? (p.PrecioObjetivo > 0 ? p.PrecioObjetivo : null);
            var origen = pub is not null ? "publicacion" : propio is not null ? "precio_objetivo" : null;
            var est = Indicadores.Estadistica(precios);
            var pos = propio is null ? null : Indicadores.Posicion(propio.Value, precios, cfg.BandaCompetenciaPct);
            return new ResumenCompetencia(p.Id, p.Codigo, p.Nombre, p.Categoria, mias.Count, est, propio, origen, pos,
                p.PrecioMinimo, est is not null && p.PrecioMinimo > 0 && est.Mediana < p.PrecioMinimo, mias.Select(o => (DateOnly?)o.FechaConsulta).Max());
        }).OrderBy(r => r.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ======================= Dashboard =======================

    public record Monto(string Clave, string Nombre, int Ventas, int Piezas, decimal Ingresos, decimal Utilidad, decimal? MargenPct);
    public record ProductoIndicador(string ProductoId, string Codigo, string Nombre, string Categoria, int Recibidas, int Vendidas, int Disponibles,
        decimal Ingresos, decimal Utilidad, decimal? MargenPct, decimal? RotacionPct, int DiasSinMovimiento, decimal? CostoUnitario, decimal PrecioMinimo,
        string? PosicionCompetitiva, decimal? MedianaMercado);
    public record PalletIndicador(string PalletId, string Nombre, decimal Inversion, decimal Recuperado, decimal? RecuperadoPct, decimal Pendiente);
    public record PuntoSerie(DateOnly Fecha, decimal Valor);

    public record Financieros(decimal Inversion, string BaseInversion, decimal Ingresos, decimal CostoVendido, decimal Merma, decimal GastosComerciales,
        decimal UtilidadBruta, decimal UtilidadComercial, decimal? GastosOperacion, decimal UtilidadNetaEstimada, bool NetaIncluyeOperacion,
        decimal? MargenPct, decimal? RoiPct, decimal? RendimientoSobreCostoPct, decimal CapitalRecuperado, decimal CapitalPendiente, decimal? RecuperadoPct,
        decimal Cobrado, decimal PorCobrar, int Ventas, int Piezas);
    public record Inventario(int Productos, int PiezasRecibidas, int PiezasDisponibles, int ProductosDisponibles, int ProductosPublicados,
        int PiezasVendidas, int ProductosVendidos, int PiezasDanadas, int PiezasIncompletas, int PiezasNoVendibles,
        int ProductosSinMovimiento, int PiezasSinMovimiento, Antiguedad Antiguedad, List<Monto> PorCondicion);
    public record Dashboard(FiltroComercial Filtros, int UmbralSinMovimiento, decimal BandaCompetenciaPct, bool FiscalPendiente,
        Financieros Financieros, Inventario Inventario, List<PalletIndicador> PorPallet,
        List<Monto> PorPlataforma, List<Monto> PorCategoria, List<ProductoIndicador> Productos,
        List<ProductoIndicador> MasVendidos, List<ProductoIndicador> MasRentables, List<ProductoIndicador> MenosRentables,
        List<ProductoIndicador> MayorRotacion, List<ProductoIndicador> CandidatosLiquidacion, List<PuntoSerie> Recuperacion,
        Dictionary<string, int> Competencia);

    /// <summary>Parte de una venta que corresponde al alcance (todo, o un pallet).</summary>
    private sealed record Porcion(Venta V, ResultadoReal R, decimal Ingresos, decimal Gastos, decimal Costo, decimal Merma, decimal Utilidad,
        decimal Efectivo, decimal PorCobrar, int Piezas);

    private static Porcion Recortar(Venta v, ResultadoReal r, List<VentaDetalle> det, string? palletId)
    {
        var cancelada = v.Estado == "cancelado";
        var ingresos = r.IngresoNeto - r.ReembolsosNetos;
        if (palletId is null || det.Count == 0)
            return new(v, r, ingresos, r.GastosTotales, r.CostoVendido, r.CostoMerma, r.Utilidad, r.Cobrada ? r.EfectivoNeto : 0, r.PorCobrarEstimado,
                cancelada ? 0 : v.Cantidad - v.CantidadDevuelta);
        // Varios pallets en una venta: ingresos, gastos y efectivo en proporción al importe de cada renglón; costo exacto por renglón.
        decimal Parte(decimal monto) => CalculoVenta.RepartirPorPeso(monto, det.Select(d => (d.PalletId, d.Cantidad * d.PrecioUnitario)).ToList()).GetValueOrDefault(palletId);
        var mios = det.Where(d => d.PalletId == palletId).ToList();
        var costo = cancelada ? 0 : C(mios.Sum(d => (d.Cantidad - d.Devueltas) * d.CostoUnitario));
        var merma = cancelada ? 0 : C(mios.Sum(d => d.DevueltasAMerma * d.CostoUnitario));
        var ing = Parte(ingresos); var gas = Parte(r.GastosTotales);
        return new(v, r, ing, gas, costo, merma, ing - gas - costo - merma, r.Cobrada ? Parte(r.EfectivoNeto) : 0, Parte(r.PorCobrarEstimado),
            cancelada ? 0 : mios.Sum(d => d.Cantidad - d.Devueltas));
    }

    public async Task<Dashboard> DashboardAsync(FiltroComercial f)
    {
        var cfg = await db.Configuracion.AsNoTracking().OrderBy(x => x.Id).FirstAsync();
        var fiscal = await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new ConfiguracionFiscal();
        var pallets = await db.Pallets.AsNoTracking().OrderBy(p => p.FechaCompra).ToListAsync();
        var prodsTodos = await db.Productos.AsNoTracking().ToListAsync();
        var lotesTodos = await db.Lotes.AsNoTracking().ToListAsync();
        var plats = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
        var pubs = await db.Publicaciones.AsNoTracking().Where(p => p.Estatus == "publicado").ToListAsync();
        var vigentes = await db.CosteoCorridas.AsNoTracking().Where(c => c.Vigente).Select(c => c.Id).ToListAsync();
        var costos = (await db.CostosLote.AsNoTracking().Where(c => vigentes.Contains(c.CorridaId)).ToListAsync()).GroupBy(c => c.LoteId).ToDictionary(g => g.Key, g => g.First());
        var pq = await Ventas.CargarAsync(db);

        // ---- Alcance ----
        bool CatOk(Producto p) => f.Categoria is null || string.Equals(p.Categoria, f.Categoria, StringComparison.OrdinalIgnoreCase);
        var lotes = lotesTodos.Where(l => (f.PalletId is null || l.PalletId == f.PalletId)).ToList();
        var prods = prodsTodos.Where(p => CatOk(p) && (f.ProductoId is null || p.Id == f.ProductoId) && (f.PalletId is null || lotes.Any(l => l.ProductoId == p.Id))).ToDictionary(p => p.Id);
        lotes = lotes.Where(l => prods.ContainsKey(l.ProductoId)).ToList();
        var palletsAlcance = pallets.Where(p => f.PalletId is null || p.Id == f.PalletId).ToList();

        // Vendidas netas por lote (ventas no canceladas, cualquier fecha) para existencias
        var activas = pq.Ventas.Where(v => v.Estado != "cancelado").Select(v => v.Id).ToHashSet();
        var vendLote = pq.Detalle.SelectMany(g => g).Where(d => activas.Contains(d.VentaId)).GroupBy(d => d.LoteId).ToDictionary(g => g.Key, g => g.Sum(d => d.Cantidad - d.Devueltas));
        int Disp(InventarioLote l) => l.Cantidad - vendLote.GetValueOrDefault(l.Id);

        // Ventas en alcance
        bool VentaEnAlcance(Venta v, bool conFechas) => prods.ContainsKey(v.ProductoId)
            && (f.PlataformaId is null || v.PlataformaId == f.PlataformaId)
            && (f.PalletId is null || pq.Detalle[v.Id].Any(d => d.PalletId == f.PalletId))
            && (!conFechas || ((f.Desde is null || v.Fecha >= f.Desde) && (f.Hasta is null || v.Fecha <= f.Hasta)));
        var porciones = pq.Ventas.Where(v => VentaEnAlcance(v, true))
            .Select(v => Recortar(v, Ventas.Resultado(pq, v), pq.Detalle[v.Id].ToList(), f.PalletId)).ToList();
        // Capital recuperado: acumulado hasta la fecha final (no depende de "desde")
        var porcionesCapital = pq.Ventas.Where(v => VentaEnAlcance(v, false) && (f.Hasta is null || v.Fecha <= f.Hasta))
            .Select(v => Recortar(v, Ventas.Resultado(pq, v), pq.Detalle[v.Id].ToList(), f.PalletId)).Where(x => x.R.Cobrada).ToList();

        // ---- Financieros ----
        decimal inversion; string baseInv;
        if (f.SoloPallets) { inversion = palletsAlcance.Sum(p => p.InversionTotal); baseInv = "pallets"; }
        else { inversion = lotes.Sum(l => costos.TryGetValue(l.Id, out var c) ? c.CostoTotal : 0m); baseInv = "costo_asignado"; }
        var ingresos = porciones.Sum(x => x.Ingresos);
        var costo = porciones.Sum(x => x.Costo); var merma = porciones.Sum(x => x.Merma);
        var gastosCom = porciones.Sum(x => x.Gastos);
        var bruta = ingresos - costo - merma;
        var comercial = porciones.Sum(x => x.Utilidad);
        // Los gastos generales de operación no se pueden asignar a un pallet, categoría, juguete o plataforma: solo se restan sin esos filtros.
        decimal? gastosOp = f.SinFiltroDeAlcance
            ? (await db.Gastos.AsNoTracking().ToListAsync()).Where(g => (f.Desde is null || g.Fecha >= f.Desde) && (f.Hasta is null || g.Fecha <= f.Hasta)).Sum(g => g.Monto)
            : null;
        var neta = comercial - (gastosOp ?? 0m);
        var recuperado = porcionesCapital.Sum(x => x.Efectivo);
        var fin = new Financieros(inversion, baseInv, ingresos, costo, merma, gastosCom, bruta, comercial, gastosOp, neta, gastosOp is not null,
            Pct(comercial, ingresos), Indicadores.Roi(neta, inversion), Pct(comercial, costo + merma),
            recuperado, Math.Max(0m, inversion - recuperado), Pct(recuperado, inversion),
            porciones.Where(x => x.R.Cobrada).Sum(x => x.R.ImporteCobrado), porciones.Where(x => !x.R.Cobrada).Sum(x => x.PorCobrar),
            porciones.Count(x => x.V.Estado != "cancelado"), porciones.Sum(x => x.Piezas));

        // ---- Por pallet (capital recuperado) ----
        var capitalPallet = new Dictionary<string, decimal>();
        foreach (var v in pq.Ventas.Where(v => VentaEnAlcance(v, false) && (f.Hasta is null || v.Fecha <= f.Hasta)))
        {
            var r = Ventas.Resultado(pq, v);
            if (!r.Cobrada) continue;
            foreach (var kv in CalculoVenta.RepartirPorPeso(r.EfectivoNeto, pq.Detalle[v.Id].Select(d => (d.PalletId, d.Cantidad * d.PrecioUnitario)).ToList()))
                capitalPallet[kv.Key] = capitalPallet.GetValueOrDefault(kv.Key) + kv.Value;
        }
        var porPallet = palletsAlcance.Select(p =>
        {
            var rec = capitalPallet.GetValueOrDefault(p.Id);
            return new PalletIndicador(p.Id, p.Nombre, p.InversionTotal, rec, Pct(rec, p.InversionTotal), Math.Max(0m, p.InversionTotal - rec));
        }).ToList();

        // ---- Inventario ----
        var ultimaVenta = pq.Ventas.Where(v => v.Estado != "cancelado").GroupBy(v => v.ProductoId).ToDictionary(g => g.Key, g => g.Max(v => v.Fecha));
        var corte = Hoy;
        var lotesVend = lotes.Where(l => l.Comercializable).ToList();
        int DispVend(string pid) => lotesVend.Where(l => l.ProductoId == pid).Sum(l => Math.Max(0, Disp(l)));
        int DiasSinMov(string pid)
        {
            var conPiezas = lotesVend.Where(l => l.ProductoId == pid && Disp(l) > 0).ToList();
            if (conPiezas.Count == 0) return 0;
            return Indicadores.DiasSinMovimiento(conPiezas.Min(l => l.FechaIngreso), ultimaVenta.TryGetValue(pid, out var u) ? u : null, corte);
        }
        var publicados = pubs.Select(p => p.ProductoId).Where(prods.ContainsKey).ToHashSet();
        var vendidasProd = lotes.GroupBy(l => l.ProductoId).ToDictionary(g => g.Key, g => g.Sum(l => vendLote.GetValueOrDefault(l.Id)));
        var sinMov = prods.Keys.Where(pid => DispVend(pid) > 0 && DiasSinMov(pid) >= cfg.DiasSinMovimiento).ToList();
        int PiezasCond(string cond) => lotes.Where(l => l.Condicion == cond).Sum(l => Math.Max(0, Disp(l)));
        var condiciones = Catalogos.Condiciones.ToDictionary(c => c.Codigo, c => c.Nombre);
        var inv = new Inventario(
            prods.Count, lotes.Sum(l => l.Cantidad), lotesVend.Sum(l => Math.Max(0, Disp(l))), prods.Keys.Count(pid => DispVend(pid) > 0), publicados.Count,
            vendidasProd.Values.Sum(), vendidasProd.Count(kv => kv.Value > 0), PiezasCond("danado"), PiezasCond("incompleto"),
            lotes.Where(l => !l.Comercializable).Sum(l => Math.Max(0, Disp(l))),
            sinMov.Count, sinMov.Sum(DispVend),
            Indicadores.Antiguedad(lotesVend.Select(l => new PiezaInventario(Math.Max(0, Disp(l)), l.FechaIngreso)), corte),
            lotes.GroupBy(l => l.Condicion).Select(g => new Monto(g.Key, condiciones.GetValueOrDefault(g.Key) ?? g.Key, 0, g.Sum(l => Math.Max(0, Disp(l))), 0, 0, null))
                .Where(m => m.Piezas > 0).OrderByDescending(m => m.Piezas).ToList());

        // ---- Comerciales ----
        Monto Agrupar(string clave, string nombre, IEnumerable<Porcion> xs)
        {
            var l = xs.ToList(); var ing = l.Sum(x => x.Ingresos); var u = l.Sum(x => x.Utilidad);
            return new Monto(clave, nombre, l.Count(x => x.V.Estado != "cancelado"), l.Sum(x => x.Piezas), ing, u, Pct(u, ing));
        }
        var porPlataforma = porciones.GroupBy(x => x.V.PlataformaId ?? "")
            .Select(g => Agrupar(g.Key, g.Key == "" ? "Sin plataforma asignada" : plats.GetValueOrDefault(g.Key) ?? g.Key, g))
            .OrderByDescending(m => m.Ingresos).ToList();
        var porCategoria = porciones.GroupBy(x => prods.TryGetValue(x.V.ProductoId, out var p) && p.Categoria != "" ? p.Categoria : "Sin categoría")
            .Select(g => Agrupar(g.Key, g.Key, g)).OrderByDescending(m => m.Utilidad).ToList();

        var comp = (await CompetenciaAsync()).ToDictionary(c => c.ProductoId);
        var costoUnit = lotesVend.Where(l => costos.ContainsKey(l.Id) && l.Cantidad > 0).GroupBy(l => l.ProductoId)
            .ToDictionary(g => g.Key, g => (decimal?)C(g.Sum(l => costos[l.Id].CostoTotal) / g.Sum(l => l.Cantidad)));
        var ventasProd = porciones.GroupBy(x => x.V.ProductoId).ToDictionary(g => g.Key, g => g.ToList());
        var productos = prods.Values.Select(p =>
        {
            var vs = ventasProd.GetValueOrDefault(p.Id) ?? [];
            var ing = vs.Sum(x => x.Ingresos); var u = vs.Sum(x => x.Utilidad);
            var recib = lotes.Where(l => l.ProductoId == p.Id).Sum(l => l.Cantidad);
            var vend = vendidasProd.GetValueOrDefault(p.Id);
            var c = comp.GetValueOrDefault(p.Id);
            return new ProductoIndicador(p.Id, p.Codigo, p.Nombre, p.Categoria, recib, vend, DispVend(p.Id), ing, u, Pct(u, ing),
                Indicadores.RotacionPct(vend, recib), DiasSinMov(p.Id), costoUnit.GetValueOrDefault(p.Id), p.PrecioMinimo,
                c?.Posicion?.Posicion, c?.Mercado?.Mediana);
        }).ToList();
        var conVentas = productos.Where(p => ventasProd.ContainsKey(p.ProductoId)).ToList();
        var porProdPeriodo = conVentas.Select(p => p with { Vendidas = ventasProd[p.ProductoId].Sum(x => x.Piezas) }).ToList();

        // Evolución de la recuperación: efectivo neto acumulado por fecha de cobro
        var serie = new List<PuntoSerie>(); decimal acum = 0;
        foreach (var g in porcionesCapital.GroupBy(x => x.V.FechaCobro ?? x.V.Fecha).OrderBy(g => g.Key))
        { acum += g.Sum(x => x.Efectivo); serie.Add(new PuntoSerie(g.Key, acum)); }

        var compResumen = new Dictionary<string, int>
        {
            ["conCompetencia"] = comp.Values.Count(c => prods.ContainsKey(c.ProductoId) && c.Observaciones > 0),
            ["barato"] = comp.Values.Count(c => prods.ContainsKey(c.ProductoId) && c.Posicion?.Posicion == "barato"),
            ["en_mercado"] = comp.Values.Count(c => prods.ContainsKey(c.ProductoId) && c.Posicion?.Posicion == "en_mercado"),
            ["caro"] = comp.Values.Count(c => prods.ContainsKey(c.ProductoId) && c.Posicion?.Posicion == "caro"),
            ["mercadoBajoMinimo"] = comp.Values.Count(c => prods.ContainsKey(c.ProductoId) && c.MercadoBajoTuMinimo),
        };

        return new Dashboard(f, cfg.DiasSinMovimiento, cfg.BandaCompetenciaPct, fiscal.Regimen == "pendiente", fin, inv, porPallet, porPlataforma, porCategoria,
            productos.OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase).ToList(),
            porProdPeriodo.OrderByDescending(p => p.Vendidas).ThenByDescending(p => p.Utilidad).Take(5).ToList(),
            porProdPeriodo.OrderByDescending(p => p.Utilidad).Take(5).ToList(),
            porProdPeriodo.OrderBy(p => p.Utilidad).Take(5).ToList(),
            productos.Where(p => p.RotacionPct > 0).OrderByDescending(p => p.RotacionPct).ThenBy(p => p.Codigo).Take(5).ToList(),
            productos.Where(p => sinMov.Contains(p.ProductoId)).OrderByDescending(p => p.DiasSinMovimiento).ToList(),
            serie, compResumen);
    }
}
