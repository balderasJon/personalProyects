using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>El precio de lista siempre se guarda en pesos; si se captura en dólares se convierte con el tipo de cambio del día.</summary>
public class PrecioListaTests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    public PrecioListaTests(AppFactory f) => this.f = f;

    private static async Task<JsonElement> Producto(HttpClient c, string id) =>
        (await (await c.GetAsync("/api/estado")).Json()).GetProperty("productos").EnumerateArray().Single(p => p.Str("id") == id);

    [Fact]
    public async Task EnDolares_SeConvierteAPesosYGuardaElOriginal()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "PL", moneda = "MXN", costoOriginal = 100m, tipoCambio = 1m })).Json(201);
        object Juguete(decimal lista, decimal? usd) => new { codigo = "PL-1", nombre = "Lista", cantidad = 1, precioLista = lista, precioListaUsd = usd };

        // Sin tipo de cambio del día no se acepta un precio en dólares (nunca se guardan dólares como pesos)
        // (en las pruebas no hay internet: el tipo de cambio del DOF no se pudo consultar y está en 0)
        Assert.Contains("tipo de cambio", (await (await c.PostAsJsonAsync("/api/productos", Juguete(0m, 34.99m))).Json(400)).Str("mensaje"));

        await (await c.PutAsJsonAsync("/api/tipo-cambio", new { valor = 18.4677m })).Json();
        var id = (await (await c.PostAsJsonAsync("/api/productos", Juguete(0m, 34.99m))).Json(201)).Str("id");
        var p = await Producto(c, id);
        Assert.Equal(646m, p.Dec("precioLista"));            // 34.99 × 18.4677 = 646.19 → $646
        Assert.Equal(34.99m, p.Dec("precioListaUsd"));
        Assert.Equal(18.4677m, p.Dec("precioListaTipoCambio"));

        // Mismo monto en dólares con otro tipo de cambio: la referencia no cambia sola
        await (await c.PutAsJsonAsync("/api/tipo-cambio", new { valor = 20m })).Json();
        await (await c.PutAsJsonAsync($"/api/productos/{id}", Juguete(0m, 34.99m))).Json();
        Assert.Equal(646m, (await Producto(c, id)).Dec("precioLista"));

        // Otro monto en dólares: se convierte con el tipo de cambio de hoy
        await (await c.PutAsJsonAsync($"/api/productos/{id}", Juguete(0m, 30m))).Json();
        p = await Producto(c, id);
        Assert.Equal(600m, p.Dec("precioLista"));
        Assert.Equal(20m, p.Dec("precioListaTipoCambio"));

        // Cliente anterior (sin el campo) que no cambia el monto: conserva el dato en dólares
        await (await c.PutAsJsonAsync($"/api/productos/{id}", new { codigo = "PL-1", nombre = "Lista", cantidad = 1, precioLista = 600m })).Json();
        Assert.Equal(30m, (await Producto(c, id)).Dec("precioListaUsd"));

        // Capturado en pesos: se guarda tal cual y se borra el dato en dólares
        await (await c.PutAsJsonAsync($"/api/productos/{id}", Juguete(700m, 0m))).Json();
        p = await Producto(c, id);
        Assert.Equal(700m, p.Dec("precioLista"));
        Assert.Equal(JsonValueKind.Null, p.GetProperty("precioListaUsd").ValueKind);
        await (await c.PutAsJsonAsync($"/api/productos/{id}", Juguete(0m, -1m))).Json(400);
    }
}
