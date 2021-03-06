using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using SimpleInjector;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using KCAA.Settings;
using KCAA.Services.Interfaces;
using KCAA.Services.Factories;
using KCAA.Settings.GameSettings;
using KCAA.Services.Builders;
using KCAA.Models.Quarters;
using KCAA.Models.Characters;
using KCAA.Models;
using KCAA.Services.Providers;
using KCAA.Extensions;
using KCAA.Services.TelegramApi;
using Telegram.Bot.Types.Enums;
using KCAA.Services.TelegramApi.TelegramUpdateHandlers;

namespace KCAA
{
    public class Startup
    {
        private readonly Container _container = new();
        private readonly IConfiguration _configuration;
        private TelegramSettings _telegramSettings;
        private GameSettings _gameSettings;
        private MongoDBSettings _mongoDBSettings;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            _telegramSettings = _configuration.GetSection(TelegramSettings.ConfigKey).Get<TelegramSettings>();
            _mongoDBSettings = _configuration.GetSection(MongoDBSettings.ConfigKey).Get<MongoDBSettings>();
            _gameSettings = _configuration.GetSection(GameSettings.ConfigKey).Get<GameSettings>();

            services.AddControllers().AddNewtonsoftJson();

            services.AddHealthChecks().AddMongoDb(_mongoDBSettings.ConnectionString);

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
                endpoints.MapControllers();
                endpoints.MapCustomHealthChecks();
            });

            _container.Verify();

            RegisterGameObjects<Quarter>(_gameSettings.QuarterSettingsPath).GetAwaiter().GetResult();
            RegisterGameObjects<CharacterBase>(_gameSettings.CharacterSettingsPath).GetAwaiter().GetResult();

            InitializePolling();
        }

        private void InitializeContainer()
        {
            _container.Register(typeof(ICardBuilder<>), typeof(CardBuilder<>));

            _container.Register<ICardFactory<Quarter>, QuarterFactory>(Lifestyle.Singleton);
            _container.Register<ICardFactory<CharacterBase>, CharacterFactory>(Lifestyle.Singleton);

            //register settings
            _container.RegisterInstance(_telegramSettings);
            _container.RegisterInstance(_gameSettings);

            //register mongo db dependencies
            _container.RegisterInstance(InitializeMongoDB());

            _container.Register<ILobbyProvider, LobbyProvider>();
            _container.Register<IPlayerProvider, PlayerProvider>();

            //register telegram dependencies
            _container.RegisterInstance(InitializeBotClient());

            _container.Register<ITelegramUpdateGateway, TelegramUpdateGateway>();

            _container.RegisterInstance<ITelegramHandlerFactory>(new TelegramHandlerFactory
            {
                {UpdateType.Message, () => _container.GetInstance<TelegramMessageHandler>() },
                {UpdateType.CallbackQuery, () => _container.GetInstance<TelegramCallbackQueryHandler>() },
                {UpdateType.MyChatMember, () => _container.GetInstance<TelegramMyChatMemberHandler>() },
                {UpdateType.Unknown, () => _container.GetInstance<TelegramUnknownUpdateHandler>() }
            });

            _container.Register<TelegramMessageHandler>();
            _container.Register<TelegramCallbackQueryHandler>();
            _container.Register<TelegramMyChatMemberHandler>();
            _container.Register<TelegramUnknownUpdateHandler>();
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

        private async Task RegisterGameObjects<T>(string settingsPath) where T: CardObject
        {
            var builder = _container.GetInstance<ICardBuilder<T>>();
            var gameObjects = await builder.GetCardsFromSettings(settingsPath);

            var factory = _container.GetInstance<ICardFactory<T>>();
            var registerTasks = gameObjects.Select(x => factory.RegisterCard(x));

            await Task.WhenAll(registerTasks);
        }

        private void InitializePolling()
        {
            var botClient = _container.GetInstance<ITelegramBotClient>();
            var handler = _container.GetInstance<ITelegramUpdateGateway>();
            var cts = new CancellationTokenSource();

            botClient.StartReceiving(new DefaultUpdateHandler(handler.HandleUpdateAsync, handler.HandleErrorAsync), cts.Token);
        }
    }
}
