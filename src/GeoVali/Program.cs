using Microsoft.Extensions.FileProviders;
using GeoVali;
using GeoVali.Configuration;
using GeoVali.Startup;
using GeoVali.Web;

var noBrowser = args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(args);

// Tests point this at a temp folder; normally it is the per-OS application data location.
var configDirectory = builder.Configuration["GeoVali:ConfigDirectory"] ?? AppPaths.ConfigDirectory;
Directory.CreateDirectory(configDirectory);

builder.Services.AddGeoVali(configDirectory);

// WebApplicationFactory supplies its own server, so only bind a real port outside tests.
var isTestHost = builder.Configuration["GeoVali:ConfigDirectory"] is not null;
var url = "";
if (!isTestHost)
{
    var preferred = new ConfigStore(configDirectory).Read().dashboardPort;
    var plan = await StartupPlan.CreateAsync(preferred, TimeSpan.FromMilliseconds(500));

    // Starting a second copy would mean a second scheduler regenerating and publishing the same
    // maps. Hand the user over to the one already running instead.
    if (plan.AlreadyRunning)
    {
        Console.WriteLine(noBrowser
            ? $"{AppInfo.ProductName} is already running at {plan.Url}"
            : $"{AppInfo.ProductName} is already running — opening {plan.Url}");

        if (!noBrowser)
        {
            BrowserLauncher.Open(plan.Url);
        }

        return;
    }

    url = plan.Url;
    builder.WebHost.UseUrls(url);
}

var app = builder.Build();

var embedded = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");

app.UseLocalOnly();
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = embedded });
app.UseStaticFiles(new StaticFileOptions { FileProvider = embedded });
app.MapGeoValiApi();

if (!isTestHost)
{
    Console.WriteLine($"{AppInfo.ProductName} {AppInfo.Version} — dashboard at {url}");
    Console.WriteLine("Press Ctrl+C to quit.");
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (!noBrowser)
        {
            BrowserLauncher.Open(url);
        }
    });
}

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
