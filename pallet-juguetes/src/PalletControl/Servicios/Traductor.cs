using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PalletControl.Servicios;

/// <summary>
/// Traduce del inglés al español con la API gratuita de MyMemory (sin registro; límite diario por IP).
/// Además convierte libras, onzas y pulgadas a unidades métricas.
/// </summary>
public partial class Traductor(HttpClient http, IConfiguration config, ILogger<Traductor> log)
{
    private const int MaxSegmento = 450;   // MyMemory acepta hasta 500 bytes por consulta

    public class ErrorTraduccion(string m) : Exception(m);

    public async Task<string> TraducirAsync(string texto, CancellationToken ct)
    {
        texto = (texto ?? "").Trim();
        if (texto.Length == 0) return "";
        var url = config["Traduccion:UrlMyMemory"] ?? "https://api.mymemory.translated.net/get";
        var salida = new List<string>();
        foreach (var linea in texto.Split('\n'))
        {
            var (etiqueta, contenido) = SepararEtiqueta(linea.Trim());
            if (contenido.Length == 0) { salida.Add(etiqueta); continue; }
            var partes = new List<string>();
            foreach (var seg in Segmentos(Metrico(contenido)))
                partes.Add(SinLetras(seg) ? seg : await TraducirSegmentoAsync(url, seg, ct));
            salida.Add(etiqueta + string.Join(" ", partes));
        }
        return string.Join("\n", salida).Trim();
    }

    private async Task<string> TraducirSegmentoAsync(string url, string seg, CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync($"{url}?q={Uri.EscapeDataString(seg)}&langpair=en|es", ct);
            var cuerpo = await r.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(cuerpo);
            var raiz = doc.RootElement;
            var estado = raiz.TryGetProperty("responseStatus", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : (int)r.StatusCode;
            if (raiz.TryGetProperty("quotaFinished", out var q) && q.ValueKind == JsonValueKind.True || estado == 429)
                throw new ErrorTraduccion("Se agotó el límite diario gratuito de traducción de MyMemory. Intenta mañana o traduce a mano.");
            if (estado != 200) throw new ErrorTraduccion($"El servicio de traducción respondió con un error ({estado}).");
            var t = raiz.GetProperty("responseData").GetProperty("translatedText").GetString() ?? seg;
            return System.Net.WebUtility.HtmlDecode(t);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            log.LogWarning(e, "Falló la traducción");
            throw new ErrorTraduccion("No se pudo traducir: revisa la conexión a internet.");
        }
    }

    // "Marca: Hot Wheels" → no se traduce la etiqueta (ya está en español) y las marcas/modelos se dejan igual.
    private static readonly string[] SinTraducir = ["Marca:", "Modelo:", "Dimensiones:", "Peso:"];
    private static readonly string[] Etiquetas = ["Marca:", "Modelo:", "Color:", "Tamaño:", "Dimensiones:", "Peso:"];
    private static (string, string) SepararEtiqueta(string linea)
    {
        foreach (var e in Etiquetas)
            if (linea.StartsWith(e, StringComparison.OrdinalIgnoreCase))
            {
                var resto = linea[e.Length..].Trim();
                return SinTraducir.Contains(e) ? (e + " " + Metrico(resto), "") : (e + " ", resto);
            }
        return ("", linea);
    }

    private static bool SinLetras(string s) => !s.Any(char.IsLetter) || s.Length < 2;

    private static IEnumerable<string> Segmentos(string s)
    {
        if (s.Length <= MaxSegmento) { yield return s; yield break; }
        var sb = new StringBuilder();
        foreach (var frase in Frases().Split(s))
        {
            if (sb.Length + frase.Length + 1 > MaxSegmento && sb.Length > 0) { yield return sb.ToString().Trim(); sb.Clear(); }
            if (frase.Length > MaxSegmento)
                for (var i = 0; i < frase.Length; i += MaxSegmento) yield return frase.Substring(i, Math.Min(MaxSegmento, frase.Length - i));
            else sb.Append(frase).Append(' ');
        }
        if (sb.Length > 0) yield return sb.ToString().Trim();
    }

    /// <summary>1.9 Pounds → 1.9 lb (0.86 kg); 15 X 3.5 X 10.6 inches → … pulg (38.1 × 8.9 × 26.9 cm).</summary>
    public static string Metrico(string s)
    {
        s = Libras().Replace(s, m => { var v = Num(m.Groups[1].Value); return $"{m.Groups[1].Value} lb ({v * 0.45359237m:0.##} kg)"; });
        s = Onzas().Replace(s, m => { var v = Num(m.Groups[1].Value); return $"{m.Groups[1].Value} oz ({v * 28.349523m:0} g)"; });
        s = Pulgadas().Replace(s, m =>
        {
            var nums = NumeroRe().Matches(m.Groups[1].Value).Select(x => Num(x.Value) * 2.54m).Select(x => x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            return $"{m.Groups[1].Value.Trim()} pulg ({string.Join(" × ", nums)} cm)";
        });
        return s;
    }
    private static decimal Num(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<=[.!?;])\s+")] private static partial Regex Frases();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:pounds|pound|lbs|lb)\b", RegexOptions.IgnoreCase)] private static partial Regex Libras();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*(?:ounces|ounce|oz)\b", RegexOptions.IgnoreCase)] private static partial Regex Onzas();
    [GeneratedRegex(@"((?:\d+(?:\.\d+)?\s*[xX×]\s*)*\d+(?:\.\d+)?)\s*(?:inches|inch|in)\b", RegexOptions.IgnoreCase)] private static partial Regex Pulgadas();
    [GeneratedRegex(@"\d+(?:\.\d+)?")] private static partial Regex NumeroRe();
}
