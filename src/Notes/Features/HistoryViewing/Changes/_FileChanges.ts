// The changes of a commit to a text file (_FileChanges): both sides in Monaco's diff editor, or only the one that may be seen in the
// read-only editor (see editor.ts). What the page knows about them comes from #file-changes-options.
(function () {
    interface ChangesOptions {
        monacoPath: string;
        showsBoth: boolean;
        original: string;
        modified: string;
        language: string;
        readOnlyMessage: string;
    }

    const options: ChangesOptions = JSON.parse(document.getElementById('file-changes-options')!.textContent!);
    const element = document.getElementById('changes-editor')!;
    let editor: MonacoCodeEditor | MonacoDiffEditor | null = null;

    // The element's key shortcut (see shortcuts.ts) focuses the side as the commit left it
    element.addEventListener('shortcut', function (e) {
        e.preventDefault();
        if (editor)
            (options.showsBoth ? (editor as MonacoDiffEditor).getModifiedEditor() : (editor as MonacoCodeEditor)).focus();
    });

    const made: Promise<MonacoCodeEditor | MonacoDiffEditor> = options.showsBoth
        ? createDiffEditor(element, {
            basePath: options.monacoPath,
            original: options.original,
            modified: options.modified,
            language: options.language,
            readOnlyMessage: options.readOnlyMessage
        })
        : createEditor(element, {
            basePath: options.monacoPath,
            value: options.modified,
            language: options.language,
            readOnly: true,
            readOnlyMessage: options.readOnlyMessage
        });
    void made.then(function (created) {
        editor = created;
        window.changesEditor = created;
    });
})();
