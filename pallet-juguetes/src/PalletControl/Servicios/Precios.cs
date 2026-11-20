using PalletControl.Models;

namespace PalletControl.Servicios;

/// <summary>Reglas de precio compartidas por la API.</summary>
public static class Precios
{
    /// <summary>Precios por tienda de un juguete: Liverpool, Mercado Libre, Walmart, Juguetron, Juguetibici y Amazon México.</summary>
    public static decimal[] DeTiendas(Producto p) =>
        [p.PrecioLiverpool, p.PrecioMercadoLibre, p.PrecioWalmart, p.PrecioJuguetron, p.PrecioJuguetibici, p.PrecioAmazon];

    /// <summary>Promedio de los precios por tienda capturados (mayores a cero); null si no hay ninguno.</summary>
    public static decimal? PromedioTiendas(params decimal[] tiendas)
    {
        var p = tiendas.Where(x => x > 0).ToArray();
        return p.Length == 0 ? null : Math.Round(p.Average(), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Método proporcional: la inversión se reparte según el valor en México de cada juguete.
    /// factor = inversión ÷ Σ(cantidad × precio en México). Mínimo de un juguete = su precio en México × factor.
    /// Null si ningún juguete tiene precio en México.
    /// </summary>
    public static decimal? Factor(decimal inversion, IEnumerable<Producto> productos)
    {
        var valor = productos.Where(p => p.PrecioMexico > 0).Sum(p => p.Cantidad * p.PrecioMexico);
        return valor > 0 && inversion > 0 ? inversion / valor : null;
    }

    public static decimal Minimo(decimal precioMexico, decimal factor) =>
        Math.Round(precioMexico * factor, 0, MidpointRounding.AwayFromZero);
}
