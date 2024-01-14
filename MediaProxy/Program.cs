using System.Net;
using MediaProxy;
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
                handler.Proxy = proxy == null ? null : new WebProxy(proxy);
                return handler;
            });
    })
    .ConfigureFunctionsWebApplication(app =>
    {
        app.UseMiddleware<BadHttpRequestMiddleware>();
        app.UseMiddleware<InformationalVersionMiddleware>();
    })
    .Build();

host.Run();