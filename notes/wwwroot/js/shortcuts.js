// Declarative keyboard shortcuts: put a `key` attribute on any button, link or form field.
//
//   <button key="^S">Save</button>           Ctrl+S
//   <a key="!ArrowUp" href="..">Parent</a>   Alt+ArrowUp
//   <input key="^!T" />                      Ctrl+Alt+T (focuses the field)
//   <button key="^S !S">...</button>         Several shortcuts, separated by spaces
//
// Modifiers (prefix, any combination, matched exactly): ^ Ctrl, ! Alt, + Shift, # Meta (Win/Cmd).
// Keys: a letter or digit, any other single character ("/", ","), or a key name exactly as listed in
// the official docs (case-insensitive), matching either a key value or a physical key code:
//   https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_key_values
//   https://developer.mozilla.org/en-US/docs/Web/API/UI_Events/Keyboard_event_code_values
//
// Triggering clicks the element, or focuses it if it is a text field. Disabled elements are skipped.
// A shortcut without Ctrl/Alt/Meta is ignored while typing in a field.
// Shortcuts are captured before the page (and Monaco) sees them, so they take precedence over those.
//
// While HINT_KEY is held, a tooltip with its shortcut is shown on every visible element that has one.
(function () {
    const HINT_KEY = 'ControlRight'; // a KeyboardEvent.code

    // Symbols shown in the hint tooltip. Key names that aren't listed are shown as written.
    const MODIFIER_SYMBOLS = { ctrlKey: '⎈', altKey: '⎇', shiftKey: '⇧', metaKey: '❖' };
    const KEY_SYMBOLS = {
        arrowup: '↑', arrowdown: '↓', arrowleft: '←', arrowright: '→', space: '␣', escape: '⎋',
        tab: '↹', enter: '↵', backspace: '⌫', delete: '⌦'
    };

    const MODIFIERS = { '^': 'ctrlKey', '!': 'altKey', '+': 'shiftKey', '#': 'metaKey' };
    const MODIFIER_NAMES = { ctrlKey: 'Control', altKey: 'Alt', shiftKey: 'Shift', metaKey: 'Meta' };

    function parse(spec) {
        const mods = { ctrlKey: false, altKey: false, shiftKey: false, metaKey: false };
        let i = 0;
        // The last character is always the key, so "^+" is Ctrl and the "+" key
        while (i < spec.length - 1 && MODIFIERS[spec[i]])
            mods[MODIFIERS[spec[i++]]] = true;

        const name = spec.slice(i);
        // Physical key of a letter or digit, used when the layout doesn't produce a Latin character
        let code = null;
        if (/^[a-z]$/i.test(name))
            code = 'Key' + name.toUpperCase();
        else if (/^\d$/.test(name))
            code = 'Digit' + name;
        return { mods, name, code };
    }

    function parseAll(attr) {
        return attr.split(/\s+/).filter(Boolean).map(parse);
    }

    function matches(combo, e) {
        for (const mod in combo.mods)
            if (combo.mods[mod] !== e[mod])
                return false;

        const name = combo.name.toLowerCase();
        if (name.length > 1)
            return name === e.key.toLowerCase() || name === e.code.toLowerCase();
        if (combo.code === null)
            return combo.name === e.key;
        // Letters and digits follow the typed character, so non-QWERTY layouts work. Only when that isn't
        // a plain Latin letter/digit (non-Latin layout, Alt dead keys on macOS) fall back to the physical key.
        if (/^[a-z0-9]$/i.test(e.key))
            return e.key.toLowerCase() === name;
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

    function keyText(combo) {
        return combo.name.length === 1 ? combo.name.toUpperCase() : combo.name;
    }

    // Compact form for the on-screen hint, e.g. "⎇↑"
    function symbols(combo) {
        const mods = Object.keys(combo.mods).filter(m => combo.mods[m]).map(m => MODIFIER_SYMBOLS[m]).join('');
        return mods + (KEY_SYMBOLS[combo.name.toLowerCase()] || keyText(combo));
    }

    // Text form, e.g. "Alt+ArrowUp" (also the aria-keyshortcuts format)
    function describe(combo) {
        const parts = Object.keys(combo.mods).filter(m => combo.mods[m]).map(m => MODIFIER_NAMES[m]);
        parts.push(keyText(combo));
        return parts.join('+');
    }

    let hints = [];

    function showHints() {
        hideHints();
        for (const el of document.querySelectorAll('[key]')) {
            const rect = el.getBoundingClientRect();
            if (rect.width === 0 && rect.height === 0)
                continue; // not rendered (e.g. display: none), nothing to anchor the tooltip to

            const hint = document.createElement('div');
            hint.className = 'key-hint';
            hint.textContent = parseAll(el.getAttribute('key')).map(symbols).join(' ');
            document.body.appendChild(hint);

            // Centered on the element, kept inside the viewport
            const left = rect.left + rect.width / 2 - hint.offsetWidth / 2;
            hint.style.left = Math.max(4, Math.min(left, document.documentElement.clientWidth - hint.offsetWidth - 4)) + 'px';
            hint.style.top = (rect.top + rect.height / 2 - hint.offsetHeight / 2) + 'px';
            hints.push(hint);
        }
    }

    function hideHints() {
        for (const hint of hints)
            hint.remove();
        hints = [];
    }

    document.addEventListener('keydown', function (e) {
        if (e.code === HINT_KEY && !e.repeat)
            showHints();
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

    document.addEventListener('keyup', function (e) {
        if (e.code === HINT_KEY)
            hideHints();
    }, true);
    window.addEventListener('blur', hideHints);

    // Expose each shortcut to assistive technology
    document.addEventListener('DOMContentLoaded', function () {
        for (const el of document.querySelectorAll('[key]'))
            el.setAttribute('aria-keyshortcuts', parseAll(el.getAttribute('key')).map(describe).join(' '));
    });
})();
