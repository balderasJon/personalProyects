using PalletControl.Dominio.Calculo;
using Xunit;

namespace PalletControl.Tests.Unitarias;

/// <summary>Fase 4: competencia e indicadores. Precios de competencia de PRUEBA (precio total = publicado + envío).</summary>
public class IndicadoresTests
{
    private static readonly decimal[] Mercado = [599m, 649m, 699m, 750m];

    [Fact]
    public void Estadistica_MinMaxPromedioMediana()
    {
        var e = Indicadores.Estadistica(Mercado)!;
        Assert.Equal(4, e.Observaciones);
        Assert.Equal(599m, e.Minimo);
        Assert.Equal(750m, e.Maximo);
        Assert.Equal(674.25m, e.Promedio);
        Assert.Equal(674m, e.Mediana);       // (649 + 699) / 2
        Assert.Equal(649m, Indicadores.Estadistica([750m, 599m, 649m])!.Mediana);   // impar
        Assert.Null(Indicadores.Estadistica([]));
    }

    [Theory]
    [InlineData(639, -5.19, "barato", 2)]      // (639 − 674) / 674
    [InlineData(659, -2.23, "en_mercado", 3)]
    [InlineData(674, 0, "en_mercado", 3)]
    [InlineData(710, 5.34, "caro", 4)]
    [InlineData(800, 18.69, "caro", 5)]
    public void Posicion_ConBandaDe5(decimal propio, decimal dif, string pos, int lugar)
    {
        var p = Indicadores.Posicion(propio, Mercado, 5m)!;
        Assert.Equal(dif, p.DiferenciaPct);
        Assert.Equal(pos, p.Posicion);
        Assert.Equal(lugar, p.Lugar);
        Assert.Equal(5, p.Total);
    }

    [Fact]
    public void Posicion_SinDatosOPrecio()
    {
        Assert.Null(Indicadores.Posicion(639m, [], 5m));
        Assert.Null(Indicadores.Posicion(0m, Mercado, 5m));
        Assert.Equal("barato", Indicadores.Posicion(639m, Mercado, 5m)!.Posicion);
        Assert.Equal("en_mercado", Indicadores.Posicion(639m, Mercado, 10m)!.Posicion);   // la banda es configurable
    }

    [Fact]
    public void Antiguedad_PonderadaPorPieza()
    {
        var corte = new DateOnly(2026, 11, 18);
        var a = Indicadores.Antiguedad([
            new PiezaInventario(2, new DateOnly(2026, 11, 1)),   // 17 días
            new PiezaInventario(3, new DateOnly(2026, 9, 1)),    // 78 días
            new PiezaInventario(1, new DateOnly(2026, 7, 1)),    // 140 días
            new PiezaInventario(0, new DateOnly(2020, 1, 1)),    // sin piezas: no cuenta
        ], corte);
        Assert.Equal(68.0m, a.DiasPromedio);   // (34 + 234 + 140) / 6
        Assert.Equal((2, 0, 3, 1), (a.Piezas0a30, a.Piezas31a60, a.Piezas61a90, a.PiezasMas90));
    }

    [Fact]
    public void SinMovimiento_RotacionYRoi()
    {
        var corte = new DateOnly(2026, 11, 18);
        Assert.Equal(48, Indicadores.DiasSinMovimiento(new DateOnly(2026, 10, 1), null, corte));
        Assert.Equal(8, Indicadores.DiasSinMovimiento(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 10), corte));
        Assert.Equal(33.33m, Indicadores.RotacionPct(1, 3));
        Assert.Null(Indicadores.RotacionPct(0, 0));
        Assert.Equal(11.44m, Indicadores.Roi(72.25m, 631.80m));
        Assert.Null(Indicadores.Roi(10m, 0m));
    }
}
