using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>Levanta la aplicación real con una base SQLite temporal y sin servicios externos.</summary>
public class AppFactory : WebApplicationFactory<Program>
{
    /// <summary>Las pruebas de seguridad conservan el inicio de sesión; <see cref="SinLoginFactory"/> lo desactiva.</summary>
    protected virtual bool Requerido => true;
    public string Carpeta { get; } = Path.Combine(Path.GetTempPath(), "pallet-pruebas-" + Guid.NewGuid().ToString("N")[..8]);

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        Directory.CreateDirectory(Carpeta);
        b.UseSetting("ConnectionStrings:Pallet", $"Data Source={Path.Combine(Carpeta, "pallet.db")}");
        b.UseSetting("Auth:PermitirConfiguracionRemota", "true");
        b.UseSetting("Auth:Requerido", Requerido ? "true" : "false");
        b.UseSetting("Respaldos:Conservar", "0");
        // Sin internet en las pruebas: las URL externas apuntan a un puerto cerrado.
        b.UseSetting("TipoCambio:UrlDolarDof", "http://127.0.0.1:9/");
        b.UseSetting("CodigoBarras:UrlUpcItemDb", "http://127.0.0.1:9/");
        b.UseSetting("Traduccion:UrlMyMemory", "http://127.0.0.1:9/");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Carpeta, true); } catch (IOException) { }
    }

    private HttpClient? unico;
    private readonly SemaphoreSlim candado = new(1, 1);

    /// <summary>Cliente con sesión, compartido por las pruebas de la misma clase (el primer usuario solo se crea una vez).</summary>
    public async Task<HttpClient> ClienteConSesionAsyncUnaVez()
    {
        await candado.WaitAsync();
        try { return unico ??= await ClienteConSesionAsync(); }
        finally { candado.Release(); }
    }

    /// <summary>Cliente con sesión iniciada (crea el primer usuario).</summary>
    public async Task<HttpClient> ClienteConSesionAsync()
    {
        var c = CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/configurar", new { usuario = "prueba", nombreCompleto = "Usuario de prueba", contrasena = "ClaveSegura1" });
        r.EnsureSuccessStatusCode();
        return c;
    }
}

public static class Http
{
    public static async Task<JsonElement> Json(this HttpResponseMessage r, int esperado = 200)
    {
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.True((int)r.StatusCode == esperado, $"Se esperaba {esperado} y llegó {(int)r.StatusCode}: {cuerpo}");
        return cuerpo.Length == 0 ? default : JsonDocument.Parse(cuerpo).RootElement.Clone();
    }
    public static decimal Dec(this JsonElement e, string p) => e.GetProperty(p).GetDecimal();
    public static string Str(this JsonElement e, string p) => e.GetProperty(p).GetString()!;
}
