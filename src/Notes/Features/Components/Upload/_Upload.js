// Uploading to the current directory (_Upload.cshtml): the file picker, the drop target, the progress and the Upload button of the
// toolbar. The size of the chunks, and why nothing may be uploaded here (if so), come from the server as data- attributes of the picker.
(function () {
    const input = document.getElementById('upload-input');
    const CHUNK_SIZE = Number(input.dataset.chunkSize);
    const MAX_ATTEMPTS = 3;
    const texts = textsOf('upload-texts');

    const modalElement = document.getElementById('upload-modal');
    const list = document.getElementById('upload-list');
    const overlay = document.getElementById('upload-drop-overlay');
    const progress = document.getElementById('upload-progress');
    const statusText = document.getElementById('upload-status');
    const bar = document.getElementById('upload-bar');
    const errorBox = document.getElementById('upload-error');
    const addMoreButton = document.getElementById('upload-add-more');
    const cancelButton = document.getElementById('upload-cancel');
    const confirmButton = document.getElementById('upload-confirm');

    let pending = [];
    let isUploading = false;
    const disabledReason = input.dataset.uploadDenied || null; // nothing may be uploaded here

    function formatSize(bytes) {
        const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
        let i = 0;
        while (bytes >= 1024 && i < units.length - 1) {
            bytes /= 1024;
            i++;
        }
        return (i === 0 ? bytes : bytes.toFixed(1)) + ' ' + units[i];
    }

    function render() {
        list.replaceChildren();
        for (const file of pending) {
            const item = document.createElement('li');
            item.className = 'list-group-item d-flex justify-content-between align-items-center gap-2';

            const name = document.createElement('span');
            name.className = 'text-break';
            name.textContent = file.name;
            const size = document.createElement('span');
            size.className = 'text-secondary small text-nowrap';
            size.textContent = formatSize(file.size);

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-sm p-0 text-secondary';
            remove.title = texts.remove;
            remove.disabled = isUploading;
            remove.innerHTML = '<i class="fa-solid fa-xmark"></i>';
            remove.addEventListener('click', function () {
                pending = pending.filter(f => f !== file);
                if (pending.length === 0)
                    closeModal();
                else
                    render();
            });

            const right = document.createElement('span');
            right.className = 'd-flex align-items-center gap-2';
            right.append(size, remove);
            item.append(name, right);
            list.appendChild(item);
        }
    }

    function setBusy(busy) {
        isUploading = busy;
        progress.hidden = !busy;
        for (const button of [addMoreButton, cancelButton, confirmButton])
            button.disabled = busy;
        render();
    }

    function showError(message) {
        errorBox.textContent = message;
        errorBox.hidden = false;
    }

    function openModal() {
        bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }

    function closeModal() {
        bootstrap.Modal.getOrCreateInstance(modalElement).hide();
    }

    function addFiles(files) {
        if (isUploading || files.length === 0)
            return;
        if (disabledReason) {
            notify(disabledReason, 'warning');
            return;
        }
        pending.push(...files);
        errorBox.hidden = true;
        render();
        openModal();
    }

    // Sends one chunk, retrying on network errors and server failures
    async function sendChunk(url, token, blob) {
        let lastError;
        for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token, 'Content-Type': 'application/octet-stream' },
                    body: blob
                });
                if (response.ok)
                    return;
                lastError = fillIn(texts.serverAnswered, { status: response.status });
                if (response.status >= 400 && response.status < 500) {
                    lastError = (await response.text()) || lastError; // e.g. a permission that is missing
                    break; // retrying won't help
                }
            } catch {
                lastError = texts.networkError;
            }
            await new Promise(resolve => setTimeout(resolve, 500 * attempt));
        }
        throw new Error(lastError);
    }

    // The server makes the id of an upload; it only works for the user who asked for it
    async function startUpload(token) {
        const response = await fetch(window.location.pathname + '?upload=start', {
            method: 'POST',
            headers: { 'RequestVerificationToken': token }
        });
        if (!response.ok)
            throw new Error(fillIn(texts.serverAnswered, { status: response.status }));
        return await response.text();
    }

    async function uploadFile(file, token, onBytes) {
        const id = await startUpload(token);
        const base = window.location.pathname + '?upload=' + id + '&name=' + encodeURIComponent(file.name);
        let offset = 0;
        try {
            // Do-while so that an empty file still sends its one (empty) chunk
            do {
                const end = Math.min(offset + CHUNK_SIZE, file.size);
                const isLast = end >= file.size;
                await sendChunk(base + '&offset=' + offset + (isLast ? '&last' : ''), token, file.slice(offset, end));
                onBytes(end - offset);
                offset = end;
            } while (offset < file.size);
        } catch (e) {
            // Best effort: don't leave the partial file behind
            fetch(window.location.pathname + '?upload=' + id + '&abort', { method: 'POST',
                headers: { 'RequestVerificationToken': token } }).catch(() => { });
            throw e;
        }
    }

    async function upload() {
        if (isUploading || pending.length === 0)
            return;

        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
        const total = pending.reduce((sum, f) => sum + f.size, 0);
        let done = 0;
        errorBox.hidden = true;
        setBusy(true);

        const queue = [...pending];
        for (let i = 0; i < queue.length; i++) {
            const file = queue[i];
            statusText.textContent = fillIn(texts.uploading, { name: file.name, number: i + 1, count: queue.length });
            try {
                await uploadFile(file, token, bytes => {
                    done += bytes;
                    bar.style.width = (total === 0 ? 100 : done / total * 100) + '%';
                });
            } catch (e) {
                // Keep the files that did not make it, so that Upload retries just those
                pending = queue.slice(i);
                setBusy(false);
                showError(fillIn(texts.failed, {
                    name: file.name,
                    reason: e.message,
                    uploaded: i === 0 ? '' : i === 1 ? texts.alreadyUploadedOne : fillIn(texts.alreadyUploadedMany, { count: i })
                }));
                return;
            }
            pending = pending.filter(f => f !== file);
        }

        window.removeEventListener('beforeunload', warnBeforeUnload);
        notifyAfterNavigation(queue.length === 1 ? texts.uploadedOne : fillIn(texts.uploadedMany, { count: queue.length }));
        window.location.reload();
    }

    function warnBeforeUnload(e) {
        if (isUploading)
            e.preventDefault();
    }
    window.addEventListener('beforeunload', warnBeforeUnload);

    document.getElementById('upload-button').addEventListener('click', () => input.click());
    addMoreButton.addEventListener('click', () => input.click());
    input.addEventListener('change', function () {
        addFiles([...input.files]);
        input.value = ''; // so that picking the same file again fires another change event
    });
    cancelButton.addEventListener('click', function () {
        pending = [];
        closeModal();
    });
    confirmButton.addEventListener('click', upload);

    // Drag and drop anywhere on the page
    const hasFiles = e => e.dataTransfer && [...e.dataTransfer.types].includes('Files');
    let dragDepth = 0;

    window.addEventListener('dragenter', function (e) {
        if (!hasFiles(e))
            return;
        e.preventDefault();
        if (++dragDepth === 1 && !isUploading && !disabledReason)
            overlay.hidden = false;
    });
    window.addEventListener('dragover', function (e) {
        if (hasFiles(e))
            e.preventDefault(); // marks the page as a drop target; also stops the browser from opening the file
    });
    window.addEventListener('dragleave', function (e) {
        if (hasFiles(e) && --dragDepth <= 0) {
            dragDepth = 0;
            overlay.hidden = true;
        }
    });
    window.addEventListener('drop', function (e) {
        if (!hasFiles(e))
            return;
        e.preventDefault();
        dragDepth = 0;
        overlay.hidden = true;

        // Dropped directories can't be read as files, so they are skipped
        const files = [];
        for (const item of e.dataTransfer.items) {
            const entry = item.webkitGetAsEntry ? item.webkitGetAsEntry() : null;
            if (item.kind === 'file' && (!entry || entry.isFile))
                files.push(item.getAsFile());
        }
        addFiles(files.filter(f => f !== null));
    });
})();
