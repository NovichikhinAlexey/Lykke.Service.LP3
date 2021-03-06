using System;
using System.Linq;
using Common.Log;
using Lykke.Common.Log;
using Lykke.Service.Assets.Client.Models.v3;
using Lykke.Service.Assets.Client.ReadModels;
using Lykke.Service.LP3.Domain.Assets;
using Lykke.Service.LP3.Domain.Services;

namespace Lykke.Service.LP3.DomainServices.Exchanges
{
    public class LykkeAssetsService : IAssetsService
    {
        private readonly IAssetPairsReadModelRepository _assetPairsService;
        private readonly IAssetsReadModelRepository _assetsService;
        private readonly IAssetLinkService _assetLinkService;
        private readonly ILog _log;

        public LykkeAssetsService(ILogFactory logFactory,
            IAssetPairsReadModelRepository assetPairsService,
            IAssetsReadModelRepository assetsService,
            IAssetLinkService assetLinkService)
        {
            _assetPairsService = assetPairsService;
            _assetsService = assetsService;
            _assetLinkService = assetLinkService;
            _log = logFactory.CreateLog(this);
        }
        
        public AssetPairInfo GetAssetPairInfo(string assetPairId)
        {
            AssetPair assetPair = _assetPairsService.TryGetIfEnabled(assetPairId);
            if (assetPair == null)
            {
                throw new Exception($"AssetService have returned null for asset pair {assetPairId}");
            }

            Asset baseAsset = _assetsService.TryGet(assetPair.BaseAssetId);
            if (baseAsset == null)
            {
                throw new Exception(
                    $"AssetService have returned null for base asset {assetPair.BaseAssetId} from pair {assetPairId}");
            }

            var assetLinks = _assetLinkService.GetAllAsync().GetAwaiter().GetResult();
            
            return new AssetPairInfo
            {
                AssetPairId = assetPairId,
                DisplayName = assetPair.Name,
                MinVolume = assetPair.MinVolume,
                PriceAccuracy = assetPair.Accuracy,
                VolumeAccuracy = baseAsset.Accuracy,
                BaseAssetId = assetLinks.SingleOrDefault(x => x.ExternalAssetId == assetPair.BaseAssetId)?.AssetId ?? assetPair.BaseAssetId,
                QuoteAssetId = assetLinks.SingleOrDefault(x => x.ExternalAssetId == assetPair.QuotingAssetId)?.AssetId ?? assetPair.QuotingAssetId
            };
        }

        public AssetInfo GetAssetInfo(string assetId)
        {
            throw new NotImplementedException();
        }
    }
}
