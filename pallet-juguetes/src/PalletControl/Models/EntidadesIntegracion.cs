namespace PalletControl.Models;

/// <summary>
/// Cuenta de una plataforma conectada por API oficial (Fase 5).
/// Los secretos (Client Secret, tokens) se guardan cifrados con ASP.NET Data Protection y nunca se devuelven por la API.
/// </summary>
public class IntegracionCuenta
{
    public string Id { get; set; } = "";
    /// <summary>mercadolibre o meta.</summary>
    public string Conector { get; set; } = "";
    /// <summary>Plataforma del catálogo a la que se asignan las ventas importadas.</summary>
    public string PlataformaId { get; set; } = "";
    public string Nombre { get; set; } = "";
    /// <summary>sin_configurar, pendiente_autorizacion, conectada o error.</summary>
    public string Estado { get; set; } = "sin_configurar";
    /// <summary>Identificador de la cuenta en la plataforma (user_id de Mercado Libre, catálogo de Meta).</summary>
    public string CuentaExterna { get; set; } = "";
    /// <summary>Configuración no secreta en JSON (Client ID, redirect URI, ID de catálogo…).</summary>
    public string ConfigJson { get; set; } = "{}";
    /// <summary>Secretos cifrados (JSON protegido).</summary>
    public string SecretosCifrados { get; set; } = "";
    public DateTime? TokenExpiraUtc { get; set; }
    /// <summary>Estado OAuth en curso (anti-CSRF), de un solo uso.</summary>
    public string? OAuthState { get; set; }
    public DateTime? UltimaLecturaUtc { get; set; }
    public string UltimoError { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Una lectura de la plataforma: genera propuestas que el usuario revisa y confirma.</summary>
public class SincronizacionLote
{
    public string Id { get; set; } = "";
    public string CuentaId { get; set; } = "";
    /// <summary>pendiente, aplicado, parcial, descartado o error.</summary>
    public string Estado { get; set; } = "pendiente";
    public string Resumen { get; set; } = "";
    public string Usuario { get; set; } = "";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AplicadoUtc { get; set; }
}

/// <summary>Cambio propuesto. Nada se aplica en la plataforma ni en la aplicación sin confirmación.</summary>
public class SincronizacionPropuesta
{
    public string Id { get; set; } = "";
    public string LoteId { get; set; } = "";
    public int Orden { get; set; }
    /// <summary>pausar, activar, existencias, precio, nueva_venta, cancelar_venta, registrar_cobro, informativa.</summary>
    public string Tipo { get; set; } = "";
    /// <summary>plataforma (cambia el anuncio) o aplicacion (cambia tus datos).</summary>
    public string Destino { get; set; } = "plataforma";
    public string? PublicacionId { get; set; }
    public string? VentaId { get; set; }
    public string ReferenciaExterna { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Antes { get; set; } = "";
    public string Despues { get; set; } = "";
    public string DatosJson { get; set; } = "{}";
    /// <summary>pendiente, aplicada, rechazada, error o bloqueada (falta información para aplicarla).</summary>
    public string Estado { get; set; } = "pendiente";
    public string Resultado { get; set; } = "";
    public DateTime? AplicadaUtc { get; set; }
}

/// <summary>Bitácora técnica de llamadas a las plataformas (sin secretos).</summary>
public class IntegracionEvento
{
    public long Id { get; set; }
    public string CuentaId { get; set; } = "";
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;
    /// <summary>info, aviso o error.</summary>
    public string Nivel { get; set; } = "info";
    public string Operacion { get; set; } = "";
    public string Detalle { get; set; } = "";
    public string Usuario { get; set; } = "";
}
