// The sidebar of the list of changes (_CommitSidebar.cshtml, which says how it works). The names of the parameters of the query come
// from the server, as data- attributes of the sidebar.
(function () {
    const sidebar = document.getElementById('commit-sidebar');
    const LIST_PARAMETER = sidebar.dataset.listParameter;
    const PAGE_PARAMETER = sidebar.dataset.pageParameter;
    const NEWER_PARAMETER = sidebar.dataset.newerParameter;
    const COMMIT_PARAMETER = sidebar.dataset.commitParameter;
    const STATE_PARAMETERS = [sidebar.dataset.beforeParameter, COMMIT_PARAMETER];

    const contents = document.getElementById('commit-sidebar-contents');
    const hideButton = document.getElementById('commit-sidebar-hide');
    const toolbarButton = document.getElementById('commits-button');
    const isCommitView = sidebar.dataset.commitView === 'true';
    const narrow = window.matchMedia('(max-width: 767.98px)');
    // Next to the page rather than inside it, so that the page can make room for it (or be hidden for it)
    document.body.prepend(sidebar);

    // The state the page shows (?before= or ?commit=), which the list starts from
    const query = new URLSearchParams(window.location.search);
    const shownStateName = STATE_PARAMETERS.find(name => query.has(name));
    const shownState = shownStateName ? '&' + shownStateName + '=' + encodeURIComponent(query.get(shownStateName)) : '';

    let hasLoaded = contents.children.length > 0;

    const isShown = () => !sidebar.hidden;
    // Always shown: in the view of a commit, where there is room for it next to the page
    const isPinned = () => isCommitView && !narrow.matches;
    const hasFocus = () => sidebar.contains(document.activeElement);
    const lines = () => [...contents.querySelectorAll('.commit-line')];

    // Only one line can be reached with Tab (the one last gone to): the others are reached with the arrow keys
    function focusLine(line) {
        for (const other of lines())
            other.tabIndex = other === line ? 0 : -1;
        line.focus();
        line.scrollIntoView({ block: 'nearest' });
    }

    // The line gone to last, otherwise the commit that is shown, otherwise the newest; the sidebar itself while there is none
    function focusList() {
        const line = contents.querySelector('.commit-line[tabindex="0"]') || contents.querySelector('[aria-current]') || lines()[0];
        if (line)
            focusLine(line);
        else
            sidebar.focus();
    }

    // What came from the server: the times in the browser's time zone, the arrow keys going to the lines, the buttons in their state
    function prepare(root) {
        for (const time of root.querySelectorAll('time.commit-time')) {
            const date = new Date(time.dateTime);
            time.textContent = date.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
            time.closest('.commit-line').title = date.toLocaleString(undefined, { dateStyle: 'full', timeStyle: 'medium' });
        }
        for (const line of root.querySelectorAll('.commit-line'))
            line.addEventListener('shortcut', function (e) {
                e.preventDefault();
                focusLine(line);
            });
        for (const button of root.querySelectorAll('[data-label]'))
            refreshButton(button);
    }

    // Gives each arrow key (and Escape) to what it leads to from where the focus is; nothing while the focus is elsewhere
    function updateKeys() {
        for (const element of document.querySelectorAll('[data-sidebar-key]')) {
            element.removeAttribute('key');
            element.removeAttribute('data-sidebar-key');
        }
        if (!hasFocus())
            return;

        const keys = new Map();
        const give = (element, key) => {
            if (element && element.getAttribute('aria-disabled') !== 'true')
                keys.set(element, [...(keys.get(element) || []), key]);
        };
        const all = lines();
        const index = all.indexOf(document.activeElement);
        const newer = document.getElementById('commit-newer');
        const older = document.getElementById('commit-older');
        // Down: the next commit, after the last one the next page
        give(index + 1 < all.length ? all[index + 1] : older, 'ArrowDown');
        // Up: the commit before, above the first one the newer ones
        if (index > 0)
            give(all[index - 1], 'ArrowUp');
        else if (index === 0)
            give(document.getElementById('commit-show-newer') || newer, 'ArrowUp');
        give(newer, 'ArrowLeft');
        give(older, 'ArrowRight');
        // Shift+Enter: the changes of the commit of the line (Enter, the link's own, opens its state)
        if (index >= 0)
            give(all[index].parentElement.querySelector('.commit-compare'), '+Enter');
        // Escape leaves the sidebar: where it is always shown only the focus goes (the sidebar's own shortcut, shown over the toolbar
        // button), elsewhere it is hidden
        give(isPinned() ? sidebar : hideButton, 'Escape');

        for (const [element, elementKeys] of keys) {
            element.setAttribute('key', elementKeys.join(' '));
            element.setAttribute('data-sidebar-key', '');
        }
    }

    async function fetchHtml(parameters) {
        const response = await fetch(window.location.pathname + '?' + LIST_PARAMETER + '&' + parameters);
        if (!response.ok)
            throw new Error(String(response.status));
        return await response.text();
    }

    // Puts a page of the list in, with a spinner on the button that asked for it until then. If the focus is in the sidebar, it goes to
    // the first or the last commit of the page (`focusAt`), as the arrow keys go. Returns whether it could be loaded.
    async function loadPage(number, button, focusAt) {
        setButtonBusy(button, true);
        let html;
        try {
            html = await fetchHtml(PAGE_PARAMETER + '=' + number + shownState);
        } catch {
            notify('The changes could not be loaded.', 'danger');
            setButtonBusy(button, false);
            return false;
        }
        const hadFocus = hasFocus();
        contents.innerHTML = html;
        prepare(contents);
        hasLoaded = true;
        setButtonBusy(button, false); // (the toolbar's; one of the pages is gone with the old page)

        const all = lines();
        if (hadFocus && all.length > 0)
            focusLine(focusAt === 'last' ? all[all.length - 1] : all[0]);
        else if (hadFocus)
            sidebar.focus();
        updateKeys();
        return true;
    }

    // Puts the newer commits in place of "Show newer…" (with the next "Show newer…" above them, if there are more), with a spinner on it
    // until then. If the focus is in the sidebar, it goes to the one right above those there were, as the up arrow goes.
    async function loadNewer(button) {
        setButtonBusy(button, true);
        let html;
        try {
            html = await fetchHtml(NEWER_PARAMETER + '&' + COMMIT_PARAMETER + '=' + button.dataset.newerAfter);
        } catch {
            notify('The newer changes could not be loaded.', 'danger');
            setButtonBusy(button, false);
            return;
        }
        const hadFocus = hasFocus();
        const template = document.createElement('template');
        template.innerHTML = html;
        const added = [...template.content.querySelectorAll('.commit-line')];
        prepare(template.content);
        button.replaceWith(template.content);
        if (hadFocus)
            focusLine(added.length > 0 ? added[added.length - 1] : lines()[0]);
        updateKeys();
    }

    // The focus goes back to the page as a whole, not to any one control of it
    function leaveFocus() {
        if (hasFocus())
            document.activeElement.blur();
    }

    function setShown(shown) {
        if (!shown)
            leaveFocus();
        sidebar.hidden = !shown;
        updateKeys();
    }

    // Shows it, asking for the list first if it isn't there yet (with a spinner on the toolbar button meanwhile)
    async function show() {
        if (!hasLoaded && !await loadPage(1, toolbarButton))
            return;
        setShown(true);
        focusList();
    }

    function moveFocus() {
        if (hasFocus())
            leaveFocus();
        else
            focusList();
    }

    // The click: where it is always shown it moves the focus in and out, elsewhere it shows (and focuses) or hides it
    window.toggleCommitSidebar = function () {
        if (isPinned())
            moveFocus();
        else if (isShown())
            setShown(false);
        else
            show();
    };
    toolbarButton.addEventListener('click', () => window.toggleCommitSidebar());

    // The shortcut: the same where it is always shown, elsewhere hidden → shown and focused → focused (when the focus went elsewhere) → hidden
    toolbarButton.addEventListener('shortcut', function (e) {
        e.preventDefault();
        if (isPinned())
            moveFocus();
        else if (!isShown())
            show();
        else if (!hasFocus())
            focusList();
        else
            setShown(false);
    });

    hideButton.addEventListener('click', () => setShown(false));
    sidebar.addEventListener('shortcut', function (e) {
        e.preventDefault();
        leaveFocus();
    });

    contents.addEventListener('click', function (e) {
        const button = e.target instanceof Element ? e.target.closest('button') : null;
        if (!button)
            return;
        if (button.id === 'commit-show-newer')
            loadNewer(button);
        else if (button.dataset.page)
            loadPage(Number(button.dataset.page), button, button.id === 'commit-newer' ? 'last' : 'first');
    });

    sidebar.addEventListener('focusin', updateKeys);
    // (Once the focus is where it went)
    sidebar.addEventListener('focusout', () => setTimeout(updateKeys));

    prepare(contents);
    // In the view of a commit there is not enough room next to the page on a narrow screen: there it is shown when asked for, as elsewhere
    if (isCommitView && narrow.matches)
        sidebar.hidden = true;
    contents.querySelector('[aria-current]')?.scrollIntoView({ block: 'nearest' });
})();
