namespace PalletControl.Dominio.Calculo;

/// <summary>Cómo se fija la utilidad deseada.</summary>
public enum EsquemaRentabilidad { Margen, Markup, UtilidadFija }

/// <summary>Redondeo del precio sugerido. Siempre hacia arriba.</summary>
public enum ModoRedondeo { Centavo, Peso, Diez, Terminacion9 }

/// <summary>
/// Cargos de una plataforma para una venta. Los porcentajes se expresan como 15 = 15 %.
/// Comisión, medio de pago, cargo fijo, logística, envío absorbido y almacenamiento los descuenta la plataforma;
/// preparación, empaque, publicidad y reserva por devoluciones los paga el vendedor.
/// Las retenciones NO son gastos: son anticipos de impuestos y se muestran aparte.
/// </summary>
public record ComponentesTarifa(
    decimal ComisionPct = 0, decimal ComisionMedioPagoPct = 0, decimal CargoFijo = 0,
    decimal CostoLogistico = 0, decimal EnvioAbsorbido = 0, decimal Almacenamiento = 0,
    decimal Preparacion = 0, decimal Empaque = 0, decimal PublicidadPct = 0, decimal PublicidadFija = 0,
    decimal ReservaDevolucionesPct = 0,
    /// <summary>% de ISR que retiene la plataforma, sobre el precio sin IVA.</summary>
    decimal RetencionIsrPct = 0,
    /// <summary>% del IVA trasladado que retiene la plataforma.</summary>
    decimal RetencionIvaPct = 0)
{
    public decimal Porcentuales => (ComisionPct + ComisionMedioPagoPct + PublicidadPct + ReservaDevolucionesPct) / 100m;
    public decimal Fijos => CargoFijo + CostoLogistico + EnvioAbsorbido + Almacenamiento + Preparacion + Empaque + PublicidadFija;
}

/// <summary>Configuración fiscal. Con régimen sin definir, todo en cero (el motor lo advierte).</summary>
public record ConfigFiscal(bool CobraIva = false, decimal TasaIvaPct = 16m)
{
    /// <summary>Fracción del precio que es ingreso (sin IVA). Los precios al consumidor incluyen IVA.</summary>
    public decimal FactorIngreso => CobraIva ? 1m / (1m + TasaIvaPct / 100m) : 1m;
}

public record ObjetivoRentabilidad(EsquemaRentabilidad Esquema, decimal Valor, decimal MargenMinimoPct,
    decimal DescuentoPromocionalPct, ModoRedondeo Redondeo);

/// <summary>Resultado de vender a un precio dado.</summary>
public record Desglose(
    decimal Precio, decimal IvaTrasladado, decimal IngresoSinIva,
    decimal Comision, decimal CargosPlataforma, decimal GastosPropios, decimal GastosTotales,
    decimal Costo, decimal Utilidad, decimal MargenPct, decimal? RoiPct,
    decimal RetencionIsr, decimal RetencionIva, decimal DepositoPlataforma);

public record Alerta(string Codigo, string Nivel, string Mensaje);

public record PreciosCanal(
    bool Ok, string? Error,
    decimal? Equilibrio, decimal? Minimo, decimal? Objetivo, decimal? Sugerido, decimal? Promocional, bool PromocionalLimitado,
    Desglose? AlSugerido);

/// <summary>
/// Motor de precios. Fórmula base (aproximación comercial):
///   ingreso(P) = P × f − P × p − F,  con f = 1/(1+IVA) si se cobra IVA (si no, 1), p = cargos porcentuales, F = cargos fijos.
///   utilidad(P) = ingreso(P) − costo.
///   Margen m sobre el precio sin IVA:  P = (C + F) / (f − p − m·f).
///   Markup k sobre costo:              P = (C + F + k·C) / (f − p).
///   Utilidad fija u:                   P = (C + F + u) / (f − p).
/// El IVA trasladado y las retenciones se presentan aparte; no se cuentan como gasto ni como ingreso.
/// </summary>
public static class MotorPrecios
{
    public static decimal ArribaCentavo(decimal v) => Math.Ceiling(v * 100m) / 100m;

    public static decimal Redondear(decimal v, ModoRedondeo modo) => modo switch
    {
        ModoRedondeo.Peso => Math.Ceiling(v),
        ModoRedondeo.Diez => Math.Ceiling(v / 10m) * 10m,
        ModoRedondeo.Terminacion9 => Terminacion9(v),
        _ => ArribaCentavo(v),
    };

    private static decimal Terminacion9(decimal v)
    {
        var n = (long)Math.Ceiling(v);
        var r = n % 10;
        return n + (9 - r + 10) % 10;
    }

    /// <summary>Precio que logra una utilidad de margen m (fracción) sobre el ingreso sin IVA. Null si no es posible.</summary>
    public static decimal? PrecioPorMargen(decimal costo, ComponentesTarifa t, ConfigFiscal fi, decimal margen)
    {
        var f = fi.FactorIngreso;
        var den = f - t.Porcentuales - margen * f;
        return den > 0 ? (costo + t.Fijos) / den : null;
    }

    public static decimal? PrecioObjetivo(decimal costo, ComponentesTarifa t, ConfigFiscal fi, ObjetivoRentabilidad o)
    {
        var den = fi.FactorIngreso - t.Porcentuales;
        return o.Esquema switch
        {
            EsquemaRentabilidad.Margen => PrecioPorMargen(costo, t, fi, o.Valor / 100m),
            EsquemaRentabilidad.Markup => den > 0 ? (costo + t.Fijos + costo * o.Valor / 100m) / den : null,
            EsquemaRentabilidad.UtilidadFija => den > 0 ? (costo + t.Fijos + o.Valor) / den : null,
            _ => null,
        };
    }

    /// <summary>Desglose de vender a precio P. Cada concepto se redondea al centavo y la utilidad sale de esos montos.</summary>
    public static Desglose Evaluar(decimal precio, decimal costo, ComponentesTarifa t, ConfigFiscal fi)
    {
        var c = Dinero.Centavos;
        var sinIva = c(precio * fi.FactorIngreso);
        var iva = precio - sinIva;
        var comision = c(precio * (t.ComisionPct + t.ComisionMedioPagoPct) / 100m);
        var cargosPlat = c(t.CargoFijo + t.CostoLogistico + t.EnvioAbsorbido + t.Almacenamiento);
        var propios = c(t.Preparacion + t.Empaque + t.PublicidadFija + precio * (t.PublicidadPct + t.ReservaDevolucionesPct) / 100m);
        var gastos = comision + cargosPlat + propios;
        var util = sinIva - gastos - c(costo);
        var retIsr = c(sinIva * t.RetencionIsrPct / 100m);
        var retIva = c(iva * t.RetencionIvaPct / 100m);
        return new Desglose(precio, iva, sinIva, comision, cargosPlat, propios, gastos, c(costo), util,
            sinIva > 0 ? Math.Round(util / sinIva * 100m, 2, MidpointRounding.AwayFromZero) : 0m,
            costo > 0 ? Math.Round(util / costo * 100m, 2, MidpointRounding.AwayFromZero) : null,
            retIsr, retIva, precio - comision - cargosPlat - retIsr - retIva);
    }

    /// <summary>Precios de un producto en un canal con una tarifa ya elegida.</summary>
    public static PreciosCanal Calcular(decimal costo, ComponentesTarifa t, ConfigFiscal fi, ObjetivoRentabilidad o)
    {
        if (costo < 0) return Error("El costo no puede ser negativo.");
        var eq = PrecioPorMargen(costo, t, fi, 0m);
        if (eq is null) return Error("Las comisiones y gastos porcentuales suman 100 % o más: ningún precio es rentable en este canal.");
        var min = PrecioPorMargen(costo, t, fi, o.MargenMinimoPct / 100m);
        if (min is null) return Error("Con el margen mínimo configurado, ningún precio es posible en este canal.");
        var obj = PrecioObjetivo(costo, t, fi, o);
        if (obj is null) return Error("Con el objetivo de rentabilidad configurado, ningún precio es posible en este canal.");

        var equilibrio = ArribaCentavo(eq.Value);
        var minimo = ArribaCentavo(min.Value);
        var objetivo = Math.Max(ArribaCentavo(obj.Value), minimo);           // el objetivo nunca queda debajo del mínimo
        var sugerido = Math.Max(Redondear(objetivo, o.Redondeo), minimo);    // redondeo hacia arriba: nunca debajo del mínimo
        var promo = sugerido;
        var limitado = false;
        if (o.DescuentoPromocionalPct > 0)
        {
            var x = sugerido * (1m - o.DescuentoPromocionalPct / 100m);
            if (x < minimo) { x = minimo; limitado = true; }
            promo = Math.Min(Redondear(x, o.Redondeo), sugerido);
            if (promo < minimo) promo = minimo;
        }
        return new PreciosCanal(true, null, equilibrio, minimo, objetivo, sugerido, promo, limitado, Evaluar(sugerido, costo, t, fi));
    }

    /// <summary>Alertas al vender a un precio dado.</summary>
    public static List<Alerta> Alertas(decimal precio, decimal costo, ComponentesTarifa t, ConfigFiscal fi, PreciosCanal p, ObjetivoRentabilidad o, decimal? precioMercado)
    {
        var a = new List<Alerta>();
        if (!p.Ok) { a.Add(new("no_rentable", "error", p.Error!)); return a; }
        var d = Evaluar(precio, costo, t, fi);
        if (precio < costo) a.Add(new("bajo_costo", "error", $"El precio (${precio:N2}) está por debajo del costo del juguete (${costo:N2})."));
        else if (precio < p.Equilibrio) a.Add(new("perdida", "error", $"A ${precio:N2} pierdes ${-d.Utilidad:N2} después de comisiones y gastos. El equilibrio es ${p.Equilibrio:N2}."));
        if (precio >= p.Equilibrio && precio < p.Minimo) a.Add(new("bajo_minimo", "error", $"El precio está por debajo del mínimo autorizado (${p.Minimo:N2}, margen {o.MargenMinimoPct:0.##} %)."));
        if (precio >= p.Minimo && precio < p.Objetivo) a.Add(new("margen_insuficiente", "aviso", $"Margen de {d.MargenPct:0.##} %: menor al objetivo. El precio objetivo es ${p.Objetivo:N2}."));
        var logistica = t.CostoLogistico + t.EnvioAbsorbido + t.Almacenamiento;
        if (logistica > 0 && logistica > d.Utilidad) a.Add(new("logistica", "aviso", $"Los gastos logísticos (${logistica:N2}) superan la utilidad de la venta (${d.Utilidad:N2})."));
        if (precioMercado is > 0 && p.Minimo > precioMercado)
            a.Add(new("no_rentable", "error", $"El precio mínimo (${p.Minimo:N2}) es mayor que el precio de mercado (${precioMercado:N2}): difícil de vender con utilidad en este canal."));
        return a;
    }

    private static PreciosCanal Error(string m) => new(false, m, null, null, null, null, null, false, null);
}

// ---------------------------------------------------------------------------------------------
// Selección de tarifa por vigencia y criterios
// ---------------------------------------------------------------------------------------------

/// <summary>Regla de tarifa. Un criterio nulo aplica a cualquier valor. Rangos inclusivos.</summary>
public record ReglaTarifa(
    string Id, DateOnly Desde, DateOnly? Hasta, bool Activa, bool Predeterminada,
    string? TipoPublicacion, string? Modalidad, string? Categoria, string? MedioPago,
    decimal? PrecioMin, decimal? PrecioMax, decimal? PesoMinKg, decimal? PesoMaxKg, decimal? LadoMaxCm,
    ComponentesTarifa Componentes);

public record ContextoTarifa(DateOnly Fecha, string? TipoPublicacion = null, string? Modalidad = null, string? Categoria = null,
    string? MedioPago = null, decimal? PesoKg = null, decimal? LadoMayorCm = null);

public record TarifaElegida(ReglaTarifa? Regla, PreciosCanal Precios, string? Aviso);

public static class SelectorTarifa
{
    private static bool Igual(string? regla, string? ctx) =>
        regla is null || (ctx is not null && string.Equals(regla, ctx, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reglas vigentes que aplican al contexto (sin considerar el precio), de la más a la menos específica.</summary>
    public static List<ReglaTarifa> Candidatas(IEnumerable<ReglaTarifa> reglas, ContextoTarifa c, out string? faltaDato)
    {
        faltaDato = null;
        var res = new List<(ReglaTarifa R, int Esp)>();
        foreach (var r in reglas.Where(r => r.Activa && r.Desde <= c.Fecha && (r.Hasta is null || r.Hasta >= c.Fecha)))
        {
            // Tipo de publicación y modalidad: si no se indican, se usa la tarifa predeterminada o la que no los distingue.
            if (r.TipoPublicacion is not null && c.TipoPublicacion is not null && !Igual(r.TipoPublicacion, c.TipoPublicacion)) continue;
            if (r.Modalidad is not null && c.Modalidad is not null && !Igual(r.Modalidad, c.Modalidad)) continue;
            if (!Igual(r.Categoria, c.Categoria) || !Igual(r.MedioPago, c.MedioPago)) continue;
            if (r.PesoMinKg is not null || r.PesoMaxKg is not null)
            {
                if (c.PesoKg is null) { faltaDato = "peso"; continue; }
                if (c.PesoKg < (r.PesoMinKg ?? 0) || (r.PesoMaxKg is { } pm && c.PesoKg > pm)) continue;
            }
            if (r.LadoMaxCm is not null)
            {
                if (c.LadoMayorCm is null) { faltaDato = "medidas"; continue; }
                if (c.LadoMayorCm > r.LadoMaxCm) continue;
            }
            var esp = new object?[] { r.TipoPublicacion, r.Modalidad, r.Categoria, r.MedioPago, r.PesoMinKg ?? r.PesoMaxKg, r.LadoMaxCm }.Count(x => x is not null);
            var explicito = (c.TipoPublicacion is not null && r.TipoPublicacion is not null ? 1 : 0) + (c.Modalidad is not null && r.Modalidad is not null ? 1 : 0);
            res.Add((r, explicito * 100 + (r.Predeterminada ? 50 : 0) + esp));
        }
        return res.OrderByDescending(x => x.Esp).ThenByDescending(x => x.R.Desde).ThenBy(x => x.R.Id, StringComparer.Ordinal).Select(x => x.R).ToList();
    }

    public static bool CubrePrecio(ReglaTarifa r, decimal precio) =>
        (r.PrecioMin is null || precio >= r.PrecioMin) && (r.PrecioMax is null || precio <= r.PrecioMax);

    /// <summary>La regla que aplica a un precio ya conocido.</summary>
    public static ReglaTarifa? ParaPrecio(List<ReglaTarifa> candidatas, decimal precio) => candidatas.FirstOrDefault(r => CubrePrecio(r, precio));

    /// <summary>
    /// Elige la tarifa y calcula los precios. Si las tarifas dependen del precio (p. ej. un cargo fijo solo debajo de cierto monto),
    /// se busca la regla cuyo precio sugerido cae dentro de su propio rango.
    /// </summary>
    public static TarifaElegida Elegir(IEnumerable<ReglaTarifa> reglas, ContextoTarifa c, decimal costo, ConfigFiscal fi, ObjetivoRentabilidad o)
    {
        var cand = Candidatas(reglas, c, out var falta);
        if (cand.Count == 0)
            return new(null, new PreciosCanal(false, falta is null ? "No hay tarifa vigente para esta plataforma en la fecha indicada. Captúrala en Plataformas y tarifas."
                : $"Hay tarifas que dependen del {falta} del juguete: captura su {falta}.", null, null, null, null, null, false, null), null);
        foreach (var r in cand)
        {
            var p = MotorPrecios.Calcular(costo, r.Componentes, fi, o);
            if (!p.Ok) { if (cand.Count == 1) return new(r, p, null); continue; }
            if (ReferenceEquals(ParaPrecio(cand, p.Sugerido!.Value), r)) return new(r, p, null);
        }
        // Sin una regla consistente (salto de tarifa): se toma el precio más alto, que cumple con cualquiera de las reglas.
        var todos = cand.Select(r => (r, p: MotorPrecios.Calcular(costo, r.Componentes, fi, o))).Where(x => x.p.Ok).OrderByDescending(x => x.p.Sugerido).ToList();
        if (todos.Count == 0) return new(cand[0], MotorPrecios.Calcular(costo, cand[0].Componentes, fi, o), null);
        var elegido = todos[0];
        var regla = ParaPrecio(cand, elegido.p.Sugerido!.Value) ?? elegido.r;
        return new(regla, MotorPrecios.Calcular(costo, regla.Componentes, fi, o) is { Ok: true } pr && pr.Sugerido >= elegido.p.Sugerido ? pr : elegido.p,
            "Las tarifas por rango de precio no dan un precio exacto; se usó el más conservador.");
    }
}
