using Microsoft.JSInterop;

namespace RealTimeDashboard.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly IJSRuntime _js;
    private string _currentLang = "en";

    public string CurrentLanguage => _currentLang;
    public event Action? OnLanguageChanged;

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en"] = new()
        {
            ["dashboard.title"] = "Real-Time Transaction Dashboard",
            ["dashboard.transactions1m"] = "Transactions (1m)",
            ["dashboard.lastHour"] = "last hour",
            ["dashboard.volume1m"] = "Volume (1m)",
            ["dashboard.tps"] = "TPS",
            ["dashboard.successRate"] = "Success rate",
            ["dashboard.flagged"] = "Flagged",
            ["dashboard.connections"] = "Connections",

            ["transactions.title"] = "Transactions",
            ["transactions.status"] = "Status",
            ["transactions.type"] = "Type",
            ["transactions.all"] = "All",
            ["transactions.loading"] = "Loading transactions...",
            ["transactions.notFound"] = "No transactions found.",
            ["transactions.id"] = "Transaction ID",
            ["transactions.amount"] = "Amount",
            ["transactions.source"] = "Source",
            ["transactions.created"] = "Created",
            ["transactions.previous"] = "Previous",
            ["transactions.next"] = "Next",
            ["transactions.page"] = "Page",
            ["transactions.of"] = "of",
            ["transactions.total"] = "total",

            ["status.connected"] = "Connected",
            ["status.reconnecting"] = "Reconnecting...",
            ["status.disconnected"] = "Disconnected",
            ["status.connecting"] = "Connecting...",
            ["status.unknown"] = "Unknown",

            ["table.recentTransactions"] = "Recent Transactions",
            ["table.id"] = "ID",
            ["table.amount"] = "Amount",
            ["table.type"] = "Type",
            ["table.source"] = "Source",
            ["table.status"] = "Status",
            ["table.time"] = "Time",

            ["chart.volumeTitle"] = "Transaction Volume (Last 5 Minutes)",
            ["chart.volumeLabel"] = "Volume ($)",
            ["chart.statusTitle"] = "Status Distribution",
            ["chart.completed"] = "Completed",
            ["chart.pending"] = "Pending",
            ["chart.processing"] = "Processing",
            ["chart.failed"] = "Failed",
            ["chart.flagged"] = "Flagged",
            ["chart.sourceTitle"] = "Transactions by Source",
            ["chart.sourceLabel"] = "Transactions",

            ["nav.dashboard"] = "Dashboard",
            ["nav.transactions"] = "Transactions",
            ["nav.brand"] = "RealTime",

            ["footer.builtBy"] = "Built by",
        },
        ["es"] = new()
        {
            ["dashboard.title"] = "Panel de Transacciones en Tiempo Real",
            ["dashboard.transactions1m"] = "Transacciones (1m)",
            ["dashboard.lastHour"] = "\u00faltima hora",
            ["dashboard.volume1m"] = "Volumen (1m)",
            ["dashboard.tps"] = "TPS",
            ["dashboard.successRate"] = "Tasa de \u00e9xito",
            ["dashboard.flagged"] = "Marcadas",
            ["dashboard.connections"] = "Conexiones",

            ["transactions.title"] = "Transacciones",
            ["transactions.status"] = "Estado",
            ["transactions.type"] = "Tipo",
            ["transactions.all"] = "Todos",
            ["transactions.loading"] = "Cargando transacciones...",
            ["transactions.notFound"] = "No se encontraron transacciones.",
            ["transactions.id"] = "ID de Transacci\u00f3n",
            ["transactions.amount"] = "Monto",
            ["transactions.source"] = "Fuente",
            ["transactions.created"] = "Creado",
            ["transactions.previous"] = "Anterior",
            ["transactions.next"] = "Siguiente",
            ["transactions.page"] = "P\u00e1gina",
            ["transactions.of"] = "de",
            ["transactions.total"] = "total",

            ["status.connected"] = "Conectado",
            ["status.reconnecting"] = "Reconectando...",
            ["status.disconnected"] = "Desconectado",
            ["status.connecting"] = "Conectando...",
            ["status.unknown"] = "Desconocido",

            ["table.recentTransactions"] = "Transacciones Recientes",
            ["table.id"] = "ID",
            ["table.amount"] = "Monto",
            ["table.type"] = "Tipo",
            ["table.source"] = "Fuente",
            ["table.status"] = "Estado",
            ["table.time"] = "Hora",

            ["chart.volumeTitle"] = "Volumen de Transacciones (\u00daltimos 5 Minutos)",
            ["chart.volumeLabel"] = "Volumen ($)",
            ["chart.statusTitle"] = "Distribuci\u00f3n de Estados",
            ["chart.completed"] = "Completada",
            ["chart.pending"] = "Pendiente",
            ["chart.processing"] = "Procesando",
            ["chart.failed"] = "Fallida",
            ["chart.flagged"] = "Marcada",
            ["chart.sourceTitle"] = "Transacciones por Fuente",
            ["chart.sourceLabel"] = "Transacciones",

            ["nav.dashboard"] = "Panel",
            ["nav.transactions"] = "Transacciones",
            ["nav.brand"] = "RealTime",

            ["footer.builtBy"] = "Desarrollado por",
        }
    };

    public LocalizationService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", "app-lang");
            if (!string.IsNullOrEmpty(stored) && Translations.ContainsKey(stored))
            {
                _currentLang = stored;
            }
            else
            {
                var browserLang = await _js.InvokeAsync<string>("eval",
                    "navigator.language?.substring(0,2) || 'en'");
                _currentLang = Translations.ContainsKey(browserLang) ? browserLang : "en";
                await _js.InvokeVoidAsync("localStorage.setItem", "app-lang", _currentLang);
            }
        }
        catch
        {
            _currentLang = "en";
        }
    }

    public async Task SetLanguageAsync(string lang)
    {
        if (!Translations.ContainsKey(lang)) return;
        _currentLang = lang;
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", "app-lang", lang);
        }
        catch { /* localStorage may be unavailable */ }
        OnLanguageChanged?.Invoke();
    }

    public string Get(string key)
    {
        if (Translations.TryGetValue(_currentLang, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (Translations.TryGetValue("en", out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
            return fallbackValue;
        return key;
    }
}
