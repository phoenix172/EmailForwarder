namespace EmailForwarder
{
    public class Program
    {
        public static void Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(x=>x.AddConsole())
                .ConfigureServices(ConfigureServices)
                .Build();

            host.Run();
        }

        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            var settings = context.Configuration.Get<AppSettings>() ?? throw new InvalidOperationException("Invalid appsettings.json file");

            services.AddSingleton(settings)
                .AddSingleton(settings.ReceiverSettings)
                .AddSingleton(settings.SenderSettings);

            foreach (var rule in settings.ForwardingRules)
            {
                services.AddSingleton(svc => 
                    new EmailForwarder(
                        rule,
                        svc.GetRequiredService<ReceiverSettings>(),
                        svc.GetRequiredService<SenderSettings>(),
                        svc.GetRequiredService<ILogger<EmailForwarder>>()));
            }

            services.AddHostedService<EmailForwardingWorker>();
        }
    }
}