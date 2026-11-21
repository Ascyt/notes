// The card of the earlier state that the page shows (_EarlierStateCard.cshtml, which says what it does). The names of the parameters
// of the query come from the server, as data- attributes of the card.
(function () {
    const card = document.getElementById('history-card');
    const COMMIT_PARAMETER = card.dataset.commitParameter;
    const STATE_PARAMETERS = [card.dataset.beforeParameter, COMMIT_PARAMETER];
    const shownState = card.dataset.parameter + '=' + card.dataset.value + (card.dataset.changes ? '&' + card.dataset.changesParameter : '');
    const timeElement = document.getElementById('history-card-time');
    document.getElementById('floating-cards').append(card); // last, so the messages that come in are above it

    // The time in the browser's own time zone (the server only knows UTC); a commit to the second, as it was made
    timeElement.textContent = new Date(timeElement.dateTime).toLocaleString(undefined,
        { dateStyle: 'medium', timeStyle: card.dataset.parameter === COMMIT_PARAMETER ? 'medium' : 'short' });

    // Adds the state to a link that leads to a page of the notes. Links within the page (#…), to the app's own pages (/_/…), to other sites,
    // the way back to now and links that ask for a state already are left as they are.
    function keepStateOnLink(link) {
        const href = link.getAttribute('href');
        if (!href || href.startsWith('#') || link.hasAttribute('data-history-exit'))
            return;
        const url = new URL(href, window.location.href);
        if (url.origin !== window.location.origin || url.pathname.startsWith('/_/') || STATE_PARAMETERS.some(name => url.searchParams.has(name)))
            return;
        // Appended as it is (not through URLSearchParams), so that "?download" stays "?download" instead of becoming "?download="
        url.search = (url.search ? url.search + '&' : '?') + shownState;
        link.setAttribute('href', url.pathname + url.search + url.hash);
    }

    // Once the whole page is there, also the parts of it that come after the card
    document.addEventListener('DOMContentLoaded', function () {
        for (const link of document.querySelectorAll('a[href]'))
            keepStateOnLink(link);
    });
})();
