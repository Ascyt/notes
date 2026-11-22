// Sends one file to the server in chunks (see UploadHandler), for every script that uploads (_Upload.js, the media of the editor):
//
//   await uploadChunks.uploadFile(file, {
//       directory,   the address of the directory it goes into (e.g. location.pathname of its page)
//       name,        the name it is to get (by default its own)
//       folder,      folders inside the directory to put it into ("assets/clips"), made if they aren't there; optional
//       chunkSize,   bytes per request (from the server, see _Upload.ChunkSize)
//       texts,       the texts of UploadTexts (serverAnswered, networkError)
//       onBytes      optional, called with the number of bytes of each chunk that arrived
//   })                returns the virtual path the file got (with " (1)" added if the name was taken); throws an Error with the reason
(function () {
    const MAX_ATTEMPTS = 3;

    function token() {
        return document.querySelector('input[name="__RequestVerificationToken"]').value;
    }

    // Sends one chunk, retrying on network errors and server failures. Returns what the server answered.
    async function sendChunk(url, blob, texts) {
        let lastError;
        for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token(), 'Content-Type': 'application/octet-stream' },
                    body: blob
                });
                if (response.ok)
                    return await response.text();
                lastError = fillIn(texts.serverAnswered, { status: response.status });
                if (response.status >= 400 && response.status < 500) {
                    lastError = (await response.text()) || lastError; // e.g. a permission that is missing
                    break; // retrying won't help
                }
            } catch {
                lastError = texts.networkError;
            }
            await new Promise(resolve => setTimeout(resolve, 500 * attempt));
        }
        throw new Error(lastError);
    }

    // The server makes the id of an upload; it only works for the user who asked for it
    async function startUpload(directory, texts) {
        let response;
        try {
            response = await fetch(directory + '?upload=start', { method: 'POST', headers: { 'RequestVerificationToken': token() } });
        } catch {
            throw new Error(texts.networkError);
        }
        if (!response.ok)
            throw new Error((await response.text()) || fillIn(texts.serverAnswered, { status: response.status }));
        return await response.text();
    }

    async function uploadFile(file, options) {
        const { directory, chunkSize, texts } = options;
        const onBytes = options.onBytes || (() => { });
        const id = await startUpload(directory, texts);
        const base = directory + '?upload=' + id + '&name=' + encodeURIComponent(options.name || file.name)
            + (options.folder ? '&folder=' + encodeURIComponent(options.folder) : '');
        let offset = 0;
        try {
            // Do-while so that an empty file still sends its one (empty) chunk
            let answer;
            do {
                const end = Math.min(offset + chunkSize, file.size);
                const isLast = end >= file.size;
                answer = await sendChunk(base + '&offset=' + offset + (isLast ? '&last' : ''), file.slice(offset, end), texts);
                onBytes(end - offset);
                offset = end;
            } while (offset < file.size);
            return answer;
        } catch (e) {
            // Best effort: don't leave the partial file behind
            fetch(directory + '?upload=' + id + '&abort', { method: 'POST', headers: { 'RequestVerificationToken': token() } }).catch(() => { });
            throw e;
        }
    }

    window.uploadChunks = { uploadFile };
})();
