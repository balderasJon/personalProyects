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
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
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
    public string Responsable { get; set; } = "";
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
    public string Persona { get; set; } = "";
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
    public decimal HorasA { get; set; }
    public decimal HorasB { get; set; }
    public string Notas { get; set; } = "";
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Configuración general del negocio (una sola fila, Id = 1).</summary>
public class Configuracion
{
    public int Id { get; set; } = 1;
    public decimal Inversion { get; set; } = 49000m;
    public string SocioA { get; set; } = "Persona A";
    public string SocioB { get; set; } = "Persona B";
    public int RepartoA { get; set; } = 50;
    public int StockBajo { get; set; } = 1;
    public decimal MetaMensual { get; set; }
    /// <summary>Pesos por dólar, capturado por los socios. 0 = sin capturar (no se convierten precios).</summary>
    public decimal TipoCambioUsd { get; set; }
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
