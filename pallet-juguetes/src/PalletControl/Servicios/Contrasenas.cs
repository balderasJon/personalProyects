using System.Security.Cryptography;
using System.Text;

namespace PalletControl.Servicios;

/// <summary>Hash de contraseñas con PBKDF2-SHA256 (sal aleatoria de 16 bytes, comparación en tiempo constante).</summary>
public static class Contrasenas
{
    public const int Iteraciones = 600_000;   // recomendación OWASP para PBKDF2-SHA256
    public const int LongitudMinima = 8;

    public static (string Hash, string Sal, int Iteraciones) Crear(string contrasena)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        return (Convert.ToBase64String(Derivar(contrasena, sal, Iteraciones)), Convert.ToBase64String(sal), Iteraciones);
    }

    public static bool Verificar(string contrasena, string hash, string sal, int iteraciones)
    {
        try
        {
            var esperado = Convert.FromBase64String(hash);
            var calculado = Derivar(contrasena, Convert.FromBase64String(sal), iteraciones);
            return CryptographicOperations.FixedTimeEquals(esperado, calculado);
        }
        catch (FormatException) { return false; }
    }

    public static string? Validar(string? contrasena) =>
        string.IsNullOrEmpty(contrasena) || contrasena.Length < LongitudMinima
            ? $"La contraseña debe tener al menos {LongitudMinima} caracteres." : null;

    /// <summary>Sello de sesión: cambia cuando cambia la contraseña, e invalida las sesiones abiertas.</summary>
    public static string Sello(string hash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)))[..16];

    private static byte[] Derivar(string contrasena, byte[] sal, int iteraciones) =>
        Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA256, 32);
}
