using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

public class SinLoginFactory : AppFactory
{
    protected override bool Requerido => false;
}

/// <summary>Modo sin inicio de sesión (configuración predeterminada desde el 2026-11-20): todo entra como el usuario local.</summary>
public class SinLoginTests : IClassFixture<SinLoginFactory>
{
    private readonly SinLoginFactory f;
    public SinLoginTests(SinLoginFactory f) => this.f = f;

    [Fact]
    public async Task SinUsuarioNiContrasena_SeUsaLaAplicacionCompleta()
    {
        var c = f.CreateClient();
        var est = await (await c.GetAsync("/api/auth/estado")).Json();
        Assert.True(est.GetProperty("sinLogin").GetBoolean());
        Assert.True(est.GetProperty("autenticado").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/estado")).StatusCode);
        var pal = await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "Sin login", moneda = "MXN", costoOriginal = 100m, tipoCambio = 1m })).Json(201);
        Assert.Equal("Sin login", pal.Str("nombre"));
    }
}
