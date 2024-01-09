using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddHttpClient("proxy")
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
                var proxy = context.Configuration.GetValue<string>("HttpClientProxy");
                if (proxy != null)
                {
                    handler.Proxy = new WebProxy(proxy);
                }

                return handler;
            });
    })
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();