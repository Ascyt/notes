// Renders what the server leaves to the browser in the preview of a markdown file (see MarkdownRenderer): math with KaTeX,
// mermaid code blocks as diagrams, and what abbreviations stand for. The page loads KaTeX and mermaid only if the file has math or
// diagrams, and this script after them.
(function () {
    const preview = document.getElementById('markdown-preview');

    // ---- Abbreviations ----

    // The browser shows the title of an <abbr> only on hover, which phones don't have. A tooltip that also opens when the abbreviation
    // has the focus works there (tapping focuses it, tapping elsewhere closes it) and with the keyboard.
    // Bootstrap's script comes at the end of the page, after this one.
    document.addEventListener('DOMContentLoaded', function () {
        const abbreviations = preview.querySelectorAll('abbr[title]');
        for (const abbreviation of abbreviations) {
            abbreviation.tabIndex = 0;
            new bootstrap.Tooltip(abbreviation, { trigger: 'hover focus' });
            abbreviation.addEventListener('click', () => abbreviation.focus()); // not every phone browser focuses it on a tap by itself
        }
        // ...nor takes the focus away when tapping somewhere that can't have it
        if (abbreviations.length > 0)
            document.addEventListener('click', function (e) {
                if (document.activeElement instanceof HTMLElement && document.activeElement.matches('#markdown-preview abbr')
                    && !document.activeElement.contains(e.target))
                    document.activeElement.blur();
            });
    });

    // ---- Math ----

    // Markdig writes inline math as <span class="math">\(...\)</span> and display math as <div class="math">\[...\]</div>
    if (window.katex) {
        for (const element of preview.querySelectorAll('.math')) {
            const isDisplay = element.tagName === 'DIV';
            const source = element.textContent.trim().replace(/^\\[([]/, '').replace(/\\[)\]]$/, '');
            // An error is shown in place of the formula (in red), instead of stopping the rest
            katex.render(source, element, { displayMode: isDisplay, throwOnError: false });
        }
    }

    // ---- Diagrams ----

    if (window.mermaid) {
        // The text of each block, read from the escaped code block, so nothing of it ever becomes markup on its own
        const diagrams = [...preview.querySelectorAll('pre > code.language-mermaid')].map(function (code) {
            const container = document.createElement('div');
            container.className = 'markdown-diagram';
            const block = code.parentElement;
            block.replaceWith(container);
            return { container, block, source: code.textContent };
        });

        let renderCount = 0;

        // Again whenever the theme changes, as mermaid bakes its colors into the picture
        async function renderAll() {
            // Strict: mermaid cleans what the diagram contains, and doesn't allow scripts or clicks in it
            mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: getTheme() === 'dark' ? 'dark' : 'default' });
            for (const diagram of diagrams) {
                const id = 'markdown-diagram-' + renderCount++;
                try {
                    const { svg } = await mermaid.render(id, diagram.source);
                    diagram.container.innerHTML = svg;
                } catch (error) {
                    // The text as it was, and why it isn't a diagram
                    document.getElementById('d' + id)?.remove(); // what mermaid leaves behind of a failed attempt
                    const message = document.createElement('div');
                    message.className = 'small text-danger';
                    message.dataset.diagramError = '';
                    message.textContent = 'This diagram could not be drawn: ' + (error && error.message ? error.message : error);
                    diagram.container.replaceChildren(diagram.block, message);
                }
            }
        }

        // Kept, so that printing (markdown-print.js) can wait for the diagrams in the colors of the theme it prints in
        window.markdownDiagramsRendered = renderAll();
        window.addEventListener('themechange', function () {
            window.markdownDiagramsRendered = renderAll();
        });
    }
})();
