using AutoMapper;
using Lykke.Service.LP3.Client.Models;
using Lykke.Service.LP3.Client.Models.Levels;
using Lykke.Service.LP3.Client.Models.Orders;
using Lykke.Service.LP3.Client.Models.Settings;
using Lykke.Service.LP3.Client.Models.Trades;
using Lykke.Service.LP3.Domain;
using Lykke.Service.LP3.Domain.Orders;

namespace Lykke.Service.LP3
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            CreateMap<Level, LevelSettingsModel>(MemberList.Destination)
                .ForMember(x => x.Volume, m => m.MapFrom(x => x.OriginalVolume));
            
            CreateMap<LevelSettingsModel, Level>(MemberList.None)
                .ConstructUsing(x => new Level(x.Name, x.Delta, x.Volume));
            
            CreateMap<Level, LevelModel>(MemberList.Source);
            
            CreateMap<InitialPrice, InitialPriceModel>(MemberList.Source);
            CreateMap<InitialPriceModel, InitialPrice>(MemberList.Destination);
            
            CreateMap<LimitOrder, LimitOrderModel>(MemberList.Source);
            
            CreateMap<Trade, TradeModel>(MemberList.Source);
        }
    }
}