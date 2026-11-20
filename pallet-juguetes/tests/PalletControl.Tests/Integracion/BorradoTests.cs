using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PalletControl.Tests.Integracion;

/// <summary>«Borrar todos los datos»: quita lo capturado, guarda respaldo y conserva la configuración.</summary>
public class BorradoTests : IClassFixture<AppFactory>
{
    private readonly AppFactory f;
    public BorradoTests(AppFactory f) => this.f = f;

    [Fact]
    public async Task BorrarTodo_PideConfirmacion_RespaldaYDejaLaConfiguracion()
    {
        var c = await f.ClienteConSesionAsyncUnaVez();
        var pal = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "Prueba", moneda = "MXN", costoOriginal = 1000m, tipoCambio = 1m })).Json(201)).Str("id");
        var prod = (await (await c.PostAsJsonAsync("/api/productos", new { codigo = "B-1", nombre = "Peluche", cantidad = 3, palletId = pal, valorReferencia = 400m, precioObjetivo = 500m })).Json(201)).Str("id");
        using (var foto = new MultipartFormDataContent())
        {
            foto.Add(new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]), "archivo", "f.png");
            await c.PostAsync($"/api/productos/{prod}/imagenes", foto);
        }
        await (await c.PostAsJsonAsync($"/api/pallets/{pal}/costeo/aplicar", new { metodo = "proporcional" })).Json();
        var plats = (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").GetArrayLength();
        await (await c.PostAsJsonAsync("/api/ventas", new { productoId = prod, cantidad = 1, precio = 500m, estado = "entregado", fecha = "2026-11-20" })).Json(201);
        await (await c.PutAsJsonAsync("/api/configuracion", new { inversion = 1000m, stockBajo = 3, metaMensual = 5000m })).Json();

        // Sin la palabra de confirmación no borra nada
        var mal = await c.PostAsJsonAsync("/api/datos/borrar-todo", new { confirmacion = "borrar" });
        Assert.Equal(HttpStatusCode.BadRequest, mal.StatusCode);
        Assert.Single((await (await c.GetAsync("/api/estado")).Json()).GetProperty("productos").EnumerateArray());

        var r = await (await c.PostAsJsonAsync("/api/datos/borrar-todo", new { confirmacion = "BORRAR" })).Json();
        Assert.True(File.Exists(r.Str("respaldo")));

        var e = await (await c.GetAsync("/api/estado")).Json();
        foreach (var k in new[] { "productos", "ventas", "pallets", "lotes", "publicaciones", "gastos" })
            if (e.TryGetProperty(k, out var arr)) Assert.Equal(0, arr.GetArrayLength());
        // Se conserva la configuración y el catálogo de plataformas
        Assert.Equal(3, e.GetProperty("configuracion").GetProperty("stockBajo").GetInt32());
        Assert.Equal(plats, (await (await c.GetAsync("/api/plataformas")).Json()).GetProperty("plataformas").GetArrayLength());

        // Y se puede volver a capturar desde cero
        var pal2 = (await (await c.PostAsJsonAsync("/api/pallets", new { nombre = "Real", moneda = "MXN", costoOriginal = 2000m, tipoCambio = 1m })).Json(201)).Str("id");
        await (await c.PostAsJsonAsync("/api/productos", new { codigo = "001", nombre = "Muñeca", cantidad = 1, palletId = pal2, valorReferencia = 300m })).Json(201);
    }
}
