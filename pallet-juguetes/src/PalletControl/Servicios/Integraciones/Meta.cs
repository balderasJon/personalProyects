using PalletControl.Dominio.Integracion;

namespace PalletControl.Servicios.Integraciones;

/// <summary>
/// Catálogo de Meta (Facebook / Instagram / WhatsApp) por la Graph API (developers.facebook.com, consultada el 18-nov-2026):
/// - Leer productos: GET /{versión}/{catalog_id}/products?fields=… (paginado).
/// - Cambios: POST /{catalog_id}/items_batch con item_type=PRODUCT_ITEM y requests=[{method:"UPDATE", data:{id: retailer_id, …}}];
///   price con formato "639.00 MXN"; availability "in stock" / "out of stock"; quantity_to_sell_on_facebook reemplaza a inventory.
/// - Los catálogos de WhatsApp son los mismos catálogos de Meta.
/// No disponible para esta aplicación:
/// - Pedidos: el pago de Shops ahora ocurre en el sitio del vendedor (offsite checkout); los pedidos de WhatsApp llegan por webhooks,
///   que requieren una URL pública. Se registran a mano como ventas.
/// - Publicaciones personales de Facebook Marketplace: no se encontró API en la documentación para desarrolladores.
/// - Lectura del precio: no se verificó el formato en que la API lo devuelve; el precio no se compara automáticamente.
/// </summary>
public class ConectorMeta : IConector
{
    public string Codigo => "meta";
    public string Nombre => "Meta (catálogo de Facebook, Instagram y WhatsApp)";
    public string PlataformaSugerida => "facebook_marketplace";
    public bool UsaOAuth => false;
    public bool LeePedidos => false;
    public string Documentacion => "https://developers.facebook.com/documentation/ads-commerce/marketing-api/reference/product-catalog/items_batch";
    public CapacidadesEscritura Escritura => new(Pausar: true, Activar: true, Existencias: true, Precio: false);

    public IReadOnlyList<Capacidad> Capacidades =>
    [
        new("existencias", "Existencias y pausas", true, "Marca productos del catálogo como agotados o disponibles y ajusta quantity_to_sell_on_facebook."),
        new("precios", "Precios", false, "No se verificó cómo la API devuelve el precio; cámbialo en Commerce Manager."),
        new("pedidos", "Pedidos → ventas", false, "El pago ocurre fuera de Meta (offsite checkout) y los pedidos de WhatsApp requieren webhooks públicos: regístralos a mano."),
        new("cobros", "Cobros y comisiones", false, "No aplica: Meta no cobra por ti en este esquema."),
        new("marketplace", "Publicaciones personales de Marketplace", false, "No se encontró una API para desarrolladores: se administran a mano."),
    ];

    public IReadOnlyList<CampoConfig> Campos =>
    [
        new("catalogId", "ID del catálogo", false, true, "Commerce Manager › Catálogo › Configuración."),
        new("graphVersion", "Versión de Graph API", false, false, "La que indique la documentación vigente.", "v25.0"),
        new("moneda", "Moneda", false, false, "Código ISO de 3 letras.", "MXN"),
        new("accessToken", "Token de acceso", true, true, "Con permiso para administrar el catálogo. Se guarda cifrado."),
    ];

    private static string Base(ContextoConector c) => $"{c.C("_graph", "https://graph.facebook.com").TrimEnd('/')}/{c.C("graphVersion", "v25.0")}";

    public string? UrlAutorizacion(ContextoConector c, string state, string? challenge) => null;
    public Task<Tokens> IntercambiarCodigoAsync(ContextoConector c, string code, string? v) => throw new ErrorIntegracion("Esta cuenta usa un token capturado, no autorización OAuth.", 400);
    public Task<Tokens> RefrescarAsync(ContextoConector c) => throw new ErrorIntegracion("El token de Meta se captura a mano; reemplázalo cuando venza.", 400);

    public async Task<string> VerificarAsync(ContextoConector c)
    {
        var cat = c.C("catalogId");
        if (cat.Length == 0) throw new ErrorIntegracion("Captura el ID del catálogo.", 400);
        await Http.EnviarAsync(c, () => new HttpRequestMessage(HttpMethod.Get,
            $"{Base(c)}/{Uri.EscapeDataString(cat)}/products?fields=retailer_id&limit=1&access_token={Uri.EscapeDataString(c.S("accessToken"))}"), "GET catalog/products");
        return cat;
    }

    private static string Estatus(string? a) => a switch { "in stock" => "activa", "out of stock" => "pausada", _ => "otra" };

    public async Task<List<PublicacionRemota>> LeerPublicacionesAsync(ContextoConector c, IReadOnlyCollection<string> ids)
    {
        var res = new List<PublicacionRemota>();
        string? url = $"{Base(c)}/{Uri.EscapeDataString(c.C("catalogId"))}/products?fields=retailer_id,name,availability,quantity_to_sell_on_facebook,url&limit=100&access_token={Uri.EscapeDataString(c.S("accessToken"))}";
        for (var pagina = 0; url is not null && pagina < 50; pagina++)
        {
            var actual = url;
            var j = await Http.EnviarAsync(c, () => new HttpRequestMessage(HttpMethod.Get, actual), "GET catalog/products");
            res.AddRange(j.Arr("data").Select(p => new PublicacionRemota(p.Str("retailer_id") ?? "", p.Str("name") ?? "", null,
                p.Int("quantity_to_sell_on_facebook"), Estatus(p.Str("availability")), p.Str("url"), false, p.Str("availability") ?? "")));
            url = j.Obj("paging").Str("next");
        }
        var filtro = ids.ToHashSet();
        return filtro.Count == 0 ? res : res.Where(r => filtro.Contains(r.IdExterno)).ToList();
    }

    public async Task AplicarAsync(ContextoConector c, CambioPropuesto x)
    {
        var data = new Dictionary<string, object> { ["id"] = x.Referencia };
        switch (x.Tipo)
        {
            case "pausar": data["availability"] = "out of stock"; break;
            case "activar": data["availability"] = "in stock"; break;
            case "existencias":
                data["quantity_to_sell_on_facebook"] = x.Existencias ?? 0;
                if (x.Existencias == 0) data["availability"] = "out of stock";
                break;
            default: throw new ErrorIntegracion($"Cambio no soportado en Meta: {x.Tipo}.", 400);
        }
        var requests = System.Text.Json.JsonSerializer.Serialize(new[] { new { method = "UPDATE", data } });
        var j = await Http.EnviarAsync(c, () => new HttpRequestMessage(HttpMethod.Post, $"{Base(c)}/{Uri.EscapeDataString(c.C("catalogId"))}/items_batch")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["item_type"] = "PRODUCT_ITEM", ["allow_upsert"] = "false", ["requests"] = requests, ["access_token"] = c.S("accessToken"),
            }),
        }, $"POST items_batch ({x.Tipo})");
        var errores = j.Arr("validation_status").SelectMany(v => v.Arr("errors")).Select(e => e.Str("message")).Where(m => m is not null).ToList();
        if (errores.Count > 0) throw new ErrorIntegracion("Meta rechazó el cambio: " + Http.Recortar(string.Join(" ", errores), 200), 409);
    }

    public Task<List<PedidoRemoto>> LeerPedidosAsync(ContextoConector c, DateOnly desde, IReadOnlyCollection<string> conocidos) => Task.FromResult(new List<PedidoRemoto>());
}
