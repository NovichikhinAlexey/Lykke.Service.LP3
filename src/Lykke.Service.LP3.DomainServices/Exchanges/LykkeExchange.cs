using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Common.Log;
using Lykke.MatchingEngine.Connector.Abstractions.Services;
using Lykke.MatchingEngine.Connector.Models.Api;
using Lykke.Service.Assets.Client.Models.v3;
using Lykke.Service.LP3.Domain.Exchanges;
using Lykke.Service.LP3.Domain.Orders;
using Lykke.Service.LP3.Domain.Services;
using Lykke.Service.LP3.DomainServices.Extensions;

namespace Lykke.Service.LP3.DomainServices.Exchanges
{
    public class LykkeExchange : ILykkeExchange
    {
        private readonly IMatchingEngineClient _matchingEngineClient;
        private readonly ISettingsService _settingsService;
        private readonly ILog _log;

        private List<LimitOrder> _orders;
        
        public LykkeExchange(ILogFactory logFactory,
            IMatchingEngineClient matchingEngineClient,
            ISettingsService settingsService)
        {
            _matchingEngineClient = matchingEngineClient;
            _settingsService = settingsService;

            _log = logFactory.CreateLog(this);
        }
        
        public async Task ApplyAsync(AssetPair assetPair, IReadOnlyList<LimitOrder> limitOrders)
        {
            if (_orders != null && limitOrders.SequenceEqual(_orders, new LimitOrdersComparer()))
            {
                _log.Info("New orders are the same as previously placed, don't replace");
                return;
            }
            
            string walletId = _settingsService.GetWalletId();

            if (string.IsNullOrEmpty(walletId))
                throw new Exception("WalletId is not set");

            var map = new Dictionary<string, Guid>();
            
            var multiOrderItems = new List<MultiOrderItemModel>();

            foreach (LimitOrder limitOrder in limitOrders)
            {
                var multiOrderItem = new MultiOrderItemModel
                {
                    Id = Guid.NewGuid().ToString("D"),
                    OrderAction = limitOrder.TradeType.ToOrderAction(),
                    Price = (double) Math.Round(limitOrder.Price, assetPair.Accuracy),
                    Volume = (double) Math.Round(Math.Abs(limitOrder.Volume), assetPair.InvertedAccuracy)
                };

                multiOrderItems.Add(multiOrderItem);

                map[multiOrderItem.Id] = limitOrder.Id;
            }

            var multiLimitOrder = new MultiLimitOrderModel
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = walletId,
                AssetPairId = assetPair.Id,
                CancelPreviousOrders = true,
                Orders = multiOrderItems,
                CancelMode = CancelMode.BothSides
            };

            _log.Info("ME place multi limit order request", new {request = $"data: {multiLimitOrder.ToJson()}"});

            MultiLimitOrderResponse response;

            try
            {
                response = await _matchingEngineClient.PlaceMultiLimitOrderAsync(multiLimitOrder);
            }
            catch (Exception exception)
            {
                _log.Error(exception, "An error occurred during creating limit orders",
                    new {request = $"data: {multiLimitOrder.ToJson()}"});

                throw;
            }

            if (response == null)
            {
                throw new Exception("ME response is null");
            }

            _log.Info("ME place multi limit order response", new {response = $"data: {response.ToJson()}"});

            foreach (var orderStatus in response.Statuses)
            {
                if (map.TryGetValue(orderStatus.Id, out var limitOrderId))
                {
                    var limitOrder = limitOrders.Single(e => e.Id == limitOrderId);

                    limitOrder.Error = orderStatus.Status.ToOrderError();
                    limitOrder.ErrorMessage = limitOrder.Error != LimitOrderError.Unknown 
                        ? orderStatus.StatusReason
                        : !string.IsNullOrEmpty(orderStatus.StatusReason) ? orderStatus.StatusReason : "Unknown error";
                }
            }

            _orders = limitOrders.ToList();
        }
    }
}
