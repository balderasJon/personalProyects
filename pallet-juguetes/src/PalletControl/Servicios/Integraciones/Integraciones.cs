using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Dominio.Integracion;
using PalletControl.Models;

namespace PalletControl.Servicios.Integraciones;

/// <summary>
/// Orquesta las integraciones: credenciales cifradas, renovación de tokens, lectura (genera propuestas) y aplicación confirmada.
/// Nada cambia en una plataforma ni en la aplicación sin que el usuario confirme cada propuesta.
/// </summary>
public class ServicioIntegraciones(AppDbContext db, IDataProtectionProvider dp, IHttpClientFactory hf, IConfiguration cfg, IEnumerable<IConector> conectores)
{
    private readonly IDataProtector protector = dp.CreateProtector("PalletControl.Integraciones.v1");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IConector Conector(string codigo) => conectores.FirstOrDefault(c => c.Codigo == codigo) ?? throw new ErrorNegocio("Conector no disponible.", 404);
    public IEnumerable<IConector> Todos => conectores;

    // ---------- Secretos y configuración ----------
    public Dictionary<string, string> Secretos(IntegracionCuenta c)
    {
        if (string.IsNullOrEmpty(c.SecretosCifrados)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(c.SecretosCifrados)) ?? []; }
        catch (CryptographicException) { throw new ErrorNegocio("No se pudieron leer las credenciales guardadas (¿cambió la carpeta de datos o sus claves?). Vuelve a capturarlas.", 409); }
    }
    public void GuardarSecretos(IntegracionCuenta c, Dictionary<string, string> s) =>
        c.SecretosCifrados = s.Count == 0 ? "" : protector.Protect(JsonSerializer.Serialize(s));
    public static Dictionary<string, string> Config(IntegracionCuenta c) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(c.ConfigJson) ?? [];

    private Dictionary<string, string> ConfigConUrls(IntegracionCuenta c)
    {
        var d = Config(c);
        // Direcciones base oficiales; configurables (solo para pruebas).
        d["_api"] = cfg["Integraciones:MercadoLibre:Api"] ?? "https://api.mercadolibre.com";
        d["_auth"] = cfg["Integraciones:MercadoLibre:Auth"] ?? "https://auth.mercadolibre.com.mx";
        d["_graph"] = cfg["Integraciones:Meta:Graph"] ?? "https://graph.facebook.com";
        return d;
    }

    private ContextoConector Ctx(IntegracionCuenta c, Dictionary<string, string>? secretos = null) => new()
    {
        Http = hf.CreateClient("integraciones"), Config = ConfigConUrls(c), Secretos = secretos ?? Secretos(c), CuentaExterna = c.CuentaExterna,
        Registrar = (nivel, op, detalle) =>
        {
            db.IntegracionEventos.Add(new IntegracionEvento { CuentaId = c.Id, Nivel = nivel, Operacion = Recortar(op, 60), Detalle = Recortar(detalle, 1000), Usuario = db.UsuarioActual });
            return Task.CompletedTask;
        },
    };
    private static string Recortar(string s, int n) => s.Length > n ? s[..n] : s;

    /// <summary>Contexto listo para llamar: renueva el token de acceso si vence en menos de 5 minutos (y guarda el nuevo refresh token de inmediato).</summary>
    public async Task<ContextoConector> ContextoAsync(IntegracionCuenta c)
    {
        var con = Conector(c.Conector);
        var s = Secretos(c);
        if (con.UsaOAuth)
        {
            if (!s.ContainsKey("refreshToken") && !s.ContainsKey("accessToken")) throw new ErrorNegocio("La cuenta aún no está autorizada: usa «Conectar».", 409);
            if (c.TokenExpiraUtc is null || c.TokenExpiraUtc < DateTime.UtcNow.AddMinutes(5))
            {
                try
                {
                    var t = await con.RefrescarAsync(Ctx(c, s));
                    AplicarTokens(c, s, t);
                }
                catch (ErrorIntegracion e)
                {
                    c.Estado = "error"; c.UltimoError = Recortar(e.Message, 500);
                    await db.GuardarAsync();
                    throw;
                }
                await db.GuardarAsync();   // el refresh token anterior ya no sirve: el nuevo se guarda antes de seguir
            }
        }
        return Ctx(c, s);
    }

    public void AplicarTokens(IntegracionCuenta c, Dictionary<string, string> s, Tokens t)
    {
        s["accessToken"] = t.AccessToken;
        if (t.RefreshToken is not null) s["refreshToken"] = t.RefreshToken;
        GuardarSecretos(c, s);
        c.TokenExpiraUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, t.ExpiraSegundos));
        if (t.CuentaExterna is not null) c.CuentaExterna = t.CuentaExterna;
        c.Estado = "conectada"; c.UltimoError = ""; c.ActualizadoUtc = DateTime.UtcNow;
    }

    // ---------- OAuth (Mercado Libre) ----------
    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task<string> IniciarAutorizacionAsync(IntegracionCuenta c)
    {
        var con = Conector(c.Conector);
        if (!con.UsaOAuth) throw new ErrorNegocio("Esta plataforma no usa autorización OAuth: captura el token.", 400);
        var cfgC = Config(c);
        if (string.IsNullOrWhiteSpace(cfgC.GetValueOrDefault("clientId")) || string.IsNullOrWhiteSpace(cfgC.GetValueOrDefault("redirectUri")))
            throw new ErrorNegocio("Captura el Client ID y la Redirect URI de tu aplicación.", 400);
        var s = Secretos(c);
        if (!s.ContainsKey("clientSecret")) throw new ErrorNegocio("Captura el Client Secret.", 400);
        c.OAuthState = Base64Url(RandomNumberGenerator.GetBytes(24));
        string? challenge = null;
        if (cfgC.GetValueOrDefault("pkce", "no").Trim().ToLowerInvariant() is "sí" or "si" or "yes" or "true")
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            s["pkceVerifier"] = verifier;
            challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        }
        else s.Remove("pkceVerifier");
        GuardarSecretos(c, s);
        c.Estado = "pendiente_autorizacion"; c.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return con.UrlAutorizacion(Ctx(c, s), c.OAuthState, challenge)!;
    }

    public async Task CompletarAutorizacionAsync(IntegracionCuenta c, string code, string? state)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ErrorNegocio("No llegó el código de autorización.", 400);
        if (c.OAuthState is null || state is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(c.OAuthState), Encoding.UTF8.GetBytes(state)))
            throw new ErrorNegocio("La autorización no corresponde a la solicitud iniciada desde esta aplicación (state). Vuelve a usar «Conectar».", 400);
        var s = Secretos(c);
        var t = await Conector(c.Conector).IntercambiarCodigoAsync(Ctx(c, s), code.Trim(), s.GetValueOrDefault("pkceVerifier"));
        s.Remove("pkceVerifier");
        c.OAuthState = null;
        AplicarTokens(c, s, t);
        await db.GuardarAsync();
    }

    // ---------- Lectura: genera propuestas ----------
    public record Resultado(SincronizacionLote Lote, List<SincronizacionPropuesta> Propuestas, List<PublicacionRemota> SinVincular, int Leidas, int Pedidos);

    public async Task<Resultado> LeerAsync(IntegracionCuenta c)
    {
        var con = Conector(c.Conector);
        var ctx = await ContextoAsync(c);
        if (string.IsNullOrEmpty(c.CuentaExterna)) { c.CuentaExterna = await con.VerificarAsync(ctx); ctx = Ctx(c); }

        // Publicaciones vinculadas: las de la plataforma de la cuenta con identificador externo.
        var pubs = await db.Publicaciones.Where(p => p.PlataformaId == c.PlataformaId && p.IdExterno != "" && p.Estatus != "eliminado").ToListAsync();
        // Meta devuelve el catálogo completo; Mercado Libre solo los anuncios vinculados (sin vínculos no se consulta nada).
        var remotas = con.Codigo == "meta" ? await con.LeerPublicacionesAsync(ctx, [])
            : pubs.Count == 0 ? [] : await con.LeerPublicacionesAsync(ctx, pubs.Select(p => p.IdExterno).ToList());
        var porId = remotas.GroupBy(r => r.IdExterno).ToDictionary(g => g.Key, g => g.First());
        var locales = new List<PublicacionLocal>();
        foreach (var p in pubs)
        {
            p.CuentaIntegracionId = c.Id;
            if (porId.TryGetValue(p.IdExterno, out var r))
            {
                p.EstatusRemoto = r.EstatusOriginal.Length > 0 ? r.EstatusOriginal : r.Estatus;
                p.ExistenciasRemotas = r.Existencias; p.PrecioRemoto = r.Precio; p.SincronizadoUtc = DateTime.UtcNow;
            }
            locales.Add(new PublicacionLocal(p.Id, p.ProductoId, p.IdExterno, p.Estatus, p.PrecioPublicado, await Ventas.DisponibleVentaAsync(db, p.ProductoId)));
        }
        var cambios = Planificador.Publicaciones(locales, porId, con.Escritura);

        var pedidos = 0;
        if (con.LeePedidos)
        {
            var importadas = await db.Ventas.Where(v => v.CuentaIntegracionId == c.Id && v.PedidoExterno != null).ToListAsync();
            var conocidos = importadas.Where(v => v.Estado != "cancelado").Select(v => v.PedidoExterno!.Split('/')[0]).Distinct().ToList();
            var desde = DateOnly.FromDateTime((c.UltimaLecturaUtc ?? DateTime.UtcNow.AddDays(-30)).AddDays(-7));
            var remotos = await con.LeerPedidosAsync(ctx, desde, conocidos);
            pedidos = remotos.Count;
            var vinculos = pubs.Where(p => p.Estatus is not ("eliminado" or "cancelado")).GroupBy(p => p.IdExterno).ToDictionary(g => g.Key, g => (g.First().Id, g.First().ProductoId));
            cambios.AddRange(Planificador.Pedidos(remotos,
                importadas.Select(v => new VentaImportada(v.Id, v.PedidoExterno!, v.Estado, v.ImporteCobrado is not null)).ToList(), vinculos));
        }

        var vinculados = pubs.Select(p => p.IdExterno).ToHashSet();
        var sinVincular = con.Codigo == "meta" ? remotas.Where(r => !vinculados.Contains(r.IdExterno)).ToList() : [];
        var lote = new SincronizacionLote
        {
            Id = Inventario.NuevoId(), CuentaId = c.Id, Usuario = db.UsuarioActual,
            Resumen = Recortar($"{remotas.Count} anuncio(s) leídos, {pedidos} pedido(s); {cambios.Count(x => x.Bloqueo is null)} propuesta(s), {cambios.Count(x => x.Bloqueo is not null)} para revisar a mano.", 500),
            Estado = cambios.Any(x => x.Bloqueo is null) ? "pendiente" : "aplicado",
        };
        db.SincronizacionLotes.Add(lote);
        var props = cambios.Select((x, i) => new SincronizacionPropuesta
        {
            Id = Inventario.NuevoId(), LoteId = lote.Id, Orden = i, Tipo = x.Tipo, Destino = x.Destino, PublicacionId = x.PublicacionId, VentaId = x.VentaId,
            ReferenciaExterna = Recortar(x.Referencia, 80), Descripcion = Recortar(x.Descripcion, 500), Antes = Recortar(x.Antes, 200), Despues = Recortar(x.Despues, 200),
            DatosJson = JsonSerializer.Serialize(x, Json), Estado = x.Bloqueo is null ? "pendiente" : "bloqueada",
            Resultado = x.Bloqueo is null ? "" : x.Bloqueo,
        }).ToList();
        db.SincronizacionPropuestas.AddRange(props);
        c.UltimaLecturaUtc = DateTime.UtcNow; c.Estado = "conectada"; c.UltimoError = ""; c.ActualizadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return new Resultado(lote, props, sinVincular, remotas.Count, pedidos);
    }

    // ---------- Aplicación confirmada ----------
    public async Task<SincronizacionLote> AplicarAsync(SincronizacionLote lote, IReadOnlyCollection<string> elegidas)
    {
        if (lote.Estado != "pendiente") throw new ErrorNegocio("Esta lectura ya se aplicó o se descartó. Vuelve a leer la plataforma.", 409);
        var c = await db.IntegracionCuentas.FindAsync(lote.CuentaId) ?? throw new ErrorNegocio("La cuenta ya no existe.", 404);
        var con = Conector(c.Conector);
        var ctx = await ContextoAsync(c);
        var ids = await db.SincronizacionPropuestas.Where(p => p.LoteId == lote.Id).OrderBy(p => p.Orden).Select(p => p.Id).ToListAsync();
        int ok = 0, err = 0;
        foreach (var pid in ids)
        {
            var p = (await db.SincronizacionPropuestas.FindAsync(pid))!;
            if (p.Estado != "pendiente") continue;
            if (!elegidas.Contains(p.Id)) { p.Estado = "rechazada"; await db.GuardarAsync(); continue; }
            var x = JsonSerializer.Deserialize<CambioPropuesto>(p.DatosJson, Json)!;
            try
            {
                if (p.Destino == "plataforma") await AplicarEnPlataformaAsync(con, ctx, x);
                else await AplicarEnAplicacionAsync(c, x);
                p.Estado = "aplicada"; p.Resultado = "Listo"; p.AplicadaUtc = DateTime.UtcNow;
                await db.GuardarAsync();
                ok++;
            }
            catch (Exception e) when (e is ErrorNegocio or ErrorIntegracion)
            {
                // Se descartan los cambios a medias de esta propuesta y se registra el error; las demás siguen.
                db.ChangeTracker.Clear();
                p = (await db.SincronizacionPropuestas.FindAsync(pid))!;
                p.Estado = "error"; p.Resultado = Recortar(e.Message, 500);
                db.IntegracionEventos.Add(new IntegracionEvento { CuentaId = c.Id, Nivel = "error", Operacion = "aplicar " + p.Tipo, Detalle = Recortar($"{p.ReferenciaExterna}: {e.Message}", 1000), Usuario = db.UsuarioActual });
                await db.GuardarAsync();
                c = (await db.IntegracionCuentas.FindAsync(lote.CuentaId))!;
                err++;
            }
        }
        lote = (await db.SincronizacionLotes.FindAsync(lote.Id))!;
        lote.Estado = err == 0 ? "aplicado" : ok == 0 ? "error" : "parcial";
        lote.AplicadoUtc = DateTime.UtcNow;
        await db.GuardarAsync();
        return lote;
    }

    private async Task AplicarEnPlataformaAsync(IConector con, ContextoConector ctx, CambioPropuesto x)
    {
        await con.AplicarAsync(ctx, x);
        if (x.PublicacionId is null || await db.Publicaciones.FindAsync(x.PublicacionId) is not { } pub) return;
        switch (x.Tipo)
        {
            case "pausar": if (pub.Estatus == "publicado") pub.Estatus = "pausado"; pub.EstatusRemoto = "paused"; break;
            case "activar": pub.Estatus = "publicado"; pub.EstatusRemoto = "active"; break;
            case "existencias": pub.Existencias = x.Existencias ?? pub.Existencias; pub.ExistenciasRemotas = x.Existencias; break;
            case "precio": pub.PrecioRemoto = x.Precio; break;
        }
        pub.RequiereAccion = ""; pub.SincronizadoUtc = DateTime.UtcNow; pub.ActualizadoUtc = DateTime.UtcNow;
    }

    private async Task AplicarEnAplicacionAsync(IntegracionCuenta c, CambioPropuesto x)
    {
        switch (x.Tipo)
        {
            case "nueva_venta":
                if (await db.Ventas.AnyAsync(v => v.PedidoExterno == x.Referencia && v.CuentaIntegracionId == c.Id))
                    throw new ErrorNegocio("Ese pedido ya se importó.", 409);
                var gastos = (x.Gastos ?? []).Select(g => new GastoVentaIn(g.Tipo, g.Concepto, g.Monto, g.DeducidoPorPlataforma, g.Estimado)).ToList();
                var v = await Ventas.RegistrarAsync(db, new VentaDatos(x.ProductoId!, x.Cantidad, x.Precio ?? 0m, "pagado", x.Fecha ?? DateOnly.FromDateTime(DateTime.Now),
                    "", "", Recortar($"Pedido {x.Referencia.Split('/')[0]} importado de {Conector(c.Conector).Nombre}", 400),
                    c.PlataformaId, x.PublicacionId, null, null, 0m, gastos, 0m, 0m, null, null));
                v.PedidoExterno = x.Referencia; v.CuentaIntegracionId = c.Id;
                break;
            case "cancelar_venta":
                var vc = await db.Ventas.FindAsync(x.VentaId) ?? throw new ErrorNegocio("La venta ya no existe.", 404);
                await Ventas.CancelarAsync(db, vc, DateOnly.FromDateTime(DateTime.Now), $"Cancelado en la plataforma (pedido {x.Referencia.Split('/')[0]})", 0m, []);
                break;
            case "registrar_cobro":
                var vb = await db.Ventas.FindAsync(x.VentaId) ?? throw new ErrorNegocio("La venta ya no existe.", 404);
                if (vb.ImporteCobrado is not null) throw new ErrorNegocio("La venta ya tiene cobro registrado.", 409);
                Ventas.RegistrarCobro(vb, x.Importe, DateOnly.FromDateTime(DateTime.Now));
                break;
            default: throw new ErrorNegocio($"Propuesta no aplicable: {x.Tipo}.", 400);
        }
    }
}
