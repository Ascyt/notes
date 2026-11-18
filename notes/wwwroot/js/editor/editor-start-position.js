// Where an editor starts: the rendered view of a markdown file can lead to a place in its text (#line=…&column=…, both counted from 1,
// where a block starts; see markdown-edit-here.js). With &textBefore=… it leads to a character inside the block instead: the rendered text of
// the block before it, which is found again in the markdown it was rendered from.
//
//   editorStartPosition.read()                          the place the address asks for ({ line, column, before }), or null
//   editorStartPosition.goToInMonaco(editor, position)  puts the cursor there, in the middle of the editor
//   editorStartPosition.goToInTextArea(field, position) the same in a text field
//   editorStartPosition.placeInTextArea(field)          where the cursor of a text field is ({ line, column })
//   editorStartPosition.linkToPreview(link, place)      makes the link to the preview lead to that place of the text
(function () {
    // How far a rendered character may be from the one before it in the markdown (the address of a link, markup), before it is taken
    // to be one that isn't written as itself (an emoji, math) and is skipped
    const MAX_MARKUP_LENGTH = 300;

    // The offset in the text of where the rendered text before the place ends: each of its characters is looked for after the one
    // before it. Spaces are left out, as rendering changes them (line breaks, several in a row).
    function offsetAfter(text, start, before) {
        let offset = start;
        for (const character of before.replace(/\s+/g, '')) {
            const found = text.indexOf(character, offset);
            if (found !== -1 && found - offset <= MAX_MARKUP_LENGTH)
                offset = found + character.length;
        }
        // A place right after a space is at the start of the next word: past the markup up to that space (the end of a link), and the space
        if (/\s$/.test(before)) {
            const space = text.slice(offset, offset + MAX_MARKUP_LENGTH).search(/\s/);
            if (space !== -1)
                offset += space;
            while (offset < text.length && /[ \t]/.test(text[offset]))
                offset++;
        }
        return offset;
    }

    // The offset of a place in the text (line breaks as \n), and back
    function offsetOf(text, position) {
        const lines = text.split('\n');
        const lineIndex = Math.min(position.line, lines.length) - 1;
        let offset = 0;
        for (let i = 0; i < lineIndex; i++)
            offset += lines[i].length + 1;
        return offset + Math.min(position.column - 1, lines[lineIndex].length);
    }

    function positionOf(text, offset) {
        const lines = text.slice(0, offset).split('\n');
        return { line: lines.length, column: lines[lines.length - 1].length + 1 };
    }

    // The place to go to, with the character inside the block if the address names one
    function resolve(text, position) {
        text = text.replace(/\r\n?/g, '\n');
        const start = offsetOf(text, position);
        return positionOf(text, position.before ? offsetAfter(text, start, position.before) : start);
    }

    window.editorStartPosition = {
        // The address doesn't keep it, so a reload starts where the editor usually does
        read() {
            const parameters = new URLSearchParams(window.location.hash.slice(1));
            const line = parseInt(parameters.get('line'), 10);
            const column = parseInt(parameters.get('column'), 10);
            if (Number.isNaN(line))
                return null;
            history.replaceState(history.state, '', window.location.pathname + window.location.search);
            return { line: Math.max(1, line), column: Number.isNaN(column) ? 1 : Math.max(1, column), before: parameters.get('textBefore') || '' };
        },

        goToInMonaco(editor, position) {
            const place = resolve(editor.getValue(), position);
            const target = { lineNumber: place.line, column: place.column };
            editor.setPosition(target); // Monaco keeps it inside the text
            editor.revealPositionInCenter(target);
            editor.focus();
        },

        goToInTextArea(field, position) {
            const offset = offsetOf(field.value, resolve(field.value, position));
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
        },

        // The preview shows the part of the text with that place (markdown-start-position.js)
        linkToPreview(link, place) {
            link.setAttribute('href', link.getAttribute('href').split('#')[0] + '#line=' + place.line + '&column=' + place.column);
        },

        // The place of the cursor of a text field, as the editors count it
        placeInTextArea(field) {
            return positionOf(field.value, field.selectionStart);
        }
    };
})();
