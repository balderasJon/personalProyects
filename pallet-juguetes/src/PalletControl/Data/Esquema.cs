using Microsoft.EntityFrameworkCore;

namespace PalletControl.Data;

/// <summary>
/// EnsureCreated solo crea la base cuando no existe. Para bases creadas con una versión anterior,
/// aquí se agregan las columnas y tablas nuevas sin perder datos.
/// </summary>
public static class Esquema
{
    public static void Actualizar(AppDbContext db)
    {
        AgregarColumna(db, "productos", "CodigoBarras", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "configuracion", "TipoCambioUsd", "TEXT NOT NULL DEFAULT '0.0'");
        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_productos_CodigoBarras"
            ON "productos" ("CodigoBarras") WHERE "CodigoBarras" <> '';
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "consultas_codigo_barras" (
                "Codigo" TEXT NOT NULL CONSTRAINT "PK_consultas_codigo_barras" PRIMARY KEY,
                "Fuente" TEXT NOT NULL,
                "Encontrado" INTEGER NOT NULL,
                "Respuesta" TEXT NOT NULL,
                "ConsultadoUtc" TEXT NOT NULL);
            """);
    }

    private static void AgregarColumna(AppDbContext db, string tabla, string columna, string definicion)
    {
        var existe = db.Database
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('{tabla}') WHERE name = '{columna}'")
            .AsEnumerable().First() > 0;
        if (!existe)
            db.Database.ExecuteSqlRaw($"ALTER TABLE \"{tabla}\" ADD COLUMN \"{columna}\" {definicion}");
    }
}
