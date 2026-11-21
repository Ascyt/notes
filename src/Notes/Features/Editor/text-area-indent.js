// Tab and Shift+Tab of the code editor (Monaco), for a plain text field:
//
//   textAreaIndent.indent(field)   a selection over several lines: indents each of them. Otherwise puts an indent in place of the
//                                  selection (or at the cursor), up to the next tab stop.
//   textAreaIndent.outdent(field)  takes one indent away from the start of each line of the selection, or of the cursor's line
//
// An indent is a tab if the text is indented with tabs, otherwise spaces up to the next multiple of TAB_SIZE (as Monaco does by
// default). The change goes through the browser's own editing where it can, so that Ctrl+Z undoes it.
(function () {
    const TAB_SIZE = 4;

    // Tabs if more lines start with one than with a space
    function usesTabs(text) {
        const tabs = (text.match(/^\t/gm) || []).length;
        const spaces = (text.match(/^ /gm) || []).length;
        return tabs > spaces;
    }

    // The lines a selection touches: from the start of its first line to the end of its last. A selection that ends right at the start
    // of a line doesn't take that line in (as in Monaco).
    function touchedLines(text, start, end) {
        if (end > start && text[end - 1] === '\n')
            end--;
        const from = text.lastIndexOf('\n', start - 1) + 1;
        const lineEnd = text.indexOf('\n', end);
        return { from, to: lineEnd === -1 ? text.length : lineEnd };
    }

    // Replaces part of the field's text and selects the given range of the new text
    function replace(field, from, to, text, selectionStart, selectionEnd) {
        field.focus({ preventScroll: true });
        field.setSelectionRange(from, to);
        // execCommand is the only way to change a text field that the browser's undo knows about; where it is gone, the change is made directly
        if (!document.execCommand || !document.execCommand('insertText', false, text)) {
            field.setRangeText(text, from, to, 'end');
            field.dispatchEvent(new Event('input', { bubbles: true }));
        }
        field.setSelectionRange(selectionStart, selectionEnd);
    }

    // How many characters of indent to take from the start of a line: a tab, or the spaces back to the tab stop before
    function outdentLength(line) {
        if (line.startsWith('\t'))
            return 1;
        const spaces = line.match(/^ */)[0].length;
        return spaces === 0 ? 0 : ((spaces - 1) % TAB_SIZE) + 1;
    }

    window.textAreaIndent = {
        indent(field) {
            const text = field.value;
            const start = field.selectionStart, end = field.selectionEnd;
            const tabs = usesTabs(text);

            // Within one line: an indent in place of the selection
            if (!text.slice(start, end).includes('\n')) {
                const column = start - (text.lastIndexOf('\n', start - 1) + 1);
                const indent = tabs ? '\t' : ' '.repeat(TAB_SIZE - (column % TAB_SIZE));
                replace(field, start, end, indent, start + indent.length, start + indent.length);
                return;
            }

            // Over several lines: an indent before each of them (but not before empty ones), which the selection keeps covering
            const lines = touchedLines(text, start, end);
            const indent = tabs ? '\t' : ' '.repeat(TAB_SIZE);
            const changed = text.slice(lines.from, lines.to).split('\n').map(line => line === '' ? line : indent + line);
            const firstAdded = text.slice(lines.from, lines.to).split('\n')[0] === '' ? 0 : indent.length;
            const newText = changed.join('\n');
            replace(field, lines.from, lines.to, newText, start + firstAdded, end + newText.length - (lines.to - lines.from));
        },

        outdent(field) {
            const text = field.value;
            const start = field.selectionStart, end = field.selectionEnd;
            const lines = touchedLines(text, start, end);
            const original = text.slice(lines.from, lines.to).split('\n');
            const removed = original.map(outdentLength);
            if (removed.every(length => length === 0))
                return;

            const newText = original.map((line, i) => line.slice(removed[i])).join('\n');
            // The selection moves with the text, but not past the start of its line
            const removedInAll = (lines.to - lines.from) - newText.length;
            const newStart = Math.max(lines.from, start - removed[0]);
            const newEnd = Math.max(newStart, end - removedInAll);
            replace(field, lines.from, lines.to, newText, newStart, newEnd);
        }
    };
})();
