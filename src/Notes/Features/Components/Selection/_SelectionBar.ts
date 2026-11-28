// The card at the bottom of a directory listing, and all of the selecting (_SelectionBar.cshtml, which says what it is made of).
// What the selection is when the page is made comes from the server, in #selection-state, with what the selected entries are like for this
// browser (status: locked by a password, zipped, unzipped, unlocked; SelectionStatus), which every answer about the selection has too.
(function () {
    // What the selected entries need first, as they are for this browser: the addresses of the entries in each state
    interface SelectionStatus {
        locked: string[];
        unlock: string[];
        zipped: string[];
        unzipped: string[];
        unlocked: string[];
    }

    // The selection as the server last said it
    interface SelectionSnapshot {
        version: number;
        paths: string[];
        busy: boolean;
        canDelete: boolean;
        directoryCount: number;
        /** How far a running copy or move is (0 to 1). */
        progress?: number | null;
        status?: SelectionStatus;
    }

    // Where a drag over the rows is, and how the rows were before it
    interface Drag {
        start: number;
        current: number;
        select: boolean;
        initial: boolean[];
    }

    type TransferKind = 'copy' | 'move' | 'delete';

    const bar = document.getElementById('selection-bar')!;
    document.getElementById('floating-cards')!.append(bar); // last, so the messages that come in are above it
    const countText = document.getElementById('selection-count')!;
    const actions = document.getElementById('selection-actions')!;
    const copyButton = document.getElementById('selection-copy')!;
    const moveButton = document.getElementById('selection-move')!;
    const deleteButton = document.getElementById('selection-delete')!;
    const downloadButton = document.getElementById('selection-download')!;
    const unlockButton = document.getElementById('selection-unlock')!;
    const unzipButton = document.getElementById('selection-unzip')!;
    const zipButton = document.getElementById('selection-zip')!;
    const lockButton = document.getElementById('selection-lock')!;
    const KEEP_KEY = 'notes.selection-kept'; // the page is loaded again by the card itself: the selection stays
    const deleteModal = document.getElementById('selection-delete-modal')!;
    const spinner = document.getElementById('selection-spinner')!;
    const progress = document.getElementById('selection-progress')!;
    const clearButton = document.getElementById('selection-clear')!;
    const directory = bar.dataset.directory!;
    const createDeniedReason = bar.dataset.createDenied || null; // nothing may be copied or moved into this directory
    const rows = [...document.querySelectorAll<HTMLElement>('.entry-row')];
    const headers = [...document.querySelectorAll<HTMLInputElement>('.entry-select-all')];

    // What the server last said the selection is. Version and paths are only ever replaced as a whole by an answer of the server.
    let state: SelectionSnapshot = JSON.parse(document.getElementById('selection-state')!.textContent!);
    let selected = new Set<string>(state.paths);
    let pending = 0;          // requests to change the selection that are on their way
    let queue: Promise<void> = Promise.resolve();
    let isTransferring = false; // a copy or move started by this tab is running
    let pollTimer: number | null = null;

    const token = () => document.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')!.value;
    const isBusy = () => state.busy || isTransferring;
    const checkboxOf = (row: HTMLElement) => row.querySelector<HTMLInputElement>('.entry-select')!;
    const isChecked = (row: HTMLElement) => checkboxOf(row).checked;
    const parentOf = (path: string) => path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';

    // ---- Showing the selection ----

    const texts = textsOf('selection-texts');
    const BUSY_REASON = texts.busy;

    // The directories (lower case, as the server compares paths ignoring case) with something selected somewhere inside them. Taken from the
    // selected paths alone: every directory above a selected path, so nothing has to be listed or walked for it.
    let directoriesWithSelectedInside = new Set<string>();

    function findDirectoriesWithSelectedInside(): Set<string> {
        const directories = new Set<string>();
        for (const path of selected) {
            // From the directory the entry is in up to the top; once one is known, so are all above it
            for (let end = path.lastIndexOf('/'); end > 0; end = path.lastIndexOf('/', end - 1)) {
                const above = path.slice(0, end).toLowerCase();
                if (directories.has(above))
                    break;
                directories.add(above);
            }
        }
        return directories;
    }

    // A row's box: ticked if it is selected, a dash if it isn't but something inside it is
    function setRowChecked(row: HTMLElement, checked: boolean): void {
        const box = checkboxOf(row);
        box.checked = checked;
        box.indeterminate = !checked && directoriesWithSelectedInside.has(row.dataset.path!.toLowerCase());
        row.classList.toggle('table-active', checked);
    }

    function render(): void {
        directoriesWithSelectedInside = findDirectoriesWithSelectedInside();
        for (const row of rows) {
            setRowChecked(row, selected.has(row.dataset.path!));
            checkboxOf(row).disabled = isBusy();
            row.title = isBusy() ? BUSY_REASON : '';
        }
        renderHeaders();
        renderStatus();

        const count = selected.size;

        // Selected in this very directory: there is nothing to copy or move here, so the card offers Delete instead. Otherwise it is Copy and Move.
        const allHere = count > 0 && [...selected].every(path => parentOf(path) === directory);
        setAvailable(copyButton, !allHere);
        setAvailable(moveButton, !allHere);
        setAvailable(deleteButton, allHere);
        setAvailable(downloadButton, count > 0); // everything selected may be read, which is all a download needs

        // What the selected entries need first, as they are for this browser: what a password locks can only be unlocked (nothing else can be
        // done with it, the server refuses it); otherwise what is zipped can be unzipped, and what is unzipped or unlocked zipped or locked
        const status = statusOf();
        const locked = status.locked.length > 0;
        setAvailable(unlockButton, locked);
        setLabel(unlockButton, status.unlock.length === 1 ? texts.unlockOne : texts.unlockAll);
        setAvailable(unzipButton, !locked && status.zipped.length > 0);
        setLabel(unzipButton, status.zipped.length === 1 ? texts.unzipOne : texts.unzipAll);
        setAvailable(zipButton, status.unzipped.length > 0);
        setLabel(zipButton, status.unzipped.length === 1 ? texts.zipOne : texts.zipAll);
        setAvailable(lockButton, status.unlocked.length > 0);
        setLabel(lockButton, status.unlocked.length === 1 ? texts.lockOne : texts.lockAll);

        // What the permissions allow for the selected entries themselves, as far as the server knew when they were selected. What is inside
        // a directory is checked only when it is copied, moved or deleted (and the answer says what is wrong). Moving takes the entries
        // away from where they are, so it needs what deleting needs.
        const lockedReason = locked ? texts.unlockFirst : null;
        const removeDeniedReason = state.canDelete ? null : texts.cantDeleteOrMove;
        setButtonDisabled(copyButton, lockedReason || createDeniedReason);
        setButtonDisabled(moveButton, lockedReason || createDeniedReason || removeDeniedReason);
        setButtonDisabled(deleteButton, lockedReason || removeDeniedReason);
        setButtonDisabled(downloadButton, lockedReason);
    }

    // (A page made before there was a status, or an answer without one, has nothing special selected)
    const statusOf = (): SelectionStatus => state.status || { locked: [], unlock: [], zipped: [], unzipped: [], unlocked: [] };

    function setLabel(button: HTMLElement, label: string): void {
        button.dataset.label = label;
        button.querySelector('.selection-label')!.textContent = label;
        refreshLabel(button);
    }
    const refreshLabel = (button: HTMLElement) => setButtonDisabled(button, button.dataset.disabledReason || null);

    // The top of the card: how many are selected. The boxes show a change at once, but the card shows what the server knows, so
    // until its answer is there the card is only a spinner (and is there even if nothing was selected yet).
    function renderStatus(): void {
        const count = selected.size;
        const isWaiting = pending > 0;
        // Kept while this tab's transfer is running even once the server cleared the selection, until its answer is there
        bar.hidden = count === 0 && !isWaiting && !isBusy();
        countText.classList.toggle('d-none', isWaiting); // only the spinner until the answer is there
        if (!isWaiting) {
            // Those a password locks are said first, as they are all that can be done anything with now
            const locked = statusOf().locked.length;
            const others = count - locked;
            countText.textContent = locked === 0 ? (count === 1 ? texts.oneSelected : fillIn(texts.manySelected, { count }))
                : (locked === 1 ? texts.oneLockedSelected : fillIn(texts.manyLockedSelected, { count: locked }))
                  + (others === 0 ? '' : others === 1 ? texts.andOneOther : fillIn(texts.andManyOthers, { count: others }));
            countText.dataset.count = String(count);
            countText.dataset.locked = String(locked);
        }
        // What the buttons would act on is not known until the answer is there
        actions.classList.toggle('d-none', isBusy() || isWaiting);
        setAvailable(clearButton, !isBusy() && !isWaiting && count > 0); // and Escape clears the selection only while there is one
        spinner.classList.toggle('d-none', isBusy() || !isWaiting);
        renderProgress();
    }

    // What the server last said about the running transfer. Once it is over the bar stays as it was, as the page reloads anyway.
    function renderProgress(): void {
        progress.classList.toggle('d-none', !isBusy());
        if (state.progress === null || state.progress === undefined)
            return;
        const percent = Math.round(state.progress * 100);
        (progress.firstElementChild as HTMLElement).style.width = percent + '%';
        progress.setAttribute('aria-valuenow', String(percent));
    }

    // Shows or hides one of the card's buttons. Its shortcut only exists while it is shown (a hidden button's would still work).
    function setAvailable(button: HTMLElement, available: boolean): void {
        button.classList.toggle('d-none', !available);
        if (available && button.dataset.key)
            button.setAttribute('key', button.dataset.key);
        else
            button.removeAttribute('key');
    }

    // The box above a group of entries: checked if all are selected, a dash if only some
    function renderHeaders(): void {
        for (const header of headers) {
            const group = rows.filter(row => row.dataset.group === header.dataset.group);
            const count = group.filter(row => isChecked(row)).length;
            header.checked = count > 0 && count === group.length;
            header.indeterminate = count > 0 && count < group.length;
            header.disabled = isBusy() || group.length === 0;
            header.closest<HTMLElement>('th, .media-group-header')!.title = isBusy() ? BUSY_REASON : group.length === 0 ? texts.nothingToSelect : '';
        }
    }

    // Adopts the selection the server answered with, unless a newer change of this tab is on its way (which would make it flicker)
    function adopt(snapshot: SelectionSnapshot): void {
        if (snapshot.version < state.version)
            return;
        state = snapshot;
        selected = new Set(snapshot.paths);
        if (pending === 0)
            render();
        if (state.busy && !isTransferring)
            startPolling();
    }

    // ---- Talking to the server ----

    async function sync(): Promise<void> {
        try {
            const response = await fetch(window.location.pathname + '?selection', { headers: { 'Accept': 'application/json' } });
            if (response.ok)
                adopt(await response.json());
        } catch { /* offline; the next try will do */ }
    }

    // Changes are sent one after the other, so they arrive in the order they were made
    function send(action: string, paths: string[]): Promise<void> {
        pending++;
        renderStatus();
        queue = queue.then(async () => {
            try {
                const response = await fetch(window.location.pathname + '?selection=' + action, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ paths })
                });
                pending--;
                if (response.ok)
                    adopt(await response.json());
                else
                    await sync(); // e.g. a transfer is running in another tab: show what is true
            } catch {
                pending--;
                await sync();
            }
            if (pending === 0)
                render();
        });
        return queue;
    }

    const change = (paths: string[], select: boolean): Promise<void> => paths.length === 0 ? Promise.resolve() : send(select ? 'add' : 'remove', paths);

    // While a transfer started elsewhere (another tab, or this page before it was reloaded) runs, wait for it and show the result
    function startPolling(): void {
        if (pollTimer !== null)
            return;
        render();
        pollTimer = window.setInterval(async function () {
            await sync();
            if (state.busy)
                return;

            clearInterval(pollTimer!);
            pollTimer = null;
            // A success cleared the selection and changed the listing. After a failure the selection is still there, and a reload would clear it.
            if (selected.size === 0)
                window.location.reload();
            else
                render();
        }, 1500);
    }

    async function transfer(kind: TransferKind): Promise<void> {
        if (isBusy() || selected.size === 0)
            return;

        const transferred = [...selected];
        isTransferring = true;
        state.progress = 0;
        render();
        // The answer only comes when it is done; meanwhile the selection says how far it is (on the card, which stays above what covers the
        // page), and the page can't be used or left, so that the answer is not lost
        const progressTimer = setInterval(sync, 500);
        const { leftOutUnreadable, message } = await whileBusy(async function (): Promise<{ message: string | null; leftOutUnreadable?: boolean }> {
            try {
                const response = await fetch(window.location.pathname + '?transfer=' + kind, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ version: state.version })
                });
                if (response.ok)
                    return { leftOutUnreadable: (await response.json().catch(() => ({}))).leftOutUnreadable, message: null };
                return { message: (await response.text()) || fillIn(texts.failed, { status: response.status }) };
            } catch {
                return { message: { copy: texts.copyConnectionLost, move: texts.moveConnectionLost, delete: texts.deleteConnectionLost }[kind] };
            } finally {
                clearInterval(progressTimer);
            }
        }, { blockPage: true });

        if (message === null) {
            // Done, and the selection is cleared. Reloading shows the new contents.
            const done = { copy: texts.copiedHere, move: texts.movedHere, delete: texts.deleted }[kind];
            notifyAfterNavigation(fillIn(done, {
                elements: transferred.length === 1 ? texts.oneElement : fillIn(texts.manyElements, { count: transferred.length }),
                except: leftOutUnreadable ? texts.exceptUnreadable : ''
            }));

            // This very directory may be gone with what was deleted: go to where the topmost deleted entry was
            const gone = kind === 'delete'
                ? transferred.filter(p => directory === p || directory.startsWith(p + '/')).sort((a, b) => a.length - b.length)[0]
                : undefined;
            if (gone !== undefined)
                window.location.href = '/' + parentOf(gone).split('/').map(encodeURIComponent).join('/');
            else
                window.location.reload();
            return;
        }

        isTransferring = false;
        await sync();
        render();
        notify(message, 'danger');
    }

    // ---- Using the boxes ----

    // Whether the row's box shows a dash: it isn't selected, but something inside it is
    const isDashed = (row: HTMLElement) => !selected.has(row.dataset.path!) && directoriesWithSelectedInside.has(row.dataset.path!.toLowerCase());

    // A dash means some of it is selected, so (like the box above a group) using it unselects all of it: everything selected inside the
    // directory, at any depth. Using it again then selects the directory itself.
    function unselectInside(row: HTMLElement): void {
        const prefix = row.dataset.path!.toLowerCase() + '/';
        const inside = [...selected].filter(path => path.toLowerCase().startsWith(prefix));
        const box = checkboxOf(row);
        box.checked = false;
        box.indeterminate = false; // until the server's answer is there, which makes the boxes as it says
        void change(inside, false);
    }

    // A box of a single entry (clicked, or Space/shortcut while focused)
    for (const row of rows)
        checkboxOf(row).addEventListener('change', function () {
            // The browser has ticked it already, so whether it had a dash is told by the selection
            if (isDashed(row)) {
                unselectInside(row);
                return;
            }
            const checked = checkboxOf(row).checked;
            setRowChecked(row, checked); // unticked again, it gets its dash back
            renderHeaders();
            void change([row.dataset.path!], checked);
        });

    // The box above a group selects all of it, or, if any of it is selected already (a tick or a dash), none of it
    for (const header of headers) {
        const group = rows.filter(row => row.dataset.group === header.dataset.group);
        header.addEventListener('change', function () {
            const select = !group.some(isChecked);
            const affected = group.filter(row => isChecked(row) !== select);
            for (const row of affected)
                setRowChecked(row, select);
            renderHeaders();
            void change(affected.map(row => row.dataset.path!), select);
        });

        // ...and so does a click beside it
        header.closest<HTMLElement>('th, .media-group-header')!.addEventListener('click', function (e) {
            if (e.target !== header && !isBusy() && group.length > 0)
                header.click();
        });
    }

    // Pressing on the background of a row selects it (or unselects it if it was selected), and dragging over more rows does
    // the same to them. Rows the pointer leaves again go back to how they were. The change is sent when the button is released.
    let drag: Drag | null = null;

    function applyDrag(dragged: Drag): void {
        const [from, to] = [Math.min(dragged.start, dragged.current), Math.max(dragged.start, dragged.current)];
        rows.forEach(function (row, i) {
            setRowChecked(row, i >= from && i <= to ? dragged.select : dragged.initial[i]);
        });
        renderHeaders();
    }

    document.addEventListener('mousedown', function (e) {
        if (e.button !== 0 || isBusy())
            return;
        const row = e.target instanceof Element ? e.target.closest<HTMLElement>('.entry-row') : null;
        // Links, boxes and the like do their own thing
        if (!row || (e.target as Element).closest('a, input, button, label'))
            return;

        e.preventDefault(); // no text highlighting while dragging
        // Pressing on a row with a dash does what its box does, and drags nothing
        if (isDashed(row)) {
            unselectInside(row);
            return;
        }
        const start = rows.indexOf(row);
        drag = { start, current: start, select: !isChecked(row), initial: rows.map(isChecked) };
        applyDrag(drag);
    });

    document.addEventListener('mousemove', function (e) {
        if (drag === null)
            return;
        const element = document.elementFromPoint(e.clientX, e.clientY);
        const hovered = element ? element.closest<HTMLElement>('.entry-row') : null;
        const index = hovered ? rows.indexOf(hovered) : -1;
        if (index >= 0 && index !== drag.current) {
            drag.current = index;
            applyDrag(drag);
        }
    });

    document.addEventListener('mouseup', function () {
        if (drag === null)
            return;
        const { select, initial } = drag;
        const changed = rows.filter((row, i) => isChecked(row) !== initial[i]).map(row => row.dataset.path!);
        drag = null;
        void change(changed, select);
    });

    clearButton.addEventListener('click', function () {
        if (!isBusy())
            void send('clear', []);
    });
    copyButton.addEventListener('click', () => void transfer('copy'));
    moveButton.addEventListener('click', () => void transfer('move'));
    deleteButton.addEventListener('click', async function () {
        await queue; // the counts come from the server, so wait for the changes still on their way
        if (isBusy() || selected.size === 0)
            return;
        // "2 directories and 1 file"; what is inside the directories is only mentioned if there are any
        const directories = state.directoryCount;
        const files = selected.size - directories;
        const parts: string[] = [];
        if (directories > 0)
            parts.push(directories === 1 ? texts.oneDirectory : fillIn(texts.manyDirectories, { count: directories }));
        if (files > 0)
            parts.push(files === 1 ? texts.oneFile : fillIn(texts.manyFiles, { count: files }));
        const countElement = document.getElementById('selection-delete-count')!;
        countElement.textContent = parts.length === 2 ? fillIn(texts.directoriesAndFiles, { directories: parts[0], files: parts[1] }) : parts[0];
        countElement.dataset.directories = String(directories);
        countElement.dataset.files = String(files);
        document.getElementById('selection-delete-inside')!.textContent = directories > 0
            ? (directories === 1 ? texts.insideOneDirectory : texts.insideManyDirectories)
            : '';
        bootstrap.Modal.getOrCreateInstance(deleteModal).show();
    });
    // One file is downloaded as it is; more than one entry, or a directory, as an archive, made as the dialog asks
    downloadButton.addEventListener('click', async function () {
        await queue; // what is selected comes from the server, so wait for the changes still on their way
        if (isBusy() || selected.size === 0)
            return;
        if (selected.size === 1 && state.directoryCount === 0) {
            const link = document.createElement('a');
            link.href = '/' + [...selected][0].split('/').map(encodeURIComponent).join('/') + '?download';
            link.download = '';
            document.body.append(link);
            link.click();
            link.remove();
            return;
        }
        void archiveDownload.open({ kind: 'selection', version: state.version });
    });
    document.getElementById('selection-delete-confirm')!.addEventListener('click', function () {
        bootstrap.Modal.getOrCreateInstance(deleteModal).hide();
        void transfer('delete');
    });

    // ---- What the selected entries need first (SelectionStatus) ----

    // The page is loaded again to show how they are now (their marks, which can be opened), with the selection as it is
    function reloadKeepingSelection(message?: string, kind?: string): void {
        if (message)
            notifyAfterNavigation(message, kind || 'info');
        try {
            sessionStorage.setItem(KEEP_KEY, '1');
        } catch { }
        window.location.reload();
    }

    // Asks for each password in turn (the dialog of _PasswordModal.ts), the outermost first; closing the dialog stops there
    unlockButton.addEventListener('click', async function () {
        await queue;
        // (Asked of the server again after each: a folder above may have unlocked more than its own entry)
        let unlocked = 0;
        for (let round = 0; round < 50 && statusOf().unlock.length > 0; round++) {
            if (!await passwordPrompt.ask(statusOf().unlock[0]))
                break;
            unlocked++;
            await sync();
        }
        if (unlocked > 0)
            reloadKeepingSelection(texts.unlocked, 'success');
    });

    // Unzips them one after the other, covering the page with how far each got
    unzipButton.addEventListener('click', async function () {
        await queue;
        const addresses = [...statusOf().zipped];
        for (let i = 0; i < addresses.length; i++) {
            const entry = decodeURIComponent(addresses[i]).replace(/^\//, '');
            const failure = await whileBusy(setProgress => {
                setProgress(0);
                return archiveUnzipping.waitUntilUnzipped(addresses[i], setProgress);
            }, { blockPage: true, message: fillIn(texts.unzippingOf, { entry, number: i + 1, count: addresses.length }) });
            if (failure !== null) {
                notify(failure, 'danger');
                return;
            }
        }
        reloadKeepingSelection(texts.unzipped, 'success');
    });

    // Zipping (?zip=entry) and locking (?lock=entry) are asked of each; the server answers at once (zipping goes on, with its bar)
    async function postToEach(button: HTMLElement, addresses: string[], query: string, message: string): Promise<void> {
        await queue;
        setButtonBusy(button, true);
        for (const address of addresses) {
            try {
                const response = await fetch(address + query, { method: 'POST', headers: { 'RequestVerificationToken': token() } });
                if (!response.ok) {
                    setButtonBusy(button, false);
                    notify((await response.text()) || fillIn(texts.failed, { status: response.status }), 'danger');
                    return;
                }
            } catch {
                setButtonBusy(button, false);
                notify(fillIn(texts.failed, { status: 0 }), 'danger');
                return;
            }
        }
        reloadKeepingSelection(message);
    }
    zipButton.addEventListener('click', () => void postToEach(zipButton, [...statusOf().unzipped], '?zip=entry', texts.zipping));
    lockButton.addEventListener('click', () => void postToEach(lockButton, [...statusOf().unlocked], '?lock=entry', texts.locked));

    // ---- Keeping up ----

    // Another tab of this user may have changed the selection (or started a transfer) meanwhile
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible' && !isTransferring)
            void sync();
    });
    // Coming back through the back button shows the page as it was left, which may be outdated
    window.addEventListener('pageshow', function (e) {
        if (e.persisted)
            void sync();
    });

    // A manual reload starts afresh. (A transfer finishing reloads as well, but then there is nothing left to clear.)
    // (Not when the card itself loaded it again, to show what it did to the selected entries)
    let keptByCard = false;
    try {
        keptByCard = sessionStorage.getItem(KEEP_KEY) === '1';
        sessionStorage.removeItem(KEEP_KEY);
    } catch { }
    const navigation = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined;
    if (navigation && navigation.type === 'reload' && selected.size > 0 && !state.busy && !keptByCard) {
        selected = new Set();
        void send('clear', []);
    }

    render();
    if (state.busy)
        startPolling();
})();
