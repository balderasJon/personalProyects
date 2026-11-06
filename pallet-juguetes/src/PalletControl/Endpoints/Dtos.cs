namespace PalletControl.Endpoints;

public record ProductoIn(string? Codigo, string? CodigoBarras, string? Nombre, string? Categoria, string? Clase, string? Estado,
    int Cantidad, decimal PrecioLista, decimal PrecioObjetivo, decimal PrecioMinimo, string? Notas,
    decimal PrecioMexico = 0, string? Caracteristicas = null,
    decimal PrecioLiverpool = 0, decimal PrecioMercadoLibre = 0, decimal PrecioWalmart = 0);

public record VentaIn(string? ProductoId, int Cantidad, decimal Precio, string? Estado, DateOnly? Fecha,
    string? Canal, string? Cliente, string? Notas);

public record CambioEstadoIn(string? Estado);

public record GastoIn(string? Tipo, decimal Monto, DateOnly? Fecha, string? Concepto);

public record SemanaIn(int Publicadas, int Mensajes, decimal Horas, string? Notas);

public record ConfiguracionIn(decimal Inversion, int StockBajo, decimal MetaMensual, int DescuentoSugerido = 35);

public record Error(string Mensaje);

public record TipoCambioIn(decimal Valor);

public record TraducirIn(string? Texto);
