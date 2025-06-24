using StackExchange.Redis;
using System;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;
using ConsoleApp.Test;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SeedWork.Redis;

namespace RedisTestApp
{
    
    class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;

                    config.SetBasePath(Directory.GetCurrentDirectory());
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IRedisService, RedisService>();
                    services.AddTransient<RedisTestRunner>();
                });
            
            var host = builder.Build();
            var runner = host.Services.GetRequiredService<RedisTestRunner>();
            await runner.RunAsync();
        }
    }
}