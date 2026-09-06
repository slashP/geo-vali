using GeoVali.Autostart;
using GeoVali.Configuration;
using GeoVali.Geoguessr;
using GeoVali.Running;
using GeoVali.Vali;

namespace GeoVali.Web;

public static class ServiceRegistration
{
    public static IServiceCollection AddGeoVali(this IServiceCollection services, string configDirectory)
    {
        services.AddSingleton(new ConfigStore(configDirectory));
        services.AddSingleton(_ => new CredentialStore(configDirectory, CredentialProtectorFactory.Create()));

        services.AddSingleton(provider => new RunLog(
            Path.Combine(configDirectory, "logs"),
            () => provider.GetRequiredService<CredentialStore>().ReadCookie()));

        services.AddSingleton<IValiRunner>(provider =>
            new ValiRunner(provider.GetRequiredService<ConfigStore>().Current.valiExecutablePath));

        // A DelegatingHandler attaches the cookie per request, reading the store each time, so a
        // freshly pasted cookie takes effect without restarting the tool and without any singleton
        // holding a stale header.
        services.AddTransient<TransientRetryHandler>(_ => new TransientRetryHandler());
        services.AddTransient<CookieHandler>();
        services.AddHttpClient<IGeoguessrClient, GeoguessrClient>(http =>
            {
                http.BaseAddress = new Uri(GeoguessrClient.BaseAddress);
                http.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddHttpMessageHandler<CookieHandler>()
            .AddHttpMessageHandler<TransientRetryHandler>();

        services.AddSingleton(provider => new UpdateRunner(
            provider.GetRequiredService<IValiRunner>(),
            provider.GetRequiredService<IGeoguessrClient>(),
            provider.GetRequiredService<RunLog>(),
            () => provider.GetRequiredService<ConfigStore>().Current.mapsRoot,
            () => provider.GetRequiredService<ConfigStore>().Current.defaultCadenceDays,
            () => DateTime.Now,
            name => provider.GetRequiredService<RunCoordinator>().CurrentMapName = name));

        services.AddSingleton(provider => new RunCoordinator(
            provider.GetRequiredService<UpdateRunner>(),
            provider.GetRequiredService<RunLog>()));

        services.AddSingleton<IAutostart>(_ => AutostartFactory.Create(
            AutostartFactory.CurrentExecutablePath(),
            AutostartFactory.DefaultStateDirectory()));

        services.AddHostedService<Scheduler>();

        return services;
    }
}
