using PalletControl.Dominio.Calculo;
using Xunit;

namespace PalletControl.Tests.Unitarias;

/// <summary>Costo estimado al escanear un juguete, antes del costeo del pallet. Datos de prueba.</summary>
public class EstimacionCostoTests
{
    [Fact]
    public void Referencia_PromedioBajoAltoPorTipoDeCambio()
    {
        Assert.Equal(553.85m, EstimacionCosto.Referencia(24.99m, 34.99m, 18.4677m));   // promedio 29.99 × 18.4677 = 553.8463 → $553.85
    }

    [Fact]
    public void Referencia_UnSoloPrecioOSinTipoDeCambio()
    {
        Assert.Equal(369.35m, EstimacionCosto.Referencia(null, 20m, 18.4677m));
        Assert.Null(EstimacionCosto.Referencia(10m, 20m, 0m));
        Assert.Null(EstimacionCosto.Referencia(null, null, 18m));
    }

    [Fact]
    public void SinValorDeMercadoDelPallet_SeRepartenEntreLosRegistrados()
    {
        // Pallet $1,000; ya registrado 1 juguete de $600; se escanea uno de $400 → base $1,000 → costo $400.
        var e = EstimacionCosto.Calcular(1000m, null, [(1, 600m)], 1, 400m)!;
        Assert.Equal(400m, e.CostoUnitario);
        Assert.Equal(1000m, e.BaseReparto);
        Assert.False(e.UsaValorMercadoPallet);
        Assert.Equal(2, e.PiezasRegistradas);
        // Primer juguete del pallet: absorbe toda la inversión (por eso conviene el valor de mercado del pallet)
        Assert.Equal(1000m, EstimacionCosto.Calcular(1000m, null, [], 1, 400m)!.CostoUnitario);
    }

    [Fact]
    public void ConValorDeMercadoDelPallet_EstimadoEstable()
    {
        var e = EstimacionCosto.Calcular(1000m, 5000m, [(1, 600m)], 1, 400m)!;
        Assert.Equal(80m, e.CostoUnitario);   // 1,000 × 400 ÷ 5,000
        Assert.True(e.UsaValorMercadoPallet);
        // Si lo registrado ya supera el valor del manifiesto, se usa lo registrado
        Assert.Equal(400m, EstimacionCosto.Calcular(1000m, 800m, [(1, 600m)], 1, 400m)!.CostoUnitario);
    }

    [Fact]
    public void PiezasSinValor_NoEntranAlReparto()
    {
        var e = EstimacionCosto.Calcular(1000m, null, [(3, null), (1, 600m)], 2, 200m)!;
        Assert.Equal(3, e.PiezasSinValor);
        Assert.Equal(200m, e.CostoUnitario);   // base 600 + 400 = 1,000
        Assert.Null(EstimacionCosto.Calcular(1000m, null, [], 1, 0m));
    }
}
