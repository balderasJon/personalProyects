using System.Net.Http.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>Decisión del usuario (2026-11-23): lo que costó el pallet ya incluye IVA; el sugerido y el mínimo no lo suman.</summary>
public class SinIvaTests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    public SinIvaTests(AppFactory f) => this.f = f;

    [Fact]
    public async Task ConDatosFiscalesGuardados_ElSugeridoNoSumaIva()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        // Configuración fiscal que antes sí sumaba IVA (régimen definido y «cobro IVA»)
        await c.PutAsJsonAsync("/api/fiscal", new { regimen = "resico", cobraIva = true, tasaIvaPct = 16m, notas = "" });
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "IVA", moneda = "MXN", costoOriginal = 1000m, tipoCambio = 1m })).Json(201)).Str("id");
        await (await c.PostAsJsonAsync("/api/productos", new { codigo = "IVA-A", nombre = "A", cantidad = 1, palletId = pal, valorReferencia = 600m })).Json(201);

        // Costo 1,000 × 400 ÷ (600 + 400) = $400 → mínimo 400 ÷ 0.90 = 444.44 → $445; sugerido 400 ÷ 0.70 = 571.43 → $579 (sin × 1.16)
        var r = await (await c.PostAsJsonAsync("/api/productos/sugerir-precio", new { palletId = pal, cantidad = 1, precioMexico = 0m, valorReferencia = 400m })).Json();
        Assert.Equal(400m, r.GetProperty("costo").Dec("unitario"));
        Assert.Equal(445m, r.Dec("precioMinimo"));
        Assert.Equal(579m, r.Dec("precioObjetivo"));
        Assert.False(r.GetProperty("fiscalPendiente").GetBoolean());
    }
}
