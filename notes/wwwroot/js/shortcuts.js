// Declarative keyboard shortcuts: put a `key` attribute on any button, link or form field.
//
//   <button key="^S">Save</button>      Ctrl+S
//   <a key="!Up" href="..">Parent</a>   Alt+Up
//   <input key="^!T" />                 Ctrl+Alt+T (focuses the field)
//   <button key="^S !S">...</button>    Several shortcuts, separated by spaces
//
// Modifiers (prefix, any combination, matched exactly): ^ Ctrl, ! Alt, + Shift, # Meta (Win/Cmd).
// Keys: a letter or digit, any other single character ("/", ","), or a name: Up, Down, Left, Right,
// Enter, Esc, Tab, Space, Backspace, Delete, Home, End, PageUp, PageDown, F1..F12.
//
// Triggering clicks the element, or focuses it if it is a text field. Disabled elements are skipped.
// A shortcut without Ctrl/Alt/Meta is ignored while typing in a field.
// Shortcuts are captured before the page (and Monaco) sees them, so they take precedence over those.
(function () {
    const MODIFIERS = { '^': 'ctrlKey', '!': 'altKey', '+': 'shiftKey', '#': 'metaKey' };
    const ALIASES = { up: 'ArrowUp', down: 'ArrowDown', left: 'ArrowLeft', right: 'ArrowRight', esc: 'Escape' };
    const DISPLAY = { ctrlKey: 'Ctrl', altKey: 'Alt', shiftKey: 'Shift', metaKey: 'Meta' };
    const ARIA = { ctrlKey: 'Control', altKey: 'Alt', shiftKey: 'Shift', metaKey: 'Meta' };

    function parse(spec) {
        const mods = { ctrlKey: false, altKey: false, shiftKey: false, metaKey: false };
        let i = 0;
        // The last character is always the key, so "^+" is Ctrl and the "+" key
        while (i < spec.length - 1 && MODIFIERS[spec[i]])
            mods[MODIFIERS[spec[i++]]] = true;

        const name = spec.slice(i);
        let code = null;
        if (/^[a-z]$/i.test(name))
            code = 'Key' + name.toUpperCase();
        else if (/^\d$/.test(name))
            code = 'Digit' + name;
        else if (name.length > 1)
            code = ALIASES[name.toLowerCase()] || name;
        return { mods, name, code };
    }

    function parseAll(attr) {
        return attr.split(/\s+/).filter(Boolean).map(parse);
    }

    function matches(combo, e) {
        for (const mod in combo.mods)
            if (combo.mods[mod] !== e[mod])
                return false;
        if (combo.code === null)
            return combo.name === e.key;
        // Named keys (arrows, Enter, ...) are layout-independent
        if (combo.name.length > 1)
            return combo.code.toLowerCase() === e.code.toLowerCase();
        // Letters and digits follow the typed character, so non-QWERTY layouts work. Only when that isn't
        // a plain Latin letter/digit (non-Latin layout, Alt dead keys on macOS) fall back to the physical key.
        if (/^[a-z0-9]$/i.test(e.key))
            return e.key.toLowerCase() === combo.name.toLowerCase();
        return combo.code.toLowerCase() === e.code.toLowerCase();
    }

    function isEditable(target) {
        return target instanceof Element && target.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"]') !== null;
    }

    function trigger(el) {
        if (el.matches('input:not([type=button]):not([type=submit]):not([type=reset]):not([type=checkbox]):not([type=radio]), textarea, select')) {
            el.focus();
            if (typeof el.select === 'function' && el.matches('input, textarea'))
                el.select();
        } else {
            el.click();
        }
    }

    function describe(combo, names, aria) {
        const parts = Object.keys(combo.mods).filter(m => combo.mods[m]).map(m => names[m]);
        parts.push(combo.name.length === 1 ? combo.name.toUpperCase() : (aria ? combo.code : combo.name));
        return parts.join('+');
    }

    document.addEventListener('keydown', function (e) {
        if (e.repeat || e.isComposing)
            return;

        const typing = isEditable(e.target);
        for (const el of document.querySelectorAll('[key]')) {
            if (el.disabled)
                continue;
            for (const combo of parseAll(el.getAttribute('key'))) {
                if (!matches(combo, e))
                    continue;
                if (typing && !combo.mods.ctrlKey && !combo.mods.altKey && !combo.mods.metaKey)
                    continue;

                e.preventDefault();
                e.stopPropagation();
                trigger(el);
                return;
            }
        }
    }, true);

    // Expose each shortcut to assistive technology and as a tooltip hint
    document.addEventListener('DOMContentLoaded', function () {
        for (const el of document.querySelectorAll('[key]')) {
            const combos = parseAll(el.getAttribute('key'));
            el.setAttribute('aria-keyshortcuts', combos.map(c => describe(c, ARIA, true)).join(' '));

            const hint = combos.map(c => describe(c, DISPLAY)).join(', ');
            const title = el.getAttribute('title');
            el.setAttribute('title', title ? title + ' (' + hint + ')' : hint);
        }
    });
})();
