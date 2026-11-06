namespace PalletControl.Servicios;

/// <summary>Guarda las imágenes de los juguetes como archivos en datos/imagenes.</summary>
public class AlmacenImagenes(string carpeta)
{
    public const long TamanoMaximo = 5 * 1024 * 1024;
    public const int MaximoPorJuguete = 12;
    public string Carpeta { get; } = carpeta;

    /// <summary>Detecta el formato por su contenido (no por la extensión). Null si no es una imagen admitida.</summary>
    public static string? Extension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ".png";
        if (b.Length >= 6 && (b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8))) return ".gif";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    /// <summary>Lee hasta el tamaño máximo; regresa null si se pasa.</summary>
    public static async Task<byte[]?> LeerLimitadoAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            if (ms.Length + n > TamanoMaximo) return null;
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    public async Task<string> GuardarAsync(string id, byte[] datos, string ext, CancellationToken ct)
    {
        Directory.CreateDirectory(Carpeta);
        var archivo = id + ext;
        await File.WriteAllBytesAsync(Path.Combine(Carpeta, archivo), datos, ct);
        return archivo;
    }

    public void Borrar(string archivo)
    {
        // Solo nombres simples generados por la app; nunca rutas.
        if (archivo.Length == 0 || archivo != Path.GetFileName(archivo)) return;
        var ruta = Path.Combine(Carpeta, archivo);
        if (File.Exists(ruta)) File.Delete(ruta);
    }
}
