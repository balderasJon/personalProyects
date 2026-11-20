using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

public record BorrarTodoIn(string? Confirmacion);

/// <summary>
/// «Borrar todos los datos y empezar de cero» (Ajustes). Pedido del usuario el 2026-11-20 para quitar sus datos de prueba.
/// Antes de borrar guarda una copia completa de la base y mueve las fotos a datos/respaldos, para poder recuperarlas.
/// Borra: pallets, juguetes, lotes y movimientos, costeos, fotos, ventas y sus detalles, gastos, semanas, publicaciones,
/// competencia, precios por juguete, objetivos de un juguete, integraciones y la bitácora.
/// Conserva la configuración: Ajustes, plataformas, objetivos generales y por categoría, situación fiscal, tarifas,
/// usuarios y la caché de códigos de barras (para no volver a gastar consultas de UPCitemdb).
/// </summary>
public static class BorradoApi
{
    public const string Palabra = "BORRAR";

    public static void MapBorrado(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapPost("/datos/borrar-todo", (BorrarTodoIn i, AppDbContext db, AlmacenImagenes alm, HttpContext ctx) => Api.Bloqueado(async () =>
        {
            if (!string.Equals((i.Confirmacion ?? "").Trim(), Palabra, StringComparison.Ordinal))
                return Results.BadRequest(new Error($"Para confirmar escribe {Palabra} en mayúsculas."));

            var sello = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            var dirDatos = Path.GetDirectoryName(Path.GetFullPath(db.Database.GetDbConnection().DataSource))!;
            var dirResp = Path.Combine(dirDatos, "respaldos");
            Directory.CreateDirectory(dirResp);
            var copia = Path.Combine(dirResp, $"pallet-antes-de-borrar-{sello}.db");
            await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", copia);

            string T<TE>() => db.Model.FindEntityType(typeof(TE))!.GetTableName()!;
            // Hijos primero; las tablas de configuración no se tocan.
            var tablas = new[]
            {
                T<SincronizacionPropuesta>(), T<SincronizacionLote>(), T<IntegracionEvento>(), T<IntegracionCuenta>(),
                T<VentaIncidencia>(), T<VentaGasto>(), T<VentaDetalle>(), T<Venta>(),
                T<Publicacion>(), T<Competencia>(), T<PrecioProducto>(),
                T<CostoLote>(), T<CosteoCorrida>(), T<MovimientoInventario>(), T<InventarioLote>(),
                T<ProductoImagen>(), T<PalletGasto>(), T<Gasto>(), T<Semana>(),
            };
            var borrados = 0;
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                foreach (var t in tablas) borrados += await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{t}\"");
                // Objetivos de rentabilidad de un juguete concreto (los generales y por categoría se conservan).
                borrados += await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{T<ParametroRentabilidad>()}\" WHERE \"ProductoId\" IS NOT NULL");
                borrados += await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{T<Producto>()}\"");
                borrados += await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{T<Pallet>()}\"");
                await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{T<Auditoria>()}\"");
                await tx.CommitAsync();
            }

            // Fotos: se mueven a la carpeta de respaldos (no se pierden si te arrepientes).
            var fotos = 0;
            if (Directory.Exists(alm.Carpeta) && Directory.EnumerateFiles(alm.Carpeta).Any())
            {
                var destino = Path.Combine(dirResp, $"imagenes-antes-de-borrar-{sello}");
                Directory.CreateDirectory(destino);
                foreach (var f in Directory.EnumerateFiles(alm.Carpeta)) { File.Move(f, Path.Combine(destino, Path.GetFileName(f))); fotos++; }
            }

            db.ChangeTracker.Clear();
            db.Auditoria.Add(new Auditoria
            {
                Usuario = ctx.User.FindFirstValue(ClaimTypes.Name) ?? "sistema", Entidad = "datos", EntidadId = "todos", Accion = "baja",
                Cambios = $"{{\"borradoTotal\":[\"{borrados} registros y {fotos} fotos\",\"respaldo: {Path.GetFileName(copia)}\"]}}",
            });
            await db.GuardarAsync();
            return Results.Ok(new { registros = borrados, fotos, respaldo = copia });
        }));
    }
}
