// The text editor (Monaco) of the pages that edit text: a file, and the permissions of an entry.
//
//   createEditor(element, {
//       basePath,         where Monaco is served from (the folder with loader.js)
//       value, language,  what it starts with, and how that is highlighted
//       uri,              if given, the editor edits a model of that name (needed to tie a JSON schema to it, see onBeforeCreate)
//       readOnly, readOnlyMessage,
//       onBeforeCreate    function(monaco), called before the editor is made, e.g. to set up a language
//   })                    returns a promise of the editor
//
// Every editor gets the same keys: Escape leaves it, Alt+Z toggles word wrap, Ctrl+Plus/Minus/0 zoom the editor (not the page) while it is focused.
//
// Monaco's loader defines a global AMD `define`, which makes the Bootstrap bundle register as a module
// instead of setting window.bootstrap. So Monaco is only loaded once all page scripts (incl. Bootstrap) have run.
(function () {
    let loading = null;

    function loadMonaco(basePath) {
        if (loading)
            return loading;

        // Absolute, as Monaco's web workers (which do the work for JSON, among others) are started from a blob: a relative path means nothing to them
        const base = new URL(basePath, document.baseURI).href.replace(/\/+$/, '');

        loading = new Promise(function (resolve, reject) {
            function load() {
                const loader = document.createElement('script');
                loader.src = base + '/loader.js';
                loader.onload = function () {
                    require.config({ paths: { vs: base } });
                    require(['vs/editor/editor.main'], function () { resolve(); }, reject);
                };
                loader.onerror = reject;
                document.head.appendChild(loader);
            }

            if (document.readyState === 'loading')
                document.addEventListener('DOMContentLoaded', load);
            else
                load();
        });
        return loading;
    }

    function bindKeys(editor) {
        // Escape leaves the editor, unless Monaco needs it to close a widget or drop extra cursors
        editor.addCommand(monaco.KeyCode.Escape, function () {
            document.activeElement.blur();
        }, '!findWidgetVisible && !suggestWidgetVisible && !parameterHintsVisible && !inSnippetMode && !renameInputVisible && !hasMultipleSelections && !editorHasSelection');

        // Alt+Z toggles word wrap
        editor.addCommand(monaco.KeyMod.Alt | monaco.KeyCode.KeyZ, function () {
            const isOn = editor.getOption(monaco.editor.EditorOption.wordWrap) !== 'off';
            editor.updateOptions({ wordWrap: isOn ? 'off' : 'on' });
        });

        // Ctrl+Plus / Ctrl+Minus / Ctrl+0 zoom the editor instead of the page while it is focused
        const zoomBindings = [
            ['editor.action.fontZoomIn', [monaco.KeyCode.Equal, monaco.KeyCode.NumpadAdd]],
            ['editor.action.fontZoomOut', [monaco.KeyCode.Minus, monaco.KeyCode.NumpadSubtract]],
            ['editor.action.fontZoomReset', [monaco.KeyCode.Digit0, monaco.KeyCode.Numpad0]]
        ];
        for (const [actionId, keys] of zoomBindings) {
            for (const key of keys) {
                for (const shift of [0, monaco.KeyMod.Shift]) {
                    editor.addCommand(monaco.KeyMod.CtrlCmd | shift | key, function () {
                        editor.getAction(actionId).run();
                    });
                }
            }
        }
    }

    window.createEditor = async function (element, options) {
        await loadMonaco(options.basePath);
        if (options.onBeforeCreate)
            options.onBeforeCreate(monaco);

        const settings = {
            theme: 'vs',
            readOnly: Boolean(options.readOnly),
            readOnlyMessage: { value: options.readOnlyMessage || '' },
            automaticLayout: true,
            wordWrap: 'on',
            mouseWheelZoom: true // Ctrl+scroll zooms the editor
        };
        if (options.uri)
            settings.model = monaco.editor.createModel(options.value, options.language, monaco.Uri.parse(options.uri));
        else {
            settings.value = options.value;
            settings.language = options.language;
        }

        const editor = monaco.editor.create(element, settings);
        bindKeys(editor);
        return editor;
    };
})();
