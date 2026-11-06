using Microsoft.EntityFrameworkCore;

namespace PalletControl.Data;

/// <summary>
/// EnsureCreated solo crea la base cuando no existe. Para bases creadas con una versión anterior,
/// aquí se agregan las columnas y tablas nuevas sin perder datos.
/// </summary>
public static class Esquema
{
    /// <summary>Columnas de la versión para dos socios que ya no existen en el modelo.</summary>
    private static readonly (string Tabla, string Columna)[] ColumnasRetiradas =
    [
        ("configuracion", "SocioA"), ("configuracion", "SocioB"), ("configuracion", "RepartoA"),
        ("ventas", "Responsable"), ("gastos", "Persona"),
        ("semanas", "HorasA"), ("semanas", "HorasB"),
    ];

    public static void Actualizar(AppDbContext db, string dirRespaldos)
    {
        // Antes de quitar columnas se guarda una copia completa de la base.
        if (ColumnasRetiradas.Any(c => Existe(db, c.Tabla, c.Columna)))
        {
            Directory.CreateDirectory(dirRespaldos);
            var copia = Path.Combine(dirRespaldos, $"pallet-antes-de-un-socio-{DateTime.Now:yyyy-MM-dd-HHmmss}.db");
            db.Database.ExecuteSqlRaw("VACUUM INTO {0}", copia);
        }

        // Las horas de los dos socios se suman en un solo campo.
        if (Existe(db, "semanas", "HorasA") && !Existe(db, "semanas", "Horas"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE \"semanas\" ADD COLUMN \"Horas\" TEXT NOT NULL DEFAULT '0.0'");
            db.Database.ExecuteSqlRaw("""
                UPDATE "semanas" SET "Horas" =
                    CAST(COALESCE(CAST("HorasA" AS REAL), 0) + COALESCE(CAST("HorasB" AS REAL), 0) AS TEXT);
                """);
        }
        foreach (var (tabla, columna) in ColumnasRetiradas)
            if (Existe(db, tabla, columna))
                db.Database.ExecuteSqlRaw($"ALTER TABLE \"{tabla}\" DROP COLUMN \"{columna}\"");

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

    private static bool Existe(AppDbContext db, string tabla, string columna) => db.Database
        .SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info('{tabla}') WHERE name = '{columna}'")
        .AsEnumerable().First() > 0;

    private static void AgregarColumna(AppDbContext db, string tabla, string columna, string definicion)
    {
        if (!Existe(db, tabla, columna))
            db.Database.ExecuteSqlRaw($"ALTER TABLE \"{tabla}\" ADD COLUMN \"{columna}\" {definicion}");
    }
}
