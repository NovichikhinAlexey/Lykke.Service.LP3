using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lykke.MatchingEngine.ExchangeModels;
using Lykke.Service.LP3.Domain.Orders;

namespace Lykke.Service.LP3.Domain.Services
{
    public interface ILykkeTradeService
    {
        Task HandleAsync(LimitOrders limitOrders);
        Task<IReadOnlyList<Trade>> GetAsync(DateTime startDate, DateTime endDate);
    }
}