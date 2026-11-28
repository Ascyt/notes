// Uploading to the current directory (_Upload.cshtml): the file picker, the drop target, the dialog with the list and the progress, and the
// Upload button of the toolbar. The size of the chunks, and why nothing may be uploaded here (if so), come from the server as data-
// attributes of the picker.
//
// Each file is first compared with the file of its name that is there (GET ?hashes, the SHA-256 of each piece): the same one is left out.
// Archives that are ticked are unzipped by the server once they are uploaded (in the background: the listing shows a bar for each).
// While it runs, the upload is kept in the browser's storage (upload-operation.ts), so that it can be continued after the tab was closed:
// the files are picked again then, and what was there already, or half sent, is not sent again.
(function () {
    // A file of the dialog: the file itself (null while it has to be picked again) with what is kept of it in the storage
    interface UploadItem extends StoredUploadFile {
        file: File | null;
    }

    const input = document.getElementById('upload-input') as HTMLInputElement;
    const CHUNK_SIZE = Number(input.dataset.chunkSize);
    const texts = textsOf('upload-texts');
    const operations = window.uploadOperation || null; // (only for those logged in, who are the only ones who can upload)
    const directory = window.location.pathname;

    const modalElement = document.getElementById('upload-modal')!;
    const list = document.getElementById('upload-list')!;
    const overlay = document.getElementById('upload-drop-overlay')!;
    const progress = document.getElementById('upload-progress')!;
    const statusText = document.getElementById('upload-status')!;
    const bar = document.getElementById('upload-bar')!;
    const detail = document.getElementById('upload-detail')!;
    const errorBox = document.getElementById('upload-error')!;
    const addMoreButton = document.getElementById('upload-add-more') as HTMLButtonElement;
    const cancelButton = document.getElementById('upload-cancel')!;
    const confirmButton = document.getElementById('upload-confirm') as HTMLButtonElement;
    const unzipField = document.getElementById('upload-unzip-field')!;
    const unzipAll = document.getElementById('upload-unzip-all') as HTMLInputElement;
    const resumeNote = document.getElementById('upload-resume-note')!;

    // What can be unzipped (UploadedArchives.FormatOf)
    const isArchive = (name: string) => /\.(zip|7z)$/i.test(name);

    // The files of the dialog, in order
    let items: UploadItem[] = [];
    let isUploading = false;
    let controller: AbortController | null = null; // stops the upload (Cancel)
    let release = () => { }; // lets go of the upload in the storage (upload-operation.ts)
    const disabledReason = input.dataset.uploadDenied || null; // nothing may be uploaded here

    const archivesToDo = () => items.filter(item => item.state === 'todo' && isArchive(item.name));

    function itemOf(file: File): UploadItem {
        const archives = archivesToDo();
        // A new archive is ticked as the others are (as the box above them is, at first)
        const unzip = archives.length === 0 ? true : archives.every(item => item.unzip) || (unzipAll.indeterminate ? true : unzipAll.checked);
        return { file, name: file.name, size: file.size, lastModified: file.lastModified, unzip: isArchive(file.name) && unzip, state: 'todo', id: null, offset: 0 };
    }

    // What is kept of it in the storage: all but the files themselves
    const stored = (): StoredUpload => ({ directory, files: items.map(({ file, ...rest }) => rest) });

    function persist(): void {
        if (operations)
            operations.save(stored());
    }

    function tagOf(item: UploadItem): HTMLElement | null {
        const label: [string, string] | null = item.state === 'done' ? [texts.done, 'success']
            : item.state === 'skipped' ? [texts.alreadyThere, 'secondary']
            : !item.file ? [texts.pickAgain, 'warning']
            : null;
        if (!label || !label[0])
            return null;
        const [text, kind] = label;
        const tag = document.createElement('span');
        tag.className = 'badge text-bg-' + kind + ' ms-2';
        tag.textContent = text;
        return tag;
    }

    function render(): void {
        list.replaceChildren();
        items.forEach(function (item, index) {
            const row = document.createElement('li');
            row.className = 'list-group-item d-flex justify-content-between align-items-center gap-2';

            const name = document.createElement('span');
            name.className = 'text-break';
            name.textContent = item.name;
            const tag = tagOf(item);
            if (tag)
                name.appendChild(tag);

            const right = document.createElement('span');
            right.className = 'd-flex align-items-center gap-2';
            // An archive that is still to be uploaded has its own box: unzipped once uploaded, or not
            if (isArchive(item.name) && item.state === 'todo') {
                const check = document.createElement('span');
                check.className = 'form-check m-0 small text-nowrap';
                const box = document.createElement('input');
                box.type = 'checkbox';
                box.className = 'form-check-input upload-unzip-one';
                box.id = 'upload-unzip-' + index;
                box.checked = item.unzip;
                box.disabled = isUploading;
                box.setAttribute('aria-label', fillIn(texts.unzipOneLabel, { name: item.name }));
                box.addEventListener('change', function () {
                    item.unzip = box.checked;
                    updateUnzipAll();
                });
                const label = document.createElement('label');
                label.className = 'form-check-label';
                label.htmlFor = box.id;
                label.textContent = texts.unzipOne;
                check.append(box, label);
                right.appendChild(check);
            }
            const size = document.createElement('span');
            size.className = 'text-secondary small text-nowrap';
            size.textContent = formatSize(item.size);

            const remove = document.createElement('button');
            remove.type = 'button';
            remove.className = 'btn btn-sm p-0 text-secondary';
            remove.title = texts.remove;
            remove.setAttribute('aria-label', texts.remove);
            remove.disabled = isUploading;
            remove.innerHTML = '<i class="fa-solid fa-xmark"></i>';
            remove.addEventListener('click', function () {
                items = items.filter(other => other !== item);
                // (What the server has of it is of no use now)
                if (item.id && item.state === 'todo')
                    uploadChunks.abort(directory, item.id);
                if (items.length === 0) {
                    discardStored();
                    closeModal();
                    return;
                }
                if (operations && operations.load())
                    persist();
                render();
            });

            right.append(size, remove);
            row.append(name, right);
            list.appendChild(row);
        });
        unzipField.hidden = archivesToDo().length === 0;
        unzipAll.disabled = isUploading;
        updateUnzipAll();
        confirmButton.disabled = isUploading || !items.some(item => item.state === 'todo' && item.file);
        resumeNote.hidden = !items.some(item => item.state === 'todo' && !item.file);
    }

    // The box above the archives: ticked if all are, a dash if some are
    function updateUnzipAll(): void {
        const archives = archivesToDo();
        const ticked = archives.filter(item => item.unzip).length;
        unzipAll.checked = archives.length > 0 && ticked === archives.length;
        unzipAll.indeterminate = ticked > 0 && ticked < archives.length;
    }
    unzipAll.addEventListener('change', function () {
        for (const item of archivesToDo())
            item.unzip = unzipAll.checked;
        render();
    });

    // While it uploads, only Cancel can be used (and not by Escape, which is too easily pressed): the dialog can't be closed otherwise
    function setBusy(busy: boolean): void {
        isUploading = busy;
        progress.hidden = !busy;
        addMoreButton.disabled = busy;
        if (busy)
            cancelButton.removeAttribute('key');
        else
            cancelButton.setAttribute('key', 'Escape');
        render();
    }

    function showError(message: string): void {
        errorBox.textContent = message;
        errorBox.hidden = false;
    }

    // The bar of the whole upload, and under it how far it got, in files and bytes
    function setBar(done: number, total: number, queue: UploadItem[]): void {
        const fraction = total === 0 ? 1 : Math.min(1, done / total);
        bar.style.width = fraction * 100 + '%';
        detail.textContent = describeProgress({
            phase: 'uploading', files: queue.filter(item => item.state !== 'todo').length, totalFiles: queue.length, bytes: done, totalBytes: total
        }, fraction);
    }

    function openModal(): void {
        bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }

    function closeModal(): void {
        bootstrap.Modal.getOrCreateInstance(modalElement).hide();
    }

    function discardStored(): void {
        if (operations && operations.load())
            operations.discard(stored());
    }

    function addFiles(files: File[]): void {
        if (isUploading || files.length === 0)
            return;
        if (disabledReason) {
            notify(disabledReason, 'warning');
            return;
        }
        for (const file of files) {
            // One of an upload that is continued, picked again: the same name and size (the time it was changed only decides between
            // two of those)
            const candidates = items.filter(item => !item.file && item.name === file.name && item.size === file.size);
            const again = candidates.find(item => item.lastModified === file.lastModified) || candidates[0];
            if (again)
                again.file = file;
            else
                items.push(itemOf(file));
        }
        errorBox.hidden = true;
        render();
        openModal();
    }

    const hex = (buffer: ArrayBuffer) => [...new Uint8Array(buffer)].map(b => b.toString(16).padStart(2, '0')).join('');

    // Whether the file of its name that is there is the same, piece by piece (the browser can't hash a large file in one go). Without the
    // browser's hashing (a page that isn't https), or if the server can't tell, it is uploaded (and gets a number if the name is taken).
    async function isThereAlready(item: UploadItem, signal: AbortSignal): Promise<boolean> {
        if (!(window.crypto && crypto.subtle))
            return false;
        let answer: { hashes?: string[] };
        try {
            const response = await fetch(directory + '?hashes=' + encodeURIComponent(item.name) + '&size=' + item.size + '&piece=' + CHUNK_SIZE,
                { headers: { 'Accept': 'application/json' }, signal });
            if (!response.ok)
                return false;
            answer = await response.json();
        } catch (e) {
            if (signal.aborted)
                throw e;
            return false;
        }
        const pieces = Math.max(1, Math.ceil(item.size / CHUNK_SIZE));
        if (!Array.isArray(answer.hashes) || answer.hashes.length !== pieces)
            return false;
        for (let i = 0; i < pieces; i++) {
            const piece = item.file!.slice(i * CHUNK_SIZE, Math.min((i + 1) * CHUNK_SIZE, item.size));
            const digest = hex(await crypto.subtle.digest('SHA-256', await piece.arrayBuffer()));
            if (signal.aborted)
                throw new DOMException('Aborted', 'AbortError');
            if (digest !== answer.hashes[i])
                return false;
        }
        return true;
    }

    async function upload(): Promise<void> {
        if (isUploading)
            return;
        const queue = items.filter(item => item.state === 'todo' && item.file);
        if (queue.length === 0)
            return;

        // What was sent before (of an upload that is continued) counts as done
        const total = queue.reduce((sum, item) => sum + item.size, 0);
        let done = queue.reduce((sum, item) => sum + (item.id ? item.offset : 0), 0);
        let uploaded = items.filter(item => item.state === 'done').length;
        let skipped = items.filter(item => item.state === 'skipped').length;
        errorBox.hidden = true;
        controller = new AbortController();
        const signal = controller.signal;
        setBusy(true);
        setBar(done, total, queue);
        persist();
        release = operations ? operations.hold(stored) : () => { };

        for (let n = 0; n < queue.length; n++) {
            const item = queue[n];
            try {
                if (!item.id) {
                    statusText.textContent = fillIn(texts.comparing, { name: item.name, number: n + 1, count: queue.length });
                    if (await isThereAlready(item, signal)) {
                        item.state = 'skipped';
                        skipped++;
                        done += item.size;
                        setBar(done, total, queue);
                        persist();
                        render();
                        continue;
                    }
                }
                statusText.textContent = fillIn(texts.uploading, { name: item.name, number: n + 1, count: queue.length });
                await uploadChunks.uploadFile(item.file!, {
                    directory,
                    chunkSize: CHUNK_SIZE,
                    texts,
                    unzip: isArchive(item.name) && item.unzip,
                    signal,
                    resume: item.id ? { id: item.id, offset: item.offset } : null,
                    onStarted: id => {
                        item.id = id;
                        item.offset = 0;
                        persist();
                    },
                    onBytes: (bytes, offset) => {
                        done += bytes;
                        item.offset = offset;
                        setBar(done, total, queue);
                        persist();
                    }
                });
                item.state = 'done';
                uploaded++;
                persist();
                render();
            } catch (e) {
                const error = e as Error & { status?: number };
                if (signal.aborted) {
                    cancelled(uploaded);
                    return;
                }
                // Refused: what the server had of it is gone. Otherwise it is kept, and Upload (or continuing later) goes on from there.
                if (error.status !== undefined && error.status >= 400 && error.status < 500) {
                    item.id = null;
                    item.offset = 0;
                }
                persist();
                release();
                setBusy(false);
                const there = uploaded + skipped;
                showError(fillIn(texts.failed, {
                    name: item.name,
                    reason: error.message,
                    uploaded: there === 0 ? '' : there === 1 ? texts.alreadyUploadedOne : fillIn(texts.alreadyUploadedMany, { count: there })
                }));
                return;
            }
        }

        release();
        if (operations)
            operations.clear();
        window.removeEventListener('beforeunload', warnBeforeUnload);
        const count = uploaded + skipped;
        notifyAfterNavigation(skipped === 0 ? (count === 1 ? texts.uploadedOne : fillIn(texts.uploadedMany, { count }))
            : uploaded === 0 ? fillIn(texts.nothingNew, { count })
            : fillIn(texts.uploadedWithSkipped, { count, skipped }));
        window.location.reload();
    }

    // Stopped by Cancel: what was uploaded stays, the rest (and what the server had of the file that was being sent) is discarded
    function cancelled(uploaded: number): void {
        release();
        setBusy(false);
        discardStored();
        items = [];
        closeModal();
        const message = fillIn(texts.cancelled, {
            uploaded: uploaded === 0 ? '' : uploaded === 1 ? texts.alreadyUploadedOne : fillIn(texts.alreadyUploadedMany, { count: uploaded })
        });
        if (uploaded === 0) {
            notify(message, 'info');
            return;
        }
        window.removeEventListener('beforeunload', warnBeforeUnload);
        notifyAfterNavigation(message, 'info');
        window.location.reload();
    }

    function warnBeforeUnload(e: BeforeUnloadEvent): void {
        if (isUploading)
            e.preventDefault();
    }
    window.addEventListener('beforeunload', warnBeforeUnload);

    document.getElementById('upload-button')!.addEventListener('click', () => input.click());
    addMoreButton.addEventListener('click', () => input.click());
    document.getElementById('upload-pick-again')!.addEventListener('click', () => input.click());
    input.addEventListener('change', function () {
        addFiles([...input.files!]);
        input.value = ''; // so that picking the same file again fires another change event
    });
    cancelButton.addEventListener('click', function () {
        if (isUploading) {
            controller!.abort();
            return;
        }
        discardStored();
        items = [];
        closeModal();
    });
    confirmButton.addEventListener('click', () => void upload());

    // ---- Continuing an upload that didn't finish (upload-operation.ts asked, and opened this directory for it)

    function resume(): void {
        const operation = operations && operations.load();
        if (!operation || operation.directory !== directory)
            return;
        if (disabledReason) {
            notify(disabledReason, 'warning');
            return;
        }
        items = operation.files.map(file => ({ ...file, file: null }));
        errorBox.hidden = true;
        render();
        openModal();
    }
    document.addEventListener('DOMContentLoaded', function () {
        if (operations && operations.takeResume() === directory)
            resume();
    });
    window.addEventListener('notes-upload-resume', function () {
        operations!.takeResume();
        resume();
    });

    // ---- Drag and drop anywhere on the page

    const hasFiles = (e: DragEvent) => Boolean(e.dataTransfer && [...e.dataTransfer.types].includes('Files'));
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
        const files: (File | null)[] = [];
        for (const item of e.dataTransfer!.items) {
            const entry = item.webkitGetAsEntry ? item.webkitGetAsEntry() : null;
            if (item.kind === 'file' && (!entry || entry.isFile))
                files.push(item.getAsFile());
        }
        addFiles(files.filter((f): f is File => f !== null));
    });
})();
