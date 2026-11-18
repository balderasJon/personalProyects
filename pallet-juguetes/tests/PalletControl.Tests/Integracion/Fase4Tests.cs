using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>
/// Fase 4 contra la API real, con base propia (los totales sin filtros dependen de todos los datos).
/// Escenario: pallet A $631.80 con «Rompecabezas» (2 pz, $315.90 c/u, ingresó hace 5 días);
/// pallet B $300 con «Muñeca» (3 pz, $100 c/u, ingresó hace 45 días, nunca vendida).
/// Venta del caso 13 (1 rompecabezas a $639 en ML, tarifa de PRUEBA 15 % + $25 + envío $120 + empaque $10), cobrada en $398.15.
/// Gasto general de operación: $50.
/// </summary>
public class Fase4Tests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    private static readonly SemaphoreSlim Uno = new(1, 1);
    private static (HttpClient C, string PalA, string PalB, string Rompe, string Muneca, string Ml)? esc;
    private static string Dia(int menos) => DateOnly.FromDateTime(DateTime.Now).AddDays(-menos).ToString("yyyy-MM-dd");

    public Fase4Tests(AppFactory f) => this.f = f;

    private async Task<(HttpClient C, string PalA, string PalB, string Rompe, string Muneca, string Ml)> Escenario()
    {
        await Uno.WaitAsync();
        try
        {
            if (esc is not null) return esc.Value;
            var c = await f.ClienteConSesionAsyncUnaVez();
            var pls = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").EnumerateArray().ToList();
            var ml = pls.Single(p => p.GetProperty("plataforma").Str("codigo") == "ml_clasica").GetProperty("plataforma").Str("id");
            await (await c.PostAsJsonAsync("/api/tarifas", new { plataformaId = ml, nombre = "ML prueba", comisionPct = 15m, cargoFijo = 25m, envioAbsorbido = 120m, empaque = 10m, vigenteDesde = "2026-01-01", predeterminada = true })).Json();
            var palA = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "A", moneda = "MXN", costoOriginal = 631.80m, tipoCambio = 1m, fechaCompra = Dia(5) })).Json(201)).Str("id");
            var palB = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "B", moneda = "MXN", costoOriginal = 300m, tipoCambio = 1m, fechaCompra = Dia(45) })).Json(201)).Str("id");
            var rompe = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "R-1", nombre = "Rompecabezas", categoria = "Didácticos", cantidad = 2, precioObjetivo = 639m, precioMinimo = 350m, palletId = palA, fechaIngreso = Dia(5) })).Json(201)).Str("id");
            var muneca = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "M-1", nombre = "Muñeca", categoria = "Muñecas", cantidad = 3, precioObjetivo = 250m, palletId = palB, fechaIngreso = Dia(45) })).Json(201)).Str("id");
            await (await c.PostAsJsonAsync($"/api/pallets/{palA}/costeo/aplicar", new { metodo = "equitativa" })).Json();
            await (await c.PostAsJsonAsync($"/api/pallets/{palB}/costeo/aplicar", new { metodo = "equitativa" })).Json();
            await (await c.PostAsJsonAsync("/api/ventas", new { productoId = rompe, cantidad = 1, precio = 639m, estado = "entregado", fecha = Dia(0), plataformaId = ml, importeCobrado = 398.15m })).Json(201);
            await (await c.PostAsJsonAsync("/api/gastos", new { tipo = "Empaque", monto = 50m, fecha = Dia(0), concepto = "Cajas" })).Json(201);
            esc = (c, palA, palB, rompe, muneca, ml);
            return esc.Value;
        }
        finally { Uno.Release(); }
    }

    private static JsonElement Fin(JsonElement d) => d.GetProperty("financieros");

    [Fact]
    public async Task Dashboard_SinFiltros_IndicadoresFinancierosYDeInventario()
    {
        var (c, _, _, rompe, muneca, _) = await Escenario();
        var d = await (await c.GetAsync("/api/dashboard")).Json();
        var x = Fin(d);
        Assert.Equal(931.80m, x.Dec("inversion"));
        Assert.Equal("pallets", x.Str("baseInversion"));
        Assert.Equal(639m, x.Dec("ingresos"));
        Assert.Equal(315.90m, x.Dec("costoVendido"));
        Assert.Equal(250.85m, x.Dec("gastosComerciales"));
        Assert.Equal(323.10m, x.Dec("utilidadBruta"));
        Assert.Equal(72.25m, x.Dec("utilidadComercial"));
        Assert.Equal(50m, x.Dec("gastosOperacion"));
        Assert.Equal(22.25m, x.Dec("utilidadNetaEstimada"));
        Assert.Equal(11.31m, x.Dec("margenPct"));
        Assert.Equal(2.39m, x.Dec("roiPct"));                      // 22.25 / 931.80
        Assert.Equal(22.87m, x.Dec("rendimientoSobreCostoPct"));    // 72.25 / 315.90
        Assert.Equal(388.15m, x.Dec("capitalRecuperado"));
        Assert.Equal(543.65m, x.Dec("capitalPendiente"));
        Assert.Equal(41.66m, x.Dec("recuperadoPct"));

        var inv = d.GetProperty("inventario");
        Assert.Equal(2, inv.GetProperty("productos").GetInt32());
        Assert.Equal(5, inv.GetProperty("piezasRecibidas").GetInt32());
        Assert.Equal(4, inv.GetProperty("piezasDisponibles").GetInt32());
        Assert.Equal(1, inv.GetProperty("piezasVendidas").GetInt32());
        Assert.Equal(1, inv.GetProperty("productosSinMovimiento").GetInt32());
        Assert.Equal(3, inv.GetProperty("piezasSinMovimiento").GetInt32());
        var ant = inv.GetProperty("antiguedad");
        Assert.Equal(35.0m, ant.Dec("diasPromedio"));    // (1 × 5 + 3 × 45) / 4
        Assert.Equal(1, ant.GetProperty("piezas0a30").GetInt32());
        Assert.Equal(3, ant.GetProperty("piezas31a60").GetInt32());

        var cand = d.GetProperty("candidatosLiquidacion").EnumerateArray().Single();
        Assert.Equal(muneca, cand.Str("productoId"));
        Assert.Equal(45, cand.GetProperty("diasSinMovimiento").GetInt32());
        Assert.Equal(100m, cand.Dec("costoUnitario"));
        var top = d.GetProperty("masRentables")[0];
        Assert.Equal(rompe, top.Str("productoId"));
        Assert.Equal(50m, top.Dec("rotacionPct"));
        var ml = d.GetProperty("porPlataforma").EnumerateArray().Single();
        Assert.Equal("Mercado Libre Clásica", ml.Str("nombre"));
        Assert.Equal(72.25m, ml.Dec("utilidad"));
        Assert.Equal(388.15m, d.GetProperty("recuperacion")[0].Dec("valor"));
    }

    [Fact]
    public async Task Dashboard_Filtros_PalletCategoriaYPlataforma()
    {
        var (c, palA, palB, _, _, ml) = await Escenario();
        var a = Fin(await (await c.GetAsync($"/api/dashboard?palletId={palA}")).Json());
        Assert.Equal(631.80m, a.Dec("inversion"));
        Assert.Equal(JsonValueKind.Null, a.GetProperty("gastosOperacion").ValueKind);   // no se asignan a un pallet
        Assert.Equal(72.25m, a.Dec("utilidadNetaEstimada"));
        Assert.Equal(11.44m, a.Dec("roiPct"));
        Assert.Equal(61.44m, a.Dec("recuperadoPct"));
        var b = Fin(await (await c.GetAsync($"/api/dashboard?palletId={palB}")).Json());
        Assert.Equal(0m, b.Dec("ingresos"));
        Assert.Equal(0m, b.Dec("capitalRecuperado"));
        var cat = Fin(await (await c.GetAsync("/api/dashboard?categoria=Mu%C3%B1ecas")).Json());
        Assert.Equal("costo_asignado", cat.Str("baseInversion"));
        Assert.Equal(300m, cat.Dec("inversion"));
        var fecha = Fin(await (await c.GetAsync($"/api/dashboard?desde={Dia(-1)}")).Json());   // mañana: sin ventas
        Assert.Equal(0m, fecha.Dec("ingresos"));
        Assert.Equal(1, Fin(await (await c.GetAsync($"/api/dashboard?plataformaId={ml}")).Json()).GetProperty("ventas").GetInt32());
    }

    // Competencia: estadística, diferencia y posición con la banda configurada (±5 %).
    [Fact]
    public async Task Competencia_EstadisticaYPosicion()
    {
        var (c, _, _, rompe, _, ml) = await Escenario();
        async Task<string> Obs(decimal precio, decimal envio, string? plat, string? texto) =>
            (await (await c.PostAsJsonAsync("/api/competencia", new { productoId = rompe, productoReferencia = "Rompecabezas 1000 pz", plataformaId = plat, plataformaTexto = texto,
                marca = "Marca X", condicion = "nuevo_sellado", precioPublicado = precio, precioEnvio = envio, fechaConsulta = Dia(1) })).Json(201)).Str("id");
        await Obs(549m, 50m, ml, null);           // total 599
        await Obs(649m, 0m, null, "Liverpool");
        await Obs(699m, 0m, ml, null);
        var cara = await Obs(700m, 50m, null, "Walmart");   // total 750
        var r = (await (await c.GetAsync($"/api/competencia/resumen?productoId={rompe}")).Json())[0];
        Assert.Equal(674m, r.GetProperty("mercado").Dec("mediana"));
        Assert.Equal(674.25m, r.GetProperty("mercado").Dec("promedio"));
        Assert.Equal(599m, r.GetProperty("mercado").Dec("minimo"));
        Assert.Equal("precio_objetivo", r.Str("origenPrecioPropio"));
        Assert.Equal(-5.19m, r.GetProperty("posicion").Dec("diferenciaPct"));
        Assert.Equal("barato", r.GetProperty("posicion").Str("posicion"));
        Assert.Equal(2, r.GetProperty("posicion").GetProperty("lugar").GetInt32());

        // Validaciones
        await (await c.PostAsJsonAsync("/api/competencia", new { productoId = rompe, productoReferencia = "X", plataformaTexto = "Tienda", precioPublicado = 0m })).Json(400);
        await (await c.PostAsJsonAsync("/api/competencia", new { productoId = rompe, productoReferencia = "X", precioPublicado = 10m })).Json(400);   // sin plataforma
        await (await c.PostAsJsonAsync("/api/competencia", new { productoId = rompe, productoReferencia = "X", plataformaTexto = "T", precioPublicado = 10m, fechaConsulta = Dia(-3) })).Json(400);

        // Descartar una observación: deja de contar (mediana de 599, 649, 699 = 649 → 639 queda en precio de mercado)
        await (await c.PutAsJsonAsync($"/api/competencia/{cara}", new { productoId = rompe, productoReferencia = "Rompecabezas 1000 pz", plataformaTexto = "Walmart", precioPublicado = 700m, precioEnvio = 50m, vigente = false })).Json();
        r = (await (await c.GetAsync($"/api/competencia/resumen?productoId={rompe}")).Json())[0];
        Assert.Equal(3, r.GetProperty("observaciones").GetInt32());
        Assert.Equal(-1.54m, r.GetProperty("posicion").Dec("diferenciaPct"));
        Assert.Equal("en_mercado", r.GetProperty("posicion").Str("posicion"));

        // Foto para identificar el producto: solo imágenes
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var ok = new MultipartFormDataContent { { new ByteArrayContent(png), "imagen", "foto.png" } };
        Assert.StartsWith("/imagenes/", (await (await c.PostAsync($"/api/competencia/{cara}/imagen", ok)).Json()).Str("imagen"));
        var mala = new MultipartFormDataContent { { new ByteArrayContent("hola"u8.ToArray()), "imagen", "x.png" } };
        await (await c.PostAsync($"/api/competencia/{cara}/imagen", mala)).Json(400);
    }

    [Fact]
    public async Task Parametros_DiasSinMovimientoConfigurables()
    {
        var (c, _, _, _, _, _) = await Escenario();
        await (await c.PutAsJsonAsync("/api/configuracion/comercial", new { diasSinMovimiento = 0, bandaCompetenciaPct = 5m })).Json(400);
        await (await c.PutAsJsonAsync("/api/configuracion/comercial", new { diasSinMovimiento = 60, bandaCompetenciaPct = 5m })).Json();
        var d = await (await c.GetAsync("/api/dashboard")).Json();
        Assert.Empty(d.GetProperty("candidatosLiquidacion").EnumerateArray());
        await (await c.PutAsJsonAsync("/api/configuracion/comercial", new { diasSinMovimiento = 30, bandaCompetenciaPct = 5m })).Json();
    }

    [Fact]
    public async Task Reportes_CsvParaExcel()
    {
        var (c, _, _, _, _, _) = await Escenario();
        var r = await c.GetAsync("/api/reportes/ventas");
        Assert.Equal("text/csv", r.Content.Headers.ContentType!.MediaType);
        var texto = await r.Content.ReadAsStringAsync();
        Assert.StartsWith("Folio,Fecha", texto.TrimStart('﻿'));
        Assert.Contains("V-00001", texto);
        Assert.Contains("72.25", texto);
        foreach (var t in new[] { "productos", "plataformas", "categorias", "pallets", "inventario", "competencia", "liquidacion" })
            Assert.True((await c.GetAsync($"/api/reportes/{t}")).IsSuccessStatusCode, t);
        Assert.Contains("Muñeca", await (await c.GetAsync("/api/reportes/liquidacion")).Content.ReadAsStringAsync());
        await (await c.GetAsync("/api/reportes/inexistente")).Json(404);
    }
}
