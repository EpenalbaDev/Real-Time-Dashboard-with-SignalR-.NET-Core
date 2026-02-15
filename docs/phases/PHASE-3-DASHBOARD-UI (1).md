# Phase 3: Dashboard UI

## Objective
Build the visual dashboard with Chart.js, real-time data binding via SignalR, and responsive layout.

## Tasks

### 3.1 Chart.js Interop Layer
Create `wwwroot/js/chartInterop.js`:

```javascript
// Functions exposed to Blazor:
// - createChart(canvasId, type, config) → creates Chart.js instance
// - updateChartData(canvasId, labels, datasets) → updates existing chart
// - destroyChart(canvasId) → cleanup
// - resizeChart(canvasId) → responsive handling

// Chart types needed:
// 1. Line chart: Transaction volume over time (streaming)
// 2. Doughnut chart: Transaction status distribution
// 3. Bar chart: Transaction by source (ATM, POS, Online, etc.)
// 4. Line chart: TPS (transactions per second) gauge
```

Create `ChartJsInterop.cs` service:
```csharp
public sealed class ChartJsInterop : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    
    public async Task CreateLineChart(string canvasId, LineChartConfig config);
    public async Task UpdateData(string canvasId, ChartUpdateDto data);
    public async ValueTask DisposeAsync();
}
```

### 3.2 Dashboard Page Layout
```
┌─────────────────────────────────────────────────────────┐
│  Real-Time Transaction Dashboard          🟢 Connected  │
├──────────┬──────────┬──────────┬────────────────────────│
│ Total TX │ Volume $ │ TPS      │ Flagged ⚠️              │
│ 1,247    │ $2.3M    │ 47/s     │ 12                     │
├──────────┴──────────┴──────────┴────────────────────────│
│                                                         │
│  [Transaction Volume - Streaming Line Chart]            │
│  ════════════════════════════════════════════           │
│                                                         │
├─────────────────────────┬───────────────────────────────│
│ [Status Distribution]   │ [Transactions by Source]      │
│  Doughnut Chart         │  Bar Chart                   │
│                         │                               │
├─────────────────────────┴───────────────────────────────│
│  Recent Transactions (live feed)                        │
│  ┌─────┬────────┬────────┬────────┬─────────┬────────┐ │
│  │ ID  │ Amount │ Type   │ Source │ Status  │ Time   │ │
│  ├─────┼────────┼────────┼────────┼─────────┼────────┤ │
│  │ ... │ ...    │ ...    │ ...    │ ...     │ ...    │ │
│  └─────┴────────┴────────┴────────┴─────────┴────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 3.3 Blazor Components

**MetricsCard.razor:**
- Animated number transitions (count up effect via JS interop or CSS)
- Color coding: green (normal), yellow (warning), red (critical)
- Shows trend arrow (↑↓) comparing to previous period

**TransactionChart.razor:**
- Wraps Chart.js line chart
- Streaming mode: slides window (last 5 minutes visible)
- Max 300 data points in memory (older ones drop off)
- Receives updates from parent Dashboard via Parameter

**StatusChart.razor:**
- Doughnut chart with live status distribution
- Smooth transitions on data change

**TransactionTable.razor:**
- Virtual scrolling (Blazor `Virtualize` component)
- Last 100 transactions visible
- Status badges with color coding
- New transactions highlight briefly (CSS animation)

**StatusIndicator.razor:**
- Shows SignalR connection state (Connected/Reconnecting/Disconnected)
- Auto-reconnect logic

### 3.4 SignalR Client Integration in Blazor
```csharp
@code {
    private HubConnection? _hubConnection;
    
    protected override async Task OnInitializedAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/dashboard-hub"))
            .WithAutomaticReconnect(new[] { 
                TimeSpan.Zero, 
                TimeSpan.FromSeconds(2), 
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10) 
            })
            .Build();
            
        _hubConnection.On<IReadOnlyList<TransactionDto>>("ReceiveTransactionBatch", OnBatchReceived);
        _hubConnection.On<DashboardMetricsDto>("ReceiveMetricsUpdate", OnMetricsReceived);
        
        await _hubConnection.StartAsync();
    }
}
```

### 3.5 CSS/Styling
- Dark theme (professional dashboard look)
- CSS Grid layout for responsive cards
- No CSS framework dependency — custom CSS
- Minimal animations: number transitions, new row highlight
- Mobile: stack cards vertically

## Definition of Done
- [ ] Dashboard loads and shows historical data immediately
- [ ] Charts update in real-time with smooth animations
- [ ] Connection status indicator works (connect/reconnect/disconnect)
- [ ] Table virtualizes correctly with 1000+ rows
- [ ] Responsive on mobile (cards stack, chart resizes)
- [ ] No JS console errors
- [ ] bUnit tests for component rendering
- [ ] Git: commit on `phase/3-dashboard-ui`, tag `v0.3.0`

## Estimated Time: 4-5 hours with Claude Code
