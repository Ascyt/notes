// Enter in a list of a Markdown file goes on with the list, as in most Markdown editors:
//
//   "  - item|"     →  "  - item" and "  - |"     the same bullet and indent on the new line
//   "3. item|"      →  "4. |"                     an ordered list counts on (with the same "." or ")")
//   "- [x] done|"   →  "- [ ] |"                  a task list gets a task that isn't done yet
//   "  - |"         →  "|"                        Enter on an empty item ends the list instead
//
// Shift+Enter is a plain line break. For both editors of a file:
//   markdownLists.bindToTextArea(field)   the simple view
//   markdownLists.bindToMonaco(editor)    the code editor
(function () {
    // Indent, then the bullet or number with its delimiter, the spaces after it, and the box of a task
    const LIST_ITEM = /^(\s*)(?:([-*+])|(\d{1,9})([.)]))(\s+)(\[[ xX]\]\s+)?/;

    // What Enter does at the cursor, given the text of its line before and after it: null (a plain line break), { text } to put in place of
    // the line break, or { endsList: true } to take the empty item's marker away
    function onEnter(before, after) {
        const match = LIST_ITEM.exec(before);
        if (!match)
            return null;
        const [marker, indent, bullet, number, delimiter, spaces, task] = match;
        if ((before.slice(marker.length) + after).trim() === '')
            return { endsList: true };

        const next = bullet ?? (String(Number(number) + 1) + delimiter);
        return { text: '\n' + indent + next + spaces + (task ? '[ ] ' : '') };
    }

    window.markdownLists = {
        onEnter,

        bindToTextArea(field) {
            // Which Enter it was isn't part of the input event, so the key is remembered
            let withShift = false;
            field.addEventListener('keydown', function (e) {
                if (e.key === 'Enter')
                    withShift = e.shiftKey;
            });

            // The input, not the key: a phone's keyboard doesn't always tell which key it was, but it always says that it breaks the line
            field.addEventListener('beforeinput', function (e) {
                if (e.inputType !== 'insertLineBreak' || withShift || field.readOnly)
                    return;
                const text = field.value;
                const start = field.selectionStart, end = field.selectionEnd;
                if (text.slice(start, end).includes('\n'))
                    return;
                const lineStart = text.lastIndexOf('\n', start - 1) + 1;
                const nextBreak = text.indexOf('\n', end);
                const lineEnd = nextBreak === -1 ? text.length : nextBreak;
                const action = onEnter(text.slice(lineStart, start), text.slice(end, lineEnd));
                if (!action)
                    return;

                e.preventDefault();
                // Through the browser's own editing, so that Ctrl+Z undoes it (directly where that is gone)
                const from = action.endsList ? lineStart : start;
                const insert = action.endsList ? '' : action.text;
                field.setSelectionRange(from, end);
                const done = document.execCommand && (insert === '' ? document.execCommand('delete') : document.execCommand('insertText', false,
                    insert));
                if (!done) {
                    field.setRangeText(insert, from, end, 'end');
                    field.dispatchEvent(new Event('input', { bubbles: true }));
                }
            });
        },

        bindToMonaco(editor) {
            // Not while a suggestion is shown, which Enter takes; with several cursors, Enter stays as it is
            editor.addCommand(monaco.KeyCode.Enter, function () {
                const selection = editor.getSelection();
                const action = editor.getSelections().length === 1 && selection.startLineNumber === selection.endLineNumber
                    ? onEnter(editor.getModel().getLineContent(selection.startLineNumber).slice(0, selection.startColumn - 1),
                        editor.getModel().getLineContent(selection.startLineNumber).slice(selection.endColumn - 1))
                    : null;
                if (!action) {
                    editor.trigger('keyboard', 'type', { text: '\n' }); // what Enter does otherwise, with its indent
                    return;
                }

                const line = selection.startLineNumber;
                editor.pushUndoStop();
                if (action.endsList)
                    editor.executeEdits('markdown-lists', [{ range: new monaco.Range(line, 1, line, selection.endColumn), text: '' }],
                        [new monaco.Selection(line, 1, line, 1)]);
                else {
                    const column = action.text.length; // the text after the line break, plus one
                    editor.executeEdits('markdown-lists', [{ range: selection, text: action.text }],
                        [new monaco.Selection(line + 1, column, line + 1, column)]);
                }
                editor.pushUndoStop();
                editor.revealPosition(editor.getPosition());
            }, 'editorTextFocus && !editorReadonly && !suggestWidgetVisible && !inSnippetMode && !editorHasMultipleSelections');
        }
    };
})();
