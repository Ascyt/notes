// The bars after uploaded archives of the listing that are being unzipped (_UnzipMark.cshtml): asks the folder how it goes
// (GET <folder>?unzipping=<name>, which answers { state, progress, folder, leftOut, error }) until it ended. Unzipped: the page is loaded
// again, where the archive is a folder now (unless a dialog is open: then once it closes). Failed: the bar becomes the red mark.
(function () {
    const POLL_EVERY_MS = 1000;
    const TRIES_WITHOUT_CONNECTION = 10;
    const texts = textsOf('unzip-mark-texts');

    let reloadPending = false;
    function reloadWhenFree() {
        if (reloadPending)
            return;
        reloadPending = true;
        const reload = () => window.location.reload();
        const open = document.querySelector('.modal.show');
        if (open)
            open.addEventListener('hidden.bs.modal', reload, { once: true });
        else
            reload();
    }

    function failedMark(error) {
        const mark = document.createElement('i');
        mark.className = 'fa-solid fa-circle-exclamation fa-xs ms-1 text-danger';
        mark.title = fillIn(texts.couldNotUnzip, { reason: error || '' });
        mark.setAttribute('aria-label', mark.title);
        mark.dataset.unzipFailed = 'true';
        return mark;
    }

    async function follow(mark) {
        const bar = mark.querySelector('.progress');
        const name = mark.dataset.unzippingName;
        const directory = mark.dataset.unzippingDirectory;
        let lost = 0;
        for (;;) {
            await new Promise(resolve => setTimeout(resolve, POLL_EVERY_MS));
            let state;
            try {
                const response = await fetch(directory + '?unzipping=' + encodeURIComponent(name), { headers: { 'Accept': 'application/json' } });
                if (!response.ok)
                    return; // (gone, say: the next page tells)
                state = await response.json();
            } catch {
                if (++lost >= TRIES_WITHOUT_CONNECTION)
                    return;
                continue;
            }
            lost = 0;
            if (state.state === 'unzipping') {
                const percent = Math.round(Math.min(1, Math.max(0, state.progress)) * 100);
                bar.setAttribute('aria-valuenow', String(percent));
                bar.querySelector('.progress-bar').style.width = percent + '%';
                showPending(bar, state.status);
                mark.querySelector('.upload-unzipping-detail').textContent = describeProgress(state.status, state.progress);
                continue;
            }
            if (state.state === 'failed')
                mark.replaceWith(failedMark(state.error));
            else if (state.state === 'unzipped') {
                const folder = (state.folder || '').split('/').pop();
                notifyAfterNavigation(fillIn(texts.unzippedInto, { name, folder }));
                if (state.leftOut > 0)
                    notifyAfterNavigation(fillIn(texts.leftOutOfArchive, { count: state.leftOut, name }), 'warning');
                reloadWhenFree();
            } else
                mark.remove();
            return;
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-unzipping-name]').forEach(follow);
    });
})();
