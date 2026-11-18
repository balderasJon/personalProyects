namespace PalletControl.Dominio.Integracion;

/// <summary>Publicación registrada en la aplicación y vinculada a un anuncio de la plataforma (por su identificador externo).</summary>
public record PublicacionLocal(string Id, string ProductoId, string IdExterno, string Estatus, decimal PrecioPublicado, int Disponibles);

/// <summary>Anuncio leído de la plataforma. Estatus normalizado: activa, pausada, cerrada u otra. Null = la plataforma no lo informa.</summary>
public record PublicacionRemota(string IdExterno, string Titulo, decimal? Precio, int? Existencias, string Estatus, string? Url = null,
    bool ConVariaciones = false, string EstatusOriginal = "");

/// <summary>Qué cambios admite el conector (según la documentación oficial de cada plataforma).</summary>
public record CapacidadesEscritura(bool Pausar, bool Activar, bool Existencias, bool Precio);

/// <summary>Línea de un pedido de la plataforma. Comision = la que informa la plataforma para esa línea.</summary>
public record LineaRemota(string IdPublicacion, string Titulo, int Cantidad, decimal PrecioUnitario, decimal Comision);

/// <summary>Pedido leído. Estatus normalizado: pagado, cancelado, pendiente u otro.</summary>
public record PedidoRemoto(string Id, DateOnly Fecha, string Estatus, IReadOnlyList<LineaRemota> Lineas, decimal Total,
    decimal? CostoEnvioVendedor, string EstatusOriginal = "");

/// <summary>Venta de la aplicación ya importada de un pedido (clave "pedido/publicación").</summary>
public record VentaImportada(string VentaId, string Clave, string Estado, bool Cobrada);

public record GastoPropuesto(string Tipo, string Concepto, decimal Monto, bool DeducidoPorPlataforma, bool Estimado);

/// <summary>
/// Cambio propuesto. Bloqueo != null significa que no se puede aplicar (falta un vínculo o la plataforma no lo permite);
/// se muestra para que el usuario lo resuelva a mano.
/// </summary>
public record CambioPropuesto(string Tipo, string Destino, string? PublicacionId, string Referencia, string Descripcion, string Antes, string Despues,
    int? Existencias = null, decimal? Precio = null, string? Bloqueo = null,
    // Para ventas
    string? VentaId = null, string? ProductoId = null, int Cantidad = 0, DateOnly? Fecha = null, IReadOnlyList<GastoPropuesto>? Gastos = null,
    decimal? Importe = null);

/// <summary>
/// Compara el inventario central con lo que hay en la plataforma y propone cambios. Puro: no llama a ninguna API.
/// Reglas:
/// - Publicación activa sin piezas disponibles → pausar (o existencias 0 si la plataforma no permite pausar).
/// - Publicación activa que anuncia más piezas de las disponibles → existencias = disponibles.
/// - Precio distinto al registrado en la aplicación → enviar el de la aplicación (la decisión de precio es tuya).
/// - Publicación pausada en la plataforma, publicada en la aplicación y con piezas → reactivar.
/// - Pedido pagado no importado → nueva venta con la comisión y el envío que informa la plataforma.
/// - Pedido cancelado ya importado → cancelar la venta.
/// - Venta importada sin cobro → registrar cobro estimado (se confirma contra el estado de cuenta).
/// </summary>
public static class Planificador
{
    private static string M(decimal? v) => v is null ? "—" : "$" + v.Value.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-MX"));

    public static List<CambioPropuesto> Publicaciones(IEnumerable<PublicacionLocal> locales, IReadOnlyDictionary<string, PublicacionRemota> remotas, CapacidadesEscritura cap)
    {
        var res = new List<CambioPropuesto>();
        foreach (var l in locales.Where(l => l.Estatus is not ("eliminado" or "cancelado")).OrderBy(l => l.IdExterno, StringComparer.Ordinal))
        {
            if (!remotas.TryGetValue(l.IdExterno, out var r))
            {
                res.Add(new("informativa", "plataforma", l.Id, l.IdExterno, "No se encontró este anuncio en la cuenta conectada. Revisa el identificador.", "", "", Bloqueo: "no_encontrada"));
                continue;
            }
            if (r.ConVariaciones)
            {
                res.Add(new("informativa", "plataforma", l.Id, l.IdExterno, $"«{r.Titulo}» tiene variaciones: sus existencias y precio se ajustan en la plataforma.", "", "", Bloqueo: "variaciones"));
                continue;
            }
            var disp = Math.Max(0, l.Disponibles);
            if (r.Estatus == "activa")
            {
                if (disp == 0)
                {
                    if (cap.Pausar) res.Add(new("pausar", "plataforma", l.Id, l.IdExterno, $"«{r.Titulo}» sigue activa y ya no tienes piezas: pausarla evita una doble venta.", "Activa", "Pausada"));
                    else if (cap.Existencias) res.Add(new("existencias", "plataforma", l.Id, l.IdExterno, $"«{r.Titulo}» sigue disponible y ya no tienes piezas.", $"{r.Existencias?.ToString() ?? "—"} pz", "0 pz", Existencias: 0));
                }
                else if (cap.Existencias && r.Existencias is { } ex && ex > disp)
                    res.Add(new("existencias", "plataforma", l.Id, l.IdExterno, $"«{r.Titulo}» anuncia más piezas de las que tienes.", $"{ex} pz", $"{disp} pz", Existencias: disp));
            }
            else if (r.Estatus == "pausada" && l.Estatus == "publicado" && disp > 0 && cap.Activar)
                res.Add(new("activar", "plataforma", l.Id, l.IdExterno, $"«{r.Titulo}» está pausada en la plataforma y tienes {disp} pieza(s). Reactívala si fue una pausa por falta de piezas.", "Pausada", "Activa"));

            if (cap.Precio && r.Estatus is "activa" or "pausada" && r.Precio is { } pr && l.PrecioPublicado > 0 && pr != l.PrecioPublicado && disp > 0)
                res.Add(new("precio", "plataforma", l.Id, l.IdExterno, $"El precio de «{r.Titulo}» en la plataforma no coincide con el registrado en la aplicación.", M(pr), M(l.PrecioPublicado), Precio: l.PrecioPublicado));
        }
        return res;
    }

    /// <summary>Clave de una línea importada: un pedido puede traer varios juguetes; cada uno es una venta en la aplicación.</summary>
    public static string Clave(string pedido, string publicacion) => $"{pedido}/{publicacion}";

    public static List<CambioPropuesto> Pedidos(IEnumerable<PedidoRemoto> pedidos, IReadOnlyList<VentaImportada> importadas,
        IReadOnlyDictionary<string, (string PublicacionId, string ProductoId)> vinculos)
    {
        var res = new List<CambioPropuesto>();
        var porClave = importadas.ToDictionary(v => v.Clave);
        foreach (var p in pedidos.OrderBy(p => p.Fecha).ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            for (var i = 0; i < p.Lineas.Count; i++)
            {
                var l = p.Lineas[i];
                var clave = Clave(p.Id, l.IdPublicacion);
                var bruto = l.PrecioUnitario * l.Cantidad;
                var envio = i == 0 ? p.CostoEnvioVendedor : 0m;   // el envío del pedido se carga a su primera línea
                porClave.TryGetValue(clave, out var v);
                if (v is null)
                {
                    if (p.Estatus != "pagado") continue;   // solo se importan pedidos pagados
                    var gastos = new List<GastoPropuesto>();
                    if (l.Comision > 0) gastos.Add(new("comision", "Comisión informada por la plataforma", l.Comision, true, l.Cantidad > 1));
                    if (envio is > 0) gastos.Add(new("envio", "Costo de envío a cargo del vendedor", envio.Value, true, false));
                    if (!vinculos.TryGetValue(l.IdPublicacion, out var vin))
                    {
                        res.Add(new("nueva_venta", "aplicacion", null, clave, $"Pedido {p.Id}: «{l.Titulo}» × {l.Cantidad}. El anuncio {l.IdPublicacion} no está vinculado a ningún juguete: regístralo en Publicaciones con ese identificador.",
                            "", M(bruto), Bloqueo: "sin_vinculo", Cantidad: l.Cantidad, Fecha: p.Fecha, Precio: l.PrecioUnitario, Gastos: gastos));
                        continue;
                    }
                    res.Add(new("nueva_venta", "aplicacion", vin.PublicacionId, clave,
                        $"Pedido {p.Id}: «{l.Titulo}» × {l.Cantidad} a {M(l.PrecioUnitario)}" + (l.Cantidad > 1 && l.Comision > 0 ? ". Verifica la comisión: la documentación no aclara si es por pieza o por línea." : "."),
                        "", M(bruto), ProductoId: vin.ProductoId, Cantidad: l.Cantidad, Fecha: p.Fecha, Precio: l.PrecioUnitario, Gastos: gastos));
                }
                else if (p.Estatus == "cancelado" && v.Estado != "cancelado")
                    res.Add(new("cancelar_venta", "aplicacion", null, clave, $"El pedido {p.Id} aparece cancelado en la plataforma.", "Vigente", "Cancelada", VentaId: v.VentaId));
                else if (p.Estatus == "pagado" && v.Estado != "cancelado" && !v.Cobrada)
                {
                    var neto = bruto - l.Comision - (envio ?? 0m);
                    res.Add(new("registrar_cobro", "aplicacion", null, clave,
                        $"Pedido {p.Id}: cobro estimado (precio − comisión − envío). Confírmalo cuando el dinero esté disponible en tu cuenta.",
                        "Sin cobro", M(neto), VentaId: v.VentaId, Importe: Math.Max(0m, neto), Fecha: p.Fecha));
                }
            }
        }
        return res;
    }
}
