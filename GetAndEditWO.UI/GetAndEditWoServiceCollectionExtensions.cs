using GetAndEditWO.UI.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GetAndEditWO.UI;

public static class GetAndEditWoServiceCollectionExtensions
{
    public const string MesClientName = "MES";

    public static IServiceCollection AddGetAndEditWo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MesOptions>(configuration.GetSection("GetAndEditWo"));

        services.AddHttpClient(MesClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MesOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException("GetAndEditWo configuration requires a non-empty BaseUrl.");
            }

            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException("GetAndEditWo:BaseUrl must be a valid absolute URI.");
            }

            client.BaseAddress = baseUri;
        });

        return services;
    }
}
