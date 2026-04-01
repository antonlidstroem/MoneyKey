// BudgetPlanner — Blazor JS interop helpers

window.renderBarChart = (elementId, categories, income, expenses) => {
    const el = document.getElementById(elementId);
    if (!el) return;
    if (el._chart) { el._chart.destroy(); }

    el._chart = new ApexCharts(el, {
        chart:   { type: 'bar', height: 280, toolbar: { show: false }, fontFamily: 'Inter, system-ui, sans-serif' },
        series:  [
            { name: 'Inkomster', data: income,   color: '#2E7D32' },
            { name: 'Utgifter',  data: expenses, color: '#C62828' }
        ],
        xaxis:   { categories },
        yaxis:   { labels: { formatter: v => v.toLocaleString('sv-SE') + ' kr' } },
        plotOptions: { bar: { columnWidth: '55%', borderRadius: 4 } },
        legend:  { position: 'top' },
        grid:    { borderColor: '#E0E0E0' },
        tooltip: { y: { formatter: v => v.toLocaleString('sv-SE', { minimumFractionDigits: 0 }) + ' kr' } }
    });
    el._chart.render();
};

window.renderDonutChart = (elementId, labels, values) => {
    const el = document.getElementById(elementId);
    if (!el) return;
    if (el._chart) { el._chart.destroy(); }

    el._chart = new ApexCharts(el, {
        chart:   { type: 'donut', height: 280, toolbar: { show: false }, fontFamily: 'Inter, system-ui, sans-serif' },
        series:  values,
        labels,
        legend:  { position: 'bottom' },
        plotOptions: { pie: { donut: { size: '65%' } } },
        tooltip: { y: { formatter: v => v.toLocaleString('sv-SE', { minimumFractionDigits: 0 }) + ' kr' } }
    });
    el._chart.render();
};

window.renderLineChart = (elementId, categories, values) => {
    const el = document.getElementById(elementId);
    if (!el) return;
    if (el._chart) { el._chart.destroy(); }

    el._chart = new ApexCharts(el, {
        chart:   { type: 'area', height: 220, toolbar: { show: false }, fontFamily: 'Inter, system-ui, sans-serif' },
        series:  [{ name: 'Kassaflöde', data: values, color: '#1565C0' }],
        xaxis:   { categories },
        yaxis:   { labels: { formatter: v => v.toLocaleString('sv-SE') + ' kr' } },
        fill:    { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.3, opacityTo: 0.05 } },
        stroke:  { curve: 'smooth', width: 2 },
        grid:    { borderColor: '#E0E0E0' },
        tooltip: { y: { formatter: v => v.toLocaleString('sv-SE', { minimumFractionDigits: 0 }) + ' kr' } }
    });
    el._chart.render();
};
