namespace PalletControl.Dominio.Calculo;

/// <summary>Resumen de precios observados en el mercado. Los precios publicados no son ventas realizadas.</summary>
public record EstadisticaPrecios(int Observaciones, decimal Minimo, decimal Maximo, decimal Promedio, decimal Mediana);

/// <summary>
/// Posición de tu precio frente a la mediana del mercado.
/// barato: más de la banda por debajo; en_mercado: dentro de ±banda; caro: más de la banda por encima.
/// Lugar: 1 = el más barato entre tu precio y los de la competencia.
/// </summary>
public record PosicionCompetitiva(decimal PrecioPropio, decimal DiferenciaPct, string Posicion, int Lugar, int Total);

public record PiezaInventario(int Piezas, DateOnly Desde);
public record Antiguedad(decimal DiasPromedio, int Piezas0a30, int Piezas31a60, int Piezas61a90, int PiezasMas90);

public static class Indicadores
{
    private static decimal C(decimal v) => Dinero.Centavos(v);
    private static decimal P(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static EstadisticaPrecios? Estadistica(IEnumerable<decimal> precios)
    {
        var l = precios.OrderBy(x => x).ToList();
        if (l.Count == 0) return null;
        if (l.Any(x => x < 0)) throw new ArgumentException("Los precios no pueden ser negativos.");
        var med = l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2m;
        return new EstadisticaPrecios(l.Count, l[0], l[^1], C(l.Sum() / l.Count), C(med));
    }

    public static PosicionCompetitiva? Posicion(decimal precioPropio, IReadOnlyCollection<decimal> preciosMercado, decimal bandaPct)
    {
        var e = Estadistica(preciosMercado);
        if (e is null || e.Mediana <= 0 || precioPropio <= 0) return null;
        if (bandaPct < 0) throw new ArgumentOutOfRangeException(nameof(bandaPct));
        var dif = P((precioPropio - e.Mediana) / e.Mediana * 100m);
        var pos = dif < -bandaPct ? "barato" : dif > bandaPct ? "caro" : "en_mercado";
        // Empates: tu precio queda antes que los iguales de la competencia.
        var lugar = preciosMercado.Count(x => x < precioPropio) + 1;
        return new PosicionCompetitiva(precioPropio, dif, pos, lugar, preciosMercado.Count + 1);
    }

    /// <summary>Días promedio (ponderado por pieza) y piezas por rango de antigüedad al día de corte.</summary>
    public static Antiguedad Antiguedad(IEnumerable<PiezaInventario> piezas, DateOnly corte)
    {
        int a = 0, b = 0, c = 0, d = 0; long dias = 0; int total = 0;
        foreach (var p in piezas.Where(p => p.Piezas > 0))
        {
            var x = Math.Max(0, corte.DayNumber - p.Desde.DayNumber);
            dias += (long)x * p.Piezas; total += p.Piezas;
            if (x <= 30) a += p.Piezas; else if (x <= 60) b += p.Piezas; else if (x <= 90) c += p.Piezas; else d += p.Piezas;
        }
        return new Antiguedad(total > 0 ? Math.Round((decimal)dias / total, 1, MidpointRounding.AwayFromZero) : 0m, a, b, c, d);
    }

    /// <summary>Días sin movimiento: desde la última venta o, si nunca se ha vendido, desde su ingreso.</summary>
    public static int DiasSinMovimiento(DateOnly ingreso, DateOnly? ultimaVenta, DateOnly corte) =>
        Math.Max(0, corte.DayNumber - (ultimaVenta ?? ingreso).DayNumber);

    /// <summary>Rotación = piezas vendidas ÷ piezas recibidas, en %.</summary>
    public static decimal? RotacionPct(int vendidas, int recibidas) => recibidas > 0 ? P((decimal)vendidas / recibidas * 100m) : null;

    /// <summary>ROI en % = utilidad ÷ inversión. Null si no hay inversión.</summary>
    public static decimal? Roi(decimal utilidad, decimal inversion) => inversion > 0 ? P(utilidad / inversion * 100m) : null;
}
