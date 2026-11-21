// Keeps the breadcrumb (_Breadcrumb.cshtml) to one line. The name at its end (the rename input) has the room it needs for the whole name
// first; the parents make room for it, outermost first: each becomes a dot (with the name as tooltip), then the outer ones go into one "…",
// which leads to the innermost of them. The innermost parent always stays, as its dot leads up. Only when even that isn't enough does the
// name get less room than it needs.
(function () {
    const row = document.currentScript.closest('.breadcrumb-row');
    const parents = [...row.querySelectorAll('.breadcrumb-parent')];
    const collapsed = document.getElementById('breadcrumb-collapsed');
    const collapsedLink = collapsed.querySelector('a');
    const rename = document.getElementById('rename');
    const input = document.getElementById('rename-input');
    const measuring = document.createElement('canvas').getContext('2d');

    function showAsDot(part, asDot) {
        part.querySelector('.breadcrumb-name').hidden = asDot;
        part.querySelector('.breadcrumb-dot').hidden = !asDot;
    }

    // How wide the input has to be for the whole name, at most as wide as it may be
    function wantedNameWidth() {
        const style = getComputedStyle(input);
        measuring.font = style.font;
        const frame = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight) + parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
        const wanted = Math.ceil(measuring.measureText(input.value).width + frame) + 2; // a little more, so that the end isn't cut off
        const maximum = parseFloat(getComputedStyle(rename).maxWidth);
        return Number.isNaN(maximum) ? wanted : Math.min(wanted, maximum);
    }

    // Nothing in the row shrinks while measuring, so whatever doesn't fit makes it wider than it is
    const fits = () => row.scrollWidth <= row.clientWidth + 1;

    function fit() {
        // 1. Everything in full, and the name as wide as it wants to be
        collapsed.hidden = true;
        for (const part of parents) {
            part.hidden = false;
            showAsDot(part, false);
        }
        rename.style.flex = '0 0 ' + wantedNameWidth() + 'px';

        // 2. Dots in place of the names, outermost first
        for (let i = 0; i < parents.length && !fits(); i++)
            showAsDot(parents[i], true);

        // 3. The outer dots into "…", which leads to the innermost one it stands for
        for (let i = 0; i < parents.length - 1 && !fits(); i++) {
            parents[i].hidden = true;
            collapsed.hidden = false;
            const hiddenNames = parents.slice(0, i + 1).map(part => part.dataset.name).join('/');
            collapsedLink.href = parents[i].dataset.address;
            collapsedLink.title = hiddenNames;
            collapsedLink.setAttribute('aria-label', hiddenNames);
        }

        // 4. The name takes whatever room is left (and shrinks if even this doesn't fit)
        rename.style.flex = '';
    }

    // Again whenever the width changes, and once the fonts are there (they change the widths)
    let lastWidth = null;
    new ResizeObserver(function () {
        if (row.clientWidth === lastWidth)
            return;
        lastWidth = row.clientWidth;
        fit();
    }).observe(row);
    input.addEventListener('input', fit);
    if (document.fonts)
        document.fonts.ready.then(fit);
})();
