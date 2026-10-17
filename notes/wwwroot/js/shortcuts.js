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
// To do something else (e.g. focus a custom editor), handle the `shortcut` event the element receives
// and call preventDefault() to skip the default action:
//   el.addEventListener('shortcut', e => { e.preventDefault(); editor.focus(); });
// A shortcut without Ctrl/Alt/Meta is ignored while typing in a field.
// Shortcuts are captured before the page (and Monaco) sees them, so they take precedence over those.
// Escape in a form field (except Monaco) also unfocuses it.
//
// While any of HINT_KEYS is held, a tooltip with its shortcut is shown on every visible element that has one,
// alternative shortcuts under each other. By default it is centered on the element; to place it over another
// one, add `key-anchor` with a CSS selector, searched within the element's parent first, then the document:
//   <td><i class="icon"></i> <a href=".." key="^1" key-anchor=".icon">Notes</a></td>   shows over the icon
(function () {
    const HINT_KEYS = ['ControlRight']; // KeyboardEvent.code values
    const heldHintKeys = new Set();

    // Symbols shown in the hint tooltip. Key names that aren't listed are shown as written.
    const MODIFIER_SYMBOLS = { ctrlKey: '⎈', altKey: '⎇', shiftKey: '⇧', metaKey: '❖' };
    const KEY_SYMBOLS = {
        arrowup: '↑', arrowdown: '↓', arrowleft: '←', arrowright: '→', space: '␣', escape: '⎋',
        tab: '↹', enter: '↵', backspace: '⌫', delete: '⌦', home: '⇱', end: '⇲'
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
        // A page can take over the action: listen for `shortcut` on the element and call preventDefault()
        if (!el.dispatchEvent(new CustomEvent('shortcut', { cancelable: true })))
            return;

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

    function isRendered(el) {
        const rect = el.getBoundingClientRect();
        return rect.width !== 0 || rect.height !== 0;
    }

    // The element the hint is centered on: the one `key-anchor` selects, otherwise the element itself
    function anchorOf(el) {
        const selector = el.getAttribute('key-anchor');
        if (selector) {
            const anchor = (el.parentElement && el.parentElement.querySelector(selector)) || document.querySelector(selector);
            if (anchor && isRendered(anchor))
                return anchor;
        }
        return el;
    }

    // Elements whose shortcuts are currently live. While a modal is open only its own shortcuts are,
    // so e.g. Escape and Enter reach the modal's buttons instead of whatever is behind it.
    function activeElements() {
        const modal = document.querySelector('.modal.show');
        return [...document.querySelectorAll('[key]')].filter(el => !modal || modal.contains(el));
    }

    function showHints() {
        hideHints();
        for (const el of activeElements()) {
            const anchor = anchorOf(el);
            if (!isRendered(anchor))
                continue; // not rendered (e.g. display: none), nothing to anchor the tooltip to
            const rect = anchor.getBoundingClientRect();

            // Alternative shortcuts are listed under each other
            const hint = document.createElement('div');
            hint.className = 'key-hint';
            hint.textContent = parseAll(el.getAttribute('key')).map(symbols).join('\n');
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
        if (HINT_KEYS.includes(e.code) && !e.repeat) {
            heldHintKeys.add(e.code);
            showHints();
        }
        if (e.repeat || e.isComposing)
            return;

        const plain = !e.ctrlKey && !e.altKey && !e.metaKey;
        // A plain shortcut must not hijack typing, nor Enter/Space on a focused button or link
        if (plain && (isEditable(e.target)
            || ((e.key === 'Enter' || e.key === ' ') && e.target instanceof Element && e.target.closest('button, a[href], summary'))))
            return;

        // The same key may be set on several elements (e.g. Enter confirms whichever dialog or form
        // is relevant): prefer one that is visible, in DOM order
        const candidates = activeElements().filter(el => !el.disabled
            && parseAll(el.getAttribute('key')).some(combo => matches(combo, e)));
        const el = candidates.find(isRendered) || candidates[0];
        if (!el)
            return;

        e.preventDefault();
        e.stopPropagation();
        trigger(el);
    }, true);

    // Escape leaves a form field. Bubble phase, so a field's own Escape handling runs first and can
    // opt out with preventDefault(). Monaco's textarea is excluded: it needs Escape for its widgets.
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !e.defaultPrevented && !e.isComposing
            && e.target.matches('input, textarea, select') && !e.target.closest('.monaco-editor'))
            e.target.blur();
    });

    document.addEventListener('keyup', function (e) {
        heldHintKeys.delete(e.code);
        if (heldHintKeys.size === 0)
            hideHints();
    }, true);
    window.addEventListener('blur', function () {
        heldHintKeys.clear();
        hideHints();
    });

    // Expose each shortcut to assistive technology
    document.addEventListener('DOMContentLoaded', function () {
        for (const el of document.querySelectorAll('[key]'))
            el.setAttribute('aria-keyshortcuts', parseAll(el.getAttribute('key')).map(describe).join(' '));
    });
})();
