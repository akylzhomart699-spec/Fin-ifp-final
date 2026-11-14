using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace FinTrack.Web.Services;

/// <summary>Входные данные модели: текст описания и метка (Id категории).</summary>
public sealed class TxInput
{
    public string Description { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>Результат модели: предсказанная метка и вероятности по всем классам.</summary>
public sealed class TxPrediction
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = "";

    public float[] Score { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Автокатегоризация операций.
/// 1) ML.NET: многоклассовая классификация текста (FeaturizeText + SdcaMaximumEntropy),
///    обучается на истории операций пользователя и ключевых словах категорий.
/// 2) Если модель не уверена — поиск по ключевым словам.
/// </summary>
public sealed class CategorizationService(
    IDbContextFactory<AppDbContext> factory,
    ILogger<CategorizationService> logger) : ICategorizationService
{
    public const float MinConfidence = 0.35f;

    private readonly MLContext _ml = new(seed: 1);
    private readonly SemaphoreSlim _trainLock = new(1, 1);
    private readonly object _engineLock = new();
    private PredictionEngine<TxInput, TxPrediction>? _engine;

    public bool IsModelReady => _engine is not null;
    public DateTime? TrainedAt { get; private set; }
    public int TrainingSamples { get; private set; }
    public int LabelsCount { get; private set; }

    public async Task TrainAsync(CancellationToken ct = default)
    {
        await _trainLock.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var categories = await db.Categories.AsNoTracking().ToListAsync(ct);

            var samples = new List<TxInput>();
            foreach (var c in categories)
            {
                samples.Add(new TxInput { Description = Normalize(c.Name), Label = c.Id.ToString() });
                samples.AddRange(c.KeywordList.Select(k => new TxInput { Description = Normalize(k), Label = c.Id.ToString() }));
            }

            var history = await db.Transactions.AsNoTracking()
                .OrderByDescending(t => t.Date)
                .Take(5000)
                .Select(t => new { t.Description, t.CategoryId })
                .ToListAsync(ct);
            samples.AddRange(history
                .Where(h => !string.IsNullOrWhiteSpace(h.Description))
                .Select(h => new TxInput { Description = Normalize(h.Description), Label = h.CategoryId.ToString() }));

            var labels = samples.Select(s => s.Label).Distinct().Count();
            if (labels < 2)
            {
                logger.LogInformation("Недостаточно категорий для обучения модели");
                return;
            }

            var data = _ml.Data.LoadFromEnumerable(samples);
            var pipeline = _ml.Transforms.Conversion.MapValueToKey("LabelKey", nameof(TxInput.Label))
                .Append(_ml.Transforms.Text.FeaturizeText("Features", nameof(TxInput.Description)))
                .Append(_ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey", "Features"))
                .Append(_ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            var model = await Task.Run(() => pipeline.Fit(data), ct);
            var engine = _ml.Model.CreatePredictionEngine<TxInput, TxPrediction>(model);

            lock (_engineLock)
            {
                _engine?.Dispose();
                _engine = engine;
            }

            TrainedAt = DateTime.Now;
            TrainingSamples = samples.Count;
            LabelsCount = labels;
            logger.LogInformation("Модель обучена: {Samples} примеров, {Labels} категорий", samples.Count, labels);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Без модели приложение продолжит работать на ключевых словах
            logger.LogError(ex, "Не удалось обучить модель категоризации");
        }
        finally
        {
            _trainLock.Release();
        }
    }

    public async Task<CategorySuggestion?> SuggestAsync(string description, CategoryType? type, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Categories.AsNoTracking();
        if (type is CategoryType t) query = query.Where(c => c.Type == t);
        var candidates = await query.ToListAsync(ct);

        return Suggest(description, candidates);
    }

    public CategorySuggestion? Suggest(string description, IReadOnlyList<Category> candidates)
    {
        if (string.IsNullOrWhiteSpace(description) || candidates.Count == 0) return null;
        var text = Normalize(description);

        TxPrediction? prediction = null;
        lock (_engineLock)
        {
            if (_engine is not null)
                prediction = _engine.Predict(new TxInput { Description = text });
        }

        if (prediction is not null && Guid.TryParse(prediction.PredictedLabel, out var id))
        {
            var confidence = prediction.Score.Length > 0 ? prediction.Score.Max() : 0f;
            var category = candidates.FirstOrDefault(c => c.Id == id);
            if (category is not null && confidence >= MinConfidence)
                return new CategorySuggestion(category.Id, confidence, SuggestionMethods.Ml);
        }

        var byKeyword = KeywordMatch(text, candidates);
        return byKeyword is null ? null : new CategorySuggestion(byKeyword.Id, 1f, SuggestionMethods.Keywords);
    }

    /// <summary>Ищет категорию, ключевое слово которой встречается в тексте. Самое длинное совпадение побеждает.</summary>
    public static Category? KeywordMatch(string text, IEnumerable<Category> candidates)
    {
        var normalized = Normalize(text);
        return candidates
            .SelectMany(c => c.KeywordList.Select(k => (Category: c, Keyword: Normalize(k))))
            .Where(x => x.Keyword.Length > 0 && normalized.Contains(x.Keyword))
            .OrderByDescending(x => x.Keyword.Length)
            .Select(x => x.Category)
            .FirstOrDefault();
    }

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();
}
