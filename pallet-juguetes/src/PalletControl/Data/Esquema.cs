using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PalletControl.Models;

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
                EjecutarDdl(db, $"ALTER TABLE \"{Id(tabla)}\" DROP COLUMN \"{Id(columna)}\"");

        AgregarColumna(db, "productos", "CodigoBarras", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "configuracion", "TipoCambioUsd", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "configuracion", "TipoCambioFecha", "TEXT NULL");
        AgregarColumna(db, "configuracion", "TipoCambioFuente", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "configuracion", "TipoCambioPublicado", "TEXT NULL");
        AgregarColumna(db, "configuracion", "DescuentoSugerido", "INTEGER NOT NULL DEFAULT 35");
        AgregarColumna(db, "productos", "PrecioMexico", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "productos", "Caracteristicas", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "productos", "PrecioLiverpool", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "productos", "PrecioMercadoLibre", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "productos", "PrecioWalmart", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "productos", "PrecioJuguetron", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "productos", "PrecioJuguetibici", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "productos", "PrecioAmazon", "TEXT NOT NULL DEFAULT '0.0'");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "producto_imagenes" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_producto_imagenes" PRIMARY KEY,
                "ProductoId" TEXT NOT NULL,
                "Archivo" TEXT NOT NULL,
                "Origen" TEXT NOT NULL,
                "Principal" INTEGER NOT NULL,
                "CreadoUtc" TEXT NOT NULL,
                CONSTRAINT "FK_producto_imagenes_productos_ProductoId" FOREIGN KEY ("ProductoId") REFERENCES "productos" ("Id") ON DELETE CASCADE);
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_producto_imagenes_ProductoId" ON "producto_imagenes" ("ProductoId");""");
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

        // ---- Fase 1: datos comerciales del producto ----
        AgregarColumna(db, "productos", "Marca", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "productos", "Modelo", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "productos", "Subcategoria", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "productos", "DescripcionComercial", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "productos", "AccesoriosIncluidos", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "productos", "EdadRecomendada", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "productos", "PesoKg", "TEXT NULL");
        AgregarColumna(db, "productos", "LargoCm", "TEXT NULL");
        AgregarColumna(db, "productos", "AnchoCm", "TEXT NULL");
        AgregarColumna(db, "productos", "AltoCm", "TEXT NULL");
        AgregarColumna(db, "productos", "EstadoComercial", "TEXT NOT NULL DEFAULT 'en_revision'");
        AgregarColumna(db, "productos", "FechaIngreso", "TEXT NULL");

        // ---- Fase 3: encabezado financiero de la venta ----
        AgregarColumna(db, "ventas", "Folio", "INTEGER NOT NULL DEFAULT 0");
        AgregarColumna(db, "ventas", "PlataformaId", "TEXT NULL");
        AgregarColumna(db, "ventas", "PublicacionId", "TEXT NULL");
        AgregarColumna(db, "ventas", "TarifaId", "TEXT NULL");
        AgregarColumna(db, "ventas", "PrecioPublicado", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "ventas", "Descuento", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "ventas", "FactorIngreso", "TEXT NOT NULL DEFAULT '1.0'");
        AgregarColumna(db, "ventas", "RetencionIsr", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "ventas", "RetencionIva", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "ventas", "ImporteCobrado", "TEXT NULL");
        AgregarColumna(db, "ventas", "FechaCobro", "TEXT NULL");
        AgregarColumna(db, "ventas", "CantidadDevuelta", "INTEGER NOT NULL DEFAULT 0");
        AgregarColumna(db, "ventas", "CostoTotal", "TEXT NOT NULL DEFAULT '0.0'");
        AgregarColumna(db, "ventas", "CostoOrigen", "TEXT NOT NULL DEFAULT 'sin_costeo'");
        AgregarColumna(db, "ventas", "Migrada", "INTEGER NOT NULL DEFAULT 0");

        // ---- Fase 4: umbrales de inteligencia comercial (configurables) ----
        AgregarColumna(db, "configuracion", "DiasSinMovimiento", "INTEGER NOT NULL DEFAULT 30");
        AgregarColumna(db, "configuracion", "BandaCompetenciaPct", "TEXT NOT NULL DEFAULT '5.0'");

        // ---- Precio de lista capturado en dólares (monto original y tipo de cambio usado) ----
        AgregarColumna(db, "productos", "PrecioListaUsd", "TEXT NULL");
        AgregarColumna(db, "productos", "PrecioListaTipoCambio", "TEXT NULL");
        // ---- Precio sugerido al escanear: valor de referencia del juguete y valor de mercado del pallet ----
        AgregarColumna(db, "productos", "ValorReferencia", "TEXT NULL");
        AgregarColumna(db, "productos", "ValorReferenciaOrigen", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "pallets", "ValorMercadoEstimado", "TEXT NULL");

        // ---- Fase 5: vínculos con plataformas conectadas ----
        AgregarColumna(db, "publicaciones", "CuentaIntegracionId", "TEXT NULL");
        AgregarColumna(db, "publicaciones", "EstatusRemoto", "TEXT NOT NULL DEFAULT ''");
        AgregarColumna(db, "publicaciones", "ExistenciasRemotas", "INTEGER NULL");
        AgregarColumna(db, "publicaciones", "PrecioRemoto", "TEXT NULL");
        AgregarColumna(db, "publicaciones", "SincronizadoUtc", "TEXT NULL");
        AgregarColumna(db, "ventas", "PedidoExterno", "TEXT NULL");
        AgregarColumna(db, "ventas", "CuentaIntegracionId", "TEXT NULL");

        // ---- Tablas nuevas, tal como las define el modelo de EF ----
        CrearTablasFaltantes(db);
        MigrarAPallets(db, dirRespaldos);
        SembrarFase2(db);
        MigrarVentasFase3(db, dirRespaldos);
    }

    /// <summary>
    /// Fase 2: catálogo inicial de plataformas (sin tarifas: se capturan de las fuentes oficiales), objetivo global
    /// de rentabilidad (decisión del usuario: margen 30 %, mínimo 10 %, terminación 9) y situación fiscal pendiente.
    /// Solo agrega lo que falta; nunca modifica lo ya capturado.
    /// </summary>
    private static void SembrarFase2(AppDbContext db)
    {
        // Se agregan las plataformas del catálogo que falten (p. ej. Instagram en bases creadas antes); nunca se modifican las existentes.
        {
            var semilla = new (string Codigo, string Nombre, string Tipo)[]
            {
                ("facebook_marketplace", "Facebook Marketplace", "local"),
                ("ml_clasica", "Mercado Libre Clásica", "marketplace"),
                ("ml_premium", "Mercado Libre Premium", "marketplace"),
                ("amazon_mx", "Amazon México", "marketplace"),
                ("whatsapp", "WhatsApp Business", "local"),
                ("venta_fisica", "Ventas físicas", "fisica"),
                // Canales que ya existían en el registro de ventas (decisión 11.5: se conservan como plataformas adicionales)
                ("grupos_facebook", "Grupos de Facebook", "local"),
                ("conocidos", "Conocidos", "local"),
                ("bazar_escuela", "Bazar o escuela", "fisica"),
                ("instagram", "Instagram", "local"),
                ("facebook", "Facebook (perfil o página)", "local"),
            };
            var existentes = db.Plataformas.Select(p => p.Codigo).ToHashSet();
            var i = 0;
            foreach (var (codigo, nombre, tipo) in semilla)
            {
                i++;
                if (!existentes.Contains(codigo))
                    db.Plataformas.Add(new Plataforma { Id = Guid.NewGuid().ToString("N")[..12], Codigo = codigo, Nombre = nombre, Tipo = tipo, Orden = i });
            }
        }
        if (!db.Parametros.Any(p => p.Categoria == null && p.ProductoId == null && p.PlataformaId == null))
            db.Parametros.Add(new ParametroRentabilidad
            {
                Id = Guid.NewGuid().ToString("N")[..12], Esquema = "margen", Valor = 30m, MargenMinimoPct = 10m,
                DescuentoPromocionalPct = 0m, Redondeo = "terminacion9", Notas = "Objetivo general definido por el usuario al iniciar la Fase 2.",
            });
        if (db.Fiscal.Find(1) is null) db.Fiscal.Add(new ConfiguracionFiscal());
        db.SaveChanges();
    }

    /// <summary>
    /// Crea las tablas e índices del modelo que no existan, usando el script que genera EF Core.
    /// Así una base migrada queda igual que una base nueva.
    /// </summary>
    private static void CrearTablasFaltantes(AppDbContext db)
    {
        var script = db.Database.GenerateCreateScript();
        foreach (var sentencia in script.Split(";", StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var tabla = Regex.Match(sentencia, "^CREATE TABLE \"([^\"]+)\"");
            if (tabla.Success)
            {
                if (!ExisteTabla(db, tabla.Groups[1].Value)) db.Database.ExecuteSqlRaw(sentencia);
                continue;
            }
            if (Regex.IsMatch(sentencia, "^CREATE (UNIQUE )?INDEX \""))
                db.Database.ExecuteSqlRaw(Regex.Replace(sentencia, "^CREATE (UNIQUE )?INDEX ", "CREATE $1INDEX IF NOT EXISTS "));
        }
    }

    /// <summary>
    /// Migración de la Fase 1: si hay productos y aún no hay pallets, crea "Pallet 1" con la inversión de Ajustes
    /// y un lote por producto (condición según la decisión 11.4), con su movimiento de inventario inicial.
    /// Guarda un respaldo antes. No borra ni cambia datos existentes.
    /// </summary>
    private static void MigrarAPallets(AppDbContext db, string dirRespaldos)
    {
        if (db.Pallets.Any() || !db.Productos.Any()) return;

        Directory.CreateDirectory(dirRespaldos);
        db.Database.ExecuteSqlRaw("VACUUM INTO {0}", Path.Combine(dirRespaldos, $"pallet-antes-de-fase1-{DateTime.Now:yyyy-MM-dd-HHmmss}.db"));

        using var tx = db.Database.BeginTransaction();
        var cfg = db.Configuracion.Find(1) ?? new Configuracion();
        var productos = db.Productos.ToList();
        var primera = productos.Min(p => p.CreadoUtc);
        var pallet = new Pallet
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Nombre = "Pallet 1",
            FechaCompra = DateOnly.FromDateTime(primera.ToLocalTime()),
            PaisOrigen = "Estados Unidos",
            Moneda = "MXN",
            CostoOriginal = cfg.Inversion,
            TipoCambio = 1m,
            CostoMxn = cfg.Inversion,
            InversionTotal = cfg.Inversion,
            Estatus = "en_venta",
            Notas = "Creado automáticamente con la inversión de Ajustes al actualizar a la Fase 1. Revisa proveedor, fecha y moneda.",
        };
        db.Pallets.Add(pallet);
        foreach (var p in productos)
        {
            var condicion = Catalogos.CondicionDesdeEstado(p.Estado);
            var lote = new InventarioLote
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                ProductoId = p.Id,
                PalletId = pallet.Id,
                Condicion = condicion,
                Comercializable = Catalogos.ComercializablePorDefecto(condicion),
                Tratamiento = "",
                Cantidad = p.Cantidad,
                FechaIngreso = DateOnly.FromDateTime(p.CreadoUtc.ToLocalTime()),
            };
            db.Lotes.Add(lote);
            if (p.Cantidad > 0)
                db.Movimientos.Add(new MovimientoInventario { LoteId = lote.Id, Tipo = "inventario_inicial", Cantidad = p.Cantidad, Usuario = "sistema", Motivo = "Saldo inicial al migrar a la Fase 1" });
            p.FechaIngreso ??= lote.FechaIngreso;
        }
        db.SaveChanges();
        tx.Commit();
    }

    /// <summary>
    /// Migración de la Fase 3: cada venta anterior recibe folio, plataforma (si su canal coincide con una), renglones por lote
    /// (piezas vendibles del pallet más antiguo primero), costo histórico estimado con el costeo vigente y su salida en el kárdex.
    /// Las ventas pagadas o entregadas se consideran cobradas por su total (no tenían gastos registrados).
    /// Guarda un respaldo antes y no borra ni modifica montos existentes.
    /// </summary>
    private static void MigrarVentasFase3(AppDbContext db, string dirRespaldos)
    {
        var pendientes = db.Ventas.Where(v => v.Folio == 0).ToList().OrderBy(v => v.CreadoUtc).ThenBy(v => v.Id).ToList();
        if (pendientes.Count == 0) return;
        Directory.CreateDirectory(dirRespaldos);
        db.Database.ExecuteSqlRaw("VACUUM INTO {0}", Path.Combine(dirRespaldos, $"pallet-antes-de-fase3-{DateTime.Now:yyyy-MM-dd-HHmmss}.db"));

        using var tx = db.Database.BeginTransaction();
        var folio = (db.Ventas.Max(v => (int?)v.Folio) ?? 0) + 1;
        static string Clave(string s) => new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => char.IsLetterOrDigit(ch)).ToArray()).ToLowerInvariant();
        var plataformas = db.Plataformas.ToList().ToDictionary(p => Clave(p.Nombre), p => p.Id);
        var vigentes = db.CosteoCorridas.Where(c => c.Vigente).Select(c => c.Id).ToList();
        var costos = db.CostosLote.Where(c => vigentes.Contains(c.CorridaId)).ToList().ToDictionary(c => c.LoteId);
        var pallets = db.Pallets.ToList().ToDictionary(p => p.Id);
        var lotes = db.Lotes.ToList();
        var asignado = new Dictionary<string, int>();
        foreach (var v in pendientes)
        {
            v.Folio = folio++;
            v.Migrada = true;
            v.PrecioPublicado = v.Precio;
            if (v.PlataformaId is null && plataformas.TryGetValue(Clave(v.Canal), out var pid)) v.PlataformaId = pid;
            var activa = v.Estado != "cancelado";
            var orden = lotes.Where(l => l.ProductoId == v.ProductoId)
                .OrderByDescending(l => l.Comercializable).ThenBy(l => pallets[l.PalletId].FechaCompra).ThenBy(l => l.FechaIngreso).ThenBy(l => l.Id).ToList();
            if (orden.Count == 0) continue;   // sin lote (no debería ocurrir tras la Fase 1)
            var asign = Dominio.Calculo.CalculoVenta.AsignarLotes(v.Cantidad,
                orden.Select(l => (l.Id, Math.Max(0, l.Cantidad - asignado.GetValueOrDefault(l.Id)))).ToList())
                ?? [(orden[0].Id, v.Cantidad)];
            decimal costoTotal = 0; var origenes = new HashSet<string>();
            foreach (var (loteId, cant) in asign)
            {
                var lote = orden.First(l => l.Id == loteId);
                var c = costos.GetValueOrDefault(loteId);
                var cu = c is not null && c.Cantidad > 0 ? c.CostoUnitario : 0m;
                var origen = c is not null ? "estimado_migracion" : "sin_costeo";
                origenes.Add(origen);
                var ct = Dominio.Calculo.Dinero.Centavos(cu * cant);
                costoTotal += ct;
                db.VentaDetalles.Add(new VentaDetalle
                {
                    Id = Guid.NewGuid().ToString("N")[..12], VentaId = v.Id, ProductoId = v.ProductoId, LoteId = loteId, PalletId = lote.PalletId,
                    Cantidad = cant, PrecioUnitario = v.Precio, CostoUnitario = cu, CostoTotal = ct, CostoCorridaId = c?.CorridaId, CostoOrigen = origen,
                });
                if (activa)
                {
                    asignado[loteId] = asignado.GetValueOrDefault(loteId) + cant;
                    db.Movimientos.Add(new MovimientoInventario { LoteId = loteId, Tipo = "venta", Cantidad = -cant, Referencia = $"V-{v.Folio:00000}", Usuario = "sistema", Motivo = "Venta anterior (migración a la Fase 3)" });
                }
            }
            v.CostoTotal = costoTotal;
            v.CostoOrigen = origenes.Count == 1 ? origenes.First() : "mixto";
            if (Catalogos.Cobrada(v.Estado) && v.ImporteCobrado is null)
            {
                v.ImporteCobrado = Dominio.Calculo.Dinero.Centavos(v.Precio * v.Cantidad);
                v.FechaCobro = v.FechaPago ?? v.Fecha;
            }
        }
        db.SaveChanges();
        tx.Commit();
    }

    private static bool ExisteTabla(AppDbContext db, string tabla) => db.Database
        .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {tabla}")
        .AsEnumerable().First() > 0;

    private static bool Existe(AppDbContext db, string tabla, string columna) => db.Database
        .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM pragma_table_info({tabla}) WHERE name = {columna}")
        .AsEnumerable().First() > 0;

    private static void AgregarColumna(AppDbContext db, string tabla, string columna, string definicion)
    {
        if (!Existe(db, tabla, columna))
            EjecutarDdl(db, $"ALTER TABLE \"{Id(tabla)}\" ADD COLUMN \"{Id(columna)}\" {definicion}");
    }

    /// <summary>Los nombres de tablas y columnas no se pueden parametrizar en DDL: solo se aceptan identificadores simples definidos en este archivo.</summary>
    private static string Id(string nombre) =>
        Regex.IsMatch(nombre, "^[A-Za-z_][A-Za-z0-9_]*$") ? nombre : throw new ArgumentException($"Identificador no válido: {nombre}");

#pragma warning disable EF1002 // DDL con identificadores validados por Id() y definiciones constantes de este archivo.
    private static void EjecutarDdl(AppDbContext db, string sql) => db.Database.ExecuteSqlRaw(sql);
#pragma warning restore EF1002
}
