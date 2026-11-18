using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>
/// Precio sugerido y mínimo al escanear: referencia de mercado → costo estimado (proporcional) → motor de precios.
/// Objetivo general 30 %, mínimo 10 %, terminación 9; régimen fiscal pendiente. Datos y tarifa de PRUEBA.
/// </summary>
public class SugerenciaPrecioTests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    public SugerenciaPrecioTests(AppFactory f) => this.f = f;

    [Fact]
    public async Task SinCodigoDeCosteo_SugiereConCostoEstimadoYLuegoConCostoReal()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "SG", moneda = "MXN", costoOriginal = 1000m, tipoCambio = 1m })).Json(201)).Str("id");
        // Juguete A ya registrado con referencia de mercado $600 (de su código de barras)
        await (await c.PostAsJsonAsync("/api/productos", new { codigo = "SG-A", nombre = "A", cantidad = 1, palletId = pal, valorReferencia = 600m })).Json(201);

        object Sug(string? productoId = null) => new { productoId, palletId = pal, cantidad = 1, precioMexico = 0m, valorReferencia = 400m };

        // 1) Sin tarifas ni valor de mercado del pallet: costo = 1,000 × 400 ÷ (600 + 400) = $400; venta directa:
        //    mínimo 400 ÷ 0.90 = 444.44 → $445; sugerido 400 ÷ 0.70 = 571.43 → $579 (terminación 9)
        var r = await (await c.PostAsJsonAsync("/api/productos/sugerir-precio", Sug())).Json();
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(400m, r.GetProperty("costo").Dec("unitario"));
        Assert.Equal("estimado", r.GetProperty("costo").Str("origen"));
        Assert.Equal(445m, r.Dec("precioMinimo"));
        Assert.Equal(579m, r.Dec("precioObjetivo"));
        Assert.Equal("Venta directa sin comisiones", r.GetProperty("canal").Str("plataforma"));
        var avisos = string.Join(" ", r.GetProperty("avisos").EnumerateArray().Select(a => a.GetString()));
        Assert.Contains("bajará conforme registres", avisos);
        Assert.Contains("no recupera su costo", avisos);   // el mínimo supera la referencia de $400

        // 2) Con el valor de mercado del pallet (manifiesto $5,000) y tarifa de ML: costo 1,000 × 400 ÷ 5,000 = $80;
        //    mínimo (80 + 25 + 10) ÷ (1 − 0.15 − 0.10) = 153.33 → $154; sugerido 115 ÷ 0.55 = 209.09 → $219 (terminación 9, siempre hacia arriba)
        await (await c.PutAsJsonAsync($"/api/pallets/{pal}", new { nombre = "SG", moneda = "MXN", costoOriginal = 1000m, tipoCambio = 1m, valorMercadoEstimado = 5000m })).Json();
        var pls = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").EnumerateArray().Select(p => p.GetProperty("plataforma")).ToList();
        var ml = pls.Single(p => p.Str("codigo") == "ml_clasica").Str("id");
        await (await c.PostAsJsonAsync("/api/tarifas", new { plataformaId = ml, nombre = "ML prueba", comisionPct = 15m, cargoFijo = 25m, empaque = 10m, vigenteDesde = "2026-01-01", predeterminada = true })).Json();
        r = await (await c.PostAsJsonAsync("/api/productos/sugerir-precio", Sug())).Json();
        Assert.Equal(80m, r.GetProperty("costo").Dec("unitario"));
        Assert.True(r.GetProperty("costo").GetProperty("estimacion").GetProperty("usaValorMercadoPallet").GetBoolean());
        Assert.Equal("Mercado Libre Clásica", r.GetProperty("canal").Str("plataforma"));
        Assert.Equal(154m, r.Dec("precioMinimo"));
        Assert.Equal(219m, r.Dec("precioObjetivo"));

        // 3) Guardado y con costeo proporcional del pallet: el valor de referencia entra al reparto y el costo pasa a ser real ($400)
        var b = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "SG-B", nombre = "B", cantidad = 1, palletId = pal, valorReferencia = 400m, valorReferenciaOrigen = "upcitemdb" })).Json(201)).Str("id");
        await (await c.PostAsJsonAsync($"/api/pallets/{pal}/costeo/aplicar", new { metodo = "proporcional" })).Json();
        r = await (await c.PostAsJsonAsync("/api/productos/sugerir-precio", Sug(b))).Json();
        Assert.Equal("real", r.GetProperty("costo").Str("origen"));
        Assert.Equal(400m, r.GetProperty("costo").Dec("unitario"));

        // Sin pallet ni referencia no se inventa nada
        var sin = await (await c.PostAsJsonAsync("/api/productos/sugerir-precio", new { cantidad = 1, precioMexico = 0m })).Json();
        Assert.False(sin.GetProperty("ok").GetBoolean());
    }
}
