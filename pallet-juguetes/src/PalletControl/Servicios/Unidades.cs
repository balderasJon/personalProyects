using System.Globalization;
using System.Text.RegularExpressions;

namespace PalletControl.Servicios;

/// <summary>Convierte pesos y medidas en texto (como los da UPCitemdb) a kilogramos y centímetros.</summary>
public static partial class Unidades
{
    public static decimal? PesoKg(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var m = Valor().Match(texto);
        if (!m.Success) return null;
        var v = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var u = texto.ToLowerInvariant();
        decimal kg = u.Contains("lb") || u.Contains("pound") ? v * 0.45359237m
                   : u.Contains("oz") || u.Contains("ounce") ? v * 0.028349523125m
                   : u.Contains("kg") ? v
                   : u.Contains("g") ? v / 1000m
                   : -1m;
        return kg < 0 ? null : Math.Round(kg, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>"15.0 X 3.5 X 10.6 inches" → (38.1, 8.89, 26.92) en cm.</summary>
    public static (decimal? Largo, decimal? Ancho, decimal? Alto) MedidasCm(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return (null, null, null);
        var nums = Valor().Matches(texto).Select(x => decimal.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        if (nums.Count != 3) return (null, null, null);
        var u = texto.ToLowerInvariant();
        decimal f = u.Contains("inch") || u.Contains(" in") || u.Contains("pulg") ? 2.54m : u.Contains("mm") ? 0.1m : u.Contains("cm") ? 1m : -1m;
        if (f < 0) return (null, null, null);
        decimal R(decimal x) => Math.Round(x * f, 2, MidpointRounding.AwayFromZero);
        return (R(nums[0]), R(nums[1]), R(nums[2]));
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)")] private static partial Regex Valor();
}
