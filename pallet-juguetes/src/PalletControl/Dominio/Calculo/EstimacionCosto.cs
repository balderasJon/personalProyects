namespace PalletControl.Dominio.Calculo;

/// <summary>Resultado de estimar el costo de una pieza antes de calcular el costeo del pallet.</summary>
/// <param name="ValorRegistrado">Σ cantidad × valor de referencia de los juguetes registrados (incluido el que se captura).</param>
/// <param name="BaseReparto">El mayor entre lo registrado y el valor de mercado estimado del pallet.</param>
/// <param name="PiezasSinValor">Piezas vendibles del pallet sin valor de referencia: no entran al reparto.</param>
public record CostoEstimado(decimal CostoUnitario, decimal Inversion, decimal ValorPieza, decimal ValorRegistrado, decimal BaseReparto,
    bool UsaValorMercadoPallet, int PiezasRegistradas, int PiezasSinValor);

/// <summary>
/// Estimación del costo por pieza con el método proporcional (el mismo del costeo del pallet):
///   costo = inversión × valor de la pieza ÷ base,  base = max(Σ valor registrado del pallet, valor de mercado estimado del pallet).
/// Con el valor de mercado del pallet (manifiesto) el estimado es estable desde el primer juguete; sin él, se reparte solo
/// entre los juguetes ya registrados y baja conforme se registran más.
/// </summary>
public static class EstimacionCosto
{
    /// <summary>Referencia de mercado en pesos: promedio de los precios bajo y alto (o el que exista) × tipo de cambio, al centavo.</summary>
    public static decimal? Referencia(decimal? bajo, decimal? alto, decimal tipoCambio)
    {
        if (tipoCambio <= 0) return null;
        var vals = new[] { bajo, alto }.Where(v => v is > 0).Select(v => v!.Value).ToList();
        return vals.Count == 0 ? null : Dinero.Centavos(vals.Average() * tipoCambio);
    }

    public static CostoEstimado? Calcular(decimal inversion, decimal? valorMercadoPallet, IEnumerable<(int Cantidad, decimal? Valor)> otros,
        int cantidad, decimal valorPieza)
    {
        if (inversion < 0 || valorPieza <= 0 || cantidad < 1) return null;
        var lista = otros.Where(o => o.Cantidad > 0).ToList();
        var registrado = lista.Where(o => o.Valor is > 0).Sum(o => o.Cantidad * o.Valor!.Value) + cantidad * valorPieza;
        var sinValor = lista.Where(o => o.Valor is not > 0).Sum(o => o.Cantidad);
        var usaMercado = valorMercadoPallet is { } vm && vm > registrado;
        var baseR = usaMercado ? valorMercadoPallet!.Value : registrado;
        return new CostoEstimado(Dinero.Centavos(inversion * valorPieza / baseR), inversion, valorPieza, Dinero.Centavos(registrado), Dinero.Centavos(baseR),
            usaMercado, lista.Sum(o => o.Cantidad) + cantidad, sinValor);
    }
}
