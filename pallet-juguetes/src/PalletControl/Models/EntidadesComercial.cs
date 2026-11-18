namespace PalletControl.Models;

/// <summary>
/// Precio observado de un producto similar en el mercado (Fase 4). Un precio publicado no es una venta realizada.
/// Si no hay UPC/EAN, el producto se identifica por foto, marca, modelo y características.
/// </summary>
public class Competencia
{
    public string Id { get; set; } = "";
    /// <summary>Tu juguete con el que se compara.</summary>
    public string ProductoId { get; set; } = "";
    /// <summary>Nombre del producto de la competencia.</summary>
    public string ProductoReferencia { get; set; } = "";
    /// <summary>Plataforma del catálogo; si no está (p. ej. una tienda), se usa PlataformaTexto.</summary>
    public string? PlataformaId { get; set; }
    public string PlataformaTexto { get; set; } = "";
    public string Marca { get; set; } = "";
    public string Modelo { get; set; } = "";
    /// <summary>Código de condición (nuevo_sellado, usado_bueno…) o vacío si no se sabe.</summary>
    public string Condicion { get; set; } = "";
    public string CodigoBarras { get; set; } = "";
    public string Caracteristicas { get; set; } = "";
    public decimal PrecioPublicado { get; set; }
    public decimal PrecioEnvio { get; set; }
    /// <summary>Publicado + envío: es el que se compara.</summary>
    public decimal PrecioTotal { get; set; }
    public string Url { get; set; } = "";
    public string Vendedor { get; set; } = "";
    public DateOnly FechaConsulta { get; set; }
    public string Observaciones { get; set; } = "";
    public string? ImagenArchivo { get; set; }
    /// <summary>false = descartada (ya no se usa en la comparación, se conserva en el historial).</summary>
    public bool Vigente { get; set; } = true;
    /// <summary>manual; en la Fase 5, la API oficial que la obtuvo.</summary>
    public string Fuente { get; set; } = "manual";
    public string Usuario { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}
