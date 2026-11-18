namespace PalletControl.Endpoints;

public record ProductoIn(string? Codigo, string? CodigoBarras, string? Nombre, string? Categoria, string? Clase, string? Estado,
    int Cantidad, decimal PrecioLista, decimal PrecioObjetivo, decimal PrecioMinimo, string? Notas,
    decimal PrecioMexico = 0, string? Caracteristicas = null,
    decimal PrecioLiverpool = 0, decimal PrecioMercadoLibre = 0, decimal PrecioWalmart = 0,
    // Fase 1. En los campos de texto y medidas, null = conservar el valor actual.
    string? PalletId = null, string? Condicion = null,
    string? Marca = null, string? Modelo = null, string? Subcategoria = null, string? DescripcionComercial = null,
    string? AccesoriosIncluidos = null, string? EdadRecomendada = null,
    decimal? PesoKg = null, decimal? LargoCm = null, decimal? AnchoCm = null, decimal? AltoCm = null,
    string? EstadoComercial = null, DateOnly? FechaIngreso = null);

public record GastoIn(string? Tipo, decimal Monto, DateOnly? Fecha, string? Concepto);

public record SemanaIn(int Publicadas, int Mensajes, decimal Horas, string? Notas);

public record ConfiguracionIn(decimal Inversion, int StockBajo, decimal MetaMensual, int DescuentoSugerido = 35);

public record Error(string Mensaje);

public record TipoCambioIn(decimal Valor);

public record TraducirIn(string? Texto);
