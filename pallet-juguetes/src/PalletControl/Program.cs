using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PalletControl.Data;
using PalletControl.Endpoints;
using PalletControl.Models;

// Publicada (wwwroot junto al ejecutable): la carpeta del ejecutable es la raíz, aunque se lance desde otro lado.
// En desarrollo (dotnet run) la raíz es la carpeta del proyecto.
var publicada = File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = publicada ? AppContext.BaseDirectory : null
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
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

static IEnumerable<IPAddress> IpsLocales() =>
    NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
