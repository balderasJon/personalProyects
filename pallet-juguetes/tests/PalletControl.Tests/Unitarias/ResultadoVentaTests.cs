using PalletControl.Dominio.Calculo;
using Xunit;

namespace PalletControl.Tests.Unitarias;

/// <summary>
/// Casos obligatorios 12–15 (Fase 3). Costo histórico por pieza: $315.90. Gastos de PRUEBA (no son tarifas reales).
/// Sin IVA salvo donde se indica.
/// </summary>
public class ResultadoVentaTests
{
    private const decimal Costo = 315.90m;

    // Caso 12: 2 piezas publicadas a $639, vendidas a $600 c/u con $50 de descuento adicional.
    // Bruto 1,150; comisión 15 % = 172.50 y cargo fijo 2 × 25 = 50 (los descuenta la plataforma); empaque 2 × 10 = 20 (tuyo).
    // Utilidad = 1,150 − 172.50 − 50 − 20 − 631.80 = 275.70; margen 23.97 %.
    [Fact]
    public void Caso12_VentaConDescuento()
    {
        var v = new DatosVenta([new LineaVenta(2, 600m, Costo)], 50m, 1m,
            [new(172.50m, true), new(50m, true), new(20m, false)], 0m, false, null);
        var r = CalculoVenta.Calcular(v);
        Assert.Equal(1150m, r.IngresoBruto);
        Assert.Equal(631.80m, r.CostoVendido);
        Assert.Equal(275.70m, r.Utilidad);
        Assert.Equal(23.97m, r.MargenPct);
        Assert.False(r.Cobrada);
        Assert.Equal(0m, r.EfectivoNeto);               // registrada no es cobrada
        Assert.Equal(927.50m, r.PorCobrarEstimado);     // 1,150 − 172.50 − 50
        var cobrada = CalculoVenta.Calcular(v with { ImporteCobrado = 927.50m });
        Assert.Equal(907.50m, cobrada.EfectivoNeto);    // 927.50 − 20 de empaque
    }

    // Caso 13: 1 pieza a $639 con comisión 95.85, cargo fijo 25 y envío absorbido 120 (deducidos) y empaque 10 (tuyo).
    // Utilidad = 639 − 95.85 − 25 − 120 − 10 − 315.90 = 72.25; margen 11.31 %. Depósito 398.15; efectivo 388.15.
    [Fact]
    public void Caso13_VentaConComisionYEnvio()
    {
        var r = CalculoVenta.Calcular(Caso13(null));
        Assert.Equal(72.25m, r.Utilidad);
        Assert.Equal(11.31m, r.MargenPct);
        Assert.Equal(240.85m, r.GastosDeducidos);
        Assert.Equal(398.15m, r.PorCobrarEstimado);
        var c = CalculoVenta.Calcular(Caso13(398.15m));
        Assert.Equal(388.15m, c.EfectivoNeto);
    }

    private static DatosVenta Caso13(decimal? cobrado) =>
        new([new LineaVenta(1, 639m, Costo)], 0m, 1m, [new(95.85m, true), new(25m, true), new(120m, true), new(10m, false)], 0m, false, cobrado);

    // Caso 14: devolución de la venta del caso 13 con reembolso total y $120 de envío de regreso (tuyo).
    [Fact]
    public void Caso14_DevolucionReintegradaAInventario()
    {
        var v = Caso13(398.15m) with
        {
            Lineas = [new LineaVenta(1, 639m, Costo, Devueltas: 1)],
            Reembolsos = 639m,
            Gastos = [new(95.85m, true), new(25m, true), new(120m, true), new(10m, false), new(120m, false)],
        };
        var r = CalculoVenta.Calcular(v);
        Assert.Equal(0m, r.CostoVendido);          // la pieza regresó al inventario: su costo se recupera
        Assert.Equal(-370.85m, r.Utilidad);         // 639 − 639 − 370.85 de gastos
        Assert.Equal(-370.85m, r.EfectivoNeto);     // 398.15 − 130 − 639: coincide con la utilidad
    }

    [Fact]
    public void Caso14_DevolucionDaniadaEsMerma()
    {
        var v = Caso13(398.15m) with
        {
            Lineas = [new LineaVenta(1, 639m, Costo, Devueltas: 1, DevueltasAMerma: 1)],
            Reembolsos = 639m,
            Gastos = [new(95.85m, true), new(25m, true), new(120m, true), new(10m, false), new(120m, false)],
        };
        var r = CalculoVenta.Calcular(v);
        Assert.Equal(315.90m, r.CostoMerma);
        Assert.Equal(-686.75m, r.Utilidad);
    }

    // Caso 15: cancelación — sin ingreso ni costo; si hubo un gasto no recuperable, es pérdida.
    [Fact]
    public void Caso15_Cancelacion()
    {
        var sin = CalculoVenta.Calcular(new DatosVenta([new LineaVenta(1, 639m, Costo)], 0m, 1m, [], 0m, true, null));
        Assert.Equal(0m, sin.IngresoBruto);
        Assert.Equal(0m, sin.CostoVendido);
        Assert.Equal(0m, sin.Utilidad);
        Assert.Equal(0m, sin.PorCobrarEstimado);
        var conGasto = CalculoVenta.Calcular(new DatosVenta([new LineaVenta(1, 639m, Costo)], 0m, 1m, [new(80m, false)], 0m, true, null));
        Assert.Equal(-80m, conGasto.Utilidad);
    }

    [Fact]
    public void Iva_YReembolsoParcial()
    {
        var f = 1m / 1.16m;
        var r = CalculoVenta.Calcular(new DatosVenta([new LineaVenta(1, 1160m, 500m)], 0m, f, [], 580m, false, null));
        Assert.Equal(1000m, r.IngresoNeto);
        Assert.Equal(160m, r.IvaTrasladado);
        Assert.Equal(500m, r.ReembolsosNetos);
        Assert.Equal(0m, r.Utilidad);   // 1,000 − 500 de reembolso sin IVA − 500 de costo
    }

    [Fact]
    public void Validaciones()
    {
        Assert.Throws<ArgumentException>(() => CalculoVenta.Calcular(new DatosVenta([new LineaVenta(1, 10m, 5m, Devueltas: 2)], 0m, 1m, [], 0m, false, null)));
        Assert.Throws<ArgumentException>(() => CalculoVenta.Calcular(new DatosVenta([new LineaVenta(2, 10m, 5m, Devueltas: 1, DevueltasAMerma: 2)], 0m, 1m, [], 0m, false, null)));
    }

    [Fact]
    public void AsignacionDeLotes_EnOrdenYSinExceder()
    {
        var lotes = new List<(string, int)> { ("A", 2), ("B", 5) };
        Assert.Equal([("A", 2), ("B", 2)], CalculoVenta.AsignarLotes(4, lotes));
        Assert.Null(CalculoVenta.AsignarLotes(8, lotes));
        Assert.Equal([("B", 1)], CalculoVenta.AsignarLotes(1, [("A", 0), ("B", 5)]));
    }

    [Fact]
    public void RepartoPorPeso_CuadraAlCentavo()
    {
        var r = CalculoVenta.RepartirPorPeso(100m, [("P1", 1m), ("P2", 1m), ("P3", 1m)]);
        Assert.Equal(100m, r.Values.Sum());
        Assert.Equal(33.34m, r["P1"]);
        var n = CalculoVenta.RepartirPorPeso(-50m, [("P1", 3m), ("P2", 1m)]);
        Assert.Equal(-37.50m, n["P1"]);
        Assert.Equal(-12.50m, n["P2"]);
    }
}
