using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>
/// Fase 3 contra la API real. Tarifa de PRUEBA para Mercado Libre Clásica (no es la tarifa real):
/// hasta el 30-nov-2026: comisión 15 %, cargo fijo $25, envío absorbido $120 y empaque $10; desde el 1-dic: comisión 17 %.
/// Cada prueba usa su propio pallet de $631.80 con 2 piezas (costo histórico $315.90 por pieza).
/// </summary>
public class Fase3Tests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    private static readonly SemaphoreSlim Uno = new(1, 1);
    private static (HttpClient C, string Ml, string Amazon)? esc;

    public Fase3Tests(AppFactory f) => this.f = f;

    private async Task<(HttpClient C, string Ml, string Amazon)> Escenario()
    {
        await Uno.WaitAsync();
        try
        {
            if (esc is not null) return esc.Value;
            var c = await f.ClienteConSesionAsyncUnaVez();
            var pls = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").EnumerateArray().ToList();
            string Pl(string codigo) => pls.Single(p => p.GetProperty("plataforma").Str("codigo") == codigo).GetProperty("plataforma").Str("id");
            var ml = Pl("ml_clasica");
            var t = await (await c.PostAsJsonAsync("/api/tarifas", Tarifa(ml, 15m, new DateOnly(2026, 1, 1)))).Json();
            await (await c.PostAsJsonAsync($"/api/tarifas/{t.Str("id")}/nueva-vigencia", Tarifa(ml, 17m, new DateOnly(2026, 12, 1)))).Json();
            esc = (c, ml, Pl("amazon_mx"));
            return esc.Value;
        }
        finally { Uno.Release(); }
    }

    private static object Tarifa(string pl, decimal comision, DateOnly desde) => new
    {
        plataformaId = pl, nombre = $"ML prueba {comision} %", comisionPct = comision, cargoFijo = 25m, envioAbsorbido = 120m, empaque = 10m,
        vigenteDesde = desde, predeterminada = true,
    };

    /// <summary>Pallet de $631.80 con un juguete de 2 piezas, costeado (equitativo): $315.90 por pieza.</summary>
    private static async Task<(string Pallet, string Prod)> Juguete(HttpClient c, string codigo, int piezas = 2)
    {
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "F3 " + codigo, moneda = "MXN", costoOriginal = 315.90m * piezas, tipoCambio = 1m, fechaCompra = "2026-11-01" })).Json(201)).Str("id");
        var prod = (await (await c.PostAsJsonAsync("/api/productos", new { codigo, nombre = "Juguete " + codigo, categoria = "Didácticos", cantidad = piezas, precioMexico = 600m, palletId = pal })).Json(201)).Str("id");
        await (await c.PostAsJsonAsync($"/api/pallets/{pal}/costeo/aplicar", new { metodo = "equitativa" })).Json();
        return (pal, prod);
    }

    private static async Task<JsonElement> Vender(HttpClient c, object venta, int esperado = 201) =>
        await (await c.PostAsJsonAsync("/api/ventas", venta)).Json(esperado);

    private static JsonElement Res(JsonElement detalle) => detalle.GetProperty("resultado");

    // Caso 13 de punta a punta: gastos precargados con la tarifa vigente, costo congelado y cobro separado del registro.
    [Fact]
    public async Task Caso13_VentaConGastosDeTarifaYCobroReal()
    {
        var (c, ml, _) = await Escenario();
        var (_, prod) = await Juguete(c, "F3-13");
        var v = await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, estado = "pagado", fecha = "2026-11-18", plataformaId = ml });
        Assert.StartsWith("V-", v.Str("folio"));
        var gastos = v.GetProperty("gastos").EnumerateArray().ToList();
        Assert.Equal(95.85m, gastos.Single(g => g.Str("tipo") == "comision").Dec("monto"));
        Assert.Equal(120m, gastos.Single(g => g.Str("tipo") == "envio").Dec("monto"));
        Assert.All(gastos, g => Assert.True(g.GetProperty("estimado").GetBoolean()));
        Assert.Equal(315.90m, v.GetProperty("detalle")[0].Dec("costoUnitario"));
        Assert.Equal("costeo", v.GetProperty("detalle")[0].Str("costoOrigen"));
        var r = Res(v);
        Assert.Equal(72.25m, r.Dec("utilidad"));
        Assert.Equal(11.31m, r.Dec("margenPct"));
        // Registrada y marcada como pagada, pero no cobrada: no hay efectivo hasta registrar el depósito.
        Assert.False(r.GetProperty("cobrada").GetBoolean());
        Assert.Equal(0m, r.Dec("efectivoNeto"));
        Assert.Equal(398.15m, r.Dec("porCobrarEstimado"));
        var cobrada = await (await c.PostAsJsonAsync($"/api/ventas/{v.GetProperty("venta").Str("id")}/cobro", new { importe = 398.15m, fecha = "2026-11-25" })).Json();
        Assert.Equal(388.15m, Res(cobrada).Dec("efectivoNeto"));
        // Cliente anterior: canal como texto, sin plataforma → se asigna por nombre
        var legacy = await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, estado = "apartado", fecha = "2026-11-18", canal = "Mercado Libre Clásica" });
        Assert.Equal(ml, legacy.GetProperty("venta").Str("plataformaId"));
        Assert.Equal(72.25m, Res(legacy).Dec("utilidad"));
        // Ya no quedan piezas
        await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, fecha = "2026-11-18" }, 409);
    }

    // Caso 14 de punta a punta: devolución dañada → la pieza pasa a un lote no vendible y su costo es merma.
    [Fact]
    public async Task Caso14_DevolucionDaniadaYReembolso()
    {
        var (c, ml, _) = await Escenario();
        var (_, prod) = await Juguete(c, "F3-14");
        var v = await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, estado = "entregado", fecha = "2026-11-18", plataformaId = ml, importeCobrado = 398.15m });
        var id = v.GetProperty("venta").Str("id");
        await (await c.PostAsJsonAsync($"/api/ventas/{id}/devolucion", new { cantidad = 2, montoReembolsado = 0m })).Json(400);
        var d = await (await c.PostAsJsonAsync($"/api/ventas/{id}/devolucion", new
        {
            cantidad = 1, condicionDestino = "danado", montoReembolsado = 639m, fecha = "2026-11-28", motivo = "Llegó rota",
            gastos = new[] { new { tipo = "devolucion", concepto = "Envío de regreso", monto = 120m, deducidoPorPlataforma = false } },
        })).Json();
        var r = Res(d);
        Assert.Equal(315.90m, r.Dec("costoMerma"));
        Assert.Equal(0m, r.Dec("costoVendido"));
        Assert.Equal(-686.75m, r.Dec("utilidad"));
        Assert.Equal(-370.85m, r.Dec("efectivoNeto"));   // 398.15 − 10 − 120 − 639
        var ex = (await (await c.GetAsync($"/api/productos/{prod}/existencias")).Json()).EnumerateArray().ToList();
        Assert.Equal(1, ex.Single(x => x.GetProperty("comercializable").GetBoolean()).GetProperty("disponibles").GetInt32());
        Assert.Equal(1, ex.Single(x => x.Str("condicion") == "danado").GetProperty("disponibles").GetInt32());
        // No se reembolsa más de lo pagado
        await (await c.PostAsJsonAsync($"/api/ventas/{id}/reembolso", new { monto = 1m })).Json(400);
    }

    // Caso 15 de punta a punta: cancelación — las piezas regresan, se quitan los gastos estimados y queda como pérdida el que se indique.
    [Fact]
    public async Task Caso15_CancelacionDevuelveExistencias()
    {
        var (c, ml, _) = await Escenario();
        var (_, prod) = await Juguete(c, "F3-15");
        var v = await Vender(c, new { productoId = prod, cantidad = 2, precio = 639m, fecha = "2026-11-18", plataformaId = ml });
        var id = v.GetProperty("venta").Str("id");
        var x = await (await c.PostAsJsonAsync($"/api/ventas/{id}/cancelar", new
        {
            motivo = "El cliente se arrepintió", gastos = new[] { new { tipo = "otro", concepto = "Guía de envío no reembolsable", monto = 80m, deducidoPorPlataforma = false } },
        })).Json();
        var r = Res(x);
        Assert.Equal(0m, r.Dec("ingresoBruto"));
        Assert.Equal(-80m, r.Dec("utilidad"));
        Assert.Equal("cancelado", x.GetProperty("venta").Str("estado"));
        Assert.Single(x.GetProperty("gastos").EnumerateArray());
        var ex = (await (await c.GetAsync($"/api/productos/{prod}/existencias")).Json()).EnumerateArray().Single();
        Assert.Equal(2, ex.GetProperty("disponibles").GetInt32());
        await (await c.PostAsJsonAsync($"/api/ventas/{id}/cancelar", new { })).Json(409);
        // Una venta con incidencias no se elimina
        await (await c.DeleteAsync($"/api/ventas/{id}")).Json(409);
    }

    // Caso 16: un juguete publicado en dos canales. Al venderse en uno, el otro pide pausarse y una segunda venta se rechaza.
    [Fact]
    public async Task Caso16_PublicadoEnVariosCanales_SinDobleVenta()
    {
        var (c, ml, amazon) = await Escenario();
        var (_, prod) = await Juguete(c, "F3-16", piezas: 1);
        var pMl = (await (await c.PostAsJsonAsync("/api/publicaciones", new { productoId = prod, plataformaId = ml, precioPublicado = 639m, estatus = "publicado", existencias = 1, idExterno = "MLM-1" })).Json(201)).Str("id");
        var pAm = (await (await c.PostAsJsonAsync("/api/publicaciones", new { productoId = prod, plataformaId = amazon, precioPublicado = 649m, estatus = "publicado", existencias = 1 })).Json(201)).Str("id");
        var v = await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, fecha = "2026-11-18", publicacionId = pMl });
        Assert.Equal(ml, v.GetProperty("venta").Str("plataformaId"));
        Assert.Equal(639m, v.GetProperty("venta").Dec("precioPublicado"));
        JsonElement Pub(JsonElement lista, string id) => lista.EnumerateArray().Single(x => x.GetProperty("publicacion").Str("id") == id).GetProperty("publicacion");
        var pubs = await (await c.GetAsync($"/api/publicaciones?productoId={prod}")).Json();
        Assert.Equal("vendido", Pub(pubs, pMl).Str("estatus"));
        Assert.Equal("pausar", Pub(pubs, pAm).Str("requiereAccion"));
        Assert.True((await (await c.GetAsync("/api/publicaciones/alertas")).Json()).GetInt32() >= 1);
        // La venta en Amazon se rechaza: no hay existencias centrales
        var rechazo = await Vender(c, new { productoId = prod, cantidad = 1, precio = 649m, fecha = "2026-11-18", publicacionId = pAm }, 409);
        Assert.Contains("No hay existencias", rechazo.Str("mensaje"));
        // Si se cancela la venta, el aviso de pausar desaparece y la publicación vendida pide reactivarse
        await (await c.PostAsJsonAsync($"/api/ventas/{v.GetProperty("venta").Str("id")}/cancelar", new { motivo = "Prueba" })).Json();
        pubs = await (await c.GetAsync($"/api/publicaciones?productoId={prod}")).Json();
        Assert.Equal("", Pub(pubs, pAm).Str("requiereAccion"));
        Assert.Equal("reactivar", Pub(pubs, pMl).Str("requiereAccion"));
        var at = await (await c.PostAsync($"/api/publicaciones/{pMl}/atendida", null)).Json();
        Assert.Equal("publicado", at.Str("estatus"));
        Assert.Equal(1, at.GetProperty("existencias").GetInt32());
    }

    // Caso 19: consulta histórica — recostear el pallet y cambiar la tarifa no altera la rentabilidad de ventas anteriores.
    [Fact]
    public async Task Caso19_ConsultaHistoricaDeRentabilidad()
    {
        var (c, ml, _) = await Escenario();
        var (pal, prod) = await Juguete(c, "F3-19");
        await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, fecha = "2026-11-18", plataformaId = ml });
        // Después de la venta: gasto adicional de $400 en el pallet y nuevo costeo ($515.90 por pieza)
        await (await c.PostAsJsonAsync($"/api/pallets/{pal}/gastos", new { concepto = "Flete tardío", montoMxn = 400m, incluidoEnCosto = false })).Json();
        await (await c.PostAsJsonAsync($"/api/pallets/{pal}/costeo/aplicar", new { metodo = "equitativa" })).Json();
        var dic = await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, fecha = "2026-12-02", plataformaId = ml });
        Assert.Equal(515.90m, dic.GetProperty("detalle")[0].Dec("costoUnitario"));
        Assert.Equal(108.63m, dic.GetProperty("gastos").EnumerateArray().Single(g => g.Str("tipo") == "comision").Dec("monto"));   // 17 %
        Assert.Equal(-140.53m, Res(dic).Dec("utilidad"));   // 639 − 108.63 − 25 − 120 − 10 − 515.90

        var nov = await (await c.GetAsync($"/api/rentabilidad-real?desde=2026-11-01&hasta=2026-11-30&productoId={prod}")).Json();
        Assert.Equal(1, nov.GetProperty("totales").GetProperty("ventas").GetInt32());
        Assert.Equal(315.90m, nov.GetProperty("totales").Dec("costoVendido"));
        Assert.Equal(72.25m, nov.GetProperty("totales").Dec("utilidad"));
        var todo = await (await c.GetAsync($"/api/rentabilidad-real?productoId={prod}")).Json();
        Assert.Equal(-68.28m, todo.GetProperty("totales").Dec("utilidad"));   // 72.25 − 140.53
        var porPl = todo.GetProperty("porPlataforma").EnumerateArray().Single();
        Assert.Equal("Mercado Libre Clásica", porPl.Str("plataforma"));
        Assert.True(todo.GetProperty("fiscal").GetProperty("pendiente").GetBoolean());
    }

    // Caso 20: recuperación de inversión — solo cuenta lo cobrado.
    [Fact]
    public async Task Caso20_RecuperacionDeInversion()
    {
        var (c, ml, _) = await Escenario();
        var (pal, prod) = await Juguete(c, "F3-20");
        var v = await Vender(c, new { productoId = prod, cantidad = 1, precio = 639m, estado = "pagado", fecha = "2026-11-18", plataformaId = ml });
        JsonElement Pallet(JsonElement r) => r.GetProperty("porPallet").EnumerateArray().Single(p => p.Str("palletId") == pal);
        var antes = Pallet(await (await c.GetAsync($"/api/rentabilidad-real?palletId={pal}")).Json());
        Assert.Equal(0m, antes.Dec("capitalRecuperado"));
        Assert.Equal(631.80m, antes.Dec("faltaPorRecuperar"));
        Assert.Equal(72.25m, antes.Dec("utilidad"));
        await (await c.PostAsJsonAsync($"/api/ventas/{v.GetProperty("venta").Str("id")}/cobro", new { importe = 398.15m })).Json();
        var despues = Pallet(await (await c.GetAsync($"/api/rentabilidad-real?palletId={pal}")).Json());
        Assert.Equal(388.15m, despues.Dec("capitalRecuperado"));
        Assert.Equal(61.44m, despues.Dec("capitalRecuperadoPct"));   // 388.15 / 631.80
        Assert.Equal(243.65m, despues.Dec("faltaPorRecuperar"));
    }

    // Inventario y ventas consistentes: no se ajusta ni reclasifica un lote por debajo de lo vendido; editar la venta mueve las piezas.
    [Fact]
    public async Task Consistencia_LotesVentasYEdicion()
    {
        var (c, _, _) = await Escenario();
        var (_, prod) = await Juguete(c, "F3-CON");
        var v = await Vender(c, new { productoId = prod, cantidad = 2, precio = 500m, fecha = "2026-11-18", canal = "Conocidos" });
        var id = v.GetProperty("venta").Str("id");
        var lote = v.GetProperty("detalle")[0].Str("loteId");
        await (await c.PostAsJsonAsync($"/api/lotes/{lote}/reclasificar", new { cantidad = 1, condicion = "danado" })).Json(409);
        // Bajar a 1 pieza libera una
        var e = await (await c.PutAsJsonAsync($"/api/ventas/{id}", new { productoId = prod, cantidad = 1, precio = 500m, estado = "apartado", fecha = "2026-11-18", canal = "Conocidos" })).Json();
        Assert.Equal(315.90m, e.GetProperty("venta").Dec("costoTotal"));
        Assert.Equal(1, (await (await c.GetAsync($"/api/productos/{prod}/existencias")).Json())[0].GetProperty("disponibles").GetInt32());
        // Venta apartada sin cobro: se puede eliminar y las piezas regresan
        await (await c.DeleteAsync($"/api/ventas/{id}")).Json(204);
        Assert.Equal(2, (await (await c.GetAsync($"/api/productos/{prod}/existencias")).Json())[0].GetProperty("disponibles").GetInt32());
        var movs = (await (await c.GetAsync($"/api/productos/{prod}/lotes")).Json()).GetProperty("movimientos").EnumerateArray().Select(m => m.Str("tipo")).ToList();
        Assert.Contains("cancelacion_venta", movs);
        // El lote conserva su cantidad costeable
        var lotes = (await (await c.GetAsync($"/api/productos/{prod}/lotes")).Json()).GetProperty("lotes");
        Assert.Equal(2, lotes[0].GetProperty("lote").GetProperty("cantidad").GetInt32());
    }
}
