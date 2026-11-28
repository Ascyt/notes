// The storage operations that run now (_Operations.cshtml): the button in the navbar (#operations-button) is there while any that the user
// may see runs, which is asked every few seconds while the page is in view (GET /?operations), and more often while the dialog shows them.
// Cancel (POST /?operations=cancel) asks the server to stop one; it goes from the list once it ended.
(function () {
    // A running operation, as the server lists it
    interface RunningOperation {
        id: string;
        title: string;
        directory: string;
        user: string;
        cancelling: boolean;
        canCancel: boolean;
        progress?: number;
        status?: WorkStatus;
    }

    const modal = document.getElementById('operations-modal')!;
    const list = document.getElementById('operations-list')!;
    const nothing = document.getElementById('operations-nothing')!;
    const texts = textsOf('operations-texts');
    const CHECK_EVERY_MS = 5000;
    const WHILE_OPEN_EVERY_MS = 1000;
    let open = false;
    let timer: number | undefined;

    const button = () => document.getElementById('operations-button');
    const token = () => document.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')?.value || '';

    async function fetchOperations(): Promise<RunningOperation[] | null> {
        try {
            const response = await fetch('/?operations', { headers: { 'Accept': 'application/json' } });
            return response.ok ? await response.json() : null;
        } catch {
            return null;
        }
    }

    function rowOf(operation: RunningOperation): HTMLElement {
        const row = document.createElement('li');
        row.className = 'list-group-item';
        row.dataset.operation = operation.id;

        const head = document.createElement('div');
        head.className = 'd-flex justify-content-between align-items-start gap-2';
        const what = document.createElement('div');
        const title = document.createElement('div');
        title.className = 'fw-semibold operation-title';
        title.textContent = operation.title;
        const where = document.createElement('div');
        where.className = 'small text-secondary';
        const link = document.createElement('a');
        link.href = operation.directory;
        link.className = 'operation-directory';
        link.textContent = decodeURIComponent(operation.directory);
        const by = document.createElement('span');
        by.className = 'operation-user';
        by.textContent = ' · ' + fillIn(texts.startedBy, { user: operation.user });
        where.append(link, by);
        what.append(title, where);

        const cancel = document.createElement('button');
        cancel.type = 'button';
        cancel.className = 'btn btn-sm btn-outline-danger operation-cancel';
        cancel.textContent = operation.cancelling ? texts.cancelling : texts.cancel;
        cancel.disabled = !operation.canCancel || operation.cancelling;
        cancel.title = operation.canCancel ? '' : texts.mayNotCancel;
        cancel.addEventListener('click', () => void cancelOperation(operation, cancel));
        head.append(what, cancel);

        // The bar (an hourglass while it is pending), and what it does now in one line
        const progress = document.createElement('div');
        progress.className = 'd-flex align-items-center gap-2 mt-2';
        const bar = document.createElement('div');
        bar.className = 'progress flex-grow-1';
        bar.setAttribute('role', 'progressbar');
        bar.setAttribute('aria-label', operation.title);
        const percent = Math.round(Math.min(1, Math.max(0, operation.progress || 0)) * 100);
        bar.setAttribute('aria-valuenow', String(percent));
        const fill = document.createElement('div');
        fill.className = 'progress-bar progress-bar-striped progress-bar-animated';
        fill.style.width = percent + '%';
        bar.appendChild(fill);
        progress.appendChild(bar);
        showPending(bar, operation.status);
        const detail = document.createElement('div');
        detail.className = 'small text-secondary text-truncate mt-1 operation-detail';
        detail.textContent = describeProgress(operation.status, operation.progress);
        detail.title = detail.textContent;

        row.append(head, progress, detail);
        return row;
    }

    async function cancelOperation(operation: RunningOperation, cancel: HTMLButtonElement): Promise<void> {
        cancel.disabled = true;
        cancel.textContent = texts.cancelling;
        try {
            const response = await fetch('/?operations=cancel', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ id: operation.id })
            });
            if (!response.ok)
                notify((await response.text()) || fillIn(texts.cancelFailed, { status: response.status }), 'danger');
        } catch {
            notify(fillIn(texts.cancelFailed, { status: 0 }), 'danger');
        }
        void refresh();
    }

    async function refresh(): Promise<void> {
        const operations = await fetchOperations();
        if (operations === null)
            return;
        const operationsButton = button();
        if (operationsButton)
            operationsButton.closest<HTMLElement>('[data-operations-item]')!.hidden = operations.length === 0;
        if (open) {
            list.replaceChildren(...operations.map(rowOf));
            nothing.hidden = operations.length > 0;
        }
    }

    function schedule(): void {
        clearTimeout(timer);
        if (document.visibilityState !== 'visible')
            return;
        timer = window.setTimeout(async () => {
            await refresh();
            schedule();
        }, open ? WHILE_OPEN_EVERY_MS : CHECK_EVERY_MS);
    }

    onClickOf('operations-button', function () {
        bootstrap.Modal.getOrCreateInstance(modal).show();
    });
    modal.addEventListener('show.bs.modal', function () {
        open = true;
        void refresh();
        schedule();
    });
    modal.addEventListener('hidden.bs.modal', function () {
        open = false;
        schedule();
    });
    document.addEventListener('visibilitychange', schedule);
    // (Layouts without the navbar's account part have no button: there is nothing to keep up to date then, unless the dialog is opened)
    document.addEventListener('DOMContentLoaded', function () {
        if (button())
            schedule();
    });
})();
