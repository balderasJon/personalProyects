using PalletControl.Dominio.Integracion;
using Xunit;

namespace PalletControl.Tests.Unitarias;

/// <summary>Fase 5: propuestas de sincronización (puras, sin llamar a ninguna API). Datos de prueba.</summary>
public class PlanificadorTests
{
    private static readonly CapacidadesEscritura Todo = new(true, true, true, true);

    private static Dictionary<string, PublicacionRemota> R(params PublicacionRemota[] r) => r.ToDictionary(x => x.IdExterno);

    [Fact]
    public void SinPiezas_PropónPausar_OExistenciasCeroSiNoSePuedePausar()
    {
        var l = new[] { new PublicacionLocal("p1", "j1", "MLM1", "vendido", 639m, 0) };
        var rem = R(new PublicacionRemota("MLM1", "Rompecabezas", 639m, 1, "activa"));
        var c = Assert.Single(Planificador.Publicaciones(l, rem, Todo));
        Assert.Equal("pausar", c.Tipo);
        var sinPausa = Assert.Single(Planificador.Publicaciones(l, rem, Todo with { Pausar = false }));
        Assert.Equal("existencias", sinPausa.Tipo);
        Assert.Equal(0, sinPausa.Existencias);
    }

    [Fact]
    public void ExistenciasDeMas_PrecioDistinto_YReactivar()
    {
        var l = new[]
        {
            new PublicacionLocal("p1", "j1", "MLM1", "publicado", 649m, 2),
            new PublicacionLocal("p2", "j2", "MLM2", "publicado", 300m, 3),
        };
        var rem = R(new PublicacionRemota("MLM1", "Rompecabezas", 639m, 5, "activa"), new PublicacionRemota("MLM2", "Muñeca", 300m, 3, "pausada"));
        var c = Planificador.Publicaciones(l, rem, Todo);
        Assert.Equal(2, c.Single(x => x.Tipo == "existencias").Existencias);
        Assert.Equal(649m, c.Single(x => x.Tipo == "precio").Precio);
        Assert.Equal("p2", c.Single(x => x.Tipo == "activar").PublicacionId);
        // Sin capacidad de precio no se propone
        Assert.DoesNotContain(Planificador.Publicaciones(l, rem, Todo with { Precio = false }), x => x.Tipo == "precio");
    }

    [Fact]
    public void Coinciden_NoProponeNada_YVariacionesOAusentesSeBloquean()
    {
        var l = new[]
        {
            new PublicacionLocal("p1", "j1", "MLM1", "publicado", 639m, 2),
            new PublicacionLocal("p2", "j2", "MLM2", "publicado", 300m, 1),
            new PublicacionLocal("p3", "j3", "MLM3", "publicado", 300m, 1),
        };
        var rem = R(new PublicacionRemota("MLM1", "A", 639m, 2, "activa"), new PublicacionRemota("MLM2", "B", 300m, 9, "activa", ConVariaciones: true));
        var c = Planificador.Publicaciones(l, rem, Todo);
        Assert.Equal(2, c.Count);
        Assert.Equal("variaciones", c.Single(x => x.PublicacionId == "p2").Bloqueo);
        Assert.Equal("no_encontrada", c.Single(x => x.PublicacionId == "p3").Bloqueo);
    }

    private static readonly Dictionary<string, (string, string)> Vinculos = new() { ["MLM1"] = ("p1", "j1") };

    [Fact]
    public void PedidoPagado_NuevaVentaConComisionYEnvioReales()
    {
        var ped = new PedidoRemoto("2001", new DateOnly(2026, 11, 18), "pagado", [new LineaRemota("MLM1", "Rompecabezas", 1, 639m, 95.85m)], 639m, 120m);
        var c = Assert.Single(Planificador.Pedidos([ped], [], Vinculos));
        Assert.Equal("nueva_venta", c.Tipo);
        Assert.Equal("2001/MLM1", c.Referencia);
        Assert.Equal("j1", c.ProductoId);
        Assert.Null(c.Bloqueo);
        Assert.Equal([95.85m, 120m], c.Gastos!.Select(g => g.Monto));
        Assert.All(c.Gastos!, g => Assert.True(g.DeducidoPorPlataforma));
        // Pedidos no pagados no se importan
        Assert.Empty(Planificador.Pedidos([ped with { Estatus = "pendiente" }], [], Vinculos));
    }

    [Fact]
    public void PedidoSinVinculo_SeBloquea()
    {
        var ped = new PedidoRemoto("2002", new DateOnly(2026, 11, 18), "pagado", [new LineaRemota("MLM9", "Otro", 1, 100m, 10m)], 100m, null);
        Assert.Equal("sin_vinculo", Assert.Single(Planificador.Pedidos([ped], [], Vinculos)).Bloqueo);
    }

    [Fact]
    public void Importada_CobroEstimado_OCancelacion()
    {
        var ped = new PedidoRemoto("2001", new DateOnly(2026, 11, 18), "pagado", [new LineaRemota("MLM1", "Rompecabezas", 1, 639m, 95.85m)], 639m, 120m);
        var imp = new VentaImportada("v1", "2001/MLM1", "pagado", false);
        var cobro = Assert.Single(Planificador.Pedidos([ped], [imp], Vinculos));
        Assert.Equal("registrar_cobro", cobro.Tipo);
        Assert.Equal(423.15m, cobro.Importe);   // 639 − 95.85 − 120
        Assert.Empty(Planificador.Pedidos([ped], [imp with { Cobrada = true }], Vinculos));
        var can = Assert.Single(Planificador.Pedidos([ped with { Estatus = "cancelado" }], [imp with { Cobrada = true }], Vinculos));
        Assert.Equal("cancelar_venta", can.Tipo);
        Assert.Equal("v1", can.VentaId);
        Assert.Empty(Planificador.Pedidos([ped with { Estatus = "cancelado" }], [imp with { Estado = "cancelado" }], Vinculos));
    }
}
