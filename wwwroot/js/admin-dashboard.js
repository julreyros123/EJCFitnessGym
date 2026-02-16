
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

    const checkinsList = dashboard.querySelector('[data-checkins-list]');
    const trendSubtitle = dashboard.querySelector('[data-trend-subtitle]');
    const checkinsSubtitle = dashboard.querySelector('[data-checkins-subtitle]');
    const revenueSummary = dashboard.querySelector('[data-revenue-summary]');

    const kpiValueElements = {
        activeMembers: dashboard.querySelector('[data-kpi-value="activeMembers"]'),
        checkIns: dashboard.querySelector('[data-kpi-value="checkIns"]'),
        followUps: dashboard.querySelector('[data-kpi-value="followUps"]'),
        auditAlerts: dashboard.querySelector('[data-kpi-value="auditAlerts"]')
    };

    const kpiMetaElements = {
        activeMembers: dashboard.querySelector('[data-kpi-meta="activeMembers"]'),
        checkIns: dashboard.querySelector('[data-kpi-meta="checkIns"]'),
        followUps: dashboard.querySelector('[data-kpi-meta="followUps"]'),
        auditAlerts: dashboard.querySelector('[data-kpi-meta="auditAlerts"]')
    };

    const kpiBadgeElements = {
        activeMembers: dashboard.querySelector('[data-kpi-badge="activeMembers"]'),
        checkIns: dashboard.querySelector('[data-kpi-badge="checkIns"]'),
        followUps: dashboard.querySelector('[data-kpi-badge="followUps"]'),
        auditAlerts: dashboard.querySelector('[data-kpi-badge="auditAlerts"]')
    };

    const numberFormatter = new Intl.NumberFormat('en-PH');
    const currencyFormatter = new Intl.NumberFormat('en-PH', {
        style: 'currency',
        currency: 'PHP',
        maximumFractionDigits: 0
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

    const addDays = (value, amount) => {
        const next = cloneDate(value);
        next.setDate(next.getDate() + amount);
        return next;
    };

    const toInputDate = (value) => value.toISOString().slice(0, 10);

    const compactCurrency = (value) => {
        const absolute = Math.abs(value);
        if (absolute >= 1000000) {
            return `PHP ${(value / 1000000).toFixed(1)}M`;
        }
        if (absolute >= 1000) {
            return `PHP ${(value / 1000).toFixed(0)}k`;
        }
        return `PHP ${Math.round(value)}`;
    };

    const datasetStart = new Date('2024-01-01T00:00:00');
    const datasetEnd = cloneDate(new Date());

    const dailyRecords = [];
    {
        const cursor = cloneDate(datasetStart);
        let index = 0;

        while (cursor <= datasetEnd) {
            const weekday = cursor.getDay();
            const month = cursor.getMonth();
            const isWeekend = weekday === 0 || weekday === 6;

            const seasonal = (Math.sin(index / 18) * 22) + (Math.cos(index / 37) * 13);
            const trendLift = index * 0.18;
            const monthLift = (month === 0 || month === 5 || month === 11) ? 10 : 0;

            const baseCheckIns = 140 + seasonal + trendLift + monthLift;
            const checkIns = Math.max(38, Math.round(baseCheckIns * (isWeekend ? 0.76 : 1)));
            const revenue = Math.round((checkIns * (215 + ((index % 9) * 6))) + (cursor.getDate() === 1 ? 9000 : 0));
            const activeMembers = Math.max(780, Math.round(920 + (index * 0.25) + (Math.sin(index / 28) * 26)));
            const followUps = Math.max(4, Math.round((activeMembers * 0.011) + (checkIns < 90 ? 4 : 1) + (weekday === 1 ? 2 : 0)));
            const alerts = Math.max(1, Math.round((followUps / 7) + (weekday === 5 ? 1 : 0)));

            dailyRecords.push({
                date: cloneDate(cursor),
                checkIns,
                revenue,
                activeMembers,
                followUps,
                alerts
            });

            cursor.setDate(cursor.getDate() + 1);
            index += 1;
        }
    }

    const state = {
        start: addDays(datasetEnd, -29),
        end: cloneDate(datasetEnd),
        mode: 'daily'
    };

    const clampDate = (value) => {
        if (value < datasetStart) {
            return cloneDate(datasetStart);
        }
        if (value > datasetEnd) {
            return cloneDate(datasetEnd);
        }
        return cloneDate(value);
    };

    const filterRecords = () =>
        dailyRecords.filter((item) => item.date >= state.start && item.date <= state.end);

    const summarizeRecords = (records) => {
        const totals = records.reduce((accumulator, current) => {
            accumulator.checkIns += current.checkIns;
            accumulator.revenue += current.revenue;
            accumulator.activeMembers += current.activeMembers;
            accumulator.followUps += current.followUps;
            accumulator.alerts = Math.max(accumulator.alerts, current.alerts);
            return accumulator;
        }, {
            checkIns: 0,
            revenue: 0,
            activeMembers: 0,
            followUps: 0,
            alerts: 0
        });

        const days = Math.max(records.length, 1);
        return {
            days,
            totalCheckIns: totals.checkIns,
            totalRevenue: totals.revenue,
            averageMembers: Math.round(totals.activeMembers / days),
            averageFollowUps: Math.round(totals.followUps / days),
            peakAlerts: totals.alerts
        };
    };

    const groupByYear = (records, valueKey) => {
        const grouped = new Map();
        records.forEach((item) => {
            const year = item.date.getFullYear();
            grouped.set(year, (grouped.get(year) || 0) + item[valueKey]);
        });

        const ordered = Array.from(grouped.entries()).sort((a, b) => a[0] - b[0]);
        return {
            labels: ordered.map(([year]) => String(year)),
            values: ordered.map(([, value]) => value)
        };
    };

    const sampleDailySeries = (records, valueKey) => {
        const labels = records.map((item) => shortDateFormatter.format(item.date));
        const values = records.map((item) => item[valueKey]);
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

    const buildTrendSeries = (records) =>
        state.mode === 'yearly'
            ? groupByYear(records, 'revenue')
            : sampleDailySeries(records, 'revenue');

    const buildCheckinRows = (records) => {
        if (state.mode === 'yearly') {
            const grouped = groupByYear(records, 'checkIns');
            return grouped.labels.map((label, index) => ({
                label,
                value: grouped.values[index]
            }));
        }

        return records.slice(-7).map((item) => ({
            label: shortDateFormatter.format(item.date),
            value: item.checkIns
        }));
    };

    const renderCheckins = (rows) => {
        if (!checkinsList) {
            return;
        }

        checkinsList.innerHTML = '';
        if (!rows.length) {
            const fallback = document.createElement('li');
            fallback.innerHTML = '<span>N/A</span><div class="progress"><div class="progress-bar" style="width: 0%"></div></div><strong>0</strong>';
            checkinsList.appendChild(fallback);
            return;
        }

        const maxValue = Math.max(...rows.map((row) => row.value), 1);
        rows.forEach((row) => {
            const item = document.createElement('li');
            const width = Math.max(6, Math.round((row.value / maxValue) * 100));

            item.innerHTML = `
                <span>${row.label}</span>
                <div class="progress"><div class="progress-bar" style="width: ${width}%"></div></div>
                <strong>${numberFormatter.format(row.value)}</strong>
            `;

            checkinsList.appendChild(item);
        });
    };

    const setKpi = (key, value, meta, badge) => {
        if (kpiValueElements[key]) {
            kpiValueElements[key].textContent = numberFormatter.format(value);
        }
        if (kpiMetaElements[key]) {
            kpiMetaElements[key].textContent = meta;
        }
        if (kpiBadgeElements[key]) {
            kpiBadgeElements[key].textContent = badge;
        }
    };

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
                    tension: 0.35,
                    pointRadius: 3,
                    pointHoverRadius: 5,
                    borderWidth: 2,
                    borderColor: 'rgba(163, 230, 53, 0.95)',
                    backgroundColor: (context) => {
                        const { chart } = context;
                        const { ctx, chartArea } = chart;
                        if (!chartArea) {
                            return 'rgba(132, 204, 22, 0.2)';
                        }

                        const gradient = ctx.createLinearGradient(0, chartArea.top, 0, chartArea.bottom);
                        gradient.addColorStop(0, 'rgba(163, 230, 53, 0.38)');
                        gradient.addColorStop(1, 'rgba(163, 230, 53, 0.02)');
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
                        color: '#9fb3cb',
                        font: {
                            size: 12,
                            weight: '500'
                        }
                    }
                },
                y: {
                    beginAtZero: true,
                    ticks: {
                        color: '#9fb3cb',
                        callback: (value) => compactCurrency(Number(value))
                    },
                    grid: {
                        color: 'rgba(159, 179, 203, 0.16)'
                    }
                }
            }
        }
    });

    const renderDashboard = () => {
        const records = filterRecords();
        if (!records.length) {
            return;
        }

        const summary = summarizeRecords(records);
        const trendSeries = buildTrendSeries(records);
        const checkinRows = buildCheckinRows(records);

        revenueChart.data.labels = trendSeries.labels;
        revenueChart.data.datasets[0].data = trendSeries.values;
        revenueChart.data.datasets[0].label = state.mode === 'yearly'
            ? 'Revenue (year to year)'
            : 'Revenue (day to day)';
        revenueChart.update();

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
            const averagePerDay = Math.round(summary.totalRevenue / summary.days);
            revenueSummary.textContent = `Total revenue ${currencyFormatter.format(summary.totalRevenue)} from ${summary.days} days. Average ${currencyFormatter.format(averagePerDay)} per day.`;
        }

        setKpi('activeMembers', summary.averageMembers, 'average members in selected range', 'Average');
        setKpi('checkIns', summary.totalCheckIns, `${summary.days} day total`, 'Total');
        setKpi('followUps', summary.averageFollowUps, 'average open follow-ups per day', 'Daily Avg');
        setKpi('auditAlerts', summary.peakAlerts, 'highest daily alert count in range', 'Peak');

        renderCheckins(checkinRows);
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
        renderDashboard();
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
            renderDashboard();
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
            renderDashboard();
        });
    }

    if (endInput) {
        endInput.addEventListener('change', () => {
            applyControlsToState();
            setQuickButtonsState('');
            renderDashboard();
        });
    }

    if (trendModeSelect) {
        trendModeSelect.addEventListener('change', () => {
            applyControlsToState();
            renderDashboard();
        });
    }

    syncInputs();
    setQuickButtonsState('30');
    renderDashboard();
})();
