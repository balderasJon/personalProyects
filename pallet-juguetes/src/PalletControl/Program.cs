using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Endpoints;
using PalletControl.Models;
using PalletControl.Servicios;

// La raíz de la aplicación es la carpeta que contiene wwwroot/index.html: junto al ejecutable si está
// publicada, o la carpeta del proyecto (subiendo desde bin/Debug/net8.0) en desarrollo. Así funciona
// igual con dotnet run, F5 de VS Code / Visual Studio o doble clic, sin importar la carpeta de inicio.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = BuscarRaiz()
});

// Base de datos SQLite en un solo archivo (por defecto ./datos/pallet.db).
// Una ruta relativa se resuelve contra la carpeta de la aplicación, no contra donde se lanzó.
var csb = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Pallet") ?? "Data Source=datos/pallet.db");
if (!Path.IsPathRooted(csb.DataSource))
    csb.DataSource = Path.Combine(builder.Environment.ContentRootPath, csb.DataSource);
var dbPath = Path.GetFullPath(csb.DataSource);
var dbDir = Path.GetDirectoryName(dbPath)!;
Directory.CreateDirectory(dbDir);
csb.DataSource = dbPath;
var cs = csb.ToString();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(cs));
builder.Services.AddHttpClient<BuscadorCodigoBarras>(h =>
{
    h.Timeout = TimeSpan.FromSeconds(12);
    h.DefaultRequestHeaders.UserAgent.ParseAdd("PalletControl/1.0");
    h.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

builder.Services.AddHttpClient<TipoCambioDof>(h =>
{
    h.Timeout = TimeSpan.FromSeconds(10);
    h.DefaultRequestHeaders.UserAgent.ParseAdd("PalletControl/1.0");
    h.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddHttpClient("imagenes", h =>
{
    h.Timeout = TimeSpan.FromSeconds(15);
    h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PalletControl/1.0)");
    h.DefaultRequestHeaders.Accept.ParseAdd("image/*");
});
var imgDir = Path.Combine(dbDir, "imagenes");
Directory.CreateDirectory(imgDir);
builder.Services.AddSingleton(new AlmacenImagenes(imgDir));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 30 * 1024 * 1024);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    Esquema.Actualizar(db, Path.Combine(dbDir, "respaldos"));
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    if (db.Configuracion.Find(1) is null) db.Configuracion.Add(new Configuracion());
    if (db.Version.Find(1) is null) db.Version.Add(new VersionDatos());
    db.SaveChanges();

    // Respaldo automático al arrancar: una copia por día, se conservan las últimas N.
    var conservar = app.Configuration.GetValue("Respaldos:Conservar", 14);
    if (conservar > 0)
    {
        var dir = Path.Combine(dbDir, "respaldos");
        Directory.CreateDirectory(dir);
        var destino = Path.Combine(dir, $"pallet-{DateTime.Now:yyyy-MM-dd}.db");
        if (!File.Exists(destino))
            db.Database.ExecuteSqlRaw("VACUUM INTO {0}", destino);
        foreach (var viejo in Directory.GetFiles(dir, "pallet-*.db").OrderDescending().Skip(conservar))
            File.Delete(viejo);
    }
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(imgDir),
    RequestPath = "/imagenes"
});
app.MapApi();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var puerto = app.Urls.Select(u => new Uri(u.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost")).Port).FirstOrDefault(5080);
    Console.WriteLine();
    Console.WriteLine("  Control Pallet de Juguetes en ejecución");
    Console.WriteLine($"  En esta computadora:  http://localhost:{puerto}");
    foreach (var ip in IpsLocales())
        Console.WriteLine($"  Desde la red local:   http://{ip}:{puerto}");
    Console.WriteLine($"  Base de datos:        {Path.GetFullPath(dbPath)}");
    Console.WriteLine("  Para detener: Ctrl+C");
    Console.WriteLine();
});

app.Run();

static string? BuscarRaiz()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; dir is not null && i < 6; i++, dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "wwwroot", "index.html")))
            return dir.FullName;
    return null; // se usa la carpeta actual
}

static IEnumerable<IPAddress> IpsLocales() =>
    NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
