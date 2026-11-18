namespace PalletControl.Models;

/// <summary>Canal de venta (Mercado Libre Clásica, Facebook Marketplace, venta física…). Catálogo administrable.</summary>
public class Plataforma
{
    public string Id { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    /// <summary>marketplace, local o fisica.</summary>
    public string Tipo { get; set; } = "marketplace";
    public bool Activa { get; set; } = true;
    public int Orden { get; set; }
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Regla de tarifa de una plataforma con vigencia. Los criterios vacíos aplican a cualquier valor.
/// Los porcentajes se capturan como 15 = 15 %. Ningún valor viene precargado: se capturan del contrato o sitio oficial de cada plataforma.
/// </summary>
public class TarifaPlataforma
{
    public string Id { get; set; } = "";
    public string PlataformaId { get; set; } = "";
    public string Nombre { get; set; } = "";
    // ---- Criterios ----
    public string? TipoPublicacion { get; set; }
    public string? Modalidad { get; set; }
    public string? Categoria { get; set; }
    public string? MedioPago { get; set; }
    public decimal? PrecioMin { get; set; }
    public decimal? PrecioMax { get; set; }
    public decimal? PesoMinKg { get; set; }
    public decimal? PesoMaxKg { get; set; }
    public decimal? LadoMaxCm { get; set; }
    // ---- Cargos ----
    public decimal ComisionPct { get; set; }
    public decimal ComisionMedioPagoPct { get; set; }
    public decimal CargoFijo { get; set; }
    public decimal CostoLogistico { get; set; }
    public decimal EnvioAbsorbido { get; set; }
    public decimal Almacenamiento { get; set; }
    public decimal Preparacion { get; set; }
    public decimal Empaque { get; set; }
    public decimal PublicidadPct { get; set; }
    public decimal PublicidadFija { get; set; }
    public decimal ReservaDevolucionesPct { get; set; }
    public decimal RetencionIsrPct { get; set; }
    public decimal RetencionIvaPct { get; set; }
    // ---- Vigencia ----
    public DateOnly VigenteDesde { get; set; }
    public DateOnly? VigenteHasta { get; set; }
    public bool Activa { get; set; } = true;
    /// <summary>Se usa en el análisis general cuando no se indica tipo de publicación ni modalidad.</summary>
    public bool Predeterminada { get; set; }
    /// <summary>manual; en la Fase 5, el nombre de la API oficial que la actualizó.</summary>
    public string Fuente { get; set; } = "manual";
    /// <summary>Tarifa a la que reemplaza (cadena de vigencias).</summary>
    public string? ReemplazaA { get; set; }
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Objetivo de rentabilidad. Ámbito por los campos opcionales; gana el más específico:
/// producto+plataforma > producto > categoría+plataforma > categoría > plataforma > global.
/// </summary>
public class ParametroRentabilidad
{
    public string Id { get; set; } = "";
    public string? Categoria { get; set; }
    public string? ProductoId { get; set; }
    public string? PlataformaId { get; set; }
    /// <summary>margen, markup o utilidad_fija.</summary>
    public string Esquema { get; set; } = "margen";
    /// <summary>% para margen y markup; pesos para utilidad fija.</summary>
    public decimal Valor { get; set; }
    public decimal MargenMinimoPct { get; set; }
    public decimal DescuentoPromocionalPct { get; set; }
    /// <summary>centavo, peso, diez o terminacion9.</summary>
    public string Redondeo { get; set; } = "terminacion9";
    public bool Activo { get; set; } = true;
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Situación fiscal (una fila, Id = 1). Con régimen "pendiente" los impuestos van en cero y se muestra un aviso.</summary>
public class ConfiguracionFiscal
{
    public int Id { get; set; } = 1;
    /// <summary>pendiente, resico, plataformas_tecnologicas, actividad_empresarial o persona_moral.</summary>
    public string Regimen { get; set; } = "pendiente";
    public bool CobraIva { get; set; }
    public decimal TasaIvaPct { get; set; } = 16m;
    public string Notas { get; set; } = "";
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Precios calculados y guardados de un producto en una plataforma (historial; uno vigente por par).</summary>
public class PrecioProducto
{
    public string Id { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public string PlataformaId { get; set; } = "";
    public string? TarifaId { get; set; }
    public string? ParametroId { get; set; }
    public DateOnly FechaTarifa { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public string Usuario { get; set; } = "";
    public decimal Costo { get; set; }
    public decimal Equilibrio { get; set; }
    public decimal Minimo { get; set; }
    public decimal Objetivo { get; set; }
    public decimal Sugerido { get; set; }
    public decimal Promocional { get; set; }
    public decimal Utilidad { get; set; }
    public decimal MargenPct { get; set; }
    public bool Vigente { get; set; }
}
