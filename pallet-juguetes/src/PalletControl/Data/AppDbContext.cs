using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PalletControl.Models;

namespace PalletControl.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, IUsuarioActual? usuario = null) : DbContext(options)
{
    public string UsuarioActual => usuario?.Nombre ?? "sistema";
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<Semana> Semanas => Set<Semana>();
    public DbSet<Configuracion> Configuracion => Set<Configuracion>();
    public DbSet<VersionDatos> Version => Set<VersionDatos>();
    public DbSet<ConsultaCodigoBarras> ConsultasCodigoBarras => Set<ConsultaCodigoBarras>();
    public DbSet<ProductoImagen> Imagenes => Set<ProductoImagen>();
    public DbSet<Pallet> Pallets => Set<Pallet>();
    public DbSet<PalletGasto> PalletGastos => Set<PalletGasto>();
    public DbSet<InventarioLote> Lotes => Set<InventarioLote>();
    public DbSet<MovimientoInventario> Movimientos => Set<MovimientoInventario>();
    public DbSet<CosteoCorrida> CosteoCorridas => Set<CosteoCorrida>();
    public DbSet<CostoLote> CostosLote => Set<CostoLote>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Auditoria> Auditoria => Set<Auditoria>();
    public DbSet<Plataforma> Plataformas => Set<Plataforma>();
    public DbSet<TarifaPlataforma> Tarifas => Set<TarifaPlataforma>();
    public DbSet<ParametroRentabilidad> Parametros => Set<ParametroRentabilidad>();
    public DbSet<ConfiguracionFiscal> Fiscal => Set<ConfiguracionFiscal>();
    public DbSet<PrecioProducto> PreciosProducto => Set<PrecioProducto>();

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
            e.Property(x => x.Caracteristicas).HasMaxLength(2000);
            e.Property(x => x.Marca).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.Modelo).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.Subcategoria).HasMaxLength(60).HasDefaultValue("");
            e.Property(x => x.DescripcionComercial).HasMaxLength(2000).HasDefaultValue("");
            e.Property(x => x.AccesoriosIncluidos).HasMaxLength(300).HasDefaultValue("");
            e.Property(x => x.EdadRecomendada).HasMaxLength(40).HasDefaultValue("");
            e.Property(x => x.EstadoComercial).HasMaxLength(20).HasDefaultValue("en_revision");
        });
        b.Entity<Pallet>(e =>
        {
            e.ToTable("pallets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Nombre).IsUnique();
            e.Property(x => x.Proveedor).HasMaxLength(80);
            e.Property(x => x.PaisOrigen).HasMaxLength(40);
            e.Property(x => x.Moneda).HasMaxLength(3);
            e.Property(x => x.TipoCambioFuente).HasMaxLength(10);
            e.Property(x => x.Estatus).HasMaxLength(12);
            e.Property(x => x.MetodoCosteo).HasMaxLength(12);
            e.Property(x => x.ModoMerma).HasMaxLength(10);
            e.Property(x => x.Notas).HasMaxLength(500);
        });
        b.Entity<PalletGasto>(e =>
        {
            e.ToTable("pallet_gastos");
            e.HasKey(x => x.Id);
            e.HasOne<Pallet>().WithMany().HasForeignKey(x => x.PalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PalletId);
            e.Property(x => x.Tipo).HasMaxLength(40);
            e.Property(x => x.Concepto).HasMaxLength(120);
        });
        b.Entity<InventarioLote>(e =>
        {
            e.ToTable("inventario_lotes");
            e.HasKey(x => x.Id);
            e.HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Pallet>().WithMany().HasForeignKey(x => x.PalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProductoId, x.PalletId, x.Condicion }).IsUnique();
            e.HasIndex(x => x.PalletId);
            e.Property(x => x.Condicion).HasMaxLength(20);
            e.Property(x => x.Tratamiento).HasMaxLength(20);
            e.Property(x => x.Notas).HasMaxLength(300);
            e.ToTable(t => t.HasCheckConstraint("CK_lotes_cantidad", "\"Cantidad\" >= 0"));
        });
        b.Entity<MovimientoInventario>(e =>
        {
            e.ToTable("movimientos_inventario");
            e.HasKey(x => x.Id);
            e.HasOne<InventarioLote>().WithMany().HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.LoteId);
            e.Property(x => x.Tipo).HasMaxLength(30);
            e.Property(x => x.Referencia).HasMaxLength(60);
            e.Property(x => x.Motivo).HasMaxLength(200);
            e.Property(x => x.Usuario).HasMaxLength(40);
        });
        b.Entity<CosteoCorrida>(e =>
        {
            e.ToTable("costeo_corridas");
            e.HasKey(x => x.Id);
            e.HasOne<Pallet>().WithMany().HasForeignKey(x => x.PalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PalletId, x.Vigente });
            e.Property(x => x.Metodo).HasMaxLength(12);
            e.Property(x => x.ModoMerma).HasMaxLength(10);
            e.Property(x => x.Usuario).HasMaxLength(40);
            e.Property(x => x.Huella).HasMaxLength(64);
            e.Property(x => x.Notas).HasMaxLength(300);
        });
        b.Entity<CostoLote>(e =>
        {
            e.ToTable("costos_lote");
            e.HasKey(x => x.Id);
            e.HasOne<CosteoCorrida>().WithMany().HasForeignKey(x => x.CorridaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CorridaId);
            e.HasIndex(x => x.LoteId);
            e.Property(x => x.Condicion).HasMaxLength(20);
        });
        b.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(40).IsRequired().UseCollation("NOCASE");
            e.HasIndex(x => x.Nombre).IsUnique();
            e.Property(x => x.NombreCompleto).HasMaxLength(80);
        });
        b.Entity<Auditoria>(e =>
        {
            e.ToTable("auditoria");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Entidad, x.EntidadId });
            e.HasIndex(x => x.FechaUtc);
            e.Property(x => x.Usuario).HasMaxLength(40);
            e.Property(x => x.Entidad).HasMaxLength(40);
            e.Property(x => x.EntidadId).HasMaxLength(40);
            e.Property(x => x.Accion).HasMaxLength(10);
        });
        b.Entity<ProductoImagen>(e =>
        {
            e.ToTable("producto_imagenes");
            e.HasKey(x => x.Id);
            e.HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ProductoId);
            e.Property(x => x.Archivo).HasMaxLength(80);
            e.Property(x => x.Origen).HasMaxLength(20);
        });
        b.Entity<Venta>(e =>
        {
            e.ToTable("ventas");
            e.HasKey(x => x.Id);
            e.HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProductoId);
            e.Property(x => x.Estado).HasMaxLength(12);
            e.Property(x => x.Canal).HasMaxLength(40);
            e.Property(x => x.Cliente).HasMaxLength(80);
            e.Property(x => x.Notas).HasMaxLength(400);
        });
        b.Entity<Gasto>(e =>
        {
            e.ToTable("gastos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(40);
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
            e.Property(x => x.TipoCambioFuente).HasMaxLength(20);
        });
        b.Entity<ConsultaCodigoBarras>(e =>
        {
            e.ToTable("consultas_codigo_barras");
            e.HasKey(x => x.Codigo);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.Property(x => x.Fuente).HasMaxLength(30);
        });
        // ---- Fase 2 ----
        b.Entity<Plataforma>(e =>
        {
            e.ToTable("plataformas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Nombre).HasMaxLength(60).IsRequired().UseCollation("NOCASE");
            e.HasIndex(x => x.Nombre).IsUnique();
            e.Property(x => x.Tipo).HasMaxLength(12);
            e.Property(x => x.Notas).HasMaxLength(300);
        });
        b.Entity<TarifaPlataforma>(e =>
        {
            e.ToTable("tarifas_plataforma");
            e.HasKey(x => x.Id);
            e.HasOne<Plataforma>().WithMany().HasForeignKey(x => x.PlataformaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PlataformaId, x.VigenteDesde });
            e.Property(x => x.Nombre).HasMaxLength(80);
            e.Property(x => x.TipoPublicacion).HasMaxLength(40);
            e.Property(x => x.Modalidad).HasMaxLength(40);
            e.Property(x => x.Categoria).HasMaxLength(60);
            e.Property(x => x.MedioPago).HasMaxLength(40);
            e.Property(x => x.Fuente).HasMaxLength(30);
            e.Property(x => x.Notas).HasMaxLength(500);
        });
        b.Entity<ParametroRentabilidad>(e =>
        {
            e.ToTable("parametros_rentabilidad");
            e.HasKey(x => x.Id);
            e.HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Plataforma>().WithMany().HasForeignKey(x => x.PlataformaId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Categoria).HasMaxLength(60);
            e.Property(x => x.Esquema).HasMaxLength(14);
            e.Property(x => x.Redondeo).HasMaxLength(14);
            e.Property(x => x.Notas).HasMaxLength(300);
        });
        b.Entity<ConfiguracionFiscal>(e =>
        {
            e.ToTable("configuracion_fiscal");
            e.HasKey(x => x.Id);
            e.Property(x => x.Regimen).HasMaxLength(30);
            e.Property(x => x.Notas).HasMaxLength(500);
        });
        b.Entity<PrecioProducto>(e =>
        {
            e.ToTable("precios_producto");
            e.HasKey(x => x.Id);
            e.HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Plataforma>().WithMany().HasForeignKey(x => x.PlataformaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProductoId, x.PlataformaId, x.Vigente });
            e.Property(x => x.Usuario).HasMaxLength(40);
        });
        b.Entity<VersionDatos>(e => { e.ToTable("version_datos"); e.HasKey(x => x.Id); });
    }

    // ---------- Auditoría: cada alta, cambio o baja de entidades de negocio queda en la bitácora ----------
    private static readonly HashSet<Type> NoAuditar = [typeof(VersionDatos), typeof(ConsultaCodigoBarras), typeof(Auditoria), typeof(MovimientoInventario), typeof(CostoLote)];
    private static readonly HashSet<string> CamposPrivados = ["Hash", "Sal", "Iteraciones"];

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RegistrarAuditoria();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        RegistrarAuditoria();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void RegistrarAuditoria()
    {
        var ahora = DateTime.UtcNow;
        foreach (var e in ChangeTracker.Entries().Where(e => !NoAuditar.Contains(e.Entity.GetType())
                     && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            var cambios = new Dictionary<string, object?[]>();
            foreach (var p in e.Properties.Where(p => !CamposPrivados.Contains(p.Metadata.Name)))
            {
                if (e.State == EntityState.Added) cambios[p.Metadata.Name] = [null, p.CurrentValue];
                else if (e.State == EntityState.Deleted) cambios[p.Metadata.Name] = [p.OriginalValue, null];
                else if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue) && p.Metadata.Name != "ActualizadoUtc")
                    cambios[p.Metadata.Name] = [p.OriginalValue, p.CurrentValue];
            }
            if (e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name == "Hash" && p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
                cambios["Contrasena"] = ["(oculta)", "(cambiada)"];
            if (e.State == EntityState.Modified && cambios.Count == 0) continue;
            var llave = e.Metadata.FindPrimaryKey()!.Properties.Select(p => e.Property(p.Name).CurrentValue?.ToString()).FirstOrDefault() ?? "";
            Auditoria.Add(new Auditoria
            {
                FechaUtc = ahora,
                Usuario = UsuarioActual,
                Entidad = e.Metadata.GetTableName() ?? e.Entity.GetType().Name,
                EntidadId = llave,
                Accion = e.State switch { EntityState.Added => "alta", EntityState.Deleted => "baja", _ => "cambio" },
                Cambios = JsonSerializer.Serialize(cambios),
            });
        }
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
