using Microsoft.EntityFrameworkCore;
using PalletControl.Models;

namespace PalletControl.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<Semana> Semanas => Set<Semana>();
    public DbSet<Configuracion> Configuracion => Set<Configuracion>();
    public DbSet<VersionDatos> Version => Set<VersionDatos>();
    public DbSet<ConsultaCodigoBarras> ConsultasCodigoBarras => Set<ConsultaCodigoBarras>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // SQLite no guarda la zona horaria: todas las marcas de tiempo se tratan como UTC.
        c.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    private sealed class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Producto>(e =>
        {
            e.ToTable("productos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired().UseCollation("NOCASE");
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.CodigoBarras).HasMaxLength(20).HasDefaultValue("");
            e.HasIndex(x => x.CodigoBarras).IsUnique().HasFilter("\"CodigoBarras\" <> ''").HasDatabaseName("IX_productos_CodigoBarras");
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.Categoria).HasMaxLength(60);
            e.Property(x => x.Clase).HasMaxLength(1);
            e.Property(x => x.Estado).HasMaxLength(12);
            e.Property(x => x.Notas).HasMaxLength(500);
        });
        b.Entity<Venta>(e =>
        {
            e.ToTable("ventas");
            e.HasKey(x => x.Id);
            e.HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProductoId);
            e.Property(x => x.Estado).HasMaxLength(12);
            e.Property(x => x.Canal).HasMaxLength(40);
            e.Property(x => x.Responsable).HasMaxLength(40);
            e.Property(x => x.Cliente).HasMaxLength(80);
            e.Property(x => x.Notas).HasMaxLength(400);
        });
        b.Entity<Gasto>(e =>
        {
            e.ToTable("gastos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(40);
            e.Property(x => x.Persona).HasMaxLength(40);
            e.Property(x => x.Concepto).HasMaxLength(120);
        });
        b.Entity<Semana>(e =>
        {
            e.ToTable("semanas");
            e.HasKey(x => x.Inicio);
            e.Property(x => x.Notas).HasMaxLength(600);
        });
        b.Entity<Configuracion>(e =>
        {
            e.ToTable("configuracion");
            e.HasKey(x => x.Id);
            e.Property(x => x.SocioA).HasMaxLength(40);
            e.Property(x => x.SocioB).HasMaxLength(40);
        });
        b.Entity<ConsultaCodigoBarras>(e =>
        {
            e.ToTable("consultas_codigo_barras");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.Property(x => x.Fuente).HasMaxLength(30);
        });
        b.Entity<VersionDatos>(e => { e.ToTable("version_datos"); e.HasKey(x => x.Id); });
    }

    /// <summary>Guarda cambios e incrementa la versión global en la misma transacción.</summary>
    public async Task GuardarAsync()
    {
        var v = await Version.FindAsync(1);
        if (v is null) { v = new VersionDatos { Id = 1 }; Version.Add(v); }
        v.Valor++;
        await SaveChangesAsync();
    }
}
