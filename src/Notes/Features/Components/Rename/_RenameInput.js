// The name field of the breadcrumb (_RenameInput.cshtml): Enter or the check mark renames the entry, Escape or the cross resets the name.

// What the page was made with (the field's value attribute)
const renameInput = document.getElementById('rename-input');
const renameTexts = textsOf('rename-texts');
const originalName = renameInput.defaultValue;

function updateRenameButtons() {
    const changed = renameInput.value !== originalName;
    document.getElementById('rename-confirm').style.display = changed ? '' : 'none';
    document.getElementById('rename-cancel').style.display = changed ? '' : 'none';
}

function resetRename() {
    renameInput.value = originalName;
    updateRenameButtons();
}

async function renameEntry() {
    const newName = renameInput.value;
    if (newName === originalName)
        return;

    // A page may veto renaming (e.g. a file with unsaved changes)
    if (typeof window.beforeRename === 'function' && window.beforeRename() === false)
        return;

    const token = document.querySelector('#rename input[name="__RequestVerificationToken"]').value;
    // A directory with much in it takes a while: the page is covered meanwhile, and can't be left before the answer is there
    const { ok, status, text } = await whileBusy(async function () {
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

document.getElementById('rename-cancel').addEventListener('click', resetRename);
document.getElementById('rename-confirm').addEventListener('click', renameEntry);
renameInput.addEventListener('input', updateRenameButtons);
renameInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') {
        e.preventDefault();
        renameEntry();
    } else if (e.key === 'Escape') {
        resetRename();
    }
});
