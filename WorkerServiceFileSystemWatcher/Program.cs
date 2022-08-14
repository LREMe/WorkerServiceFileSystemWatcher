using WorkerServiceFileSystemWatcher;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();
        services.Configure<AppSettings>(hostContext.Configuration.GetSection("AppSettings"));
    })
    .Build();

await host.RunAsync();
