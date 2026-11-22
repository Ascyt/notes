// The dialog that asks how to make an archive (_ArchiveModal.cshtml), and the download with what it asks:
//
//   archiveDownload.open({ kind: 'directory' })                      the directory of the page
//   archiveDownload.open({ kind: 'selection', version: 1234 })        the selection, as the page showed it (its version)
//
// The archive is made by the server first (POST ?archive=…, with the password in the body, never in an address) while the page is covered,
// and then downloaded (GET ?archive=<id>). The format and compression chosen last are kept in the browser; the password never is.
(function () {
    const modal = document.getElementById('archive-modal');
    const form = document.getElementById('archive-form');
    const compression = document.getElementById('archive-compression');
    const storeOption = document.getElementById('archive-compression-store');
    const minOption = document.getElementById('archive-compression-min');
    const password = document.getElementById('archive-password');
    const passwordToggle = document.getElementById('archive-password-toggle');
    const texts = textsOf('archive-texts');
    const CHOICE_KEY = 'notes.archive-options';

    let request = null; // what the dialog is open for

    const token = () => document.querySelector('input[name="__RequestVerificationToken"]').value;
    const chosenFormat = () => form.querySelector('input[name="archive-format"]:checked').value;

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

    function open(what) {
        request = what;
        password.value = '';
        setPasswordShown(false);
        restoreChoice();
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
        const body = { ...(request.kind === 'selection' ? { version: request.version } : {}),
            format: chosenFormat(), compression: compression.value, password: password.value };
        const kind = request.kind;
        request = null;
        keepChoice();
        password.value = ''; // not kept in the page longer than needed
        bootstrap.Modal.getOrCreateInstance(modal).hide();

        const answer = await whileBusy(async function () {
            try {
                const response = await fetch(window.location.pathname + '?archive=' + kind, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                });
                if (response.ok)
                    return { archive: await response.json() };
                return { message: (await response.text()) || fillIn(texts.failed, { status: response.status }) };
            } catch {
                return { message: texts.connectionLost };
            }
        }, { blockPage: true, message: texts.preparing });

        if (answer.message !== undefined) {
            notify(answer.message, 'danger');
            return;
        }
        if (answer.archive.leftOutUnreadable)
            notify(texts.leftOutUnreadable, 'info');
        download(answer.archive.id);
    }

    form.addEventListener('change', updateCompressionChoices);
    // Enter in the password field submits the form
    form.addEventListener('submit', function (e) {
        e.preventDefault();
        confirm();
    });
    document.getElementById('archive-confirm').addEventListener('click', confirm);
    passwordToggle.addEventListener('click', () => setPasswordShown(password.type === 'password'));
    modal.addEventListener('shown.bs.modal', () => compression.focus());
    // A dialog that was closed without downloading forgets the password typed into it
    modal.addEventListener('hidden.bs.modal', function () {
        request = null;
        password.value = '';
    });

    if (modal.dataset.opensFromToolbar === 'true')
        onClickOf('download-button', () => open({ kind: 'directory' }));

    window.archiveDownload = { open };
})();
