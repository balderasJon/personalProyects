namespace PalletControl.Endpoints;

public record ProductoIn(string? Codigo, string? Nombre, string? Categoria, string? Clase, string? Estado,
    int Cantidad, decimal PrecioLista, decimal PrecioObjetivo, decimal PrecioMinimo, string? Notas);

public record VentaIn(string? ProductoId, int Cantidad, decimal Precio, string? Estado, DateOnly? Fecha,
    string? Canal, string? Responsable, string? Cliente, string? Notas);

public record CambioEstadoIn(string? Estado);

public record GastoIn(string? Tipo, decimal Monto, DateOnly? Fecha, string? Persona, string? Concepto);

public record SemanaIn(int Publicadas, int Mensajes, decimal HorasA, decimal HorasB, string? Notas);

public record ConfiguracionIn(decimal Inversion, string? SocioA, string? SocioB, int RepartoA, int StockBajo, decimal MetaMensual);

public record Error(string Mensaje);
