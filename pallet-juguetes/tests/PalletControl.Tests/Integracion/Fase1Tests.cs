using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>Pruebas de integración de la Fase 1 (pallets, lotes, costeo, seguridad y auditoría) contra la API real.</summary>
public class Fase1Tests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    private static readonly SemaphoreSlim Uno = new(1, 1);
    private static HttpClient? cliente;

    public Fase1Tests(AppFactory f) => this.f = f;

    private async Task<HttpClient> C()
    {
        await Uno.WaitAsync();
        try { return cliente ??= await f.ClienteConSesionAsync(); }
        finally { Uno.Release(); }
    }

    private static async Task<JsonElement> NuevoPallet(HttpClient c, string nombre, decimal costo, string moneda = "MXN", decimal tc = 1m) =>
        (await (await c.PostAsJsonAsync("/api/pallets", new { nombre, moneda, costoOriginal = costo, tipoCambio = tc, tipoCambioFuente = "Manual", paisOrigen = "Estados Unidos" })).Json(201));

    private static async Task<string> NuevoProducto(HttpClient c, string palletId, string codigo, int cantidad, decimal precioMexico, string condicion = "nuevo_sellado") =>
        (await (await c.PostAsJsonAsync("/api/productos", new { codigo, nombre = "Juguete " + codigo, cantidad, precioMexico, palletId, condicion })).Json(201)).Str("id");

    private static async Task<JsonElement> Lotes(HttpClient c, string productoId) =>
        (await (await c.GetAsync($"/api/productos/{productoId}/lotes")).Json()).GetProperty("lotes");

    [Fact]
    public async Task Seguridad_SinSesion_ApiEImagenesResponden401()
    {
        var anon = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/estado")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/pallets")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/imagenes/x.png")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/api/auth/estado")).StatusCode);
    }

    // Caso 1 y 2 por la API: pallet en USD, conversión y gastos adicionales incluidos / no incluidos.
    [Fact]
    public async Task Caso01_PalletUsdConGastos()
    {
        var c = await C();
        var p = await NuevoPallet(c, "USD prueba", 2500m, "USD", 18.7147m);
        Assert.Equal(46786.75m, p.Dec("costoMxn"));
        var id = p.Str("id");
        await (await c.PostAsJsonAsync($"/api/pallets/{id}/gastos", new { concepto = "Flete a bodega", tipo = "Flete adicional", montoMxn = 1200m, incluidoEnCosto = false })).Json();
        var g = await (await c.PostAsJsonAsync($"/api/pallets/{id}/gastos", new { concepto = "Importación (ya incluida)", montoMxn = 500m, incluidoEnCosto = true })).Json();
        Assert.Equal(1200m, g.GetProperty("pallet").Dec("gastosAdicionales"));
        Assert.Equal(47986.75m, g.GetProperty("pallet").Dec("inversionTotal"));
        // Tipo de cambio obligatorio en USD
        await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "Sin TC", moneda = "USD", costoOriginal = 10m, tipoCambio = 0m })).Json(400);
    }

    [Fact]
    public async Task Producto_AltaCreaLoteYMovimiento_ReclasificarYAjustar()
    {
        var c = await C();
        var pid = (await NuevoPallet(c, "Lotes", 1000m)).Str("id");
        var prod = await NuevoProducto(c, pid, "L-001", 10, 100m);
        var l = await Lotes(c, prod);
        Assert.Equal(1, l.GetArrayLength());
        var lote = l[0].GetProperty("lote");
        Assert.Equal(10, lote.GetProperty("cantidad").GetInt32());

        // Caso 6: 2 piezas resultan dañadas → nuevo lote no comercializable; el total del juguete no cambia.
        var r = await (await c.PostAsJsonAsync($"/api/lotes/{lote.Str("id")}/reclasificar", new { cantidad = 2, condicion = "danado", motivo = "Caja rota" })).Json();
        Assert.Equal(8, r.GetProperty("origen").GetProperty("cantidad").GetInt32());
        Assert.Equal(2, r.GetProperty("destino").GetProperty("cantidad").GetInt32());
        Assert.False(r.GetProperty("destino").GetProperty("comercializable").GetBoolean());
        var estado = await (await c.GetAsync("/api/estado")).Json();
        var p = estado.GetProperty("productos").EnumerateArray().Single(x => x.Str("id") == prod);
        Assert.Equal(10, p.GetProperty("cantidad").GetInt32());

        // Con varios lotes, la cantidad no se edita desde la ficha
        await (await c.PutAsJsonAsync($"/api/productos/{prod}", new { codigo = "L-001", nombre = "Juguete L-001", cantidad = 12, precioMexico = 100m })).Json(400);

        // No se puede ajustar por debajo de lo vendido
        await (await c.PostAsJsonAsync("/api/ventas", new { productoId = prod, cantidad = 7, precio = 150m, estado = "pagado" })).Json(201);
        // 8 comercializables + 2 dañadas = 10; quitar 4 dejaría 6 < 7 vendidas
        await (await c.PostAsJsonAsync($"/api/lotes/{lote.Str("id")}/ajuste", new { cantidad = -4, motivo = "Conteo" })).Json(409);
        await (await c.PostAsJsonAsync($"/api/lotes/{lote.Str("id")}/ajuste", new { cantidad = 3, motivo = "Llegaron más" })).Json();
        var movs = (await (await c.GetAsync($"/api/productos/{prod}/lotes")).Json()).GetProperty("movimientos");
        Assert.Equal(["ajuste", "reclasificacion_entrada", "reclasificacion_salida", "recepcion"], movs.EnumerateArray().Select(m => m.Str("tipo")));
    }

    // Caso 4 de punta a punta: mismo resultado que la prueba unitaria y cuadre exacto.
    [Fact]
    public async Task Caso04_CosteoProporcionalAplicado()
    {
        var c = await C();
        var pid = (await NuevoPallet(c, "Proporcional", 49000m)).Str("id");
        var a = await NuevoProducto(c, pid, "P-1", 12, 1099m);
        var b = await NuevoProducto(c, pid, "P-2", 30, 150m);
        var d = await NuevoProducto(c, pid, "P-3", 4, 1274.50m);
        var r = await (await c.PostAsJsonAsync($"/api/pallets/{pid}/costeo/aplicar", new { metodo = "proporcional", modoMerma = "absorber" })).Json();
        var res = r.GetProperty("resultado");
        Assert.True(res.GetProperty("ok").GetBoolean());
        decimal Costo(string prod) => res.GetProperty("lotes").EnumerateArray().Single(x => x.GetProperty("producto").Str("id") == prod).GetProperty("resultado").Dec("costoTotal");
        Assert.Equal(28360.05m, Costo(a));
        Assert.Equal(9676.99m, Costo(b));
        Assert.Equal(10962.96m, Costo(d));
        Assert.Equal(49000m, res.Dec("totalComercializable") + res.Dec("totalMerma"));
    }

    // Caso 5: manual que no cuadra → rechazado sin guardar.
    [Fact]
    public async Task Caso05_ManualQueNoCuadraEsRechazado()
    {
        var c = await C();
        var pid = (await NuevoPallet(c, "Manual", 1000m)).Str("id");
        var a = await NuevoProducto(c, pid, "M-1", 2, 0m);
        var b = await NuevoProducto(c, pid, "M-2", 1, 0m);
        var la = (await Lotes(c, a))[0].GetProperty("lote").Str("id");
        var lb = (await Lotes(c, b))[0].GetProperty("lote").Str("id");
        var mal = await c.PostAsJsonAsync($"/api/pallets/{pid}/costeo/aplicar", new { metodo = "manual", costosManuales = new Dictionary<string, decimal> { [la] = 600m, [lb] = 399.99m } });
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        var ok = await (await c.PostAsJsonAsync($"/api/pallets/{pid}/costeo/aplicar", new { metodo = "manual", costosManuales = new Dictionary<string, decimal> { [la] = 600m, [lb] = 400m } })).Json();
        Assert.Equal(1000m, ok.GetProperty("resultado").Dec("totalComercializable"));
    }

    // Caso 18: modificar costos conserva el historial y marca el costeo anterior como desactualizado.
    [Fact]
    public async Task Caso18_ModificacionDeCostosConservaHistorial()
    {
        var c = await C();
        var pid = (await NuevoPallet(c, "Historial", 1000m)).Str("id");
        await NuevoProducto(c, pid, "H-1", 3, 0m);
        await NuevoProducto(c, pid, "H-2", 4, 0m);
        var r1 = await (await c.PostAsJsonAsync($"/api/pallets/{pid}/costeo/aplicar", new { metodo = "equitativa" })).Json();
        var id1 = r1.GetProperty("corrida").Str("id");

        await (await c.PostAsJsonAsync($"/api/pallets/{pid}/gastos", new { concepto = "Maniobras", montoMxn = 400m, incluidoEnCosto = false })).Json();
        var det = await (await c.GetAsync($"/api/pallets/{pid}")).Json();
        Assert.True(det.GetProperty("costeo").GetProperty("desactualizado").GetBoolean());

        var r2 = await (await c.PostAsJsonAsync($"/api/pallets/{pid}/costeo/aplicar", new { metodo = "equitativa", notas = "Con maniobras" })).Json();
        Assert.Equal(1400m, r2.GetProperty("corrida").Dec("totalDistribuible"));
        det = await (await c.GetAsync($"/api/pallets/{pid}")).Json();
        Assert.False(det.GetProperty("costeo").GetProperty("desactualizado").GetBoolean());
        Assert.Equal(2, det.GetProperty("historial").GetArrayLength());

        // La corrida anterior no se modificó: sigue con 1,000.00 (428.57 + 571.43)
        var viejo = await (await c.GetAsync($"/api/costeo/corridas/{id1}")).Json();
        Assert.False(viejo.GetProperty("corrida").GetProperty("vigente").GetBoolean());
        Assert.Equal(1000m, viejo.GetProperty("costos").EnumerateArray().Sum(x => x.GetProperty("costo").Dec("costoTotal")));
    }

    [Fact]
    public async Task InversionGlobal_EsLaSumaDeLosPallets_YNoSeBorraPalletConLotes()
    {
        var c = await C();
        var pid = (await NuevoPallet(c, "Global", 5000m)).Str("id");
        await NuevoProducto(c, pid, "G-1", 1, 0m);
        var est = await (await c.GetAsync("/api/estado")).Json();
        var suma = est.GetProperty("pallets").EnumerateArray().Sum(p => p.Dec("inversionTotal"));
        Assert.Equal(suma, est.GetProperty("configuracion").Dec("inversion"));
        await (await c.DeleteAsync($"/api/pallets/{pid}")).Json(409);

        // Un pallet sin juguetes sí se borra y la inversión global se actualiza en la misma operación.
        var vacio = (await NuevoPallet(c, "Vacío", 1234.56m)).Str("id");
        var antes = (await (await c.GetAsync("/api/estado")).Json()).GetProperty("configuracion").Dec("inversion");
        await (await c.DeleteAsync($"/api/pallets/{vacio}")).Json(204);
        var despues = (await (await c.GetAsync("/api/estado")).Json()).GetProperty("configuracion").Dec("inversion");
        Assert.Equal(antes - 1234.56m, despues);
    }

    [Fact]
    public async Task Auditoria_RegistraUsuarioYCambios()
    {
        var c = await C();
        var pid = (await NuevoPallet(c, "Auditado", 777m)).Str("id");
        await (await c.PutAsJsonAsync($"/api/pallets/{pid}", new { nombre = "Auditado", moneda = "MXN", costoOriginal = 800m, tipoCambio = 1m })).Json();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PalletControl.Data.AppDbContext>();
        var regs = db.Auditoria.Where(a => a.Entidad == "pallets" && a.EntidadId == pid).OrderBy(a => a.Id).ToList();
        Assert.Equal(["alta", "cambio"], regs.Select(r => r.Accion));
        Assert.All(regs, r => Assert.Equal("prueba", r.Usuario));
        var cambio = JsonDocument.Parse(regs[1].Cambios).RootElement.GetProperty("CostoOriginal");
        Assert.Equal(777m, cambio[0].GetDecimal());
        Assert.Equal(800m, cambio[1].GetDecimal());
    }
}
