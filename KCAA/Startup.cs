using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using SimpleInjector;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using KCAA.Services;
using KCAA.Settings;
using KCAA.Services.Interfaces;

namespace KCAA
{
    public class Startup
    {
        private readonly Container _container = new Container();
        private IConfiguration _configuration;
        private TelegramSettings _telegramSettings;
        private MongoDBSettings _mongoDBSettings;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            _telegramSettings = _configuration.GetSection(TelegramSettings.ConfigKey).Get<TelegramSettings>();
            _mongoDBSettings = _configuration.GetSection(MongoDBSettings.ConfigKey).Get<MongoDBSettings>();

            services.AddControllers().AddNewtonsoftJson();

            services.AddSimpleInjector(_container, options =>
            {
                options.AddAspNetCore().AddControllerActivation();
            });

            InitializeContainer();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Hello World!");
                });
            });

            _container.Verify();

            InitializePolling();
        }

        private void InitializeContainer()
        {
            _container.RegisterInstance(InitializeMongoDB());

            _container.RegisterInstance(InitializeBotClient());

            _container.Register<ITelegramUpdateHandler, TelegramUpdateHandler>();
        }

        private IMongoDatabase InitializeMongoDB()
        {
            var settings = MongoClientSettings.FromUrl(new MongoUrl(_mongoDBSettings.ConnectionString));
            var client = new MongoClient(settings);

            var db = client.GetDatabase(_mongoDBSettings.DatabaseName);

            return db;
        }

        private ITelegramBotClient InitializeBotClient()
        {
            ITelegramBotClient botClient = new TelegramBotClient(_telegramSettings.BotToken);

            botClient.SetMyCommandsAsync(_telegramSettings.BotCommands).GetAwaiter().GetResult();

            return botClient;
        }

        private void InitializePolling()
        {
            var botClient = _container.GetInstance<ITelegramBotClient>();
            var handler = _container.GetInstance<ITelegramUpdateHandler>();
            var cts = new CancellationTokenSource();

            botClient.StartReceiving(new DefaultUpdateHandler(handler.HandleUpdateAsync, handler.HandleErrorAsync), cts.Token);
        }
    }
}
