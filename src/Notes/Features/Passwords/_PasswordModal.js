// Passwords of entries (_PasswordModal.cshtml): whatever the page's scripts ask the server for, an answer that says a password is needed
// (the header X-Notes-Password-Required, with the address of the entry that has it) opens the dialog, unlocks that entry
// (POST <address>?unlock, with the password in the body, never in an address) and sends the request again, so nothing that was being
// done is lost (an edit that wasn't saved yet). Cancelling hands the script the answer as it was, which it shows as an error like any other.
//
//   passwordPrompt.ask('/docs/secret')   asks for the password of that entry; resolves to whether it was unlocked
//
// Too many wrong passwords make the next one wait (Retry-After): the dialog counts down meanwhile. On the page of a locked entry
// (#password-prompt) the dialog opens by itself, and the page is loaded again once it is unlocked.
//
// While anything is unlocked (#lock-button is shown), using the page keeps it unlocked (POST ?unlock=renew, at most once a minute), and the
// lock button locks everything again at once.
(function () {
    const REQUIRED_HEADER = 'X-Notes-Password-Required';
    const RENEW_EVERY_MS = 60 * 1000;

    const modal = document.getElementById('password-modal');
    const form = document.getElementById('password-form');
    const entry = document.getElementById('password-entry');
    const input = document.getElementById('password-input');
    const toggle = document.getElementById('password-toggle');
    const error = document.getElementById('password-error');
    const confirmButton = document.getElementById('password-confirm');
    const texts = textsOf('password-texts');
    const originalFetch = window.fetch.bind(window);

    const token = () => document.querySelector('input[name="__RequestVerificationToken"]').value;

    // ---- The dialog

    let current = null;      // { address, resolve } of what the dialog is open for
    let waiting = null;      // a request for the same entry while the dialog is open shares its answer
    let countdown = null;    // the timer while the next password has to wait

    function showError(message) {
        error.textContent = message || '';
        error.hidden = !message;
    }

    function waitThen(seconds) {
        clearInterval(countdown);
        let left = seconds;
        const tick = function () {
            if (left <= 0) {
                clearInterval(countdown);
                countdown = null;
                showError('');
                setButtonDisabled(confirmButton, null);
                return;
            }
            const message = fillIn(texts.wait, { seconds: left });
            showError(message);
            setButtonDisabled(confirmButton, message);
            left--;
        };
        tick();
        countdown = setInterval(tick, 1000);
    }

    function retryAfterOf(response) {
        const seconds = parseInt(response.headers.get('Retry-After') || '', 10);
        return Number.isFinite(seconds) && seconds > 0 ? seconds : null;
    }

    function nameOf(address) {
        return decodeURIComponent(address).replace(/^\/+/, '') || '/';
    }

    // Resolves to whether the entry at the address was unlocked
    function ask(address) {
        if (current !== null)
            return current.address === address ? waiting : Promise.resolve(false);

        waiting = new Promise(function (resolve) {
            current = { address, resolve };
        });
        entry.textContent = fillIn(texts.isProtected, { entry: nameOf(address) });
        input.value = '';
        setShown(false);
        showError('');
        bootstrap.Modal.getOrCreateInstance(modal).show();
        return waiting;
    }

    function finish(unlocked) {
        if (current === null)
            return;
        const { resolve } = current;
        current = null;
        input.value = ''; // not kept in the page longer than needed
        resolve(unlocked);
        if (unlocked)
            showLockButton();
    }

    function setShown(shown) {
        input.type = shown ? 'text' : 'password';
        toggle.setAttribute('aria-pressed', String(shown));
    }

    async function confirm() {
        if (current === null || countdown !== null || confirmButton.dataset.busy === 'true')
            return;
        const address = current.address;
        setButtonBusy(confirmButton, true);
        try {
            const response = await originalFetch(address + '?unlock', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/json' },
                body: JSON.stringify({ password: input.value })
            });
            if (response.ok) {
                // (Before hiding it: without a fade, hiding says it is hidden at once, which counts as cancelled)
                finish(true);
                bootstrap.Modal.getOrCreateInstance(modal).hide();
                return;
            }
            const message = (await response.text()) || fillIn(texts.failed, { status: response.status });
            input.select();
            const seconds = retryAfterOf(response);
            if (response.status === 429 && seconds !== null)
                waitThen(seconds);
            else {
                showError(message);
                // A wrong password that makes the next one wait says so at once
                if (seconds !== null)
                    setTimeout(() => waitThen(seconds), 1500);
            }
        } catch {
            showError(texts.connectionLost);
        } finally {
            setButtonBusy(confirmButton, false);
        }
    }

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        confirm();
    });
    confirmButton.addEventListener('click', confirm);
    toggle.addEventListener('click', () => setShown(input.type === 'password'));
    modal.addEventListener('shown.bs.modal', () => input.focus());
    // Closed without unlocking: the request that needed it gets its answer as it was
    modal.addEventListener('hidden.bs.modal', () => finish(false));

    // ---- Every request of the page's scripts

    window.fetch = async function (resource, options) {
        let response = await originalFetch(resource, options);
        // (Each round unlocks one entry: a password further in may still be needed after it)
        for (let round = 0; round < 8 && response.headers.has(REQUIRED_HEADER); round++) {
            if (!await ask(response.headers.get(REQUIRED_HEADER)))
                return response;
            response = await originalFetch(resource, options);
        }
        return response;
    };

    window.passwordPrompt = { ask };

    // ---- The page of a locked entry

    // (The dialog is at the start of the page, the prompt further down: looked for once the page is there)
    async function askHere() {
        const prompt = document.getElementById('password-prompt');
        if (prompt && await ask(prompt.dataset.address))
            window.location.reload();
    }
    onClickOf('password-prompt-button', askHere);
    document.addEventListener('DOMContentLoaded', askHere);

    // ---- Keeping it unlocked, and locking it again

    const lockButton = () => document.getElementById('lock-button');

    function showLockButton() {
        const button = lockButton();
        if (button)
            button.closest('[data-lock-item]').hidden = false;
        // The lock button goes first, with the same shortcut: the archive button waits until nothing is unlocked
        const zipItem = document.querySelector('[data-zip-item]');
        if (zipItem)
            zipItem.hidden = true;
    }

    let lastRenewal = Date.now();
    let toldExpired = false;
    async function renew() {
        const button = lockButton();
        if (!button || button.closest('[data-lock-item]').hidden || Date.now() - lastRenewal < RENEW_EVERY_MS)
            return;
        lastRenewal = Date.now();
        try {
            // Not through the dialog: only when something is done with the entry is the password asked for again
            const response = await originalFetch(window.location.pathname + '?unlock=renew', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token() }
            });
            if (response.headers.has(REQUIRED_HEADER) && !toldExpired) {
                toldExpired = true;
                notify(texts.expired, 'warning');
            }
        } catch { /* tried again with the next edit */ }
    }

    // An edit counts as using the entry, saved or not
    document.addEventListener('input', renew, true);

    // The lock button of the navbar locks everything (POST ?lock); that of the toolbar only the entry's own password (POST ?lock=entry)
    async function lockAndLeave(query, message) {
        // Where the page goes: the folder above what was unlocked here, if the server names one (instead of the password prompt)
        let location = null;
        try {
            const response = await originalFetch(window.location.pathname + query, {
                method: 'POST',
                headers: { 'RequestVerificationToken': token() }
            });
            if (response.status === 200)
                location = (await response.json()).location;
        } catch { /* the unlocks run out by themselves */ }
        notifyAfterNavigation(message, 'info');
        // (Leaving an editor with unsaved changes asks first)
        if (location)
            window.location.assign(location);
        else
            window.location.reload();
    }

    onClickOf('lock-button', () => lockAndLeave('?lock', texts.lockedAll));
    onClickOf('lock-entry-button', () => lockAndLeave('?lock=entry', texts.lockedEntry));
})();
