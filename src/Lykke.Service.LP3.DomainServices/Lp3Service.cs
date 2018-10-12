using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Common;
using Common.Log;
using Lykke.Common.Log;
using Lykke.Service.LP3.Domain;
using Lykke.Service.LP3.Domain.Exchanges;
using Lykke.Service.LP3.Domain.Orders;
using Lykke.Service.LP3.Domain.Services;
using Lykke.Service.LP3.Domain.Settings;

namespace Lykke.Service.LP3.DomainServices
{
    public class Lp3Service : ILp3Service, IStartable
    {
        private readonly ISettingsService _settingsService;
        private readonly ILevelsService _levelsService;
        private readonly IAdditionalVolumeService _additionalVolumeService;
        private readonly IInitialPriceService _initialPriceService;
        private readonly ILykkeExchange _lykkeExchange;
        private readonly IOrdersConverter _ordersConverter;
        private readonly ILog _log;
        
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
        private bool _started;

        private readonly ConcurrentDictionary<string, List<LimitOrder>> _ordersByAssetPairs = 
            new ConcurrentDictionary<string, List<LimitOrder>>();
        
        private decimal _inventory = 0;
        private decimal _oppositeInventory = 0;
        private string _baseAssetPairId;

        public Lp3Service(ILogFactory logFactory,
            ISettingsService settingsService,
            ILevelsService levelsService,
            IAdditionalVolumeService additionalVolumeService,
            IInitialPriceService initialPriceService,
            ILykkeExchange lykkeExchange,
            IOrdersConverter ordersConverter)
        {
            _settingsService = settingsService;
            _levelsService = levelsService;
            _additionalVolumeService = additionalVolumeService;
            _initialPriceService = initialPriceService;
            _lykkeExchange = lykkeExchange;
            _ordersConverter = ordersConverter;
            _log = logFactory.CreateLog(this);
        }
        

        public void Start()
        {
            SynchronizeAsync(async () => await StartAsync()).GetAwaiter().GetResult();;
        }

        private async Task StartAsync()
        {
            var initialPrice = await _initialPriceService.GetAsync();
            if (initialPrice == null)
            {
                _log.Info("No initial price to start algorithm, waiting for adding one via API");
                return;
            }
        
            _baseAssetPairId = (await _settingsService.GetBaseAssetPairSettingsAsync())?.AssetPairId;
            if (_baseAssetPairId == null)
            {
                _log.Info("No baseAssetPairId to start algorithm, waiting for adding it via API");
                return;
            }

            _levelsService.UpdateReference(initialPrice.Price);

            _started = true;
            
            await ApplyOrdersAsync();
        }

        public async Task HandleTradesAsync(IReadOnlyList<Trade> trades)
        {
            await SynchronizeAsync(async () =>
            {
                var newInitialPrice = trades.Last().Price;
                await _initialPriceService.AddOrUpdateAsync(newInitialPrice);
                _log.Info("InitialPrice is updated", 
                    context: $"Trades: [{string.Join(", ", trades.Select(x => x.ToJson()))}], new InitialPrice: {newInitialPrice}");
                
                foreach (var trade in trades)
                {
                    HandleTrade(trade); // TODO: pass all trades at once?
                }

                await ApplyOrdersAsync();
            });
        }

        public async Task HandleTimerAsync()
        {
            await SynchronizeAsync(async () =>
            {
                if (!_started)
                {
                    await StartAsync();
                }
                else
                {
                    await ApplyOrdersAsync();
                }
            });
        }

        public IReadOnlyList<LimitOrder> GetBaseOrders()
        {
            return GetOrders(_baseAssetPairId);
        }

        public IReadOnlyList<LimitOrder> GetOrders(string assetPairId)
        {
            return _ordersByAssetPairs.TryGetValue(assetPairId, out var orders) ? orders : new List<LimitOrder>();
        }
        
        private void HandleTrade(Trade trade)
        {
            _log.Info("Trade is received", context: $"Trade: {trade.ToJson()}");

            if (!_levelsService.GetLevels().Any())
            {
                _log.Error("Trade is received but there aren't any levels");
                return;
            }

            var volume = trade.Volume;

            if (trade.Type == TradeType.Sell)
            {
                volume *= -1;
            }
            
            while (volume != 0)
            {
                volume = HandleVolume(volume);
            }
            
            _levelsService.SaveStatesAsync().GetAwaiter().GetResult();
        }
        
        private decimal HandleVolume(decimal volume)
        {
            if (volume < 0)
            {
                var level = _levelsService.GetLevels().OrderBy(e => e.Sell).First();

                if (volume <= level.VolumeSell)
                {
                    volume -= level.VolumeSell;

                    _inventory += level.VolumeSell;
                    _oppositeInventory -= level.VolumeSell * level.Sell; // TODO: get rounded price from trade ? 

                    level.Inventory += level.VolumeSell;
                    level.OppositeInventory -= level.VolumeSell * level.Sell;

                    level.UpdateReference(level.Sell);
                    level.VolumeSell = -level.OriginalVolume;
                }
                else
                {
                    level.VolumeSell -= volume;

                    _inventory += volume;
                    _oppositeInventory -= volume * level.Sell;
                    level.Inventory += volume;
                    level.OppositeInventory -= volume * level.Sell;

                    volume = 0;
                }
                
                _log.Info($"Level {level.Name} is executed", context: $"Level state: {level.ToJson()}");

                return volume;
            }

            if (volume > 0)
            {
                var level = _levelsService.GetLevels().OrderByDescending(e => e.Buy).First();

                if (volume >= level.VolumeBuy)
                {
                    volume -= level.VolumeBuy;

                    _inventory += level.VolumeBuy;
                    _oppositeInventory -= level.VolumeBuy * level.Buy;

                    level.Inventory += level.VolumeBuy;
                    level.OppositeInventory -= level.VolumeBuy * level.Buy;

                    level.UpdateReference(level.Buy);
                    level.VolumeBuy = level.OriginalVolume;
                }
                else
                {
                    level.VolumeBuy -= volume;
                    _inventory += volume;
                    _oppositeInventory -= volume * level.Buy;
                    level.Inventory += volume;
                    level.OppositeInventory -= volume * level.Buy;
                    volume = 0;
                }
                
                _log.Info($"Level {level.Name} is executed", context: $"Level state: {level.ToJson()}");
                
                return volume;
            }

            return 0;
        }

        private async Task SynchronizeAsync(Func<Task> asyncAction)
        {
            bool lockTaken = false;
            try
            {
                lockTaken = await _semaphore.WaitAsync(Consts.LockTimeOut);
                if (!lockTaken)
                {
                    _log.Warning($"Can't take lock for {Consts.LockTimeOut}");
                    return;
                }

                await asyncAction();
            }
            finally
            {
                if (lockTaken)
                {
                    _semaphore.Release();
                }
            }
        }

        private async Task ApplyOrdersAsync()
        {
            try
            {
                if (await BaseAssetPairWasDeleted())
                {
                    _log.Info("Base pair deletion is detected, remove all orders");
                    _ordersByAssetPairs[_baseAssetPairId] = new List<LimitOrder>();
                    _started = false;
                }
                else
                {
                    var levelOrders = _levelsService.GetOrders().ToList();
                    var additionalOrders = await _additionalVolumeService.GetOrdersAsync(levelOrders);

                    _ordersByAssetPairs[_baseAssetPairId] = levelOrders.Union(additionalOrders).ToList();    
                }
                
                var dependentPairsSettings = (await _settingsService.GetDependentAssetPairsSettingsAsync()).ToList();
                foreach (var pairSettings in dependentPairsSettings)
                {
                    _ordersByAssetPairs[pairSettings.AssetPairId] = _ordersByAssetPairs[_baseAssetPairId]
                        .Select(x => (LimitOrder)_ordersConverter.ConvertAsync(x, pairSettings).GetAwaiter().GetResult()).ToList();
                }

                ClearDeletedDependentPairs(dependentPairsSettings);

                foreach (var ordersByAssetPair in _ordersByAssetPairs)
                {
                    try
                    {
                        await _lykkeExchange.ApplyAsync(ordersByAssetPair.Key, ordersByAssetPair.Value);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, $"Error on placing orders for {ordersByAssetPair.Key}");
                    }
                }
            }
            catch (Exception e)
            {
                _log.Error(e);
            }
        }

        private async Task<bool> BaseAssetPairWasDeleted()
        {
            return await _settingsService.GetBaseAssetPairSettingsAsync() == null;
        }

        private void ClearDeletedDependentPairs(IEnumerable<AssetPairSettings> dependentPairsSettings)
        {
            var allEnabledDependentAssetPairs = dependentPairsSettings.Select(x => x.AssetPairId);
            var deletedDependentAssetPairs =
                _ordersByAssetPairs.Keys.Where(x => !allEnabledDependentAssetPairs.Contains(x));
            
            foreach (var dependentAssetPairId in deletedDependentAssetPairs)
            {
                _ordersByAssetPairs[dependentAssetPairId] = new List<LimitOrder>();
            }
        }
    }
}
