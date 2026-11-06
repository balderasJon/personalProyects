namespace PalletControl.Models;

/// <summary>Juguete (tipo de producto) dentro del pallet.</summary>
public class Producto
{
    public string Id { get; set; } = "";
    public string Codigo { get; set; } = "";
    /// <summary>EAN/UPC impreso en la caja (opcional, único cuando se captura).</summary>
    public string CodigoBarras { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Categoria { get; set; } = "";
    /// <summary>A, B o C.</summary>
    public string Clase { get; set; } = "B";
    /// <summary>nuevo, danado o incompleto.</summary>
    public string Estado { get; set; } = "nuevo";
    public int Cantidad { get; set; }
    public decimal PrecioLista { get; set; }
    public decimal PrecioObjetivo { get; set; }
    public decimal PrecioMinimo { get; set; }
    /// <summary>Precio del mismo juguete en tiendas de México (capturado a mano). 0 = sin capturar.</summary>
    public decimal PrecioMexico { get; set; }
    /// <summary>Descripción y datos técnicos (marca, modelo, color, medidas, peso...).</summary>
    public string Caracteristicas { get; set; } = "";
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Imagen de un juguete, guardada como archivo en datos/imagenes.</summary>
public class ProductoImagen
{
    public string Id { get; set; } = "";
    public string ProductoId { get; set; } = "";
    /// <summary>Nombre del archivo dentro de datos/imagenes.</summary>
    public string Archivo { get; set; } = "";
    /// <summary>subida o upcitemdb.</summary>
    public string Origen { get; set; } = "subida";
    public bool Principal { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Venta de una o varias piezas de un juguete.</summary>
public class Venta
{
    public string Id { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public int Cantidad { get; set; } = 1;
    public decimal Precio { get; set; }
    /// <summary>apartado, pagado, entregado o cancelado.</summary>
    public string Estado { get; set; } = "apartado";
    public DateOnly Fecha { get; set; }
    public DateOnly? FechaPago { get; set; }
    public string Canal { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Gasto de operación (empaque, traslados, publicidad, etc.).</summary>
public class Gasto
{
    public string Id { get; set; } = "";
    public string Tipo { get; set; } = "Otro";
    public decimal Monto { get; set; }
    public DateOnly Fecha { get; set; }
    public string Concepto { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Indicadores capturados a mano por semana (la llave es el lunes).</summary>
public class Semana
{
    public DateOnly Inicio { get; set; }
    public int Publicadas { get; set; }
    public int Mensajes { get; set; }
    public decimal Horas { get; set; }
    public string Notas { get; set; } = "";
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Configuración general del negocio (una sola fila, Id = 1).</summary>
public class Configuracion
{
    public int Id { get; set; } = 1;
    public decimal Inversion { get; set; } = 49000m;
    public int StockBajo { get; set; } = 1;
    public decimal MetaMensual { get; set; }
    /// <summary>Pesos por dólar. 0 = sin definir (no se convierten precios).</summary>
    public decimal TipoCambioUsd { get; set; }
    /// <summary>Día para el que se definió el tipo de cambio.</summary>
    public DateOnly? TipoCambioFecha { get; set; }
    /// <summary>DOF (consultado en dolardof.com) o Manual.</summary>
    public string TipoCambioFuente { get; set; } = "";
    /// <summary>Fecha de publicación en el DOF del valor consultado.</summary>
    public DateOnly? TipoCambioPublicado { get; set; }
    /// <summary>% que se descuenta al precio en México para sugerir el precio objetivo.</summary>
    public int DescuentoSugerido { get; set; } = 35;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Respuesta guardada de una consulta externa por código de barras (ahorra el límite diario).</summary>
public class ConsultaCodigoBarras
{
    public string Codigo { get; set; } = "";
    public string Fuente { get; set; } = "";
    public bool Encontrado { get; set; }
    public string Respuesta { get; set; } = "";
    public DateTime ConsultadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Contador global de cambios; los navegadores lo consultan para refrescarse.</summary>
public class VersionDatos
{
    public int Id { get; set; } = 1;
    public long Valor { get; set; }
}

public static class Catalogos
{
    public static readonly string[] Clases = ["A", "B", "C"];
    public static readonly string[] EstadosProducto = ["nuevo", "danado", "incompleto"];
    public static readonly string[] EstadosVenta = ["apartado", "pagado", "entregado", "cancelado"];
    public static readonly string[] Canales =
        ["Facebook Marketplace", "WhatsApp Business", "Grupos de Facebook", "Conocidos", "Bazar o escuela", "Instagram", "MercadoLibre", "Otro"];
    public static readonly string[] TiposGasto =
        ["Empaque", "Traslados y gasolina", "Publicidad", "Comisión de plataforma", "Impuestos", "Otro"];

    public static bool Cobrada(string estado) => estado is "pagado" or "entregado";

    public static DateOnly Lunes(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
