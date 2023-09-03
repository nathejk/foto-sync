using FotoSync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

Console.WriteLine("Starter...");

CreateHostBuilder(args).Build().Run();

Console.WriteLine("Tryk på en tast for at stoppe");

Console.ReadKey();

static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureHostConfiguration(cfg =>
        {
            cfg.AddJsonFile("appsettings.json");
        })
        .ConfigureServices(
            (_, services) =>
            {
                services
                    .AddHttpClient(nameof(Synchronize))
                    .ConfigureHttpMessageHandlerBuilder(
                        builder =>
                            builder.PrimaryHandler = new HttpClientHandler()
                            {
                                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                            }
                    );
                // services.AddHttpClient();
                services.AddQuartz(q =>
                {
                    q.UseMicrosoftDependencyInjectionJobFactory();
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
