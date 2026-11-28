// A file that isn't text (BinaryView.cshtml): media the browser shows itself, with the toolbar's fullscreen button. Comes before the
// media in the page, so that it hears of their errors from the start.
(function () {
    // Safari on an iPhone only has fullscreen for videos, by this name
    type MaybeFullscreenElement = HTMLElement & { webkitEnterFullscreen?: () => void };

    const texts = textsOf('binary-view-texts');
    // Whatever the browser fails to show (a format it doesn't know, a broken file) gets the message instead. Errors of what is loaded
    // don't bubble, so they are caught on their way down.
    document.addEventListener('error', function (e) {
        if (!(e.target instanceof HTMLElement) || !e.target.hasAttribute('data-unviewable-on-error'))
            return;
        e.target.hidden = true;
        document.getElementById('unviewable')!.hidden = false;
        const fullscreenButton = document.getElementById('fullscreen-button');
        if (fullscreenButton)
            setButtonDisabled(fullscreenButton, texts.cantBeShown);
    }, true);

    // The toolbar's fullscreen button (ToolbarButton.Fullscreen), which leaves fullscreen again too
    window.toggleMediaFullscreen = function () {
        const media = document.getElementById('media-view') as MaybeFullscreenElement;
        if (document.fullscreenElement)
            void document.exitFullscreen();
        else if (typeof media.requestFullscreen === 'function')
            void media.requestFullscreen();
        else if (media.webkitEnterFullscreen)
            media.webkitEnterFullscreen(); // the only fullscreen of a video on an iPhone
    };
    onClickOf('fullscreen-button', () => window.toggleMediaFullscreen());

    // Where the browser has no fullscreen for it (an image or a PDF on an iPhone), the button says so
    document.addEventListener('DOMContentLoaded', function () {
        const media = document.getElementById('media-view') as MaybeFullscreenElement | null;
        const button = document.getElementById('fullscreen-button');
        if (media && button && typeof media.requestFullscreen !== 'function' && !media.webkitEnterFullscreen)
            setButtonDisabled(button, texts.noFullscreen);
    });
})();
