using System.Reflection;
using BiboWebBot.HomeAssistant.Services;
using BiboWebBot.Mqtt;
using BiboWebBot.VoebbParsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetDaemon.AppModel;
using NetDaemon.Extensions.Logging;
using NetDaemon.Extensions.Scheduler;
using NetDaemon.Extensions.Tts;
using NetDaemon.Runtime;
using HomeAssistantGenerated;

#pragma warning disable CA1812

try
{
    await Host.CreateDefaultBuilder(args)
        .UseNetDaemonAppSettings()
        .UseNetDaemonDefaultLogging()
        .UseNetDaemonRuntime()
        .UseNetDaemonTextToSpeech()
        .ConfigureServices((_, services) =>
            services
                .AddAppsFromAssembly(Assembly.GetExecutingAssembly())
                .AddNetDaemonStateManager()
                .AddNetDaemonScheduler()
                .AddHomeAssistantGenerated()
                .AddScoped<IVoebbAutomationService, VoebbAutomationService>()
                .AddScoped<IMqttPublishService, MqttPublishService>()
                .AddHostedService<BiboLoanSyncHostedService>()
        )
        .Build()
        .RunAsync()
        .ConfigureAwait(false);
}
catch (Exception e)
{
    Console.WriteLine($"Failed to start host... {e}");
    throw;
}
