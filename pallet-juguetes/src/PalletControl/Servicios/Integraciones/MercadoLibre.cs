using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PalletControl.Dominio.Integracion;

namespace PalletControl.Servicios.Integraciones;

/// <summary>
/// Mercado Libre México por su API oficial (developers.mercadolibre.com.mx, consultada el 18-nov-2026):
/// - OAuth 2.0 Authorization Code (server side), PKCE opcional; token en https://api.mercadolibre.com/oauth/token;
///   el refresh_token es de un solo uso y se reemplaza en cada renovación; header "Authorization: Bearer".
/// - Anuncios: GET /users/{id}/items/search (paging offset/limit) y GET /items/bulk?ids= (máx. 20; reemplaza a /items?ids= desde oct-2026).
/// - Cambios: PUT /items/{id} con {"status":"paused"|"active"}, {"available_quantity":n} o {"price":p}.
///   Si el anuncio tiene automatización de precios, el precio se rechaza o se ignora (la plataforma avisa).
/// - Pedidos: GET /orders/search?seller=…&order.date_created.from=…, GET /orders/{id}; order_items[].sale_fee = comisión.
/// - Costo de envío del vendedor: GET /shipments/{id}/costs con header x-format-new: true → senders[].cost.
/// El importe neto depositado lo informa Mercado Pago (otra API, no integrada): el cobro se propone como estimado.
/// </summary>
public class ConectorMercadoLibre : IConector
{
    public string Codigo => "mercadolibre";
    public string Nombre => "Mercado Libre";
    public string PlataformaSugerida => "ml_clasica";
    public bool UsaOAuth => true;
    public bool LeePedidos => true;
    public string Documentacion => "https://developers.mercadolibre.com.mx/es_ar/autenticacion-y-autorizacion";
    public CapacidadesEscritura Escritura => new(Pausar: true, Activar: true, Existencias: true, Precio: true);

    public IReadOnlyList<Capacidad> Capacidades =>
    [
        new("existencias", "Existencias y pausas", true, "Pausa, reactiva y ajusta piezas de tus anuncios (PUT /items)."),
        new("precios", "Precios", true, "Envía el precio registrado en la aplicación. Si el anuncio tiene automatización de precios, Mercado Libre lo rechaza o lo ignora."),
        new("pedidos", "Pedidos → ventas", true, "Importa pedidos pagados con la comisión (sale_fee) y el costo de envío a tu cargo."),
        new("cobros", "Cobros y comisiones", true, "Comisión y envío reales. El neto depositado lo informa Mercado Pago (no integrado): el cobro se propone como estimado y lo confirmas tú."),
    ];

    public IReadOnlyList<CampoConfig> Campos =>
    [
        new("clientId", "Client ID (APP ID)", false, true, "De tu aplicación en developers.mercadolibre.com.mx."),
        new("redirectUri", "Redirect URI", false, true, "Exactamente la registrada en tu aplicación, sin partes variables."),
        new("pkce", "PKCE activado en la aplicación", false, false, "Escribe «sí» si activaste PKCE en tu aplicación (entonces es obligatorio).", "no"),
        new("clientSecret", "Client Secret", true, true, "Se guarda cifrado; nunca se vuelve a mostrar."),
    ];

    private static string Api(ContextoConector c) => c.C("_api", "https://api.mercadolibre.com").TrimEnd('/');

    public string? UrlAutorizacion(ContextoConector c, string state, string? challenge)
    {
        var q = $"response_type=code&client_id={Uri.EscapeDataString(c.C("clientId"))}&redirect_uri={Uri.EscapeDataString(c.C("redirectUri"))}&state={Uri.EscapeDataString(state)}";
        if (challenge is not null) q += $"&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256";
        return $"{c.C("_auth", "https://auth.mercadolibre.com.mx").TrimEnd('/')}/authorization?{q}";
    }

    private async Task<Tokens> TokenAsync(ContextoConector c, Dictionary<string, string> datos, string op)
    {
        var j = await Http.EnviarAsync(c, () => new HttpRequestMessage(HttpMethod.Post, Api(c) + "/oauth/token")
        {
            Content = new FormUrlEncodedContent(datos),
            Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json") } },
        }, op);
        return new Tokens(j.Str("access_token") ?? throw new ErrorIntegracion("Mercado Libre no devolvió el token."),
            j.Str("refresh_token"), j.Int("expires_in") ?? 10800, j.Str("user_id"));
    }

    public Task<Tokens> IntercambiarCodigoAsync(ContextoConector c, string code, string? verifier)
    {
        var d = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = c.C("clientId"), ["client_secret"] = c.S("clientSecret"),
            ["code"] = code, ["redirect_uri"] = c.C("redirectUri"),
        };
        if (verifier is not null) d["code_verifier"] = verifier;
        return TokenAsync(c, d, "oauth: código por token");
    }

    public Task<Tokens> RefrescarAsync(ContextoConector c) => TokenAsync(c, new()
    {
        ["grant_type"] = "refresh_token", ["client_id"] = c.C("clientId"), ["client_secret"] = c.S("clientSecret"), ["refresh_token"] = c.S("refreshToken"),
    }, "oauth: renovar token");

    private static HttpRequestMessage Req(ContextoConector c, HttpMethod m, string ruta, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(m, Api(c) + ruta);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.S("accessToken"));
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (cuerpo is not null) r.Content = JsonContent.Create(cuerpo);
        return r;
    }

    public async Task<string> VerificarAsync(ContextoConector c)
    {
        var j = await Http.EnviarAsync(c, () => Req(c, HttpMethod.Get, "/users/me"), "GET /users/me");
        return j.Str("id") ?? throw new ErrorIntegracion("Respuesta inesperada de /users/me.");
    }

    private static string EstatusItem(string? s) => s switch { "active" => "activa", "paused" => "pausada", "closed" => "cerrada", _ => "otra" };

    public async Task<List<PublicacionRemota>> LeerPublicacionesAsync(ContextoConector c, IReadOnlyCollection<string> ids)
    {
        var lista = ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
        if (lista.Count == 0)
        {
            // Lista de anuncios de la cuenta (para vincular). Se limita a 1,000; con más se requiere search_type=scan.
            for (var offset = 0; offset < 1000; offset += 50)
            {
                var j = await Http.EnviarAsync(c, () => Req(c, HttpMethod.Get, $"/users/{Uri.EscapeDataString(c.CuentaExterna)}/items/search?offset={offset}&limit=50"), "GET items/search");
                var res = j.Arr("results").Select(x => x.GetString()!).ToList();
                lista.AddRange(res);
                var total = j.Obj("paging").Int("total") ?? 0;
                if (res.Count == 0 || offset + 50 >= total) break;
            }
        }
        var salida = new List<PublicacionRemota>();
        foreach (var bloque in lista.Chunk(20))
        {
            var j = await Http.EnviarAsync(c, () => Req(c, HttpMethod.Get, "/items/bulk?ids=" + string.Join(",", bloque.Select(Uri.EscapeDataString))), "GET items/bulk");
            foreach (var x in j.ValueKind == JsonValueKind.Array ? j.EnumerateArray() : Enumerable.Empty<JsonElement>())
            {
                var codigo = x.Int("status_code") ?? x.Int("code");
                var b = x.Obj("body");
                if (codigo != 200 || b.ValueKind != JsonValueKind.Object) continue;
                var variaciones = b.Arr("variations").Any();
                salida.Add(new PublicacionRemota(b.Str("id") ?? x.Str("id") ?? "", b.Str("title") ?? "", b.Dec("price"), b.Int("available_quantity"),
                    EstatusItem(b.Str("status")), b.Str("permalink"), variaciones, b.Str("status") ?? ""));
            }
        }
        return salida;
    }

    public async Task AplicarAsync(ContextoConector c, CambioPropuesto x)
    {
        object cuerpo = x.Tipo switch
        {
            "pausar" => new { status = "paused" },
            "activar" => new { status = "active" },
            "existencias" => new { available_quantity = x.Existencias ?? throw new ErrorIntegracion("Falta la cantidad.", 400) },
            "precio" => new { price = x.Precio ?? throw new ErrorIntegracion("Falta el precio.", 400) },
            _ => throw new ErrorIntegracion($"Cambio no soportado: {x.Tipo}.", 400),
        };
        var j = await Http.EnviarAsync(c, () => Req(c, HttpMethod.Put, "/items/" + Uri.EscapeDataString(x.Referencia), cuerpo), $"PUT items ({x.Tipo})");
        // Con automatización de precios, Mercado Libre puede responder 200 con un aviso de que el precio no se actualizó.
        if (x.Tipo == "precio" && j.Arr("warnings").Any())
            throw new ErrorIntegracion("Mercado Libre no actualizó el precio: " + Http.Recortar(string.Join(" ", j.Arr("warnings").Select(w => w.Str("message") ?? w.ToString())), 200), 409);
    }

    private static string EstatusPedido(string? s) => s switch
    {
        "paid" => "pagado",
        "cancelled" => "cancelado",
        "confirmed" or "payment_required" or "payment_in_process" or "partially_paid" => "pendiente",
        _ => "otro",   // partially_refunded, pending_cancel…: se revisan a mano
    };

    private static DateOnly Fecha(string? iso) => DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? DateOnly.FromDateTime(d.Date) : DateOnly.FromDateTime(DateTime.Now);

    private async Task<PedidoRemoto> PedidoAsync(ContextoConector c, JsonElement o)
    {
        var estatus = EstatusPedido(o.Str("status"));
        decimal? envio = null;
        var envioId = o.Obj("shipping").Str("id");
        if (estatus == "pagado" && envioId is not null)
        {
            var costos = await Http.EnviarAsync(c, () =>
            {
                var r = Req(c, HttpMethod.Get, $"/shipments/{Uri.EscapeDataString(envioId)}/costs");
                r.Headers.Add("x-format-new", "true");
                return r;
            }, "GET shipments/costs");
            envio = costos.Arr("senders").Where(s => s.Str("user_id") == c.CuentaExterna).Select(s => s.Dec("cost")).FirstOrDefault();
        }
        var lineas = o.Arr("order_items").Select(i => new LineaRemota(i.Obj("item").Str("id") ?? "", i.Obj("item").Str("title") ?? "",
            i.Int("quantity") ?? 0, i.Dec("unit_price") ?? 0m, i.Dec("sale_fee") ?? 0m)).Where(l => l.Cantidad > 0).ToList();
        return new PedidoRemoto(o.Str("id") ?? "", Fecha(o.Str("date_closed") ?? o.Str("date_created")), estatus, lineas, o.Dec("total_amount") ?? 0m, envio, o.Str("status") ?? "");
    }

    public async Task<List<PedidoRemoto>> LeerPedidosAsync(ContextoConector c, DateOnly desde, IReadOnlyCollection<string> conocidos)
    {
        var res = new List<PedidoRemoto>();
        var vistos = new HashSet<string>();
        var desdeTxt = Uri.EscapeDataString(desde.ToString("yyyy-MM-dd") + "T00:00:00.000-00:00");
        for (var offset = 0; offset < 2000; offset += 50)
        {
            var j = await Http.EnviarAsync(c, () => Req(c, HttpMethod.Get,
                $"/orders/search?seller={Uri.EscapeDataString(c.CuentaExterna)}&order.date_created.from={desdeTxt}&offset={offset}&limit=50"), "GET orders/search");
            var pagina = j.Arr("results").ToList();
            foreach (var o in pagina) { var p = await PedidoAsync(c, o); if (vistos.Add(p.Id)) res.Add(p); }
            var total = j.Obj("paging").Int("total") ?? 0;
            if (pagina.Count == 0 || offset + 50 >= total) break;
        }
        // Pedidos ya importados que no salieron en la búsqueda (más antiguos o cancelados): se consultan uno por uno.
        foreach (var id in conocidos.Where(id => !vistos.Contains(id)).Take(200))
        {
            var o = await Http.EnviarAsync(c, () => Req(c, HttpMethod.Get, "/orders/" + Uri.EscapeDataString(id)), "GET orders/{id}");
            res.Add(await PedidoAsync(c, o));
        }
        return res;
    }
}
