// The editor of the configuration of a directory or file (ConfigEditorView.cshtml, which says what it does). What the page knows
// about it comes from #config-editor-options: the text, the default (for Reset), the schema and where Monaco is served from.
(function () {
    interface ConfigEditorOptions {
        monacoPath: string;
        contents: string;
        defaultContents: string;
        schema: string;
    }

    const options: ConfigEditorOptions = JSON.parse(document.getElementById('config-editor-options')!.textContent!);
    const texts = textsOf('config-editor-texts');
    const errorBox = document.getElementById('config-errors')!;
    const saveButton = document.getElementById('save-button')!;
    let savedVersionId: number | null = null;
    let isDirty = false;
    let isSaving = false;

    // The editor's key shortcut (see shortcuts.ts) focuses it
    document.getElementById('editor')!.addEventListener('shortcut', function (e) {
        e.preventDefault();
        if (window.editor)
            window.editor.focus();
    });

    function updateDirty(): void {
        isDirty = window.editor!.getModel()!.getAlternativeVersionId() !== savedVersionId;
        setButtonDisabled(saveButton, isDirty ? null : texts.nothingToSave);
    }

    function setSaving(saving: boolean): void {
        isSaving = saving;
        setButtonBusy(saveButton, saving);
    }

    // With nothing to save the Save button (and so its shortcut) does nothing, but the browser's own "save page" is never what is meant
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 's')
            e.preventDefault();
    });

    // Replaces everything in the editor, in a way that can be undone
    function replaceAll(text: string): void {
        const editor = window.editor!;
        const model = editor.getModel()!;
        editor.pushUndoStop();
        editor.executeEdits('config', [{ range: model.getFullModelRange(), text: text }]);
        editor.pushUndoStop();
    }

    // Puts the default into the editor. Nothing is saved until Save.
    function resetConfig(): void {
        if (!window.editor)
            return;

        replaceAll(options.defaultContents);
        window.editor.focus();
    }

    // The text is sent as it is; the server checks all of it and says what is wrong, or answers with what it saved, which may have more
    // in it than what was sent (entries for new files).
    async function save(): Promise<void> {
        if (!window.editor || isSaving)
            return;

        setSaving(true);
        errorBox.classList.add('d-none');
        try {
            const token = document.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')!.value;
            let response: Response | undefined; // undefined: the server wasn't reached
            try {
                response = await fetch(window.location.pathname + '?config', {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token, 'Content-Type': 'text/plain; charset=utf-8' },
                    body: window.editor.getValue()
                });
            } catch {
                response = undefined;
            }

            const message = response ? await response.text() : '';
            const status = response ? response.status : 'network error';
            if (!response || !response.ok) {
                errorBox.textContent = message || fillIn(texts.saveFailed, { status });
                errorBox.classList.remove('d-none');
                notify(status === 400 ? texts.notValid : (message.split('\n')[0] || fillIn(texts.saveFailed, { status })), 'danger');
                return;
            }

            if (window.editor.getValue().replace(/\r\n/g, '\n') !== message)
                replaceAll(message);
            // Saved, but something about it is worth knowing (archiving inside an archived folder, say)
            const warning = response.headers.get('X-Notes-Config-Warning');
            if (warning)
                notify(decodeURIComponent(warning), 'warning');
            savedVersionId = window.editor.getModel()!.getAlternativeVersionId();
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
    void createEditor(document.getElementById('editor')!, {
        basePath: options.monacoPath,
        value: options.contents,
        language: 'json',
        uri: 'inmemory://app/config.json',
        // Comments are allowed in the configuration, and the schema is what completes and checks them while typing
        onBeforeCreate: function (monaco) {
            monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
                validate: true,
                allowComments: true,
                comments: 'ignore',
                trailingCommas: 'error',
                schemas: [{ uri: 'inmemory://app/config-schema.json', fileMatch: ['inmemory://app/config.json'], schema: JSON.parse(options.schema) }]
            });
        }
    }).then(function (editor) {
        window.editor = editor;
        savedVersionId = editor.getModel()!.getAlternativeVersionId();
        editor.onDidChangeModelContent(updateDirty);
        editor.focus();
    });
})();
