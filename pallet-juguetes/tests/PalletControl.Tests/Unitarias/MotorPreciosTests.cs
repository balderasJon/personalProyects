using PalletControl.Dominio.Calculo;
using Xunit;

namespace PalletControl.Tests.Unitarias;

/// <summary>
/// Casos obligatorios 8–11 y 17 (Fase 2). Escenario: rompecabezas con costo real de $315.90 (resultado del costeo de la Fase 1).
/// Tarifas de PRUEBA, no reales: “Marketplace A” 15 % de comisión + $25 fijo + $10 de empaque; “Venta local” $40 de traslado + $10 de empaque.
/// Objetivo: margen 30 %, mínimo 10 %, redondeo a terminación 9. Sin IVA (régimen sin definir).
/// Resultados verificados con aritmética exacta (fracciones) fuera de .NET.
/// </summary>
public class MotorPreciosTests
{
    private const decimal Costo = 315.90m;
    private static readonly ComponentesTarifa MarketA = new(ComisionPct: 15m, CargoFijo: 25m, Empaque: 10m);
    private static readonly ComponentesTarifa Local = new(CostoLogistico: 40m, Empaque: 10m);
    private static readonly ConfigFiscal SinIva = new();
    private static readonly ObjetivoRentabilidad Obj = new(EsquemaRentabilidad.Margen, 30m, 10m, 0m, ModoRedondeo.Terminacion9);

    // Caso 8: precio mínimo = (C + F) / (1 − p − m_min) = 350.90 / 0.75 = 467.8666… → 467.87 (hacia arriba)
    [Fact]
    public void Caso08_PrecioMinimo()
    {
        var p = MotorPrecios.Calcular(Costo, MarketA, SinIva, Obj);
        Assert.True(p.Ok);
        Assert.Equal(412.83m, p.Equilibrio);   // 350.90 / 0.85
        Assert.Equal(467.87m, p.Minimo);
        // A precio mínimo, el margen es al menos 10 %
        Assert.True(MotorPrecios.Evaluar(p.Minimo!.Value, Costo, MarketA, SinIva).MargenPct >= 10m);
    }

    // Caso 9: precio objetivo = 350.90 / 0.55 = 638.00 → sugerido con terminación 9 = 639
    [Fact]
    public void Caso09_PrecioObjetivoYSugerido()
    {
        var p = MotorPrecios.Calcular(Costo, MarketA, SinIva, Obj);
        Assert.Equal(638.00m, p.Objetivo);
        Assert.Equal(639m, p.Sugerido);
        Assert.Equal(639m, p.Promocional);   // sin descuento promocional
    }

    [Theory]
    [InlineData(EsquemaRentabilidad.Markup, 50, 598.65)]        // (315.90 + 35 + 157.95) / 0.85
    [InlineData(EsquemaRentabilidad.UtilidadFija, 100, 530.48)] // (315.90 + 35 + 100) / 0.85
    public void Caso09_OtrosEsquemas(EsquemaRentabilidad e, double valor, double esperado)
    {
        var p = MotorPrecios.Calcular(Costo, MarketA, SinIva, Obj with { Esquema = e, Valor = (decimal)valor, Redondeo = ModoRedondeo.Centavo });
        Assert.Equal((decimal)esperado, p.Objetivo);
    }

    // Caso 10: margen al precio sugerido de 639: comisión 95.85; utilidad 639 − 95.85 − 25 − 10 − 315.90 = 192.25
    [Fact]
    public void Caso10_Margen()
    {
        var d = MotorPrecios.Evaluar(639m, Costo, MarketA, SinIva);
        Assert.Equal(95.85m, d.Comision);
        Assert.Equal(25m, d.CargosPlataforma);
        Assert.Equal(10m, d.GastosPropios);
        Assert.Equal(192.25m, d.Utilidad);
        Assert.Equal(30.09m, d.MargenPct);   // 192.25 / 639 = 30.086 %
        Assert.Equal(60.86m, d.RoiPct);      // 192.25 / 315.90 = 60.858 %
        Assert.Equal(518.15m, d.DepositoPlataforma); // 639 − 95.85 − 25
    }

    // Caso 11: comparación entre plataformas
    [Fact]
    public void Caso11_ComparacionEntrePlataformas()
    {
        var local = MotorPrecios.Calcular(Costo, Local, SinIva, Obj);
        Assert.Equal(522.72m, local.Objetivo);   // 365.90 / 0.70
        Assert.Equal(529m, local.Sugerido);
        Assert.Equal(163.10m, local.AlSugerido!.Utilidad);

        // Al mismo precio de mercado ($600), la venta local deja más: 234.10 contra 159.10
        Assert.Equal(234.10m, MotorPrecios.Evaluar(600m, Costo, Local, SinIva).Utilidad);
        Assert.Equal(159.10m, MotorPrecios.Evaluar(600m, Costo, MarketA, SinIva).Utilidad);
    }

    // Caso 17: actualización de tarifas por vigencia — la comisión sube de 15 % a 17 % el 1 de diciembre.
    [Fact]
    public void Caso17_ActualizacionDeTarifasPorVigencia()
    {
        var reglas = new List<ReglaTarifa>
        {
            new("t1", new(2026, 1, 1), new(2026, 11, 30), true, true, null, null, null, null, null, null, null, null, null, MarketA),
            new("t2", new(2026, 12, 1), null, true, true, null, null, null, null, null, null, null, null, null, MarketA with { ComisionPct = 17m }),
        };
        var nov = SelectorTarifa.Elegir(reglas, new(new DateOnly(2026, 11, 30)), Costo, SinIva, Obj);
        var dic = SelectorTarifa.Elegir(reglas, new(new DateOnly(2026, 12, 1)), Costo, SinIva, Obj);
        Assert.Equal("t1", nov.Regla!.Id);
        Assert.Equal(639m, nov.Precios.Sugerido);
        Assert.Equal("t2", dic.Regla!.Id);
        Assert.Equal(662.08m, dic.Precios.Objetivo);   // 350.90 / 0.53
        Assert.Equal(669m, dic.Precios.Sugerido);
    }

    [Fact]
    public void SinTarifaVigente_EsError()
    {
        var r = SelectorTarifa.Elegir([], new(new DateOnly(2026, 11, 18)), Costo, SinIva, Obj);
        Assert.False(r.Precios.Ok);
    }

    // Tarifas que dependen del precio: cargo fijo de $25 solo hasta $298.99.
    [Theory]
    [InlineData(100, "bajo", 229)]   // con cargo: 125 / 0.55 = 227.28 → 229 (cae en su rango)
    [InlineData(200, "alto", 369)]   // con cargo daría 419 (fuera de rango) → sin cargo: 200 / 0.55 = 363.64 → 369
    public void TarifaPorRangoDePrecio(double costo, string esperada, double sugerido)
    {
        var reglas = new List<ReglaTarifa>
        {
            new("bajo", new(2026, 1, 1), null, true, false, null, null, null, null, null, 298.99m, null, null, null, new(ComisionPct: 15m, CargoFijo: 25m)),
            new("alto", new(2026, 1, 1), null, true, false, null, null, null, null, 299m, null, null, null, null, new(ComisionPct: 15m)),
        };
        var r = SelectorTarifa.Elegir(reglas, new(new DateOnly(2026, 11, 18)), (decimal)costo, SinIva, Obj);
        Assert.Equal(esperada, r.Regla!.Id);
        Assert.Equal((decimal)sugerido, r.Precios.Sugerido);
    }

    [Fact]
    public void TarifaPorPeso_SinPeso_PideCapturarlo()
    {
        var reglas = new List<ReglaTarifa> { new("p", new(2026, 1, 1), null, true, true, null, null, null, null, null, null, 0m, 5m, null, MarketA) };
        var r = SelectorTarifa.Elegir(reglas, new(new DateOnly(2026, 11, 18)), Costo, SinIva, Obj);
        Assert.False(r.Precios.Ok);
        Assert.Contains("peso", r.Precios.Error);
    }

    // Impuestos separados: precio 1,160 con IVA 16 %, retención ISR 1 % e IVA 50 % (valores de prueba).
    [Fact]
    public void Iva_YRetenciones_NoSonGastos()
    {
        var t = new ComponentesTarifa(RetencionIsrPct: 1m, RetencionIvaPct: 50m);
        var d = MotorPrecios.Evaluar(1160m, 500m, t, new ConfigFiscal(CobraIva: true));
        Assert.Equal(1000m, d.IngresoSinIva);
        Assert.Equal(160m, d.IvaTrasladado);
        Assert.Equal(500m, d.Utilidad);          // 1000 − 500: ni el IVA ni las retenciones se restan como gasto
        Assert.Equal(10m, d.RetencionIsr);
        Assert.Equal(80m, d.RetencionIva);
        Assert.Equal(1070m, d.DepositoPlataforma); // 1160 − 10 − 80
    }

    [Theory]
    [InlineData(337.40, 339)]
    [InlineData(1284, 1289)]
    [InlineData(1290, 1299)]
    [InlineData(0.5, 9)]
    public void Redondeo_Terminacion9_SiempreHaciaArriba(double v, double esperado) =>
        Assert.Equal((decimal)esperado, MotorPrecios.Redondear((decimal)v, ModoRedondeo.Terminacion9));

    // Nunca se recomienda menos del mínimo: un descuento promocional de 40 % se limita al mínimo.
    [Fact]
    public void Promocional_NuncaDebajoDelMinimo()
    {
        var p = MotorPrecios.Calcular(Costo, MarketA, SinIva, Obj with { DescuentoPromocionalPct = 40m });
        Assert.True(p.PromocionalLimitado);
        Assert.Equal(469m, p.Promocional);   // mínimo 467.87 redondeado a terminación 9
        Assert.True(p.Promocional >= p.Minimo);
        var p10 = MotorPrecios.Calcular(Costo, MarketA, SinIva, Obj with { DescuentoPromocionalPct = 10m });
        Assert.Equal(579m, p10.Promocional); // 639 × 0.9 = 575.10 → 579
    }

    [Fact]
    public void Alertas()
    {
        var p = MotorPrecios.Calcular(Costo, MarketA, SinIva, Obj);
        string[] Codigos(decimal precio, decimal? mercado = null) => MotorPrecios.Alertas(precio, Costo, MarketA, SinIva, p, Obj, mercado).Select(a => a.Codigo).ToArray();
        Assert.Contains("bajo_costo", Codigos(300m));
        Assert.Contains("perdida", Codigos(400m));
        Assert.Contains("bajo_minimo", Codigos(450m));
        Assert.Contains("margen_insuficiente", Codigos(500m));
        Assert.Empty(Codigos(639m));
        Assert.Contains("no_rentable", Codigos(639m, mercado: 450m));   // el mínimo (467.87) supera el precio de mercado
        var conEnvio = MarketA with { EnvioAbsorbido = 150m };
        var p2 = MotorPrecios.Calcular(Costo, conEnvio, SinIva, Obj);
        Assert.Contains("logistica", MotorPrecios.Alertas(p2.Minimo!.Value, Costo, conEnvio, SinIva, p2, Obj, null).Select(a => a.Codigo));
    }

    [Fact]
    public void ComisionesDeCienPorCiento_NoEsRentable()
    {
        var p = MotorPrecios.Calcular(Costo, new ComponentesTarifa(ComisionPct: 100m), SinIva, Obj);
        Assert.False(p.Ok);
    }
}
