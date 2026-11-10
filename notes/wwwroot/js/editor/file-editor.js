// The editor of a text file: the code editor (Monaco, see editor.js) or the simple view, a plain text field for phones, saving, and
// what the components of the page (delete, rename) ask of it. What the page knows about the file comes from #file-editor-options.
// The toolbar's buttons call saveFile() and toggleSimpleView().
(function () {
    const SIMPLE_VIEW_KEY = 'notes.simpleEditor';
    const options = JSON.parse(document.getElementById('file-editor-options').textContent);
    const canWrite = options.canWrite; // otherwise the editor is read-only and Save stays disabled
    const monacoElement = document.getElementById('editor');
    const simpleElement = document.getElementById('simple-editor');
    const viewButton = document.getElementById('simple-view-button');
    const normalize = text => text.replace(/\r\n?/g, '\n');

    let monacoEditor = null;  // only there once Monaco has loaded, which it does only when it is shown (it is a lot to load on a phone)
    let monacoLoading = null;
    let useSimple = readSimpleView();
    let savedValue = normalize(options.contents); // what the file is, as far as this page knows (line breaks as \n, as in the text field)
    let isDirty = false;
    let isSaving = false;

    simpleElement.value = options.contents;
    simpleElement.readOnly = !canWrite;

    // localStorage can throw (blocked storage, private mode), and then the choice just isn't kept
    function readSimpleView() {
        try {
            return localStorage.getItem(SIMPLE_VIEW_KEY) === 'true';
        } catch {
            return false;
        }
    }

    function writeSimpleView(simple) {
        try {
            localStorage.setItem(SIMPLE_VIEW_KEY, String(simple));
        } catch {
        }
    }

    // The contents of whichever view is shown
    function currentValue() {
        return useSimple || !monacoEditor ? simpleElement.value : monacoEditor.getValue();
    }

    function updateDirty() {
        isDirty = normalize(currentValue()) !== savedValue;
        if (canWrite)
            setButtonDisabled(document.getElementById('save-button'), isDirty ? null : 'There is nothing to save.');
    }

    function setSaving(saving) {
        isSaving = saving;
        setButtonBusy(document.getElementById('save-button'), saving);
    }

    // The editor's key shortcut (see shortcuts.js) focuses it
    monacoElement.addEventListener('shortcut', function (e) {
        e.preventDefault();
        if (monacoEditor)
            monacoEditor.focus();
    });
    simpleElement.addEventListener('shortcut', function (e) {
        e.preventDefault(); // not the default, which would select all of it
        simpleElement.focus();
    });
    simpleElement.addEventListener('input', updateDirty);

    // One zoom for both views (see editor.js), so switching keeps the size
    editorZoom.bindZoomKeys(simpleElement);
    editorZoom.onChange(function (factor) {
        simpleElement.style.fontSize = factor + 'rem';
    });

    // ---- Saving ----

    // With nothing to save the Save button (and so its shortcut) does nothing, but the browser's own "save page" is never what is meant
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 's')
            e.preventDefault();
    });

    window.saveFile = async function () {
        if (isSaving || (!useSimple && !monacoEditor))
            return;

        setSaving(true);

        const value = currentValue();
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
        let response;
        try {
            response = await fetch(window.location.pathname, {
                method: 'POST',
                headers: { 'RequestVerificationToken': token, 'Content-Type': 'text/plain; charset=utf-8' },
                body: value
            });
        } catch {
            response = { ok: false, status: 'network error' };
        } finally {
            setSaving(false);
        }
        if (!response.ok) {
            const message = typeof response.text === 'function' ? await response.text() : '';
            notify(message || ('Failed to save file (' + response.status + ').'), 'danger');
            return;
        }
        savedValue = normalize(value);
        updateDirty();
    };

    // ---- The two views ----

    async function showMonaco() {
        simpleElement.hidden = true;
        monacoElement.hidden = false;

        if (!monacoLoading)
            monacoLoading = createEditor(monacoElement, {
                basePath: options.monacoPath,
                value: options.contents,
                language: options.language,
                readOnly: !canWrite,
                readOnlyMessage: options.readOnlyMessage
            }).then(function (editor) {
                monacoEditor = editor;
                window.editor = editor;
                editor.onDidChangeModelContent(updateDirty);
                return editor;
            });

        const editor = await monacoLoading;
        if (useSimple)
            return; // the simple view was chosen again while Monaco loaded

        // What was typed in the simple view meanwhile
        if (normalize(editor.getValue()) !== simpleElement.value)
            editor.setValue(simpleElement.value);
        editor.focus();
    }

    function showSimple() {
        if (monacoEditor)
            simpleElement.value = monacoEditor.getValue();
        monacoElement.hidden = true;
        simpleElement.hidden = false;
    }

    // The button leads to the other view, so its icon is that of the other view
    function updateViewButton() {
        const icon = viewButton.querySelector('.btn-icon');
        icon.classList.remove('fa-solid', 'fa-regular', 'fa-mobile-screen-button', 'fa-keyboard');
        icon.classList.add(...(useSimple ? ['fa-regular', 'fa-keyboard'] : ['fa-solid', 'fa-mobile-screen-button']));
        const label = useSimple ? 'Switch to the code editor' : 'Switch to the simple editor (for phones)';
        viewButton.dataset.label = label;
        viewButton.dataset.leadsTo = useSimple ? 'code' : 'simple';
        viewButton.setAttribute('aria-label', label);
        refreshButton(viewButton);
    }

    window.toggleSimpleView = function () {
        useSimple = !useSimple;
        writeSimpleView(useSimple);
        updateViewButton();
        if (useSimple)
            showSimple();
        else
            showMonaco();
        updateDirty();
    };

    // ---- The other components of the page ----

    // Deleting needs confirmation unless the file is empty and has no unsaved changes
    window.needsDeleteConfirmation = () => (!useSimple && !monacoEditor) || isDirty || currentValue() !== '';
    window.onEntryDeleted = () => { isDirty = false; };
    window.beforeRename = () => {
        if (isDirty || isSaving) {
            notify('The file has unsaved changes, which have to be saved before renaming.', 'warning');
            return false;
        }
    };

    // Built-in browser prompt when leaving with unsaved changes
    window.addEventListener('beforeunload', function (e) {
        if (isDirty)
            e.preventDefault();
    });

    // ---- Start ----

    // Last, as it needs everything above. Monaco is made once it has loaded.
    const startPosition = editorStartPosition.read();
    updateViewButton();
    if (useSimple) {
        showSimple();
        if (startPosition)
            editorStartPosition.goToInTextArea(simpleElement, startPosition);
    } else {
        showMonaco().then(function () {
            if (startPosition && !useSimple)
                editorStartPosition.goToInMonaco(monacoEditor, startPosition);
        });
    }
})();
