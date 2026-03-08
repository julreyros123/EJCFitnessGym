(() => {
  const dashboard = document.querySelector("[data-finance-dashboard]");
  if (!dashboard) {
    return;
  }

  const trendCanvas = dashboard.querySelector("#financeTrendChart");
  const mixCanvas = dashboard.querySelector("#financeMixChart");
  const pipelineCanvas = dashboard.querySelector("#financePipelineChart");
  const chartsEnabled =
    typeof Chart !== "undefined" &&
    Boolean(trendCanvas) &&
    Boolean(mixCanvas) &&
    Boolean(pipelineCanvas);

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
  const noDataAlert = dashboard.querySelector("[data-fin-no-data-alert]");
  const tryLast30dButton = dashboard.querySelector("[data-fin-try-last-30d]");
  const tryYtdButton = dashboard.querySelector("[data-fin-try-ytd]");

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

  const aiValueElements = {
    branchId: dashboard.querySelector('[data-fin-ai-value="branchId"]'),
    generatedAt: dashboard.querySelector('[data-fin-ai-value="generatedAt"]'),
    highRisk: dashboard.querySelector('[data-fin-ai-value="highRisk"]'),
    mediumRisk: dashboard.querySelector('[data-fin-ai-value="mediumRisk"]'),
    overdueMembers: dashboard.querySelector('[data-fin-ai-value="overdueMembers"]'),
    exposure: dashboard.querySelector('[data-fin-ai-value="exposure"]'),
    scopedMembers: dashboard.querySelector('[data-fin-ai-value="scopedMembers"]'),
    renewalsDue: dashboard.querySelector('[data-fin-ai-value="renewalsDue"]'),
    openInvoices: dashboard.querySelector('[data-fin-ai-value="openInvoices"]')
  };

  const aiPriorityBody = dashboard.querySelector("[data-fin-ai-priority-body]");
  const aiEmptyState = dashboard.querySelector("[data-fin-ai-empty]");
  const aiTableWrap = dashboard.querySelector("[data-fin-ai-table-wrap]");
  const hasAiWidgets =
    Object.values(aiValueElements).some(Boolean) ||
    Boolean(aiPriorityBody) ||
    Boolean(aiEmptyState) ||
    Boolean(aiTableWrap);

  const pipelineLabels = {
    forReview: "For Review",
    pending: "Pending",
    queued: "Queued",
    approved: "Approved"
  };
  let latestOverviewRequestId = 0;
  let latestAiOverviewRequestId = 0;

  const numberFormatter = new Intl.NumberFormat("en-PH");
  const currencyFormatter = new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0
  });
  const compactNumberFormatter = new Intl.NumberFormat("en-US", {
    notation: "compact",
    maximumFractionDigits: 1
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

  const getThemeToken = (token, fallback) => {
    const value = window.getComputedStyle(document.body).getPropertyValue(token).trim();
    return value || fallback;
  };

  const getChartThemeTokens = () => ({
    axisLabel: getThemeToken("--ejc-chart-axis", "#94a3b8"),
    text: getThemeToken("--ejc-chart-text", "#cbd5f5"),
    line: getThemeToken("--ejc-chart-line", "#84cc16"),
    lineHover: getThemeToken("--ejc-chart-line-hover", "#a3e635"),
    lineSecondary: getThemeToken("--ejc-chart-line-secondary", "#38bdf8"),
    neutral: getThemeToken("--ejc-chart-neutral", "#64748b"),
    grid: getThemeToken("--ejc-chart-grid", "rgba(148, 163, 184, 0.15)"),
    axisLine: getThemeToken("--ejc-chart-axis-line", "rgba(148, 163, 184, 0.25)"),
    tooltipBg: getThemeToken("--ejc-chart-tooltip-bg", "#0f172a"),
    tooltipText: getThemeToken("--ejc-chart-tooltip-text", "#e2e8f0")
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
    return `PHP ${compactNumberFormatter.format(value).toUpperCase()}`;
  };

  let monthlyRecords = [
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

  let datasetStart = monthlyRecords[0].startDate;
  let datasetEnd = monthlyRecords[monthlyRecords.length - 1].endDate;

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

  const initialChartTheme = getChartThemeTokens();
  let trendChart = null;
  let mixChart = null;
  let pipelineChart = null;

  if (chartsEnabled) {
    trendChart = new Chart(trendCanvas, {
      type: "line",
      data: {
        labels: [],
        datasets: [
          {
            label: "Revenue",
            data: [],
            borderColor: initialChartTheme.lineSecondary,
            pointBackgroundColor: initialChartTheme.lineSecondary,
            pointBorderColor: initialChartTheme.lineSecondary,
            pointHoverBackgroundColor: initialChartTheme.lineSecondary,
            pointHoverBorderColor: initialChartTheme.lineSecondary,
            borderWidth: 2.4,
            pointRadius: 3,
            pointHoverRadius: 5,
            tension: 0.35
          },
          {
            label: "Net Profit",
            data: [],
            borderColor: initialChartTheme.line,
            pointBackgroundColor: initialChartTheme.line,
            pointBorderColor: initialChartTheme.line,
            pointHoverBackgroundColor: initialChartTheme.lineHover,
            pointHoverBorderColor: initialChartTheme.lineHover,
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
              color: initialChartTheme.text
            }
          },
          tooltip: {
            backgroundColor: initialChartTheme.tooltipBg,
            titleColor: initialChartTheme.tooltipText,
            bodyColor: initialChartTheme.tooltipText,
            borderColor: initialChartTheme.axisLine,
            borderWidth: 1,
            callbacks: {
              label: (ctx) => `${ctx.dataset.label}: ${currencyFormatter.format(Number(ctx.parsed.y || 0))}`
            }
          }
        },
        scales: {
          x: {
            ticks: { color: initialChartTheme.axisLabel },
            grid: { color: initialChartTheme.grid },
            border: { color: initialChartTheme.axisLine }
          },
          y: {
            ticks: {
              color: initialChartTheme.axisLabel,
              callback: (value) => compactCurrency(Number(value))
            },
            grid: { color: initialChartTheme.grid },
            border: { color: initialChartTheme.axisLine }
          }
        }
      }
    });

    mixChart = new Chart(mixCanvas, {
      type: "doughnut",
      data: {
        labels: ["COGS", "Operating Expenses", "Net Profit"],
        datasets: [
          {
            data: [0, 0, 0],
            backgroundColor: [initialChartTheme.lineSecondary, initialChartTheme.neutral, initialChartTheme.line],
            borderColor: initialChartTheme.axisLine,
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
              color: initialChartTheme.text,
              boxWidth: 10,
              usePointStyle: true,
              pointStyle: "circle"
            }
          },
          tooltip: {
            backgroundColor: initialChartTheme.tooltipBg,
            titleColor: initialChartTheme.tooltipText,
            bodyColor: initialChartTheme.tooltipText,
            borderColor: initialChartTheme.axisLine,
            borderWidth: 1,
            callbacks: {
              label: (ctx) => `${ctx.label}: ${currencyFormatter.format(Number(ctx.parsed || 0))}`
            }
          }
        }
      }
    });

    pipelineChart = new Chart(pipelineCanvas, {
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
            ticks: { color: initialChartTheme.axisLabel },
            grid: { display: false },
            border: { color: initialChartTheme.axisLine }
          },
          y: {
            beginAtZero: true,
            ticks: {
              color: initialChartTheme.axisLabel,
              precision: 0,
              stepSize: 5
            },
            grid: { color: initialChartTheme.grid },
            border: { color: initialChartTheme.axisLine }
          }
        }
      }
    });
  }

  const applyChartTheme = () => {
    if (!trendChart || !mixChart || !pipelineChart) {
      return;
    }

    const palette = getChartThemeTokens();

    const trendRevenue = trendChart.data.datasets[0];
    trendRevenue.borderColor = palette.lineSecondary;
    trendRevenue.pointBackgroundColor = palette.lineSecondary;
    trendRevenue.pointBorderColor = palette.lineSecondary;
    trendRevenue.pointHoverBackgroundColor = palette.lineSecondary;
    trendRevenue.pointHoverBorderColor = palette.lineSecondary;

    const trendNet = trendChart.data.datasets[1];
    trendNet.borderColor = palette.line;
    trendNet.pointBackgroundColor = palette.line;
    trendNet.pointBorderColor = palette.line;
    trendNet.pointHoverBackgroundColor = palette.lineHover;
    trendNet.pointHoverBorderColor = palette.lineHover;

    trendChart.options.plugins.legend.labels.color = palette.text;
    trendChart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
    trendChart.options.plugins.tooltip.titleColor = palette.tooltipText;
    trendChart.options.plugins.tooltip.bodyColor = palette.tooltipText;
    trendChart.options.plugins.tooltip.borderColor = palette.axisLine;
    trendChart.options.scales.x.ticks.color = palette.axisLabel;
    trendChart.options.scales.x.grid.color = palette.grid;
    trendChart.options.scales.x.border.color = palette.axisLine;
    trendChart.options.scales.y.ticks.color = palette.axisLabel;
    trendChart.options.scales.y.grid.color = palette.grid;
    trendChart.options.scales.y.border.color = palette.axisLine;

    mixChart.data.datasets[0].backgroundColor = [palette.lineSecondary, palette.neutral, palette.line];
    mixChart.data.datasets[0].borderColor = palette.axisLine;
    mixChart.options.plugins.legend.labels.color = palette.text;
    mixChart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
    mixChart.options.plugins.tooltip.titleColor = palette.tooltipText;
    mixChart.options.plugins.tooltip.bodyColor = palette.tooltipText;
    mixChart.options.plugins.tooltip.borderColor = palette.axisLine;

    pipelineChart.options.plugins.tooltip.backgroundColor = palette.tooltipBg;
    pipelineChart.options.plugins.tooltip.titleColor = palette.tooltipText;
    pipelineChart.options.plugins.tooltip.bodyColor = palette.tooltipText;
    pipelineChart.options.plugins.tooltip.borderColor = palette.axisLine;
    pipelineChart.options.scales.x.ticks.color = palette.axisLabel;
    pipelineChart.options.scales.x.border.color = palette.axisLine;
    pipelineChart.options.scales.y.ticks.color = palette.axisLabel;
    pipelineChart.options.scales.y.grid.color = palette.grid;
    pipelineChart.options.scales.y.border.color = palette.axisLine;

    trendChart.update("none");
    mixChart.update("none");
    pipelineChart.update("none");
  };

  if (typeof MutationObserver === "function") {
    const themeObserver = new MutationObserver((mutations) => {
      const changedTheme = mutations.some(
        (mutation) => mutation.type === "attributes" && mutation.attributeName === "data-ejc-theme"
      );
      if (changedTheme) {
        applyChartTheme();
      }
    });

    themeObserver.observe(document.body, {
      attributes: true,
      attributeFilter: ["data-ejc-theme"]
    });
  }

  applyChartTheme();

  const setKpiValue = (key, value) => {
    const element = kpiValueElements[key];
    if (element) {
      element.innerHTML = compactCurrency(value);
      element.classList.remove('ejc-kpi-loading');
    }
  };

  const setKpiMeta = (key, text) => {
    const element = kpiMetaElements[key];
    if (element) {
      element.textContent = text;
      element.classList.remove('ejc-kpi-loading');
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

  const setAiValue = (key, value) => {
    const element = aiValueElements[key];
    if (element) {
      element.textContent = value;
    }
  };

  const churnBadgeClass = (level) => {
    if (level && level.toLowerCase() === "high") {
      return "bg-danger";
    }
    if (level && level.toLowerCase() === "medium") {
      return "bg-warning text-dark";
    }
    return "bg-success";
  };

  const renderAiPriorityRows = (members) => {
    if (!aiPriorityBody || !aiEmptyState || !aiTableWrap) {
      return;
    }

    aiPriorityBody.innerHTML = "";
    if (!Array.isArray(members) || members.length === 0) {
      aiEmptyState.classList.remove("d-none");
      aiTableWrap.classList.add("d-none");
      return;
    }

    aiEmptyState.classList.add("d-none");
    aiTableWrap.classList.remove("d-none");

    members.forEach((member) => {
      const row = document.createElement("tr");

      const memberCell = document.createElement("td");
      memberCell.textContent = member.memberEmail || "-";
      row.appendChild(memberCell);

      const riskCell = document.createElement("td");
      const badge = document.createElement("span");
      badge.className = `badge ${churnBadgeClass(member.riskLevel)}`;
      badge.textContent = member.riskLevel || "Low";
      riskCell.appendChild(badge);
      const score = document.createElement("div");
      score.className = "small text-muted";
      score.textContent = `Score ${toNumber(member.riskScore)}`;
      riskCell.appendChild(score);
      row.appendChild(riskCell);

      const issueCell = document.createElement("td");
      issueCell.textContent = member.reasonSummary || "-";

      if (toNumber(member.overdueInvoiceCount) > 0) {
        const overdueCount = document.createElement("div");
        overdueCount.className = "small text-muted";
        overdueCount.textContent = `${Math.round(toNumber(member.overdueInvoiceCount))} expired invoice(s)`;
        issueCell.appendChild(overdueCount);
      }

      if (toNumber(member.overdueAmount) > 0) {
        const overdueAmount = document.createElement("div");
        overdueAmount.className = "small text-muted";
        overdueAmount.textContent = `PHP ${toNumber(member.overdueAmount).toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
        issueCell.appendChild(overdueAmount);
      }
      row.appendChild(issueCell);

      const nextStepCell = document.createElement("td");
      nextStepCell.textContent = member.suggestedAction || "-";
      row.appendChild(nextStepCell);

      const actionCell = document.createElement("td");
      actionCell.className = "text-end";
      if (member.memberUserId) {
        const actionLink = document.createElement("a");
        actionLink.className = "btn btn-sm btn-outline-primary";
        actionLink.href = `/Admin/MemberAccounts/Details/${encodeURIComponent(member.memberUserId)}`;
        actionLink.textContent = "Open";
        actionCell.appendChild(actionLink);
      } else {
        actionCell.textContent = "-";
      }
      row.appendChild(actionCell);

      aiPriorityBody.appendChild(row);
    });
  };

  const applyAiOverview = (overview) => {
    if (!overview) {
      return;
    }

    setAiValue("branchId", overview.branchId || "Unscoped");

    const generatedAt = overview.generatedAtUtc ? new Date(overview.generatedAtUtc) : new Date();
    setAiValue(
      "generatedAt",
      Number.isNaN(generatedAt.getTime())
        ? "-"
        : `${generatedAt.getFullYear()}-${String(generatedAt.getMonth() + 1).padStart(2, "0")}-${String(generatedAt.getDate()).padStart(2, "0")} ${String(generatedAt.getHours()).padStart(2, "0")}:${String(generatedAt.getMinutes()).padStart(2, "0")}`
    );

    setAiValue("highRisk", numberFormatter.format(toNumber(overview.highRiskCount)));
    setAiValue("mediumRisk", numberFormatter.format(toNumber(overview.mediumRiskCount)));
    setAiValue("overdueMembers", numberFormatter.format(toNumber(overview.overdueMemberCount)));
    setAiValue("exposure", `PHP ${toNumber(overview.openInvoiceExposureAmount).toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`);
    setAiValue("scopedMembers", numberFormatter.format(toNumber(overview.scopedMemberCount)));
    setAiValue("renewalsDue", numberFormatter.format(toNumber(overview.renewalsDueIn30DaysCount)));
    setAiValue("openInvoices", numberFormatter.format(toNumber(overview.openInvoiceCount)));

    renderAiPriorityRows(overview.priorityMembers || []);
  };

  const toNumber = (value) => {
    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric : 0;
  };

  const normalizeMonthlySnapshot = (item) => {
    const startDateValue = item.monthStartUtc || item.monthStart || item.start;
    if (!startDateValue) {
      return null;
    }

    const startDate = cloneDate(startDateValue);
    const revenue = toNumber(item.revenue);
    const grossProfit = toNumber(item.grossProfit);
    const netProfit = toNumber(item.netProfit);
    const cogsValue = item.costOfServices ?? item.cogs;
    const opExValue = item.operatingExpenses ?? item.opEx;

    return {
      start: toInputDate(startDate),
      startDate,
      endDate: endOfMonth(startDate),
      revenue,
      grossProfit,
      netProfit,
      cogs: toNumber(cogsValue ?? (revenue - grossProfit)),
      opEx: toNumber(opExValue ?? (grossProfit - netProfit)),
      projected: Boolean(item.isProjected || item.projected),
      pipeline: {
        forReview: Math.round(toNumber(item.forReviewCount ?? item.pipeline?.forReview)),
        pending: Math.round(toNumber(item.pendingCount ?? item.pipeline?.pending)),
        queued: Math.round(toNumber(item.queuedCount ?? item.pipeline?.queued)),
        approved: Math.round(toNumber(item.approvedCount ?? item.pipeline?.approved))
      }
    };
  };

  const applyMonthlyRecords = (records) => {
    if (!Array.isArray(records) || records.length === 0) {
      return;
    }

    monthlyRecords = [...records].sort((a, b) => a.startDate - b.startDate);
    datasetStart = monthlyRecords[0].startDate;
    datasetEnd = monthlyRecords[monthlyRecords.length - 1].endDate;

    const shouldResetRange =
      state.start < datasetStart ||
      state.end > datasetEnd ||
      state.start > state.end;

    if (shouldResetRange) {
      state.start = addDays(datasetEnd, -29);
      state.end = cloneDate(datasetEnd);
      setQuickButtonsState("30");
    }

    syncInputs();
    renderDashboard();
  };

  const loadMonthlySeries = async () => {
    try {
      const query = new URLSearchParams({
        months: "18",
        includeProjection: "true"
      });

      const response = await fetch(`/api/finance/monthly?${query.toString()}`, {
        headers: { Accept: "application/json" }
      });
      if (!response.ok) {
        return;
      }

      const payload = await response.json();
      if (!Array.isArray(payload) || payload.length === 0) {
        return;
      }

      const normalized = payload
        .map((item) => normalizeMonthlySnapshot(item))
        .filter((item) => Boolean(item));
      if (!normalized.length) {
        return;
      }

      applyMonthlyRecords(normalized);
    } catch (error) {
      console.error("Failed to load monthly finance snapshots.", error);
    }
  };

  const applyLiveOverview = (overview) => {
    if (!overview) {
      setKpiValue("grossRevenue", 0);
      setKpiValue("grossProfit", 0);
      setKpiValue("netProfit", 0);
      setKpiValue("projectedNetProfit", 0);
      setKpiMeta("grossRevenue", "No data available for selected range");
      setKpiMeta("grossProfit", "No data available");
      setKpiMeta("netProfit", "No data available");
      setKpiMeta("projectedNetProfit", "No data available");
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

    const grossProfit = totalRevenue - operatingExpenses;
    const projectedNet = Math.round(estimatedNetProfit * 1.05);

    // Check if we have any actual data
    const hasData = totalRevenue > 0 || operatingExpenses > 0 || equipmentInvestment > 0;

    if (!hasData) {
      setKpiValue("grossRevenue", 0);
      setKpiValue("grossProfit", 0);
      setKpiValue("netProfit", 0);
      setKpiValue("projectedNetProfit", 0);
      setKpiMeta("grossRevenue", "No payments recorded in this date range");
      setKpiMeta("grossProfit", "No expenses recorded in this date range");
      setKpiMeta("netProfit", "No financial activity in this date range");
      setKpiMeta("projectedNetProfit", "Insufficient data for forecast");
      
      // Show no data alert
      if (noDataAlert) {
        noDataAlert.classList.remove("d-none");
      }
      return;
    }

    // Hide no data alert if we have data
    if (noDataAlert) {
      noDataAlert.classList.add("d-none");
    }

    setKpiValue("grossRevenue", totalRevenue);
    setKpiValue("grossProfit", grossProfit);
    setKpiValue("netProfit", estimatedNetProfit);
    setKpiValue("projectedNetProfit", projectedNet);

    const revenueMetaText = successfulPayments > 0 
      ? `${successfulPayments} successful payment(s) in selected range`
      : "No payments recorded in selected range";
    
    const profitMetaText = operatingExpenses > 0
      ? `Operating expenses: ${currencyFormatter.format(operatingExpenses)}`
      : "No operating expenses recorded";
    
    const netMetaText = totalCosts > 0
      ? `Total costs incl. depreciation: ${currencyFormatter.format(totalCosts)}`
      : `Costs: ${currencyFormatter.format(operatingExpenses + equipmentMonthlyDepreciation)}`;
    
    const projectedMetaText = equipmentInvestment > 0
      ? `PayMongo: ${currencyFormatter.format(payMongoRevenue)} • Equipment payback: ${equipmentPaybackPercent.toFixed(1)}%`
      : payMongoRevenue > 0
        ? `PayMongo revenue: ${currencyFormatter.format(payMongoRevenue)}`
        : "Forecast based on current run-rate";

    setKpiMeta("grossRevenue", revenueMetaText);
    setKpiMeta("grossProfit", profitMetaText);
    setKpiMeta("netProfit", netMetaText);
    setKpiMeta("projectedNetProfit", projectedMetaText);
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

      if (requestId !== latestOverviewRequestId) {
        return;
      }

      if (!response.ok) {
        console.warn(`Finance overview API returned ${response.status}`);
        applyLiveOverview(null);
        return;
      }

      const overview = await response.json();
      if (requestId !== latestOverviewRequestId) {
        return;
      }

      applyLiveOverview(overview);
    } catch (error) {
      console.error("Failed to load live finance overview.", error);
      if (requestId === latestOverviewRequestId) {
        applyLiveOverview(null);
      }
    }
  };

  const loadAiOverview = async () => {
    const requestId = ++latestAiOverviewRequestId;

    const fromUtc = cloneDate(state.start);
    const toUtc = cloneDate(state.end);
    toUtc.setHours(23, 59, 59, 999);

    const query = new URLSearchParams({
      fromUtc: fromUtc.toISOString(),
      toUtc: toUtc.toISOString()
    });

    try {
      const response = await fetch(`/api/finance/ai-overview?${query.toString()}`, {
        headers: { Accept: "application/json" }
      });

      if (!response.ok || requestId !== latestAiOverviewRequestId) {
        return;
      }

      const overview = await response.json();
      if (requestId !== latestAiOverviewRequestId) {
        return;
      }

      applyAiOverview(overview);
    } catch (error) {
      console.error("Failed to load finance AI overview.", error);
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

    if (trendChart) {
      trendChart.data.labels = revenueSeries.labels;
      trendChart.data.datasets[0].data = revenueSeries.values;
      trendChart.data.datasets[1].data = netProfitSeries.values;
      trendChart.update();
    }

    if (mixChart) {
      mixChart.data.datasets[0].data = [summary.totalCogs, summary.totalOpEx, summary.totalNetProfit];
      mixChart.update();
    }

    if (pipelineChart) {
      pipelineChart.data.datasets[0].data = [
        summary.pipeline.forReview,
        summary.pipeline.pending,
        summary.pipeline.queued,
        summary.pipeline.approved
      ];
      pipelineChart.update();
    }

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
    if (hasAiWidgets) {
      void loadAiOverview();
    }
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
      void loadMonthlySeries();
      void loadLiveOverview();
      if (hasAiWidgets) {
        void loadAiOverview();
      }
    }
  });

  // Quick action buttons in no-data alert
  if (tryLast30dButton) {
    tryLast30dButton.addEventListener("click", () => {
      setQuickRange("30");
      if (noDataAlert) {
        noDataAlert.classList.add("d-none");
      }
    });
  }

  if (tryYtdButton) {
    tryYtdButton.addEventListener("click", () => {
      setQuickRange("ytd");
      if (noDataAlert) {
        noDataAlert.classList.add("d-none");
      }
    });
  }

  syncInputs();
  setQuickButtonsState("30");
  renderDashboard();
  void loadMonthlySeries();
})();
