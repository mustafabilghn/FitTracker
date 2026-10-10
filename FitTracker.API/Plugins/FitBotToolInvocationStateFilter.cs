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
        await next(context);
        _state.Record(
            $"{context.Function.PluginName}-{context.Function.Name}",
            context.Result?.GetValue<object>());
    }
}
