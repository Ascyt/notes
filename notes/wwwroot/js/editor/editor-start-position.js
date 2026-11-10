// Where an editor starts: the rendered view of a markdown file can lead to a place in its text (#line=…&column=…, both counted from 1;
// see markdown-edit-here.js).
//
//   editorStartPosition.read()                          the place the address asks for ({ line, column }), or null
//   editorStartPosition.goToInMonaco(editor, position)  puts the cursor there, in the middle of the editor
//   editorStartPosition.goToInTextArea(field, position) the same in a text field
(function () {
    window.editorStartPosition = {
        // The address doesn't keep it, so a reload starts where the editor usually does
        read() {
            const parameters = new URLSearchParams(window.location.hash.slice(1));
            const line = parseInt(parameters.get('line'), 10);
            const column = parseInt(parameters.get('column'), 10);
            if (Number.isNaN(line))
                return null;
            history.replaceState(history.state, '', window.location.pathname + window.location.search);
            return { line: Math.max(1, line), column: Number.isNaN(column) ? 1 : Math.max(1, column) };
        },

        goToInMonaco(editor, position) {
            const target = { lineNumber: position.line, column: position.column };
            editor.setPosition(target); // Monaco keeps it inside the text
            editor.revealPositionInCenter(target);
            editor.focus();
        },

        goToInTextArea(field, position) {
            const lines = field.value.split('\n');
            const lineIndex = Math.min(position.line, lines.length) - 1;
            let offset = 0;
            for (let i = 0; i < lineIndex; i++)
                offset += lines[i].length + 1;
            offset += Math.min(position.column - 1, lines[lineIndex].length);

            field.focus({ preventScroll: true });
            field.setSelectionRange(offset, offset);

            // How far down the place is, with the lines wrapped as they are: a copy of the field holding only the text before it is that high
            const measure = field.cloneNode();
            measure.removeAttribute('id');
            measure.style.position = 'absolute';
            measure.style.visibility = 'hidden';
            measure.style.width = field.offsetWidth + 'px';
            measure.style.height = '0';
            measure.value = field.value.slice(0, offset);
            field.after(measure);
            const placeTop = measure.scrollHeight;
            measure.remove();
            field.scrollTop = Math.max(0, placeTop - field.clientHeight / 2);
        }
    };
})();
