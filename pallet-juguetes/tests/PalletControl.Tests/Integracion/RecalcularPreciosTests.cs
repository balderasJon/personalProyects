using System.Net.Http.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>«Recalcular precios»: vista previa sin guardar y luego objetivo + mínimo de todos con el costo y los márgenes actuales.</summary>
public class RecalcularPreciosTests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    public RecalcularPreciosTests(AppFactory f) => this.f = f;

    [Fact]
    public async Task VistaPrevia_NoGuarda_YAplicar_ActualizaObjetivoYMinimo()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "RP", moneda = "MXN", costoOriginal = 1000m, tipoCambio = 1m })).Json(201)).Str("id");
        // Objetivo «atorado» de cuando era el único juguete registrado
        var a = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "RP-A", nombre = "A", cantidad = 1, palletId = pal, valorReferencia = 600m, precioObjetivo = 1429m, precioMinimo = 1112m })).Json(201)).Str("id");
        await (await c.PostAsJsonAsync("/api/productos", new { codigo = "RP-B", nombre = "B", cantidad = 1, palletId = pal, valorReferencia = 400m })).Json(201);
        await (await c.PostAsJsonAsync($"/api/pallets/{pal}/costeo/aplicar", new { metodo = "proporcional" })).Json();

        // Costo real de A: 1,000 × 600 ÷ 1,000 = $600 → objetivo 600 ÷ 0.70 = 857.14 → $859; mínimo 600 ÷ 0.90 = 666.67 → $667
        var prev = await (await c.PostAsJsonAsync("/api/productos/recalcular-precios", new { aplicar = false })).Json();
        var ca = prev.GetProperty("cambios").EnumerateArray().Single(x => x.Str("productoId") == a);
        Assert.Equal(1429m, ca.Dec("objetivoAntes"));
        Assert.Equal(859m, ca.Dec("objetivoNuevo"));
        Assert.Equal(667m, ca.Dec("minimoNuevo"));
        Assert.Equal(1429m, Producto(await (await c.GetAsync("/api/estado")).Json(), a).Dec("precioObjetivo"));   // la vista previa no guarda

        await (await c.PostAsJsonAsync("/api/productos/recalcular-precios", new { aplicar = true })).Json();
        var pa = Producto(await (await c.GetAsync("/api/estado")).Json(), a);
        Assert.Equal(859m, pa.Dec("precioObjetivo"));
        Assert.Equal(667m, pa.Dec("precioMinimo"));

        // Ya al día: sin cambios
        var otra = await (await c.PostAsJsonAsync("/api/productos/recalcular-precios", new { aplicar = false })).Json();
        Assert.Equal(0, otra.GetProperty("cambios").GetArrayLength());
    }

    private static System.Text.Json.JsonElement Producto(System.Text.Json.JsonElement estado, string id) =>
        estado.GetProperty("productos").EnumerateArray().Single(x => x.Str("id") == id);
}
