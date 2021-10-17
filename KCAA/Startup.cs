using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
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

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            _telegramSettings = _configuration.GetSection(TelegramSettings.ConfigKey).Get<TelegramSettings>();

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
            var botClient = GetBotClient();
            _container.RegisterInstance(botClient);

            _container.Register<ITelegramUpdateHandler, TelegramUpdateHandler>();
        }

        private ITelegramBotClient GetBotClient()
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
