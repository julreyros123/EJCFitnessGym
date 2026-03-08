(() => {
    const dashboard = document.querySelector('[data-admin-dashboard]');
    const chartElement = document.getElementById('revenueTrendChart');
    if (!dashboard || !chartElement || !window.Chart) {
        return;
    }

    const startInput = dashboard.querySelector('[data-filter-start]');
    const endInput = dashboard.querySelector('[data-filter-end]');
    const trendModeSelect = dashboard.querySelector('[data-trend-mode]');
    const applyButton = dashboard.querySelector('[data-filter-apply]');
    const resetButton = dashboard.querySelector('[data-filter-reset]');
    const quickRangeButtons = Array.from(dashboard.querySelectorAll('[data-filter-quick]'));

    const checkinsChartElement = dashboard.querySelector('#checkinsTrendChart');
    const revenueByPlanChartElement = dashboard.querySelector('#revenueByPlanChart');
    const branchCheckinsChartElement = dashboard.querySelector('#branchCheckinsChart');
    const trendSubtitle = dashboard.querySelector('[data-trend-subtitle]');
    const checkinsSubtitle = dashboard.querySelector('[data-checkins-subtitle]');
    const revenueSummary = dashboard.querySelector('[data-revenue-summary]');

    const kpiValueElements = {
        activeMembers: dashboard.querySelector('[data-kpi-value="activeMembers"]'),
        checkIns: dashboard.querySelector('[data-kpi-value="checkIns"]'),
        followUps: dashboard.querySelector('[data-kpi-value="followUps"]'),
        expiringPlans: dashboard.querySelector('[data-kpi-value="expiringPlans"]'),
        auditAlerts: dashboard.querySelector('[data-kpi-value="auditAlerts"]')
    };

    const kpiMetaElements = {
        activeMembers: dashboard.querySelector('[data-kpi-meta="activeMembers"]'),
        checkIns: dashboard.querySelector('[data-kpi-meta="checkIns"]'),
        followUps: dashboard.querySelector('[data-kpi-meta="followUps"]'),
        expiringPlans: dashboard.querySelector('[data-kpi-meta="expiringPlans"]'),
        auditAlerts: dashboard.querySelector('[data-kpi-meta="auditAlerts"]')
    };

    const kpiBadgeElements = {
        activeMembers: dashboard.querySelector('[data-kpi-badge="activeMembers"]'),
        checkIns: dashboard.querySelector('[data-kpi-badge="checkIns"]'),
        followUps: dashboard.querySelector('[data-kpi-badge="followUps"]'),
        expiringPlans: dashboard.querySelector('[data-kpi-badge="expiringPlans"]'),
        auditAlerts: dashboard.querySelector('[data-kpi-badge="auditAlerts"]')
    };

    const numberFormatter = new Intl.NumberFormat('en-PH');
    const currencyFormatter = new Intl.NumberFormat('en-PH', {
        style: 'currency',
        currency: 'PHP',
        maximumFractionDigits: 0
    });
    const compactNumberFormatter = new Intl.NumberFormat('en-US', {
        notation: 'compact',
        maximumFractionDigits: 1
    });
    const longDateFormatter = new Intl.DateTimeFormat('en-US', {
        month: 'short',
        day: 'numeric',
        year: 'numeric'
    });
    const shortDateFormatter = new Intl.DateTimeFormat('en-US', {
        month: 'short',
        day: 'numeric'
    });

    const getThemeToken = (token, fallback) => {
        const value = window.getComputedStyle(document.body).getPropertyValue(token).trim();
        return value || fallback;
    };
    const getChartThemeTokens = () => ({
        axisLabel: getThemeToken('--ejc-chart-axis', '#94a3b8'),
        text: getThemeToken('--ejc-chart-text', '#cbd5f5'),
        line: getThemeToken('--ejc-chart-line', '#84cc16'),
        lineHover: getThemeToken('--ejc-chart-line-hover', '#a3e635'),
        grid: getThemeToken('--ejc-chart-grid', 'rgba(148, 163, 184, 0.15)'),
        axisLine: getThemeToken('--ejc-chart-axis-line', 'rgba(148, 163, 184, 0.25)'),
        tooltipBg: getThemeToken('--ejc-chart-tooltip-bg', '#0f172a'),
        tooltipText: getThemeToken('--ejc-chart-tooltip-text', '#e2e8f0'),
        fillTop: getThemeToken('--ejc-chart-fill-top', 'rgba(163, 230, 53, 0.34)'),
        fillBottom: getThemeToken('--ejc-chart-fill-bottom', 'rgba(163, 230, 53, 0.03)')
    });

    const toNumber = (value) => {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : 0;
    };

    const compactCurrency = (value) => `PHP ${compactNumberFormatter.format(value).toUpperCase()}`;

    const cloneDate = (value) => {
        const date = new Date(value);
        date.setHours(0, 0, 0, 0);
        return date;
    };

    const parseInputDate = (value) => {
        if (!value) {
            return null;
        }

        const parsed = new Date(`${value}T00:00:00`);
        if (Number.isNaN(parsed.getTime())) {
            return null;
        }

        return cloneDate(parsed);
    };

    const parseApiDate = (value) => {
        if (!value) {
            return null;
        }

        if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
            return parseInputDate(value);
        }

        const parsed = new Date(value);
        if (Number.isNaN(parsed.getTime())) {
            return null;
        }

        return cloneDate(parsed);
    };

    const addDays = (value, amount) => {
        const next = cloneDate(value);
        next.setDate(next.getDate() + amount);
        return next;
    };

    const toInputDate = (value) => value.toISOString().slice(0, 10);

    const datasetEnd = cloneDate(new Date());
    const datasetStart = addDays(datasetEnd, -729);
    const state = {
        start: addDays(datasetEnd, -29),
        end: cloneDate(datasetEnd),
        mode: 'daily'
    };
    let latestOverviewRequestId = 0;

    const clampDate = (value) => {
        if (value < datasetStart) {
            return cloneDate(datasetStart);
        }
        if (value > datasetEnd) {
            return cloneDate(datasetEnd);
        }
        return cloneDate(value);
    };

    const normalizeDailyRows = (rows) => {
        if (!Array.isArray(rows)) {
            return [];
        }

        return rows
            .map((row) => ({
                date: parseApiDate(row.date),
                revenue: toNumber(row.revenue),
                checkIns: Math.max(0, Math.round(toNumber(row.checkIns)))
            }))
            .filter((row) => row.date)
            .sort((left, right) => left.date - right.date);
    };

    const groupByYear = (rows, valueKey) => {
        const grouped = new Map();
        rows.forEach((row) => {
            const year = row.date.getFullYear();
            grouped.set(year, (grouped.get(year) || 0) + toNumber(row[valueKey]));
        });

        const ordered = Array.from(grouped.entries()).sort((a, b) => a[0] - b[0]);
        return {
            labels: ordered.map(([year]) => String(year)),
            values: ordered.map(([, value]) => value)
        };
    };

    const sampleDailySeries = (rows, valueKey) => {
        const labels = rows.map((row) => shortDateFormatter.format(row.date));
        const values = rows.map((row) => toNumber(row[valueKey]));
        const maxPoints = 45;

        if (labels.length <= maxPoints) {
            return { labels, values };
        }

        const step = Math.ceil(labels.length / maxPoints);
        const sampledLabels = [];
        const sampledValues = [];

        for (let index = 0; index < labels.length; index += step) {
            sampledLabels.push(labels[index]);
            sampledValues.push(values[index]);
        }

        if (sampledLabels[sampledLabels.length - 1] !== labels[labels.length - 1]) {
            sampledLabels.push(labels[labels.length - 1]);
            sampledValues.push(values[values.length - 1]);
        }

        return {
            labels: sampledLabels,
            values: sampledValues
        };
    };

    const buildTrendSeries = (rows) =>
        state.mode === 'yearly'
            ? groupByYear(rows, 'revenue')
            : sampleDailySeries(rows, 'revenue');

    const buildCheckinRows = (rows) => {
        if (state.mode === 'yearly') {
            const grouped = groupByYear(rows, 'checkIns');
            return grouped.labels.map((label, index) => ({
                label,
                value: Math.round(grouped.values[index])
            }));
        }

        return rows.slice(-7).map((row) => ({
            label: shortDateFormatter.format(row.date),
            value: row.checkIns
        }));
    };

    const setKpi = (key, value, meta, badge) => {
        if (kpiValueElements[key]) {
            kpiValueElements[key].textContent = numberFormatter.format(Math.max(0, Math.round(toNumber(value))));
        }
        if (kpiMetaElements[key]) {
            kpiMetaElements[key].textContent = meta;
        }
        if (kpiBadgeElements[key]) {
            kpiBadgeElements[key].textContent = badge;
        }
    };

    const initialChartTheme = getChartThemeTokens();
    const chartContext = chartElement.getContext('2d');
    const revenueChart = new Chart(chartContext, {
        type: 'line',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'Revenue',
                    data: [],
                    fill: true,
                    tension: 0.45,
                    pointRadius: 3,
                    pointHoverRadius: 5,
                    borderWidth: 2,
                    borderColor: initialChartTheme.line,
                    pointBackgroundColor: initialChartTheme.line,
                    pointBorderColor: initialChartTheme.line,
                    pointHoverBackgroundColor: initialChartTheme.lineHover,
                    pointHoverBorderColor: initialChartTheme.lineHover,
                    backgroundColor: (context) => {
                        const { chart } = context;
                        const { ctx, chartArea } = chart;
                        const palette = getChartThemeTokens();
                        if (!chartArea) {
                            return palette.fillTop;
                        }

                        const gradient = ctx.createLinearGradient(0, chartArea.top, 0, chartArea.bottom);
                        gradient.addColorStop(0, palette.fillTop);
                        gradient.addColorStop(1, palette.fillBottom);
                        return gradient;
                    }
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'index',
                intersect: false
            },
            plugins: {
                legend: {
                    display: false
                },
                tooltip: {
                    backgroundColor: initialChartTheme.tooltipBg,
                    titleColor: initialChartTheme.tooltipText,
                    bodyColor: initialChartTheme.tooltipText,
                    borderColor: initialChartTheme.axisLine,
                    borderWidth: 1,
                    callbacks: {
                        label: (context) => `Revenue: ${currencyFormatter.format(context.parsed.y)}`
                    }
                }
            },
            scales: {
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        maxRotation: 0,
                        autoSkip: true,
                        color: initialChartTheme.axisLabel,
                        font: {
                            size: 12,
                            weight: '500'
                        }
                    },
                    border: {
                        color: initialChartTheme.axisLine
                    }
                },
                y: {
                    beginAtZero: true,
                    ticks: {
                        color: initialChartTheme.axisLabel,
                        callback: (value) => compactCurrency(Number(value))
                    },
                    grid: {
                        color: initialChartTheme.grid
                    },
                    border: {
                        color: initialChartTheme.axisLine
                    }
                }
            }
        }
    });

    const checkinsChartContext = checkinsChartElement ? checkinsChartElement.getContext('2d') : null;
    const checkinsChart = checkinsChartContext ? new Chart(checkinsChartContext, {
        type: 'bar',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'Check-Ins',
                    data: [],
                    backgroundColor: initialChartTheme.line,
                    borderRadius: 4,
                    maxBarThickness: 48
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'index',
                intersect: false
            },
            plugins: {
                legend: {
                    display: false
                },
                tooltip: {
                    backgroundColor: initialChartTheme.tooltipBg,
                    titleColor: initialChartTheme.tooltipText,
                    bodyColor: initialChartTheme.tooltipText,
                    borderColor: initialChartTheme.axisLine,
                    borderWidth: 1
                }
            },
            scales: {
                x: {
                    grid: {
                        display: false
                    },
                    ticks: {
                        color: initialChartTheme.axisLabel,
                        font: { size: 12, weight: '500' }
                    },
                    border: { color: initialChartTheme.axisLine }
                },
                y: {
                    beginAtZero: true,
                    ticks: {
                        color: initialChartTheme.axisLabel,
                        stepSize: 1
                    },
                    grid: { color: initialChartTheme.grid },
                    border: { color: initialChartTheme.axisLine }
                }
            }
        }
    }) : null;

    const revenueByPlanChartContext = revenueByPlanChartElement ? revenueByPlanChartElement.getContext('2d') : null;
    const revenueByPlanChart = revenueByPlanChartContext ? new Chart(revenueByPlanChartContext, {
        type: 'doughnut',
        data: {
            labels: [],
            datasets: [{
                data: [],
                backgroundColor: [
                    '#84cc16', '#3b82f6', '#f59e0b', '#ec4899', '#8b5cf6', '#06b6d4'
                ],
                borderWidth: 0
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            cutout: '70%',
            layout: {
                padding: 16
            },
            plugins: {
                legend: {
                    position: 'bottom',
                    labels: { color: initialChartTheme.axisLabel, padding: 20 }
                },
                tooltip: {
                    backgroundColor: initialChartTheme.tooltipBg,
                    titleColor: initialChartTheme.tooltipText,
                    bodyColor: initialChartTheme.tooltipText,
                    borderColor: initialChartTheme.axisLine,
                    borderWidth: 1,
                    callbacks: {
                        label: (context) => `Revenue: ${currencyFormatter.format(context.parsed)}`
                    }
                }
            }
        }
    }) : null;

    const branchCheckinsChartContext = branchCheckinsChartElement ? branchCheckinsChartElement.getContext('2d') : null;
    const branchCheckinsChart = branchCheckinsChartContext ? new Chart(branchCheckinsChartContext, {
        type: 'bar',
        data: {
            labels: [],
            datasets: [
                {
                    label: 'Check-Ins',
                    data: [],
                    backgroundColor: initialChartTheme.line,
                    borderRadius: 4,
                    maxBarThickness: 48
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'index',
                intersect: false
            },
            plugins: {
                legend: {
                    display: false
                },
                tooltip: {
                    backgroundColor: initialChartTheme.tooltipBg,
                    titleColor: initialChartTheme.tooltipText,
                    bodyColor: initialChartTheme.tooltipText,
                    borderColor: initialChartTheme.axisLine,
                    borderWidth: 1
                }
            },
            scales: {
                x: {
                    grid: { display: false },
                    ticks: {
                        color: initialChartTheme.axisLabel,
                        font: { size: 12, weight: '500' }
                    },
                    border: { color: initialChartTheme.axisLine }
                },
                y: {
                    beginAtZero: true,
                    ticks: {
                        color: initialChartTheme.axisLabel,
                        stepSize: 1
                    },
                    grid: { color: initialChartTheme.grid },
                    border: { color: initialChartTheme.axisLine }
                }
            }
        }
    }) : null;

    const applyChartTheme = () => {
        const palette = getChartThemeTokens();
        const revenueDataset = revenueChart.data.datasets[0];
        revenueDataset.borderColor = palette.line;
        revenueDataset.pointBackgroundColor = palette.line;
        revenueDataset.pointBorderColor = palette.line;
        revenueDataset.pointHoverBackgroundColor = palette.lineHover;
        revenueDataset.pointHoverBorderColor = palette.lineHover;

        revenueChart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
        revenueChart.options.plugins.tooltip.titleColor = palette.tooltipText;
        revenueChart.options.plugins.tooltip.bodyColor = palette.tooltipText;
        revenueChart.options.plugins.tooltip.borderColor = palette.axisLine;

        revenueChart.options.scales.x.ticks.color = palette.axisLabel;
        revenueChart.options.scales.x.border.color = palette.axisLine;
        revenueChart.options.scales.y.ticks.color = palette.axisLabel;
        revenueChart.options.scales.y.grid.color = palette.grid;
        revenueChart.options.scales.y.border.color = palette.axisLine;

        revenueChart.update('none');

        if (checkinsChart) {
            checkinsChart.data.datasets[0].backgroundColor = palette.line;

            checkinsChart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
            checkinsChart.options.plugins.tooltip.titleColor = palette.tooltipText;
            checkinsChart.options.plugins.tooltip.bodyColor = palette.tooltipText;
            checkinsChart.options.plugins.tooltip.borderColor = palette.axisLine;

            checkinsChart.options.scales.x.ticks.color = palette.axisLabel;
            checkinsChart.options.scales.x.border.color = palette.axisLine;
            checkinsChart.options.scales.y.ticks.color = palette.axisLabel;
            checkinsChart.options.scales.y.grid.color = palette.grid;
            checkinsChart.options.scales.y.border.color = palette.axisLine;

            checkinsChart.update('none');
        }

        if (revenueByPlanChart) {
            revenueByPlanChart.options.plugins.legend.labels.color = palette.axisLabel;
            revenueByPlanChart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
            revenueByPlanChart.options.plugins.tooltip.titleColor = palette.tooltipText;
            revenueByPlanChart.options.plugins.tooltip.bodyColor = palette.tooltipText;
            revenueByPlanChart.options.plugins.tooltip.borderColor = palette.axisLine;
            revenueByPlanChart.update('none');
        }

        if (branchCheckinsChart) {
            branchCheckinsChart.data.datasets[0].backgroundColor = palette.line;
            branchCheckinsChart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
            branchCheckinsChart.options.plugins.tooltip.titleColor = palette.tooltipText;
            branchCheckinsChart.options.plugins.tooltip.bodyColor = palette.tooltipText;
            branchCheckinsChart.options.plugins.tooltip.borderColor = palette.axisLine;
            branchCheckinsChart.options.scales.x.ticks.color = palette.axisLabel;
            branchCheckinsChart.options.scales.x.border.color = palette.axisLine;
            branchCheckinsChart.options.scales.y.ticks.color = palette.axisLabel;
            branchCheckinsChart.options.scales.y.grid.color = palette.grid;
            branchCheckinsChart.options.scales.y.border.color = palette.axisLine;
            branchCheckinsChart.update('none');
        }
    };

    if (typeof MutationObserver === 'function') {
        const themeObserver = new MutationObserver((mutations) => {
            const changedTheme = mutations.some((mutation) =>
                mutation.type === 'attributes' && mutation.attributeName === 'data-ejc-theme');
            if (changedTheme) {
                applyChartTheme();
            }
        });
        themeObserver.observe(document.body, {
            attributes: true,
            attributeFilter: ['data-ejc-theme']
        });
    }

    applyChartTheme();

    const loadOverview = async () => {
        const requestId = ++latestOverviewRequestId;
        const fromUtc = cloneDate(state.start);
        const toUtc = cloneDate(state.end);
        toUtc.setHours(23, 59, 59, 999);

        const query = new URLSearchParams({
            fromUtc: fromUtc.toISOString(),
            toUtc: toUtc.toISOString()
        });

        try {
            const response = await fetch(`/api/admin/dashboard/overview?${query.toString()}`, {
                headers: { Accept: 'application/json' }
            });
            if (!response.ok || requestId !== latestOverviewRequestId) {
                return null;
            }

            const payload = await response.json();
            if (requestId !== latestOverviewRequestId) {
                return null;
            }

            return payload;
        } catch (error) {
            console.error('Failed to load admin dashboard overview.', error);
            return null;
        }
    };

    const renderDashboard = async () => {
        const payload = await loadOverview();
        if (!payload) {
            return;
        }

        const rows = normalizeDailyRows(payload.daily);
        const trendSeries = buildTrendSeries(rows);
        const checkinRows = buildCheckinRows(rows);

        revenueChart.data.labels = trendSeries.labels;
        revenueChart.data.datasets[0].data = trendSeries.values;
        revenueChart.data.datasets[0].label = state.mode === 'yearly'
            ? 'Revenue (year to year)'
            : 'Revenue (day to day)';
        revenueChart.update();

        const dayCount = Math.max(1, Math.round(toNumber(payload.dayCount) || rows.length || 1));
        const kpis = payload.kpis || {};
        const summary = payload.summary || {};
        const totalRevenue = toNumber(summary.totalRevenue) || rows.reduce((sum, row) => sum + row.revenue, 0);
        const averageRevenuePerDay = toNumber(summary.averageRevenuePerDay) || (dayCount > 0 ? totalRevenue / dayCount : 0);

        if (trendSubtitle) {
            const modeText = state.mode === 'yearly' ? 'Year to year' : 'Day to day';
            trendSubtitle.textContent = `${longDateFormatter.format(state.start)} - ${longDateFormatter.format(state.end)} • ${modeText}`;
        }

        if (checkinsSubtitle) {
            checkinsSubtitle.textContent = state.mode === 'yearly'
                ? 'Yearly totals'
                : 'Latest 7 days in range';
        }

        if (revenueSummary) {
            revenueSummary.textContent = `Total revenue ${currencyFormatter.format(totalRevenue)} from ${dayCount} days. Average ${currencyFormatter.format(averageRevenuePerDay)} per day.`;
        }

        setKpi('activeMembers', kpis.activeMembers, 'active in selected scope', 'Active');
        setKpi('checkIns', kpis.checkIns, `${dayCount}-day total`, 'Total');
        setKpi('followUps', kpis.followUps, 'open follow-ups', 'Open');
        setKpi('expiringPlans', kpis.expiringPlans, 'ending in next 7 days', '7D');
        setKpi('auditAlerts', kpis.auditAlerts, 'highest daily count', 'Peak');

        if (checkinsChart) {
            checkinsChart.data.labels = checkinRows.map(r => r.label);
            checkinsChart.data.datasets[0].data = checkinRows.map(r => r.value);
            checkinsChart.update();
        }

        if (revenueByPlanChart && payload.revenueByPlan) {
            const totalPlanRevenue = payload.revenueByPlan.reduce((sum, r) => sum + r.revenue, 0);
            revenueByPlanChart.data.labels = payload.revenueByPlan.map(r => {
                const percentage = totalPlanRevenue > 0 ? ((r.revenue / totalPlanRevenue) * 100).toFixed(1) : 0;
                return `${r.planName} (${percentage}%)`;
            });
            revenueByPlanChart.data.datasets[0].data = payload.revenueByPlan.map(r => r.revenue);
            revenueByPlanChart.update();
        }

        if (branchCheckinsChart && payload.checkInsByBranch) {
            branchCheckinsChart.data.labels = payload.checkInsByBranch.map(r => r.branchId);
            branchCheckinsChart.data.datasets[0].data = payload.checkInsByBranch.map(r => r.checkIns);
            branchCheckinsChart.update();
        }
    };

    const setQuickButtonsState = (activeValue) => {
        quickRangeButtons.forEach((button) => {
            button.classList.toggle('active', button.dataset.filterQuick === activeValue);
        });
    };

    const syncInputs = () => {
        if (startInput) {
            startInput.value = toInputDate(state.start);
        }
        if (endInput) {
            endInput.value = toInputDate(state.end);
        }
        if (trendModeSelect) {
            trendModeSelect.value = state.mode;
        }
    };

    const applyControlsToState = () => {
        const inputStart = parseInputDate(startInput ? startInput.value : '') || addDays(datasetEnd, -29);
        const inputEnd = parseInputDate(endInput ? endInput.value : '') || cloneDate(datasetEnd);
        const mode = trendModeSelect && trendModeSelect.value === 'yearly' ? 'yearly' : 'daily';

        let start = clampDate(inputStart);
        let end = clampDate(inputEnd);
        if (start > end) {
            [start, end] = [end, start];
        }

        state.start = start;
        state.end = end;
        state.mode = mode;
    };

    const setQuickRange = (quickKey) => {
        const end = cloneDate(datasetEnd);
        let start = cloneDate(end);

        switch (quickKey) {
            case 'today':
                break;
            case '7':
                start = addDays(end, -6);
                break;
            case '30':
                start = addDays(end, -29);
                break;
            case 'ytd':
                start = new Date(end.getFullYear(), 0, 1);
                break;
            case '365':
                start = addDays(end, -364);
                break;
            case '730':
                start = addDays(end, -729);
                break;
            default:
                start = addDays(end, -29);
                break;
        }

        state.start = clampDate(start);
        state.end = clampDate(end);
        syncInputs();
        setQuickButtonsState(quickKey);
        void renderDashboard();
    };

    quickRangeButtons.forEach((button) => {
        button.addEventListener('click', () => {
            setQuickRange(button.dataset.filterQuick || '30');
        });
    });

    if (applyButton) {
        applyButton.addEventListener('click', () => {
            applyControlsToState();
            setQuickButtonsState('');
            void renderDashboard();
        });
    }

    if (resetButton) {
        resetButton.addEventListener('click', () => {
            state.mode = 'daily';
            setQuickRange('30');
        });
    }

    if (startInput) {
        startInput.addEventListener('change', () => {
            applyControlsToState();
            setQuickButtonsState('');
            void renderDashboard();
        });
    }

    if (endInput) {
        endInput.addEventListener('change', () => {
            applyControlsToState();
            setQuickButtonsState('');
            void renderDashboard();
        });
    }

    if (trendModeSelect) {
        trendModeSelect.addEventListener('change', () => {
            applyControlsToState();
            void renderDashboard();
        });
    }

    window.addEventListener('ejc:erp-event', (event) => {
        const eventType = event && event.detail ? event.detail.eventType : '';
        if (!eventType) {
            return;
        }

        if (eventType === 'payment.succeeded' ||
            eventType === 'staff.member.checkin' ||
            eventType === 'staff.member.checkout' ||
            eventType === 'membership.activated' ||
            eventType === 'membership.renewed') {
            void renderDashboard();
        }
    });

    syncInputs();
    setQuickButtonsState('30');
    void renderDashboard();
})();
