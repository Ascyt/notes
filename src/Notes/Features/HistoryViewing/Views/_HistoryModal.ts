// Choosing an earlier state of the current file or directory (_HistoryModal.cshtml, which says how it works): the dialog behind the
// History button of the toolbar. The names of the parameters and the kind of the entry come from the server, as data- attributes of the dialog.
(function () {
    // A time range in which the entry can be seen, as the server tells it
    interface VisibleTimeRange {
        after: string;
        until: string | null;
    }

    const modal = document.getElementById('history-modal')!;
    const texts = textsOf('history-texts-modal');
    const PARAMETER = modal.dataset.beforeParameter!;
    const VISIBLE_TIMES_PARAMETER = modal.dataset.visibleTimesParameter!;
    const KIND = modal.dataset.kind;
    const form = document.getElementById('history-form')!;
    const input = document.getElementById('history-before') as HTMLInputElement;
    const apply = document.getElementById('history-apply')!;
    const problemText = document.getElementById('history-problem')!;
    const visibleTimesList = document.getElementById('history-times')!;

    // The times at which the entry can be seen: [{ after, until }], each after `after` (the state's commit) up to `until` (null: now).
    // null while they aren't known (loading, or they couldn't be asked for): then only the server says whether a time can be seen.
    let visibleTimes: VisibleTimeRange[] | null = null;

    // What datetime-local reads and writes: the local time, to the second (the states are), without a time zone
    function toLocalInputValue(date: Date): string {
        const pad = (number: number) => String(number).padStart(2, '0');
        return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate())
            + 'T' + pad(date.getHours()) + ':' + pad(date.getMinutes()) + ':' + pad(date.getSeconds());
    }

    function displayInLocalTime(date: Date): string {
        return date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' });
    }

    function isAtVisibleTime(date: Date): boolean {
        return visibleTimes!.some(range => date > new Date(range.after) && (range.until === null || date <= new Date(range.until)));
    }

    // Why the time in the field can't be viewed, or null if it can
    function whyTimeCantBeViewed(value: string): string | null {
        if (value === '')
            return texts.chooseATime;
        const date = new Date(value); // a datetime-local value is read as local time
        if (isNaN(date.getTime()))
            return texts.notATime;
        if (date.getTime() > Date.now())
            return texts.inTheFuture;
        if (visibleTimes !== null && !isAtVisibleTime(date))
            return KIND === 'file' ? texts.fileCantBeSeenThen : texts.directoryCantBeSeenThen;
        return null;
    }

    function updateViewButton(): void {
        const problem = whyTimeCantBeViewed(input.value);
        setButtonDisabled(apply, problem);
        problemText.textContent = problem || '';
        input.classList.toggle('is-invalid', problem !== null);
    }

    // Lists the visible times, or instead the text (while loading, when they couldn't be loaded)
    function showVisibleTimes(textInstead?: string): void {
        visibleTimesList.replaceChildren();
        if (textInstead !== undefined) {
            const item = document.createElement('li');
            item.className = 'fst-italic';
            item.textContent = textInstead;
            visibleTimesList.append(item);
            return;
        }
        for (const range of visibleTimes!) {
            const item = document.createElement('li');
            item.dataset.after = range.after;
            item.dataset.until = range.until || '';
            item.textContent = fillIn(texts.seenBetween, {
                after: displayInLocalTime(new Date(range.after)),
                until: range.until ? displayInLocalTime(new Date(range.until)) : texts.now
            });
            visibleTimesList.append(item);
        }
        if (visibleTimes!.length === 0)
            showVisibleTimes(texts.atNoTime);
    }

    async function loadVisibleTimes(): Promise<void> {
        visibleTimes = null;
        showVisibleTimes(texts.loading);
        try {
            const response = await fetch(window.location.pathname + '?' + VISIBLE_TIMES_PARAMETER, { headers: { 'Accept': 'application/json' } });
            if (!response.ok)
                throw new Error(String(response.status));
            visibleTimes = (await response.json()).ranges as VisibleTimeRange[];
            showVisibleTimes();
            // The oldest is last
            if (visibleTimes.length > 0)
                input.min = toLocalInputValue(new Date(visibleTimes[visibleTimes.length - 1].after));
        } catch {
            showVisibleTimes(texts.timesNotLoaded);
        }
        updateViewButton();
    }

    window.openHistoryModal = function () {
        const now = new Date();
        input.max = toLocalInputValue(now);
        input.removeAttribute('min');
        // The state that is shown, if it is an earlier one; otherwise now, to go back from
        const current = new Date(new URLSearchParams(window.location.search).get(PARAMETER) || '');
        input.value = toLocalInputValue(isNaN(current.getTime()) ? now : current);
        updateViewButton();
        bootstrap.Modal.getOrCreateInstance(modal).show();
        void loadVisibleTimes();
    };
    onClickOf('history-button', () => window.openHistoryModal());

    modal.addEventListener('shown.bs.modal', () => input.focus());
    input.addEventListener('input', updateViewButton);

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        updateViewButton();
        if (whyTimeCantBeViewed(input.value) !== null)
            return;

        // Only the time: the other views (?edit and the like) change things, which an earlier state can't
        const before = new Date(input.value).toISOString().replace(/\.\d{3}Z$/, 'Z');
        // (Digits, '-', ':', 'T' and 'Z' need no escaping in a query, and stay readable)
        window.location.href = window.location.pathname + '?' + PARAMETER + '=' + before;
    });
})();
