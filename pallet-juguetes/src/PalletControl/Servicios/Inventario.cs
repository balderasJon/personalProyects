using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;

namespace PalletControl.Servicios;

public class ErrorNegocio(string mensaje, int estado = 400) : Exception(mensaje)
{
    public int Estado { get; } = estado;
}

/// <summary>Reglas compartidas de pallets, lotes y costeo.</summary>
public static class Inventario
{
    public static string NuevoId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Recalcula costo en MXN, gastos adicionales e inversión total del pallet.</summary>
    public static async Task RecalcularPalletAsync(AppDbContext db, Pallet p)
    {
        var gastos = await db.PalletGastos.Where(g => g.PalletId == p.Id).ToListAsync();
        // Incluye cambios aún no guardados de este mismo contexto.
        gastos = gastos.Where(g => db.Entry(g).State != EntityState.Deleted)
            .Concat(db.PalletGastos.Local.Where(g => g.PalletId == p.Id && db.Entry(g).State == EntityState.Added))
            .Distinct().ToList();
        if (p.Moneda == "MXN") { p.TipoCambio = 1m; p.TipoCambioFuente = ""; }
        p.CostoMxn = Dinero.AMxn(p.CostoOriginal, p.Moneda, p.TipoCambio);
        p.GastosAdicionales = Dinero.Centavos(gastos.Where(g => !g.IncluidoEnCosto).Sum(g => g.MontoMxn));
        p.InversionTotal = InversionPallet.Calcular(p.CostoMxn, gastos.Select(g => new GastoPallet(g.MontoMxn, g.IncluidoEnCosto)));
        p.ActualizadoUtc = DateTime.UtcNow;
    }

    /// <summary>La inversión global (Ajustes, Resumen) es la suma de los pallets.</summary>
    public static async Task SincronizarInversionGlobalAsync(AppDbContext db)
    {
        // Incluye los pallets agregados o borrados en esta misma operación (aún sin guardar).
        var pallets = (await db.Pallets.ToListAsync()).Concat(db.Pallets.Local).Distinct()
            .Where(p => db.Entry(p).State != EntityState.Deleted).ToList();
        var c = await db.Configuracion.OrderBy(x => x.Id).FirstAsync();
        if (pallets.Count > 0) c.Inversion = pallets.Sum(p => p.InversionTotal);
    }

    /// <summary>Cantidad total y estado anterior del producto, derivados de sus lotes.</summary>
    public static async Task SincronizarProductoAsync(AppDbContext db, string productoId)
    {
        var p = await db.Productos.FindAsync(productoId) ?? throw new ErrorNegocio("El juguete ya no existe.", 404);
        var lotes = await LotesDeAsync(db, productoId);
        p.Cantidad = lotes.Sum(l => l.Cantidad);
        var conPiezas = lotes.Where(l => l.Cantidad > 0).ToList();
        p.Estado = conPiezas.Count == 1 ? Catalogos.EstadoDesdeCondicion(conPiezas[0].Condicion)
                 : conPiezas.All(l => !l.Comercializable) && conPiezas.Count > 0 ? Catalogos.EstadoDesdeCondicion(conPiezas[0].Condicion)
                 : "nuevo";
        p.ActualizadoUtc = DateTime.UtcNow;
    }

    public static async Task<List<InventarioLote>> LotesDeAsync(AppDbContext db, string productoId)
    {
        var guardados = await db.Lotes.Where(l => l.ProductoId == productoId).ToListAsync();
        return guardados.Concat(db.Lotes.Local.Where(l => l.ProductoId == productoId)).Distinct()
            .Where(l => db.Entry(l).State != EntityState.Deleted).ToList();
    }

    public static async Task<int> VendidasAsync(AppDbContext db, string productoId) =>
        await db.Ventas.Where(v => v.ProductoId == productoId && v.Estado != "cancelado").SumAsync(v => (int?)v.Cantidad) ?? 0;

    /// <summary>Registra un movimiento y ajusta la cantidad del lote. Nunca deja cantidades negativas.</summary>
    public static void Mover(AppDbContext db, InventarioLote lote, int cantidad, string tipo, string motivo, string referencia = "")
    {
        if (cantidad == 0) return;
        if (lote.Cantidad + cantidad < 0) throw new ErrorNegocio($"El lote solo tiene {lote.Cantidad} pieza(s).");
        lote.Cantidad += cantidad;
        lote.ActualizadoUtc = DateTime.UtcNow;
        db.Movimientos.Add(new MovimientoInventario
        {
            LoteId = lote.Id, Tipo = tipo, Cantidad = cantidad, Motivo = Recortar(motivo, 200),
            Referencia = Recortar(referencia, 60), Usuario = db.UsuarioActual,
        });
    }

    /// <summary>Obtiene o crea el lote de (producto, pallet, condición).</summary>
    public static async Task<InventarioLote> LoteAsync(AppDbContext db, string productoId, string palletId, string condicion, DateOnly fechaIngreso)
    {
        if (!Catalogos.CondicionValida(condicion)) throw new ErrorNegocio("Condición no válida.");
        var lote = (await LotesDeAsync(db, productoId)).FirstOrDefault(l => l.PalletId == palletId && l.Condicion == condicion);
        if (lote is not null) return lote;
        if (await db.Pallets.FindAsync(palletId) is null) throw new ErrorNegocio("El pallet no existe.");
        lote = new InventarioLote
        {
            Id = NuevoId(), ProductoId = productoId, PalletId = palletId, Condicion = condicion,
            Comercializable = Catalogos.ComercializablePorDefecto(condicion), FechaIngreso = fechaIngreso,
        };
        db.Lotes.Add(lote);
        return lote;
    }

    /// <summary>Mueve piezas de un lote a otra condición (mismo producto y pallet).</summary>
    public static async Task<InventarioLote> ReclasificarAsync(AppDbContext db, InventarioLote origen, int cantidad, string condicion, string motivo)
    {
        if (cantidad <= 0) throw new ErrorNegocio("Indica cuántas piezas cambian de condición.");
        if (condicion == origen.Condicion) throw new ErrorNegocio("Elige una condición distinta a la actual.");
        if (cantidad > origen.Cantidad) throw new ErrorNegocio($"El lote solo tiene {origen.Cantidad} pieza(s).");
        var destino = await LoteAsync(db, origen.ProductoId, origen.PalletId, condicion, origen.FechaIngreso);
        var refe = $"{origen.Id}→{destino.Id}";
        Mover(db, origen, -cantidad, "reclasificacion_salida", motivo, refe);
        Mover(db, destino, cantidad, "reclasificacion_entrada", motivo, refe);
        return destino;
    }

    // ---------- Costeo ----------

    public record EntradaCosteo(Pallet Pallet, List<(InventarioLote Lote, Producto Producto, decimal? ValorRelativo)> Lotes, string Huella);

    /// <summary>Datos de entrada del costeo de un pallet y su huella (para detectar costeos desactualizados).</summary>
    public static async Task<EntradaCosteo> EntradaAsync(AppDbContext db, string palletId)
    {
        var p = await db.Pallets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == palletId) ?? throw new ErrorNegocio("El pallet no existe.", 404);
        var lotes = await db.Lotes.AsNoTracking().Where(l => l.PalletId == palletId).ToListAsync();
        var ids = lotes.Select(l => l.ProductoId).Distinct().ToList();
        var productos = await db.Productos.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var filas = lotes.OrderBy(l => l.Id, StringComparer.Ordinal)
            .Select(l => (l, productos[l.ProductoId], l.ValorRelativoUnitario ?? (productos[l.ProductoId].PrecioMexico > 0 ? productos[l.ProductoId].PrecioMexico : (decimal?)null)))
            .ToList();
        var sb = new StringBuilder($"{p.InversionTotal}|");
        foreach (var (l, _, v) in filas) sb.Append($"{l.Id}:{l.Cantidad}:{l.Comercializable}:{v};");
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..32];
        return new EntradaCosteo(p, filas, huella);
    }

    public static MetodoCosteo Metodo(string m) => m switch
    {
        "equitativa" => MetodoCosteo.Equitativa,
        "manual" => MetodoCosteo.Manual,
        "proporcional" => MetodoCosteo.Proporcional,
        _ => throw new ErrorNegocio("Método de costeo no válido."),
    };

    public static ModoMerma Modo(string m) => m switch
    {
        "absorber" => ModoMerma.Absorber,
        "perdida" => ModoMerma.Perdida,
        _ => throw new ErrorNegocio("Modo de merma no válido."),
    };

    private static string Recortar(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
}
