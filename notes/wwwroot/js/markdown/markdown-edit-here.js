// The button left of the rendered markdown that opens the editor where the part of the text next to it is written. With a mouse it
// shows for the part on the line the pointer is on, anywhere across the page: the innermost block (paragraph, list item, heading, …)
// that MarkdownRenderer marked with where it starts. Otherwise it shows for the part that is tapped or focused.
// The editor reads that place from the address (#line=…&column=…).
(function () {
    const GAP_PX = 4;

    const preview = document.getElementById('markdown-preview');
    const button = document.getElementById('edit-here-button');
    const area = button.parentElement; // the text and the button, which is positioned in it
    let currentBlock = null;
    let pointerY = null; // where the mouse is, while it is on the page

    function blockAt(target) {
        const block = target instanceof Element ? target.closest('[data-source-line]') : null;
        return block && preview.contains(block) ? block : null;
    }

    // The innermost block that reaches over that height of the screen. Blocks come in document order, so an inner one comes after the
    // one around it.
    function blockOnLine(y) {
        let found = null;
        for (const block of preview.querySelectorAll('[data-source-line]')) {
            const box = block.getBoundingClientRect();
            if (y >= box.top && y < box.bottom && (!found || found.contains(block)))
                found = block;
        }
        return found;
    }

    function show(block) {
        if (!block || block === currentBlock)
            return;
        currentBlock = block;
        button.href = '?edit#line=' + encodeURIComponent(block.dataset.sourceLine) + '&column=' + encodeURIComponent(block.dataset.sourceColumn);
        button.hidden = false;
        place();
    }

    function hide() {
        currentBlock = null;
        button.hidden = true;
    }

    // In the gutter left of the text, at the height of the block's first line
    function place() {
        if (!currentBlock)
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

    function followPointer() {
        if (pointerY === null)
            return;
        const block = blockOnLine(pointerY);
        if (block)
            show(block);
        else
            hide();
    }

    // ---- Mouse: the line it is on ----

    document.addEventListener('pointermove', function (e) {
        if (e.pointerType !== 'mouse')
            return;
        pointerY = e.clientY;
        followPointer();
    });
    document.documentElement.addEventListener('mouseleave', function () {
        pointerY = null;
        hide();
    });
    // The text moves under a mouse that stays where it is
    window.addEventListener('scroll', followPointer, { passive: true });

    // ---- Touch and keyboard: what is tapped or focused ----

    function showFor(e) {
        if (!button.contains(e.target))
            show(blockAt(e.target));
    }

    area.addEventListener('click', showFor);
    area.addEventListener('focusin', showFor);

    // A tap anywhere else takes it away
    document.addEventListener('click', function (e) {
        if (!area.contains(e.target))
            hide();
    });

    // Diagrams and math are drawn later, and the text reflows when the window changes, which moves the blocks
    new ResizeObserver(function () {
        place();
        followPointer();
    }).observe(preview);
})();
