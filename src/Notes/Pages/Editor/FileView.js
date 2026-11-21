// The editor of a text file: the code editor (Monaco, see editor.js) or the simple view, a plain text field for phones, saving, and
// what the components of the page (rename) ask of it. What the page knows about the file comes from #file-editor-options.
// The toolbar's buttons run saveFile(), toggleSimpleView() and indentSimpleView(outdent), which are also there for other scripts; the
// way to the preview leads to the cursor's place.
(function () {
    const SIMPLE_VIEW_KEY = 'notes.simpleEditor';
    const options = JSON.parse(document.getElementById('file-editor-options').textContent);
    const canWrite = options.canWrite; // otherwise the editor is read-only and Save stays disabled
    const texts = textsOf('editor-texts');
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
    // Enter goes on with a list (markdown-lists.js)
    const continuesLists = canWrite && options.language === 'markdown';
    if (continuesLists)
        markdownLists.bindToTextArea(simpleElement);

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
            setButtonDisabled(document.getElementById('save-button'), isDirty ? null : texts.nothingToSave);
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
            notify(message || fillIn(texts.saveFailed, { status: response.status }), 'danger');
            return;
        }
        savedValue = normalize(value);
        updateDirty();
    };
    onClickOf('save-button', () => window.saveFile());

    // ---- The two views ----

    async function showMonaco() {
        simpleElement.hidden = true;
        monacoElement.hidden = false;
        fitEditorAboveKeyboard();

        if (!monacoLoading)
            monacoLoading = createEditor(monacoElement, {
                basePath: options.monacoPath,
                value: options.contents,
                language: options.language,
                readOnly: !canWrite,
                readOnlyMessage: options.readOnlyMessage,
                suggestsWords: options.suggestsWords
            }).then(function (editor) {
                monacoEditor = editor;
                window.editor = editor;
                editor.onDidChangeModelContent(updateDirty);
                if (continuesLists)
                    markdownLists.bindToMonaco(editor);
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
        fitEditorAboveKeyboard();
    }

    // ---- Indenting in the simple view ----

    // Only shown in the simple view. Disabled in the code editor too, so that their shortcuts are left to Monaco, which has the same.
    const indentButtons = ['indent-button', 'outdent-button'].map(id => document.getElementById(id)).filter(Boolean);
    for (const button of indentButtons)
        button.addEventListener('mousedown', e => e.preventDefault()); // the text field keeps the focus, and a phone its keyboard

    function updateIndentButtons() {
        for (const button of indentButtons) {
            button.hidden = !useSimple;
            setButtonDisabled(button, useSimple ? null : texts.codeEditorIndents);
        }
    }

    window.indentSimpleView = function (outdent) {
        if (!useSimple || !canWrite)
            return;
        if (outdent)
            textAreaIndent.outdent(simpleElement);
        else
            textAreaIndent.indent(simpleElement);
    };
    onClickOf('indent-button', () => window.indentSimpleView(false));
    onClickOf('outdent-button', () => window.indentSimpleView(true));

    // ---- The way to the preview ----

    // Leads to where the cursor is, in whichever view is shown
    const previewButton = document.getElementById('preview-button');
    if (previewButton)
        previewButton.addEventListener('click', function () {
            let place = null;
            if (useSimple)
                place = editorStartPosition.placeInTextArea(simpleElement);
            else if (monacoEditor && monacoEditor.getPosition()) {
                const position = monacoEditor.getPosition();
                place = { line: position.lineNumber, column: position.column };
            }
            if (place)
                editorStartPosition.linkToPreview(previewButton, place);
        });

    // The button leads to the other view, so its icon is that of the other view
    function updateViewButton() {
        updateIndentButtons();
        const icon = viewButton.querySelector('.btn-icon');
        icon.classList.remove('fa-solid', 'fa-regular', 'fa-mobile-screen-button', 'fa-keyboard');
        icon.classList.add(...(useSimple ? ['fa-regular', 'fa-keyboard'] : ['fa-solid', 'fa-mobile-screen-button']));
        const label = useSimple ? texts.switchToCodeEditor : texts.switchToSimpleEditor;
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
    onClickOf('simple-view-button', () => window.toggleSimpleView());

    // ---- Room above a phone's keyboard ----

    // With a phone's keyboard open, only what is above it can be seen (the visual viewport), and typing at the bottom of an editor that goes
    // on under the keyboard scrolls the whole page, so the breadcrumb and the toolbar go out of sight. So the editor ends above the keyboard
    // (or the bottom of a low window) instead, and the page stays at its top while the editor has the focus.
    const NORMAL_HEIGHT = 0.75; // of the window, as the page's style has it
    const MIN_HEIGHT = 80;      // px, so that there is always some of it
    const BOTTOM_GAP = 8;       // px between the editor and the keyboard

    function fitEditorAboveKeyboard() {
        const viewport = window.visualViewport;
        const shown = useSimple ? simpleElement : monacoElement;
        if (!viewport || shown.hidden)
            return;

        // Where the editor starts with the page at its top, and how much room that leaves
        const top = shown.getBoundingClientRect().top + window.scrollY;
        const room = viewport.height - top - BOTTOM_GAP;
        const isCramped = room < window.innerHeight * NORMAL_HEIGHT;
        for (const element of [simpleElement, monacoElement])
            element.style.height = isCramped ? Math.max(MIN_HEIGHT, room) + 'px' : '';

        if (isCramped && shown.contains(document.activeElement)) {
            window.scrollTo(0, 0);
            // The cursor may now be under the new bottom of the editor
            if (!useSimple && monacoEditor && monacoEditor.getPosition()) {
                monacoEditor.layout();
                monacoEditor.revealPositionInCenterIfOutsideViewport(monacoEditor.getPosition());
            }
        }
    }

    if (window.visualViewport)
        window.visualViewport.addEventListener('resize', fitEditorAboveKeyboard);
    window.addEventListener('resize', fitEditorAboveKeyboard);
    // Opening the keyboard may come before or after the focus does
    simpleElement.addEventListener('focus', fitEditorAboveKeyboard);
    monacoElement.addEventListener('focusin', fitEditorAboveKeyboard);

    // ---- The other components of the page ----

    window.beforeRename = () => {
        if (isDirty || isSaving) {
            notify(texts.unsavedBeforeRenaming, 'warning');
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
