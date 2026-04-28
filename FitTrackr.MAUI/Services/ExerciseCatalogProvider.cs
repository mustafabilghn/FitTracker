using FitTrackr.MAUI.Models.DTO;
using System.Text.Json;

namespace FitTrackr.MAUI.Services
{
    public class ExerciseCatalogProvider
    {
        private const string CatalogFileName = "exercise_catalog.json";

        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private IReadOnlyList<ExerciseCatalogItemDto>? _cachedCatalog;

        public ExerciseCatalogProvider(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public async Task<IReadOnlyList<ExerciseCatalogItemDto>> GetCatalogAsync()
        {
            if (_cachedCatalog != null)
            {
                return _cachedCatalog;
            }

            await _loadLock.WaitAsync();
            try
            {
                if (_cachedCatalog != null)
                {
                    return _cachedCatalog;
                }

                await using var stream = await FileSystem.OpenAppPackageFileAsync(CatalogFileName);
                var catalogItems = await JsonSerializer.DeserializeAsync<List<ExerciseCatalogItemDto>>(stream, _jsonOptions)
                    ?? [];

                _cachedCatalog = NormalizeCatalog(catalogItems);
                return _cachedCatalog;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private static IReadOnlyList<ExerciseCatalogItemDto> NormalizeCatalog(IEnumerable<ExerciseCatalogItemDto> items)
        {
            var seenIds = new HashSet<Guid>();
            var normalizedItems = new List<ExerciseCatalogItemDto>();

            foreach (var item in items)
            {
                if (item.Id == Guid.Empty || !seenIds.Add(item.Id))
                {
                    continue;
                }

                normalizedItems.Add(new ExerciseCatalogItemDto
                {
                    Id = item.Id,
                    Name = item.Name?.Trim() ?? string.Empty,
                    BodyPart = item.BodyPart?.Trim() ?? string.Empty,
                    Equipment = item.Equipment?.Trim() ?? string.Empty,
                    Level = item.Level?.Trim() ?? string.Empty,
                    Description = item.Description,
                    ImageName = string.IsNullOrWhiteSpace(item.ImageName) ? string.Empty : item.ImageName.Trim()
                });
            }

            return normalizedItems;
        }
    }
}
