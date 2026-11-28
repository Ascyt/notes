// Buttons that are there but can't be used right now are never just greyed out: they say why, and while they work they show a spinner.
//
//   setButtonDisabled(button, 'Select something first.')   greys it out and shows the reason on hover
//   setButtonDisabled(button, null)                         makes it usable again
//   setButtonBusy(button, true)                             replaces its icon with a spinner (and it can't be used) until false
//
// This works for buttons that carry their label in `data-label` (and, if disabled from the start, the reason in `data-disabled-reason`),
// like those of the toolbar: the label is the tooltip as long as there is no reason. A button with an icon `.btn-icon` and a
// `.btn-spinner` next to it can show the spinner.
//
// The buttons stay clickable as far as the browser is concerned, but do nothing (see below): a real `disabled` attribute would stop some
// browsers from showing the tooltip, and silences mouse events.

function refreshButton(button) {
    const reason = button.dataset.disabledReason || '';
    const busy = button.dataset.busy === 'true';
    const label = button.dataset.label || '';

    button.setAttribute('aria-disabled', String(reason !== '' || busy));
    button.classList.toggle('is-disabled', reason !== '');
    button.title = reason !== '' ? (label !== '' ? label + ': ' + reason : reason) : label;

    const icon = button.querySelector('.btn-icon');
    const spinner = button.querySelector('.btn-spinner');
    if (icon)
        icon.classList.toggle('d-none', busy);
    if (spinner)
        spinner.classList.toggle('d-none', !busy);
}

window.setButtonDisabled = function (button, reason) {
    button.dataset.disabledReason = reason || '';
    refreshButton(button);
};

window.setButtonBusy = function (button, busy) {
    button.dataset.busy = String(Boolean(busy));
    refreshButton(button);
};

// Something long that the page waits for (renaming, preparing an archive) shows that it is working, and the page asks before it is left
// meanwhile, as its answer would be lost. With blockPage, the page is covered until it is done (_BusyOverlay), with what is going on:
//
//   const response = await whileBusy(() => fetch(…), { blockPage: true, message: texts.renaming });
//
// It returns what the work returns (and throws what it throws). Leaving the page afterwards (to show the result) asks nothing, as long as
// nothing else is still running. Work that knows how far it got is given setProgress (0 to 1), which shows a bar in place of the spinner,
// and, with what it is doing (a WorkStatus of the server, see describeProgress), that in words under it:
//
//   await whileBusy(async setProgress => { … setProgress(0.4, state.status); … }, { blockPage: true, message: texts.preparing });
(function () {
    let running = 0;
    let blocking = 0;

    function showOverlay(message) {
        const overlay = document.getElementById('busy-overlay');
        if (!overlay)
            return;
        document.getElementById('busy-overlay-message').textContent = message || '';
        showProgress(null);
        overlay.hidden = blocking === 0;
        if (blocking > 0)
            overlay.focus(); // so that Enter doesn't press the button that started it again
    }

    // null hides the bar (and shows the spinner again), and the text under it
    function showProgress(fraction, status) {
        const bar = document.getElementById('busy-overlay-progress');
        if (!bar)
            return;
        const detail = document.getElementById('busy-overlay-detail');
        const text = typeof fraction === 'number' ? describeProgress(status, fraction) : '';
        detail.textContent = text;
        detail.hidden = text === '';
        const known = typeof fraction === 'number';
        bar.hidden = !known;
        showPending(bar, known ? status : null);
        document.getElementById('busy-overlay-spinner').hidden = known;
        const percent = known ? Math.round(Math.min(1, Math.max(0, fraction)) * 100) : 0;
        bar.setAttribute('aria-valuenow', String(percent));
        bar.querySelector('.progress-bar').style.width = percent + '%';
    }

    window.whileBusy = async function (work, options) {
        const { blockPage = false, message = '' } = options || {};
        running++;
        if (blockPage) {
            blocking++;
            showOverlay(message);
        }
        try {
            return await work(blockPage ? showProgress : () => { });
        } finally {
            running--;
            if (blockPage) {
                blocking--;
                showOverlay('');
            }
        }
    };

    window.isBusy = () => running > 0;

    window.addEventListener('beforeunload', function (e) {
        if (running > 0)
            e.preventDefault();
    });

    // While the page is covered, its shortcuts do nothing either (this runs before those of shortcuts.js, which listen on the document).
    // What is shown above the cover (data-above-busy-overlay: the dialog for a password that what runs needs) still gets its keys.
    window.addEventListener('keydown', function (e) {
        if (blocking > 0 && !(e.target instanceof Element && e.target.closest('[data-above-busy-overlay]'))
            && !document.querySelector('[data-above-busy-overlay].show')) {
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }, true);
})();

// Whether an address of this site (a path, "/…") is the app's own rather than one of the entries: its first name is one the app takes at
// the root (the layout's app-root-names, such as "_" for /_/Login). Names are compared ignoring case, as the server does.
window.isAppAddress = function (pathname) {
    const names = (document.querySelector('meta[name="app-root-names"]')?.content || '').toLowerCase().split(' ');
    const first = decodeURIComponent(pathname.split('/')[1] || '').toLowerCase();
    return first !== '' && names.includes(first);
};

// Runs the function when the element with that id is clicked, if the page has one. The scripts of the page attach their buttons with
// it (there are no inline handlers: the policy of the site doesn't run them), also those that come later in the page than the script.
window.onClickOf = function (id, handler) {
    const attach = () => document.getElementById(id)?.addEventListener('click', () => handler());
    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', attach);
    else
        attach();
};

// An image or video with data-hidden-on-error is hidden if it can't be loaded (what is behind it shows instead). Errors of what is
// loaded don't bubble, so they are caught on their way down; and this script is in the head, so none comes before it.
document.addEventListener('error', function (e) {
    if (e.target instanceof Element && e.target.hasAttribute('data-hidden-on-error'))
        e.target.hidden = true;
}, true);

// Swallows every click on such a button before anything else (listeners, shortcuts) gets to see it
document.addEventListener('click', function (e) {
    if (e.target instanceof Element && e.target.closest('[aria-disabled="true"]')) {
        e.preventDefault();
        e.stopImmediatePropagation();
    }
}, true);

// The buttons made by the server start out in the state their data attributes describe
document.addEventListener('DOMContentLoaded', function () {
    for (const button of document.querySelectorAll('[data-label]'))
        refreshButton(button);
});

// The heights of the parts that stay at the top of the screen (site.css), as they change: the navbar's menu opens on a phone
document.addEventListener('DOMContentLoaded', function () {
    const parts = { '--site-header-height': document.getElementById('site-header'),
        '--breadcrumb-height': document.querySelector('.sticky-breadcrumb') };
    const observer = new ResizeObserver(function () {
        for (const [property, element] of Object.entries(parts))
            if (element)
                // Not offsetHeight: rounded to whole pixels, it leaves a gap of a fraction of one (on a scaled screen) the page shows through
                document.documentElement.style.setProperty(property, element.getBoundingClientRect().height + 'px');
    });
    for (const element of Object.values(parts))
        if (element)
            observer.observe(element);
});

// The texts a script shows (messages, tooltips) come from the texts class of its component, which its view writes into a
// <script type="application/json"> block (<script-texts>, see ScriptTextsTagHelper). A text with parts that change has them in braces,
// which fillIn puts in:
//
//   const texts = textsOf('upload-texts');
//   notify(fillIn(texts.uploaded, { count: 3 }));
window.textsOf = function (id) {
    const block = document.getElementById(id);
    return block ? JSON.parse(block.textContent) : {};
};

window.fillIn = function (text, values) {
    return String(text).replace(/\{(\w+)\}/g, (whole, name) => name in values ? String(values[name]) : whole);
};

// Where work is pending (its status is "waiting"): an hourglass in place of its bar, whose tooltip says so (and for what, where that is
// known). The hourglass goes right before the bar, and the bar is back once the work begins.
window.showPending = function (bar, status) {
    const pending = !!status && status.phase === 'waiting';
    let mark = bar.previousElementSibling;
    if (!mark || !mark.classList.contains('pending-mark')) {
        mark = document.createElement('i');
        mark.className = 'fa-solid fa-hourglass-half text-secondary pending-mark';
        bar.before(mark);
    }
    const texts = textsOf('progress-texts');
    mark.hidden = !pending;
    mark.title = !pending ? '' : status.waitingFor ? fillIn(texts.pendingFor, { operation: status.waitingFor }) : texts.pending;
    mark.setAttribute('aria-label', mark.title);
    // (Not the hidden attribute: the bars are flex boxes, which their class shows whatever it says)
    bar.style.display = pending ? 'none' : '';
    return pending;
};

// A size in bytes as people read it: "1.2 MiB"
window.formatSize = function (bytes) {
    const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    let i = 0;
    while (bytes >= 1024 && i < units.length - 1) {
        bytes /= 1024;
        i++;
    }
    return (i === 0 ? bytes : bytes.toFixed(1)) + ' ' + units[i];
};

// What long work is doing, in words, for the text under its bar: the status the server tells (WorkStatus: { phase, files, totalFiles, bytes,
// totalBytes }), with how far it got (0 to 1) if known: "Writing the archive: 12 of 340 files, 1.2 of 40.0 MiB (35%)". '' for none.
window.describeProgress = function (status, fraction) {
    if (!status || !status.phase)
        return '';
    const texts = textsOf('progress-texts');
    if (status.phase === 'waiting' && status.waitingFor)
        return fillIn(texts.waitingFor, { operation: status.waitingFor });
    const phase = texts[status.phase] || status.phase;
    const counts = [];
    if (status.totalFiles > 0)
        counts.push(fillIn(status.phase === 'unzipping' ? texts.entries : texts.files, { done: status.files, total: status.totalFiles }));
    if (status.totalBytes > 0)
        counts.push(fillIn(texts.bytes, { done: formatSize(status.bytes), total: formatSize(status.totalBytes) }));
    const text = counts.length === 0 ? phase : fillIn(texts.withCounts, { phase, counts: counts.join(', ') });
    return typeof fraction === 'number'
        ? fillIn(texts.withPercent, { text, percent: fillIn(texts.percent, { percent: Math.round(Math.min(1, Math.max(0, fraction)) * 100) }) })
        : text;
};
