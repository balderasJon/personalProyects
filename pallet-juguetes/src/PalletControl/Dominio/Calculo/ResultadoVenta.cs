namespace PalletControl.Dominio.Calculo;

/// <summary>Renglón vendido: piezas de un lote con su costo histórico (congelado al registrar la venta).</summary>
/// <param name="Devueltas">Piezas devueltas por el cliente.</param>
/// <param name="DevueltasAMerma">De las devueltas, cuántas no regresan a inventario vendible: su costo sigue siendo pérdida.</param>
public record LineaVenta(int Cantidad, decimal PrecioUnitario, decimal CostoUnitario, int Devueltas = 0, int DevueltasAMerma = 0);

/// <summary>Gasto de una venta. Deducido = lo descuenta la plataforma del depósito; si no, lo pagas tú.</summary>
public record GastoVentaCalc(decimal Monto, bool DeducidoPorPlataforma);

/// <summary>Datos de una venta para calcular su resultado real.</summary>
/// <param name="FactorIngreso">1/(1+IVA) si se cobró IVA en la venta; 1 si no (guardado al registrarla).</param>
/// <param name="Reembolsos">Montos devueltos al cliente (con IVA incluido, como se pagaron).</param>
/// <param name="ImporteCobrado">Lo que efectivamente llegó a tu cuenta. Null = no cobrada.</param>
public record DatosVenta(
    IReadOnlyList<LineaVenta> Lineas, decimal Descuento, decimal FactorIngreso, IReadOnlyList<GastoVentaCalc> Gastos,
    decimal Reembolsos, bool Cancelada, decimal? ImporteCobrado, decimal RetencionIsr = 0, decimal RetencionIva = 0);

/// <summary>Resultado real de una venta. Separa ingresos, utilidad comercial, impuestos y flujo de efectivo.</summary>
public record ResultadoReal(
    decimal IngresoBruto, decimal IvaTrasladado, decimal IngresoNeto,
    decimal Reembolsos, decimal ReembolsosNetos,
    decimal GastosDeducidos, decimal GastosPropios, decimal GastosTotales,
    decimal CostoVendido, decimal CostoMerma,
    decimal Utilidad, decimal? MargenPct,
    decimal RetencionIsr, decimal RetencionIva,
    bool Cobrada, decimal ImporteCobrado, decimal EfectivoNeto, decimal PorCobrarEstimado);

/// <summary>
/// Utilidad real de una venta:
///   ingreso bruto = Σ(cantidad × precio) − descuento (lo que paga el cliente)
///   ingreso neto  = ingreso bruto − IVA trasladado
///   utilidad      = ingreso neto − reembolsos sin IVA − gastos (deducidos y propios) − costo de lo vendido − costo de devoluciones a merma
/// Las piezas devueltas que regresan a inventario vendible recuperan su costo (no se cuentan como vendidas).
/// Una venta cancelada no tiene ingreso ni costo; sus gastos (si los hubo) son pérdida.
/// Efectivo neto = importe cobrado − gastos que pagaste tú − reembolsos. Solo cuenta si se registró el cobro.
/// </summary>
public static class CalculoVenta
{
    public static ResultadoReal Calcular(DatosVenta v)
    {
        var c = Dinero.Centavos;
        if (v.Descuento < 0) throw new ArgumentOutOfRangeException(nameof(v.Descuento));
        foreach (var l in v.Lineas)
            if (l.Cantidad <= 0 || l.Devueltas < 0 || l.Devueltas > l.Cantidad || l.DevueltasAMerma < 0 || l.DevueltasAMerma > l.Devueltas)
                throw new ArgumentException("Cantidades de la venta no válidas.");

        var deducidos = c(v.Gastos.Where(g => g.DeducidoPorPlataforma).Sum(g => g.Monto));
        var propios = c(v.Gastos.Where(g => !g.DeducidoPorPlataforma).Sum(g => g.Monto));
        var gastos = deducidos + propios;

        // Lo reembolsado siempre sale de tu cuenta; en una venta cancelada no afecta la utilidad (no hubo ingreso).
        decimal bruto = 0, iva = 0, neto = 0, reembNeto = 0, costo = 0, merma = 0, reemb = c(v.Reembolsos);
        if (!v.Cancelada)
        {
            bruto = c(v.Lineas.Sum(l => l.Cantidad * l.PrecioUnitario) - v.Descuento);
            neto = c(bruto * v.FactorIngreso);
            iva = bruto - neto;
            reembNeto = c(reemb * v.FactorIngreso);
            costo = c(v.Lineas.Sum(l => (l.Cantidad - l.Devueltas) * l.CostoUnitario));
            merma = c(v.Lineas.Sum(l => l.DevueltasAMerma * l.CostoUnitario));
        }
        var util = neto - reembNeto - gastos - costo - merma;
        var base_ = neto - reembNeto;
        decimal? margen = base_ > 0 ? Math.Round(util / base_ * 100m, 2, MidpointRounding.AwayFromZero) : null;
        var cobrada = v.ImporteCobrado is not null;
        var cobrado = c(v.ImporteCobrado ?? 0m);
        var efectivo = cobrada ? cobrado - propios - reemb : 0m;
        var porCobrar = cobrada || v.Cancelada ? 0m : Math.Max(0m, bruto - deducidos - c(v.RetencionIsr) - c(v.RetencionIva));
        return new ResultadoReal(bruto, iva, neto, reemb, reembNeto, deducidos, propios, gastos, costo, merma, util, margen,
            c(v.RetencionIsr), c(v.RetencionIva), cobrada, cobrado, efectivo, porCobrar);
    }

    /// <summary>
    /// Asigna piezas a lotes (primero el lote elegido; si no, en el orden dado: lotes vendibles del pallet más antiguo).
    /// Devuelve (loteId, piezas) o null si no alcanza.
    /// </summary>
    public static List<(string LoteId, int Cantidad)>? AsignarLotes(int cantidad, IReadOnlyList<(string LoteId, int Disponibles)> lotesEnOrden)
    {
        if (cantidad <= 0) return null;
        var res = new List<(string, int)>();
        var falta = cantidad;
        foreach (var (id, disp) in lotesEnOrden)
        {
            if (falta == 0) break;
            var toma = Math.Min(disp, falta);
            if (toma <= 0) continue;
            res.Add((id, toma));
            falta -= toma;
        }
        return falta == 0 ? res : null;
    }

    /// <summary>
    /// Reparte un monto (p. ej. el efectivo de una venta) entre pallets en proporción al importe de cada renglón,
    /// cuadrando al centavo con el residuo mayor.
    /// </summary>
    public static Dictionary<string, decimal> RepartirPorPeso(decimal monto, IReadOnlyList<(string Clave, decimal Peso)> pesos)
    {
        var res = new Dictionary<string, decimal>();
        var W = pesos.Sum(p => p.Peso);
        if (pesos.Count == 0) return res;
        if (W <= 0) { res[pesos[0].Clave] = monto; return res; }
        var cent = (long)Math.Round(monto * 100m, MidpointRounding.AwayFromZero);
        var signo = Math.Sign(cent); cent = Math.Abs(cent);
        var exactos = pesos.GroupBy(p => p.Clave).Select(g => (Clave: g.Key, E: cent * g.Sum(x => x.Peso) / W)).ToList();
        var baseC = exactos.ToDictionary(x => x.Clave, x => (long)Math.Floor(x.E));
        var faltan = cent - baseC.Values.Sum();
        foreach (var x in exactos.OrderByDescending(x => x.E - Math.Floor(x.E)).ThenBy(x => x.Clave, StringComparer.Ordinal).Take((int)faltan)) baseC[x.Clave]++;
        foreach (var kv in baseC) res[kv.Key] = signo * kv.Value / 100m;
        return res;
    }
}
