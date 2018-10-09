using System.Collections.Generic;
using System.Threading.Tasks;
using Lykke.Service.LP3.Domain.Orders;

namespace Lykke.Service.LP3.Domain.Services
{
    public interface ILp3Service
    {
        Task HandleTradesAsync(IReadOnlyList<Trade> trades);
        
        Task HandleTimerAsync();

        IReadOnlyList<LimitOrder> GetOrders();
    }
}