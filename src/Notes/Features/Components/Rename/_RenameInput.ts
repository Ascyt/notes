// The name field of the breadcrumb (_RenameInput.cshtml): Enter or the check mark renames the entry, Escape or the cross resets the name.

// What the page was made with (the field's value attribute)
const renameInput = document.getElementById('rename-input') as HTMLInputElement;
const renameTexts = textsOf('rename-texts');
const originalName = renameInput.defaultValue;

function updateRenameButtons(): void {
    const changed = renameInput.value !== originalName;
    document.getElementById('rename-confirm')!.style.display = changed ? '' : 'none';
    document.getElementById('rename-cancel')!.style.display = changed ? '' : 'none';
}

function resetRename(): void {
    renameInput.value = originalName;
    updateRenameButtons();
}

async function renameEntry(): Promise<void> {
    const newName = renameInput.value;
    if (newName === originalName)
        return;

    // A page may veto renaming (e.g. a file with unsaved changes)
    if (typeof window.beforeRename === 'function' && window.beforeRename() === false)
        return;

    const token = document.querySelector<HTMLInputElement>('#rename input[name="__RequestVerificationToken"]')!.value;
    // A directory with much in it takes a while: the page is covered meanwhile, and can't be left before the answer is there
    const { ok, status, text } = await whileBusy(async function (): Promise<{ ok: boolean; status: number | string; text: string }> {
        try {
            const response = await fetch(window.location.pathname + '?rename=' + encodeURIComponent(newName), {
                method: 'POST',
                headers: { 'RequestVerificationToken': token }
            });
            return { ok: response.ok, status: response.status, text: await response.text() };
        } catch {
            return { ok: false, status: 'network error', text: '' };
        }
    }, { blockPage: true, message: fillIn(renameTexts.renaming, { name: newName }) });

    if (!ok) {
        // The server says what went wrong (e.g. a permission that is missing)
        notify(text || fillIn(renameTexts.renameFailed, { status }), 'danger');
        return;
    }

    notifyAfterNavigation(fillIn(renameTexts.renamed, { name: newName }));
    // The same view of it as before (another view is a query of the same address, e.g. ?edit)
    window.location.href = text + window.location.search;
}

document.getElementById('rename-cancel')!.addEventListener('click', resetRename);
document.getElementById('rename-confirm')!.addEventListener('click', () => void renameEntry());
// Clicking the field selects in steps: the first click the name without its extension, the next one the whole name, every later one only
// places the cursor. A selection made by dragging (or a long press on a phone) is kept, and later clicks only place the cursor.
(function () {
    const isFile = document.getElementById('rename')!.dataset.isFile === 'true';
    const dragDistance = 4; // pixels the pointer may move for it to still be a click
    let clicks = 0; // the clicks since the field was focused
    let down = { x: 0, y: 0 };
    let ourSelection: { start: number; end: number } | null = null; // what the steps selected, which the select event mustn't take for a selection of the user's

    // Where the name ends: before the extension of a file (".gitignore" has none), at the end for a directory
    function nameEnd(): number {
        const dot = renameInput.value.lastIndexOf('.');
        return isFile && dot > 0 ? dot : renameInput.value.length;
    }

    // How many clicks still select something: two with an extension, one without
    const selectingClicks = () => nameEnd() < renameInput.value.length ? 2 : 1;

    function select(end: number): void {
        ourSelection = { start: 0, end };
        renameInput.setSelectionRange(0, end);
    }

    renameInput.addEventListener('pointerdown', function (e) {
        down = { x: e.clientX, y: e.clientY };
    });

    // A quick second click would select a word first: the step selects instead
    renameInput.addEventListener('mousedown', function (e) {
        if (e.detail > 1 && clicks < selectingClicks())
            e.preventDefault();
    });

    // A selection of the user's ends the steps
    renameInput.addEventListener('select', function () {
        const isOurs = ourSelection && renameInput.selectionStart === ourSelection.start && renameInput.selectionEnd === ourSelection.end;
        if (!isOurs)
            clicks = selectingClicks();
    });

    renameInput.addEventListener('click', function (e) {
        if (Math.hypot(e.clientX - down.x, e.clientY - down.y) > dragDistance) {
            clicks = selectingClicks();
            return;
        }
        clicks++;
        if (clicks === 1)
            select(nameEnd());
        else if (clicks === 2 && selectingClicks() === 2)
            select(renameInput.value.length);
        // Otherwise the cursor stays where the browser put it
    });

    renameInput.addEventListener('blur', function () {
        clicks = 0;
        ourSelection = null;
    });

    // The field's shortcut (F2) goes through steps too: the name without its extension, the whole name, then out of the field. Clicks after it
    // only place the cursor, as after a drag.
    function selectNextByKeyboard(): void {
        const end = nameEnd();
        const length = renameInput.value.length;
        const isFocused = document.activeElement === renameInput;
        const isSelected = (until: number) => isFocused && renameInput.selectionStart === 0 && renameInput.selectionEnd === until;
        // After the whole name comes leaving the field
        if (isSelected(length)) {
            renameInput.blur();
            return;
        }
        if (!isFocused)
            renameInput.focus();
        // From the name without its extension to the whole name; from anything else (the cursor, a selection of the user's) to the name
        select(isSelected(end) ? length : end);
        clicks = selectingClicks();
    }

    // Unfocused, the shortcut system triggers it; focused, a plain key like F2 reaches the field, which the shortcut system leaves alone then
    renameInput.addEventListener('shortcut', function (e) {
        e.preventDefault();
        selectNextByKeyboard();
    });
    renameInput.addEventListener('keydown', function (e) {
        if (!e.repeat && matchesShortcutOf(renameInput, e)) {
            e.preventDefault();
            selectNextByKeyboard();
        }
    });
})();

renameInput.addEventListener('input', updateRenameButtons);
renameInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') {
        e.preventDefault();
        void renameEntry();
    } else if (e.key === 'Escape') {
        resetRename();
    }
});
