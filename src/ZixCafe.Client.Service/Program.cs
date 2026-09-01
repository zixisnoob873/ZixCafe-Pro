using ZixCafe.Client.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "ZixCafeWatchdog");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
