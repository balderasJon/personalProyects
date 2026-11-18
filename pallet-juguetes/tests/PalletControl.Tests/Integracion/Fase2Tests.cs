using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>Fase 2 contra la API real. Tarifas de PRUEBA (no son tarifas reales de ninguna plataforma).</summary>
public class Fase2Tests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    private static readonly SemaphoreSlim Uno = new(1, 1);
    private static (HttpClient C, string Prod, string Ml, string Fb, string TarifaMl)? esc;

    public Fase2Tests(AppFactory f) => this.f = f;

    /// <summary>Juguete con costo real de $315.90 por pieza; precio en México $600; tarifas ML (15 % + $25 + $10) y FB ($40 + $10).</summary>
    private async Task<(HttpClient C, string Prod, string Ml, string Fb, string TarifaMl)> Escenario()
    {
        await Uno.WaitAsync();
        try
        {
            if (esc is not null) return esc.Value;
            var c = await f.ClienteConSesionAsyncUnaVez();
            var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "F2", moneda = "MXN", costoOriginal = 631.80m, tipoCambio = 1m })).Json(201)).Str("id");
            var prod = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "R-1", nombre = "Rompecabezas", categoria = "Didácticos", cantidad = 2, precioMexico = 600m, palletId = pal })).Json(201)).Str("id");
            await (await c.PostAsJsonAsync($"/api/pallets/{pal}/costeo/aplicar", new { metodo = "equitativa" })).Json();
            var pls = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").EnumerateArray().ToList();
            string Pl(string codigo) => pls.Single(p => p.GetProperty("plataforma").Str("codigo") == codigo).GetProperty("plataforma").Str("id");
            var ml = Pl("ml_clasica"); var fb = Pl("facebook_marketplace");
            var tml = (await (await c.PostAsJsonAsync("/api/tarifas", Tarifa(ml, "ML prueba", 15m, 25m, 0m, new DateOnly(2026, 1, 1)))).Json()).Str("id");
            await (await c.PostAsJsonAsync("/api/tarifas", Tarifa(fb, "Local prueba", 0m, 0m, 40m, new DateOnly(2026, 1, 1)))).Json();
            esc = (c, prod, ml, fb, tml);
            return esc.Value;
        }
        finally { Uno.Release(); }
    }

    private static object Tarifa(string pl, string nombre, decimal comision, decimal fijo, decimal logistico, DateOnly desde) => new
    {
        plataformaId = pl, nombre, comisionPct = comision, cargoFijo = fijo, costoLogistico = logistico, empaque = 10m,
        vigenteDesde = desde, predeterminada = true,
    };

    private static JsonElement Canal(JsonElement r, string pl) => r.GetProperty("canales").EnumerateArray().Single(x => x.Str("plataformaId") == pl);

    [Fact]
    public async Task Semillas_PlataformasObjetivoYFiscal()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        var pls = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").EnumerateArray().Select(p => p.GetProperty("plataforma").Str("nombre")).ToList();
        Assert.Contains("Mercado Libre Clásica", pls);
        Assert.Contains("Mercado Libre Premium", pls);
        Assert.Contains("Amazon México", pls);
        Assert.Contains("Facebook Marketplace", pls);
        Assert.Contains("WhatsApp Business", pls);
        Assert.Contains("Ventas físicas", pls);
        var par = (await (await c.GetAsync("/api/parametros-rentabilidad")).Json()).EnumerateArray().Single(p => p.Str("ambito") == "general").GetProperty("parametro");
        Assert.Equal(30m, par.Dec("valor"));
        Assert.Equal(10m, par.Dec("margenMinimoPct"));
        Assert.Equal("terminacion9", par.Str("redondeo"));
        Assert.Equal("pendiente", (await (await c.GetAsync("/api/fiscal")).Json()).Str("regimen"));
    }

    // Casos 8, 9, 10 y 11 de punta a punta
    [Fact]
    public async Task Casos08a11_PreciosPorCanalYMejorCanal()
    {
        var (c, prod, ml, fb, _) = await Escenario();
        var r = await (await c.GetAsync($"/api/rentabilidad/productos/{prod}?fecha=2026-11-18")).Json();
        Assert.Equal(315.90m, r.GetProperty("costo").Dec("unitario"));
        var m = Canal(r, ml);
        Assert.Equal(412.83m, m.Dec("equilibrio"));
        Assert.Equal(467.87m, m.Dec("minimo"));
        Assert.Equal(638.00m, m.Dec("objetivo"));
        Assert.Equal(639m, m.Dec("sugerido"));
        Assert.Equal(192.25m, m.GetProperty("alSugerido").Dec("utilidad"));
        Assert.Equal(30.09m, m.GetProperty("alSugerido").Dec("margenPct"));
        var l = Canal(r, fb);
        Assert.Equal(529m, l.Dec("sugerido"));
        // Al precio de mercado ($600) conviene la venta local: 234.10 contra 159.10
        Assert.Equal(234.10m, l.GetProperty("alReferencia").Dec("utilidad"));
        Assert.Equal(159.10m, m.GetProperty("alReferencia").Dec("utilidad"));
        Assert.Equal(fb, r.Str("mejorCanal"));
        // Plataformas sin tarifa: no se inventa nada
        var amazon = r.GetProperty("canales").EnumerateArray().Single(x => x.Str("plataforma") == "Amazon México");
        Assert.False(amazon.GetProperty("ok").GetBoolean());
        Assert.Equal("sin_tarifa", amazon.Str("estado"));
        Assert.True(r.GetProperty("fiscalPendiente").GetBoolean());
    }

    // Caso 17: nueva vigencia — la tarifa anterior se cierra un día antes y conserva sus montos.
    [Fact]
    public async Task Caso17_NuevaVigenciaDeTarifa()
    {
        var (c, prod, ml, _, tml) = await Escenario();
        // Una tarifa con los mismos criterios y vigencia traslapada se rechaza
        await (await c.PostAsJsonAsync("/api/tarifas", Tarifa(ml, "Duplicada", 16m, 25m, 0m, new DateOnly(2026, 6, 1)))).Json(409);
        var nueva = await (await c.PostAsJsonAsync($"/api/tarifas/{tml}/nueva-vigencia", Tarifa(ml, "ML prueba 17 %", 17m, 25m, 0m, new DateOnly(2026, 12, 1)))).Json();
        Assert.Equal(tml, nueva.Str("reemplazaA"));
        var hist = (await (await c.GetAsync($"/api/plataformas/{ml}/tarifas")).Json()).EnumerateArray().ToList();
        var vieja = hist.Single(t => t.Str("id") == tml);
        Assert.Equal("2026-11-30", vieja.Str("vigenteHasta"));
        Assert.Equal(15m, vieja.Dec("comisionPct"));
        var nov = await (await c.GetAsync($"/api/rentabilidad/productos/{prod}?fecha=2026-11-30")).Json();
        var dic = await (await c.GetAsync($"/api/rentabilidad/productos/{prod}?fecha=2026-12-01")).Json();
        Assert.Equal(639m, Canal(nov, ml).Dec("sugerido"));
        Assert.Equal(669m, Canal(dic, ml).Dec("sugerido"));
    }

    [Fact]
    public async Task ObjetivosPorAmbito_GanaElMasEspecifico()
    {
        var (c, prod, ml, fb, _) = await Escenario();
        var porPlataforma = await (await c.PostAsJsonAsync("/api/parametros-rentabilidad", new { plataformaId = ml, esquema = "margen", valor = 20m, margenMinimoPct = 10m, descuentoPromocionalPct = 0m, redondeo = "terminacion9" })).Json();
        var r = await (await c.GetAsync($"/api/rentabilidad/productos/{prod}?fecha=2026-11-18")).Json();
        Assert.Equal(549m, Canal(r, ml).Dec("sugerido"));   // 350.90 / 0.65 = 539.85 → 549
        Assert.Equal("plataforma", Canal(r, ml).GetProperty("parametro").Str("ambito"));
        Assert.Equal(529m, Canal(r, fb).Dec("sugerido"));   // FB sigue con el general
        // Mínimo mayor que objetivo: rechazado
        await (await c.PostAsJsonAsync("/api/parametros-rentabilidad", new { productoId = prod, esquema = "margen", valor = 10m, margenMinimoPct = 20m, redondeo = "peso" })).Json(400);
        // El general no se borra
        var general = (await (await c.GetAsync("/api/parametros-rentabilidad")).Json()).EnumerateArray().Single(p => p.Str("ambito") == "general").GetProperty("parametro").Str("id");
        await (await c.DeleteAsync($"/api/parametros-rentabilidad/{general}")).Json(400);
        await (await c.DeleteAsync($"/api/parametros-rentabilidad/{porPlataforma.Str("id")}")).Json(204);
    }

    [Fact]
    public async Task Simulador_AlertasAlPrecioCapturado()
    {
        var (c, prod, ml, _, _) = await Escenario();
        var r = await (await c.PostAsJsonAsync("/api/rentabilidad/simular", new { productoId = prod, plataformaId = ml, precio = 450m, fecha = "2026-11-18" })).Json();
        var codigos = r.GetProperty("alPrecio").GetProperty("alertas").EnumerateArray().Select(a => a.Str("codigo")).ToList();
        Assert.Contains("bajo_minimo", codigos);
        Assert.Equal(450m, r.GetProperty("alPrecio").GetProperty("desglose").Dec("precio"));
        // Sin juguete, con costo capturado
        var s = await (await c.PostAsJsonAsync("/api/rentabilidad/simular", new { costo = 315.90m, plataformaId = ml, fecha = "2026-11-18" })).Json();
        Assert.Equal(639m, s.GetProperty("canal").Dec("sugerido"));
        await (await c.PostAsJsonAsync("/api/rentabilidad/simular", new { productoId = "noexiste", plataformaId = ml })).Json(404);
    }

    [Fact]
    public async Task GuardarPrecios_HistorialYTarifaProtegida()
    {
        var (c, prod, _, _, tml) = await Escenario();
        Assert.Equal(2, (await (await c.PostAsync($"/api/rentabilidad/productos/{prod}/guardar?fecha=2026-11-18", null)).Json()).GetProperty("guardados").GetInt32());
        await (await c.PostAsync($"/api/rentabilidad/productos/{prod}/guardar?fecha=2026-11-18", null)).Json();
        var h = (await (await c.GetAsync($"/api/rentabilidad/productos/{prod}/historial")).Json()).EnumerateArray().ToList();
        Assert.Equal(4, h.Count);
        Assert.Equal(2, h.Count(x => x.GetProperty("precio").GetProperty("vigente").GetBoolean()));
        await (await c.DeleteAsync($"/api/tarifas/{tml}")).Json(409);
    }

    [Fact]
    public async Task Fiscal_ConIva_SeparaElImpuesto()
    {
        var (c, prod, ml, _, _) = await Escenario();
        await (await c.PutAsJsonAsync("/api/fiscal", new { regimen = "resico", cobraIva = true, tasaIvaPct = 16m })).Json();
        try
        {
            var r = await (await c.GetAsync($"/api/rentabilidad/productos/{prod}?fecha=2026-11-18")).Json();
            var d = Canal(r, ml).GetProperty("alSugerido");
            Assert.True(d.Dec("ivaTrasladado") > 0);
            Assert.True(d.Dec("margenPct") >= 30m);   // el margen se mide sobre el precio sin IVA
            Assert.Equal(d.Dec("precio"), d.Dec("ingresoSinIva") + d.Dec("ivaTrasladado"));
        }
        finally { await (await c.PutAsJsonAsync("/api/fiscal", new { regimen = "pendiente", cobraIva = false, tasaIvaPct = 16m })).Json(); }
    }

    [Fact]
    public async Task ListaPaginadaConResumen()
    {
        var (c, _, _, _, _) = await Escenario();
        var r = await (await c.GetAsync("/api/rentabilidad/productos?tam=5&pagina=1&orden=utilidad&desc=true")).Json();
        Assert.True(r.GetProperty("total").GetInt32() >= 1);
        Assert.True(r.GetProperty("filas").GetArrayLength() <= 5);
        Assert.Equal(401, (int)(await f.CreateClient().GetAsync("/api/rentabilidad/productos")).StatusCode);
    }
}
