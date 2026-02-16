(() => {
  const dashboard = document.querySelector("[data-finance-dashboard]");
  if (!dashboard || typeof Chart === "undefined") {
    return;
  }

  const trendCanvas = dashboard.querySelector("#financeTrendChart");
  const mixCanvas = dashboard.querySelector("#financeMixChart");
  const pipelineCanvas = dashboard.querySelector("#financePipelineChart");
  if (!trendCanvas || !mixCanvas || !pipelineCanvas) {
    return;
  }

  const startInput = dashboard.querySelector("[data-fin-filter-start]");
  const endInput = dashboard.querySelector("[data-fin-filter-end]");
  const trendModeSelect = dashboard.querySelector("[data-fin-trend-mode]");
  const applyButton = dashboard.querySelector("[data-fin-filter-apply]");
  const resetButton = dashboard.querySelector("[data-fin-filter-reset]");
  const quickRangeButtons = Array.from(dashboard.querySelectorAll("[data-fin-filter-quick]"));

  const trendSubtitle = dashboard.querySelector("[data-fin-trend-subtitle]");
  const trendSummary = dashboard.querySelector("[data-fin-trend-summary]");
  const mixSubtitle = dashboard.querySelector("[data-fin-mix-subtitle]");
  const pipelineSubtitle = dashboard.querySelector("[data-fin-pipeline-subtitle]");
  const pipelineNote = dashboard.querySelector("[data-fin-pipeline-note]");
  const headerState = dashboard.querySelector("[data-fin-header-state] span");

  const kpiValueElements = {
    grossRevenue: dashboard.querySelector('[data-fin-kpi-value="grossRevenue"]'),
    grossProfit: dashboard.querySelector('[data-fin-kpi-value="grossProfit"]'),
    netProfit: dashboard.querySelector('[data-fin-kpi-value="netProfit"]'),
    projectedNetProfit: dashboard.querySelector('[data-fin-kpi-value="projectedNetProfit"]')
  };

  const kpiMetaElements = {
    grossRevenue: dashboard.querySelector('[data-fin-kpi-meta="grossRevenue"]'),
    grossProfit: dashboard.querySelector('[data-fin-kpi-meta="grossProfit"]'),
    netProfit: dashboard.querySelector('[data-fin-kpi-meta="netProfit"]'),
    projectedNetProfit: dashboard.querySelector('[data-fin-kpi-meta="projectedNetProfit"]')
  };

  const mixValueElements = {
    cogsShare: dashboard.querySelector('[data-fin-mix-value="cogsShare"]'),
    opexShare: dashboard.querySelector('[data-fin-mix-value="opexShare"]'),
    netMargin: dashboard.querySelector('[data-fin-mix-value="netMargin"]')
  };

  const pipelineValueElements = {
    forReview: dashboard.querySelector('[data-fin-pipeline-value="forReview"]'),
    pending: dashboard.querySelector('[data-fin-pipeline-value="pending"]'),
    queued: dashboard.querySelector('[data-fin-pipeline-value="queued"]'),
    approved: dashboard.querySelector('[data-fin-pipeline-value="approved"]')
  };

  const pipelineShareElements = {
    forReview: dashboard.querySelector('[data-fin-pipeline-share="forReview"]'),
    pending: dashboard.querySelector('[data-fin-pipeline-share="pending"]'),
    queued: dashboard.querySelector('[data-fin-pipeline-share="queued"]'),
    approved: dashboard.querySelector('[data-fin-pipeline-share="approved"]')
  };

  const pipelineLabels = {
    forReview: "For Review",
    pending: "Pending",
    queued: "Queued",
    approved: "Approved"
  };
  let latestOverviewRequestId = 0;

  const numberFormatter = new Intl.NumberFormat("en-PH");
  const currencyFormatter = new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0
  });
  const longDateFormatter = new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric"
  });
  const monthLabelFormatter = new Intl.DateTimeFormat("en-US", {
    month: "short",
    year: "numeric"
  });

  const cloneDate = (value) => {
    const date = new Date(value);
    date.setHours(0, 0, 0, 0);
    return date;
  };

  const endOfMonth = (value) => {
    const date = cloneDate(value);
    return new Date(date.getFullYear(), date.getMonth() + 1, 0);
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

  const monthlyRecords = [
    { start: "2025-09-01", revenue: 1521000, grossProfit: 881100, netProfit: 463400, pipeline: { forReview: 3, pending: 2, queued: 1, approved: 4 } },
    { start: "2025-10-01", revenue: 1603800, grossProfit: 938000, netProfit: 501200, pipeline: { forReview: 3, pending: 2, queued: 1, approved: 5 } },
    { start: "2025-11-01", revenue: 1645100, grossProfit: 972300, netProfit: 519600, pipeline: { forReview: 4, pending: 2, queued: 1, approved: 5 } },
    { start: "2025-12-01", revenue: 1713900, grossProfit: 1024500, netProfit: 554900, pipeline: { forReview: 4, pending: 3, queued: 2, approved: 6 } },
    { start: "2026-01-01", revenue: 1788600, grossProfit: 1067900, netProfit: 596800, pipeline: { forReview: 5, pending: 3, queued: 2, approved: 7 } },
    { start: "2026-02-01", revenue: 1842300, grossProfit: 1096700, netProfit: 612400, pipeline: { forReview: 5, pending: 4, queued: 3, approved: 7 } },
    { start: "2026-03-01", revenue: 1906000, grossProfit: 1132400, netProfit: 648900, pipeline: { forReview: 6, pending: 4, queued: 3, approved: 8 }, projected: true }
  ].map((item) => {
    const startDate = cloneDate(item.start);
    return {
      ...item,
      startDate,
      endDate: endOfMonth(startDate),
      cogs: item.revenue - item.grossProfit,
      opEx: item.grossProfit - item.netProfit
    };
  });

  const datasetStart = monthlyRecords[0].startDate;
  const datasetEnd = monthlyRecords[monthlyRecords.length - 1].endDate;

  const state = {
    start: addDays(datasetEnd, -29),
    end: cloneDate(datasetEnd),
    mode: "monthly"
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
    monthlyRecords.filter((item) => item.endDate >= state.start && item.startDate <= state.end);

  const groupByYear = (records, key) => {
    const grouped = new Map();
    records.forEach((item) => {
      const year = item.startDate.getFullYear();
      grouped.set(year, (grouped.get(year) || 0) + item[key]);
    });

    const ordered = Array.from(grouped.entries()).sort((a, b) => a[0] - b[0]);
    return {
      labels: ordered.map(([year]) => String(year)),
      values: ordered.map(([, value]) => value)
    };
  };

  const buildSeries = (records, key) => {
    if (state.mode === "yearly") {
      return groupByYear(records, key);
    }

    const ordered = [...records].sort((a, b) => a.startDate - b.startDate);
    return {
      labels: ordered.map((item) => monthLabelFormatter.format(item.startDate)),
      values: ordered.map((item) => item[key])
    };
  };

  const summarizeRecords = (records) => {
    const totals = records.reduce(
      (accumulator, current) => {
        accumulator.revenue += current.revenue;
        accumulator.grossProfit += current.grossProfit;
        accumulator.netProfit += current.netProfit;
        accumulator.cogs += current.cogs;
        accumulator.opEx += current.opEx;
        accumulator.pipeline.forReview += current.pipeline.forReview;
        accumulator.pipeline.pending += current.pipeline.pending;
        accumulator.pipeline.queued += current.pipeline.queued;
        accumulator.pipeline.approved += current.pipeline.approved;
        return accumulator;
      },
      {
        revenue: 0,
        grossProfit: 0,
        netProfit: 0,
        cogs: 0,
        opEx: 0,
        pipeline: { forReview: 0, pending: 0, queued: 0, approved: 0 }
      }
    );

    const ordered = [...records].sort((a, b) => a.startDate - b.startDate);
    const first = ordered[0];
    const last = ordered[ordered.length - 1];

    const revenueChangePct = first && first.revenue > 0
      ? ((last.revenue - first.revenue) / first.revenue) * 100
      : 0;
    const grossMarginPct = totals.revenue > 0 ? (totals.grossProfit / totals.revenue) * 100 : 0;
    const netMarginPct = totals.revenue > 0 ? (totals.netProfit / totals.revenue) * 100 : 0;
    const projectedNext = monthlyRecords.find((item) => item.projected && item.startDate > state.end);
    const projectedNetProfit = projectedNext
      ? projectedNext.netProfit
      : Math.round((last ? last.netProfit : 0) * 1.06);

    return {
      monthCount: ordered.length,
      totalRevenue: totals.revenue,
      totalGrossProfit: totals.grossProfit,
      totalNetProfit: totals.netProfit,
      totalCogs: totals.cogs,
      totalOpEx: totals.opEx,
      grossMarginPct,
      netMarginPct,
      revenueChangePct,
      projectedNetProfit,
      pipeline: totals.pipeline
    };
  };

  const trendChart = new Chart(trendCanvas, {
    type: "line",
    data: {
      labels: [],
      datasets: [
        {
          label: "Revenue",
          data: [],
          borderColor: "#38bdf8",
          backgroundColor: "rgba(56, 189, 248, 0.2)",
          borderWidth: 2.4,
          pointRadius: 3,
          pointHoverRadius: 5,
          tension: 0.35
        },
        {
          label: "Net Profit",
          data: [],
          borderColor: "#84cc16",
          backgroundColor: "rgba(132, 204, 22, 0.2)",
          borderWidth: 2.4,
          pointRadius: 3,
          pointHoverRadius: 5,
          tension: 0.35
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          labels: {
            color: "#cbd5e1"
          }
        },
        tooltip: {
          callbacks: {
            label: (ctx) => `${ctx.dataset.label}: ${currencyFormatter.format(Number(ctx.parsed.y || 0))}`
          }
        }
      },
      scales: {
        x: {
          ticks: { color: "#cbd5e1" },
          grid: { color: "rgba(148, 163, 184, 0.25)" }
        },
        y: {
          ticks: {
            color: "#cbd5e1",
            callback: (value) => compactCurrency(Number(value))
          },
          grid: { color: "rgba(148, 163, 184, 0.25)" }
        }
      }
    }
  });

  const mixChart = new Chart(mixCanvas, {
    type: "doughnut",
    data: {
      labels: ["COGS", "Operating Expenses", "Net Profit"],
      datasets: [
        {
          data: [0, 0, 0],
          backgroundColor: ["#38bdf8", "#64748b", "#84cc16"],
          borderColor: "#0f172a",
          borderWidth: 1.5
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      cutout: "64%",
      plugins: {
        legend: {
          position: "bottom",
          labels: {
            color: "#cbd5e1",
            boxWidth: 10,
            usePointStyle: true,
            pointStyle: "circle"
          }
        },
        tooltip: {
          callbacks: {
            label: (ctx) => `${ctx.label}: ${currencyFormatter.format(Number(ctx.parsed || 0))}`
          }
        }
      }
    }
  });

  const pipelineChart = new Chart(pipelineCanvas, {
    type: "bar",
    data: {
      labels: ["For Review", "Pending", "Queued", "Approved"],
      datasets: [
        {
          label: "Requests",
          data: [0, 0, 0, 0],
          backgroundColor: ["#facc15", "#f59e0b", "#94a3b8", "#84cc16"],
          borderRadius: 8,
          maxBarThickness: 42,
          categoryPercentage: 0.62,
          barPercentage: 0.9
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => {
              const value = Number(context.parsed.y || 0);
              const dataset = context.dataset.data.map((item) => Number(item || 0));
              const total = dataset.reduce((sum, current) => sum + current, 0);
              const share = total > 0 ? (value / total) * 100 : 0;
              return `${context.label}: ${numberFormatter.format(value)} (${share.toFixed(1)}%)`;
            }
          }
        }
      },
      scales: {
        x: {
          ticks: { color: "#cbd5e1" },
          grid: { display: false }
        },
        y: {
          beginAtZero: true,
          ticks: {
            color: "#cbd5e1",
            precision: 0,
            stepSize: 5
          },
          grid: { color: "rgba(148, 163, 184, 0.25)" }
        }
      }
    }
  });

  const setKpiValue = (key, value) => {
    const element = kpiValueElements[key];
    if (element) {
      element.textContent = `PHP ${numberFormatter.format(Math.round(value))}`;
    }
  };

  const setKpiMeta = (key, text) => {
    const element = kpiMetaElements[key];
    if (element) {
      element.textContent = text;
    }
  };

  const setMixValue = (key, value) => {
    const element = mixValueElements[key];
    if (element) {
      element.textContent = `${value.toFixed(1)}%`;
    }
  };

  const setPipelineValue = (key, value) => {
    const element = pipelineValueElements[key];
    if (element) {
      element.textContent = numberFormatter.format(value);
    }
  };

  const setPipelineShare = (key, value) => {
    const element = pipelineShareElements[key];
    if (element) {
      element.textContent = `${value.toFixed(0)}%`;
    }
  };

  const toNumber = (value) => {
    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric : 0;
  };

  const applyLiveOverview = (overview) => {
    if (!overview) {
      return;
    }

    const totalRevenue = toNumber(overview.totalRevenue);
    const payMongoRevenue = toNumber(overview.payMongoRevenue);
    const operatingExpenses = toNumber(overview.operatingExpenses);
    const totalCosts = toNumber(overview.totalCosts);
    const equipmentMonthlyDepreciation = toNumber(overview.equipmentMonthlyDepreciation);
    const estimatedNetProfit = toNumber(overview.estimatedNetProfit);
    const equipmentUnits = Math.round(toNumber(overview.equipmentTotalUnits));
    const equipmentInvestment = toNumber(overview.equipmentTotalInvestment);
    const successfulPayments = Math.round(toNumber(overview.successfulPaymentsCount));
    const equipmentPaybackPercent = toNumber(overview.equipmentPaybackPercent);

    setKpiValue("grossRevenue", totalRevenue);
    setKpiValue("grossProfit", totalRevenue - operatingExpenses);
    setKpiValue("netProfit", estimatedNetProfit);
    setKpiValue("projectedNetProfit", Math.round(estimatedNetProfit * 1.05));

    setKpiMeta("grossRevenue", `${successfulPayments} successful payment(s) in selected range`);
    setKpiMeta("grossProfit", `Operating expenses in range: ${currencyFormatter.format(operatingExpenses)}`);
    setKpiMeta("netProfit", `Total costs incl. depreciation: ${currencyFormatter.format(totalCosts || (operatingExpenses + equipmentMonthlyDepreciation))}`);
    setKpiMeta(
      "projectedNetProfit",
      equipmentInvestment > 0
        ? `PayMongo revenue ${currencyFormatter.format(payMongoRevenue)} • equipment payback ${equipmentPaybackPercent.toFixed(1)}%`
        : `PayMongo revenue ${currencyFormatter.format(payMongoRevenue)} • no equipment assets yet`
    );
  };

  const loadLiveOverview = async () => {
    const requestId = ++latestOverviewRequestId;

    const fromUtc = cloneDate(state.start);
    const toUtc = cloneDate(state.end);
    toUtc.setHours(23, 59, 59, 999);

    const query = new URLSearchParams({
      fromUtc: fromUtc.toISOString(),
      toUtc: toUtc.toISOString()
    });

    try {
      const response = await fetch(`/api/finance/overview?${query.toString()}`, {
        headers: { Accept: "application/json" }
      });

      if (!response.ok || requestId !== latestOverviewRequestId) {
        return;
      }

      const overview = await response.json();
      if (requestId !== latestOverviewRequestId) {
        return;
      }

      applyLiveOverview(overview);
    } catch (error) {
      console.error("Failed to load live finance overview.", error);
    }
  };

  const renderDashboard = () => {
    const records = filterRecords();
    if (!records.length) {
      return;
    }

    const summary = summarizeRecords(records);
    const revenueSeries = buildSeries(records, "revenue");
    const netProfitSeries = buildSeries(records, "netProfit");

    trendChart.data.labels = revenueSeries.labels;
    trendChart.data.datasets[0].data = revenueSeries.values;
    trendChart.data.datasets[1].data = netProfitSeries.values;
    trendChart.update();

    mixChart.data.datasets[0].data = [summary.totalCogs, summary.totalOpEx, summary.totalNetProfit];
    mixChart.update();

    pipelineChart.data.datasets[0].data = [
      summary.pipeline.forReview,
      summary.pipeline.pending,
      summary.pipeline.queued,
      summary.pipeline.approved
    ];
    pipelineChart.update();

    const trendModeText = state.mode === "yearly" ? "Year to year" : "Month to month";
    const rangeText = `${longDateFormatter.format(state.start)} - ${longDateFormatter.format(state.end)}`;

    if (headerState) {
      headerState.textContent = `${rangeText} • ${trendModeText}`;
    }

    if (trendSubtitle) {
      trendSubtitle.textContent = `${rangeText} • ${trendModeText}`;
    }
    if (trendSummary) {
      trendSummary.textContent = `Gross revenue ${currencyFormatter.format(summary.totalRevenue)} and net profit ${currencyFormatter.format(summary.totalNetProfit)} for ${summary.monthCount} month(s) in range.`;
    }
    if (mixSubtitle) {
      mixSubtitle.textContent = `${summary.monthCount} month selection`;
    }
    const totalPipelineRequests =
      summary.pipeline.forReview +
      summary.pipeline.pending +
      summary.pipeline.queued +
      summary.pipeline.approved;

    Object.entries(summary.pipeline).forEach(([key, value]) => {
      const share = totalPipelineRequests > 0 ? (value / totalPipelineRequests) * 100 : 0;
      setPipelineValue(key, value);
      setPipelineShare(key, share);
    });

    if (pipelineSubtitle) {
      pipelineSubtitle.textContent = `${summary.monthCount} month queue totals • ${numberFormatter.format(totalPipelineRequests)} requests`;
    }
    if (pipelineNote) {
      const dominantEntry = Object.entries(summary.pipeline).sort((a, b) => b[1] - a[1])[0];
      const dominantKey = dominantEntry ? dominantEntry[0] : "forReview";
      const dominantValue = dominantEntry ? dominantEntry[1] : 0;
      const dominantShare = totalPipelineRequests > 0 ? (dominantValue / totalPipelineRequests) * 100 : 0;
      const dominantLabel = pipelineLabels[dominantKey] || "Queue";
      pipelineNote.textContent = `${dominantLabel} leads with ${numberFormatter.format(dominantValue)} request(s) (${dominantShare.toFixed(0)}%). Detailed entries remain in the Fund Requests module.`;
    }

    setKpiValue("grossRevenue", summary.totalRevenue);
    setKpiValue("grossProfit", summary.totalGrossProfit);
    setKpiValue("netProfit", summary.totalNetProfit);
    setKpiValue("projectedNetProfit", summary.projectedNetProfit);

    setKpiMeta(
      "grossRevenue",
      `${summary.revenueChangePct >= 0 ? "+" : ""}${summary.revenueChangePct.toFixed(1)}% vs first period in range`
    );
    setKpiMeta("grossProfit", `Gross margin ${summary.grossMarginPct.toFixed(1)}%`);
    setKpiMeta("netProfit", `Net margin ${summary.netMarginPct.toFixed(1)}%`);
    setKpiMeta("projectedNetProfit", "Forecast based on current run-rate");

    const revenueMeta = kpiMetaElements.grossRevenue;
    if (revenueMeta) {
      revenueMeta.classList.toggle("text-success", summary.revenueChangePct >= 0);
      revenueMeta.classList.toggle("text-danger", summary.revenueChangePct < 0);
    }

    setMixValue("cogsShare", summary.totalRevenue > 0 ? (summary.totalCogs / summary.totalRevenue) * 100 : 0);
    setMixValue("opexShare", summary.totalRevenue > 0 ? (summary.totalOpEx / summary.totalRevenue) * 100 : 0);
    setMixValue("netMargin", summary.netMarginPct);

    void loadLiveOverview();
  };

  const setQuickButtonsState = (activeValue) => {
    quickRangeButtons.forEach((button) => {
      button.classList.toggle("active", button.dataset.finFilterQuick === activeValue);
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
    const inputStart = parseInputDate(startInput ? startInput.value : "") || addDays(datasetEnd, -29);
    const inputEnd = parseInputDate(endInput ? endInput.value : "") || cloneDate(datasetEnd);
    const mode = trendModeSelect && trendModeSelect.value === "yearly" ? "yearly" : "monthly";

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
      case "today":
        break;
      case "7":
        start = addDays(end, -6);
        break;
      case "30":
        start = addDays(end, -29);
        break;
      case "ytd":
        start = new Date(end.getFullYear(), 0, 1);
        break;
      case "365":
        start = addDays(end, -364);
        break;
      case "730":
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
    button.addEventListener("click", () => {
      setQuickRange(button.dataset.finFilterQuick || "30");
    });
  });

  if (applyButton) {
    applyButton.addEventListener("click", () => {
      applyControlsToState();
      setQuickButtonsState("");
      renderDashboard();
    });
  }

  if (resetButton) {
    resetButton.addEventListener("click", () => {
      state.mode = "monthly";
      setQuickRange("30");
    });
  }

  if (startInput) {
    startInput.addEventListener("change", () => {
      applyControlsToState();
      setQuickButtonsState("");
      renderDashboard();
    });
  }

  if (endInput) {
    endInput.addEventListener("change", () => {
      applyControlsToState();
      setQuickButtonsState("");
      renderDashboard();
    });
  }

  if (trendModeSelect) {
    trendModeSelect.addEventListener("change", () => {
      applyControlsToState();
      renderDashboard();
    });
  }

  window.addEventListener("ejc:erp-event", (event) => {
    const eventType = event && event.detail ? event.detail.eventType : "";
    if (!eventType) {
      return;
    }

    if (eventType === "payment.succeeded" || eventType === "membership.activated") {
      void loadLiveOverview();
    }
  });

  syncInputs();
  setQuickButtonsState("30");
  renderDashboard();
})();
