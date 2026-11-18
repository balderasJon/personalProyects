using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Models;
using PalletControl.Servicios;
using PalletControl.Servicios.Integraciones;

namespace PalletControl.Endpoints;

public record CuentaIn(string? Conector, string? PlataformaId, string? Nombre, Dictionary<string, string>? Config, Dictionary<string, string>? Secretos);
public record CodigoIn(string? UrlOCodigo, string? State);
public record AplicarLoteIn(List<string>? Propuestas);
public record VincularIn(string? IdExterno, string? ProductoId);

/// <summary>Fase 5: cuentas conectadas por API oficial, lectura con propuestas y aplicación confirmada.</summary>
public static class IntegracionesApi
{
    private static IResult Mal(string m, int e = 400) => Results.Json(new Error(m), statusCode: e);

    /// <summary>Las credenciales solo se capturan desde la computadora principal (salvo que se permita la configuración remota).</summary>
    private static bool PuedeCapturarSecretos(HttpContext ctx, IConfiguration cfg) =>
        cfg.GetValue("Auth:PermitirConfiguracionRemota", false) || (ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip));

    private static async Task<IResult> Seguro(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (ErrorIntegracion e) { return Mal(e.Message, e.Estado is >= 400 and < 600 ? e.Estado : 502); }
    }

    private static object Vista(IntegracionCuenta c, ServicioIntegraciones s, IReadOnlyDictionary<string, string> plats) => new
    {
        c.Id, c.Conector, c.PlataformaId, plataforma = plats.GetValueOrDefault(c.PlataformaId), c.Nombre, c.Estado, c.CuentaExterna,
        config = ServicioIntegraciones.Config(c),
        // Nunca se devuelven los valores: solo qué secretos están capturados.
        secretosCapturados = SecretosSeguros(s, c),
        c.TokenExpiraUtc, c.UltimaLecturaUtc, c.UltimoError, c.ActualizadoUtc,
    };
    private static List<string> SecretosSeguros(ServicioIntegraciones s, IntegracionCuenta c)
    {
        try { return s.Secretos(c).Keys.Where(k => k != "pkceVerifier").OrderBy(k => k).ToList(); }
        catch (ErrorNegocio) { return ["(ilegible: vuelve a capturar)"]; }
    }

    public static void MapIntegraciones(this WebApplication app)
    {
        var api = app.MapGroup("/api/integraciones").RequireAuthorization();

        api.MapGet("", async (AppDbContext db, ServicioIntegraciones s, HttpContext ctx) =>
        {
            var plats = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
            var codigos = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Codigo, p => p.Id);
            var cuentas = await db.IntegracionCuentas.AsNoTracking().OrderBy(c => c.CreadoUtc).ToListAsync();
            var conectores = s.Todos.Select(c => (object)new
            {
                c.Codigo, c.Nombre, disponible = true, c.UsaOAuth, c.LeePedidos, c.Documentacion, c.Capacidades, c.Campos,
                plataformaSugeridaId = codigos.GetValueOrDefault(c.PlataformaSugerida),
            }).Append(new
            {
                Codigo = "amazon", Nombre = "Amazon México (SP-API)", disponible = false, UsaOAuth = false, LeePedidos = false,
                Documentacion = "https://developer-docs.amazon.com/sp-api/",
                Capacidades = new[] { new Capacidad("todas", "Integración", false, "No se pudo consultar la documentación oficial durante el desarrollo; no se programó ninguna llamada sin verificarla. Registra ventas y publicaciones a mano.") },
                Campos = Array.Empty<CampoConfig>(), plataformaSugeridaId = codigos.GetValueOrDefault("amazon_mx"),
            });
            var origen = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            return Results.Ok(new
            {
                conectores, cuentas = cuentas.Select(c => Vista(c, s, plats)),
                redirectSugerida = origen + "/api/integraciones/mercadolibre/callback",
                puedeCapturarSecretos = PuedeCapturarSecretos(ctx, app.Configuration),
            });
        });

        api.MapPost("/cuentas", (CuentaIn i, AppDbContext db, ServicioIntegraciones s, HttpContext ctx) => Api.Bloqueado(() => Guardar(db, s, ctx, app.Configuration, null, i)));
        api.MapPut("/cuentas/{id}", (string id, CuentaIn i, AppDbContext db, ServicioIntegraciones s, HttpContext ctx) => Api.Bloqueado(() => Guardar(db, s, ctx, app.Configuration, id, i)));
        api.MapDelete("/cuentas/{id}", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
            // Se conservan las ventas y publicaciones; solo se quitan sus vínculos con la cuenta.
            foreach (var p in await db.Publicaciones.Where(p => p.CuentaIntegracionId == id).ToListAsync()) p.CuentaIntegracionId = null;
            db.IntegracionEventos.RemoveRange(await db.IntegracionEventos.Where(e => e.CuentaId == id).ToListAsync());
            db.IntegracionCuentas.Remove(c);
            await db.GuardarAsync();
            return Results.NoContent();
        }));

        // ---------- Conexión ----------
        api.MapPost("/cuentas/{id}/autorizar", (string id, AppDbContext db, ServicioIntegraciones s) => Api.Bloqueado(() => Seguro(async () =>
        {
            var c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
            return Results.Ok(new { url = await s.IniciarAutorizacionAsync(c) });
        })));
        // Alternativa manual: pega aquí la dirección completa a la que te llevó Mercado Libre (contiene ?code=…&state=…).
        api.MapPost("/cuentas/{id}/codigo", (string id, CodigoIn i, AppDbContext db, ServicioIntegraciones s) => Api.Bloqueado(() => Seguro(async () =>
        {
            var c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
            var (code, state) = LeerCodigo(i.UrlOCodigo, i.State);
            await s.CompletarAutorizacionAsync(c, code ?? "", state);
            return Results.Ok(new { c.Estado, c.CuentaExterna });
        })));
        // Redirect URI opcional (si registras esta dirección en tu aplicación de Mercado Libre).
        api.MapGet("/mercadolibre/callback", (string? code, string? state, AppDbContext db, ServicioIntegraciones s) => Api.Bloqueado(async () =>
        {
            var c = state is null ? null : await db.IntegracionCuentas.FirstOrDefaultAsync(x => x.OAuthState == state && x.Conector == "mercadolibre");
            if (c is null) return Results.Redirect("/?integracion=error#analisis");
            try { await s.CompletarAutorizacionAsync(c, code ?? "", state); return Results.Redirect("/?integracion=ok#analisis"); }
            catch (Exception e) when (e is ErrorNegocio or ErrorIntegracion) { return Results.Redirect("/?integracion=error#analisis"); }
        })).AllowAnonymous();   // la cookie de sesión (SameSite estricto) no viaja en la redirección; el "state" aleatorio de un solo uso protege el flujo
        api.MapPost("/cuentas/{id}/probar", (string id, AppDbContext db, ServicioIntegraciones s) => Api.Bloqueado(() => Seguro(async () =>
        {
            var c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
            var ctx = await s.ContextoAsync(c);
            c.CuentaExterna = await s.Conector(c.Conector).VerificarAsync(ctx);
            c.Estado = "conectada"; c.UltimoError = ""; c.ActualizadoUtc = DateTime.UtcNow;
            await db.GuardarAsync();
            return Results.Ok(new { c.Estado, c.CuentaExterna });
        })));

        // ---------- Anuncios de la plataforma (para vincular) ----------
        api.MapGet("/cuentas/{id}/anuncios", (string id, AppDbContext db, ServicioIntegraciones s) => Api.Bloqueado(() => Seguro(async () =>
        {
            var c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
            var ctx = await s.ContextoAsync(c);
            var remotas = await s.Conector(c.Conector).LeerPublicacionesAsync(ctx, []);
            var vinc = await db.Publicaciones.Where(p => p.PlataformaId == c.PlataformaId && p.IdExterno != "" && p.Estatus != "eliminado")
                .Select(p => new { p.IdExterno, p.ProductoId }).ToListAsync();
            var prods = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Codigo + " · " + p.Nombre);
            await db.GuardarAsync();   // bitácora
            return Results.Ok(remotas.Select(r => new
            {
                anuncio = r, vinculadoA = vinc.Where(v => v.IdExterno == r.IdExterno).Select(v => prods.GetValueOrDefault(v.ProductoId)).FirstOrDefault(),
            }));
        })));
        api.MapPost("/cuentas/{id}/vincular", (string id, VincularIn i, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
            var idExt = (i.IdExterno ?? "").Trim();
            if (idExt.Length is 0 or > 80) return Mal("Indica el identificador del anuncio.");
            if (await db.Productos.FindAsync(i.ProductoId ?? "") is not { } p) return Mal("Elige el juguete.");
            if (await db.Publicaciones.AnyAsync(x => x.PlataformaId == c.PlataformaId && x.IdExterno == idExt && x.Estatus != "eliminado"))
                return Mal("Ese anuncio ya está vinculado.", 409);
            var pub = new Publicacion
            {
                Id = Inventario.NuevoId(), ProductoId = p.Id, PlataformaId = c.PlataformaId, IdExterno = idExt, Estatus = "publicado",
                PrecioPublicado = p.PrecioObjetivo, Existencias = 1, Fuente = c.Conector, CuentaIntegracionId = c.Id, FechaPublicacion = DateOnly.FromDateTime(DateTime.Now),
                Observaciones = "Vinculada desde Integraciones. Revisa el precio publicado.",
            };
            db.Publicaciones.Add(pub);
            await Ventas.RevisarPublicacionesAsync(db, p.Id, null, 0);
            await db.GuardarAsync();
            return Results.Created($"/api/publicaciones/{pub.Id}", pub);
        }));

        // ---------- Lectura y aplicación ----------
        api.MapPost("/cuentas/{id}/leer", (string id, AppDbContext db, ServicioIntegraciones s) => Api.Bloqueado(() => Seguro(async () =>
        {
            var c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
            // Lecturas pendientes anteriores quedan descartadas: solo se aplica la más reciente.
            foreach (var l in await db.SincronizacionLotes.Where(l => l.CuentaId == id && l.Estado == "pendiente").ToListAsync()) l.Estado = "descartado";
            var r = await s.LeerAsync(c);
            return Results.Ok(new { lote = r.Lote, propuestas = r.Propuestas, sinVincular = r.SinVincular, r.Leidas, r.Pedidos });
        })));
        api.MapGet("/cuentas/{id}/lotes", async (string id, AppDbContext db) =>
            Results.Ok(await db.SincronizacionLotes.AsNoTracking().Where(l => l.CuentaId == id).OrderByDescending(l => l.CreadoUtc).Take(30).ToListAsync()));
        api.MapGet("/lotes/{id}", async (string id, AppDbContext db) =>
        {
            var l = await db.SincronizacionLotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (l is null) return Mal("La lectura ya no existe.", 404);
            return Results.Ok(new { lote = l, propuestas = await db.SincronizacionPropuestas.AsNoTracking().Where(p => p.LoteId == id).OrderBy(p => p.Orden).ToListAsync() });
        });
        api.MapPost("/lotes/{id}/aplicar", (string id, AplicarLoteIn i, AppDbContext db, ServicioIntegraciones s) => Api.Bloqueado(() => Seguro(async () =>
        {
            var l = await db.SincronizacionLotes.FindAsync(id);
            if (l is null) return Mal("La lectura ya no existe.", 404);
            if (i.Propuestas is null || i.Propuestas.Count == 0) return Mal("Marca al menos un cambio para aplicar.");
            l = await s.AplicarAsync(l, i.Propuestas.ToHashSet());
            return Results.Ok(new { lote = l, propuestas = await db.SincronizacionPropuestas.AsNoTracking().Where(p => p.LoteId == id).OrderBy(p => p.Orden).ToListAsync() });
        })));
        api.MapPost("/lotes/{id}/descartar", (string id, AppDbContext db) => Api.Bloqueado(async () =>
        {
            var l = await db.SincronizacionLotes.FindAsync(id);
            if (l is null) return Mal("La lectura ya no existe.", 404);
            if (l.Estado != "pendiente") return Mal("Esta lectura ya no está pendiente.", 409);
            l.Estado = "descartado";
            foreach (var p in await db.SincronizacionPropuestas.Where(p => p.LoteId == id && p.Estado == "pendiente").ToListAsync()) p.Estado = "rechazada";
            await db.GuardarAsync();
            return Results.NoContent();
        }));
        api.MapGet("/cuentas/{id}/eventos", async (string id, AppDbContext db) =>
            Results.Ok(await db.IntegracionEventos.AsNoTracking().Where(e => e.CuentaId == id).OrderByDescending(e => e.Id).Take(100).ToListAsync()));
    }

    public static (string? Code, string? State) LeerCodigo(string? texto, string? state)
    {
        var t = (texto ?? "").Trim();
        if (Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Query.Length > 1)
        {
            var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(u.Query);
            return (q.TryGetValue("code", out var c) ? c.ToString() : null, q.TryGetValue("state", out var s) ? s.ToString() : state);
        }
        return (t.Length > 0 ? t : null, state);
    }

    private static async Task<IResult> Guardar(AppDbContext db, ServicioIntegraciones s, HttpContext ctx, IConfiguration cfg, string? id, CuentaIn i)
    {
        IntegracionCuenta? c;
        if (id is null)
        {
            var con = s.Todos.FirstOrDefault(x => x.Codigo == i.Conector);
            if (con is null) return Mal("Elige una plataforma disponible.");
            c = new IntegracionCuenta { Id = Inventario.NuevoId(), Conector = con.Codigo };
            db.IntegracionCuentas.Add(c);
        }
        else
        {
            c = await db.IntegracionCuentas.FindAsync(id);
            if (c is null) return Mal("La cuenta ya no existe.", 404);
        }
        var conector = s.Conector(c.Conector);
        if (string.IsNullOrWhiteSpace(i.PlataformaId) || await db.Plataformas.FindAsync(i.PlataformaId) is null) return Mal("Elige la plataforma del catálogo a la que se asignan las ventas.");
        c.PlataformaId = i.PlataformaId!;
        c.Nombre = (i.Nombre ?? "").Trim() is { Length: > 0 } n ? (n.Length > 80 ? n[..80] : n) : conector.Nombre;
        // Configuración no secreta: solo los campos que declara el conector.
        var config = ServicioIntegraciones.Config(c);
        foreach (var campo in conector.Campos.Where(x => !x.Secreto))
            if (i.Config?.TryGetValue(campo.Clave, out var v) == true) config[campo.Clave] = (v ?? "").Trim() is var t && t.Length > 300 ? t[..300] : (v ?? "").Trim();
        foreach (var campo in conector.Campos.Where(x => !x.Secreto && x.PorDefecto is not null && !config.ContainsKey(x.Clave))) config[campo.Clave] = campo.PorDefecto!;
        if (config.TryGetValue("redirectUri", out var ru) && ru.Length > 0 && !(Uri.TryCreate(ru, UriKind.Absolute, out var uu) && uu.Scheme is "http" or "https"))
            return Mal("La Redirect URI debe ser una dirección completa (https://…).");
        c.ConfigJson = JsonSerializer.Serialize(config);
        // Secretos: vacío = conservar el actual.
        var nuevos = (i.Secretos ?? []).Where(kv => conector.Campos.Any(x => x.Secreto && x.Clave == kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)).ToList();
        if (nuevos.Count > 0)
        {
            if (!PuedeCapturarSecretos(ctx, cfg)) return Mal("Por seguridad, las credenciales solo se capturan desde la computadora principal (http://localhost).", 403);
            var sec = id is null ? [] : s.Secretos(c);
            foreach (var kv in nuevos) sec[kv.Key] = kv.Value.Trim();
            if (conector.UsaOAuth && nuevos.Any(kv => kv.Key == "clientSecret")) { sec.Remove("accessToken"); sec.Remove("refreshToken"); c.TokenExpiraUtc = null; c.Estado = "sin_configurar"; }
            s.GuardarSecretos(c, sec);
        }
        var faltan = conector.Campos.Where(x => x.Requerido && (x.Secreto ? !s.Secretos(c).ContainsKey(x.Clave) : string.IsNullOrWhiteSpace(config.GetValueOrDefault(x.Clave)))).Select(x => x.Etiqueta).ToList();
        if (!conector.UsaOAuth && faltan.Count == 0 && c.Estado == "sin_configurar") c.Estado = "pendiente_autorizacion";
        c.UltimoError = faltan.Count > 0 ? "Falta: " + string.Join(", ", faltan) : c.UltimoError;
        c.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        var plats = await db.Plataformas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nombre);
        return id is null ? Results.Created($"/api/integraciones/cuentas/{c.Id}", Vista(c, s, plats)) : Results.Ok(Vista(c, s, plats));
    }
}
