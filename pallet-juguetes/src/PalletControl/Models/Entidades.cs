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
    /// <summary>
    /// Precio del juguete en México. Si hay precios por tienda, es su promedio; si no, el capturado a mano. 0 = sin dato.
    /// </summary>
    public decimal PrecioMexico { get; set; }
    /// <summary>Precios vistos en cada tienda (capturados a mano). 0 = sin dato.</summary>
    public decimal PrecioLiverpool { get; set; }
    public decimal PrecioMercadoLibre { get; set; }
    public decimal PrecioWalmart { get; set; }
    /// <summary>Descripción y datos técnicos (marca, modelo, color, medidas, peso...).</summary>
    public string Caracteristicas { get; set; } = "";
    public string Notas { get; set; } = "";
    // ---- Datos comerciales (Fase 1). El SKU es Codigo. ----
    public string Marca { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string Subcategoria { get; set; } = "";
    public string DescripcionComercial { get; set; } = "";
    public string AccesoriosIncluidos { get; set; } = "";
    public string EdadRecomendada { get; set; } = "";
    public decimal? PesoKg { get; set; }
    public decimal? LargoCm { get; set; }
    public decimal? AnchoCm { get; set; }
    public decimal? AltoCm { get; set; }
    /// <summary>en_revision, listo, liquidacion o retirado.</summary>
    public string EstadoComercial { get; set; } = "en_revision";
    public DateOnly? FechaIngreso { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Pallet comprado. La inversión total se reparte entre sus lotes con el costeo.</summary>
public class Pallet
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Proveedor { get; set; } = "";
    public DateOnly FechaCompra { get; set; }
    public string PaisOrigen { get; set; } = "";
    /// <summary>MXN o USD.</summary>
    public string Moneda { get; set; } = "MXN";
    /// <summary>Costo en la moneda de compra. Ya incluye la importación.</summary>
    public decimal CostoOriginal { get; set; }
    /// <summary>Pesos por unidad de moneda. 1 cuando la moneda es MXN.</summary>
    public decimal TipoCambio { get; set; } = 1m;
    /// <summary>DOF, Manual o vacío (MXN).</summary>
    public string TipoCambioFuente { get; set; } = "";
    public decimal CostoMxn { get; set; }
    /// <summary>Suma de gastos adicionales NO incluidos en el costo de compra.</summary>
    public decimal GastosAdicionales { get; set; }
    /// <summary>CostoMxn + GastosAdicionales.</summary>
    public decimal InversionTotal { get; set; }
    /// <summary>recepcion, en_venta, liquidado o cerrado.</summary>
    public string Estatus { get; set; } = "recepcion";
    /// <summary>equitativa, proporcional o manual.</summary>
    public string MetodoCosteo { get; set; } = "proporcional";
    /// <summary>absorber o perdida.</summary>
    public string ModoMerma { get; set; } = "absorber";
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Gasto relacionado con un pallet (flete adicional, maniobras, reparaciones...).</summary>
public class PalletGasto
{
    public string Id { get; set; } = "";
    public string PalletId { get; set; } = "";
    public DateOnly Fecha { get; set; }
    public string Tipo { get; set; } = "Otro";
    public string Concepto { get; set; } = "";
    public decimal MontoMxn { get; set; }
    /// <summary>true: ya viene en el costo de compra; solo se registra, no se suma a la inversión.</summary>
    public bool IncluidoEnCosto { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Lote de inventario: piezas de un juguete, de un pallet, en una condición.</summary>
public class InventarioLote
{
    public string Id { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public string PalletId { get; set; } = "";
    public string Condicion { get; set; } = "nuevo_sellado";
    public bool Comercializable { get; set; } = true;
    /// <summary>Para no comercializables: merma, liquidacion o recuperacion_parcial.</summary>
    public string Tratamiento { get; set; } = "";
    /// <summary>Piezas del lote (suma de sus movimientos).</summary>
    public int Cantidad { get; set; }
    /// <summary>Valor comercial relativo por pieza. Null = usar el precio en México del juguete.</summary>
    public decimal? ValorRelativoUnitario { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public string Notas { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Kárdex: cada entrada, ajuste o reclasificación de piezas de un lote.</summary>
public class MovimientoInventario
{
    public long Id { get; set; }
    public string LoteId { get; set; } = "";
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    /// <summary>inventario_inicial, recepcion, ajuste, reclasificacion_salida o reclasificacion_entrada.</summary>
    public string Tipo { get; set; } = "";
    /// <summary>Positivo entra, negativo sale.</summary>
    public int Cantidad { get; set; }
    public string Referencia { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string Usuario { get; set; } = "";
}

/// <summary>Una ejecución del costeo de un pallet. Solo una por pallet está vigente; las anteriores son historial.</summary>
public class CosteoCorrida
{
    public string Id { get; set; } = "";
    public string PalletId { get; set; } = "";
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    public string Usuario { get; set; } = "";
    public string Metodo { get; set; } = "";
    public string ModoMerma { get; set; } = "";
    public decimal TotalDistribuible { get; set; }
    public decimal TotalComercializable { get; set; }
    public decimal TotalMerma { get; set; }
    public decimal? CostoAbsorbidoPorMerma { get; set; }
    public int CentavosAjustados { get; set; }
    public bool Vigente { get; set; }
    /// <summary>Huella de los datos usados (inversión, lotes, valores). Si cambia, el costeo está desactualizado.</summary>
    public string Huella { get; set; } = "";
    public string Notas { get; set; } = "";
}

/// <summary>Costo asignado a un lote en una corrida (copia inmutable).</summary>
public class CostoLote
{
    public long Id { get; set; }
    public string CorridaId { get; set; } = "";
    public string LoteId { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public string Condicion { get; set; } = "";
    public bool Comercializable { get; set; }
    public int Cantidad { get; set; }
    public decimal? ValorRelativoUnitario { get; set; }
    public decimal Peso { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal CostoUnitario { get; set; }
    public bool EsMerma { get; set; }
}

/// <summary>Usuario con acceso a la aplicación. La contraseña se guarda como hash PBKDF2.</summary>
public class Usuario
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Sal { get; set; } = "";
    public int Iteraciones { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoAccesoUtc { get; set; }
}

/// <summary>Bitácora de altas, cambios y bajas.</summary>
public class Auditoria
{
    public long Id { get; set; }
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    public string Usuario { get; set; } = "";
    public string Entidad { get; set; } = "";
    public string EntidadId { get; set; } = "";
    /// <summary>alta, cambio o baja.</summary>
    public string Accion { get; set; } = "";
    /// <summary>JSON: { campo: [antes, después] }.</summary>
    public string Cambios { get; set; } = "";
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
    // ---- Fase 3: encabezado financiero. ProductoId/Cantidad/Precio resumen el renglón; el detalle por lote está en venta_detalle. ----
    public int Folio { get; set; }
    public string? PlataformaId { get; set; }
    public string? PublicacionId { get; set; }
    public string? TarifaId { get; set; }
    public decimal PrecioPublicado { get; set; }
    /// <summary>Descuento total sobre la venta (además del precio unitario pactado).</summary>
    public decimal Descuento { get; set; }
    /// <summary>1/(1+IVA) si se cobró IVA en esta venta; 1 si no. Se guarda al registrar.</summary>
    public decimal FactorIngreso { get; set; } = 1m;
    public decimal RetencionIsr { get; set; }
    public decimal RetencionIva { get; set; }
    /// <summary>Importe que efectivamente llegó a la cuenta. Null = no cobrada.</summary>
    public decimal? ImporteCobrado { get; set; }
    public DateOnly? FechaCobro { get; set; }
    public int CantidadDevuelta { get; set; }
    /// <summary>Costo histórico total de las piezas (suma del detalle).</summary>
    public decimal CostoTotal { get; set; }
    /// <summary>costeo, estimado_migracion, sin_costeo o mixto.</summary>
    public string CostoOrigen { get; set; } = "sin_costeo";
    public bool Migrada { get; set; }
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

    /// <summary>Condiciones de producto: código → (nombre, comercializable por defecto).</summary>
    public static readonly (string Codigo, string Nombre, bool Comercializable)[] Condiciones =
    [
        ("nuevo_sellado", "Nuevo sellado", true),
        ("nuevo_caja_abierta", "Nuevo con caja abierta", true),
        ("usado_bueno", "Usado en buenas condiciones", true),
        ("usado_detalles", "Usado con detalles", true),
        ("incompleto", "Incompleto", false),
        ("danado", "Dañado", false),
        ("no_comercializable", "No comercializable", false),
    ];
    public static bool CondicionValida(string c) => Condiciones.Any(x => x.Codigo == c);
    public static bool ComercializablePorDefecto(string c) => Condiciones.FirstOrDefault(x => x.Codigo == c).Comercializable;

    /// <summary>Equivalencia con el estado anterior (nuevo/danado/incompleto) — decisión 11.4.</summary>
    public static string CondicionDesdeEstado(string estado) => estado switch
    {
        "danado" => "danado",
        "incompleto" => "incompleto",
        _ => "nuevo_sellado",
    };
    public static string EstadoDesdeCondicion(string condicion) => condicion switch
    {
        "incompleto" => "incompleto",
        "danado" or "no_comercializable" => "danado",
        _ => "nuevo",
    };

    public static readonly string[] Tratamientos = ["merma", "liquidacion", "recuperacion_parcial"];
    public static readonly string[] EstadosComerciales = ["en_revision", "listo", "liquidacion", "retirado"];
    public static readonly string[] EstatusPallet = ["recepcion", "en_venta", "liquidado", "cerrado"];
    public static readonly string[] MetodosCosteo = ["equitativa", "proporcional", "manual"];
    public static readonly string[] ModosMerma = ["absorber", "perdida"];
    public static readonly string[] Monedas = ["MXN", "USD"];
    public static readonly string[] TiposGastoPallet =
        ["Flete adicional", "Maniobras y descarga", "Almacenaje", "Reparación o limpieza", "Impuestos o trámites", "Otro"];

    public static DateOnly Lunes(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
