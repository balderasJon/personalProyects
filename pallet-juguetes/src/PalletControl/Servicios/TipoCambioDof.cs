using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;

namespace PalletControl.Servicios;

/// <summary>
/// Define el tipo de cambio del día con la API pública de dolardof.com (publicación del DOF).
/// Si ya hay un valor para hoy (consultado o capturado a mano) no se vuelve a consultar.
/// </summary>
public class TipoCambioDof(HttpClient http, IConfiguration config, ILogger<TipoCambioDof> log)
{
    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static DateTime _ultimoIntentoFallido = DateTime.MinValue;
    private static DateOnly _diaVerificado;

    /// <summary>Último error al consultar, para mostrarlo en pantalla. Null si la última consulta funcionó.</summary>
    public static string? UltimoError { get; private set; }

    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);

    public async Task AsegurarHoyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (_diaVerificado == Hoy) return;                                   // camino rápido
        if (DateTime.UtcNow - _ultimoIntentoFallido < TimeSpan.FromMinutes(10)) return;
        await Candado.WaitAsync(ct);
        try
        {
            var c = await db.Configuracion.FirstAsync(ct);
            if (c.TipoCambioFecha == Hoy && c.TipoCambioUsd > 0) { _diaVerificado = Hoy; return; }
            await ConsultarYGuardarAsync(db, ct);
        }
        finally { Candado.Release(); }
    }

    /// <summary>Consulta el DOF y lo guarda como tipo de cambio de hoy (reemplaza un valor manual).</summary>
    public async Task<bool> ForzarAsync(AppDbContext db, CancellationToken ct = default)
    {
        await Candado.WaitAsync(ct);
        try { return await ConsultarYGuardarAsync(db, ct); }
        finally { Candado.Release(); }
    }

    private async Task<bool> ConsultarYGuardarAsync(AppDbContext db, CancellationToken ct)
    {
        var url = config["TipoCambio:UrlDolarDof"] ?? "https://dolardof.com/api/v1/dof/today";
        try
        {
            using var r = await http.GetAsync(url, ct);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"dolardof.com respondió {(int)r.StatusCode}");
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            var raiz = doc.RootElement;
            var valor = raiz.GetProperty("value").GetDecimal();
            var fecha = DateOnly.ParseExact(raiz.GetProperty("date").GetString()!, "yyyy-MM-dd");
            if (valor <= 0 || valor > 1000) throw new InvalidOperationException($"valor fuera de rango: {valor}");

            var c = await db.Configuracion.FirstAsync(ct);
            c.TipoCambioUsd = valor;
            c.TipoCambioFecha = Hoy;
            c.TipoCambioFuente = "DOF";
            c.TipoCambioPublicado = fecha;
            c.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            _diaVerificado = Hoy;
            UltimoError = null;
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException
                                     or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            log.LogWarning(e, "No se pudo consultar el tipo de cambio en dolardof.com");
            _ultimoIntentoFallido = DateTime.UtcNow;
            UltimoError = "No se pudo consultar dolardof.com. Revisa la conexión a internet o captura el valor a mano.";
            return false;
        }
    }

    /// <summary>Al capturar a mano, se marca el día como definido.</summary>
    public static void MarcarManual() { _diaVerificado = Hoy; UltimoError = null; }
}
