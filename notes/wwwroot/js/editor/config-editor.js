// The editor of the configuration of a directory or file (ConfigEditorView.cshtml, which says what it does). What the page knows
// about it comes from #config-editor-options: the text, the default (for Reset), the schema and where Monaco is served from.
(function () {
    const options = JSON.parse(document.getElementById('config-editor-options').textContent);
    const errorBox = document.getElementById('config-errors');
    let savedVersionId = null;
    let isDirty = false;
    let isSaving = false;

    // The editor's key shortcut (see shortcuts.js) focuses it
    document.getElementById('editor').addEventListener('shortcut', function (e) {
        e.preventDefault();
        if (window.editor)
            window.editor.focus();
    });

    function updateDirty() {
        isDirty = window.editor.getModel().getAlternativeVersionId() !== savedVersionId;
        setButtonDisabled(document.getElementById('save-button'), isDirty ? null : 'There is nothing to save.');
    }

    function setSaving(saving) {
        isSaving = saving;
        setButtonBusy(document.getElementById('save-button'), saving);
    }

    // With nothing to save the Save button (and so its shortcut) does nothing, but the browser's own "save page" is never what is meant
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 's')
            e.preventDefault();
    });

    // Replaces everything in the editor, in a way that can be undone
    function replaceAll(text) {
        const model = window.editor.getModel();
        window.editor.pushUndoStop();
        window.editor.executeEdits('config', [{ range: model.getFullModelRange(), text: text }]);
        window.editor.pushUndoStop();
    }

    // Puts the default into the editor. Nothing is saved until Save.
    function resetConfig() {
        if (!window.editor)
            return;

        replaceAll(options.defaultContents);
        window.editor.focus();
    }

    // The text is sent as it is; the server checks all of it and says what is wrong, or answers with what it saved, which may have more
    // in it than what was sent (entries for new files).
    async function save() {
        if (!window.editor || isSaving)
            return;

        setSaving(true);
        errorBox.classList.add('d-none');
        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            let response;
            try {
                response = await fetch(window.location.pathname + '?config', {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token, 'Content-Type': 'text/plain; charset=utf-8' },
                    body: window.editor.getValue()
                });
            } catch {
                response = { ok: false, status: 'network error' };
            }

            const message = typeof response.text === 'function' ? await response.text() : '';
            if (!response.ok) {
                errorBox.textContent = message || ('Failed to save (' + response.status + ').');
                errorBox.classList.remove('d-none');
                notify(response.status === 400 ? 'The configuration is not valid.' : (message.split('\n')[0] || 'Failed to save.'), 'danger');
                return;
            }

            if (window.editor.getValue().replace(/\r\n/g, '\n') !== message)
                replaceAll(message);
            savedVersionId = window.editor.getModel().getAlternativeVersionId();
            updateDirty();
        } finally {
            setSaving(false);
        }
    }

    onClickOf('save-button', save);
    onClickOf('reset-button', resetConfig);

    // Built-in browser prompt when leaving with unsaved changes
    window.addEventListener('beforeunload', function (e) {
        if (isDirty)
            e.preventDefault();
    });

    // Last, as it needs everything above. The editor itself is made once Monaco has loaded.
    createEditor(document.getElementById('editor'), {
        basePath: options.monacoPath,
        value: options.contents,
        language: 'json',
        uri: 'inmemory://notes/config.json',
        // Comments are allowed in the configuration, and the schema is what completes and checks them while typing
        onBeforeCreate: function (monaco) {
            monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
                validate: true,
                allowComments: true,
                comments: 'ignore',
                trailingCommas: 'error',
                schemas: [{ uri: 'inmemory://notes/config-schema.json', fileMatch: ['inmemory://notes/config.json'], schema: JSON.parse(options.schema) }]
            });
        }
    }).then(function (editor) {
        window.editor = editor;
        savedVersionId = editor.getModel().getAlternativeVersionId();
        editor.onDidChangeModelContent(updateDirty);
        editor.focus();
    });
})();
