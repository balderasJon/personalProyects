using System.Net;
using System.Text.Json;
using PalletControl.Dominio.Integracion;

namespace PalletControl.Servicios.Integraciones;

/// <summary>Error al hablar con una plataforma: mensaje en español, sin secretos.</summary>
public class ErrorIntegracion(string mensaje, int estado = 502) : Exception(mensaje)
{
    public int Estado { get; } = estado;
}

public record CampoConfig(string Clave, string Etiqueta, bool Secreto, bool Requerido, string Ayuda, string? PorDefecto = null);
public record Capacidad(string Clave, string Nombre, bool Disponible, string Nota);
public record Tokens(string AccessToken, string? RefreshToken, int ExpiraSegundos, string? CuentaExterna);

/// <summary>Lo que necesita un conector para una llamada: HTTP, configuración, secretos descifrados y la bitácora.</summary>
public sealed class ContextoConector
{
    public required HttpClient Http { get; init; }
    public required IReadOnlyDictionary<string, string> Config { get; init; }
    public required IReadOnlyDictionary<string, string> Secretos { get; init; }
    public required string CuentaExterna { get; init; }
    public required Func<string, string, string, Task> Registrar { get; init; }
    public string C(string clave, string porDefecto = "") => Config.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : porDefecto;
    public string S(string clave) => Secretos.TryGetValue(clave, out var v) && !string.IsNullOrEmpty(v) ? v
        : throw new ErrorIntegracion($"Falta capturar «{clave}» en la cuenta.", 400);
}

/// <summary>
/// Conector de una plataforma. Cada uno declara solo las capacidades que su documentación oficial permite;
/// lo que no está disponible se resuelve a mano en la aplicación.
/// </summary>
public interface IConector
{
    string Codigo { get; }
    string Nombre { get; }
    /// <summary>Código de la plataforma del catálogo que se sugiere al crear la cuenta.</summary>
    string PlataformaSugerida { get; }
    IReadOnlyList<Capacidad> Capacidades { get; }
    CapacidadesEscritura Escritura { get; }
    bool LeePedidos { get; }
    IReadOnlyList<CampoConfig> Campos { get; }
    bool UsaOAuth { get; }
    string Documentacion { get; }
    string? UrlAutorizacion(ContextoConector ctx, string state, string? codeChallenge);
    Task<Tokens> IntercambiarCodigoAsync(ContextoConector ctx, string code, string? codeVerifier);
    Task<Tokens> RefrescarAsync(ContextoConector ctx);
    /// <summary>Comprueba la conexión. Regresa el identificador de la cuenta en la plataforma.</summary>
    Task<string> VerificarAsync(ContextoConector ctx);
    /// <summary>Lee anuncios. Con ids vacío, lista los de la cuenta (para vincularlos).</summary>
    Task<List<PublicacionRemota>> LeerPublicacionesAsync(ContextoConector ctx, IReadOnlyCollection<string> ids);
    Task AplicarAsync(ContextoConector ctx, CambioPropuesto cambio);
    Task<List<PedidoRemoto>> LeerPedidosAsync(ContextoConector ctx, DateOnly desde, IReadOnlyCollection<string> pedidosConocidos);
}

/// <summary>Utilidades HTTP comunes: reintento ante 429, errores legibles y lectura de JSON.</summary>
public static class Http
{
    public static async Task<JsonElement> EnviarAsync(ContextoConector ctx, Func<HttpRequestMessage> crear, string operacion)
    {
        for (var intento = 1; ; intento++)
        {
            using var req = crear();
            HttpResponseMessage r;
            try { r = await ctx.Http.SendAsync(req); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                await ctx.Registrar("error", operacion, "Sin conexión con la plataforma: " + e.GetType().Name);
                throw new ErrorIntegracion("No hay conexión con la plataforma. Revisa el internet de la computadora principal.");
            }
            using (r)
            {
                var cuerpo = await r.Content.ReadAsStringAsync();
                if (r.StatusCode == HttpStatusCode.TooManyRequests && intento < 3)
                {
                    await ctx.Registrar("aviso", operacion, "Límite de llamadas (429): se reintenta.");
                    await Task.Delay(TimeSpan.FromSeconds(r.Headers.RetryAfter?.Delta?.TotalSeconds is > 0 and < 30 ? r.Headers.RetryAfter!.Delta!.Value.TotalSeconds : 2 * intento));
                    continue;
                }
                if (!r.IsSuccessStatusCode)
                {
                    var msg = MensajeError(cuerpo);
                    await ctx.Registrar("error", operacion, $"HTTP {(int)r.StatusCode}: {Recortar(msg, 300)}");
                    throw new ErrorIntegracion((int)r.StatusCode switch
                    {
                        401 => "La plataforma rechazó la autorización (401). Vuelve a conectar la cuenta.",
                        403 => "La plataforma no permite esta operación (403): revisa los permisos de la aplicación o de la cuenta." + (msg.Length > 0 ? " " + Recortar(msg, 160) : ""),
                        429 => "La plataforma limitó las llamadas (429). Intenta de nuevo en unos minutos.",
                        _ => $"La plataforma respondió con error {(int)r.StatusCode}." + (msg.Length > 0 ? " " + Recortar(msg, 200) : ""),
                    }, (int)r.StatusCode is 400 or 404 or 409 ? 409 : 502);
                }
                await ctx.Registrar("info", operacion, $"HTTP {(int)r.StatusCode}");
                return cuerpo.Length == 0 ? default : JsonDocument.Parse(cuerpo).RootElement.Clone();
            }
        }
    }

    public static string MensajeError(string cuerpo)
    {
        try
        {
            var j = JsonDocument.Parse(cuerpo).RootElement;
            foreach (var k in new[] { "message", "error_description", "error" })
                if (j.ValueKind == JsonValueKind.Object && j.TryGetProperty(k, out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ValueKind == JsonValueKind.Object && v.TryGetProperty("message", out var m) ? m.GetString() ?? "" : v.ToString();
        }
        catch (JsonException) { }
        return "";
    }

    public static string Recortar(string s, int n) => s.Length > n ? s[..n] : s;
    public static string? Str(this JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;
    public static decimal? Dec(this JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
    public static int? Int(this JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    public static JsonElement Obj(this JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) ? v : default;
    public static IEnumerable<JsonElement> Arr(this JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
}
