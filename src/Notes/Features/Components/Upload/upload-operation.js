// The upload that is under way (_Upload.js), kept in the browser's storage so that it can be continued after the tab was closed or the page
// left: which directory, and for each file its name, size and time (to find it again among the files picked then: by name and size, the time deciding between two of those), whether it is to be
// unzipped, and how far it got (done, there already, or the id and offset of what the server has of it). The files themselves can't be
// kept: they are picked again.
//
// On every page (_UploadResume.cshtml) an unfinished one that no tab is working on is offered to be continued (it goes to its directory,
// where _Upload.js asks for the files again: window.uploadOperation.takeResume()) or discarded.
//
// A tab working on one holds a lock of the browser (Web Locks) for as long as it does, so that another tab doesn't offer it meanwhile. Where
// there are no such locks, the tab marks the time every few seconds instead.
(function () {
    const KEY = 'notes.upload';
    const RESUME_KEY = 'notes.upload-resume';
    const LOCK = 'notes-upload';
    const BEAT_EVERY_MS = 5000;
    const DEAD_AFTER_MS = 30000;
    const hasLocks = !!(navigator.locks && navigator.locks.request);

    function load() {
        try {
            const operation = JSON.parse(localStorage.getItem(KEY) || 'null');
            return operation && Array.isArray(operation.files) ? operation : null;
        } catch {
            return null;
        }
    }

    function save(operation) {
        try {
            operation.beat = Date.now();
            localStorage.setItem(KEY, JSON.stringify(operation));
        } catch {
            // No storage (private mode and the like): it just can't be continued later
        }
    }

    function clear() {
        try {
            localStorage.removeItem(KEY);
        } catch { }
    }

    // Held while this tab works on it (current() gives it as it is now): call the returned function to let go
    function hold(current) {
        let release = () => { };
        if (hasLocks) {
            const held = new Promise(resolve => release = resolve);
            navigator.locks.request(LOCK, () => held).catch(() => { });
            return release;
        }
        const timer = setInterval(() => save(current()), BEAT_EVERY_MS);
        return () => clearInterval(timer);
    }

    async function isWorkedOn(operation) {
        if (hasLocks) {
            try {
                const state = await navigator.locks.query();
                return state.held.some(lock => lock.name === LOCK);
            } catch {
                return false;
            }
        }
        return Date.now() - (operation.beat || 0) < DEAD_AFTER_MS;
    }

    const isFinished = operation => operation.files.every(file => file.state === 'done' || file.state === 'skipped');

    // Whether this page was opened to continue it (and on which directory): asked once, by _Upload.js
    function takeResume() {
        try {
            const directory = sessionStorage.getItem(RESUME_KEY);
            sessionStorage.removeItem(RESUME_KEY);
            return directory;
        } catch {
            return null;
        }
    }

    // The parts the server still has of files that were half sent: discarded (it would discard them an hour after their last part anyway)
    function discard(operation) {
        clear();
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (!tokenInput)
            return;
        for (const file of operation.files)
            if (file.id && file.state === 'todo')
                fetch(operation.directory + '?upload=' + file.id + '&abort', { method: 'POST', headers: { 'RequestVerificationToken': tokenInput.value } })
                    .catch(() => { });
    }

    window.uploadOperation = { load, save, clear, hold, takeResume, discard, isFinished };

    // ---- Offering an unfinished one, when a page opens

    document.addEventListener('DOMContentLoaded', async function () {
        const modal = document.getElementById('upload-resume-modal');
        const operation = load();
        if (!modal || !operation)
            return;
        if (isFinished(operation)) {
            clear();
            return;
        }
        // (The page that is opened to continue it doesn't ask again; nor does any while a tab is at it)
        if (sessionStorage.getItem(RESUME_KEY) || await isWorkedOn(operation))
            return;

        const texts = textsOf('upload-resume-texts');
        const done = operation.files.filter(file => file.state === 'done' || file.state === 'skipped').length;
        document.getElementById('upload-resume-text').textContent = fillIn(texts.resumeText, {
            directory: decodeURIComponent(operation.directory),
            done,
            count: operation.files.length
        });
        const dialog = bootstrap.Modal.getOrCreateInstance(modal);
        document.getElementById('upload-resume-continue').addEventListener('click', function () {
            try {
                sessionStorage.setItem(RESUME_KEY, operation.directory);
            } catch { }
            dialog.hide();
            if (window.location.pathname === operation.directory)
                window.dispatchEvent(new Event('notes-upload-resume'));
            else
                window.location.href = operation.directory;
        });
        document.getElementById('upload-resume-discard').addEventListener('click', function () {
            discard(operation);
            dialog.hide();
        });
        dialog.show();
    });
})();
