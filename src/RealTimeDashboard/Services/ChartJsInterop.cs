using Microsoft.JSInterop;

namespace RealTimeDashboard.Services;

public sealed class ChartJsInterop : IAsyncDisposable
{
    private readonly IJSRuntime _js;

    public ChartJsInterop(IJSRuntime js)
    {
        _js = js;
    }

    public async Task CreateLineChartAsync(string canvasId, string[] labels, decimal[] data,
        string label = "Value", string borderColor = "#2f81f7", string backgroundColor = "rgba(47,129,247,0.1)")
    {
        await _js.InvokeVoidAsync("chartInterop.createLineChart",
            canvasId, labels, data.Select(d => (double)d).ToArray(), label, borderColor, backgroundColor);
    }

    public async Task CreateDoughnutChartAsync(string canvasId, string[] labels, int[] data, string[]? colors = null)
    {
        await _js.InvokeVoidAsync("chartInterop.createDoughnutChart", canvasId, labels, data, colors);
    }

    public async Task CreateBarChartAsync(string canvasId, string[] labels, int[] data,
        string label = "Count", string[]? colors = null)
    {
        await _js.InvokeVoidAsync("chartInterop.createBarChart", canvasId, labels, data, label, colors);
    }

    public async Task UpdateLineDataAsync(string canvasId, string[] labels, decimal[] data)
    {
        await _js.InvokeVoidAsync("chartInterop.updateChartData",
            canvasId, labels, data.Select(d => (double)d).ToArray());
    }

    public async Task UpdateDoughnutDataAsync(string canvasId, string[] labels, int[] data)
    {
        await _js.InvokeVoidAsync("chartInterop.updateDoughnutData", canvasId, labels, data);
    }

    public async Task UpdateBarDataAsync(string canvasId, string[] labels, int[] data)
    {
        await _js.InvokeVoidAsync("chartInterop.updateBarData", canvasId, labels, data);
    }

    public async Task DestroyChartAsync(string canvasId)
    {
        await _js.InvokeVoidAsync("chartInterop.destroyChart", canvasId);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
