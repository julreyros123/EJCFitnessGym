// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const currentPath = window.location.pathname.toLowerCase();
if (currentPath.startsWith('/identity/account/manage')) {
    document.body.classList.add('ejc-manage');
}

(() => {
    const body = document.body;
    if (!body || body.dataset.ejcBackofficeTheme !== 'true') {
        return;
    }

    const storageKey = 'ejc.backoffice.theme';
    const validThemes = new Set(['light', 'dark']);
    const mediaQuery = typeof window.matchMedia === 'function'
        ? window.matchMedia('(prefers-color-scheme: dark)')
        : null;

    const getStoredTheme = () => {
        try {
            const storedTheme = localStorage.getItem(storageKey);
            return validThemes.has(storedTheme) ? storedTheme : null;
        } catch {
            return null;
        }
    };

    const updateToggleButtons = (theme) => {
        const nextTheme = theme === 'dark' ? 'light' : 'dark';
        const title = nextTheme === 'light' ? 'Switch to light mode' : 'Switch to dark mode';

        document.querySelectorAll('[data-ejc-theme-toggle]').forEach((button) => {
            button.setAttribute('aria-label', title);
            button.setAttribute('title', title);

            const icon = button.querySelector('[data-ejc-theme-icon]');
            if (!icon) {
                return;
            }

            icon.classList.remove('bi-sun-fill', 'bi-moon-stars-fill');
            icon.classList.add(theme === 'dark' ? 'bi-sun-fill' : 'bi-moon-stars-fill');
        });
    };

    const applyTheme = (theme, persist = true) => {
        const resolvedTheme = theme === 'light' ? 'light' : 'dark';

        body.setAttribute('data-ejc-theme', resolvedTheme);
        document.documentElement.setAttribute('data-bs-theme', resolvedTheme);
        updateToggleButtons(resolvedTheme);

        if (!persist) {
            return;
        }

        try {
            localStorage.setItem(storageKey, resolvedTheme);
        } catch {
            // Ignore blocked storage.
        }
    };

    const storedTheme = getStoredTheme();
    const bodyTheme = body.getAttribute('data-ejc-theme');
    const initialTheme = validThemes.has(storedTheme)
        ? storedTheme
        : validThemes.has(bodyTheme)
            ? bodyTheme
            : mediaQuery?.matches
                ? 'dark'
                : 'light';

    applyTheme(initialTheme, false);

    document.addEventListener('click', (event) => {
        const toggle = event.target.closest('[data-ejc-theme-toggle]');
        if (!toggle) {
            return;
        }

        const currentTheme = body.getAttribute('data-ejc-theme') === 'light' ? 'light' : 'dark';
        applyTheme(currentTheme === 'dark' ? 'light' : 'dark');
    });

    window.addEventListener('storage', (event) => {
        if (event.key !== storageKey || !validThemes.has(event.newValue)) {
            return;
        }

        applyTheme(event.newValue, false);
    });
})();

document.addEventListener('click', (event) => {
    const toggle = event.target.closest('[data-password-toggle]');
    if (!toggle) {
        return;
    }

    const field = toggle.closest('.ejc-password-field');
    if (!field) {
        return;
    }

    const input = field.querySelector('input');
    if (!input) {
        return;
    }

    const isHidden = input.type === 'password';
    input.type = isHidden ? 'text' : 'password';
    toggle.setAttribute('aria-label', isHidden ? 'Hide password' : 'Show password');

    const icon = toggle.querySelector('i');
    if (icon) {
        icon.classList.toggle('bi-eye', !isHidden);
        icon.classList.toggle('bi-eye-slash', isHidden);
    }
});

(() => {
    const offcanvas = document.getElementById('ejcNavSidebar');
    if (!offcanvas || typeof bootstrap === 'undefined') {
        return;
    }

    offcanvas.addEventListener('click', (event) => {
        const link = event.target.closest('a[href]');
        if (!link) {
            return;
        }

        if (window.matchMedia('(min-width: 992px)').matches) {
            return;
        }

        const href = link.getAttribute('href');
        if (!href || href === '#') {
            return;
        }

        bootstrap.Offcanvas.getOrCreateInstance(offcanvas).hide();
    });
})();

(() => {
    if (typeof bootstrap === 'undefined') {
        return;
    }

    const offcanvasIds = [
        'ejcAdminSidebarOffcanvas',
        'ejcStaffSidebarOffcanvas',
        'ejcFinanceSidebarOffcanvas',
        'ejcSuperAdminSidebarOffcanvas'
    ];

    offcanvasIds.forEach((id) => {
        const offcanvas = document.getElementById(id);
        if (!offcanvas) {
            return;
        }

        offcanvas.addEventListener('click', (event) => {
            const link = event.target.closest('a[href]');
            if (!link) {
                return;
            }

            if (window.matchMedia('(min-width: 992px)').matches) {
                return;
            }

            const href = link.getAttribute('href');
            if (!href || href === '#') {
                return;
            }

            bootstrap.Offcanvas.getOrCreateInstance(offcanvas).hide();
        });
    });
})();

(() => {
    const layouts = document.querySelectorAll('[data-admin-layout]');
    if (!layouts.length) {
        return;
    }

    const storageKey = 'ejc.adminSidebarCollapsed';
    const collapsedByDefault = localStorage.getItem(storageKey) === '1';

    const applyState = (layout, isCollapsed) => {
        layout.classList.toggle('is-collapsed', isCollapsed);

        const sidebar = layout.querySelector('[data-admin-sidebar]');
        if (sidebar) {
            sidebar.classList.toggle('is-collapsed', isCollapsed);
        }

        const toggle = layout.querySelector('[data-admin-sidebar-toggle]');
        if (!toggle) {
            return;
        }

        toggle.setAttribute('aria-expanded', String(!isCollapsed));
        toggle.setAttribute('aria-label', isCollapsed ? 'Expand admin sidebar' : 'Collapse admin sidebar');

        const icon = toggle.querySelector('i');
        if (icon) {
            icon.classList.toggle('bi-layout-sidebar-inset', !isCollapsed);
            icon.classList.toggle('bi-layout-sidebar', isCollapsed);
        }
    };

    layouts.forEach((layout) => applyState(layout, collapsedByDefault));

    document.addEventListener('click', (event) => {
        const toggle = event.target.closest('[data-admin-sidebar-toggle]');
        if (!toggle) {
            return;
        }

        const layout = toggle.closest('[data-admin-layout]');
        if (!layout) {
            return;
        }

        const nextCollapsedState = !layout.classList.contains('is-collapsed');
        applyState(layout, nextCollapsedState);
        localStorage.setItem(storageKey, nextCollapsedState ? '1' : '0');
    });
})();

(() => {
    if (!document.body.classList.contains('ejc-superadmin-body')) {
        return;
    }

    const navLinks = Array.from(document.querySelectorAll('a[data-superadmin-nav-item][href]'));
    if (!navLinks.length) {
        return;
    }

    const normalize = (value) => (value || '').toLowerCase().trim();
    const toRelativeHref = (href) => {
        try {
            const url = new URL(href, window.location.origin);
            return `${url.pathname}${url.search}`;
        } catch {
            return href || '/';
        }
    };
    const normalizePath = (value) => normalize(toRelativeHref(value)).replace(/\/+$/, '') || '/';

    const currentPath = `${window.location.pathname}${window.location.search}`;
    const currentPathNormalized = normalizePath(currentPath);
    const titleBase = (document.title || '').replace(/\s*-\s*ejcfitnessgym\s*$/i, '').trim();
    const activeNavLink = document.querySelector('a[data-superadmin-nav-item].active');
    const currentLabel = (activeNavLink?.dataset.superadminNavLabel || titleBase || 'Current Page').trim();

    const currentPathKey = 'ejc.superadmin.currentPath';
    const currentLabelKey = 'ejc.superadmin.currentLabel';
    const previousPathKey = 'ejc.superadmin.previousPath';
    const previousLabelKey = 'ejc.superadmin.previousLabel';

    try {
        const storedCurrentPath = localStorage.getItem(currentPathKey);
        const storedCurrentLabel = localStorage.getItem(currentLabelKey);

        if (storedCurrentPath && normalizePath(storedCurrentPath) !== currentPathNormalized) {
            localStorage.setItem(previousPathKey, toRelativeHref(storedCurrentPath));
            if (storedCurrentLabel) {
                localStorage.setItem(previousLabelKey, storedCurrentLabel);
            }
        }

        localStorage.setItem(currentPathKey, currentPath);
        localStorage.setItem(currentLabelKey, currentLabel);
    } catch {
        // Ignore storage failures (private mode or blocked storage).
    }

    const resumeLink = document.querySelector('[data-superadmin-resume-link]');
    const resumeLabel = resumeLink?.querySelector('[data-superadmin-resume-label]');

    if (resumeLink) {
        try {
            const previousPath = localStorage.getItem(previousPathKey);
            const previousLabel = localStorage.getItem(previousLabelKey);

            if (previousPath && normalizePath(previousPath) !== currentPathNormalized) {
                resumeLink.href = toRelativeHref(previousPath);
                resumeLink.classList.remove('d-none');
                const labelText = previousLabel ? `Resume ${previousLabel}` : 'Resume Last';
                if (resumeLabel) {
                    resumeLabel.textContent = labelText;
                }
                resumeLink.setAttribute('title', labelText);
            }
        } catch {
            // Keep the resume link hidden when storage is unavailable.
        }
    }

    const navEntries = navLinks.map((link, index) => {
        const href = toRelativeHref(link.getAttribute('href') || '/');
        const label = (link.dataset.superadminNavLabel || link.textContent || `Page ${index + 1}`).trim();
        const description = (link.dataset.superadminNavDescription || link.title || '').trim();
        const shortcut = (link.dataset.superadminNavShortcut || `Alt+${index + 1}`).trim();

        return {
            href,
            label,
            description,
            shortcut,
            path: normalizePath(href),
            haystack: normalize(`${label} ${description} ${href}`)
        };
    });

    const uniqueEntries = [];
    const seenPaths = new Set();

    navEntries.forEach((entry) => {
        if (seenPaths.has(entry.path)) {
            return;
        }

        seenPaths.add(entry.path);
        uniqueEntries.push(entry);
    });

    const modalElement = document.getElementById('ejcSuperAdminCommandModal');
    const searchInput = modalElement?.querySelector('[data-superadmin-command-search]');
    const commandList = modalElement?.querySelector('[data-superadmin-command-list]');
    const openButtons = Array.from(document.querySelectorAll('[data-superadmin-command-open]'));

    const isEditableTarget = (target) => {
        if (!(target instanceof HTMLElement)) {
            return false;
        }

        const tagName = target.tagName.toLowerCase();
        return tagName === 'input'
            || tagName === 'textarea'
            || tagName === 'select'
            || target.isContentEditable;
    };

    const activateShortcut = (index) => {
        const entry = uniqueEntries[index];
        if (!entry || entry.path === currentPathNormalized) {
            return;
        }

        window.location.assign(entry.href);
    };

    document.addEventListener('keydown', (event) => {
        if ((event.ctrlKey || event.metaKey) && normalize(event.key) === 'k') {
            if (!modalElement || typeof bootstrap === 'undefined') {
                return;
            }

            event.preventDefault();
            bootstrap.Modal.getOrCreateInstance(modalElement).show();
            return;
        }

        if (event.altKey
            && !event.ctrlKey
            && !event.metaKey
            && !event.shiftKey
            && /^[1-9]$/.test(event.key)
            && !isEditableTarget(event.target)) {
            event.preventDefault();
            activateShortcut(Number.parseInt(event.key, 10) - 1);
        }
    });

    if (!modalElement || !searchInput || !commandList || typeof bootstrap === 'undefined') {
        return;
    }

    let filteredEntries = [...uniqueEntries];
    let selectedIndex = 0;

    const renderEntries = () => {
        commandList.innerHTML = '';

        if (!filteredEntries.length) {
            const emptyState = document.createElement('div');
            emptyState.className = 'ejc-superadmin-command-empty';
            emptyState.textContent = 'No destinations match your search.';
            commandList.appendChild(emptyState);
            return;
        }

        filteredEntries.forEach((entry, index) => {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'ejc-superadmin-command-item';
            if (index === selectedIndex) {
                item.classList.add('is-selected');
            }

            item.dataset.superadminCommandIndex = String(index);

            const topRow = document.createElement('div');
            topRow.className = 'ejc-superadmin-command-item-top';

            const label = document.createElement('span');
            label.className = 'ejc-superadmin-command-item-label';
            label.textContent = entry.label;

            const shortcut = document.createElement('span');
            shortcut.className = 'ejc-superadmin-command-item-shortcut';
            shortcut.textContent = entry.path === currentPathNormalized ? 'Current' : entry.shortcut;

            topRow.appendChild(label);
            topRow.appendChild(shortcut);

            const description = document.createElement('span');
            description.className = 'ejc-superadmin-command-item-description';
            description.textContent = entry.description || entry.href;

            item.appendChild(topRow);
            item.appendChild(description);
            commandList.appendChild(item);
        });
    };

    const applyFilter = () => {
        const query = normalize(searchInput.value);
        const terms = query.split(/\s+/).filter(Boolean);

        filteredEntries = uniqueEntries.filter((entry) => terms.every((term) => entry.haystack.includes(term)));
        selectedIndex = 0;
        renderEntries();
    };

    const openSelectedEntry = () => {
        const selected = filteredEntries[selectedIndex];
        if (!selected || selected.path === currentPathNormalized) {
            return;
        }

        bootstrap.Modal.getOrCreateInstance(modalElement).hide();
        window.location.assign(selected.href);
    };

    openButtons.forEach((button) => {
        button.addEventListener('click', () => {
            bootstrap.Modal.getOrCreateInstance(modalElement).show();
        });
    });

    modalElement.addEventListener('shown.bs.modal', () => {
        searchInput.value = '';
        filteredEntries = [...uniqueEntries];

        const currentIndex = filteredEntries.findIndex((entry) => entry.path === currentPathNormalized);
        selectedIndex = currentIndex >= 0 ? currentIndex : 0;

        renderEntries();
        searchInput.focus();
        searchInput.select();
    });

    searchInput.addEventListener('input', applyFilter);

    modalElement.addEventListener('keydown', (event) => {
        if (!filteredEntries.length) {
            return;
        }

        if (event.key === 'ArrowDown') {
            event.preventDefault();
            selectedIndex = (selectedIndex + 1) % filteredEntries.length;
            renderEntries();
            return;
        }

        if (event.key === 'ArrowUp') {
            event.preventDefault();
            selectedIndex = (selectedIndex - 1 + filteredEntries.length) % filteredEntries.length;
            renderEntries();
            return;
        }

        if (event.key === 'Enter') {
            event.preventDefault();
            openSelectedEntry();
        }
    });

    commandList.addEventListener('click', (event) => {
        const item = event.target.closest('[data-superadmin-command-index]');
        if (!item) {
            return;
        }

        selectedIndex = Number.parseInt(item.dataset.superadminCommandIndex || '0', 10) || 0;
        openSelectedEntry();
    });

    commandList.addEventListener('mousemove', (event) => {
        const item = event.target.closest('[data-superadmin-command-index]');
        if (!item) {
            return;
        }

        const index = Number.parseInt(item.dataset.superadminCommandIndex || '0', 10);
        if (Number.isNaN(index) || index === selectedIndex) {
            return;
        }

        selectedIndex = index;
        renderEntries();
    });
})();

(() => {
    const card = document.querySelector('[data-members-table-card]');
    if (!card) {
        return;
    }

    const rows = Array.from(card.querySelectorAll('[data-member-row]'));
    const chipButtons = Array.from(card.querySelectorAll('[data-member-status]'));
    const searchInput = card.querySelector('[data-member-search]');
    const planSelect = card.querySelector('[data-member-plan]');
    const statusSelect = card.querySelector('[data-member-status-select]');

    const normalize = (value) => (value || '').toLowerCase().trim();
    let chipFilter = 'all';

    const applyFilters = () => {
        const query = normalize(searchInput?.value);
        const planFilter = normalize(planSelect?.value || 'all');
        const statusDropdownFilter = normalize(statusSelect?.value || 'all');

        rows.forEach((row) => {
            const name = normalize(row.dataset.name);
            const plan = normalize(row.dataset.plan);
            const status = normalize(row.dataset.status);
            const haystack = normalize(row.textContent);

            const matchesSearch = !query || haystack.includes(query) || name.includes(query);
            const matchesPlan = planFilter === 'all' || plan === planFilter;
            const matchesChip = chipFilter === 'all' || status === chipFilter;
            const matchesDropdownStatus = statusDropdownFilter === 'all' || status === statusDropdownFilter;

            row.hidden = !(matchesSearch && matchesPlan && matchesChip && matchesDropdownStatus);
        });
    };

    chipButtons.forEach((button) => {
        button.addEventListener('click', () => {
            chipButtons.forEach((candidate) => candidate.classList.remove('active'));
            button.classList.add('active');
            chipFilter = normalize(button.dataset.memberStatus || 'all');
            applyFilters();
        });
    });

    searchInput?.addEventListener('input', applyFilters);
    planSelect?.addEventListener('change', applyFilters);
    statusSelect?.addEventListener('change', applyFilters);
})();

(() => {
    const tables = Array.from(document.querySelectorAll('.ejc-admin-content table.table'));
    if (!tables.length) {
        return;
    }

    const defaultPageSize = 8;
    const pageSizeOptions = [5, 8, 12, 20, 50];

    const normalize = (value) => (value || '').toLowerCase().trim();
    const escapeHtml = (value) =>
        String(value ?? '').replace(/[&<>"']/g, (char) => {
            const map = {
                '&': '&amp;',
                '<': '&lt;',
                '>': '&gt;',
                '"': '&quot;',
                '\'': '&#39;'
            };

            return map[char] ?? char;
        });

    tables.forEach((table, index) => {
        if (table.dataset.ejcPaginateDisabled === 'true') {
            return;
        }

        if (table.closest('[data-members-table-card]')) {
            return;
        }

        if (table.dataset.ejcPaginateInit === '1') {
            return;
        }

        const tbody = table.tBodies[0];
        if (!tbody) {
            return;
        }

        const rows = Array.from(tbody.rows);
        if (!rows.length) {
            return;
        }

        table.dataset.ejcPaginateInit = '1';
        if (!table.id) {
            table.id = `ejc-table-${index + 1}`;
        }

        const responsiveWrap = table.closest('.table-responsive');
        const insertionTarget = responsiveWrap || table;
        const host = insertionTarget.parentElement;
        if (!host) {
            return;
        }

        const placeholder = table.dataset.ejcSearchPlaceholder || 'Search records';
        const preferredPageSize = Number.parseInt(table.dataset.ejcPageSize || `${defaultPageSize}`, 10);

        let pageSize = Number.isFinite(preferredPageSize) && preferredPageSize > 0
            ? preferredPageSize
            : defaultPageSize;
        let currentPage = 1;
        let filteredRows = [...rows];
        let emptyRow = null;

        const controls = document.createElement('div');
        controls.className = 'ejc-table-enhancer';
        controls.setAttribute('data-ejc-table-controls', table.id);
        controls.innerHTML = `
            <div class="ejc-table-enhancer-top">
                <div class="input-group input-group-sm ejc-table-enhancer-search">
                    <span class="input-group-text"><i class="bi bi-search"></i></span>
                    <input type="search" class="form-control" data-ejc-table-search placeholder="${escapeHtml(placeholder)}" aria-label="Search table rows">
                </div>
                <div class="ejc-table-enhancer-options">
                    <label class="small text-muted mb-0" for="${table.id}-page-size">Rows</label>
                    <select id="${table.id}-page-size" class="form-select form-select-sm" data-ejc-page-size aria-label="Rows per page">
                        ${pageSizeOptions.map((size) => `<option value="${size}">${size}</option>`).join('')}
                        <option value="all">All</option>
                    </select>
                </div>
            </div>
            <div class="ejc-table-enhancer-bottom">
                <span class="text-muted small" data-ejc-page-info></span>
                <div class="btn-group btn-group-sm ejc-table-pager" role="group" aria-label="Table pagination">
                    <button type="button" class="btn btn-outline-primary" data-ejc-page-prev>Prev</button>
                    <button type="button" class="btn btn-outline-primary ejc-table-page-indicator" data-ejc-page-indicator disabled>Page 1</button>
                    <button type="button" class="btn btn-outline-primary" data-ejc-page-next>Next</button>
                </div>
            </div>
        `;

        host.insertBefore(controls, insertionTarget);
        insertionTarget.classList.add('ejc-table-enhancer-target');
        table.classList.add('ejc-table-enhanced');

        const searchInput = controls.querySelector('[data-ejc-table-search]');
        const pageSizeSelect = controls.querySelector('[data-ejc-page-size]');
        const pageInfo = controls.querySelector('[data-ejc-page-info]');
        const pageIndicator = controls.querySelector('[data-ejc-page-indicator]');
        const prevButton = controls.querySelector('[data-ejc-page-prev]');
        const nextButton = controls.querySelector('[data-ejc-page-next]');

        if (!searchInput || !pageSizeSelect || !pageInfo || !pageIndicator || !prevButton || !nextButton) {
            return;
        }

        if (!pageSizeOptions.includes(pageSize)) {
            const customOption = document.createElement('option');
            customOption.value = String(pageSize);
            customOption.textContent = String(pageSize);
            pageSizeSelect.insertBefore(customOption, pageSizeSelect.lastElementChild || null);
        }
        pageSizeSelect.value = String(pageSize);

        const removeEmptyRow = () => {
            if (!emptyRow) {
                return;
            }
            emptyRow.remove();
            emptyRow = null;
        };

        const showEmptyRow = () => {
            if (emptyRow) {
                return;
            }

            const columnCount =
                table.tHead?.rows[0]?.cells.length ||
                rows[0]?.cells.length ||
                1;

            emptyRow = document.createElement('tr');
            emptyRow.className = 'ejc-table-empty-row';
            emptyRow.innerHTML = `<td colspan="${columnCount}" class="text-center text-muted py-4">No matching records found.</td>`;
            tbody.appendChild(emptyRow);
        };

        const updateTable = () => {
            removeEmptyRow();

            const query = normalize(searchInput.value);
            filteredRows = rows.filter((row) => normalize(row.textContent).includes(query));

            const perPage = pageSize === Number.MAX_SAFE_INTEGER
                ? Math.max(filteredRows.length, 1)
                : pageSize;

            const totalRows = filteredRows.length;
            const totalPages = Math.max(1, Math.ceil(totalRows / perPage));

            currentPage = Math.max(1, Math.min(currentPage, totalPages));

            rows.forEach((row) => {
                row.hidden = true;
            });

            if (totalRows === 0) {
                showEmptyRow();
                pageInfo.textContent = 'No rows to display';
                pageIndicator.textContent = 'Page 0 / 0';
                prevButton.disabled = true;
                nextButton.disabled = true;
                controls.classList.add('is-single-page');
                controls.classList.add('is-empty');
                return;
            }

            const startIndex = (currentPage - 1) * perPage;
            const endIndex = Math.min(startIndex + perPage, totalRows);

            for (let i = startIndex; i < endIndex; i += 1) {
                filteredRows[i].hidden = false;
            }

            pageInfo.textContent = `Showing ${startIndex + 1}-${endIndex} of ${totalRows} rows`;
            pageIndicator.textContent = `Page ${currentPage} / ${totalPages}`;
            prevButton.disabled = currentPage <= 1;
            nextButton.disabled = currentPage >= totalPages;
            controls.classList.toggle('is-single-page', totalPages <= 1);
            controls.classList.remove('is-empty');
        };

        searchInput.addEventListener('input', () => {
            currentPage = 1;
            updateTable();
        });

        pageSizeSelect.addEventListener('change', () => {
            const selected = pageSizeSelect.value;
            pageSize = selected === 'all'
                ? Number.MAX_SAFE_INTEGER
                : Number.parseInt(selected, 10) || defaultPageSize;
            currentPage = 1;
            updateTable();
        });

        prevButton.addEventListener('click', () => {
            currentPage -= 1;
            updateTable();
        });

        nextButton.addEventListener('click', () => {
            currentPage += 1;
            updateTable();
        });

        updateTable();
    });
})();

(() => {
    const revealElements = Array.from(document.querySelectorAll('[data-ejc-reveal]'));
    if (!revealElements.length) {
        return;
    }

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduceMotion || typeof IntersectionObserver === 'undefined') {
        revealElements.forEach((element) => element.classList.add('is-visible'));
        return;
    }

    const observer = new IntersectionObserver(
        (entries) => {
            entries.forEach((entry) => {
                if (!entry.isIntersecting) {
                    return;
                }

                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            });
        },
        {
            rootMargin: '0px 0px -12% 0px',
            threshold: 0.08
        }
    );

    revealElements.forEach((element) => observer.observe(element));
})();

(() => {
    const forms = Array.from(document.querySelectorAll('form[method="post"]:not([data-ejc-no-loading])'));
    if (!forms.length) {
        return;
    }

    const applyButtonLoading = (button) => {
        if (!button || button.classList.contains('ejc-submit-loading')) {
            return;
        }

        if (button.tagName === 'INPUT') {
            button.dataset.ejcOriginalValue = button.value;
            button.value = 'Processing...';
            button.classList.add('ejc-submit-loading');
            button.disabled = true;
            return;
        }

        const original = button.innerHTML;
        button.dataset.ejcOriginalHtml = original;
        button.innerHTML = `<span class="ejc-btn-label">${original}</span>`;
        button.classList.add('ejc-submit-loading');
        button.setAttribute('aria-busy', 'true');
        button.disabled = true;
    };

    forms.forEach((form) => {
        let pendingButton = null;

        form.addEventListener('click', (event) => {
            const button = event.target.closest('button[type="submit"], input[type="submit"]');
            if (!button || !form.contains(button)) {
                return;
            }

            pendingButton = button;
        });

        form.addEventListener('submit', (event) => {
            const button = pendingButton
                || form.querySelector('button[type="submit"]:not([disabled]), input[type="submit"]:not([disabled])');

            pendingButton = null;
            if (!button) {
                return;
            }

            queueMicrotask(() => {
                if (event.defaultPrevented) {
                    return;
                }

                applyButtonLoading(button);
            });
        });
    });
})();
