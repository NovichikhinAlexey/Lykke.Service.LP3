using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Common.Log;
using Lykke.Service.LP3.Domain.Repositories;
using Lykke.Service.LP3.Domain.Services;
using Lykke.Service.LP3.Domain.Settings;

namespace Lykke.Service.LP3.DomainServices
{
    public class SettingsService : ISettingsService
    {
        private readonly string _walletId;
        private readonly IBaseAssetPairSettingsRepository _baseAssetPairSettingsRepository;
        private readonly IAdditionalVolumeSettingsRepository _additionalVolumeSettingsRepository;
        private readonly List<string> _availableExternalExchanges;
        private readonly ILog _log;

        public SettingsService(string walletId,
            ILogFactory logFactory,
            IBaseAssetPairSettingsRepository baseAssetPairSettingsRepository,
            IAdditionalVolumeSettingsRepository additionalVolumeSettingsRepository,
            IEnumerable<string> availableExternalExchanges)
        {
            _walletId = walletId;
            _baseAssetPairSettingsRepository = baseAssetPairSettingsRepository;
            _additionalVolumeSettingsRepository = additionalVolumeSettingsRepository;
            _availableExternalExchanges = availableExternalExchanges?.ToList() ?? new List<string>();
            _log = logFactory.CreateLog(this);
        }

        public string GetWalletId()
        {
            return _walletId;
        }

        public IReadOnlyList<string> GetAvailableExternalExchanges()
        {
            return _availableExternalExchanges;
        }

        public Task<BaseAssetPairSettings> GetBaseAssetPairSettings()
        {
            return _baseAssetPairSettingsRepository.GetAsync();
        }

        public async Task SaveBaseAssetPairSettings(BaseAssetPairSettings settings)
        {
            await _baseAssetPairSettingsRepository.AddOrUpdateAsync(settings);
            _log.Info("BaseAsset settings were updated", context: $"new settings: {settings.ToJson()}");
        }
        
        public async Task UpdateAdditionalVolumeSettingsAsync(AdditionalVolumeSettings settings)
        {
            await _additionalVolumeSettingsRepository.AddOrUpdateAsync(settings);
            _log.Info("Additional volume settings were updated", context: $"new settings: {settings.ToJson()}");
        }

        public Task<AdditionalVolumeSettings> GetAdditionalVolumeSettingsAsync()
        {
            return _additionalVolumeSettingsRepository.GetAsync();
        }
    }
}
