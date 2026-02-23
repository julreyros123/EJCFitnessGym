(function () {
    "use strict";

    var body = document.body;
    if (!body || body.dataset.ejcAuthenticated !== "true") {
        return;
    }

    if (!window.signalR) {
        return;
    }

    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/erp-events")
        .withAutomaticReconnect()
        .build();

    connection.on("erp-event", function (eventPayload) {
        if (!eventPayload || !eventPayload.eventType) {
            return;
        }

        window.dispatchEvent(new CustomEvent("ejc:erp-event", { detail: eventPayload }));
        showToast(eventPayload.message || eventPayload.eventType);
    });

    connection.start().catch(function (error) {
        console.error("Realtime connection failed.", error);
    });

    function showToast(message) {
        if (!message || typeof bootstrap === "undefined") {
            return;
        }

        var container = getToastContainer();
        var toastElement = document.createElement("div");
        toastElement.className = "toast align-items-center text-bg-dark border-0";
        toastElement.setAttribute("role", "status");
        toastElement.setAttribute("aria-live", "polite");
        toastElement.setAttribute("aria-atomic", "true");

        toastElement.innerHTML = [
            '<div class="d-flex">',
            '  <div class="toast-body"></div>',
            '  <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>',
            "</div>"
        ].join("");

        var toastBody = toastElement.querySelector(".toast-body");
        if (toastBody) {
            toastBody.textContent = message;
        }

        container.appendChild(toastElement);

        toastElement.addEventListener("hidden.bs.toast", function () {
            toastElement.remove();
        });

        var toast = new bootstrap.Toast(toastElement, { delay: 5000 });
        toast.show();
    }

    function getToastContainer() {
        var existing = document.getElementById("ejc-realtime-toast-container");
        if (existing) {
            return existing;
        }

        var container = document.createElement("div");
        container.id = "ejc-realtime-toast-container";
        container.className = "toast-container position-fixed top-0 end-0 p-3";
        container.style.zIndex = "1080";
        document.body.appendChild(container);
        return container;
    }
})();
