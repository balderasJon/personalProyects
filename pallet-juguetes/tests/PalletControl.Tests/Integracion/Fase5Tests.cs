using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>
/// Simula las APIs de Mercado Libre y Meta con las formas de respuesta de su documentación oficial.
/// Nunca se llama a internet. Datos de PRUEBA.
/// </summary>
public class PlataformaSimulada : HttpMessageHandler
{
    public readonly List<(string Metodo, string Url, string Cuerpo)> Llamadas = [];
    public int Emitidos;
    public string UltimoRefresh = "";
    public int ExpiraSegundos = 10800;
    public int ExistenciasMl = 5;
    public bool PrecioConAviso;
    public string EstatusPedido = "paid";

    private static HttpResponseMessage J(object o, HttpStatusCode c = HttpStatusCode.OK) =>
        new(c) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        var cuerpo = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
        var url = r.RequestUri!;
        lock (Llamadas) Llamadas.Add((r.Method.Method, url.PathAndQuery, cuerpo));
        var ruta = url.AbsolutePath;
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);

        // ---- Mercado Libre ----
        if (url.Host == "ml.prueba")
        {
            if (ruta == "/oauth/token")
            {
                var f = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?" + cuerpo);
                if (f["client_secret"] != "secreto-prueba") return J(new { error = "invalid_client" }, HttpStatusCode.BadRequest);
                if (f["grant_type"] == "authorization_code" && f["code"] != "TG-codigo") return J(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
                // El refresh token es de un solo uso: solo sirve el último emitido.
                if (f["grant_type"] == "refresh_token" && f["refresh_token"] != UltimoRefresh) return J(new { error = "invalid_grant", error_description = "Error validating grant." }, HttpStatusCode.BadRequest);
                Emitidos++;
                UltimoRefresh = $"TG-refresh-{Emitidos}";
                return J(new { access_token = $"APP_USR-{Emitidos}", token_type = "bearer", expires_in = ExpiraSegundos, scope = "offline_access read write", user_id = 999, refresh_token = UltimoRefresh });
            }
            if (r.Headers.Authorization?.Parameter != $"APP_USR-{Emitidos}") return J(new { message = "invalid_token" }, HttpStatusCode.Unauthorized);
            if (ruta == "/users/me") return J(new { id = 999, nickname = "VENDEDOR_PRUEBA" });
            if (ruta == "/items/bulk")
                return J(q["ids"].ToString().Split(',').Select(id => new
                {
                    id, status_code = 200,
                    body = new { id, title = "Rompecabezas de prueba", price = 639, available_quantity = ExistenciasMl, status = "active", permalink = "https://articulo.prueba/" + id, variations = Array.Empty<object>() },
                }));
            if (r.Method == HttpMethod.Put && ruta.StartsWith("/items/"))
                return PrecioConAviso && cuerpo.Contains("price") ? J(new { id = ruta[7..], warnings = new[] { new { message = "price was not updated: automation active" } } }) : J(new { id = ruta[7..] });
            if (ruta == "/orders/search")
                return J(new { results = new[] { Pedido() }, paging = new { total = 1, offset = 0, limit = 50 } });
            if (ruta == "/orders/2001") return J(Pedido());
            if (ruta == "/shipments/77/costs")
                return r.Headers.Contains("x-format-new") ? J(new { gross_amount = 150, senders = new[] { new { user_id = 999, cost = 120 } } }) : J(new { message = "falta header" }, HttpStatusCode.BadRequest);
        }
        // ---- Meta (Graph API) ----
        if (url.Host == "graph.prueba")
        {
            if (!(q.TryGetValue("access_token", out var tk) && tk == "token-meta") && !cuerpo.Contains("access_token=token-meta")) return J(new { error = new { message = "Invalid OAuth 2.0 Access Token", code = 190 } }, HttpStatusCode.BadRequest);
            if (ruta.EndsWith("/products"))
                return J(new { data = new[] { new { retailer_id = "R-1", name = "Rompecabezas", availability = "in stock", quantity_to_sell_on_facebook = 3, id = "555" } }, paging = new { } });
            if (ruta.EndsWith("/items_batch")) return J(new { handles = new[] { "h1" }, validation_status = Array.Empty<object>() });
        }
        return J(new { message = "no encontrado" }, HttpStatusCode.NotFound);
    }

    private object Pedido() => new
    {
        id = 2001, status = EstatusPedido, date_created = "2026-11-17T10:00:00.000-06:00", date_closed = "2026-11-17T10:04:07.000-06:00",
        order_items = new[] { new { item = new { id = "MLM1", title = "Rompecabezas de prueba" }, quantity = 1, unit_price = 639, sale_fee = 95.85, currency_id = "MXN" } },
        total_amount = 639, paid_amount = 639, shipping = new { id = 77 }, seller = new { id = 999 },
    };
}

public class IntegracionFactory : AppFactory
{
    public PlataformaSimulada Simulada { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        base.ConfigureWebHost(b);
        b.UseSetting("Integraciones:MercadoLibre:Api", "https://ml.prueba");
        b.UseSetting("Integraciones:MercadoLibre:Auth", "https://auth.ml.prueba");
        b.UseSetting("Integraciones:Meta:Graph", "https://graph.prueba");
        b.ConfigureTestServices(s => s.AddHttpClient("integraciones").ConfigurePrimaryHttpMessageHandler(() => Simulada));
    }
}

/// <summary>Fase 5: conectar, leer, revisar propuestas y aplicar solo lo confirmado.</summary>
public class Fase5Tests : IClassFixture<IntegracionFactory>
{
    private readonly IntegracionFactory f;
    public Fase5Tests(IntegracionFactory f) => this.f = f;
    private static readonly SemaphoreSlim Uno = new(1, 1);

    private async Task<(HttpClient C, string Prod, string Ml, string Fb)> Base()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        var pls = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").EnumerateArray().Select(p => p.GetProperty("plataforma")).ToList();
        string Pl(string k) => pls.Single(p => p.Str("codigo") == k).Str("id");
        var ml = Pl("ml_clasica"); var fb = Pl("facebook_marketplace");
        await (await c.PostAsJsonAsync("/api/tarifas", new { plataformaId = ml, nombre = "ML prueba", comisionPct = 15m, cargoFijo = 25m, envioAbsorbido = 120m, empaque = 10m, vigenteDesde = "2026-01-01", predeterminada = true })).Json();
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "F5", moneda = "MXN", costoOriginal = 631.80m, tipoCambio = 1m, fechaCompra = "2026-11-01" })).Json(201)).Str("id");
        var prod = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "R-1", nombre = "Rompecabezas", cantidad = 2, precioObjetivo = 649m, palletId = pal })).Json(201)).Str("id");
        await (await c.PostAsJsonAsync($"/api/pallets/{pal}/costeo/aplicar", new { metodo = "equitativa" })).Json();
        return (c, prod, ml, fb);
    }

    private static string[] Ids(JsonElement r, params string[] tipos) =>
        r.GetProperty("propuestas").EnumerateArray().Where(p => tipos.Contains(p.Str("tipo")) && p.Str("estado") == "pendiente").Select(p => p.Str("id")).ToArray();
    private static JsonElement Prop(JsonElement r, string tipo) => r.GetProperty("propuestas").EnumerateArray().Single(p => p.Str("tipo") == tipo);

    [Fact]
    public async Task MercadoLibre_ConectarLeerYAplicarSoloLoConfirmado()
    {
        await Uno.WaitAsync();
        try
        {
            var (c, prod, ml, _) = await Base();
            var cuenta = await (await c.PostAsJsonAsync("/api/integraciones/cuentas", new
            {
                conector = "mercadolibre", plataformaId = ml,
                config = new Dictionary<string, string> { ["clientId"] = "12345", ["redirectUri"] = "https://localhost/cb" },
                secretos = new Dictionary<string, string> { ["clientSecret"] = "secreto-prueba" },
            })).Json(201);
            var id = cuenta.Str("id");
            // El secreto nunca se devuelve
            var lista = await (await c.GetAsync("/api/integraciones")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("secreto-prueba", lista);
            Assert.Contains("clientSecret", lista);
            Assert.Contains("\"disponible\":false", lista);   // Amazon: no disponible sin documentación verificada

            // Autorización: URL oficial con state; un state ajeno se rechaza; pegar la URL de regreso conecta la cuenta.
            var url = (await (await c.PostAsync($"/api/integraciones/cuentas/{id}/autorizar", null)).Json()).Str("url");
            Assert.StartsWith("https://auth.ml.prueba/authorization?response_type=code&client_id=12345&redirect_uri=https%3A%2F%2Flocalhost%2Fcb&state=", url);
            var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
            await (await c.PostAsJsonAsync($"/api/integraciones/cuentas/{id}/codigo", new { urlOCodigo = "https://localhost/cb?code=TG-codigo&state=otro" })).Json(400);
            f.Simulada.ExpiraSegundos = 30;   // el token vence pronto: la siguiente llamada debe renovarlo
            var con = await (await c.PostAsJsonAsync($"/api/integraciones/cuentas/{id}/codigo", new { urlOCodigo = $"https://localhost/cb?code=TG-codigo&state={state}" })).Json();
            Assert.Equal("conectada", con.Str("estado"));
            Assert.Equal("999", con.Str("cuentaExterna"));
            f.Simulada.ExpiraSegundos = 10800;

            // Publicación vinculada al anuncio MLM1: anuncia 5 piezas a $639; en la aplicación hay 2 a $649.
            await (await c.PostAsJsonAsync("/api/publicaciones", new { productoId = prod, plataformaId = ml, idExterno = "MLM1", precioPublicado = 649m, estatus = "publicado", existencias = 2 })).Json(201);
            var r = await (await c.PostAsync($"/api/integraciones/cuentas/{id}/leer", null)).Json();
            Assert.Equal(2, f.Simulada.Emitidos);   // se renovó el token con el refresh token vigente
            Assert.Equal("2", Prop(r, "existencias").Str("despues").Split(' ')[0]);
            Assert.Equal("$649.00", Prop(r, "precio").Str("despues"));
            var nv = Prop(r, "nueva_venta");
            Assert.Equal("2001/MLM1", nv.Str("referenciaExterna"));

            // Se confirman existencias y la venta; el precio NO.
            var llamadasAntes = f.Simulada.Llamadas.Count(l => l.Metodo == "PUT");
            var ap = await (await c.PostAsJsonAsync($"/api/integraciones/lotes/{r.GetProperty("lote").Str("id")}/aplicar", new { propuestas = Ids(r, "existencias", "nueva_venta") })).Json();
            Assert.Equal("aplicado", ap.GetProperty("lote").Str("estado"));
            Assert.Equal("rechazada", Prop(ap, "precio").Str("estado"));
            var puts = f.Simulada.Llamadas.Where(l => l.Metodo == "PUT").Skip(llamadasAntes).ToList();
            Assert.Equal(("PUT", "/items/MLM1", "{\"available_quantity\":2}"), Assert.Single(puts));

            // La venta importada lleva la comisión y el envío reales (no los estimados de la tarifa)
            var est = await (await c.GetAsync("/api/estado")).Json();
            var v = est.GetProperty("ventas").EnumerateArray().Single(x => x.Str("productoId") == prod);
            Assert.Equal("2001/MLM1", v.Str("pedidoExterno"));
            var det = await (await c.GetAsync($"/api/ventas/{v.Str("id")}")).Json();
            Assert.Equal(new[] { 95.85m, 120m }, det.GetProperty("gastos").EnumerateArray().Select(g => g.Dec("monto")).OrderBy(x => x));
            Assert.Equal(107.25m, det.GetProperty("resultado").Dec("utilidad"));   // 639 − 95.85 − 120 − 315.90
            Assert.False(det.GetProperty("resultado").GetProperty("cobrada").GetBoolean());

            // Segunda lectura: no se duplica el pedido; se propone el cobro estimado y ajustar existencias a 1.
            var r2 = await (await c.PostAsync($"/api/integraciones/cuentas/{id}/leer", null)).Json();
            Assert.DoesNotContain(r2.GetProperty("propuestas").EnumerateArray(), p => p.Str("tipo") == "nueva_venta");
            Assert.Equal("$423.15", Prop(r2, "registrar_cobro").Str("despues"));
            var ap2 = await (await c.PostAsJsonAsync($"/api/integraciones/lotes/{r2.GetProperty("lote").Str("id")}/aplicar", new { propuestas = Ids(r2, "registrar_cobro") })).Json();
            Assert.Equal("aplicado", ap2.GetProperty("lote").Str("estado"));
            det = await (await c.GetAsync($"/api/ventas/{v.Str("id")}")).Json();
            Assert.Equal(423.15m, det.GetProperty("venta").Dec("importeCobrado"));

            // Precio con automatización: Mercado Libre responde 200 con aviso → la propuesta queda en error con el motivo.
            f.Simulada.PrecioConAviso = true;
            var r3 = await (await c.PostAsync($"/api/integraciones/cuentas/{id}/leer", null)).Json();
            var ap3 = await (await c.PostAsJsonAsync($"/api/integraciones/lotes/{r3.GetProperty("lote").Str("id")}/aplicar", new { propuestas = Ids(r3, "precio") })).Json();
            Assert.Equal("error", Prop(ap3, "precio").Str("estado"));
            Assert.Contains("no actualizó el precio", Prop(ap3, "precio").Str("resultado"));
            // Un lote aplicado no se vuelve a aplicar
            await (await c.PostAsJsonAsync($"/api/integraciones/lotes/{r3.GetProperty("lote").Str("id")}/aplicar", new { propuestas = Ids(r3, "precio") })).Json(409);

            // Pedido cancelado en la plataforma → se propone cancelar la venta
            f.Simulada.EstatusPedido = "cancelled";
            var r4 = await (await c.PostAsync($"/api/integraciones/cuentas/{id}/leer", null)).Json();
            Assert.Equal(v.Str("id"), Prop(r4, "cancelar_venta").Str("ventaId"));
            f.Simulada.EstatusPedido = "paid";
            Assert.NotEmpty((await (await c.GetAsync($"/api/integraciones/cuentas/{id}/eventos")).Json()).EnumerateArray());
        }
        finally { Uno.Release(); }
    }

    [Fact]
    public async Task Meta_CatalogoExistenciasConTokenCifrado()
    {
        await Uno.WaitAsync();
        try
        {
            var c = await f.ClienteConSesionAsyncUnaVez();
            var pls = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").EnumerateArray().Select(p => p.GetProperty("plataforma")).ToList();
            var fb = pls.Single(p => p.Str("codigo") == "facebook_marketplace").Str("id");
            var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "F5 Meta", moneda = "MXN", costoOriginal = 100m, tipoCambio = 1m })).Json(201)).Str("id");
            var prod = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "MT-1", nombre = "Carrito", cantidad = 1, precioObjetivo = 200m, palletId = pal })).Json(201)).Str("id");
            var cuenta = await (await c.PostAsJsonAsync("/api/integraciones/cuentas", new
            {
                conector = "meta", plataformaId = fb,
                config = new Dictionary<string, string> { ["catalogId"] = "777" },
                secretos = new Dictionary<string, string> { ["accessToken"] = "token-meta" },
            })).Json(201);
            var id = cuenta.Str("id");
            Assert.Equal("v25.0", cuenta.GetProperty("config").Str("graphVersion"));
            Assert.Equal("conectada", (await (await c.PostAsync($"/api/integraciones/cuentas/{id}/probar", null)).Json()).Str("estado"));
            await (await c.PostAsJsonAsync($"/api/integraciones/cuentas/{id}/vincular", new { idExterno = "R-1", productoId = prod })).Json(201);
            var r = await (await c.PostAsync($"/api/integraciones/cuentas/{id}/leer", null)).Json();
            var ex = Prop(r, "existencias");
            Assert.Equal("3 pz", ex.Str("antes"));
            Assert.Equal("1 pz", ex.Str("despues"));
            Assert.DoesNotContain(r.GetProperty("propuestas").EnumerateArray(), p => p.Str("tipo") is "nueva_venta" or "precio");
            await (await c.PostAsJsonAsync($"/api/integraciones/lotes/{r.GetProperty("lote").Str("id")}/aplicar", new { propuestas = Ids(r, "existencias") })).Json();
            var batch = f.Simulada.Llamadas.Last(l => l.Url.Contains("items_batch"));
            var form = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?" + batch.Cuerpo);
            Assert.Equal("PRODUCT_ITEM", form["item_type"]);
            Assert.Equal("false", form["allow_upsert"]);
            Assert.Equal("[{\"method\":\"UPDATE\",\"data\":{\"id\":\"R-1\",\"quantity_to_sell_on_facebook\":1}}]", form["requests"]);
            // El token se guarda cifrado en la base
            var lista = await (await c.GetAsync("/api/integraciones")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("token-meta", lista);
            var respaldo = await (await c.GetAsync("/api/respaldo")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("token-meta", respaldo);
        }
        finally { Uno.Release(); }
    }

    [Fact]
    public async Task Callback_SinStateValido_NoConecta()
    {
        var anon = f.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await anon.GetAsync("/api/integraciones/mercadolibre/callback?code=TG-codigo&state=inventado");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("integracion=error", r.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/integraciones")).StatusCode);
    }
}
