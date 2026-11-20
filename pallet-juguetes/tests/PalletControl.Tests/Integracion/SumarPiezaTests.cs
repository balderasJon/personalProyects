using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>Escanear un código que ya existe suma una pieza al lote vendible del pallet más reciente.</summary>
public class SumarPiezaTests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    public SumarPiezaTests(AppFactory f) => this.f = f;

    [Fact]
    public async Task SumarPieza_AumentaCantidadYRegistraMovimiento_YSePuedeDeshacer()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "SP", moneda = "MXN", costoOriginal = 500m, tipoCambio = 1m })).Json(201)).Str("id");
        var prod = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "SP-1", codigoBarras = "887961938814", nombre = "Pista", cantidad = 2, palletId = pal, valorReferencia = 300m })).Json(201)).Str("id");

        // El código ya existe: la búsqueda lo encuentra en el inventario
        var b = await (await c.GetAsync("/api/codigo-barras/887961938814")).Json();
        Assert.Equal("inventario", b.Str("origen"));

        var r = await (await c.PostAsync($"/api/productos/{prod}/sumar-pieza", null)).Json();
        Assert.Equal(3, r.GetProperty("cantidad").GetInt32());
        r = await (await c.PostAsync($"/api/productos/{prod}/sumar-pieza", null)).Json();
        Assert.Equal(4, r.GetProperty("cantidad").GetInt32());

        var est = await (await c.GetAsync("/api/estado")).Json();
        var p = est.GetProperty("productos").EnumerateArray().Single(x => x.Str("id") == prod);
        Assert.Equal(4, p.GetProperty("cantidad").GetInt32());

        // Deshacer = ajuste de −1 al mismo lote
        var lote = r.Str("loteId");
        await (await c.PostAsJsonAsync($"/api/lotes/{lote}/ajuste", new { cantidad = -1, motivo = "Deshacer escaneo de código de barras" })).Json();
        p = (await (await c.GetAsync("/api/estado")).Json()).GetProperty("productos").EnumerateArray().Single(x => x.Str("id") == prod);
        Assert.Equal(3, p.GetProperty("cantidad").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/productos/noexiste/sumar-pieza", null)).StatusCode);
    }
}
