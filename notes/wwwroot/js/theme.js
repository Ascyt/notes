// Light and dark mode. Bootstrap does the styling: it follows the `data-bs-theme` attribute of <html>.
//
// The theme starts out as the one saved in localStorage; without one, as the one of the system (prefers-color-scheme), which
// keeps being followed until the theme is toggled for the first time. Toggling saves the choice.
//
//   getTheme()      'light' or 'dark'
//   toggleTheme()   switches to the other one, and saves it
//
// A `themechange` event is fired on window whenever the theme is applied, for what has to follow it by itself (e.g. Monaco, see editor.js).
// This script is loaded in the head, so the theme is set before the page is first painted.
(function () {
    const STORAGE_KEY = 'theme';
    const systemQuery = window.matchMedia('(prefers-color-scheme: dark)');

    // localStorage can throw (blocked storage, private mode), and then the theme just isn't kept
    function savedTheme() {
        try {
            const theme = localStorage.getItem(STORAGE_KEY);
            return theme === 'light' || theme === 'dark' ? theme : null;
        } catch {
            return null;
        }
    }

    function saveTheme(theme) {
        try {
            localStorage.setItem(STORAGE_KEY, theme);
        } catch {
        }
    }

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-bs-theme', theme);

        // The button says what it does, not what is shown
        const button = document.getElementById('theme-toggle');
        if (button) {
            const label = theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode';
            button.title = label;
            button.setAttribute('aria-label', label);
        }

        window.dispatchEvent(new CustomEvent('themechange', { detail: theme }));
    }

    window.getTheme = function () {
        return document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
    };

    window.toggleTheme = function () {
        const theme = window.getTheme() === 'dark' ? 'light' : 'dark';
        saveTheme(theme);
        applyTheme(theme);
    };

    applyTheme(savedTheme() || (systemQuery.matches ? 'dark' : 'light'));

    // Until the user has chosen, the theme of the system stays the one shown
    systemQuery.addEventListener('change', function (e) {
        if (savedTheme() === null)
            applyTheme(e.matches ? 'dark' : 'light');
    });

    document.addEventListener('DOMContentLoaded', function () {
        const button = document.getElementById('theme-toggle');
        if (!button)
            return;
        applyTheme(window.getTheme()); // sets the label now that the button exists
        button.addEventListener('click', window.toggleTheme);
    });
})();
