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
// Triggering clicks the element, or focuses it if it is a text field. Disabled elements (also aria-disabled="true") are skipped.
// An element that can't be clicked (e.g. a <div tabindex="-1"> around a few links) is focused instead, so that Tab then goes
// through what is in it: one shortcut for a group of rarely used links, instead of one each:
//   <div tabindex="-1" key="!+F"><a href="..">License</a> <a href="..">Source</a></div>
// To do something else (e.g. focus a custom editor), handle the `shortcut` event the element receives
// and call preventDefault() to skip the default action:
//   el.addEventListener('shortcut', e => { e.preventDefault(); editor.focus(); });
// A field that gives its own shortcut a meaning while it has the focus (where a plain shortcut is ignored) asks `matchesShortcutOf(el, e)`.
// When several elements have the same shortcut, one inside the element with `key-scope` that has the focus wins
// (a sidebar's Escape over the page's), then one that is visible. While a modal is open, only its shortcuts work.
// A shortcut without Ctrl/Alt/Meta is ignored while typing in a field (not while a checkbox or radio button has the focus) and while a video
// or audio player has the focus, and Enter and Space without them are left to a focused button or link (Shift+Enter isn't).
// Shortcuts are captured before the page (and Monaco) sees them, so they take precedence over those.
// Escape in a form field (except Monaco) also unfocuses it.
//
// While any of HINT_KEYS is held, a tooltip with its shortcut is shown on every visible element that has one,
// alternative shortcuts under each other. By default it is centered on the element; to place it over another
// one, add `key-anchor` with a CSS selector, searched within the element's parent first, then the document:
//   <td><i class="icon"></i> <a href=".." key="^1" key-anchor=".icon">Notes</a></td>   shows over the icon
// Hovering a tooltip shows it larger, with the keys written out ("Alt+ArrowUp"). An element with `key-held-text`
// shows that text in place of its own while the tooltips are shown.
(function () {
    type ModifierName = 'ctrlKey' | 'altKey' | 'shiftKey' | 'metaKey';

    // One shortcut of an element: the modifiers it needs (all the others must be up), the key's name, and the physical key of a letter or digit
    interface Combo {
        mods: Record<ModifierName, boolean>;
        name: string;
        code: string | null;
    }

    const HINT_KEYS = ['ControlRight']; // KeyboardEvent.code values
    const heldHintKeys = new Set<string>();

    const MODIFIER_NAMES_IN_ORDER: ModifierName[] = ['ctrlKey', 'altKey', 'shiftKey', 'metaKey'];

    // Symbols shown in the hint tooltip. Key names that aren't listed are shown as written.
    const MODIFIER_SYMBOLS: Record<ModifierName, string> = { ctrlKey: '⎈', altKey: '⎇', shiftKey: '⇧', metaKey: '❖' };
    const KEY_SYMBOLS: Record<string, string> = {
        arrowup: '↑', arrowdown: '↓', arrowleft: '←', arrowright: '→', space: '␣', escape: '⎋',
        tab: '↹', enter: '↵', backspace: '⌫', delete: '⌦', home: '⇱', end: '⇲', pageup: '⯭', pagedown: '⯯'
    };

    const MODIFIERS: Record<string, ModifierName> = { '^': 'ctrlKey', '!': 'altKey', '+': 'shiftKey', '#': 'metaKey' };
    const MODIFIER_NAMES: Record<ModifierName, string> = { ctrlKey: 'Control', altKey: 'Alt', shiftKey: 'Shift', metaKey: 'Meta' };

    function parse(spec: string): Combo {
        const mods: Record<ModifierName, boolean> = { ctrlKey: false, altKey: false, shiftKey: false, metaKey: false };
        let i = 0;
        // The last character is always the key, so "^+" is Ctrl and the "+" key
        while (i < spec.length - 1 && MODIFIERS[spec[i]])
            mods[MODIFIERS[spec[i++]]] = true;

        const name = spec.slice(i);
        // Physical key of a letter or digit, used when the layout doesn't produce a Latin character
        let code: string | null = null;
        if (/^[a-z]$/i.test(name))
            code = 'Key' + name.toUpperCase();
        else if (/^\d$/.test(name))
            code = 'Digit' + name;
        return { mods, name, code };
    }

    function parseAll(attr: string): Combo[] {
        return attr.split(/\s+/).filter(Boolean).map(parse);
    }

    // The shortcuts of an element
    const combosOf = (el: Element) => parseAll(el.getAttribute('key') ?? '');

    function matches(combo: Combo, e: KeyboardEvent): boolean {
        for (const mod of MODIFIER_NAMES_IN_ORDER)
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

    // Whether the key event is one of the element's shortcuts
    window.matchesShortcutOf = function (el, e) {
        const attr = el.getAttribute('key');
        return attr !== null && parseAll(attr).some(combo => matches(combo, e));
    };

    // Fields that take typing. Boxes and buttons made of an input don't: a shortcut works while they have the focus (e.g. right after ticking a box).
    const TYPING_INPUT = 'input:not([type=checkbox]):not([type=radio]):not([type=button]):not([type=submit]):not([type=reset])';

    const CLICKABLE = 'a, button, input, select, textarea, summary, label, [role=button], [onclick]';

    // Monaco takes typing in an element that is no field (it uses the browser's EditContext), so anything inside it counts
    function isEditable(target: EventTarget | null): boolean {
        return target instanceof Element && target.closest(TYPING_INPUT
            + ', textarea, select, [contenteditable=""], [contenteditable="true"], .monaco-editor') !== null;
    }

    function trigger(el: HTMLElement): void {
        // A page can take over the action: listen for `shortcut` on the element and call preventDefault()
        if (!el.dispatchEvent(new CustomEvent('shortcut', { cancelable: true })))
            return;

        if (el.matches('input:not([type=button]):not([type=submit]):not([type=reset]):not([type=checkbox]):not([type=radio]), textarea, select')) {
            el.focus();
            const field = el as HTMLInputElement;
            if (typeof field.select === 'function' && el.matches('input, textarea'))
                field.select();
        } else if (!el.matches(CLICKABLE)) {
            // A group of rarely used links or buttons has one shortcut for all of them: focusing it, from where Tab reaches each
            el.focus();
        } else {
            el.click();
        }
    }

    function keyText(combo: Combo): string {
        return combo.name.length === 1 ? combo.name.toUpperCase() : combo.name;
    }

    const heldModifiers = (combo: Combo) => MODIFIER_NAMES_IN_ORDER.filter(mod => combo.mods[mod]);

    // Compact form for the on-screen hint, e.g. "⎇↑"
    function symbols(combo: Combo): string {
        const mods = heldModifiers(combo).map(mod => MODIFIER_SYMBOLS[mod]).join('');
        return mods + (KEY_SYMBOLS[combo.name.toLowerCase()] || keyText(combo));
    }

    // Text form, e.g. "Alt+ArrowUp" (also the aria-keyshortcuts format)
    function describe(combo: Combo): string {
        const parts = heldModifiers(combo).map(mod => MODIFIER_NAMES[mod]);
        parts.push(keyText(combo));
        return parts.join('+');
    }

    let hints: HTMLElement[] = [];

    function isRendered(el: Element): boolean {
        const rect = el.getBoundingClientRect();
        return rect.width !== 0 || rect.height !== 0;
    }

    // The element the hint is centered on: the one `key-anchor` selects, otherwise the element itself
    function anchorOf(el: HTMLElement): Element {
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
    function activeElements(): HTMLElement[] {
        const modal = document.querySelector('.modal.show');
        return [...document.querySelectorAll<HTMLElement>('[key]')].filter(el => !modal || modal.contains(el));
    }

    function showHints(): void {
        hideHints();
        for (const el of activeElements()) {
            const anchor = anchorOf(el);
            if (!isRendered(anchor))
                continue; // not rendered (e.g. display: none), nothing to anchor the tooltip to
            const rect = anchor.getBoundingClientRect();

            // Alternative shortcuts are listed under each other. Hovering one shows it larger, with the keys written out.
            const combos = combosOf(el);
            const compactText = combos.map(symbols).join('\n');
            const fullText = combos.map(describe).join('\n');
            const hint = document.createElement('div');
            hint.className = 'key-hint';
            hint.textContent = compactText;
            document.body.appendChild(hint);
            place(hint, rect);
            hint.addEventListener('mouseenter', function () {
                hint.classList.add('key-hint-expanded');
                hint.textContent = fullText;
                place(hint, rect);
            });
            hint.addEventListener('mouseleave', function () {
                hint.classList.remove('key-hint-expanded');
                hint.textContent = compactText;
                place(hint, rect);
            });
            hints.push(hint);
        }
        swapHeldTexts(true);
    }

    // Centered on the element, kept inside the viewport
    function place(hint: HTMLElement, rect: DOMRect): void {
        const left = rect.left + rect.width / 2 - hint.offsetWidth / 2;
        hint.style.left = Math.max(4, Math.min(left, document.documentElement.clientWidth - hint.offsetWidth - 4)) + 'px';
        hint.style.top = (rect.top + rect.height / 2 - hint.offsetHeight / 2) + 'px';
    }

    // An element with `key-held-text` shows that text instead of its own while the hints are shown
    function swapHeldTexts(held: boolean): void {
        for (const el of document.querySelectorAll<HTMLElement>('[key-held-text]')) {
            if (held && el.dataset.keyRestingText === undefined) {
                el.dataset.keyRestingText = el.textContent ?? '';
                el.textContent = el.getAttribute('key-held-text');
            } else if (!held && el.dataset.keyRestingText !== undefined) {
                el.textContent = el.dataset.keyRestingText;
                delete el.dataset.keyRestingText;
            }
        }
    }

    function hideHints(): void {
        for (const hint of hints)
            hint.remove();
        hints = [];
        swapHeldTexts(false);
    }

    document.addEventListener('keydown', function (e) {
        if (HINT_KEYS.includes(e.code) && !e.repeat) {
            heldHintKeys.add(e.code);
            showHints();
        }
        if (e.repeat || e.isComposing)
            return;

        const plain = !e.ctrlKey && !e.altKey && !e.metaKey;
        // A plain shortcut must not hijack typing, nor Enter/Space on a focused button, link or box. Shift+Enter may be one: on a link it
        // would only open a new window, and a page that gives it a meaning means that instead.
        // Nor the keys of a focused video or audio player, which seeks and changes the volume with the arrows.
        if (plain && (isEditable(e.target)
            || (((e.key === 'Enter' && !e.shiftKey) || e.key === ' ') && e.target instanceof Element
                && e.target.closest('button, a[href], summary, input'))
            || (e.target instanceof Element && e.target.closest('video, audio'))))
            return;

        // The same key may be set on several elements (e.g. Enter confirms whichever dialog or form
        // is relevant, Escape leaves whatever has the focus): prefer one in the area that has the focus
        // (`key-scope`), then one that is visible, in DOM order
        const candidates = activeElements().filter(el => !(el as HTMLButtonElement).disabled && el.getAttribute('aria-disabled') !== 'true'
            && combosOf(el).some(combo => matches(combo, e)));
        const scope = document.activeElement instanceof Element ? document.activeElement.closest('[key-scope]') : null;
        const el = (scope && candidates.find(candidate => scope.contains(candidate) && isRendered(candidate)))
            || candidates.find(isRendered) || candidates[0];
        if (!el)
            return;

        e.preventDefault();
        e.stopPropagation();
        trigger(el);
    }, true);

    // Escape leaves a form field. Bubble phase, so a field's own Escape handling runs first and can
    // opt out with preventDefault(). Monaco's textarea is excluded: it needs Escape for its widgets.
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !e.defaultPrevented && !e.isComposing && e.target instanceof HTMLElement
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
            el.setAttribute('aria-keyshortcuts', combosOf(el).map(describe).join(' '));
    });
})();
