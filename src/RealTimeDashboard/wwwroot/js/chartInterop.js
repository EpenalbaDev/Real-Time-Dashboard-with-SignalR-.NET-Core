const charts = {};

window.chartInterop = {
    createLineChart: function (canvasId, labels, data, label, borderColor, backgroundColor) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        if (charts[canvasId]) {
            charts[canvasId].destroy();
        }

        charts[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    label: label,
                    data: data,
                    borderColor: borderColor || '#2f81f7',
                    backgroundColor: backgroundColor || 'rgba(47,129,247,0.1)',
                    borderWidth: 2,
                    fill: true,
                    tension: 0.3,
                    pointRadius: 0,
                    pointHitRadius: 10
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 300 },
                scales: {
                    x: {
                        ticks: { color: '#8b949e', maxTicksLimit: 10 },
                        grid: { color: 'rgba(139,148,158,0.1)' }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { color: '#8b949e' },
                        grid: { color: 'rgba(139,148,158,0.1)' }
                    }
                },
                plugins: {
                    legend: { labels: { color: '#e6edf3' } }
                }
            }
        });
    },

    createDoughnutChart: function (canvasId, labels, data, colors) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        if (charts[canvasId]) {
            charts[canvasId].destroy();
        }

        charts[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors || [
                        '#2ea043', '#d29922', '#2f81f7', '#da3633', '#8957e5'
                    ],
                    borderColor: '#161b22',
                    borderWidth: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 300 },
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: { color: '#e6edf3', padding: 12 }
                    }
                }
            }
        });
    },

    createBarChart: function (canvasId, labels, data, label, backgroundColor) {
        const ctx = document.getElementById(canvasId);
        if (!ctx) return;

        if (charts[canvasId]) {
            charts[canvasId].destroy();
        }

        charts[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    label: label,
                    data: data,
                    backgroundColor: backgroundColor || [
                        '#2f81f7', '#8957e5', '#39d2c0', '#d29922', '#2ea043'
                    ],
                    borderRadius: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 300 },
                scales: {
                    x: {
                        ticks: { color: '#8b949e' },
                        grid: { display: false }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { color: '#8b949e' },
                        grid: { color: 'rgba(139,148,158,0.1)' }
                    }
                },
                plugins: {
                    legend: { display: false }
                }
            }
        });
    },

    updateChartData: function (canvasId, labels, data) {
        const chart = charts[canvasId];
        if (!chart) return;

        chart.data.labels = labels;
        chart.data.datasets[0].data = data;
        chart.update('none');
    },

    updateDoughnutData: function (canvasId, labels, data) {
        const chart = charts[canvasId];
        if (!chart) return;

        chart.data.labels = labels;
        chart.data.datasets[0].data = data;
        chart.update();
    },

    updateBarData: function (canvasId, labels, data) {
        const chart = charts[canvasId];
        if (!chart) return;

        chart.data.labels = labels;
        chart.data.datasets[0].data = data;
        chart.update();
    },

    destroyChart: function (canvasId) {
        if (charts[canvasId]) {
            charts[canvasId].destroy();
            delete charts[canvasId];
        }
    }
};
