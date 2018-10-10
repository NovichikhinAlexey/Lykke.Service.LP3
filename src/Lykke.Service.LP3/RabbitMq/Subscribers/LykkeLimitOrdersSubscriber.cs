using System;
using System.Threading.Tasks;
using Autofac;
using Common;
using Lykke.Common.Log;
using Lykke.MatchingEngine.ExchangeModels;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.Service.LP3.Domain.Services;
using Lykke.Service.LP3.Settings;

namespace Lykke.Service.LP3.RabbitMq.Subscribers
{
    public class LykkeLimitOrdersSubscriber : IStartable, IStopable
    {
        private readonly ILogFactory _logFactory;
        private readonly ILykkeTradeService _lykkeTradeService;
        private readonly RabbitMqSettings _settings;
        private RabbitMqSubscriber<LimitOrders> _subscriber;

        public LykkeLimitOrdersSubscriber(
            ILogFactory logFactory,
            ILykkeTradeService lykkeTradeService,
            RabbitMqSettings settings
            )
        {
            _logFactory = logFactory;
            _lykkeTradeService = lykkeTradeService;
            _settings = settings;
        }

        public void Start()
        {
            // NOTE: Read https://github.com/LykkeCity/Lykke.RabbitMqDotNetBroker/blob/master/README.md to learn
            // about RabbitMq subscriber configuration

            var settings = RabbitMqSubscriptionSettings
                .ForSubscriber(_settings.ConnectionString, _settings.ExchangeName, "lp3")
                .MakeDurable();

            _subscriber = new RabbitMqSubscriber<LimitOrders>(
                    _logFactory,
                    settings,
                    new ResilientErrorHandlingStrategy(
                        _logFactory,
                        settings,
                        TimeSpan.FromSeconds(10),
                        next: new DeadQueueErrorHandlingStrategy(_logFactory, settings)))
                .SetMessageDeserializer(new JsonMessageDeserializer<LimitOrders>())
                .Subscribe(ProcessMessageAsync)
                .CreateDefaultBinding()
                .Start();
        }

        private Task ProcessMessageAsync(LimitOrders arg)
        {
            return _lykkeTradeService.HandleAsync(arg);
        }

        public void Dispose()
        {
            _subscriber?.Dispose();
        }

        public void Stop()
        {
            _subscriber?.Stop();
        }
    }
}
