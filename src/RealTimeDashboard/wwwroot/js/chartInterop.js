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
                    borderColor: borderColor || '#3b82f6',
                    backgroundColor: backgroundColor || 'rgba(59,130,246,0.1)',
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
                        ticks: { color: '#94a3b8', maxTicksLimit: 10 },
                        grid: { color: 'rgba(148,163,184,0.1)' }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { color: '#94a3b8' },
                        grid: { color: 'rgba(148,163,184,0.1)' }
                    }
                },
                plugins: {
                    legend: { labels: { color: '#e2e8f0' } }
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
                        '#22c55e', '#eab308', '#3b82f6', '#ef4444', '#6b7280'
                    ],
                    borderColor: '#1e293b',
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
                        labels: { color: '#e2e8f0', padding: 12 }
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
                        '#3b82f6', '#8b5cf6', '#06b6d4', '#f59e0b', '#10b981'
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
                        ticks: { color: '#94a3b8' },
                        grid: { display: false }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { color: '#94a3b8' },
                        grid: { color: 'rgba(148,163,184,0.1)' }
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
