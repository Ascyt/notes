// Buttons that are there but can't be used right now are never just greyed out: they say why, and while they work they show a spinner.
//
//   setButtonDisabled(button, 'Select something first.')   greys it out and shows the reason on hover
//   setButtonDisabled(button, null)                         makes it usable again
//   setButtonBusy(button, true)                             replaces its icon with a spinner (and it can't be used) until false
//
// This works for buttons that carry their label in `data-label` (and, if disabled from the start, the reason in `data-disabled-reason`),
// like those of the toolbar: the label is the tooltip as long as there is no reason. A button with an icon `.btn-icon` and a
// `.btn-spinner` next to it can show the spinner.
//
// The buttons stay clickable as far as the browser is concerned, but do nothing (see below): a real `disabled` attribute would stop some
// browsers from showing the tooltip, and silences mouse events.

function refreshButton(button) {
    const reason = button.dataset.disabledReason || '';
    const busy = button.dataset.busy === 'true';
    const label = button.dataset.label || '';

    button.setAttribute('aria-disabled', String(reason !== '' || busy));
    button.classList.toggle('is-disabled', reason !== '');
    button.title = reason !== '' ? (label !== '' ? label + ': ' + reason : reason) : label;

    const icon = button.querySelector('.btn-icon');
    const spinner = button.querySelector('.btn-spinner');
    if (icon)
        icon.classList.toggle('d-none', busy);
    if (spinner)
        spinner.classList.toggle('d-none', !busy);
}

window.setButtonDisabled = function (button, reason) {
    button.dataset.disabledReason = reason || '';
    refreshButton(button);
};

window.setButtonBusy = function (button, busy) {
    button.dataset.busy = String(Boolean(busy));
    refreshButton(button);
};

// Runs the function when the element with that id is clicked, if the page has one. The scripts of the page attach their buttons with
// it (there are no inline handlers: the policy of the site doesn't run them), also those that come later in the page than the script.
window.onClickOf = function (id, handler) {
    const attach = () => document.getElementById(id)?.addEventListener('click', () => handler());
    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', attach);
    else
        attach();
};

// An image or video with data-hidden-on-error is hidden if it can't be loaded (what is behind it shows instead). Errors of what is
// loaded don't bubble, so they are caught on their way down; and this script is in the head, so none comes before it.
document.addEventListener('error', function (e) {
    if (e.target instanceof Element && e.target.hasAttribute('data-hidden-on-error'))
        e.target.hidden = true;
}, true);

// Swallows every click on such a button before anything else (listeners, shortcuts) gets to see it
document.addEventListener('click', function (e) {
    if (e.target instanceof Element && e.target.closest('[aria-disabled="true"]')) {
        e.preventDefault();
        e.stopImmediatePropagation();
    }
}, true);

// The buttons made by the server start out in the state their data attributes describe
document.addEventListener('DOMContentLoaded', function () {
    for (const button of document.querySelectorAll('[data-label]'))
        refreshButton(button);
});

// The heights of the parts that stay at the top of the screen (site.css), as they change: the navbar's menu opens on a phone
document.addEventListener('DOMContentLoaded', function () {
    const parts = { '--site-header-height': document.getElementById('site-header'), '--breadcrumb-height': document.querySelector('.sticky-breadcrumb') };
    const observer = new ResizeObserver(function () {
        for (const [property, element] of Object.entries(parts))
            if (element)
                // Not offsetHeight: rounded to whole pixels, it leaves a gap of a fraction of one (on a scaled screen) the page shows through
                document.documentElement.style.setProperty(property, element.getBoundingClientRect().height + 'px');
    });
    for (const element of Object.values(parts))
        if (element)
            observer.observe(element);
});
