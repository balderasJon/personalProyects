using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

public record CompetenciaIn(string? ProductoId, string? ProductoReferencia, string? PlataformaId, string? PlataformaTexto, string? Marca, string? Modelo,
    string? Condicion, string? CodigoBarras, string? Caracteristicas, decimal PrecioPublicado, decimal PrecioEnvio, string? Url, string? Vendedor,
    DateOnly? FechaConsulta, string? Observaciones, bool Vigente = true);
public record ParametrosComercialesIn(int DiasSinMovimiento, decimal BandaCompetenciaPct);

/// <summary>Fase 4: competencia, dashboard ejecutivo y reportes CSV.</summary>
public static class ComercialApi
{
    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    private static string T(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static IResult Mal(string m) => Results.BadRequest(new Error(m));

    private static FiltroComercial Filtro(DateOnly? desde, DateOnly? hasta, string? palletId, string? categoria, string? productoId, string? plataformaId) =>
        new(desde, hasta, N(palletId), N(categoria), N(productoId), N(plataformaId));

    public static void MapComercial(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---------- Competencia ----------
        api.MapGet("/competencia", async (string? productoId, bool? todas, AppDbContext db) =>
        {
            var q = db.Competencia.AsNoTracking();
            if (N(productoId) is { } pid) q = q.Where(c => c.ProductoId == pid);
            if (todas != true) q = q.Where(c => c.Vigente);
            var l = await q.ToListAsync();
            var prods = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Codigo + " · " + p.Nombre);
            var pls = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            return Results.Ok(l.OrderByDescending(c => c.FechaConsulta).ThenByDescending(c => c.CreadoUtc).Select(c => new
            {
                competencia = c, producto = prods.GetValueOrDefault(c.ProductoId),
                plataforma = c.PlataformaId is null ? c.PlataformaTexto : pls.GetValueOrDefault(c.PlataformaId),
                imagen = c.ImagenArchivo is null ? null : "/imagenes/" + c.ImagenArchivo,
            }));
        });
        api.MapGet("/competencia/resumen", async (string? productoId, AppDbContext db) =>
            Results.Ok(await new Comercial(db).CompetenciaAsync(N(productoId))));
        api.MapPost("/competencia", (CompetenciaIn i, AppDbContext db) => Api.Bloqueado(() => Guardar(db, null, i)));
        api.MapPut("/competencia/{id}", (string id, CompetenciaIn i, AppDbContext db) => Api.Bloqueado(() => Guardar(db, id, i)));
        api.MapDelete("/competencia/{id}", (string id, AppDbContext db, AlmacenImagenes alm) => Api.Bloqueado(async () =>
        {
            var c = await db.Competencia.FindAsync(id);
            if (c is null) return Results.NotFound(new Error("El registro ya no existe."));
            db.Competencia.Remove(c);
            await db.GuardarAsync();
            if (c.ImagenArchivo is not null) alm.Borrar(c.ImagenArchivo);
            return Results.NoContent();
        }));
        // Foto del producto de la competencia (sirve para identificarlo cuando no hay UPC/EAN). Reemplaza la anterior.
        api.MapPost("/competencia/{id}/imagen", async (string id, HttpRequest req, AppDbContext db, AlmacenImagenes alm, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Mal("Envía la imagen como formulario.");
            var form = await req.ReadFormAsync(ct);
            var f = form.Files.FirstOrDefault();
            if (f is null) return Mal("No llegó ninguna imagen.");
            if (f.Length > AlmacenImagenes.TamanoMaximo) return Mal("La imagen pesa más de 5 MB.");
            await using var st = f.OpenReadStream();
            var datos = await AlmacenImagenes.LeerLimitadoAsync(st, ct);
            var ext = datos is null ? null : AlmacenImagenes.Extension(datos);
            if (datos is null || ext is null) return Mal("La imagen debe ser JPG, PNG, WEBP o GIF.");
            return await Api.Bloqueado(async () =>
            {
                var c = await db.Competencia.FindAsync(id);
                if (c is null) return Results.NotFound(new Error("El registro ya no existe."));
                var anterior = c.ImagenArchivo;
                c.ImagenArchivo = await alm.GuardarAsync("c" + Inventario.NuevoId(), datos, ext, ct);
                c.ActualizadoUtc = DateTime.UtcNow;
                await db.GuardarAsync();
                if (anterior is not null) alm.Borrar(anterior);
                return Results.Ok(new { imagen = "/imagenes/" + c.ImagenArchivo });
            });
        });

        // ---------- Parámetros de inteligencia comercial ----------
        api.MapPut("/configuracion/comercial", (ParametrosComercialesIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            if (i.DiasSinMovimiento is < 1 or > 730) return Mal("Los días sin movimiento deben estar entre 1 y 730.");
            if (i.BandaCompetenciaPct is < 0 or > 50) return Mal("La banda de precio de mercado debe estar entre 0 % y 50 %.");
            var c = await db.Configuracion.OrderBy(x => x.Id).FirstAsync();
            c.DiasSinMovimiento = i.DiasSinMovimiento;
            c.BandaCompetenciaPct = Math.Round(i.BandaCompetenciaPct, 2, MidpointRounding.AwayFromZero);
            c.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(new { c.DiasSinMovimiento, c.BandaCompetenciaPct });
        }));

        // ---------- Dashboard ----------
        api.MapGet("/dashboard", async (DateOnly? desde, DateOnly? hasta, string? palletId, string? categoria, string? productoId, string? plataformaId, AppDbContext db) =>
            Results.Ok(await new Comercial(db).DashboardAsync(Filtro(desde, hasta, palletId, categoria, productoId, plataformaId))));

        // ---------- Reportes CSV (se abren en Excel) ----------
        api.MapGet("/reportes/{tipo}", async (string tipo, DateOnly? desde, DateOnly? hasta, string? palletId, string? categoria, string? productoId,
            string? plataformaId, AppDbContext db, HttpContext ctx) =>
        {
            var f = Filtro(desde, hasta, palletId, categoria, productoId, plataformaId);
            var filas = tipo switch
            {
                "ventas" => await ReporteVentasAsync(db, f),
                "productos" or "plataformas" or "categorias" or "pallets" or "liquidacion" => ReporteDashboard(tipo, await new Comercial(db).DashboardAsync(f)),
                "inventario" => await ReporteInventarioAsync(db, f),
                "competencia" => await ReporteCompetenciaAsync(db, f),
                _ => null,
            };
            if (filas is null) return Results.NotFound(new Error("Reporte no disponible."));
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"reporte-{tipo}-{DateTime.Now:yyyy-MM-dd}.csv\"";
            return Results.Text(Csv(filas), "text/csv; charset=utf-8", Encoding.UTF8);
        });
    }

    private static async Task<IResult> Guardar(AppDbContext db, string? id, CompetenciaIn i)
    {
        if (N(i.ProductoId) is not { } pid || await db.Productos.FindAsync(pid) is null) return Mal("Elige el juguete propio con el que se compara.");
        if (string.IsNullOrWhiteSpace(i.ProductoReferencia)) return Mal("Escribe el nombre del producto de la competencia.");
        var plat = N(i.PlataformaId);
        if (plat is not null && await db.Plataformas.FindAsync(plat) is null) return Mal("La plataforma elegida no existe.");
        if (plat is null && string.IsNullOrWhiteSpace(i.PlataformaTexto)) return Mal("Indica la plataforma o la tienda donde viste el precio.");
        if (i.PrecioPublicado <= 0) return Mal("El precio publicado debe ser mayor que cero.");
        if (i.PrecioEnvio < 0) return Mal("El costo de envío no puede ser negativo.");
        var cond = T(i.Condicion, 20);
        if (cond.Length > 0 && !Catalogos.CondicionValida(cond)) return Mal("Condición no válida.");
        var fecha = i.FechaConsulta ?? Hoy;
        if (fecha > Hoy) return Mal("La fecha de consulta no puede ser futura.");
        var url = T(i.Url, 500);
        if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
            return Mal("La URL debe empezar con http:// o https://.");
        Competencia? c;
        if (id is null) { c = new Competencia { Id = Inventario.NuevoId(), Usuario = db.UsuarioActual }; db.Competencia.Add(c); }
        else
        {
            c = await db.Competencia.FindAsync(id);
            if (c is null) return Results.NotFound(new Error("El registro ya no existe."));
        }
        c.ProductoId = pid; c.ProductoReferencia = T(i.ProductoReferencia, 160); c.PlataformaId = plat; c.PlataformaTexto = plat is null ? T(i.PlataformaTexto, 60) : "";
        c.Marca = T(i.Marca, 80); c.Modelo = T(i.Modelo, 80); c.Condicion = cond; c.CodigoBarras = BuscadorCodigoBarras.Normalizar(i.CodigoBarras);
        c.Caracteristicas = T(i.Caracteristicas, 600);
        c.PrecioPublicado = Dinero.Centavos(i.PrecioPublicado); c.PrecioEnvio = Dinero.Centavos(i.PrecioEnvio); c.PrecioTotal = c.PrecioPublicado + c.PrecioEnvio;
        c.Url = url; c.Vendedor = T(i.Vendedor, 100); c.FechaConsulta = fecha; c.Observaciones = T(i.Observaciones, 500); c.Vigente = i.Vigente;
        c.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return id is null ? Results.Created($"/api/competencia/{c.Id}", c) : Results.Ok(c);
    }

    // ---------------- CSV ----------------

    private static string Csv(List<object?[]> filas)
    {
        static string Celda(object? v)
        {
            var s = v switch
            {
                null => "",
                decimal d => d.ToString(CultureInfo.InvariantCulture),
                DateOnly d => d.ToString("yyyy-MM-dd"),
                bool b => b ? "Sí" : "No",
                IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
                _ => v.ToString() ?? "",
            };
            // Evita que Excel interprete texto como fórmula.
            if (s.Length > 0 && "=+-@".Contains(s[0]) && v is string) s = "'" + s;
            return s.IndexOfAny([',', '"', '\n', '\r', ';']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        return "﻿" + string.Join("\r\n", filas.Select(f => string.Join(",", f.Select(Celda))));
    }

    private static readonly Dictionary<string, string> Pos = new() { ["barato"] = "Abajo del mercado", ["en_mercado"] = "En precio de mercado", ["caro"] = "Arriba del mercado" };

    private static List<object?[]> ReporteDashboard(string tipo, Comercial.Dashboard d)
    {
        var f = new List<object?[]>();
        switch (tipo)
        {
            case "productos":
                f.Add(["Código", "Juguete", "Categoría", "Piezas recibidas", "Vendidas", "Disponibles", "Ingresos (periodo)", "Utilidad (periodo)", "Margen %", "Rotación %", "Días sin movimiento", "Costo unitario", "Precio mínimo", "Posición competitiva", "Mediana del mercado"]);
                f.AddRange(d.Productos.Select(p => new object?[] { p.Codigo, p.Nombre, p.Categoria, p.Recibidas, p.Vendidas, p.Disponibles, p.Ingresos, p.Utilidad, p.MargenPct, p.RotacionPct, p.DiasSinMovimiento, p.CostoUnitario, p.PrecioMinimo, p.PosicionCompetitiva is null ? null : Pos[p.PosicionCompetitiva], p.MedianaMercado }));
                break;
            case "plataformas":
            case "categorias":
                f.Add([tipo == "plataformas" ? "Plataforma" : "Categoría", "Ventas", "Piezas", "Ingresos", "Utilidad", "Margen %"]);
                f.AddRange((tipo == "plataformas" ? d.PorPlataforma : d.PorCategoria).Select(m => new object?[] { m.Nombre, m.Ventas, m.Piezas, m.Ingresos, m.Utilidad, m.MargenPct }));
                break;
            case "pallets":
                f.Add(["Pallet", "Inversión", "Capital recuperado", "Recuperado %", "Pendiente de recuperar"]);
                f.AddRange(d.PorPallet.Select(p => new object?[] { p.Nombre, p.Inversion, p.Recuperado, p.RecuperadoPct, p.Pendiente }));
                break;
            case "liquidacion":
                f.Add(["Código", "Juguete", "Categoría", "Disponibles", "Días sin movimiento", "Costo unitario", "Precio mínimo", "Mediana del mercado", "Posición competitiva"]);
                f.AddRange(d.CandidatosLiquidacion.Select(p => new object?[] { p.Codigo, p.Nombre, p.Categoria, p.Disponibles, p.DiasSinMovimiento, p.CostoUnitario, p.PrecioMinimo, p.MedianaMercado, p.PosicionCompetitiva is null ? null : Pos[p.PosicionCompetitiva] }));
                break;
        }
        return f;
    }

    private static async Task<List<object?[]>> ReporteVentasAsync(AppDbContext db, FiltroComercial f)
    {
        var pq = await Ventas.CargarAsync(db);
        var prods = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var pls = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
        var pal = await db.Pallets.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
        var filas = new List<object?[]> { new object?[] { "Folio", "Fecha", "Estado", "Código", "Juguete", "Categoría", "Plataforma o canal", "Pallets", "Piezas", "Devueltas", "Precio", "Descuento",
            "Cobrado al cliente", "IVA", "Ingreso", "Reembolsos", "Gastos de la plataforma", "Gastos propios", "Costo histórico", "Merma", "Utilidad", "Margen %",
            "Cobrada", "Importe cobrado", "Fecha de cobro", "Efectivo neto", "Por cobrar (estimado)", "Origen del costo" } };
        foreach (var v in pq.Ventas.OrderBy(v => v.Folio))
        {
            if (!prods.TryGetValue(v.ProductoId, out var p)) continue;
            if (f.Desde is not null && v.Fecha < f.Desde || f.Hasta is not null && v.Fecha > f.Hasta) continue;
            if (f.ProductoId is not null && v.ProductoId != f.ProductoId || f.PlataformaId is not null && v.PlataformaId != f.PlataformaId) continue;
            if (f.Categoria is not null && !string.Equals(p.Categoria, f.Categoria, StringComparison.OrdinalIgnoreCase)) continue;
            var det = pq.Detalle[v.Id].ToList();
            if (f.PalletId is not null && det.All(d => d.PalletId != f.PalletId)) continue;
            var r = Ventas.Resultado(pq, v);
            filas.Add([Ventas.Folio(v), v.Fecha, v.Estado, p.Codigo, p.Nombre, p.Categoria, v.PlataformaId is null ? v.Canal : pls.GetValueOrDefault(v.PlataformaId),
                string.Join(" / ", det.Select(d => pal.GetValueOrDefault(d.PalletId)).Distinct()), v.Cantidad, v.CantidadDevuelta, v.Precio, v.Descuento,
                r.IngresoBruto, r.IvaTrasladado, r.IngresoNeto, r.Reembolsos, r.GastosDeducidos, r.GastosPropios, r.CostoVendido, r.CostoMerma, r.Utilidad, r.MargenPct,
                r.Cobrada, r.Cobrada ? r.ImporteCobrado : null, v.FechaCobro, r.Cobrada ? r.EfectivoNeto : null, r.PorCobrarEstimado, v.CostoOrigen]);
        }
        return filas;
    }

    private static async Task<List<object?[]>> ReporteInventarioAsync(AppDbContext db, FiltroComercial f)
    {
        var prods = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var pal = await db.Pallets.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
        var vig = await db.CosteoCorridas.AsNoTracking().Where(c => c.Vigente).Select(c => c.Id).ToListAsync();
        var costos = (await db.CostosLote.AsNoTracking().Where(c => vig.Contains(c.CorridaId)).ToListAsync()).GroupBy(c => c.LoteId).ToDictionary(g => g.Key, g => g.First());
        var cond = Catalogos.Condiciones.ToDictionary(c => c.Codigo, c => c.Nombre);
        var filas = new List<object?[]> { new object?[] { "Código", "Juguete", "Categoría", "Pallet", "Condición", "Vendible", "Recibidas", "Vendidas", "Disponibles", "Fecha de ingreso", "Días en inventario", "Costo unitario", "Costo de lo disponible" } };
        foreach (var pid in prods.Keys.OrderBy(k => prods[k].Codigo, StringComparer.OrdinalIgnoreCase))
        {
            var p = prods[pid];
            if (f.ProductoId is not null && pid != f.ProductoId || f.Categoria is not null && !string.Equals(p.Categoria, f.Categoria, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var (l, disp) in await Ventas.ExistenciasAsync(db, pid))
            {
                if (f.PalletId is not null && l.PalletId != f.PalletId) continue;
                var cu = costos.TryGetValue(l.Id, out var c) && c.Cantidad > 0 ? c.CostoUnitario : (decimal?)null;
                filas.Add([p.Codigo, p.Nombre, p.Categoria, pal.GetValueOrDefault(l.PalletId), cond.GetValueOrDefault(l.Condicion) ?? l.Condicion, l.Comercializable,
                    l.Cantidad, l.Cantidad - disp, disp, l.FechaIngreso, Math.Max(0, Hoy.DayNumber - l.FechaIngreso.DayNumber), cu, cu is null ? null : Dinero.Centavos(cu.Value * Math.Max(0, disp))]);
            }
        }
        return filas;
    }

    private static async Task<List<object?[]>> ReporteCompetenciaAsync(AppDbContext db, FiltroComercial f)
    {
        var res = (await new Comercial(db).CompetenciaAsync(f.ProductoId)).ToDictionary(r => r.ProductoId);
        var obs = await db.Competencia.AsNoTracking().Where(c => f.ProductoId == null || c.ProductoId == f.ProductoId).ToListAsync();
        var pls = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
        var cond = Catalogos.Condiciones.ToDictionary(c => c.Codigo, c => c.Nombre);
        var filas = new List<object?[]> { new object?[] { "Juguete propio", "Tu precio", "Producto de referencia", "Plataforma o tienda", "Marca", "Modelo", "Condición", "Código de barras",
            "Precio publicado", "Envío", "Precio total", "Vendedor", "Fecha de consulta", "Vigente", "URL", "Observaciones",
            "Mínimo del mercado", "Máximo", "Promedio", "Mediana", "Diferencia % vs mediana", "Posición" } };
        foreach (var c in obs.OrderBy(c => c.ProductoId).ThenByDescending(c => c.FechaConsulta))
        {
            var r = res.GetValueOrDefault(c.ProductoId);
            if (r is null || f.Categoria is not null && !string.Equals(r.Categoria, f.Categoria, StringComparison.OrdinalIgnoreCase)) continue;
            filas.Add([r.Codigo + " · " + r.Nombre, r.PrecioPropio, c.ProductoReferencia, c.PlataformaId is null ? c.PlataformaTexto : pls.GetValueOrDefault(c.PlataformaId), c.Marca, c.Modelo,
                cond.GetValueOrDefault(c.Condicion) ?? c.Condicion, c.CodigoBarras, c.PrecioPublicado, c.PrecioEnvio, c.PrecioTotal, c.Vendedor, c.FechaConsulta, c.Vigente, c.Url, c.Observaciones,
                r.Mercado?.Minimo, r.Mercado?.Maximo, r.Mercado?.Promedio, r.Mercado?.Mediana, r.Posicion?.DiferenciaPct, r.Posicion is null ? null : Pos[r.Posicion.Posicion]]);
        }
        return filas;
    }
}
