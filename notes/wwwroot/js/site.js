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

// Swallows every click on such a button before anything else (inline handlers, listeners, shortcuts) gets to see it
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
