using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Calculo;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

public record PlataformaIn(string? Nombre, string? Tipo, bool Activa = true, int? Orden = null, string? Notas = null);
public record TarifaIn(string? PlataformaId, string? Nombre,
    string? TipoPublicacion, string? Modalidad, string? Categoria, string? MedioPago,
    decimal? PrecioMin, decimal? PrecioMax, decimal? PesoMinKg, decimal? PesoMaxKg, decimal? LadoMaxCm,
    decimal ComisionPct, decimal ComisionMedioPagoPct, decimal CargoFijo, decimal CostoLogistico, decimal EnvioAbsorbido,
    decimal Almacenamiento, decimal Preparacion, decimal Empaque, decimal PublicidadPct, decimal PublicidadFija,
    decimal ReservaDevolucionesPct, decimal RetencionIsrPct, decimal RetencionIvaPct,
    DateOnly? VigenteDesde, DateOnly? VigenteHasta, bool Activa = true, bool Predeterminada = false, string? Notas = null);
public record ParametroIn(string? Categoria, string? ProductoId, string? PlataformaId, string? Esquema, decimal Valor,
    decimal MargenMinimoPct, decimal DescuentoPromocionalPct, string? Redondeo, bool Activo = true, string? Notas = null);
public record FiscalIn(string? Regimen, bool CobraIva, decimal TasaIvaPct, string? Notas);
public record SimularIn(string? ProductoId, decimal? Costo, string? Categoria, decimal? PesoKg, string? PlataformaId, decimal? Precio,
    DateOnly? Fecha, string? TipoPublicacion, string? Modalidad, string? MedioPago);

/// <summary>Fase 2: plataformas, tarifas con vigencia, objetivos de rentabilidad, situación fiscal y motor de precios.</summary>
public static class PreciosApi
{
    private static readonly string[] Tipos = ["marketplace", "local", "fisica"];
    private static readonly string[] Esquemas = ["margen", "markup", "utilidad_fija"];
    private static readonly string[] Redondeos = ["terminacion9", "diez", "peso", "centavo"];
    public static readonly string[] Regimenes = ["pendiente", "resico", "plataformas_tecnologicas", "actividad_empresarial", "persona_moral"];
    private static DateOnly Hoy => DateOnly.FromDateTime(DateTime.Now);
    private static string T(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();
    private static string? N(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : T(s, max);
    private static IResult Mal(string m) => Results.BadRequest(new Error(m));

    public static void MapPrecios(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---------- Plataformas ----------
        api.MapGet("/plataformas", async (AppDbContext db) =>
        {
            var pls = await db.Plataformas.AsNoTracking().OrderBy(p => p.Orden).ThenBy(p => p.Nombre).ToListAsync();
            var tarifas = await db.Tarifas.AsNoTracking().ToListAsync();
            var hoy = Hoy;
            return Results.Ok(new
            {
                plataformas = pls.Select(p => new
                {
                    plataforma = p,
                    tarifasVigentes = tarifas.Count(t => t.PlataformaId == p.Id && t.Activa && t.VigenteDesde <= hoy && (t.VigenteHasta is null || t.VigenteHasta >= hoy)),
                    tarifasTotal = tarifas.Count(t => t.PlataformaId == p.Id),
                }),
                opciones = new
                {
                    tiposPublicacion = tarifas.Select(t => t.TipoPublicacion).Where(x => x is not null).Distinct().Order(),
                    modalidades = tarifas.Select(t => t.Modalidad).Where(x => x is not null).Distinct().Order(),
                    mediosPago = tarifas.Select(t => t.MedioPago).Where(x => x is not null).Distinct().Order(),
                },
            });
        });
        api.MapPost("/plataformas", (PlataformaIn i, AppDbContext db) => Api.Bloqueado(() => GuardarPlataforma(db, null, i)));
        api.MapPut("/plataformas/{id}", (string id, PlataformaIn i, AppDbContext db) => Api.Bloqueado(() => GuardarPlataforma(db, id, i)));

        // ---------- Tarifas ----------
        api.MapGet("/plataformas/{id}/tarifas", async (string id, AppDbContext db) =>
            Results.Ok(await db.Tarifas.AsNoTracking().Where(t => t.PlataformaId == id).OrderByDescending(t => t.VigenteDesde).ThenBy(t => t.Nombre).ToListAsync()));
        api.MapPost("/tarifas", (TarifaIn i, AppDbContext db) => Api.Bloqueado(() => GuardarTarifa(db, null, i, null)));
        api.MapPut("/tarifas/{id}", (string id, TarifaIn i, AppDbContext db) => Api.Bloqueado(() => GuardarTarifa(db, id, i, null)));
        // Nueva vigencia: cierra la tarifa actual el día anterior y crea la nueva. La anterior no se modifica en sus montos.
        api.MapPost("/tarifas/{id}/nueva-vigencia", (string id, TarifaIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var vieja = await db.Tarifas.FindAsync(id);
            if (vieja is null) return Results.NotFound(new Error("La tarifa ya no existe."));
            if (i.VigenteDesde is not { } desde || desde <= vieja.VigenteDesde)
                return Mal($"La nueva vigencia debe iniciar después del {vieja.VigenteDesde:dd/MM/yyyy}.");
            if (vieja.VigenteHasta is { } h && desde > h.AddDays(1)) return Mal("La tarifa ya terminó antes de esa fecha; captura una tarifa nueva.");
            vieja.VigenteHasta = desde.AddDays(-1);
            vieja.ActualizadoUtc = DateTime.UtcNow;
            return await GuardarTarifa(db, null, i with { PlataformaId = vieja.PlataformaId, VigenteHasta = null }, vieja.Id);
        }));
        api.MapDelete("/tarifas/{id}", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var t = await db.Tarifas.FindAsync(id);
            if (t is null) return Results.NotFound(new Error("La tarifa ya no existe."));
            if (await db.PreciosProducto.AnyAsync(p => p.TarifaId == id))
                return Results.Conflict(new Error("Hay precios guardados con esta tarifa; no se borra para conservar el historial. Desactívala o ciérrale la vigencia."));
            db.Tarifas.Remove(t);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Objetivos de rentabilidad ----------
        api.MapGet("/parametros-rentabilidad", async (AppDbContext db) =>
        {
            var ps = await db.Parametros.AsNoTracking().ToListAsync();
            var prods = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Codigo + " · " + p.Nombre);
            var pls = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            return Results.Ok(ps.Select(p => new
            {
                parametro = p, ambito = Rentabilidad.Ambito(p),
                producto = p.ProductoId is null ? null : prods.GetValueOrDefault(p.ProductoId),
                plataforma = p.PlataformaId is null ? null : pls.GetValueOrDefault(p.PlataformaId),
            }).OrderBy(x => x.parametro.ProductoId is not null ? 3 : x.parametro.Categoria is not null ? 2 : x.parametro.PlataformaId is not null ? 1 : 0));
        });
        api.MapPost("/parametros-rentabilidad", (ParametroIn i, AppDbContext db) => Api.Bloqueado(() => GuardarParametro(db, null, i)));
        api.MapPut("/parametros-rentabilidad/{id}", (string id, ParametroIn i, AppDbContext db) => Api.Bloqueado(() => GuardarParametro(db, id, i)));
        api.MapDelete("/parametros-rentabilidad/{id}", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var p = await db.Parametros.FindAsync(id);
            if (p is null) return Results.NotFound(new Error("El objetivo ya no existe."));
            if (p.Categoria is null && p.ProductoId is null && p.PlataformaId is null) return Mal("El objetivo general no se puede borrar; modifícalo.");
            db.Parametros.Remove(p);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Situación fiscal ----------
        api.MapGet("/fiscal", async (AppDbContext db) => Results.Ok(await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstAsync()));
        api.MapPut("/fiscal", (FiscalIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            if (!Regimenes.Contains(i.Regimen)) return Mal("Régimen no válido.");
            if (i.TasaIvaPct is < 0 or > 100) return Mal("Tasa de IVA no válida.");
            var f = await db.Fiscal.OrderBy(x => x.Id).FirstAsync();
            f.Regimen = i.Regimen!;
            f.CobraIva = i.CobraIva;
            f.TasaIvaPct = i.TasaIvaPct;
            f.Notas = T(i.Notas, 500);
            f.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(f);
        }));

        // ---------- Rentabilidad ----------
        api.MapGet("/rentabilidad/productos", async (AppDbContext db, DateOnly? fecha, string? q, string? categoria, string? palletId,
            string? estado, string? orden, bool? desc, int? pagina, int? tam) =>
        {
            var svc = new Rentabilidad(db);
            var prods = await db.Productos.AsNoTracking().ToListAsync();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim().ToLowerInvariant();
                prods = prods.Where(p => $"{p.Codigo} {p.CodigoBarras} {p.Nombre} {p.Marca} {p.Modelo}".ToLowerInvariant().Contains(t)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(categoria)) prods = prods.Where(p => string.Equals(p.Categoria, categoria, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrWhiteSpace(palletId))
            {
                var ids = await db.Lotes.Where(l => l.PalletId == palletId).Select(l => l.ProductoId).Distinct().ToListAsync();
                prods = prods.Where(p => ids.Contains(p.Id)).ToList();
            }
            var filas = new List<Rentabilidad.ResultadoProducto>();
            foreach (var p in prods) filas.Add(await svc.ProductoAsync(p, fecha ?? Hoy));
            if (!string.IsNullOrWhiteSpace(estado)) filas = filas.Where(f => f.Estado == estado).ToList();
            decimal? UtilMejor(Rentabilidad.ResultadoProducto f) => f.Canales.FirstOrDefault(c => c.PlataformaId == f.MejorCanal)?.AlReferencia?.Utilidad;
            decimal? MargenMejor(Rentabilidad.ResultadoProducto f) => f.Canales.FirstOrDefault(c => c.PlataformaId == f.MejorCanal)?.AlReferencia?.MargenPct;
            Func<Rentabilidad.ResultadoProducto, object?> clave = orden switch
            {
                "costo" => f => f.Costo.Unitario,
                "utilidad" => f => UtilMejor(f),
                "margen" => f => MargenMejor(f),
                _ => f => f.Producto.Codigo,
            };
            var ordenadas = (desc ?? false) ? filas.OrderByDescending(clave).ToList() : filas.OrderBy(clave).ToList();
            var t2 = Math.Clamp(tam ?? 25, 5, 200);
            var pg = Math.Max(1, pagina ?? 1);
            return Results.Ok(new
            {
                total = ordenadas.Count, pagina = pg, tam = t2,
                resumen = new
                {
                    rentables = filas.Count(f => f.Estado == "rentable"),
                    margenBajo = filas.Count(f => f.Estado is "margen_bajo" or "bajo_minimo"),
                    perdida = filas.Count(f => f.Estado is "perdida" or "no_rentable"),
                    sinDatos = filas.Count(f => f.Estado is "sin_costo" or "sin_tarifa" or "sin_objetivo"),
                },
                fiscalPendiente = (await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstAsync()).Regimen == "pendiente",
                filas = ordenadas.Skip((pg - 1) * t2).Take(t2),
            });
        });

        api.MapGet("/rentabilidad/productos/{id}", async (string id, AppDbContext db, DateOnly? fecha, string? tipoPublicacion, string? modalidad, string? medioPago) =>
        {
            var p = await db.Productos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound(new Error("El juguete no existe."));
            return Results.Ok(await new Rentabilidad(db).ProductoAsync(p, fecha ?? Hoy, null, null, N(tipoPublicacion, 40), N(modalidad, 40), N(medioPago, 40)));
        });

        // Simulador: juguete existente o costo capturado, una plataforma y, opcionalmente, un precio.
        api.MapPost("/rentabilidad/simular", async (SimularIn i, AppDbContext db) =>
        {
            if (string.IsNullOrEmpty(i.PlataformaId) || await db.Plataformas.FindAsync(i.PlataformaId) is null) return Mal("Elige la plataforma.");
            if (i.Precio is < 0 || i.Costo is < 0) return Mal("El precio y el costo no pueden ser negativos.");
            Producto p;
            decimal? costoManual = null;
            if (!string.IsNullOrEmpty(i.ProductoId))
            {
                var encontrado = await db.Productos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == i.ProductoId);
                if (encontrado is null) return Results.NotFound(new Error("El juguete no existe."));
                p = encontrado;
                if (i.Costo is not null) costoManual = i.Costo;
                if (i.PesoKg is not null) p.PesoKg = i.PesoKg;
            }
            else
            {
                if (i.Costo is null) return Mal("Elige un juguete o captura un costo.");
                p = new Producto { Id = "simulacion", Codigo = "—", Nombre = "Simulación", Categoria = T(i.Categoria, 60), PesoKg = i.PesoKg };
                costoManual = i.Costo;
            }
            var fecha = i.Fecha ?? Hoy;
            var svc = new Rentabilidad(db);
            var r = await svc.ProductoAsync(p, fecha, i.PlataformaId, costoManual, N(i.TipoPublicacion, 40), N(i.Modalidad, 40), N(i.MedioPago, 40));
            var canal = r.Canales.Single();
            object? alPrecio = null;
            if (i.Precio is { } precio && canal.Ok && r.Costo.Unitario is { } cu)
            {
                var c = await db.Tarifas.AsNoTracking().Where(t => t.PlataformaId == i.PlataformaId && t.Activa).ToListAsync();
                var fiscal = Rentabilidad.Fiscal(await db.Fiscal.AsNoTracking().OrderBy(x => x.Id).FirstAsync());
                var lado = new[] { p.LargoCm, p.AnchoCm, p.AltoCm }.Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty().Max();
                var cand = SelectorTarifa.Candidatas(c.Select(Rentabilidad.Regla),
                    new ContextoTarifa(fecha, N(i.TipoPublicacion, 40), N(i.Modalidad, 40), string.IsNullOrWhiteSpace(p.Categoria) ? null : p.Categoria, N(i.MedioPago, 40), p.PesoKg, lado > 0 ? lado : null), out _);
                var regla = SelectorTarifa.ParaPrecio(cand, precio);
                if (regla is not null)
                {
                    var par = Rentabilidad.ParametroPara(await db.Parametros.AsNoTracking().Where(x => x.Activo).ToListAsync(), p, i.PlataformaId!)!;
                    var obj = Rentabilidad.Objetivo(par);
                    var pr = MotorPrecios.Calcular(cu, regla.Componentes, fiscal, obj);
                    alPrecio = new
                    {
                        tarifa = c.First(t => t.Id == regla.Id).Nombre,
                        desglose = MotorPrecios.Evaluar(precio, cu, regla.Componentes, fiscal),
                        alertas = MotorPrecios.Alertas(precio, cu, regla.Componentes, fiscal, canal.Equilibrio is null ? pr : new PreciosCanal(true, null,
                            canal.Equilibrio, canal.Minimo, canal.Objetivo, canal.Sugerido, canal.Promocional, canal.PromocionalLimitado, canal.AlSugerido),
                            obj, p.PrecioMexico > 0 ? p.PrecioMexico : null),
                    };
                }
                else alPrecio = new { error = "Ninguna tarifa vigente cubre ese precio." };
            }
            return Results.Ok(new { resultado = r, canal, alPrecio });
        });

        // Guarda los precios calculados como recomendados (historial; uno vigente por juguete y plataforma).
        api.MapPost("/rentabilidad/productos/{id}/guardar", (string id, AppDbContext db, DateOnly? fecha) => Api.Bloqueado(async () =>
        {
            var p = await db.Productos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound(new Error("El juguete no existe."));
            var f = fecha ?? Hoy;
            var r = await new Rentabilidad(db).ProductoAsync(p, f);
            var ok = r.Canales.Where(c => c.Ok).ToList();
            if (ok.Count == 0) return Mal("No hay precios calculables para guardar (revisa costo, tarifas y objetivos).");
            foreach (var viejo in await db.PreciosProducto.Where(x => x.ProductoId == id && x.Vigente).ToListAsync()) viejo.Vigente = false;
            foreach (var c in ok)
                db.PreciosProducto.Add(new PrecioProducto
                {
                    Id = Inventario.NuevoId(), ProductoId = id, PlataformaId = c.PlataformaId,
                    TarifaId = c.Tarifa!.Id, ParametroId = c.Parametro!.Id,
                    FechaTarifa = f, Usuario = db.UsuarioActual, Costo = Dinero.Centavos(r.Costo.Unitario!.Value),
                    Equilibrio = c.Equilibrio!.Value, Minimo = c.Minimo!.Value, Objetivo = c.Objetivo!.Value, Sugerido = c.Sugerido!.Value,
                    Promocional = c.Promocional!.Value, Utilidad = c.AlSugerido!.Utilidad, MargenPct = c.AlSugerido.MargenPct, Vigente = true,
                });
            await db.GuardarAsync();
            return Results.Ok(new { guardados = ok.Count });
        }));

        api.MapGet("/rentabilidad/productos/{id}/historial", async (string id, AppDbContext db) =>
        {
            var pls = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            var h = await db.PreciosProducto.AsNoTracking().Where(x => x.ProductoId == id).ToListAsync();
            return Results.Ok(h.OrderByDescending(x => x.CreadoUtc).Select(x => new { precio = x, plataforma = pls.GetValueOrDefault(x.PlataformaId) }));
        });
    }

    private static async Task<IResult> GuardarPlataforma(AppDbContext db, string? id, PlataformaIn i)
    {
        var nombre = T(i.Nombre, 60);
        if (nombre.Length < 2) return Mal("Escribe el nombre de la plataforma.");
        var tipo = i.Tipo ?? "marketplace";
        if (!Tipos.Contains(tipo)) return Mal("Tipo no válido (marketplace, local o física).");
        if (await db.Plataformas.AnyAsync(p => p.Id != id && p.Nombre == nombre)) return Results.Conflict(new Error($"Ya existe la plataforma {nombre}."));
        Plataforma? p;
        if (id is null)
        {
            var codigo = Codigo(nombre);
            var n = 1;
            while (await db.Plataformas.AnyAsync(x => x.Codigo == codigo)) codigo = Codigo(nombre) + "_" + ++n;
            p = new Plataforma { Id = Inventario.NuevoId(), Codigo = codigo, Orden = (await db.Plataformas.MaxAsync(x => (int?)x.Orden) ?? 0) + 1 };
            db.Plataformas.Add(p);
        }
        else if ((p = await db.Plataformas.FindAsync(id)) is null) return Results.NotFound(new Error("La plataforma no existe."));
        p.Nombre = nombre;
        p.Tipo = tipo;
        p.Activa = i.Activa;
        if (i.Orden is not null) p.Orden = i.Orden.Value;
        p.Notas = T(i.Notas, 300);
        p.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return Results.Ok(p);
    }

    private static string Codigo(string nombre)
    {
        var s = nombre.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in s) if (char.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Regex.Replace(sb.ToString().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_') is var c && c.Length > 0 ? (c.Length > 34 ? c[..34] : c) : "plataforma";
    }

    private static string? ValidarTarifa(TarifaIn i)
    {
        decimal[] pct = [i.ComisionPct, i.ComisionMedioPagoPct, i.PublicidadPct, i.ReservaDevolucionesPct, i.RetencionIsrPct, i.RetencionIvaPct];
        decimal[] montos = [i.CargoFijo, i.CostoLogistico, i.EnvioAbsorbido, i.Almacenamiento, i.Preparacion, i.Empaque, i.PublicidadFija];
        if (pct.Any(x => x is < 0 or > 100)) return "Los porcentajes deben estar entre 0 y 100.";
        if (montos.Any(x => x < 0)) return "Los cargos no pueden ser negativos.";
        if (i.ComisionPct + i.ComisionMedioPagoPct + i.PublicidadPct + i.ReservaDevolucionesPct >= 100) return "Los cargos porcentuales suman 100 % o más.";
        if (i.PrecioMin is < 0 || i.PrecioMax is < 0 || i.PesoMinKg is < 0 || i.PesoMaxKg is < 0 || i.LadoMaxCm is <= 0) return "Los rangos no pueden ser negativos.";
        if (i.PrecioMin > i.PrecioMax) return "El precio mínimo del rango es mayor que el máximo.";
        if (i.PesoMinKg > i.PesoMaxKg) return "El peso mínimo del rango es mayor que el máximo.";
        if (i.VigenteDesde is null) return "Indica desde cuándo es vigente la tarifa.";
        if (i.VigenteHasta < i.VigenteDesde) return "La vigencia termina antes de empezar.";
        return null;
    }

    private static async Task<IResult> GuardarTarifa(AppDbContext db, string? id, TarifaIn i, string? reemplazaA)
    {
        if (ValidarTarifa(i) is { } e) return Mal(e);
        TarifaPlataforma? t;
        if (id is null)
        {
            if (string.IsNullOrEmpty(i.PlataformaId) || await db.Plataformas.FindAsync(i.PlataformaId) is null) return Mal("Elige la plataforma.");
            t = new TarifaPlataforma { Id = Inventario.NuevoId(), PlataformaId = i.PlataformaId!, ReemplazaA = reemplazaA };
            db.Tarifas.Add(t);
        }
        else if ((t = await db.Tarifas.FindAsync(id)) is null) return Results.NotFound(new Error("La tarifa ya no existe."));
        t.Nombre = T(i.Nombre, 80) is { Length: > 0 } n ? n : "Tarifa";
        t.TipoPublicacion = N(i.TipoPublicacion, 40); t.Modalidad = N(i.Modalidad, 40); t.Categoria = N(i.Categoria, 60); t.MedioPago = N(i.MedioPago, 40);
        t.PrecioMin = i.PrecioMin; t.PrecioMax = i.PrecioMax; t.PesoMinKg = i.PesoMinKg; t.PesoMaxKg = i.PesoMaxKg; t.LadoMaxCm = i.LadoMaxCm;
        t.ComisionPct = i.ComisionPct; t.ComisionMedioPagoPct = i.ComisionMedioPagoPct; t.CargoFijo = Dinero.Centavos(i.CargoFijo);
        t.CostoLogistico = Dinero.Centavos(i.CostoLogistico); t.EnvioAbsorbido = Dinero.Centavos(i.EnvioAbsorbido); t.Almacenamiento = Dinero.Centavos(i.Almacenamiento);
        t.Preparacion = Dinero.Centavos(i.Preparacion); t.Empaque = Dinero.Centavos(i.Empaque); t.PublicidadPct = i.PublicidadPct;
        t.PublicidadFija = Dinero.Centavos(i.PublicidadFija); t.ReservaDevolucionesPct = i.ReservaDevolucionesPct;
        t.RetencionIsrPct = i.RetencionIsrPct; t.RetencionIvaPct = i.RetencionIvaPct;
        t.VigenteDesde = i.VigenteDesde!.Value; t.VigenteHasta = i.VigenteHasta; t.Activa = i.Activa; t.Predeterminada = i.Predeterminada;
        t.Notas = T(i.Notas, 500);
        t.ActualizadoUtc = DateTime.UtcNow;

        // Dos tarifas activas con los mismos criterios no pueden traslaparse: el cálculo sería ambiguo.
        var otras = await db.Tarifas.AsNoTracking().Where(x => x.PlataformaId == t.PlataformaId && x.Id != t.Id && x.Activa && x.Id != reemplazaA).ToListAsync();
        if (t.Activa && otras.FirstOrDefault(o => MismosCriterios(o, t) && Traslapan(o, t)) is { } choque)
            return Results.Conflict(new Error($"Choca con la tarifa “{choque.Nombre}” (mismos criterios y vigencias que se traslapan). Usa “Nueva vigencia” o cambia los criterios."));
        await db.GuardarAsync();
        return Results.Ok(t);
    }

    private static bool MismosCriterios(TarifaPlataforma a, TarifaPlataforma b) =>
        string.Equals(a.TipoPublicacion, b.TipoPublicacion, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Modalidad, b.Modalidad, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Categoria, b.Categoria, StringComparison.OrdinalIgnoreCase) && string.Equals(a.MedioPago, b.MedioPago, StringComparison.OrdinalIgnoreCase)
        && a.PrecioMin == b.PrecioMin && a.PrecioMax == b.PrecioMax && a.PesoMinKg == b.PesoMinKg && a.PesoMaxKg == b.PesoMaxKg && a.LadoMaxCm == b.LadoMaxCm;

    private static bool Traslapan(TarifaPlataforma a, TarifaPlataforma b) =>
        a.VigenteDesde <= (b.VigenteHasta ?? DateOnly.MaxValue) && b.VigenteDesde <= (a.VigenteHasta ?? DateOnly.MaxValue);

    private static async Task<IResult> GuardarParametro(AppDbContext db, string? id, ParametroIn i)
    {
        var esq = i.Esquema ?? "margen";
        if (!Esquemas.Contains(esq)) return Mal("Esquema no válido (margen, markup o utilidad fija).");
        var red = i.Redondeo ?? "terminacion9";
        if (!Redondeos.Contains(red)) return Mal("Redondeo no válido.");
        if (i.Valor < 0 || i.MargenMinimoPct < 0 || i.DescuentoPromocionalPct is < 0 or >= 100) return Mal("Los valores no pueden ser negativos y el descuento debe ser menor a 100 %.");
        if (esq == "margen" && i.Valor >= 100) return Mal("El margen debe ser menor a 100 %.");
        if (i.MargenMinimoPct >= 100) return Mal("El margen mínimo debe ser menor a 100 %.");
        if (esq == "margen" && i.MargenMinimoPct > i.Valor) return Mal("El margen mínimo no puede ser mayor que el margen objetivo.");
        if (i.ProductoId is not null && await db.Productos.FindAsync(i.ProductoId) is null) return Mal("El juguete no existe.");
        if (i.PlataformaId is not null && await db.Plataformas.FindAsync(i.PlataformaId) is null) return Mal("La plataforma no existe.");
        var cat = N(i.Categoria, 60);
        if (i.ProductoId is not null) cat = null;   // un objetivo por juguete no depende de la categoría
        ParametroRentabilidad? p;
        if (id is null) { p = new ParametroRentabilidad { Id = Inventario.NuevoId() }; db.Parametros.Add(p); }
        else if ((p = await db.Parametros.FindAsync(id)) is null) return Results.NotFound(new Error("El objetivo ya no existe."));
        var eraGeneral = id is not null && p.Categoria is null && p.ProductoId is null && p.PlataformaId is null;
        if (eraGeneral && (cat is not null || i.ProductoId is not null || i.PlataformaId is not null)) return Mal("El objetivo general no puede cambiar de ámbito.");
        var existentes = await db.Parametros.AsNoTracking().Where(x => x.Id != p.Id).ToListAsync();
        if (existentes.Any(x => string.Equals(x.Categoria, cat, StringComparison.OrdinalIgnoreCase) && x.ProductoId == i.ProductoId && x.PlataformaId == i.PlataformaId))
            return Results.Conflict(new Error("Ya existe un objetivo para ese mismo ámbito; edítalo."));
        p.Categoria = cat; p.ProductoId = i.ProductoId; p.PlataformaId = i.PlataformaId;
        p.Esquema = esq; p.Valor = i.Valor; p.MargenMinimoPct = i.MargenMinimoPct; p.DescuentoPromocionalPct = i.DescuentoPromocionalPct;
        p.Redondeo = red; p.Activo = eraGeneral || i.Activo; p.Notas = T(i.Notas, 300); p.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return Results.Ok(p);
    }
}
