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
// Every editor gets the same keys: Escape leaves it, Alt+Z toggles word wrap, Ctrl+Plus/Minus/0 (and Ctrl+scroll) zoom the editor (not the page) while it is focused.
//
// The zoom is shared by all editors of the page, also by text fields that are editors (see bindZoomKeys), and kept in the browser:
//   editorZoom.zoomIn(), zoomOut(), reset()
//   editorZoom.factor             1 is the usual size
//   editorZoom.canZoomIn, canZoomOut
//   editorZoom.onChange(listener) called with the factor now and on every change
//   editorZoom.bindZoomKeys(element)  Ctrl+Plus/Minus/0 and Ctrl+scroll on that element zoom
// The zoom buttons of the toolbar, if the page has them, are disabled at the limits. So this script comes after the toolbar.
//
// Monaco's loader defines a global AMD `define`, which makes the Bootstrap bundle register as a module
// instead of setting window.bootstrap. So Monaco is only loaded once all page scripts (incl. Bootstrap) have run.
(function () {
    const ZOOM_KEY = 'notes.editorZoom';
    const ZOOM_STEP = 0.1;
    const MIN_ZOOM_LEVEL = -5;
    const MAX_ZOOM_LEVEL = 10;
    const MONACO_FONT_SIZE = 14; // explicit, as Monaco's own default differs between systems

    // The zoom in steps of ZOOM_STEP, so that zooming in and out again gives exactly the size it was
    let zoomLevel = readZoomLevel();
    const zoomListeners = [];

    // localStorage can throw (blocked storage, private mode), and then the zoom just isn't kept
    function readZoomLevel() {
        try {
            const level = parseInt(localStorage.getItem(ZOOM_KEY), 10);
            return Number.isNaN(level) ? 0 : Math.min(MAX_ZOOM_LEVEL, Math.max(MIN_ZOOM_LEVEL, level));
        } catch {
            return 0;
        }
    }

    function setZoomLevel(level) {
        level = Math.min(MAX_ZOOM_LEVEL, Math.max(MIN_ZOOM_LEVEL, level));
        if (level === zoomLevel)
            return;
        zoomLevel = level;
        try {
            localStorage.setItem(ZOOM_KEY, String(level));
        } catch {
        }
        for (const listener of zoomListeners)
            listener(window.editorZoom.factor);
    }

    // Is the key Plus, Minus or 0, with Ctrl (and Shift or not, as Plus needs Shift on some layouts)?
    function zoomKeyAction(e) {
        if (!(e.ctrlKey || e.metaKey) || e.altKey)
            return null;
        if (e.key === '+' || e.key === '=' || e.code === 'NumpadAdd')
            return 'in';
        if (e.key === '-' || e.code === 'NumpadSubtract')
            return 'out';
        if (e.key === '0' || e.code === 'Numpad0')
            return 'reset';
        return null;
    }

    window.editorZoom = {
        get factor() { return 1 + zoomLevel * ZOOM_STEP; },
        get canZoomIn() { return zoomLevel < MAX_ZOOM_LEVEL; },
        get canZoomOut() { return zoomLevel > MIN_ZOOM_LEVEL; },
        zoomIn() { setZoomLevel(zoomLevel + 1); },
        zoomOut() { setZoomLevel(zoomLevel - 1); },
        reset() { setZoomLevel(0); },
        onChange(listener) {
            zoomListeners.push(listener);
            listener(this.factor);
        },
        bindZoomKeys(element) {
            element.addEventListener('keydown', function (e) {
                const action = zoomKeyAction(e);
                if (!action)
                    return;
                e.preventDefault();
                e.stopPropagation();
                if (action === 'in')
                    window.editorZoom.zoomIn();
                else if (action === 'out')
                    window.editorZoom.zoomOut();
                else
                    window.editorZoom.reset();
            }, true);
            // Instead of the browser's zoom of the whole page
            element.addEventListener('wheel', function (e) {
                if (!e.ctrlKey || e.deltaY === 0)
                    return;
                e.preventDefault();
                e.stopPropagation();
                if (e.deltaY < 0)
                    window.editorZoom.zoomIn();
                else
                    window.editorZoom.zoomOut();
            }, { capture: true, passive: false });
        }
    };

    // The zoom buttons of the toolbar (ToolbarButton.ZoomIn/ZoomOut), on the pages that have them
    window.editorZoom.onChange(function () {
        const zoomInButton = document.getElementById('zoom-in-button');
        const zoomOutButton = document.getElementById('zoom-out-button');
        if (zoomInButton)
            setButtonDisabled(zoomInButton, window.editorZoom.canZoomIn ? null : 'The editor is zoomed in as far as it goes.');
        if (zoomOutButton)
            setButtonDisabled(zoomOutButton, window.editorZoom.canZoomOut ? null : 'The editor is zoomed out as far as it goes.');
    });

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
    }

    // Monaco's theme follows that of the page (see theme.js); it is global to all editors
    function monacoTheme() {
        return window.getTheme() === 'dark' ? 'vs-dark' : 'vs';
    }

    window.addEventListener('themechange', function () {
        if (window.monaco)
            monaco.editor.setTheme(monacoTheme());
    });

    window.createEditor = async function (element, options) {
        await loadMonaco(options.basePath);
        if (options.onBeforeCreate)
            options.onBeforeCreate(monaco);

        const settings = {
            theme: monacoTheme(),
            readOnly: Boolean(options.readOnly),
            readOnlyMessage: { value: options.readOnlyMessage || '' },
            automaticLayout: true,
            wordWrap: 'on',
            fontSize: Math.round(MONACO_FONT_SIZE * window.editorZoom.factor)
            // Monaco's own zoom (mouseWheelZoom, its zoom actions) is left off: it isn't kept, and would add to this one
        };
        if (options.uri)
            settings.model = monaco.editor.createModel(options.value, options.language, monaco.Uri.parse(options.uri));
        else {
            settings.value = options.value;
            settings.language = options.language;
        }

        const editor = monaco.editor.create(element, settings);
        bindKeys(editor);
        window.editorZoom.bindZoomKeys(element);
        window.editorZoom.onChange(function (factor) {
            editor.updateOptions({ fontSize: Math.round(MONACO_FONT_SIZE * factor) });
        });
        return editor;
    };
})();
