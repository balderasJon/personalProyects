namespace PalletControl.Models;

/// <summary>Renglón de una venta: piezas de un lote con su costo histórico (congelado al registrar la venta).</summary>
public class VentaDetalle
{
    public string Id { get; set; } = "";
    public string VentaId { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public string LoteId { get; set; } = "";
    public string PalletId { get; set; } = "";
    public int Cantidad { get; set; }
    /// <summary>Piezas devueltas por el cliente (regresan a su lote).</summary>
    public int Devueltas { get; set; }
    /// <summary>De las devueltas, cuántas pasaron a una condición no vendible: su costo es pérdida.</summary>
    public int DevueltasAMerma { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal CostoTotal { get; set; }
    public string? CostoCorridaId { get; set; }
    /// <summary>costeo, estimado_migracion o sin_costeo.</summary>
    public string CostoOrigen { get; set; } = "sin_costeo";
}

/// <summary>Gasto de una venta (comisión, envío, empaque…). Deducido = lo descuenta la plataforma del depósito.</summary>
public class VentaGasto
{
    public string Id { get; set; } = "";
    public string VentaId { get; set; } = "";
    public string? IncidenciaId { get; set; }
    /// <summary>comision, medio_pago, cargo_fijo, logistica, envio, almacenamiento, preparacion, empaque, publicidad, devolucion, reclamacion u otro.</summary>
    public string Tipo { get; set; } = "otro";
    public string Concepto { get; set; } = "";
    public decimal Monto { get; set; }
    public bool DeducidoPorPlataforma { get; set; }
    /// <summary>true si se precargó con la tarifa y aún no se confirma con el monto real.</summary>
    public bool Estimado { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Cancelación, devolución, reembolso o reclamación de una venta.</summary>
public class VentaIncidencia
{
    public string Id { get; set; } = "";
    public string VentaId { get; set; } = "";
    /// <summary>cancelacion, devolucion, reembolso o reclamacion.</summary>
    public string Tipo { get; set; } = "";
    public DateOnly Fecha { get; set; }
    /// <summary>Piezas devueltas (devolución).</summary>
    public int Cantidad { get; set; }
    /// <summary>Condición en la que regresan las piezas devueltas.</summary>
    public string? CondicionDestino { get; set; }
    /// <summary>Dinero devuelto al cliente (con IVA, como se pagó).</summary>
    public decimal MontoReembolsado { get; set; }
    /// <summary>abierta o cerrada (reclamaciones).</summary>
    public string Estado { get; set; } = "cerrada";
    public string Motivo { get; set; } = "";
    public string Usuario { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Publicación de un juguete en un canal. Las existencias reales se controlan de forma central en el inventario.</summary>
public class Publicacion
{
    public string Id { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public string PlataformaId { get; set; } = "";
    public string? LoteId { get; set; }
    /// <summary>Identificador de la publicación en la plataforma (para sincronizar en la Fase 5).</summary>
    public string IdExterno { get; set; } = "";
    public string Url { get; set; } = "";
    public decimal PrecioPublicado { get; set; }
    public DateOnly? FechaPublicacion { get; set; }
    /// <summary>pendiente, publicado, pausado, vendido, cancelado o eliminado.</summary>
    public string Estatus { get; set; } = "pendiente";
    public int Existencias { get; set; } = 1;
    /// <summary>Acción que hace falta en la plataforma: vacío, pausar o ajustar_existencias.</summary>
    public string RequiereAccion { get; set; } = "";
    public string Observaciones { get; set; } = "";
    /// <summary>manual; en la Fase 5, la API que la sincronizó.</summary>
    public string Fuente { get; set; } = "manual";
    // ---- Fase 5: vínculo con la cuenta conectada y último estado leído de la plataforma ----
    public string? CuentaIntegracionId { get; set; }
    public string EstatusRemoto { get; set; } = "";
    public int? ExistenciasRemotas { get; set; }
    public decimal? PrecioRemoto { get; set; }
    public DateTime? SincronizadoUtc { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}
