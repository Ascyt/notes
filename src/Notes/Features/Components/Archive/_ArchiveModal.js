// The dialog that asks how to make an archive (_ArchiveModal.cshtml), and the download with what it asks:
//
//   archiveDownload.open({ kind: 'directory' })                      the directory of the page
//   archiveDownload.open({ kind: 'selection', version: 1234 })        the selection, as the page showed it (its version)
//
// The archive is made by the server first (POST ?archive=…, with the password in the body, never in an address) while the page is covered,
// and then downloaded (GET ?archive=<id>). The format and compression chosen last are kept in the browser; the password never is.
//
// The archived folders in what is downloaded are asked for when it opens (GET ?archive=folders or ?archive=selection-folders). For an admin,
// each has a box, not ticked at first: not ticked, it goes in as it is kept (its configuration and its archive, sent as "asIs"); ticked, it is
// unzipped and what is in it goes in. One inside a folder that goes in as it is kept is part of that archive: its box is disabled. If the
// directory itself goes in as it is kept, the download is its archive (POST ?archive=as-is), and the options are greyed out. Everyone else
// is told that the folders are unzipped for it.
(function () {
    const modal = document.getElementById('archive-modal');
    const form = document.getElementById('archive-form');
    const compression = document.getElementById('archive-compression');
    const storeOption = document.getElementById('archive-compression-store');
    const minOption = document.getElementById('archive-compression-min');
    const password = document.getElementById('archive-password');
    const passwordToggle = document.getElementById('archive-password-toggle');
    const options = document.getElementById('archive-options');
    const foldersSection = document.getElementById('archive-folders');
    const foldersAdmin = document.getElementById('archive-folders-admin');
    const foldersUser = document.getElementById('archive-folders-user');
    const foldersWarning = document.getElementById('archive-folders-warning');
    const folderList = document.getElementById('archive-folder-list');
    const unzipAll = document.getElementById('archive-unzip-all');
    const wholeAsIs = document.getElementById('archive-whole-as-is');
    const texts = textsOf('archive-texts');
    const CHOICE_KEY = 'notes.archive-options';

    let request = null; // what the dialog is open for
    let folders = [];   // the archived folders in it, each { path, label, zipped, encrypted, inside, box, note } (admins only)

    const token = () => document.querySelector('input[name="__RequestVerificationToken"]').value;
    const chosenFormat = () => form.querySelector('input[name="archive-format"]:checked').value;
    const samePath = (a, b) => a.toLowerCase() === b.toLowerCase();

    // The directory of the page, as the server names paths: without slashes at either end, "" for the root
    const pagePath = () => decodeURIComponent(window.location.pathname).replace(/^\/+|\/+$/g, '');

    // The lowest level is the format's: a zip can be stored without compression ("store"), a 7z only compressed as little as can be
    // ("min", as the 7z writer always compresses). Only that one is offered, and a choice of the other becomes it.
    function updateCompressionChoices() {
        const isSevenZip = chosenFormat() === '7z';
        const [offered, other] = isSevenZip ? [minOption, storeOption] : [storeOption, minOption];
        const hadOther = compression.value === other.value;
        offered.hidden = offered.disabled = false;
        other.hidden = other.disabled = true;
        if (hadOther)
            compression.value = offered.value;
    }

    function restoreChoice() {
        try {
            const { format, level } = JSON.parse(localStorage.getItem(CHOICE_KEY) || '{}');
            const radio = form.querySelector(`input[name="archive-format"][value="${format}"]`);
            if (radio)
                radio.checked = true;
            if ([...compression.options].some(option => option.value === level))
                compression.value = level;
        } catch { /* nothing kept, or storage not allowed: the defaults of the dialog */ }
        updateCompressionChoices();
    }

    function keepChoice() {
        try {
            localStorage.setItem(CHOICE_KEY, JSON.stringify({ format: chosenFormat(), level: compression.value }));
        } catch { /* storage not allowed: chosen again next time */ }
    }

    // ---- The archived folders

    const folderOf = path => folders.find(folder => samePath(folder.path, path));

    // The folder kept as it is that holds it (the outermost, as that one's archive is what goes in), or null
    function keptAround(folder) {
        let kept = null;
        for (let outer = folder.inside && folderOf(folder.inside); outer; outer = outer.inside && folderOf(outer.inside))
            if (!outer.box.checked)
                kept = outer;
        return kept;
    }

    // The folders that go in as they are kept: not ticked, and not inside another that is
    const keptAsIs = () => folders.filter(folder => !folder.box.checked && keptAround(folder) === null);

    // Whether the download is the directory's own archive: it is one of them, and goes in as it is kept
    const downloadsWholeAsIs = () => request !== null && request.kind === 'directory'
        && keptAsIs().some(folder => samePath(folder.path, pagePath()));

    function updateFolders() {
        for (const folder of folders) {
            const kept = keptAround(folder);
            folder.box.disabled = kept !== null;
            const notes = [];
            if (folder.zipped)
                notes.push(texts.zippedNow);
            if (folder.encrypted)
                notes.push(texts.encrypted);
            if (kept !== null)
                notes.push(fillIn(texts.insideKept, { folder: kept.label }));
            folder.note.textContent = notes.length > 0 ? ' (' + notes.join(', ') + ')' : '';
        }
        // The box above them all: ticked if all are, a dash if some are
        const ticked = folders.filter(folder => folder.box.checked).length;
        unzipAll.checked = folders.length > 0 && ticked === folders.length;
        unzipAll.indeterminate = ticked > 0 && ticked < folders.length;

        const whole = downloadsWholeAsIs();
        options.disabled = whole;
        wholeAsIs.hidden = !whole;
    }

    function showFolders(answer) {
        folderList.replaceChildren();
        folders = [];
        const found = answer && Array.isArray(answer.folders) ? answer.folders : [];
        foldersSection.hidden = found.length === 0;
        foldersAdmin.hidden = !(answer && answer.keepsAsIs);
        foldersUser.hidden = found.length === 0 || (answer && answer.keepsAsIs);
        if (found.length === 0)
            return;

        if (!answer.keepsAsIs) {
            foldersWarning.textContent = fillIn(texts.unzippedForIt, { count: found.length });
            return;
        }
        found.forEach(function (found, i) {
            const row = document.createElement('div');
            row.className = 'form-check';
            const box = document.createElement('input');
            box.type = 'checkbox';
            box.className = 'form-check-input archive-folder-unzip';
            box.id = 'archive-folder-' + i;
            box.dataset.path = found.path;
            const label = document.createElement('label');
            label.className = 'form-check-label text-break';
            label.htmlFor = box.id;
            const icon = document.createElement('i');
            icon.className = (found.encrypted ? 'fa-solid fa-shield' : 'fa-solid fa-file-zipper') + ' fa-xs me-1 text-secondary';
            const name = document.createElement('span');
            name.textContent = fillIn(texts.unzipFolder, { folder: found.label });
            const note = document.createElement('span');
            note.className = 'text-secondary small';
            label.append(icon, name, note);
            row.append(box, label);
            folderList.append(row);
            folders.push({ ...found, box, note });
        });
    }

    async function loadFolders(what) {
        const value = what.kind === 'selection' ? 'selection-folders' : 'folders';
        try {
            const response = await fetch(window.location.pathname + '?archive=' + value, { headers: { 'Accept': 'application/json' } });
            return response.ok ? await response.json() : null;
        } catch {
            return null;
        }
    }

    async function open(what) {
        request = what;
        password.value = '';
        setPasswordShown(false);
        restoreChoice();
        // (Without the answer, the dialog works as for a download without archived folders: the server unzips them)
        showFolders(await loadFolders(what));
        updateFolders();
        bootstrap.Modal.getOrCreateInstance(modal).show();
    }

    function setPasswordShown(shown) {
        password.type = shown ? 'text' : 'password';
        passwordToggle.setAttribute('aria-pressed', String(shown));
    }

    // Hands the finished archive to the browser as a download. A link rather than leaving the page, which would ask whether to leave it.
    function download(id) {
        const link = document.createElement('a');
        link.href = window.location.pathname + '?archive=' + encodeURIComponent(id);
        link.download = '';
        document.body.append(link);
        link.click();
        link.remove();
    }

    async function confirm() {
        if (request === null)
            return;
        const kind = downloadsWholeAsIs() ? 'as-is' : request.kind;
        const asIs = keptAsIs().map(folder => folder.path);
        const body = kind === 'as-is' ? {} : { ...(request.kind === 'selection' ? { version: request.version } : {}),
            format: chosenFormat(), compression: compression.value, password: password.value, asIs };
        request = null;
        if (kind !== 'as-is')
            keepChoice();
        password.value = ''; // not kept in the page longer than needed
        bootstrap.Modal.getOrCreateInstance(modal).hide();

        const answer = await whileBusy(async function (setProgress) {
            try {
                // Answered at once; the archive is made meanwhile, and asked about until it is done
                const response = await fetch(window.location.pathname + '?archive=' + kind, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/json', 'Prefer': 'respond-async' },
                    body: JSON.stringify(body)
                });
                if (!response.ok)
                    return { message: await messageOf(response) };
                const { id } = await response.json();
                setProgress(0);
                return await waitUntilMade(id, setProgress);
            } catch {
                return { message: texts.connectionLost };
            }
        }, { blockPage: true, message: texts.preparing });

        if (answer.message !== undefined) {
            notify(answer.message, 'danger');
            return;
        }
        if (answer.leftOutUnreadable)
            notify(texts.leftOutUnreadable, 'info');
        download(answer.id);
    }

    const POLL_EVERY_MS = 700;
    const TRIES_WITHOUT_CONNECTION = 5;

    async function messageOf(response) {
        return (await response.text()) || fillIn(texts.failed, { status: response.status });
    }

    // Asks how far the archive got until it is done: { id, leftOutUnreadable }, or { message } if it failed. A few answers lost on the way
    // (a connection that comes back) are tried again.
    async function waitUntilMade(id, setProgress) {
        const address = window.location.pathname + '?archive=' + encodeURIComponent(id) + '&progress';
        let lost = 0;
        for (;;) {
            await new Promise(resolve => setTimeout(resolve, POLL_EVERY_MS));
            let response;
            try {
                response = await fetch(address, { headers: { 'Accept': 'application/json' } });
            } catch {
                if (++lost >= TRIES_WITHOUT_CONNECTION)
                    return { message: texts.connectionLost };
                continue;
            }
            lost = 0;
            if (!response.ok)
                return { message: await messageOf(response) };
            const state = await response.json();
            setProgress(state.progress, state.status);
            if (state.done)
                return { id, leftOutUnreadable: state.leftOutUnreadable };
        }
    }

    form.addEventListener('change', function (e) {
        if (e.target === unzipAll)
            folders.forEach(folder => folder.box.checked = unzipAll.checked);
        updateCompressionChoices();
        updateFolders();
    });
    // Enter in the password field submits the form
    form.addEventListener('submit', function (e) {
        e.preventDefault();
        confirm();
    });
    document.getElementById('archive-confirm').addEventListener('click', confirm);
    passwordToggle.addEventListener('click', () => setPasswordShown(password.type === 'password'));
    modal.addEventListener('shown.bs.modal', () => (folders.length > 0 ? unzipAll : compression).focus());
    // A dialog that was closed without downloading forgets the password typed into it
    modal.addEventListener('hidden.bs.modal', function () {
        request = null;
        password.value = '';
    });

    if (modal.dataset.opensFromToolbar === 'true')
        onClickOf('download-button', () => open({ kind: 'directory' }));

    window.archiveDownload = { open };
})();
