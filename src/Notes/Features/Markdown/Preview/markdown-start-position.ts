// Where the rendered view of a markdown file starts: the editor's way to it leads to the place its cursor was at (#line=…&column=…, both
// counted from 1; see linkToPreview in editor-start-position.ts). The view then shows the block that place is in, in the middle of the
// screen. Without a place it starts at the top, as any page does.
(function () {
    const parameters = new URLSearchParams(window.location.hash.slice(1));
    const line = parseInt(parameters.get('line') ?? '', 10);
    const column = parseInt(parameters.get('column') ?? '', 10) || 1;
    if (Number.isNaN(line))
        return;
    // The address doesn't keep it, so a reload starts at the top
    history.replaceState(history.state, '', window.location.href.split('#')[0]);

    const startsBefore = (block: HTMLElement) => Number(block.dataset.sourceLine) < line
        || (Number(block.dataset.sourceLine) === line && Number(block.dataset.sourceColumn) <= column);

    // The blocks are in the order of the text, so the last one that starts before the place is the innermost one around it (or, between
    // two blocks, the one above). Hidden blocks have no place on the screen.
    const block = Array.from(document.querySelectorAll<HTMLElement>('#markdown-preview [data-source-line]'))
        .filter(candidate => candidate.getBoundingClientRect().height > 0 && startsBefore(candidate))
        .pop();
    if (!block)
        return;
    block.scrollIntoView({ block: 'center' });

    // Images above it move it down once they have loaded: then it is shown again, unless the page was scrolled in the meantime
    const scrolledTo = window.scrollY;
    window.addEventListener('load', function () {
        if (window.scrollY === scrolledTo)
            block.scrollIntoView({ block: 'center' });
    }, { once: true });
})();
