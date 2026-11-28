// Sends one file to the server in chunks (see UploadHandler), for every script that uploads (_Upload.js, the media of the editor):
//
//   await uploadChunks.uploadFile(file, {
//       directory,   the address of the directory it goes into (e.g. location.pathname of its page)
//       name,        the name it is to get (by default its own)
//       folder,      folders inside the directory to put it into ("assets/clips"), made if they aren't there; optional
//       chunkSize,   bytes per request (from the server, see _Upload.ChunkSize)
//       texts,       the texts of UploadTexts (serverAnswered, networkError)
//       onBytes      optional, called with the number of bytes of each chunk that arrived, and how far the file is on the server then
//       unzip,       optional: a .zip or .7z is to be unzipped into a new folder once it is uploaded (in the background, on the server)
//       signal,      optional AbortSignal: aborting it stops sending (and discards what was sent)
//       resume,      optional { id, offset }: goes on with an upload that was started before (from offset on); if the server no longer
//                    has it (discarded an hour after its last part), it starts over
//       onStarted    optional, called with the id of the upload once the server made it (to go on with it later)
//   })                returns the virtual path the file got, with " (1)" added if the name was taken; throws an Error with the reason (and
//                     its .status, if the server answered). A failed chunk leaves what was sent on the server, to go on with (resume).
(function () {
    const MAX_ATTEMPTS = 3;

    function token() {
        return document.querySelector('input[name="__RequestVerificationToken"]').value;
    }

    function failure(message, status) {
        const error = new Error(message);
        error.status = status;
        return error;
    }

    // Sends one chunk, retrying on network errors and server failures. Returns what the server answered.
    async function sendChunk(url, blob, texts, signal) {
        let lastError;
        for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/octet-stream' },
                    body: blob,
                    signal
                });
                if (response.ok)
                    return await response.text();
                lastError = failure(fillIn(texts.serverAnswered, { status: response.status }), response.status);
                if (response.status >= 400 && response.status < 500) {
                    // e.g. a permission that is missing: retrying won't help
                    throw failure((await response.text()) || lastError.message, response.status);
                }
            } catch (e) {
                if (signal && signal.aborted)
                    throw e;
                if (e.status)
                    throw e;
                lastError = failure(texts.networkError);
            }
            await new Promise(resolve => setTimeout(resolve, 500 * attempt));
        }
        throw lastError;
    }

    // The server makes the id of an upload; it only works for the user who asked for it
    async function startUpload(directory, texts, signal) {
        let response;
        try {
            response = await fetch(directory + '?upload=start', { method: 'POST', headers: { 'RequestVerificationToken': token() }, signal });
        } catch (e) {
            if (signal && signal.aborted)
                throw e;
            throw failure(texts.networkError);
        }
        if (!response.ok)
            throw failure((await response.text()) || fillIn(texts.serverAnswered, { status: response.status }), response.status);
        return await response.text();
    }

    async function uploadFile(file, options) {
        const { directory, chunkSize, texts, signal } = options;
        const onBytes = options.onBytes || (() => { });
        let id = options.resume ? options.resume.id : null;
        let offset = options.resume ? options.resume.offset : 0;
        const baseOf = id => directory + '?upload=' + id + '&name=' + encodeURIComponent(options.name || file.name)
            + (options.folder ? '&folder=' + encodeURIComponent(options.folder) : '') + (options.unzip ? '&unzip' : '');

        if (!id) {
            id = await startUpload(directory, texts, signal);
            if (options.onStarted)
                options.onStarted(id);
        }
        try {
            // Do-while so that an empty file still sends its one (empty) chunk
            let answer;
            do {
                const end = Math.min(offset + chunkSize, file.size);
                const isLast = end >= file.size;
                try {
                    answer = await sendChunk(baseOf(id) + '&offset=' + offset + (isLast ? '&last' : ''), file.slice(offset, end), texts, signal);
                } catch (e) {
                    // Going on with one the server no longer has: from the start, as a new one
                    if (e.status === 409 && options.resume && offset > 0) {
                        options.resume = null;
                        id = await startUpload(directory, texts, signal);
                        if (options.onStarted)
                            options.onStarted(id);
                        onBytes(-offset, 0);
                        offset = 0;
                        continue;
                    }
                    throw e;
                }
                onBytes(end - offset, end);
                offset = end;
            } while (offset < file.size);
            return answer;
        } catch (e) {
            // What was sent is kept to go on with, unless it can't be (refused) or isn't wanted (cancelled)
            if ((signal && signal.aborted) || (e.status >= 400 && e.status < 500))
                abort(directory, id);
            throw e;
        }
    }

    function abort(directory, id) {
        fetch(directory + '?upload=' + id + '&abort', { method: 'POST', headers: { 'RequestVerificationToken': token() } }).catch(() => { });
    }

    window.uploadChunks = { uploadFile, abort };
})();
