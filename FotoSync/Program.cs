using FotoSync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();

Log.Information("Starter.. Tryk CTRL-C for at stoppe");

CreateHostBuilder(args).Build().Run();

Log.CloseAndFlush();

static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureHostConfiguration(cfg =>
        {
            cfg.AddJsonFile("appsettings.json");
        })
        .ConfigureServices(
            (_, services) =>
            {
                services
                    .AddHttpClient(nameof(Synchronize))
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler(){ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator});
                services.AddQuartz(q =>
                {
                    q.ScheduleJob<Synchronize>(
                        t =>
                            t.WithIdentity("every30s")
                                .StartNow()
                                .WithSimpleSchedule(
                                    s => s.WithIntervalInSeconds(30).RepeatForever()
                                )
                    );
                });
                services.AddTransient<Synchronize>();
                services.AddQuartzHostedService(options =>
                {
                    options.AwaitApplicationStarted = false;
                    options.StartDelay = TimeSpan.FromSeconds(2);
                    options.WaitForJobsToComplete = true;
                });
                services.AddLogging(opt =>
                {
                    opt.AddSimpleConsole(c =>
                    {
                        c.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                    });
                });
            }
        );
