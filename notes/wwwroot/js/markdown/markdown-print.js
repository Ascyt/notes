// Saving the rendered markdown as a PDF: the browser's print dialog does it ("Save as PDF"), which keeps the text sharp and selectable.
// markdown.css leaves out everything around the text when printing. Printing happens in the light theme, which is what reads on paper,
// and with the name of the file as the title of the page, which the browser suggests as the name of the PDF. Both are put back after.
// Every page has the name of the file and the date at the top and its number at the bottom, instead of the browser's header and footer,
// which are drawn right at the edge of the paper.
//
//   printMarkdown()   opens the print dialog, once the diagrams are drawn in the light theme
//
// Printing from the browser's menu goes through the same (beforeprint), but can't wait for the diagrams.
(function () {
    // Browsers that can't replace their own header and footer (CSS page margin boxes) print theirs, which only an option of theirs turns off
    const canReplaceHeaderAndFooter = 'CSSMarginRule' in window;
    let themeBeforePrint = null;
    let titleBeforePrint = null;

    function fileName() {
        const name = decodeURIComponent(location.pathname.split('/').pop());
        return name.replace(/\.md$/i, '') || name;
    }

    // The header of every page (markdown.css has the footer). CSS.escape makes the texts safe inside the quotes.
    const headerStyle = document.createElement('style');
    document.head.appendChild(headerStyle);

    function setHeader() {
        const look = 'font: 9pt sans-serif; color: #6c757d;';
        headerStyle.textContent = '@media print { @page {'
            + ' @top-left { content: "' + CSS.escape(fileName()) + '"; ' + look + ' }'
            + ' @top-right { content: "' + CSS.escape(new Date().toLocaleString()) + '"; ' + look + ' }'
            + ' } }';
    }

    function prepare() {
        if (themeBeforePrint !== null)
            return;
        themeBeforePrint = getTheme();
        titleBeforePrint = document.title;
        document.title = fileName();
        setHeader();
        if (themeBeforePrint !== 'light')
            showTheme('light');
    }

    function restore() {
        if (themeBeforePrint === null)
            return;
        if (themeBeforePrint !== 'light')
            showTheme(themeBeforePrint);
        document.title = titleBeforePrint;
        themeBeforePrint = null;
    }

    window.addEventListener('beforeprint', prepare);
    window.addEventListener('afterprint', restore);

    window.printMarkdown = async function () {
        // The print dialog blocks the page, so the hint has to be drawn before it opens: the second frame comes after the hint's paint
        if (!canReplaceHeaderAndFooter) {
            notify('For the best result, uncheck "Print headers and footers" in the print dialog. Use a Chromium-based browser for properly styled headers and footers.', 'info');
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        }
        prepare();
        await window.markdownDiagramsRendered; // only there for a file with diagrams
        window.print();
    };
})();
