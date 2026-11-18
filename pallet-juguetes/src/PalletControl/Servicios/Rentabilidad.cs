using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;

namespace PalletControl.Servicios;

/// <summary>
/// Calcula precios y rentabilidad por juguete y plataforma: costo real (Fase 1) + tarifa vigente + objetivo aplicable.
/// Toda la aritmética la hace el motor puro (Dominio/Calculo/MotorPrecios).
/// </summary>
public class Rentabilidad
{
    private readonly AppDbContext db;
    private Contexto? ctx;

    public Rentabilidad(AppDbContext db) => this.db = db;

    public record CostoReal(decimal? Unitario, bool Completo, bool Desactualizado);
    public record ProductoResumen(string Id, string Codigo, string Nombre, string Categoria, decimal PrecioMexico, decimal PrecioObjetivo, string Clase);
    public record TarifaResumen(string Id, string Nombre, DateOnly VigenteDesde, DateOnly? VigenteHasta);
    public record ParametroResumen(string Id, string Ambito);

    public record ResultadoCanal(
        string PlataformaId, string Plataforma, bool Ok, string? Error,
        TarifaResumen? Tarifa, ParametroResumen? Parametro,
        decimal? Equilibrio, decimal? Minimo, decimal? Objetivo, decimal? Sugerido, decimal? Promocional, bool PromocionalLimitado,
        Desglose? AlSugerido, decimal? PrecioReferencia, Desglose? AlReferencia, string Estado, List<Alerta> Alertas, string? AvisoTarifa);

    public record ResultadoProducto(ProductoResumen Producto, CostoReal Costo, List<ResultadoCanal> Canales, string? MejorCanal, string Estado, bool FiscalPendiente);

    private sealed class Contexto
    {
        public required ConfiguracionFiscal Fiscal;
        public required List<Plataforma> Plataformas;
        public required List<TarifaPlataforma> Tarifas;
        public required List<ParametroRentabilidad> Parametros;
        public required Dictionary<string, CostoReal> Costos;
    }

    private async Task<Contexto> CargarAsync()
    {
        if (ctx is not null) return ctx;
        var fiscal = await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new ConfiguracionFiscal();
        var plataformas = await db.Plataformas.AsNoTracking().OrderBy(p => p.Orden).ThenBy(p => p.Nombre).ToListAsync();
        var tarifas = await db.Tarifas.AsNoTracking().Where(t => t.Activa).ToListAsync();
        var parametros = await db.Parametros.AsNoTracking().Where(p => p.Activo).ToListAsync();

        // Costo real por pieza: costeo vigente de los lotes vendibles (igual que en la ficha del juguete).
        var corridas = await db.CosteoCorridas.AsNoTracking().Where(c => c.Vigente).ToListAsync();
        var desact = new HashSet<string>();
        foreach (var c in corridas)
            if (c.Huella != (await Inventario.EntradaAsync(db, c.PalletId)).Huella) desact.Add(c.PalletId);
        var ids = corridas.Select(c => c.Id).ToList();
        var costos = await db.CostosLote.AsNoTracking().Where(c => ids.Contains(c.CorridaId)).ToListAsync();
        var porLote = costos.ToDictionary(c => c.LoteId);
        var lotes = await db.Lotes.AsNoTracking().Where(l => l.Comercializable && l.Cantidad > 0).ToListAsync();
        var res = new Dictionary<string, CostoReal>();
        foreach (var g in lotes.GroupBy(l => l.ProductoId))
        {
            decimal tot = 0; int pz = 0; bool falta = false, des = false;
            foreach (var l in g)
            {
                if (porLote.TryGetValue(l.Id, out var c) && c.Cantidad == l.Cantidad && c.Comercializable) { tot += c.CostoTotal; pz += l.Cantidad; }
                else falta = true;
                if (desact.Contains(l.PalletId)) des = true;
            }
            res[g.Key] = new CostoReal(pz > 0 ? tot / pz : null, !falta && pz > 0, des);
        }
        return ctx = new Contexto { Fiscal = fiscal, Plataformas = plataformas, Tarifas = tarifas, Parametros = parametros, Costos = res };
    }

    private static TarifaResumen Resumen(TarifaPlataforma t) => new(t.Id, t.Nombre, t.VigenteDesde, t.VigenteHasta);

    public static ConfigFiscal Fiscal(ConfiguracionFiscal f) => new(f.Regimen != "pendiente" && f.CobraIva, f.TasaIvaPct);

    public static ComponentesTarifa Componentes(TarifaPlataforma t) => new(t.ComisionPct, t.ComisionMedioPagoPct, t.CargoFijo, t.CostoLogistico,
        t.EnvioAbsorbido, t.Almacenamiento, t.Preparacion, t.Empaque, t.PublicidadPct, t.PublicidadFija, t.ReservaDevolucionesPct,
        t.RetencionIsrPct, t.RetencionIvaPct);

    public static ReglaTarifa Regla(TarifaPlataforma t) => new(t.Id, t.VigenteDesde, t.VigenteHasta, t.Activa, t.Predeterminada,
        t.TipoPublicacion, t.Modalidad, t.Categoria, t.MedioPago, t.PrecioMin, t.PrecioMax, t.PesoMinKg, t.PesoMaxKg, t.LadoMaxCm, Componentes(t));

    public static ObjetivoRentabilidad Objetivo(ParametroRentabilidad p) => new(
        p.Esquema switch { "markup" => EsquemaRentabilidad.Markup, "utilidad_fija" => EsquemaRentabilidad.UtilidadFija, _ => EsquemaRentabilidad.Margen },
        p.Valor, p.MargenMinimoPct, p.DescuentoPromocionalPct,
        p.Redondeo switch { "peso" => ModoRedondeo.Peso, "diez" => ModoRedondeo.Diez, "centavo" => ModoRedondeo.Centavo, _ => ModoRedondeo.Terminacion9 });

    /// <summary>Objetivo aplicable: gana el más específico (producto+plataforma > producto > categoría+plataforma > categoría > plataforma > global).</summary>
    public static ParametroRentabilidad? ParametroPara(IEnumerable<ParametroRentabilidad> ps, Producto p, string plataformaId)
    {
        int Nivel(ParametroRentabilidad x)
        {
            if (x.ProductoId is not null && x.ProductoId != p.Id) return -1;
            if (x.PlataformaId is not null && x.PlataformaId != plataformaId) return -1;
            if (x.Categoria is not null && !string.Equals(x.Categoria, p.Categoria, StringComparison.OrdinalIgnoreCase)) return -1;
            return x.ProductoId is not null ? (x.PlataformaId is not null ? 6 : 5)
                 : x.Categoria is not null ? (x.PlataformaId is not null ? 4 : 3)
                 : x.PlataformaId is not null ? 2 : 1;
        }
        return ps.Select(x => (x, n: Nivel(x))).Where(t => t.n > 0).OrderByDescending(t => t.n).Select(t => t.x).FirstOrDefault();
    }

    public static string Ambito(ParametroRentabilidad x) =>
        x.ProductoId is not null ? (x.PlataformaId is not null ? "juguete y plataforma" : "juguete")
        : x.Categoria is not null ? (x.PlataformaId is not null ? "categoría y plataforma" : "categoría")
        : x.PlataformaId is not null ? "plataforma" : "general";

    public async Task<CostoReal> CostoDeAsync(string productoId) =>
        (await CargarAsync()).Costos.GetValueOrDefault(productoId) ?? new CostoReal(null, false, false);

    /// <summary>Precios y rentabilidad de un juguete en todas las plataformas activas (o en una).</summary>
    public async Task<ResultadoProducto> ProductoAsync(Producto p, DateOnly fecha, string? soloPlataforma = null, decimal? costoManual = null,
        string? tipoPublicacion = null, string? modalidad = null, string? medioPago = null)
    {
        var c = await CargarAsync();
        var costo = costoManual is not null ? new CostoReal(costoManual, true, false) : c.Costos.GetValueOrDefault(p.Id) ?? new CostoReal(null, false, false);
        var fi = Fiscal(c.Fiscal);
        var canales = new List<ResultadoCanal>();
        var lado = new[] { p.LargoCm, p.AnchoCm, p.AltoCm }.Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty().Max();
        var contexto = new ContextoTarifa(fecha, tipoPublicacion, modalidad, string.IsNullOrWhiteSpace(p.Categoria) ? null : p.Categoria, medioPago,
            p.PesoKg, lado > 0 ? lado : null);
        foreach (var pl in c.Plataformas.Where(x => soloPlataforma is null ? x.Activa : x.Id == soloPlataforma))
        {
            var par = ParametroPara(c.Parametros, p, pl.Id);
            if (costo.Unitario is null)
            {
                canales.Add(new(pl.Id, pl.Nombre, false, "Sin costo real: calcula el costeo de su pallet (Pallets y costos).", null, null,
                    null, null, null, null, null, false, null, null, null, "sin_costo", [], null));
                continue;
            }
            if (par is null)
            {
                canales.Add(new(pl.Id, pl.Nombre, false, "No hay objetivo de rentabilidad aplicable.", null, null,
                    null, null, null, null, null, false, null, null, null, "sin_objetivo", [], null));
                continue;
            }
            var obj = Objetivo(par);
            var reglas = c.Tarifas.Where(t => t.PlataformaId == pl.Id).Select(Regla).ToList();
            var el = SelectorTarifa.Elegir(reglas, contexto, costo.Unitario.Value, fi, obj);
            var pr = el.Precios;
            var tarifa = el.Regla is null ? null : c.Tarifas.First(t => t.Id == el.Regla.Id);
            if (!pr.Ok)
            {
                canales.Add(new(pl.Id, pl.Nombre, false, pr.Error, tarifa is null ? null : Resumen(tarifa), new ParametroResumen(par.Id, Ambito(par)),
                    null, null, null, null, null, false, null, null, null, tarifa is null ? "sin_tarifa" : "no_rentable",
                    tarifa is null ? [] : [new Alerta("no_rentable", "error", pr.Error!)], null));
                continue;
            }
            // Precio de referencia: precio de mercado (México) si existe; si no, el precio objetivo que capturaste; si no, el sugerido.
            var referencia = p.PrecioMexico > 0 ? p.PrecioMexico : p.PrecioObjetivo > 0 ? p.PrecioObjetivo : pr.Sugerido!.Value;
            var cand = SelectorTarifa.Candidatas(reglas, contexto, out _);
            var reglaRef = SelectorTarifa.ParaPrecio(cand, referencia) ?? el.Regla!;
            var alRef = MotorPrecios.Evaluar(referencia, costo.Unitario.Value, reglaRef.Componentes, fi);
            // Alertas al precio que planeas publicar (tu precio objetivo) o, si no lo has capturado, al sugerido.
            var precioPlan = p.PrecioObjetivo > 0 ? p.PrecioObjetivo : pr.Sugerido!.Value;
            var reglaPlan = SelectorTarifa.ParaPrecio(cand, precioPlan) ?? el.Regla!;
            var alertas = MotorPrecios.Alertas(precioPlan, costo.Unitario.Value, reglaPlan.Componentes, fi, pr, obj, p.PrecioMexico > 0 ? p.PrecioMexico : null);
            var estado = alRef.Utilidad < 0 ? "perdida"
                : referencia >= pr.Objetivo ? "rentable"
                : referencia >= pr.Minimo ? "margen_bajo" : "bajo_minimo";
            canales.Add(new(pl.Id, pl.Nombre, true, null, Resumen(tarifa!), new ParametroResumen(par.Id, Ambito(par)),
                pr.Equilibrio, pr.Minimo, pr.Objetivo, pr.Sugerido, pr.Promocional, pr.PromocionalLimitado, pr.AlSugerido,
                referencia, alRef, estado, alertas, el.Aviso));
        }
        var ok = canales.Where(x => x.Ok).ToList();
        // Mejor canal: el que deja más utilidad vendiendo al mismo precio de referencia.
        var mejor = ok.OrderByDescending(x => x.AlReferencia!.Utilidad).FirstOrDefault();
        var estadoProd = costo.Unitario is null ? "sin_costo" : ok.Count == 0 ? "sin_tarifa"
            : mejor!.Estado;
        return new ResultadoProducto(new ProductoResumen(p.Id, p.Codigo, p.Nombre, p.Categoria, p.PrecioMexico, p.PrecioObjetivo, p.Clase), costo, canales, mejor?.PlataformaId, estadoProd,
            c.Fiscal.Regimen == "pendiente");
    }
}
