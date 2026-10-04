using System.Diagnostics;
using System.Text.Json;
using FitTrackr.API.Plugins;
using Microsoft.SemanticKernel;

namespace FitTracker.Evaluation.Instrumentation;

/// <summary>Sunucu tarafında GERÇEKTEN çalıştırılan bir tool (KernelFunction) ve sonucunun güvenli özeti.</summary>
public sealed class ToolInvocationRecord
{
    public required string Name { get; init; }
    public int RequestSequenceIndex { get; init; }
    public string ArgumentsJson { get; set; } = "{}";
    public bool Executed { get; set; }
    public string? Exception { get; set; }
    public long DurationMs { get; set; }

    // Sonuç özeti (tam payload değil)
    public string ResultKind { get; set; } = "none";
    public int? ItemCount { get; set; }
    public bool? Success { get; set; }
    public string? Reason { get; set; }
    public string? Detail { get; set; }
    public string? Exercise { get; set; }
    public double? LimitKg { get; set; }
    public string? ToolError { get; set; }

    /// <summary>Kendi tool'umuz hata nesnesi döndürmediyse ve exception yoksa başarılı sayılır.</summary>
    public bool Succeeded => Executed && Exception is null && ToolError is null && Reason != SaveWorkoutPlanReasons.SaveFailed;
}

/// <summary>
/// Otomatik function-calling sırasında çalışan tool'ları kaydeden filtre. Production filtrelerinden sonra DI'a
/// eklendiği için en içte çalışır; <c>next</c>'i aynen çağırır, sonucu ve <c>Terminate</c>'i DEĞİŞTİRMEZ.
/// Doğrudan (LLM'siz) çağrılar — ör. fixture kurulumu — bu filtreyi tetiklemez.
/// </summary>
public sealed class ToolInvocationRecorder : IAutoFunctionInvocationFilter
{
    private readonly List<ToolInvocationRecord> _records = new();
    private readonly object _gate = new();

    public IReadOnlyList<ToolInvocationRecord> Invocations
    {
        get { lock (_gate) return _records.ToList(); }
    }

    public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        var record = new ToolInvocationRecord
        {
            Name = $"{context.Function.PluginName}-{context.Function.Name}",
            RequestSequenceIndex = context.RequestSequenceIndex,
            ArgumentsJson = SerializeArguments(context.Arguments)
        };
        lock (_gate) _records.Add(record);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
            record.Executed = true;
            Summarize(context.Result?.GetValue<object>(), record);
        }
        catch (Exception ex)
        {
            record.Executed = true;
            record.Exception = Redactor.Clean($"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            record.DurationMs = stopwatch.ElapsedMilliseconds;
        }
    }

    private static string SerializeArguments(KernelArguments? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return "{}";
        try
        {
            return Redactor.Clean(JsonSerializer.Serialize(arguments.ToDictionary(kv => kv.Key, kv => kv.Value)), 1500);
        }
        catch (Exception ex)
        {
            return $"<unserializable: {ex.GetType().Name}>";
        }
    }

    private static void Summarize(object? value, ToolInvocationRecord record)
    {
        switch (value)
        {
            case null:
                record.ResultKind = "null";
                return;
            case SaveWorkoutPlanResult save:
                record.ResultKind = "SaveWorkoutPlanResult";
                record.Success = save.Success;
                record.Reason = save.Reason;
                record.Detail = save.Detail;
                record.Exercise = save.Exercise;
                record.LimitKg = save.LimitKg;
                return;
        }

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                record.ResultKind = "list";
                record.ItemCount = root.GetArrayLength();
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            {
                record.ResultKind = "tool_error";
                record.ToolError = error.GetString();
            }
            else
            {
                record.ResultKind = root.ValueKind.ToString().ToLowerInvariant();
            }
        }
        catch
        {
            record.ResultKind = value.GetType().Name;
        }
    }
}
