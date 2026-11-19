using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

/// <summary>Datos del juguete que se está capturando (aún sin guardar, o ya guardado si trae ProductoId).</summary>
public record SugerirPrecioIn(string? ProductoId, string? PalletId, int Cantidad, decimal PrecioMexico, decimal? ValorReferencia,
    string? Categoria, decimal? PesoKg, decimal? LargoCm, decimal? AnchoCm, decimal? AltoCm);

/// <summary>
/// Precio sugerido y precio mínimo de un juguete (un solo criterio para el escaneo y para «Recalcular mínimos»):
/// 1) referencia de mercado (precio en México o, si no hay, precios de EE. UU. convertidos);
/// 2) costo por pieza (real si el pallet ya tiene costeo; si no, estimado con el método proporcional);
/// 3) motor de precios (Fase 2) con ese costo: mínimo y sugerido en el mejor canal con tarifa,
///    o en venta directa sin comisiones si no hay tarifas capturadas.
/// </summary>
public class SugeridorPrecios(AppDbContext db)
{
    public record Canal(string PlataformaId, string Plataforma, decimal? Minimo, decimal? Sugerido, decimal? Utilidad, decimal? MargenPct, bool Ok, string? Error);
    public record Resultado(bool Ok, decimal? Referencia, string OrigenReferencia, decimal? Costo, string OrigenCosto, CostoEstimado? Estimacion,
        Canal? Canal, List<Canal> Canales, bool FiscalPendiente, List<string> Avisos)
    {
        public decimal? PrecioObjetivo => Canal?.Sugerido;
        public decimal? PrecioMinimo => Canal?.Minimo;
    }

    // Se reutiliza para muchos juguetes (Recalcular mínimos): tarifas, objetivos y costos se cargan una vez.
    private readonly Rentabilidad rent = new(db);
    private List<ParametroRentabilidad>? parametros;
    private ConfiguracionFiscal? fiscal;

    public async Task<Resultado> SugerirAsync(SugerirPrecioIn i, Producto? existente)
    {
        var avisos = new List<string>();

        // 1) Referencia de mercado
        decimal? referencia; string origenRef;
        if (i.PrecioMexico > 0) { referencia = i.PrecioMexico; origenRef = "precio_mexico"; }
        else if (i.ValorReferencia is > 0) { referencia = i.ValorReferencia; origenRef = "precios_eeuu"; }
        else if (existente?.ValorReferencia is > 0) { referencia = existente.ValorReferencia; origenRef = "precios_eeuu"; }
        else { referencia = null; origenRef = ""; }

        // 2) Costo por pieza
        decimal? costo = null; string origenCosto = ""; CostoEstimado? est = null;
        if (existente is not null && await rent.CostoDeAsync(existente.Id) is { Unitario: { } cu, Completo: true, Desactualizado: false })
        { costo = Dinero.Centavos(cu); origenCosto = "real"; }
        else
        {
            var palletId = i.PalletId ?? (existente is null ? null : await db.Lotes.AsNoTracking().Where(l => l.ProductoId == existente.Id)
                .OrderByDescending(l => l.Cantidad).Select(l => l.PalletId).FirstOrDefaultAsync());
            var pallet = palletId is null ? null : await db.Pallets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == palletId);
            if (pallet is null) avisos.Add("Elige el pallet de origen para estimar el costo.");
            else if (referencia is null) avisos.Add("Sin precio en México ni precios de referencia del código de barras no se puede estimar el costo: captura el precio en México.");
            else
            {
                var excluir = existente?.Id ?? "";
                var lotes = await db.Lotes.AsNoTracking().Where(l => l.PalletId == pallet.Id && l.Comercializable && l.Cantidad > 0 && l.ProductoId != excluir).ToListAsync();
                var ids = lotes.Select(l => l.ProductoId).Distinct().ToList();
                var prods = await db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
                var mercado = pallet.ValorMercadoEstimado is > 0 ? Dinero.Centavos(pallet.ValorMercadoEstimado.Value * (pallet.Moneda == "MXN" ? 1m : pallet.TipoCambio)) : (decimal?)null;
                est = EstimacionCosto.Calcular(pallet.InversionTotal, mercado, lotes.Select(l => (l.Cantidad, Inventario.ValorRelativo(l, prods[l.ProductoId]))), Math.Max(1, i.Cantidad), referencia.Value);
                if (est is not null)
                {
                    costo = est.CostoUnitario; origenCosto = "estimado";
                    if (!est.UsaValorMercadoPallet)
                        avisos.Add(mercado is null
                            ? $"Costo estimado repartiendo la inversión del pallet (${est.Inversion:N2}) solo entre las {est.PiezasRegistradas} pieza(s) registradas: bajará conforme registres más juguetes. Para un estimado estable desde el primer juguete, captura en el pallet su «valor de mercado estimado» (manifiesto)."
                            : "Lo registrado ya supera el valor de mercado estimado del pallet: se reparte con lo registrado.");
                    if (est.PiezasSinValor > 0) avisos.Add($"{est.PiezasSinValor} pieza(s) del pallet sin precio de referencia no entran al reparto.");
                }
            }
        }
        if (costo is null) return new Resultado(false, referencia, origenRef, null, "", null, null, [], false, avisos);

        // 3) Motor de precios con ese costo
        var p = new Producto
        {
            Id = existente?.Id ?? "", Categoria = i.Categoria ?? existente?.Categoria ?? "", PesoKg = i.PesoKg ?? existente?.PesoKg,
            LargoCm = i.LargoCm ?? existente?.LargoCm, AnchoCm = i.AnchoCm ?? existente?.AnchoCm, AltoCm = i.AltoCm ?? existente?.AltoCm,
            PrecioMexico = referencia ?? 0m,
        };
        var r = await rent.ProductoAsync(p, DateOnly.FromDateTime(DateTime.Now), costoManual: costo);
        var canales = r.Canales.Select(c => new Canal(c.PlataformaId, c.Plataforma, c.Minimo is { } m ? Math.Ceiling(m) : null, c.Sugerido,
            c.AlSugerido?.Utilidad, c.AlSugerido?.MargenPct, c.Ok, c.Error)).ToList();
        var mejor = canales.FirstOrDefault(c => c.PlataformaId == r.MejorCanal && c.Ok);
        if (mejor is null)
        {
            // Sin tarifas capturadas: venta directa sin comisiones con el objetivo aplicable.
            parametros ??= await db.Parametros.AsNoTracking().Where(x => x.Activo).ToListAsync();
            fiscal ??= await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync() ?? new ConfiguracionFiscal();
            if (Rentabilidad.ParametroPara(parametros, p, "") is { } par)
            {
                var d = MotorPrecios.Calcular(costo.Value, new ComponentesTarifa(), Rentabilidad.Fiscal(fiscal), Rentabilidad.Objetivo(par));
                if (d.Ok) mejor = new Canal("", "Venta directa sin comisiones", Math.Ceiling(d.Minimo!.Value), d.Sugerido, d.AlSugerido?.Utilidad, d.AlSugerido?.MargenPct, true, null);
            }
            avisos.Add("No hay tarifas de plataformas capturadas: se calculó como venta directa sin comisiones. Captura las tarifas en Plataformas y tarifas para un precio por canal.");
        }
        if (mejor is not null && referencia is { } rf)
        {
            if (mejor.Minimo > rf) avisos.Add($"El mínimo (${mejor.Minimo:N0}) es mayor que la referencia de mercado (${rf:N0}): a precio de mercado este juguete no recupera su costo; considera otro canal o liquidarlo.");
            else if (mejor.Sugerido > rf) avisos.Add($"El sugerido (${mejor.Sugerido:N0}) está arriba de la referencia de mercado (${rf:N0}): publícalo entre el mínimo y la referencia.");
        }
        return new Resultado(mejor is not null, referencia, origenRef, costo, origenCosto, est, mejor,
            canales.Where(c => c.Ok).OrderByDescending(c => c.Utilidad).ToList(), r.FiscalPendiente, avisos);
    }
}

public static class SugerenciaPrecioApi
{
    public static void MapSugerenciaPrecio(this WebApplication app)
    {
        app.MapPost("/api/productos/sugerir-precio", async (SugerirPrecioIn i, AppDbContext db) =>
        {
            if (i.Cantidad < 1) return Results.BadRequest(new Error("Indica la cantidad de piezas."));
            if (i.PrecioMexico < 0 || i.ValorReferencia < 0) return Results.BadRequest(new Error("Los precios no pueden ser negativos."));
            var existente = i.ProductoId is { Length: > 0 } pid ? await db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid) : null;
            var r = await new SugeridorPrecios(db).SugerirAsync(i, existente);
            return Results.Ok(new
            {
                ok = r.Ok, referencia = r.Referencia, origenReferencia = r.OrigenReferencia,
                costo = r.Costo is null ? null : new { unitario = r.Costo, origen = r.OrigenCosto, estimacion = r.Estimacion },
                canal = r.Canal, canales = r.Canales, precioObjetivo = r.PrecioObjetivo, precioMinimo = r.PrecioMinimo,
                fiscalPendiente = r.FiscalPendiente, avisos = r.Avisos,
            });
        }).RequireAuthorization();

        // Recalcula el precio mínimo de todos los juguetes con el mismo criterio del escaneo: mínimo del mejor canal
        // (costo real o estimado + comisiones y envíos + margen mínimo). Sin datos suficientes, el mínimo actual no se toca.
        app.MapPost("/api/productos/recalcular-minimos", (AppDbContext db) => Api.Bloqueado(async () =>
        {
            var sug = new SugeridorPrecios(db);
            var ps = await db.Productos.OrderBy(p => p.Codigo).ToListAsync();
            int actualizados = 0, conCostoReal = 0, conCostoEstimado = 0;
            var sinDatos = new List<string>();
            foreach (var p in ps)
            {
                var r = await sug.SugerirAsync(new SugerirPrecioIn(p.Id, null, Math.Max(1, p.Cantidad), p.PrecioMexico, p.ValorReferencia,
                    p.Categoria, p.PesoKg, p.LargoCm, p.AnchoCm, p.AltoCm), p);
                if (!r.Ok || r.PrecioMinimo is not { } min) { sinDatos.Add(p.Codigo); continue; }
                if (r.OrigenCosto == "real") conCostoReal++; else conCostoEstimado++;
                if (min != p.PrecioMinimo) { p.PrecioMinimo = min; p.ActualizadoUtc = DateTime.UtcNow; actualizados++; }
            }
            await db.GuardarAsync();
            return Results.Ok(new { actualizados, conCostoReal, conCostoEstimado, sinDatos = sinDatos.Count, sinDatosCodigos = sinDatos.Take(20) });
        })).RequireAuthorization();
    }
}
