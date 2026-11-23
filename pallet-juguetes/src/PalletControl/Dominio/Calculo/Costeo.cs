namespace PalletControl.Dominio.Calculo;

/// <summary>Método para repartir la inversión de un pallet entre sus lotes.</summary>
public enum MetodoCosteo { Equitativa, Proporcional, Manual }

/// <summary>
/// Qué hacer con el costo de las piezas no comercializables (dañadas, incompletas...).
/// Absorber: se reparte solo entre las comercializables (las no comercializables quedan en 0).
/// Perdida: las no comercializables reciben su parte y esa parte se reconoce como merma.
/// </summary>
public enum ModoMerma { Absorber, Perdida }

/// <summary>Operaciones monetarias: siempre decimal, redondeo comercial al centavo.</summary>
public static class Dinero
{
    public static decimal Centavos(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Convierte a MXN y redondea al centavo. El tipo de cambio debe ser mayor a cero.</summary>
    public static decimal AMxn(decimal monto, string moneda, decimal tipoCambio)
    {
        if (monto < 0) throw new ArgumentOutOfRangeException(nameof(monto), "El monto no puede ser negativo.");
        if (moneda == "MXN") return Centavos(monto);
        if (tipoCambio <= 0) throw new ArgumentOutOfRangeException(nameof(tipoCambio), "El tipo de cambio debe ser mayor a cero.");
        return Centavos(monto * tipoCambio);
    }
}

public record GastoPallet(decimal MontoMxn, bool IncluidoEnCosto);

public static class InversionPallet
{
    /// <summary>
    /// Inversión total = costo convertido a MXN + gastos adicionales que NO están incluidos en el costo de compra.
    /// El costo de importación ya viene en el costo del pallet: no se suma otra vez.
    /// </summary>
    public static decimal Calcular(decimal costoMxn, IEnumerable<GastoPallet> gastos) =>
        Dinero.Centavos(costoMxn + gastos.Where(g => !g.IncluidoEnCosto).Sum(g => Dinero.Centavos(g.MontoMxn)));
}

/// <summary>Lote a costear. ValorRelativoUnitario se usa en el método proporcional; CostoManualTotal en el manual.</summary>
/// <param name="Descripcion">Cómo se nombra el lote en los mensajes (código y nombre del juguete, condición). Null = su Id.</param>
public record LoteCosteo(string LoteId, int Cantidad, bool Comercializable, decimal? ValorRelativoUnitario = null, decimal? CostoManualTotal = null,
    string? Descripcion = null);

public record LoteCosteado(string LoteId, int Cantidad, bool Comercializable, decimal Peso, decimal CostoTotal, decimal CostoUnitario, bool EsMerma);

public record ResultadoCosteo(
    bool Ok,
    IReadOnlyList<string> Errores,
    IReadOnlyList<LoteCosteado> Lotes,
    decimal TotalDistribuible,
    decimal TotalComercializable,
    decimal TotalMerma,
    /// <summary>Solo informativo (modo Absorber): costo que habrían cargado las piezas no comercializables.</summary>
    decimal? CostoAbsorbidoPorMerma,
    /// <summary>Centavos repartidos por redondeo (método del residuo mayor).</summary>
    int CentavosAjustados,
    /// <summary>Total distribuible − suma asignada. Debe ser 0.</summary>
    decimal DiferenciaCuadre);

/// <summary>
/// Distribuye la inversión total de un pallet entre sus lotes. Garantiza que
/// Σ costo asignado (comercializable) + Σ merma = inversión total, exacto al centavo.
/// </summary>
public static class DistribucionCostos
{
    public static ResultadoCosteo Calcular(decimal totalDistribuible, IReadOnlyList<LoteCosteo> lotes, MetodoCosteo metodo, ModoMerma modo)
    {
        var errores = new List<string>();
        var total = Dinero.Centavos(totalDistribuible);
        if (total < 0) errores.Add("La inversión total no puede ser negativa.");
        if (lotes.Any(l => l.Cantidad < 0)) errores.Add("Hay lotes con cantidad negativa.");
        if (lotes.Select(l => l.LoteId).Distinct().Count() != lotes.Count) errores.Add("Hay lotes repetidos.");
        var activos = lotes.Where(l => l.Cantidad > 0).ToList();
        if (activos.Count == 0) errores.Add("El pallet no tiene piezas para costear.");
        else if (!activos.Any(l => l.Comercializable)) errores.Add("El pallet no tiene piezas comercializables.");
        if (errores.Count > 0) return Fallo(total, errores);

        return metodo == MetodoCosteo.Manual
            ? Manual(total, lotes, errores)
            : Ponderado(total, lotes, activos, metodo, modo, errores);
    }

    private static ResultadoCosteo Manual(decimal total, IReadOnlyList<LoteCosteo> lotes, List<string> errores)
    {
        foreach (var l in lotes.Where(l => l.Cantidad > 0))
        {
            if (l.CostoManualTotal is null) errores.Add($"Falta el costo manual del lote {l.LoteId}.");
            else if (l.CostoManualTotal < 0) errores.Add($"El costo manual del lote {l.LoteId} es negativo.");
            else if (l.CostoManualTotal != Dinero.Centavos(l.CostoManualTotal.Value)) errores.Add($"El costo manual del lote {l.LoteId} tiene más de 2 decimales.");
        }
        if (errores.Count > 0) return Fallo(total, errores);

        var res = lotes.Select(l =>
        {
            var c = l.Cantidad > 0 ? l.CostoManualTotal!.Value : 0m;
            return new LoteCosteado(l.LoteId, l.Cantidad, l.Comercializable, 0m, c, Unitario(c, l.Cantidad), !l.Comercializable && c > 0);
        }).ToList();
        var suma = res.Sum(r => r.CostoTotal);
        if (suma != total)
            return Fallo(total, [$"La suma de los costos manuales ({suma:N2}) no es igual a la inversión total ({total:N2}). Diferencia: {total - suma:N2}."]);
        return Armar(total, res, null, 0);
    }

    private static ResultadoCosteo Ponderado(decimal total, IReadOnlyList<LoteCosteo> lotes, List<LoteCosteo> activos,
        MetodoCosteo metodo, ModoMerma modo, List<string> errores)
    {
        bool Participa(LoteCosteo l) => l.Cantidad > 0 && (l.Comercializable || modo == ModoMerma.Perdida);

        decimal? Peso(LoteCosteo l) => metodo == MetodoCosteo.Equitativa
            ? l.Cantidad
            : l.ValorRelativoUnitario is > 0 ? l.Cantidad * l.ValorRelativoUnitario.Value : null;

        if (metodo == MetodoCosteo.Proporcional)
            foreach (var l in activos.Where(Participa).Where(l => Peso(l) is null))
                errores.Add($"{(l.Descripcion is null ? $"El lote {l.LoteId}" : $"El juguete {l.Descripcion}")} no tiene valor comercial relativo: captura su precio en México (precios de tiendas), usa «Completar datos» o escribe el valor en su lote.");
        if (errores.Count > 0) return Fallo(total, errores);

        var participantes = lotes.Where(Participa).ToList();
        var W = participantes.Sum(l => Peso(l)!.Value);
        if (W <= 0) return Fallo(total, ["El valor relativo total es cero."]);

        // Reparto exacto en centavos con el método del residuo mayor.
        var totalCent = (long)(total * 100);
        var exactos = participantes.Select(l => (l.LoteId, Exacto: totalCent * Peso(l)!.Value / W)).ToList();
        var baseCent = exactos.ToDictionary(x => x.LoteId, x => (long)Math.Floor(x.Exacto));
        var faltan = totalCent - baseCent.Values.Sum();
        var orden = exactos.OrderByDescending(x => x.Exacto - Math.Floor(x.Exacto)).ThenBy(x => x.LoteId, StringComparer.Ordinal).ToList();
        for (var i = 0; i < faltan; i++) baseCent[orden[i % orden.Count].LoteId]++;

        var res = lotes.Select(l =>
        {
            var c = baseCent.TryGetValue(l.LoteId, out var cent) ? cent / 100m : 0m;
            var peso = Participa(l) ? Peso(l)!.Value : 0m;
            return new LoteCosteado(l.LoteId, l.Cantidad, l.Comercializable, peso, c, Unitario(c, l.Cantidad), !l.Comercializable && c > 0);
        }).ToList();

        // Informativo: qué habrían cargado las no comercializables si participaran.
        decimal? absorbido = null;
        if (modo == ModoMerma.Absorber)
        {
            var nc = activos.Where(l => !l.Comercializable).ToList();
            if (nc.Count == 0) absorbido = 0m;
            else if (nc.All(l => Peso(l) is not null))
            {
                var wnc = nc.Sum(l => Peso(l)!.Value);
                absorbido = Dinero.Centavos(total * wnc / (W + wnc));
            }
        }
        return Armar(total, res, absorbido, (int)faltan);
    }

    private static decimal Unitario(decimal costoTotal, int cantidad) =>
        cantidad > 0 ? Math.Round(costoTotal / cantidad, 4, MidpointRounding.AwayFromZero) : 0m;

    private static ResultadoCosteo Armar(decimal total, List<LoteCosteado> res, decimal? absorbido, int centavos)
    {
        var com = res.Where(r => !r.EsMerma).Sum(r => r.CostoTotal);
        var merma = res.Where(r => r.EsMerma).Sum(r => r.CostoTotal);
        var dif = total - com - merma;
        if (dif != 0) return Fallo(total, [$"Error interno de cuadre: diferencia de {dif:N2}."]);
        return new ResultadoCosteo(true, [], res, total, com, merma, absorbido, centavos, 0m);
    }

    private static ResultadoCosteo Fallo(decimal total, List<string> errores) =>
        new(false, errores, [], total, 0m, 0m, null, 0, total);
}
