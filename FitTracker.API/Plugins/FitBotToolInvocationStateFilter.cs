using Microsoft.SemanticKernel;

namespace FitTrackr.API.Plugins;

public sealed class FitBotToolInvocationStateFilter : IAutoFunctionInvocationFilter
{
    private readonly IFitBotToolInvocationState _state;

    public FitBotToolInvocationStateFilter(IFitBotToolInvocationState state) => _state = state;

    public async Task OnAutoFunctionInvocationAsync(
    AutoFunctionInvocationContext context,
    Func<AutoFunctionInvocationContext, Task> next)
    {
        var toolName =
            $"{context.Function.PluginName}-{context.Function.Name}";

        _state.MarkAttempted(toolName);

        await next(context);

        _state.Record(
            toolName,
            context.Result?.GetValue<object>());
    }
}
