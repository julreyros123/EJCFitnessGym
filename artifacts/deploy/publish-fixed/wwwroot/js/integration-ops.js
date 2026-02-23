(() => {
  const root = document.querySelector("[data-integration-ops]");
  if (!root) {
    return;
  }

  const outboxBody = root.querySelector("[data-outbox-body]");
  const outboxStatus = root.querySelector("[data-outbox-status]");
  const outboxTake = root.querySelector("[data-outbox-take]");
  const outboxRefreshButtons = Array.from(root.querySelectorAll("[data-outbox-refresh]"));
  const outboxRetryFailedButton = root.querySelector("[data-outbox-retry-failed]");

  const receiptsBody = root.querySelector("[data-receipts-body]");
  const receiptsStatus = root.querySelector("[data-receipts-status]");
  const receiptsReference = root.querySelector("[data-receipts-reference]");
  const receiptsTake = root.querySelector("[data-receipts-take]");
  const receiptsRefreshButton = root.querySelector("[data-receipts-refresh]");

  const replayForm = root.querySelector("[data-replay-form]");
  const replayEventKey = root.querySelector("[data-replay-event-key]");
  const replayReference = root.querySelector("[data-replay-reference]");
  const replayForce = root.querySelector("[data-replay-force]");
  const replaySubmit = root.querySelector("[data-replay-submit]");
  const replayClear = root.querySelector("[data-replay-clear]");

  const alertBox = root.querySelector("[data-ops-alert]");

  const formatUtc = (value) => {
    if (!value) {
      return "-";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return "-";
    }

    return date.toISOString().replace("T", " ").slice(0, 19);
  };

  const escapeHtml = (value) => {
    return String(value ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  };

  const toInt = (value, fallback) => {
    const parsed = Number.parseInt(value, 10);
    if (!Number.isFinite(parsed)) {
      return fallback;
    }

    return parsed;
  };

  const showAlert = (type, message) => {
    if (!alertBox) {
      return;
    }

    alertBox.classList.remove("d-none", "alert-success", "alert-danger", "alert-info", "alert-warning");
    alertBox.classList.add(`alert-${type}`);
    alertBox.textContent = message;
  };

  const clearAlert = () => {
    if (!alertBox) {
      return;
    }

    alertBox.classList.add("d-none");
    alertBox.textContent = "";
  };

  const fetchJson = async (url, options = {}) => {
    const requestOptions = {
      ...options,
      headers: {
        Accept: "application/json",
        ...(options.headers || {})
      }
    };

    const response = await fetch(url, requestOptions);
    const text = await response.text();
    let payload = null;

    if (text) {
      try {
        payload = JSON.parse(text);
      } catch {
        payload = null;
      }
    }

    if (!response.ok) {
      const message = payload && typeof payload.message === "string"
        ? payload.message
        : `Request failed (${response.status}).`;
      throw new Error(message);
    }

    return payload;
  };

  const createOutboxRow = (item) => {
    const id = escapeHtml(item.id);
    const status = escapeHtml(item.status ?? "-");
    const target = escapeHtml(item.target ?? "-");
    const targetValue = item.targetValue ? ` (${escapeHtml(item.targetValue)})` : "";
    const eventType = escapeHtml(item.eventType ?? "-");
    const message = escapeHtml(item.message ?? "-");
    const attemptCount = escapeHtml(item.attemptCount ?? 0);
    const nextAttemptUtc = escapeHtml(formatUtc(item.nextAttemptUtc));
    const lastError = escapeHtml(item.lastError ?? "-");
    const lastErrorTitle = escapeHtml(item.lastError ?? "");

    const row = document.createElement("tr");
    row.innerHTML = `
      <td class="text-nowrap">${id}</td>
      <td class="text-nowrap">${status}</td>
      <td class="text-nowrap">${target}${targetValue}</td>
      <td class="text-nowrap">${eventType}</td>
      <td>${message}</td>
      <td class="text-nowrap">${attemptCount}</td>
      <td class="text-nowrap">${nextAttemptUtc}</td>
      <td class="text-truncate" style="max-width: 260px;" title="${lastErrorTitle}">${lastError}</td>
      <td class="text-end text-nowrap"></td>
    `;

    const actionCell = row.querySelector("td:last-child");
    if (!actionCell) {
      return row;
    }

    if (item.status !== "Processed") {
      const retryBtn = document.createElement("button");
      retryBtn.type = "button";
      retryBtn.className = "btn btn-sm btn-outline-primary me-1";
      retryBtn.textContent = "Retry";
      retryBtn.dataset.action = "outbox-retry";
      retryBtn.dataset.id = String(item.id);
      actionCell.appendChild(retryBtn);

      const deadLetterBtn = document.createElement("button");
      deadLetterBtn.type = "button";
      deadLetterBtn.className = "btn btn-sm btn-outline-warning";
      deadLetterBtn.textContent = "Dead-letter";
      deadLetterBtn.dataset.action = "outbox-deadletter";
      deadLetterBtn.dataset.id = String(item.id);
      actionCell.appendChild(deadLetterBtn);
    } else {
      actionCell.innerHTML = `<span class="text-muted small">No actions</span>`;
    }

    return row;
  };

  const renderOutbox = (items) => {
    if (!outboxBody) {
      return;
    }

    outboxBody.innerHTML = "";
    if (!items.length) {
      outboxBody.innerHTML = `<tr><td colspan="9" class="text-center text-muted py-4">No outbox messages found.</td></tr>`;
      return;
    }

    const fragment = document.createDocumentFragment();
    items.forEach((item) => fragment.appendChild(createOutboxRow(item)));
    outboxBody.appendChild(fragment);
  };

  const createReceiptRow = (item) => {
    const id = escapeHtml(item.id);
    const status = escapeHtml(item.status ?? "-");
    const eventKey = escapeHtml(item.eventKey ?? "-");
    const eventKeyTitle = escapeHtml(item.eventKey ?? "");
    const eventType = escapeHtml(item.eventType ?? "-");
    const reference = escapeHtml(item.externalReference ?? "-");
    const referenceTitle = escapeHtml(item.externalReference ?? "");
    const attempts = escapeHtml(item.attemptCount ?? 0);
    const updatedUtc = escapeHtml(formatUtc(item.updatedUtc));

    const row = document.createElement("tr");
    row.innerHTML = `
      <td class="text-nowrap">${id}</td>
      <td class="text-nowrap">${status}</td>
      <td class="text-truncate" style="max-width: 210px;" title="${eventKeyTitle}">${eventKey}</td>
      <td class="text-nowrap">${eventType}</td>
      <td class="text-truncate" style="max-width: 180px;" title="${referenceTitle}">${reference}</td>
      <td class="text-nowrap">${attempts}</td>
      <td class="text-nowrap">${updatedUtc}</td>
      <td class="text-end text-nowrap"></td>
    `;

    const actionCell = row.querySelector("td:last-child");
    if (!actionCell) {
      return row;
    }

    const replayBtn = document.createElement("button");
    replayBtn.type = "button";
    replayBtn.className = "btn btn-sm btn-outline-primary";
    replayBtn.textContent = "Replay";
    replayBtn.dataset.action = "receipt-replay";
    replayBtn.dataset.eventKey = item.eventKey ?? "";
    replayBtn.dataset.reference = item.externalReference ?? "";
    actionCell.appendChild(replayBtn);

    return row;
  };

  const renderReceipts = (items) => {
    if (!receiptsBody) {
      return;
    }

    receiptsBody.innerHTML = "";
    if (!items.length) {
      receiptsBody.innerHTML = `<tr><td colspan="8" class="text-center text-muted py-4">No webhook receipts found.</td></tr>`;
      return;
    }

    const fragment = document.createDocumentFragment();
    items.forEach((item) => fragment.appendChild(createReceiptRow(item)));
    receiptsBody.appendChild(fragment);
  };

  const setLoading = (button, loading, labelWhenLoading) => {
    if (!button) {
      return;
    }

    if (loading) {
      button.dataset.originalText = button.textContent || "";
      button.textContent = labelWhenLoading;
      button.disabled = true;
    } else {
      button.textContent = button.dataset.originalText || button.textContent || "";
      button.disabled = false;
    }
  };

  const loadOutbox = async () => {
    const params = new URLSearchParams();
    const statusValue = outboxStatus && outboxStatus.value ? outboxStatus.value : "";
    const takeValue = toInt(outboxTake ? outboxTake.value : "100", 100);

    if (statusValue) {
      params.set("status", statusValue);
    }

    params.set("take", String(Math.max(1, Math.min(500, takeValue))));

    try {
      const payload = await fetchJson(`/api/admin/integration/outbox?${params.toString()}`);
      renderOutbox(Array.isArray(payload) ? payload : []);
    } catch (error) {
      showAlert("danger", `Failed to load outbox: ${error.message}`);
      renderOutbox([]);
    }
  };

  const loadReceipts = async () => {
    const params = new URLSearchParams();
    const statusValue = receiptsStatus && receiptsStatus.value ? receiptsStatus.value : "";
    const referenceValue = receiptsReference && receiptsReference.value ? receiptsReference.value.trim() : "";
    const takeValue = toInt(receiptsTake ? receiptsTake.value : "100", 100);

    if (statusValue) {
      params.set("status", statusValue);
    }

    if (referenceValue) {
      params.set("reference", referenceValue);
    }

    params.set("take", String(Math.max(1, Math.min(500, takeValue))));

    try {
      const payload = await fetchJson(`/api/admin/integration/webhooks/paymongo/receipts?${params.toString()}`);
      renderReceipts(Array.isArray(payload) ? payload : []);
    } catch (error) {
      showAlert("danger", `Failed to load receipts: ${error.message}`);
      renderReceipts([]);
    }
  };

  const retryOutbox = async (id) => {
    await fetchJson(`/api/admin/integration/outbox/${id}/retry`, { method: "POST" });
    showAlert("success", `Outbox message #${id} queued for retry.`);
    await loadOutbox();
  };

  const deadLetterOutbox = async (id) => {
    const reasonInput = window.prompt("Dead-letter reason:", "Manual review required");
    if (reasonInput === null) {
      return;
    }

    const reason = reasonInput.trim();
    await fetchJson(`/api/admin/integration/outbox/${id}/dead-letter`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason })
    });

    showAlert("warning", `Outbox message #${id} moved to dead-letter.`);
    await loadOutbox();
  };

  const retryFailedOutbox = async () => {
    const takeValue = toInt(outboxTake ? outboxTake.value : "50", 50);
    const payload = await fetchJson("/api/admin/integration/outbox/retry-failed", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        take: Math.max(1, Math.min(500, takeValue))
      })
    });

    const retried = payload && typeof payload.retried === "number" ? payload.retried : 0;
    showAlert("success", `Retried ${retried} failed outbox message(s).`);
    await loadOutbox();
  };

  const replayWebhook = async (requestBody) => {
    await fetchJson("/api/admin/integration/webhooks/paymongo/replay", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(requestBody)
    });

    showAlert("success", "Replay request accepted and queued to outbox.");
    await Promise.all([loadOutbox(), loadReceipts()]);
  };

  outboxRefreshButtons.forEach((button) => {
    button.addEventListener("click", () => {
      clearAlert();
      void loadOutbox();
    });
  });

  if (outboxRetryFailedButton) {
    outboxRetryFailedButton.addEventListener("click", async () => {
      clearAlert();
      try {
        setLoading(outboxRetryFailedButton, true, "Retrying...");
        await retryFailedOutbox();
      } catch (error) {
        showAlert("danger", `Retry failed: ${error.message}`);
      } finally {
        setLoading(outboxRetryFailedButton, false, "Retry Failed");
      }
    });
  }

  if (outboxBody) {
    outboxBody.addEventListener("click", async (event) => {
      const button = event.target.closest("button[data-action]");
      if (!button) {
        return;
      }

      const action = button.dataset.action;
      const id = toInt(button.dataset.id || "", 0);
      if (id <= 0) {
        return;
      }

      clearAlert();
      try {
        if (action === "outbox-retry") {
          setLoading(button, true, "Retrying...");
          await retryOutbox(id);
        } else if (action === "outbox-deadletter") {
          setLoading(button, true, "Saving...");
          await deadLetterOutbox(id);
        }
      } catch (error) {
        showAlert("danger", `Outbox action failed: ${error.message}`);
      } finally {
        setLoading(button, false, "Action");
      }
    });
  }

  if (receiptsRefreshButton) {
    receiptsRefreshButton.addEventListener("click", () => {
      clearAlert();
      void loadReceipts();
    });
  }

  if (receiptsBody) {
    receiptsBody.addEventListener("click", (event) => {
      const button = event.target.closest("button[data-action='receipt-replay']");
      if (!button) {
        return;
      }

      if (replayEventKey) {
        replayEventKey.value = button.dataset.eventKey || "";
      }

      if (replayReference) {
        replayReference.value = button.dataset.reference || "";
      }

      if (replayForce) {
        replayForce.checked = false;
      }

      showAlert("info", "Replay form pre-filled from selected receipt. Click 'Queue Replay' to continue.");
      window.scrollTo({ top: 0, behavior: "smooth" });
    });
  }

  if (replayForm) {
    replayForm.addEventListener("submit", async (event) => {
      event.preventDefault();
      clearAlert();

      const eventKey = replayEventKey && replayEventKey.value ? replayEventKey.value.trim() : "";
      const reference = replayReference && replayReference.value ? replayReference.value.trim() : "";
      const force = replayForce ? replayForce.checked : false;

      if (!eventKey && !reference) {
        showAlert("warning", "Provide either Event Key or Reference before replay.");
        return;
      }

      const body = {
        eventKey: eventKey || null,
        reference: reference || null,
        force
      };

      try {
        setLoading(replaySubmit, true, "Queuing...");
        await replayWebhook(body);
      } catch (error) {
        showAlert("danger", `Replay failed: ${error.message}`);
      } finally {
        setLoading(replaySubmit, false, "Queue Replay");
      }
    });
  }

  if (replayClear) {
    replayClear.addEventListener("click", () => {
      if (replayEventKey) {
        replayEventKey.value = "";
      }

      if (replayReference) {
        replayReference.value = "";
      }

      if (replayForce) {
        replayForce.checked = false;
      }

      clearAlert();
    });
  }

  void Promise.all([loadOutbox(), loadReceipts()]);
})();
