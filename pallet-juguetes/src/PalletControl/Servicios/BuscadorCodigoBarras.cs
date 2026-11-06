using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Models;

namespace PalletControl.Servicios;

/// <summary>Datos de un producto obtenidos de una fuente externa.</summary>
public record ProductoExterno(
    string Titulo, string Marca, string Categoria, string Descripcion, string Imagen,
    decimal? PrecioBajo, decimal? PrecioAlto, string Moneda, int Ofertas,
    string? Modelo = null, string? Color = null, string? Tamano = null, string? Dimensiones = null, string? Peso = null,
    List<string>? Imagenes = null, string? CaracteristicasEs = null)
{
    /// <summary>Texto para el campo Características del juguete.</summary>
    public string Caracteristicas
    {
        get
        {
            var datos = new (string, string?)[] { ("Marca", Marca), ("Modelo", Modelo), ("Color", Color), ("Tamaño", Tamano), ("Dimensiones", Dimensiones), ("Peso", Peso) }
                .Where(x => !string.IsNullOrWhiteSpace(x.Item2)).Select(x => $"{x.Item1}: {x.Item2!.Trim()}");
            var lineas = new List<string>();
            if (!string.IsNullOrWhiteSpace(Descripcion)) lineas.Add(Descripcion.Trim());
            lineas.AddRange(datos);
            var t = string.Join("\n", lineas);
            return t.Length > 2000 ? t[..2000] : t;
        }
    }
}

/// <summary>Precios sugeridos en MXN y cómo se calculó cada uno.</summary>
public record Sugerencia(decimal? PrecioLista, decimal? PrecioObjetivo, decimal? PrecioMinimo, List<string> Explicacion);

public record ResultadoBusqueda(
    string Origen,                 // inventario | upcitemdb | ninguno
    string Codigo,
    Producto? Producto,            // cuando ya existe en el inventario
    ProductoExterno? Externo,      // cuando viene de UPCitemdb
    Sugerencia? Sugerencia,
    bool DesdeCache,
    DateTime? ConsultadoUtc,
    string? Aviso,
    string? AvisoTraduccion = null);

public class ErrorBusqueda(string mensaje, int estado) : Exception(mensaje)
{
    public int Estado { get; } = estado;
}

public class BuscadorCodigoBarras(HttpClient http, IConfiguration config, ILogger<BuscadorCodigoBarras> log, TipoCambioDof tipoCambio, Traductor traductor)
{
    private const string Fuente = "upcitemdb";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Normalizar(string? codigo) => new string((codigo ?? "").Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Los EAN-8, UPC-A (12), EAN-13 y GTIN-14 son numéricos de 8 a 14 dígitos.</summary>
    public static bool EsConsultable(string codigo) => codigo.Length is >= 8 and <= 14 && codigo.All(char.IsAsciiDigit);

    public async Task<ResultadoBusqueda> BuscarAsync(AppDbContext db, string codigoCrudo, CancellationToken ct)
    {
        var codigo = Normalizar(codigoCrudo);
        if (codigo.Length == 0) throw new ErrorBusqueda("Capturen o escaneen un código de barras.", 400);

        // 1) Inventario propio.
        var propio = await db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.CodigoBarras == codigo, ct);
        if (propio is not null)
            return new ResultadoBusqueda("inventario", codigo, propio, null, null, false, null, null);

        if (!EsConsultable(codigo))
            return new ResultadoBusqueda("ninguno", codigo, null, null, null, false, null,
                "No está en el inventario. Solo se consultan en internet códigos numéricos de 8 a 14 dígitos (EAN/UPC).");

        // 2) Caché local de consultas anteriores.
        var dias = config.GetValue("CodigoBarras:DiasCache", 30);
        var cache = await db.ConsultasCodigoBarras.FindAsync([codigo], ct);
        ProductoExterno? ext;
        bool desdeCache;
        if (cache is not null && cache.ConsultadoUtc > DateTime.UtcNow.AddDays(-dias))
        {
            ext = cache.Encontrado ? JsonSerializer.Deserialize<ProductoExterno>(cache.Respuesta, Json) : null;
            desdeCache = true;
        }
        else
        {
            ext = await ConsultarUpcItemDbAsync(codigo, ct);
            if (cache is null) { cache = new ConsultaCodigoBarras { Codigo = codigo }; db.ConsultasCodigoBarras.Add(cache); }
            cache.Fuente = Fuente;
            cache.Encontrado = ext is not null;
            cache.Respuesta = ext is null ? "" : JsonSerializer.Serialize(ext, Json);
            cache.ConsultadoUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            desdeCache = false;
        }

        if (ext is null)
            return new ResultadoBusqueda("ninguno", codigo, null, null, null, desdeCache, cache.ConsultadoUtc,
                "El código no está en el inventario ni en UPCitemdb. Capturen los datos a mano.");

        // Características en español: se traducen una vez y se guardan en la caché.
        string? avisoTrad = null;
        if (ext.CaracteristicasEs is null && ext.Caracteristicas.Length > 0)
        {
            try
            {
                ext = ext with { CaracteristicasEs = await traductor.TraducirAsync(ext.Caracteristicas, ct) };
                cache.Respuesta = JsonSerializer.Serialize(ext, Json);
                await db.SaveChangesAsync(ct);
            }
            catch (Traductor.ErrorTraduccion e) { avisoTrad = e.Message + " Las características se muestran en inglés."; }
        }

        await tipoCambio.AsegurarHoyAsync(db, ct);
        var sug = await SugerirAsync(db, ext, ct);
        return new ResultadoBusqueda(Fuente, codigo, null, ext, sug, desdeCache, cache.ConsultadoUtc, null, avisoTrad);
    }

    /// <summary>URLs de imagen que UPCitemdb dio para este código (desde la caché local).</summary>
    public static async Task<List<string>> ImagenesEnCacheAsync(AppDbContext db, string codigo, CancellationToken ct)
    {
        var c = await db.ConsultasCodigoBarras.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == codigo, ct);
        if (c is null || !c.Encontrado) return [];
        var e = JsonSerializer.Deserialize<ProductoExterno>(c.Respuesta, Json);
        if (e is null) return [];
        var lista = e.Imagenes is { Count: > 0 } ? e.Imagenes : (string.IsNullOrEmpty(e.Imagen) ? [] : [e.Imagen]);
        return lista;
    }

    private async Task<ProductoExterno?> ConsultarUpcItemDbAsync(string codigo, CancellationToken ct)
    {
        var url = config["CodigoBarras:UrlUpcItemDb"] ?? "https://api.upcitemdb.com/prod/trial/lookup";
        HttpResponseMessage r;
        try
        {
            r = await http.GetAsync($"{url}?upc={Uri.EscapeDataString(codigo)}", ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(e, "Falló la consulta a UPCitemdb");
            throw new ErrorBusqueda("No se pudo consultar UPCitemdb. Revisen la conexión a internet de la computadora principal.", 503);
        }

        if (r.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ErrorBusqueda("Se alcanzó el límite gratuito de consultas de UPCitemdb (100 por día). Intenten más tarde o capturen los datos a mano.", 429);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        var cuerpo = await r.Content.ReadAsStringAsync(ct);
        UpcRespuesta? data;
        try { data = JsonSerializer.Deserialize<UpcRespuesta>(cuerpo, Json); }
        catch (JsonException) { data = null; }

        if (r.StatusCode == HttpStatusCode.BadRequest && data?.Code == "INVALID_UPC") return null;
        if (!r.IsSuccessStatusCode || data is null)
        {
            log.LogWarning("UPCitemdb respondió {Estado}: {Cuerpo}", (int)r.StatusCode, cuerpo.Length > 300 ? cuerpo[..300] : cuerpo);
            throw new ErrorBusqueda($"UPCitemdb respondió con un error ({(int)r.StatusCode}). Intenten de nuevo o capturen los datos a mano.", 502);
        }

        var it = data.Items?.FirstOrDefault();
        if (it is null || string.IsNullOrWhiteSpace(it.Title)) return null;

        // Precio más bajo y más alto registrados; si no vienen, se toman de las ofertas.
        var ofertas = (it.Offers ?? []).Where(o => o.Price > 0).ToList();
        decimal? bajo = it.LowestRecordedPrice > 0 ? it.LowestRecordedPrice : ofertas.Count > 0 ? ofertas.Min(o => o.Price) : null;
        decimal? alto = it.HighestRecordedPrice > 0 ? it.HighestRecordedPrice : ofertas.Count > 0 ? ofertas.Max(o => o.Price) : null;
        var moneda = !string.IsNullOrWhiteSpace(it.Currency) ? it.Currency!
                   : ofertas.Select(o => o.Currency).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";
        // UPCitemdb no siempre informa la moneda; su catálogo es de tiendas de EE. UU.
        if (string.IsNullOrWhiteSpace(moneda)) moneda = "USD";

        var imagenes = (it.Images ?? []).Where(i => i.StartsWith("https://") || i.StartsWith("http://")).Distinct().Take(6).ToList();
        return new ProductoExterno(
            Recortar(it.Title, 120), Recortar(it.Brand, 60), Recortar(UltimaCategoria(it.Category), 60),
            Recortar(it.Description, 1500), imagenes.FirstOrDefault(i => i.StartsWith("https://")) ?? imagenes.FirstOrDefault() ?? "",
            bajo, alto, moneda.ToUpperInvariant(), ofertas.Count,
            Recortar(it.Model, 60), Recortar(it.Color, 60), Recortar(it.Size, 60), Recortar(it.Dimension, 80), Recortar(it.Weight, 60),
            imagenes);
    }

    /// <summary>
    /// Lista = precio más alto registrado × tipo de cambio; Objetivo = precio más bajo registrado × tipo de cambio.
    /// El mínimo usa el método proporcional, que requiere el precio en México (se calcula en el formulario).
    /// </summary>
    private static async Task<Sugerencia> SugerirAsync(AppDbContext db, ProductoExterno e, CancellationToken ct)
    {
        var notas = new List<string>();
        var cfg = await db.Configuracion.AsNoTracking().FirstAsync(ct);
        decimal? lista = null, objetivo = null, minimo = null;

        if (e.PrecioBajo is null && e.PrecioAlto is null)
            notas.Add("UPCitemdb no tiene precios registrados para este código; no se sugieren precio de lista ni objetivo.");
        else if (e.Moneda != "USD")
            notas.Add($"Los precios vienen en {e.Moneda}; solo se convierten precios en USD.");
        else if (cfg.TipoCambioUsd <= 0)
            notas.Add("Capturen el tipo de cambio en Ajustes para convertir los precios de USD a pesos.");
        else
        {
            var tc = cfg.TipoCambioUsd;
            if (e.PrecioAlto is { } a)
            {
                lista = Math.Round(a * tc, 0, MidpointRounding.AwayFromZero);
                notas.Add($"Lista: precio más alto registrado US${a:0.00} × {tc:0.####} = ${lista:N0}.");
            }
            if (e.PrecioBajo is { } b)
            {
                objetivo = Math.Round(b * tc, 0, MidpointRounding.AwayFromZero);
                notas.Add($"Objetivo: precio más bajo registrado US${b:0.00} × {tc:0.####} = ${objetivo:N0}.");
            }
        }

        notas.Add("Mínimo: se calcula con el método proporcional en cuanto captures el precio en México del juguete.");

        return new Sugerencia(lista, objetivo, minimo, notas);
    }

    private static string Recortar(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static string UltimaCategoria(string? c) => (c ?? "").Split('>').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0) ?? "";

    private sealed class UpcRespuesta
    {
        public string? Code { get; set; }
        public List<UpcItem>? Items { get; set; }
    }

    private sealed class UpcItem
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Brand { get; set; }
        public string? Category { get; set; }
        public string? Currency { get; set; }
        public string? Model { get; set; }
        public string? Color { get; set; }
        public string? Size { get; set; }
        public string? Dimension { get; set; }
        public string? Weight { get; set; }
        [JsonPropertyName("lowest_recorded_price"), JsonConverter(typeof(DecimalTolerante))] public decimal LowestRecordedPrice { get; set; }
        [JsonPropertyName("highest_recorded_price"), JsonConverter(typeof(DecimalTolerante))] public decimal HighestRecordedPrice { get; set; }
        public List<string>? Images { get; set; }
        public List<UpcOferta>? Offers { get; set; }
    }

    private sealed class UpcOferta
    {
        [JsonConverter(typeof(DecimalTolerante))] public decimal Price { get; set; }
        public string? Currency { get; set; }
    }

    /// <summary>Acepta número, texto numérico, texto vacío o null (se lee como 0).</summary>
    private sealed class DecimalTolerante : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
        {
            JsonTokenType.Number => r.GetDecimal(),
            JsonTokenType.String => decimal.TryParse(r.GetString(), System.Globalization.NumberStyles.Number,
                                        System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m,
            _ => 0m
        };
        public override void Write(Utf8JsonWriter w, decimal v, JsonSerializerOptions o) => w.WriteNumberValue(v);
    }
}
