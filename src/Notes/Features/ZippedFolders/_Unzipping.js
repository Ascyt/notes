// Waiting for an archived folder to be unzipped (ArchiveGate on the server), on every page:
//
// - The page of an entry in a folder that is still being unzipped (#unzipping) shows how far it got, and is loaded again once it is done.
// - A request of the page's scripts that the server answers with 503 and X-Notes-Unzipping (the folder was zipped again meanwhile, say,
//   while a file in it was edited) covers the page with how far it got, and is sent again once it is done: nothing that was being done
//   (an edit that wasn't saved yet) is lost.
//
// How far it got is asked of the folder (GET <folder>?unzip), which answers { done, progress } (and starts unzipping it if it isn't yet).
//
// The archive button of the navbar (#zip-button) zips every archived folder that is unzipped now (POST ?zip).
//
// An archived folder of the listing that is being zipped has a bar after its name, kept up to date (GET <folder>?zipping).
(function () {
    const HEADER = 'X-Notes-Unzipping';
    const POLL_EVERY_MS = 500;
    const TRIES_WITHOUT_CONNECTION = 5;
    const texts = textsOf('unzip-texts');
    const fetchBefore = window.fetch; // (that of the password dialog, which asks for a password first where one is needed)

    // Asks until it is unzipped: null once it is, otherwise what went wrong
    async function waitUntilUnzipped(address, setProgress) {
        let lost = 0;
        for (;;) {
            let response;
            try {
                response = await fetchBefore(address + '?unzip', { headers: { 'Accept': 'application/json' } });
            } catch {
                if (++lost >= TRIES_WITHOUT_CONNECTION)
                    return texts.connectionLost;
                await new Promise(resolve => setTimeout(resolve, POLL_EVERY_MS));
                continue;
            }
            lost = 0;
            if (!response.ok)
                return (await response.text()) || fillIn(texts.failedWithStatus, { status: response.status });
            const state = await response.json();
            setProgress(state.progress);
            if (state.done)
                return null;
            // (The server waits a moment itself before it answers, so this is not asked too often)
            await new Promise(resolve => setTimeout(resolve, POLL_EVERY_MS));
        }
    }

    // (For the selection's Unzip, which unzips the selected archived folders one after the other)
    window.archiveUnzipping = { waitUntilUnzipped };

    function showOnPage(fraction) {
        const bar = document.getElementById('unzipping-progress');
        const percent = Math.round(Math.min(1, Math.max(0, fraction)) * 100);
        bar.setAttribute('aria-valuenow', String(percent));
        bar.querySelector('.progress-bar').style.width = percent + '%';
    }

    // ---- The page of an entry in a folder that is being unzipped

    document.addEventListener('DOMContentLoaded', async function () {
        const page = document.getElementById('unzipping');
        if (!page)
            return;
        const bar = document.getElementById('unzipping-progress');
        showOnPage(Number(bar.getAttribute('aria-valuenow')) / 100);
        const failure = await waitUntilUnzipped(page.dataset.address, showOnPage);
        if (failure === null)
            window.location.reload();
        else
            notify(failure, 'danger');
    });

    // ---- Every request of the page's scripts

    window.fetch = async function (resource, options) {
        let response = await fetchBefore(resource, options);
        // (Each round unzips one folder: one inside it may still have to be after it)
        for (let round = 0; round < 8 && response.status === 503 && response.headers.has(HEADER); round++) {
            const address = response.headers.get(HEADER);
            const display = decodeURIComponent(address).replace(/^\//, '');
            const failure = await whileBusy(setProgress => {
                setProgress(0);
                return waitUntilUnzipped(address, setProgress);
            }, { blockPage: true, message: fillIn(texts.unzipping, { entry: display }) });
            if (failure !== null)
                return response;
            response = await fetchBefore(resource, options);
        }
        return response;
    };

    // ---- The bar after an archived folder of the listing that is being zipped (_ArchivedMark): asks how far it got (GET <folder>?zipping,
    //      which answers { state, progress }) until it is no longer being zipped, then shows the mark it ended as

    const ZIPPING_POLL_EVERY_MS = 700;

    // (The same classes and titles as _ArchivedMark.IconClasses and Title)
    function markOf(state, encrypted) {
        const mark = document.createElement('i');
        const zipped = state === 'zipped';
        mark.className = encrypted
            ? (zipped ? 'fa-solid fa-shield fa-xs ms-1 text-warning' : 'fa-solid fa-shield-halved fa-xs ms-1 text-secondary')
            : (zipped ? 'fa-solid fa-file-zipper fa-xs ms-1 text-primary' : 'fa-regular fa-file-zipper fa-xs ms-1 text-secondary');
        mark.title = encrypted
            ? (zipped ? texts.encryptedZipped : texts.encryptedUnzipped)
            : (zipped ? texts.zipped : texts.unzipped);
        mark.setAttribute('aria-label', mark.title);
        mark.dataset.archivedMark = zipped ? 'zipped' : 'unzipped';
        mark.dataset.encrypted = String(encrypted);
        return mark;
    }

    async function followZipping(zipping) {
        const bar = zipping.querySelector('.progress');
        let lost = 0;
        for (;;) {
            await new Promise(resolve => setTimeout(resolve, ZIPPING_POLL_EVERY_MS));
            let state;
            try {
                const response = await fetchBefore(zipping.dataset.zippingAddress + '?zipping', { headers: { 'Accept': 'application/json' } });
                // (Gone, say: the bar stays as it was, and the next page tells)
                if (!response.ok)
                    return;
                state = await response.json();
            } catch {
                if (++lost >= TRIES_WITHOUT_CONNECTION)
                    return;
                continue;
            }
            lost = 0;
            if (state.state !== 'zipping') {
                // (Null: no longer archived; the mark goes)
                if (state.state)
                    zipping.replaceWith(markOf(state.state, zipping.dataset.encrypted === 'true'));
                else
                    zipping.remove();
                return;
            }
            const percent = Math.round(Math.min(1, Math.max(0, state.progress)) * 100);
            bar.setAttribute('aria-valuenow', String(percent));
            bar.querySelector('.progress-bar').style.width = percent + '%';
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-archived-mark="zipping"]').forEach(followZipping);
    });

    // ---- The archive button (ZipHandler): zips what is unzipped now; the page goes out of it, or is loaded again (for the marks)

    // The navbar's zips everything unzipped (POST ?zip); the toolbar's only the folder of the page (POST ?zip=entry)
    onClickOf('zip-button', () => zipAndLeave('?zip', document.getElementById('zip-button')));
    onClickOf('zip-entry-button', () => zipAndLeave('?zip=entry', document.getElementById('zip-entry-button')));

    // The button spins until the server answered (it answers once the zipping started), then the page goes on, where the folder shows as
    // being zipped
    async function zipAndLeave(query, button) {
        let location = null;
        setButtonBusy(button, true);
        try {
            const response = await fetchBefore(window.location.pathname + query, {
                method: 'POST',
                headers: { 'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value }
            });
            if (!response.ok) {
                setButtonBusy(button, false);
                notify((await response.text()) || fillIn(texts.failedWithStatus, { status: response.status }), 'danger');
                return;
            }
            location = (await response.json()).location;
        } catch {
            setButtonBusy(button, false);
            notify(texts.connectionLost, 'danger');
            return;
        }
        // (Leaving an editor with unsaved changes asks first)
        if (location)
            window.location.assign(location);
        else
            window.location.reload();
    }
})();
