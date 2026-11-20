using System.Net.Http.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>Precio en México = promedio de las tiendas con precio (Liverpool, Mercado Libre, Walmart, Juguetron, Juguetibici, Amazon México).</summary>
public class TiendasTests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    public TiendasTests(AppFactory f) => this.f = f;

    [Fact]
    public async Task PrecioEnMexico_PromediaLasSeisTiendas_YConservaLasNuevasSiNoSeEnvian()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "TD", moneda = "MXN", costoOriginal = 100m, tipoCambio = 1m })).Json(201)).Str("id");
        // Liverpool 600, Walmart 500, Juguetron 550, Amazon 450 (Mercado Libre y Juguetibici sin precio) → (600+500+550+450) ÷ 4 = 525
        var p = await (await c.PostAsJsonAsync("/api/productos", new
        {
            codigo = "TD-1", nombre = "Pista", cantidad = 1, palletId = pal,
            precioLiverpool = 600m, precioWalmart = 500m, precioJuguetron = 550m, precioAmazon = 450m,
        })).Json(201);
        Assert.Equal(525m, p.Dec("precioMexico"));
        Assert.Equal(550m, p.Dec("precioJuguetron"));
        Assert.Equal(0m, p.Dec("precioJuguetibici"));
        Assert.Equal(450m, p.Dec("precioAmazon"));

        // Un cliente que no envía las tiendas nuevas (p. ej. versión anterior) no las borra.
        var id = p.Str("id");
        var r = await (await c.PutAsJsonAsync($"/api/productos/{id}", new { codigo = "TD-1", nombre = "Pista", cantidad = 1, palletId = pal, precioLiverpool = 600m, precioWalmart = 500m })).Json();
        Assert.Equal(550m, r.Dec("precioJuguetron"));
        Assert.Equal(525m, r.Dec("precioMexico"));

        // Con Juguetibici capturado: (600+500+550+300+450) ÷ 5 = 480
        r = await (await c.PutAsJsonAsync($"/api/productos/{id}", new { codigo = "TD-1", nombre = "Pista", cantidad = 1, palletId = pal, precioLiverpool = 600m, precioWalmart = 500m, precioJuguetibici = 300m })).Json();
        Assert.Equal(480m, r.Dec("precioMexico"));
    }
}
