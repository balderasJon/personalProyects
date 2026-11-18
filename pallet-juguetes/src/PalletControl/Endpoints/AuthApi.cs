using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Models;
using PalletControl.Servicios;

namespace PalletControl.Endpoints;

public record ConfigurarIn(string? Usuario, string? NombreCompleto, string? Contrasena);
public record EntrarIn(string? Usuario, string? Contrasena);
public record CambiarContrasenaIn(string? Actual, string? Nueva);
public record UsuarioIn(string? Usuario, string? NombreCompleto, string? Contrasena);

/// <summary>Inicio de sesión local (decisión 11.2): usuarios propios, cookie de sesión, bloqueo por intentos.</summary>
public static class AuthApi
{
    private const int MaxIntentos = 5;
    private static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (int Fallos, DateTime Hasta)> Intentos = new(StringComparer.OrdinalIgnoreCase);

    public static void MapAuth(this WebApplication app)
    {
        var g = app.MapGroup("/api/auth").AllowAnonymous();

        g.MapGet("/estado", async (HttpContext ctx, AppDbContext db, IConfiguration cfg) =>
        {
            var hay = await db.Usuarios.AnyAsync();
            return Results.Ok(new
            {
                autenticado = ctx.User.Identity?.IsAuthenticated == true,
                usuario = ctx.User.FindFirstValue(ClaimTypes.Name),
                nombreCompleto = ctx.User.FindFirstValue(ClaimTypes.GivenName),
                requiereConfiguracion = !hay,
                configuracionPermitida = !hay && PuedeConfigurar(ctx, cfg),
            });
        });

        // Primer usuario: solo si no hay ninguno y la petición viene de esta computadora.
        g.MapPost("/configurar", async (ConfigurarIn i, HttpContext ctx, AppDbContext db, IConfiguration cfg) =>
        {
            if (await db.Usuarios.AnyAsync()) return Results.Json(new Error("La aplicación ya tiene usuarios. Inicia sesión."), statusCode: 409);
            if (!PuedeConfigurar(ctx, cfg))
                return Results.Json(new Error("El primer usuario solo se puede crear desde la computadora donde corre la aplicación (http://localhost:5080)."), statusCode: 403);
            var (u, err) = await CrearUsuarioAsync(db, i.Usuario, i.NombreCompleto, i.Contrasena);
            if (u is null) return Results.BadRequest(new Error(err!));
            await EntrarAsync(ctx, u);
            return Results.Ok(new { usuario = u.Nombre });
        });

        g.MapPost("/entrar", async (EntrarIn i, HttpContext ctx, AppDbContext db) =>
        {
            var nombre = (i.Usuario ?? "").Trim();
            if (Intentos.TryGetValue(nombre, out var it) && it.Hasta > DateTime.UtcNow)
                return Results.Json(new Error($"Demasiados intentos fallidos. Espera {Math.Ceiling((it.Hasta - DateTime.UtcNow).TotalMinutes)} minuto(s)."), statusCode: 429);
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Nombre == nombre);
            if (u is null || !u.Activo || !Contrasenas.Verificar(i.Contrasena ?? "", u.Hash, u.Sal, u.Iteraciones))
            {
                var f = Intentos.AddOrUpdate(nombre, _ => (1, DateTime.MinValue), (_, v) => (v.Fallos + 1, DateTime.MinValue));
                if (f.Fallos >= MaxIntentos) Intentos[nombre] = (0, DateTime.UtcNow + Bloqueo);
                return Results.Json(new Error("Usuario o contraseña incorrectos."), statusCode: 401);
            }
            Intentos.TryRemove(nombre, out _);
            u.UltimoAccesoUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await EntrarAsync(ctx, u);
            return Results.Ok(new { usuario = u.Nombre });
        });

        g.MapPost("/salir", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        // ---- Requieren sesión ----
        var s = app.MapGroup("/api").RequireAuthorization();

        s.MapPost("/auth/contrasena", async (CambiarContrasenaIn i, HttpContext ctx, AppDbContext db) =>
        {
            var u = await db.Usuarios.FindAsync(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (u is null) return Results.Unauthorized();
            if (!Contrasenas.Verificar(i.Actual ?? "", u.Hash, u.Sal, u.Iteraciones)) return Results.BadRequest(new Error("La contraseña actual no es correcta."));
            if (Contrasenas.Validar(i.Nueva) is { } e) return Results.BadRequest(new Error(e));
            (u.Hash, u.Sal, u.Iteraciones) = Contrasenas.Crear(i.Nueva!);
            await db.SaveChangesAsync();
            await EntrarAsync(ctx, u);   // renueva la sesión actual; las demás quedan invalidadas por el sello
            return Results.NoContent();
        });

        s.MapGet("/usuarios", async (AppDbContext db) =>
            Results.Ok((await db.Usuarios.AsNoTracking().OrderBy(x => x.Nombre).ToListAsync())
                .Select(x => new { x.Id, x.Nombre, x.NombreCompleto, x.Activo, x.CreadoUtc, x.UltimoAccesoUtc })));

        s.MapPost("/usuarios", async (UsuarioIn i, AppDbContext db) =>
        {
            var (u, err) = await CrearUsuarioAsync(db, i.Usuario, i.NombreCompleto, i.Contrasena);
            return u is null ? Results.BadRequest(new Error(err!)) : Results.Ok(new { u.Id, u.Nombre, u.NombreCompleto, u.Activo });
        });

        s.MapPost("/usuarios/{id}/activo", async (string id, bool activo, HttpContext ctx, AppDbContext db) =>
        {
            var u = await db.Usuarios.FindAsync(id);
            if (u is null) return Results.NotFound(new Error("El usuario no existe."));
            if (!activo && u.Id == ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)) return Results.BadRequest(new Error("No puedes desactivar tu propio usuario."));
            if (!activo && !await db.Usuarios.AnyAsync(x => x.Activo && x.Id != id)) return Results.BadRequest(new Error("Debe quedar al menos un usuario activo."));
            u.Activo = activo;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static bool PuedeConfigurar(HttpContext ctx, IConfiguration cfg) =>
        cfg.GetValue("Auth:PermitirConfiguracionRemota", false)
        || (ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip));

    private static async Task<(Usuario?, string?)> CrearUsuarioAsync(AppDbContext db, string? usuario, string? nombreCompleto, string? contrasena)
    {
        var nombre = (usuario ?? "").Trim();
        if (nombre.Length is < 3 or > 40 || !nombre.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            return (null, "El usuario debe tener de 3 a 40 caracteres: letras, números, punto, guion o guion bajo.");
        if (Contrasenas.Validar(contrasena) is { } e) return (null, e);
        if (await db.Usuarios.AnyAsync(x => x.Nombre == nombre)) return (null, $"Ya existe el usuario {nombre}.");
        var (h, s, it) = Contrasenas.Crear(contrasena!);
        var u = new Usuario
        {
            Id = Guid.NewGuid().ToString("N")[..12], Nombre = nombre,
            NombreCompleto = (nombreCompleto ?? "").Trim() is var nc && nc.Length > 80 ? nc[..80] : (nombreCompleto ?? "").Trim(),
            Hash = h, Sal = s, Iteraciones = it,
        };
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        return (u, null);
    }

    private static Task EntrarAsync(HttpContext ctx, Usuario u)
    {
        var id = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, u.Id),
            new Claim(ClaimTypes.Name, u.Nombre),
            new Claim(ClaimTypes.GivenName, u.NombreCompleto),
            new Claim("sello", Contrasenas.Sello(u.Hash)),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
            new AuthenticationProperties { IsPersistent = false });
    }

    /// <summary>En cada petición: el usuario debe seguir activo y con la misma contraseña.</summary>
    public static async Task ValidarSesionAsync(CookieValidatePrincipalContext c)
    {
        var db = c.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        if (u is null || !u.Activo || Contrasenas.Sello(u.Hash) != c.Principal!.FindFirstValue("sello"))
        {
            c.RejectPrincipal();
            await c.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
