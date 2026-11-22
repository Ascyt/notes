// The card at the bottom of a directory listing, and all of the selecting (_SelectionBar.cshtml, which says what it is made of).
// What the selection is when the page is made comes from the server, in #selection-state.
(function () {
    const bar = document.getElementById('selection-bar');
    document.getElementById('floating-cards').append(bar); // last, so the messages that come in are above it
    const countText = document.getElementById('selection-count');
    const actions = document.getElementById('selection-actions');
    const copyButton = document.getElementById('selection-copy');
    const moveButton = document.getElementById('selection-move');
    const deleteButton = document.getElementById('selection-delete');
    const downloadButton = document.getElementById('selection-download');
    const deleteModal = document.getElementById('selection-delete-modal');
    const spinner = document.getElementById('selection-spinner');
    const progress = document.getElementById('selection-progress');
    const clearButton = document.getElementById('selection-clear');
    const directory = bar.dataset.directory;
    const createDeniedReason = bar.dataset.createDenied || null; // nothing may be copied or moved into this directory
    const rows = [...document.querySelectorAll('.entry-row')];
    const headers = [...document.querySelectorAll('.entry-select-all')];

    // What the server last said the selection is. Version and paths are only ever replaced as a whole by an answer of the server.
    let state = JSON.parse(document.getElementById('selection-state').textContent);
    let selected = new Set(state.paths);
    let pending = 0;          // requests to change the selection that are on their way
    let queue = Promise.resolve();
    let isTransferring = false; // a copy or move started by this tab is running
    let pollTimer = null;

    const token = () => document.querySelector('input[name="__RequestVerificationToken"]').value;
    const isBusy = () => state.busy || isTransferring;
    const checkboxOf = row => row.querySelector('.entry-select');
    const isChecked = row => checkboxOf(row).checked;
    const parentOf = path => path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';

    // ---- Showing the selection ----

    const texts = textsOf('selection-texts');
    const BUSY_REASON = texts.busy;

    // The directories (lower case, as the server compares paths ignoring case) with something selected somewhere inside them. Taken from the
    // selected paths alone: every directory above a selected path, so nothing has to be listed or walked for it.
    let directoriesWithSelectedInside = new Set();

    function findDirectoriesWithSelectedInside() {
        const directories = new Set();
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
    function setRowChecked(row, checked) {
        const box = checkboxOf(row);
        box.checked = checked;
        box.indeterminate = !checked && directoriesWithSelectedInside.has(row.dataset.path.toLowerCase());
        row.classList.toggle('table-active', checked);
    }

    function render() {
        directoriesWithSelectedInside = findDirectoriesWithSelectedInside();
        for (const row of rows) {
            setRowChecked(row, selected.has(row.dataset.path));
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

        // What the permissions allow for the selected entries themselves, as far as the server knew when they were selected. What is inside
        // a directory is checked only when it is copied, moved or deleted (and the answer says what is wrong). Moving takes the entries
        // away from where they are, so it needs what deleting needs.
        const removeDeniedReason = state.canDelete ? null : texts.cantDeleteOrMove;
        setButtonDisabled(copyButton, createDeniedReason);
        setButtonDisabled(moveButton, createDeniedReason || removeDeniedReason);
        setButtonDisabled(deleteButton, removeDeniedReason);
    }

    // The top of the card: how many are selected. The boxes show a change at once, but the card shows what the server knows, so
    // until its answer is there the card is only a spinner (and is there even if nothing was selected yet).
    function renderStatus() {
        const count = selected.size;
        const isWaiting = pending > 0;
        // Kept while this tab's transfer is running even once the server cleared the selection, until its answer is there
        bar.hidden = count === 0 && !isWaiting && !isBusy();
        countText.classList.toggle('d-none', isWaiting); // only the spinner until the answer is there
        if (!isWaiting) {
            countText.textContent = count === 1 ? texts.oneSelected : fillIn(texts.manySelected, { count });
            countText.dataset.count = count;
        }
        // What the buttons would act on is not known until the answer is there
        actions.classList.toggle('d-none', isBusy() || isWaiting);
        setAvailable(clearButton, !isBusy() && !isWaiting && count > 0); // and Escape clears the selection only while there is one
        spinner.classList.toggle('d-none', isBusy() || !isWaiting);
        renderProgress();
    }

    // What the server last said about the running transfer. Once it is over the bar stays as it was, as the page reloads anyway.
    function renderProgress() {
        progress.classList.toggle('d-none', !isBusy());
        if (state.progress === null || state.progress === undefined)
            return;
        const percent = Math.round(state.progress * 100);
        progress.firstElementChild.style.width = percent + '%';
        progress.setAttribute('aria-valuenow', percent);
    }

    // Shows or hides one of the card's buttons. Its shortcut only exists while it is shown (a hidden button's would still work).
    function setAvailable(button, available) {
        button.classList.toggle('d-none', !available);
        if (available)
            button.setAttribute('key', button.dataset.key);
        else
            button.removeAttribute('key');
    }

    // The box above a group of entries: checked if all are selected, a dash if only some
    function renderHeaders() {
        for (const header of headers) {
            const group = rows.filter(row => row.dataset.group === header.dataset.group);
            const count = group.filter(row => isChecked(row)).length;
            header.checked = count > 0 && count === group.length;
            header.indeterminate = count > 0 && count < group.length;
            header.disabled = isBusy() || group.length === 0;
            header.closest('th, .media-group-header').title = isBusy() ? BUSY_REASON : group.length === 0 ? texts.nothingToSelect : '';
        }
    }

    // Adopts the selection the server answered with, unless a newer change of this tab is on its way (which would make it flicker)
    function adopt(snapshot) {
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

    async function sync() {
        try {
            const response = await fetch(window.location.pathname + '?selection', { headers: { 'Accept': 'application/json' } });
            if (response.ok)
                adopt(await response.json());
        } catch { /* offline; the next try will do */ }
    }

    // Changes are sent one after the other, so they arrive in the order they were made
    function send(action, paths) {
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

    const change = (paths, select) => paths.length === 0 ? Promise.resolve() : send(select ? 'add' : 'remove', paths);

    // While a transfer started elsewhere (another tab, or this page before it was reloaded) runs, wait for it and show the result
    function startPolling() {
        if (pollTimer !== null)
            return;
        render();
        pollTimer = setInterval(async function () {
            await sync();
            if (state.busy)
                return;

            clearInterval(pollTimer);
            pollTimer = null;
            // A success cleared the selection and changed the listing. After a failure the selection is still there, and a reload would clear it.
            if (selected.size === 0)
                window.location.reload();
            else
                render();
        }, 1500);
    }

    async function transfer(kind) {
        if (isBusy() || selected.size === 0)
            return;

        const transferred = [...selected];
        isTransferring = true;
        state.progress = 0;
        render();
        // The answer only comes when it is done; meanwhile the selection says how far it is (on the card, which stays above what covers the
        // page), and the page can't be used or left, so that the answer is not lost
        const progressTimer = setInterval(sync, 500);
        const { leftOutUnreadable, message } = await whileBusy(async function () {
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
    const isDashed = row => !selected.has(row.dataset.path) && directoriesWithSelectedInside.has(row.dataset.path.toLowerCase());

    // A dash means some of it is selected, so (like the box above a group) using it unselects all of it: everything selected inside the
    // directory, at any depth. Using it again then selects the directory itself.
    function unselectInside(row) {
        const prefix = row.dataset.path.toLowerCase() + '/';
        const inside = [...selected].filter(path => path.toLowerCase().startsWith(prefix));
        const box = checkboxOf(row);
        box.checked = false;
        box.indeterminate = false; // until the server's answer is there, which makes the boxes as it says
        change(inside, false);
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
            change([row.dataset.path], checked);
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
            change(affected.map(row => row.dataset.path), select);
        });

        // ...and so does a click beside it
        header.closest('th, .media-group-header').addEventListener('click', function (e) {
            if (e.target !== header && !isBusy() && group.length > 0)
                header.click();
        });
    }

    // Pressing on the background of a row selects it (or unselects it if it was selected), and dragging over more rows does
    // the same to them. Rows the pointer leaves again go back to how they were. The change is sent when the button is released.
    let drag = null;

    function applyDrag() {
        const [from, to] = [Math.min(drag.start, drag.current), Math.max(drag.start, drag.current)];
        rows.forEach(function (row, i) {
            setRowChecked(row, i >= from && i <= to ? drag.select : drag.initial[i]);
        });
        renderHeaders();
    }

    document.addEventListener('mousedown', function (e) {
        if (e.button !== 0 || isBusy())
            return;
        const row = e.target.closest && e.target.closest('.entry-row');
        // Links, boxes and the like do their own thing
        if (!row || e.target.closest('a, input, button, label'))
            return;

        e.preventDefault(); // no text highlighting while dragging
        // Pressing on a row with a dash does what its box does, and drags nothing
        if (isDashed(row)) {
            unselectInside(row);
            return;
        }
        const start = rows.indexOf(row);
        drag = { start, current: start, select: !isChecked(row), initial: rows.map(isChecked) };
        applyDrag();
    });

    document.addEventListener('mousemove', function (e) {
        if (drag === null)
            return;
        const element = document.elementFromPoint(e.clientX, e.clientY);
        const index = rows.indexOf(element && element.closest('.entry-row'));
        if (index >= 0 && index !== drag.current) {
            drag.current = index;
            applyDrag();
        }
    });

    document.addEventListener('mouseup', function () {
        if (drag === null)
            return;
        const { select, initial } = drag;
        const changed = rows.filter((row, i) => isChecked(row) !== initial[i]).map(row => row.dataset.path);
        drag = null;
        change(changed, select);
    });

    clearButton.addEventListener('click', function () {
        if (!isBusy())
            send('clear', []);
    });
    copyButton.addEventListener('click', () => transfer('copy'));
    moveButton.addEventListener('click', () => transfer('move'));
    deleteButton.addEventListener('click', async function () {
        await queue; // the counts come from the server, so wait for the changes still on their way
        if (isBusy() || selected.size === 0)
            return;
        // "2 directories and 1 file"; what is inside the directories is only mentioned if there are any
        const directories = state.directoryCount;
        const files = selected.size - directories;
        const parts = [];
        if (directories > 0)
            parts.push(directories === 1 ? texts.oneDirectory : fillIn(texts.manyDirectories, { count: directories }));
        if (files > 0)
            parts.push(files === 1 ? texts.oneFile : fillIn(texts.manyFiles, { count: files }));
        const countElement = document.getElementById('selection-delete-count');
        countElement.textContent = parts.length === 2 ? fillIn(texts.directoriesAndFiles, { directories: parts[0], files: parts[1] }) : parts[0];
        countElement.dataset.directories = directories;
        countElement.dataset.files = files;
        document.getElementById('selection-delete-inside').textContent = directories > 0
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
        archiveDownload.open({ kind: 'selection', version: state.version });
    });
    document.getElementById('selection-delete-confirm').addEventListener('click', function () {
        bootstrap.Modal.getOrCreateInstance(deleteModal).hide();
        transfer('delete');
    });

    // ---- Keeping up ----

    // Another tab of this user may have changed the selection (or started a transfer) meanwhile
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible' && !isTransferring)
            sync();
    });
    // Coming back through the back button shows the page as it was left, which may be outdated
    window.addEventListener('pageshow', function (e) {
        if (e.persisted)
            sync();
    });

    // A manual reload starts afresh. (A transfer finishing reloads as well, but then there is nothing left to clear.)
    const navigation = performance.getEntriesByType('navigation')[0];
    if (navigation && navigation.type === 'reload' && selected.size > 0 && !state.busy) {
        selected = new Set();
        send('clear', []);
    }

    render();
    if (state.busy)
        startPolling();
})();
