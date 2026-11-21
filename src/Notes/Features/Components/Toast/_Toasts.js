// The stack of cards at the bottom of every page (_Toasts.cshtml, which says how to use it). The page also has a message shown when it
// loads for each element with data-notify (its text) and data-notify-kind, for what the server has to say.
(function () {
    const container = document.getElementById('floating-cards');
    const DEFAULT_DURATION = Number(container.dataset.defaultDuration);
    const ERROR_DURATION = Number(container.dataset.errorDuration);
    const STORAGE_KEY = 'notes.toasts';
    const texts = textsOf('toast-texts');

    const KINDS = {
        success: { color: 'success', icon: 'fa-circle-check' },
        info: { color: 'primary', icon: 'fa-circle-info' },
        warning: { color: 'warning', icon: 'fa-triangle-exclamation' },
        danger: { color: 'danger', icon: 'fa-circle-exclamation' }
    };

    window.notify = function (message, kind) {
        const { color, icon } = KINDS[kind] || KINDS.success;

        const card = document.createElement('div');
        card.className = 'floating-card toast-card card shadow';
        card.style.borderRight = '0.25rem solid var(--bs-' + color + ')'; // the kind, at a glance
        card.setAttribute('role', kind === 'danger' ? 'alert' : 'status');
        card.dataset.kind = KINDS[kind] ? kind : 'success';
        card.title = texts.clickToDismiss;

        const body = document.createElement('div');
        body.className = 'card-body d-flex align-items-center gap-2 py-2 px-3';
        const symbol = document.createElement('i');
        symbol.className = 'fa-solid ' + icon + ' text-' + color;
        const text = document.createElement('span');
        text.textContent = message;
        body.append(symbol, text);
        card.appendChild(body);

        let timer = null;
        const dismiss = function () {
            clearTimeout(timer);
            card.remove();
        };
        card.addEventListener('click', dismiss);
        timer = setTimeout(dismiss, kind === 'danger' ? ERROR_DURATION : DEFAULT_DURATION);

        container.prepend(card); // the newest on top
    };

    window.notifyAfterNavigation = function (message, kind) {
        try {
            const queued = JSON.parse(sessionStorage.getItem(STORAGE_KEY) || '[]');
            queued.push({ message, kind });
            sessionStorage.setItem(STORAGE_KEY, JSON.stringify(queued));
        } catch {
            // No storage (private mode and the like): the message is lost, the action itself still happened
        }
    };

    // What the page before this one left for us
    try {
        const queued = JSON.parse(sessionStorage.getItem(STORAGE_KEY) || '[]');
        sessionStorage.removeItem(STORAGE_KEY);
        for (const { message, kind } of queued)
            window.notify(message, kind);
    } catch { }

    // What the server has to say with this page (the elements come after the stack, so the page has to be there first)
    document.addEventListener('DOMContentLoaded', function () {
        for (const element of document.querySelectorAll('[data-notify]'))
            window.notify(element.dataset.notify, element.dataset.notifyKind);
    });
})();
