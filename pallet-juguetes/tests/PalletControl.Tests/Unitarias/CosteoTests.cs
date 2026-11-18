using PalletControl.Dominio.Calculo;
using Xunit;

namespace PalletControl.Tests.Unitarias;

/// <summary>Casos obligatorios 1–7 del módulo de costos, con resultados verificados de forma independiente.</summary>
public class CosteoTests
{
    // Caso 1: pallet comprado en USD con un gasto adicional no incluido y otro incluido.
    [Fact]
    public void Caso01_PalletEnUsd_InversionTotal()
    {
        var costoMxn = Dinero.AMxn(2500.00m, "USD", 18.7147m);
        Assert.Equal(46786.75m, costoMxn);
        var inversion = InversionPallet.Calcular(costoMxn,
        [
            new GastoPallet(1200.00m, IncluidoEnCosto: false), // se suma
            new GastoPallet(500.00m, IncluidoEnCosto: true),   // ya venía en el precio: NO se suma
        ]);
        Assert.Equal(47986.75m, inversion);
    }

    // Caso 2: conversión USD→MXN con redondeo al centavo (23,104.420032 → 23,104.42).
    [Theory]
    [InlineData(1234.56, "USD", 18.7147, 23104.42)]
    [InlineData(100.005, "MXN", 0, 100.01)]     // MXN no usa tipo de cambio; redondeo comercial
    [InlineData(0.01, "USD", 18.7147, 0.19)]    // 0.187147 → 0.19
    public void Caso02_ConversionUsdMxn(double monto, string moneda, double tc, double esperado) =>
        Assert.Equal((decimal)esperado, Dinero.AMxn((decimal)monto, moneda, (decimal)tc));

    [Fact]
    public void Caso02_TipoDeCambioInvalido() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Dinero.AMxn(10m, "USD", 0m));

    // Caso 3: distribución equitativa — 1,000.00 entre 7 piezas (3 + 4).
    [Fact]
    public void Caso03_Equitativa()
    {
        var r = DistribucionCostos.Calcular(1000.00m, [new("A", 3, true), new("B", 4, true)], MetodoCosteo.Equitativa, ModoMerma.Absorber);
        Assert.True(r.Ok);
        Assert.Equal(428.57m, Lote(r, "A").CostoTotal);
        Assert.Equal(571.43m, Lote(r, "B").CostoTotal);   // recibe el centavo de residuo mayor
        Assert.Equal(1, r.CentavosAjustados);
        Assert.Equal(142.8567m, Lote(r, "A").CostoUnitario);
        Assert.Equal(142.8575m, Lote(r, "B").CostoUnitario);
        Assert.Equal(1000.00m, r.TotalComercializable + r.TotalMerma);
    }

    // Caso 4: distribución proporcional al valor en México (mismo escenario que la versión anterior de la app).
    [Fact]
    public void Caso04_Proporcional()
    {
        var r = DistribucionCostos.Calcular(49000.00m,
        [
            new("L1", 12, true, 1099.00m),
            new("L2", 30, true, 150.00m),
            new("L3", 4, true, 1274.50m),
        ], MetodoCosteo.Proporcional, ModoMerma.Absorber);
        Assert.True(r.Ok);
        Assert.Equal(28360.05m, Lote(r, "L1").CostoTotal);
        Assert.Equal(9676.99m, Lote(r, "L2").CostoTotal);
        Assert.Equal(10962.96m, Lote(r, "L3").CostoTotal);
        Assert.Equal(2363.3375m, Lote(r, "L1").CostoUnitario);
        Assert.Equal(322.5663m, Lote(r, "L2").CostoUnitario);
        Assert.Equal(2740.7400m, Lote(r, "L3").CostoUnitario);
        Assert.Equal(49000.00m, r.TotalComercializable);
        Assert.Equal(0m, r.DiferenciaCuadre);
    }

    [Fact]
    public void Caso04_ProporcionalSinValorRelativo_EsError()
    {
        var r = DistribucionCostos.Calcular(1000m, [new("A", 1, true, 100m), new("B", 1, true, null)], MetodoCosteo.Proporcional, ModoMerma.Absorber);
        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("lote B"));
    }

    // Caso 5: asignación manual — debe cuadrar exacto con la inversión.
    [Fact]
    public void Caso05_Manual_Cuadra()
    {
        var r = DistribucionCostos.Calcular(1000.00m, [new("A", 2, true, CostoManualTotal: 600.00m), new("B", 1, true, CostoManualTotal: 400.00m)],
            MetodoCosteo.Manual, ModoMerma.Absorber);
        Assert.True(r.Ok);
        Assert.Equal(300.0000m, Lote(r, "A").CostoUnitario);
        Assert.Equal(1000.00m, r.TotalComercializable);
    }

    [Fact]
    public void Caso05_Manual_NoCuadra_ReportaDiferencia()
    {
        var r = DistribucionCostos.Calcular(1000.00m, [new("A", 2, true, CostoManualTotal: 600.00m), new("B", 1, true, CostoManualTotal: 399.99m)],
            MetodoCosteo.Manual, ModoMerma.Absorber);
        Assert.False(r.Ok);
        Assert.Contains(r.Errores, e => e.Contains("0.01"));
    }

    // Caso 6: productos dañados — absorber vs. reconocer como merma.
    [Fact]
    public void Caso06_Danados_Absorber()
    {
        var r = DistribucionCostos.Calcular(1200.00m, [new("A", 10, true, 100m), new("B", 2, false, 100m)], MetodoCosteo.Proporcional, ModoMerma.Absorber);
        Assert.True(r.Ok);
        Assert.Equal(1200.00m, Lote(r, "A").CostoTotal);
        Assert.Equal(120.0000m, Lote(r, "A").CostoUnitario);
        Assert.Equal(0m, Lote(r, "B").CostoTotal);       // el lote dañado sigue registrado, con costo 0
        Assert.Equal(200.00m, r.CostoAbsorbidoPorMerma);  // informativo
        Assert.Equal(0m, r.TotalMerma);
    }

    [Fact]
    public void Caso06_Danados_Perdida()
    {
        var r = DistribucionCostos.Calcular(1200.00m, [new("A", 10, true, 100m), new("B", 2, false, 100m)], MetodoCosteo.Proporcional, ModoMerma.Perdida);
        Assert.True(r.Ok);
        Assert.Equal(1000.00m, r.TotalComercializable);
        Assert.Equal(200.00m, r.TotalMerma);
        Assert.True(Lote(r, "B").EsMerma);
    }

    [Fact]
    public void Caso06_SinPiezasComercializables_EsError()
    {
        var r = DistribucionCostos.Calcular(500m, [new("B", 2, false)], MetodoCosteo.Equitativa, ModoMerma.Perdida);
        Assert.False(r.Ok);
    }

    // Caso 7: validación del costo total distribuido — tres partes iguales de 100.00.
    [Fact]
    public void Caso07_RedondeoCuadraYEsReproducible()
    {
        var lotes = new List<LoteCosteo> { new("L1", 1, true), new("L2", 1, true), new("L3", 1, true) };
        var r1 = DistribucionCostos.Calcular(100.00m, lotes, MetodoCosteo.Equitativa, ModoMerma.Absorber);
        var r2 = DistribucionCostos.Calcular(100.00m, lotes, MetodoCosteo.Equitativa, ModoMerma.Absorber);
        Assert.Equal([33.34m, 33.33m, 33.33m], r1.Lotes.Select(l => l.CostoTotal));
        Assert.Equal(r1.Lotes.Select(l => l.CostoTotal), r2.Lotes.Select(l => l.CostoTotal));
    }

    [Fact]
    public void Caso07_CuadreAlCentavo_EnMilEscenariosAleatorios()
    {
        var rnd = new Random(20261118);
        for (var n = 0; n < 1000; n++)
        {
            var total = rnd.Next(0, 10_000_000) / 100m;
            var lotes = Enumerable.Range(0, rnd.Next(1, 40))
                .Select(i => new LoteCosteo($"L{i:000}", rnd.Next(1, 50), i == 0 || rnd.Next(5) > 0, rnd.Next(1, 500_000) / 100m)).ToList();
            foreach (var metodo in new[] { MetodoCosteo.Equitativa, MetodoCosteo.Proporcional })
                foreach (var modo in new[] { ModoMerma.Absorber, ModoMerma.Perdida })
                {
                    var r = DistribucionCostos.Calcular(total, lotes, metodo, modo);
                    Assert.True(r.Ok, string.Join("; ", r.Errores));
                    Assert.Equal(total, r.Lotes.Sum(l => l.CostoTotal));
                    Assert.Equal(total, r.TotalComercializable + r.TotalMerma);
                    Assert.All(r.Lotes, l => Assert.True(l.CostoTotal >= 0 && l.CostoTotal == Dinero.Centavos(l.CostoTotal)));
                }
        }
    }

    private static LoteCosteado Lote(ResultadoCosteo r, string id) => r.Lotes.Single(l => l.LoteId == id);
}
