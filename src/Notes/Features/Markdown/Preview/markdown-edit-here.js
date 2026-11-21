// The button that opens the editor where the part of the rendered markdown next to it is written. It shows for the innermost block
// (paragraph, list item, heading, …) that MarkdownRenderer marked with where it starts:
//   - With a mouse, in the gutter left of the text, for the block on the line the pointer is on, anywhere across the page.
//   - While Alt is held, right where the mouse is over text; a click there opens the editor at that very character.
//   - On a narrow screen there is no gutter: a tap on a block shows it over the text, where it was tapped, and leads to that character.
//     Another tap on the same block, or anywhere else, takes it away.
//   - Otherwise (a tap on a wide screen, the keyboard) for the block that is tapped or focused, in the gutter.
// The editor reads that place from the address (#line=…&column=…, and &textBefore=… for a character, see editor-start-position.js).
// Whoever can't change the file gets the same button for its source, which the editor then shows read-only.
(function () {
    const GAP_PX = 4;
    const NARROW_SCREEN = window.matchMedia('(max-width: 767.98px)'); // the width at which markdown.css leaves out the gutter

    const preview = document.getElementById('markdown-preview');
    const button = document.getElementById('edit-here-button') ?? document.getElementById('view-source-here-button');
    const area = button.parentElement; // the text and the button, which is positioned in it
    let currentBlock = null;
    let isOverText = false; // shown over the text at a place, rather than in the gutter
    let pointer = null;     // where the mouse is ({ x, y }), while it is on the page
    let isAltHeld = false;
    let wasPointerDown = false; // a focus that comes from a tap or click, not from the keyboard

    function blockAt(target) {
        const block = target instanceof Element ? target.closest('[data-source-line]') : null;
        return block && preview.contains(block) ? block : null;
    }

    // The blocks right inside a container (or the preview), not those nested deeper. Blocks without a box (hidden) are left out.
    function childBlocks(container) {
        return Array.from(container.querySelectorAll('[data-source-line]')).filter(function (block) {
            return block.parentElement.closest('[data-source-line], #markdown-preview') === container
                && block.getBoundingClientRect().height > 0;
        });
    }

    // The innermost block on that height of the screen. The space between two blocks (such as the margin of a paragraph in a list
    // item, which lies outside the item) belongs to the block above it, so the button doesn't jump to the list around them there.
    function blockOnLine(y) {
        const previewBox = preview.getBoundingClientRect();
        if (y < previewBox.top || y >= previewBox.bottom)
            return null;
        let found = null;
        // Step down from the preview: at each level, the last block that starts above the pointer
        for (let container = preview; ;) {
            const next = childBlocks(container).filter(block => block.getBoundingClientRect().top <= y).pop();
            if (!next)
                return found;
            found = container = next;
        }
    }

    // The place between two characters of the text nearest to a point of the screen, or null if the point isn't over a character
    // of a block. `before` is the text of the block before that place, which the editor finds the place in its text by.
    function textPlaceAt(x, y) {
        let node, offset;
        if (document.caretPositionFromPoint) {
            const caret = document.caretPositionFromPoint(x, y);
            if (caret)
                ({ offsetNode: node, offset } = caret);
        } else if (document.caretRangeFromPoint) {
            const caret = document.caretRangeFromPoint(x, y);
            if (caret)
                ({ startContainer: node, startOffset: offset } = caret);
        }
        if (!(node instanceof Text))
            return null;
        const block = blockAt(node.parentElement);
        if (!block || !isOverCharacter(node, offset, x, y))
            return null;

        const range = document.createRange();
        range.setStart(block, 0);
        range.setEnd(node, offset);
        return { block, before: range.toString() };
    }

    // The caret is put at the nearest place even far from any text, so this checks that the point is on the character next to it
    function isOverCharacter(node, offset, x, y) {
        const range = document.createRange();
        for (const [start, end] of [[offset - 1, offset], [offset, offset + 1]]) {
            if (start < 0 || end > node.length)
                continue;
            range.setStart(node, start);
            range.setEnd(node, end);
            for (const box of range.getClientRects())
                if (x >= box.left - 1 && x <= box.right + 1 && y >= box.top && y <= box.bottom)
                    return true;
        }
        return false;
    }

    function linkTo(block, before) {
        let place = '#line=' + encodeURIComponent(block.dataset.sourceLine) + '&column=' + encodeURIComponent(block.dataset.sourceColumn);
        if (before)
            place += '&textBefore=' + encodeURIComponent(before.replace(/\s+/g,
                ' ')); // the editor doesn't count spaces, which keeps the address short
        // Only the place changes: the rest of the address is the page's (in an earlier state it has the state, see _EarlierStateCard)
        const address = button.getAttribute('href').split('#')[0];
        button.setAttribute('href', address + place);
    }

    // In the gutter left of the text, at the height of the block's first line
    function showInGutter(block) {
        if (!block || (block === currentBlock && !isOverText))
            return;
        currentBlock = block;
        isOverText = false;
        linkTo(block, '');
        button.classList.remove('is-over-text');
        setAtPointer(false);
        button.hidden = false;
        placeInGutter();
    }

    function placeInGutter() {
        if (!currentBlock || isOverText)
            return;
        const areaBox = area.getBoundingClientRect();
        const previewBox = preview.getBoundingClientRect();
        const blockBox = currentBlock.getBoundingClientRect();
        const lineHeight = parseFloat(getComputedStyle(currentBlock).lineHeight);
        const firstLineOffset = Number.isNaN(lineHeight) ? 0 : Math.max(0, (lineHeight - button.offsetHeight) / 2);
        const textStart = previewBox.left - areaBox.left + (parseFloat(getComputedStyle(preview).paddingLeft) || 0);
        button.style.top = (blockBox.top - areaBox.top + firstLineOffset) + 'px';
        button.style.left = (textStart - button.offsetWidth - GAP_PX) + 'px';
    }

    // Over the text: centered on the point while Alt is held (the click goes through it), otherwise right above it so the tapped
    // text stays visible. Kept inside the text's area.
    function showOverText(block, before, x, y, atPointer) {
        currentBlock = block;
        isOverText = true;
        linkTo(block, before);
        button.classList.add('is-over-text');
        setAtPointer(atPointer);
        button.hidden = false;

        const areaBox = area.getBoundingClientRect();
        const width = button.offsetWidth, height = button.offsetHeight;
        const left = Math.min(Math.max(0, x - areaBox.left - width / 2), areaBox.width - width);
        const top = atPointer ? y - areaBox.top - height / 2 : y - areaBox.top - height - 2 * GAP_PX;
        button.style.left = left + 'px';
        button.style.top = Math.max(0, top) + 'px';
    }

    function hide() {
        currentBlock = null;
        isOverText = false;
        button.classList.remove('is-over-text');
        setAtPointer(false);
        button.hidden = true;
    }

    // At the pointer, the whole page shows the cursor of a link (markdown.css), as the click goes to the button
    function setAtPointer(atPointer) {
        button.classList.toggle('is-at-pointer', atPointer);
        document.documentElement.classList.toggle('has-edit-here-at-pointer', atPointer);
    }

    // What the mouse shows: the button at the pointer while Alt is held, else the one in the gutter (which a narrow screen hasn't)
    function followPointer() {
        if (pointer === null)
            return;
        if (isAltHeld) {
            const place = textPlaceAt(pointer.x, pointer.y);
            if (place)
                showOverText(place.block, place.before, pointer.x, pointer.y, true);
            else
                hide();
        } else if (button.classList.contains('is-at-pointer')) {
            hide();
        }
        if (isAltHeld || NARROW_SCREEN.matches)
            return;
        const block = blockOnLine(pointer.y);
        if (block)
            showInGutter(block);
        else
            hide();
    }

    // ---- Mouse: the line it is on, or with Alt the character under it ----

    document.addEventListener('pointermove', function (e) {
        if (e.pointerType !== 'mouse')
            return;
        pointer = { x: e.clientX, y: e.clientY };
        isAltHeld = e.altKey;
        followPointer();
    });
    document.documentElement.addEventListener('mouseleave', function () {
        pointer = null;
        if (!(NARROW_SCREEN.matches && isOverText && !isAltHeld))
            hide();
    });
    // The text moves under a mouse that stays where it is
    window.addEventListener('scroll', followPointer, { passive: true });

    // Alt alone, without moving the mouse
    document.addEventListener('keydown', function (e) {
        wasPointerDown = false; // what gets the focus now comes from the keyboard
        if (e.key === 'Alt' && !isAltHeld) {
            isAltHeld = true;
            followPointer();
        }
    });
    document.addEventListener('keyup', function (e) {
        if (e.key !== 'Alt')
            return;
        // Some browsers move the focus to their menu when Alt is let go alone; not after it was used for this
        if (button.classList.contains('is-at-pointer'))
            e.preventDefault();
        isAltHeld = false;
        followPointer();
    });
    window.addEventListener('blur', function () {
        isAltHeld = false;
        if (button.classList.contains('is-at-pointer'))
            hide();
    });

    // With Alt the click goes through the button to whatever is under it, which mustn't do what it would (a link, a download)
    document.addEventListener('click', function (e) {
        if (!e.altKey || e.button !== 0 || !button.classList.contains('is-at-pointer'))
            return;
        e.preventDefault();
        e.stopPropagation();
        window.location.assign(button.href);
    }, true);

    // ---- Touch and keyboard: what is tapped or focused ----

    area.addEventListener('pointerdown', function () {
        wasPointerDown = true;
    });

    area.addEventListener('click', function (e) {
        wasPointerDown = false;
        if (button.contains(e.target) || e.altKey)
            return;
        if (!NARROW_SCREEN.matches) {
            showInGutter(blockAt(e.target));
            return;
        }
        // On a narrow screen a tap toggles it, at the character tapped (or the start of the block, if it wasn't on any text)
        const block = blockAt(e.target);
        if (!block || block === currentBlock) {
            hide();
            return;
        }
        const place = textPlaceAt(e.clientX, e.clientY);
        showOverText(block, place && place.block === block ? place.before : '', e.clientX, e.clientY, false);
    });

    // The keyboard: the block of what got the focus, at its start (a tap is handled as a click above)
    area.addEventListener('focusin', function (e) {
        if (wasPointerDown || button.contains(e.target))
            return;
        const block = blockAt(e.target);
        if (!block)
            return;
        if (!NARROW_SCREEN.matches) {
            showInGutter(block);
            return;
        }
        const box = block.getBoundingClientRect();
        showOverText(block, '', box.left + button.offsetWidth / 2, box.top, false);
    });

    // A tap anywhere else takes it away
    document.addEventListener('click', function (e) {
        wasPointerDown = false;
        if (!area.contains(e.target) && !e.altKey)
            hide();
    });

    // Diagrams and math are drawn later, and the text reflows when the window changes, which moves the blocks. A button over the text
    // would then be over some other text, so it goes away instead.
    function afterReflow() {
        if (isOverText && !isAltHeld)
            hide();
        placeInGutter();
        followPointer();
    }

    new ResizeObserver(afterReflow).observe(preview);
    NARROW_SCREEN.addEventListener('change', afterReflow);
})();
