using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GeoVali;
using GeoVali.Tests.Support;
using Xunit;

namespace GeoVali.Tests;

/// <summary>
/// A global tool's content root is the directory the user launched it from, so an appsettings.json
/// shipped beside the DLL is never read. Log levels have to be set in code to have any effect,
/// and these pin them: the dashboard polls, so framework chatter at Information floods the console
/// the user is watching for the two lines GeoVali prints itself.
/// </summary>
public class LoggingTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly WebApplicationFactory<Program> _factory;

    public LoggingTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("GeoVali:ConfigDirectory", _temp.Path);
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });
    }

    private ILogger Logger(string category) =>
        _factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]                    // "Request starting/finished"
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]             // "Executing endpoint"
    [InlineData("Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware")]       // "Sending file"
    [InlineData("Microsoft.AspNetCore.Http.Result.JsonResult")]                 // "Writing value of type ... as Json"
    [InlineData("Microsoft.Hosting.Lifetime")]                                  // "Now listening on", "Content root path"
    [InlineData("System.Net.Http.HttpClient.IGeoguessrClient.LogicalHandler")]  // a pair per GeoGuessr call
    public void Routine_framework_chatter_never_reaches_the_console(string category) =>
        Assert.False(Logger(category).IsEnabled(LogLevel.Information), $"{category} still logs at Information.");

    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Microsoft.Extensions.Hosting.Internal.Host")]
    [InlineData("GeoVali")]
    public void Problems_still_reach_the_console(string category) =>
        Assert.True(Logger(category).IsEnabled(LogLevel.Warning), $"{category} is silent at Warning.");

    public void Dispose()
    {
        _factory.Dispose();
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }
}
