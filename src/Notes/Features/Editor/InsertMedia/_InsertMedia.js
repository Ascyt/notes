// Putting a file into a markdown file (_InsertMedia.cshtml): the toolbar's button opens the dialog, and so does a file pasted or dropped
// into either editor (with that file chosen already). The file is uploaded to the path of the dialog, relative to the markdown file (folders
// that aren't there are made by the server), and a link to it is written where the cursor was: an image, video or audio file is shown
// (![name](path), see MarkdownMedia), anything else is linked ([name](path)).
(function () {
    const modalElement = document.getElementById('insert-media-modal');
    const texts = textsOf('insert-media-texts');
    const CHUNK_SIZE = Number(modalElement.dataset.chunkSize);
    const DEFAULT_FOLDER = modalElement.dataset.defaultFolder;
    // The names of the directories the markdown file is in, from the root
    const fileDirectory = modalElement.dataset.filePath.split('/').filter(Boolean).slice(0, -1);

    const form = document.getElementById('insert-media-form');
    const fileInput = document.getElementById('insert-media-file');
    const preview = document.getElementById('insert-media-preview');
    const pathInput = document.getElementById('insert-media-path');
    const progress = document.getElementById('insert-media-progress');
    const statusText = document.getElementById('insert-media-status');
    const bar = document.getElementById('insert-media-bar');
    const errorBox = document.getElementById('insert-media-error');
    const cancelButton = document.getElementById('insert-media-cancel');
    const confirmButton = document.getElementById('insert-media-confirm');
    const monacoElement = document.getElementById('editor');
    const simpleElement = document.getElementById('simple-editor');

    let chosen = null;      // the file of the dialog
    let queue = [];         // further files that were dropped at once, each gets the dialog after this one
    let isUploading = false;
    let previewUrl = null;

    // ---- The file and its path ----

    const pad = number => String(number).padStart(2, '0');

    // What a pasted image is called by the browser ("image.png") says nothing, so it is named after the time, as new notes are
    function nameFor(file, isPasted) {
        const extension = /\.[^./]+$/.exec(file.name)?.[0] ?? (file.type.includes('/') ? '.' + file.type.split('/')[1].split('+')[0] : '');
        if (!isPasted || (file.name && !/^image\.\w+$/i.test(file.name)))
            return file.name || 'file' + extension;
        const now = new Date();
        return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}_${pad(now.getHours())}-${pad(now.getMinutes())}`
            + extension;
    }

    // Another file keeps the folder that is in the field, with the new name
    function choose(file, isPasted) {
        chosen = file;
        const folder = pathInput.value.includes('/') ? pathInput.value.slice(0, pathInput.value.lastIndexOf('/') + 1) : DEFAULT_FOLDER;
        pathInput.value = file ? folder + nameFor(file, isPasted) : '';
        showPreview(file);
        errorBox.hidden = true;
    }

    function showPreview(file) {
        if (previewUrl)
            URL.revokeObjectURL(previewUrl);
        previewUrl = null;
        preview.replaceChildren();
        const kind = file ? kindOf(file) : null;
        preview.hidden = !kind;
        if (!kind)
            return;
        previewUrl = URL.createObjectURL(file);
        const element = document.createElement(kind === 'image' ? 'img' : kind);
        element.src = previewUrl;
        if (kind === 'image')
            element.alt = '';
        else
            element.controls = true;
        preview.appendChild(element);
    }

    // What the preview shows of it: by its type, as the browser knows it. SVG isn't shown as an image (see MediaTypes), so it is linked.
    function kindOf(file) {
        const type = file.type;
        if (type.startsWith('image/') && type !== 'image/svg+xml')
            return 'image';
        if (type.startsWith('video/'))
            return 'video';
        if (type.startsWith('audio/'))
            return 'audio';
        return null;
    }

    // The path of the field as names from the root: relative to the markdown file, or to the root if it starts with "/". Throws the reason
    // if it can't be used.
    function resolve(path) {
        const names = path.startsWith('/') ? [] : [...fileDirectory];
        for (const name of path.split('/')) {
            if (name === '' || name === '.')
                continue;
            if (name === '..') {
                if (names.length === 0)
                    throw new Error(texts.outsideOfTheNotes);
                names.pop();
            } else
                names.push(name);
        }
        if (path.endsWith('/') || names.length === 0)
            throw new Error(texts.noFileName);
        return names;
    }

    const encodeName = name => encodeURIComponent(name).replace(/[()]/g, c => '%' + c.charCodeAt(0).toString(16).toUpperCase());

    // The address of the uploaded file, relative to the markdown file
    function relativeLink(virtualPath) {
        const names = virtualPath.split('/').filter(Boolean);
        let shared = 0;
        while (shared < fileDirectory.length && shared < names.length - 1 && fileDirectory[shared] === names[shared])
            shared++;
        const up = fileDirectory.length - shared;
        const rest = names.slice(shared).map(encodeName).join('/');
        return (up === 0 ? './' : '../'.repeat(up)) + rest;
    }

    // ---- The editors ----

    const simpleIsShown = () => !simpleElement.hidden;

    // Replaces what is selected in the editor that is shown (the cursor's place if nothing is) with the text, as typing would (so it can be
    // undone, and the page sees the change)
    function insertIntoEditor(text) {
        if (simpleIsShown()) {
            simpleElement.focus();
            if (!document.execCommand('insertText', false, text)) {
                simpleElement.setRangeText(text, simpleElement.selectionStart, simpleElement.selectionEnd, 'end');
                simpleElement.dispatchEvent(new Event('input'));
            }
            return true;
        }
        const editor = window.editor;
        if (!editor)
            return false;
        const selection = editor.getSelection();
        editor.executeEdits('insert-media', [{ range: selection, text, forceMoveMarkers: true }]);
        editor.pushUndoStop();
        editor.focus();
        return true;
    }

    function markdownFor(file, virtualPath) {
        const fileName = virtualPath.split('/').pop();
        const label = fileName.replace(/\.[^.]+$/, '').replace(/[\\\[\]]/g, c => '\\' + c);
        return (kindOf(file) ? '!' : '') + '[' + label + '](' + relativeLink(virtualPath) + ')';
    }

    // ---- The dialog ----

    function setBusy(busy) {
        isUploading = busy;
        progress.hidden = !busy;
        for (const control of [fileInput, pathInput, cancelButton, confirmButton])
            control.disabled = busy;
    }

    function showError(message) {
        errorBox.textContent = message;
        errorBox.hidden = false;
    }

    function open(file, isPasted) {
        if (isUploading)
            return;
        choose(file, isPasted);
        if (!file)
            fileInput.value = '';
        else if (window.DataTransfer) {
            // The field shows the file that was pasted or dropped (and keeps it if the dialog is opened again)
            try {
                const transfer = new DataTransfer();
                transfer.items.add(file);
                fileInput.files = transfer.files;
            } catch {
            }
        }
        bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }

    function close() {
        bootstrap.Modal.getOrCreateInstance(modalElement).hide();
    }

    // Opens the dialog for the files one after the other
    function openFor(files, isPasted) {
        if (files.length === 0)
            return;
        queue = files.slice(1);
        open(files[0], isPasted);
    }

    modalElement.addEventListener('shown.bs.modal', function () {
        (chosen ? pathInput : fileInput).focus();
        if (chosen) {
            // The name is what is most likely changed
            const start = pathInput.value.lastIndexOf('/') + 1;
            const end = pathInput.value.lastIndexOf('.');
            pathInput.setSelectionRange(start, end > start ? end : pathInput.value.length);
        }
    });
    modalElement.addEventListener('hidden.bs.modal', function () {
        showPreview(null);
        if (queue.length > 0)
            openFor(queue, false);
    });

    fileInput.addEventListener('change', () => choose(fileInput.files[0] || null, false));
    cancelButton.addEventListener('click', function () {
        queue = [];
        close();
    });

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        if (isUploading)
            return;
        errorBox.hidden = true;
        if (!chosen) {
            showError(texts.noFileChosen);
            return;
        }

        let names;
        try {
            names = resolve(pathInput.value.trim());
        } catch (error) {
            showError(error.message);
            return;
        }

        // Sent to the deepest directory that the markdown file and the path share, which is there; the rest are the folders the server makes
        const name = names.pop();
        let shared = 0;
        while (shared < fileDirectory.length && shared < names.length && fileDirectory[shared] === names[shared])
            shared++;
        const directory = '/' + names.slice(0, shared).map(encodeURIComponent).join('/');

        const file = chosen;
        setBusy(true);
        bar.style.width = '0';
        statusText.textContent = fillIn(texts.uploading, { name });
        let done = 0;
        try {
            const virtualPath = await uploadChunks.uploadFile(file, {
                directory,
                name,
                folder: names.slice(shared).join('/'),
                chunkSize: CHUNK_SIZE,
                texts,
                onBytes: bytes => {
                    done += bytes;
                    bar.style.width = (file.size === 0 ? 100 : done / file.size * 100) + '%';
                }
            });
            setBusy(false);
            close();
            if (!insertIntoEditor(markdownFor(file, virtualPath)))
                notify(texts.noEditor, 'warning');
        } catch (error) {
            setBusy(false);
            showError(fillIn(texts.failed, { name, reason: error.message }));
        }
    });

    function warnBeforeUnload(e) {
        if (isUploading)
            e.preventDefault();
    }
    window.addEventListener('beforeunload', warnBeforeUnload);

    onClickOf('insert-media-button', () => open(null, false));

    // ---- Pasting and dropping into the editors ----

    // Only what is nothing but files: copied cells of a spreadsheet, for instance, come as text and as a picture, and the text is meant
    const filesOf = transfer => transfer && [...transfer.types].includes('Files') && ![...transfer.types].includes('text/plain')
        ? [...transfer.files] : [];

    // Captured before the editor sees them, which would insert the file's name or nothing
    for (const element of [monacoElement, simpleElement]) {
        element.addEventListener('paste', function (e) {
            const files = filesOf(e.clipboardData);
            if (files.length === 0)
                return;
            e.preventDefault();
            e.stopPropagation();
            openFor(files, true);
        }, true);

        element.addEventListener('dragover', function (e) {
            if (!e.dataTransfer || ![...e.dataTransfer.types].includes('Files'))
                return;
            e.preventDefault(); // a drop target, instead of the browser opening the file
            e.stopPropagation();
            e.dataTransfer.dropEffect = 'copy';
        }, true);

        element.addEventListener('drop', function (e) {
            if (!e.dataTransfer || ![...e.dataTransfer.types].includes('Files'))
                return;
            e.preventDefault();
            e.stopPropagation();
            // The link goes where the file was dropped
            if (element === monacoElement && window.editor) {
                const target = window.editor.getTargetAtClientPoint(e.clientX, e.clientY);
                if (target && target.position)
                    window.editor.setPosition(target.position);
            }
            // Dropped directories can't be read as files, so they are skipped
            const files = [...e.dataTransfer.items]
                .filter(item => item.kind === 'file' && !(item.webkitGetAsEntry && item.webkitGetAsEntry()?.isDirectory))
                .map(item => item.getAsFile())
                .filter(Boolean);
            openFor(files, false);
        }, true);
    }
})();
