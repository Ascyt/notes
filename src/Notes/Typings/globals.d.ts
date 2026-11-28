// What the scripts of the components share. They are plain scripts, not modules: the pages load them one by one, and what one offers
// the others is a property of window (`window.notify = …` in _Toasts.ts, `notify(…)` where it is used). Each of them is declared once here,
// with its type, so that both ways of writing it are checked.

// ---- Types the server and the scripts agree on ----

type Theme = 'light' | 'dark';

/** The texts of a component (see textsOf): every public string property of its texts class, by its name in camelCase. */
type Texts = Record<string, string>;

/** What long work is doing (WorkStatus of the server, see describeProgress). */
interface WorkStatus {
    phase: string;
    files: number;
    totalFiles: number;
    bytes: number;
    totalBytes: number;
    waitingFor?: string | null;
}

/** Shows how far work is (0 to 1; null for no bar) and, as the server tells it, what it is doing. */
type SetProgress = (fraction: number | null, status?: WorkStatus | null) => void;

interface BusyOptions {
    /** The page is covered until the work is done. */
    blockPage?: boolean;
    /** What is going on, shown on the cover. */
    message?: string;
}

/** A message that is shown by the page that comes after this one (see notifyAfterNavigation). */
interface QueuedNotification {
    message: string;
    kind?: string;
}

/** A place in a text, as the editors count it: both from 1. */
interface TextPlace {
    line: number;
    column: number;
}

/** Where an editor starts: a place, and the rendered text before it where the address names a character inside a block. */
interface StartPlace extends TextPlace {
    before?: string;
}

/** What Enter does in a list (see markdown-lists.ts): put this text in place of the line break, or end the list. */
type EnterAction = { endsList: true } | { endsList?: false; text: string };

// ---- Uploads ----

/** A file of an upload, as it is kept in the browser's storage (see upload-operation.ts). */
interface StoredUploadFile {
    name: string;
    size: number;
    lastModified: number;
    unzip: boolean;
    /** 'skipped' is a file that was there already. */
    state: 'todo' | 'done' | 'skipped';
    /** What the server has of it: the id of the upload and how far it got. */
    id: string | null;
    offset: number;
}

interface StoredUpload {
    directory: string;
    files: StoredUploadFile[];
    /** When a tab last said that it works on it (where there are no browser locks). */
    beat?: number;
}

interface UploadFileOptions {
    directory: string;
    name?: string;
    folder?: string;
    chunkSize: number;
    texts: Texts;
    onBytes?: (bytes: number, offset: number) => void;
    unzip?: boolean;
    signal?: AbortSignal;
    resume?: { id: string; offset: number } | null;
    onStarted?: (id: string) => void;
}

interface UploadChunks {
    uploadFile(file: File, options: UploadFileOptions): Promise<string>;
    abort(directory: string, id: string): void;
}

interface UploadOperation {
    load(): StoredUpload | null;
    save(operation: StoredUpload): void;
    clear(): void;
    hold(current: () => StoredUpload): () => void;
    takeResume(): string | null;
    discard(operation: StoredUpload): void;
    isFinished(operation: StoredUpload): boolean;
}

// ---- Archives and passwords ----

/** What the archive dialog is opened for (see _ArchiveModal.ts). */
type ArchiveRequest = { kind: 'directory' } | { kind: 'selection'; version: number };

interface ArchiveDownload {
    open(what: ArchiveRequest): Promise<void>;
}

interface ArchiveUnzipping {
    /** Asks until the archived folder is unzipped: null once it is, otherwise what went wrong. */
    waitUntilUnzipped(address: string, setProgress: (fraction: number, status?: WorkStatus | null) => void): Promise<string | null>;
}

interface PasswordPrompt {
    /** Asks for the password of the entry; resolves to whether it was unlocked. */
    ask(address: string): Promise<boolean>;
}

// ---- Editors ----

interface EditorZoom {
    readonly factor: number;
    readonly canZoomIn: boolean;
    readonly canZoomOut: boolean;
    zoomIn(): void;
    zoomOut(): void;
    reset(): void;
    onChange(listener: (factor: number) => void): void;
    bindZoomKeys(element: HTMLElement): void;
}

interface CreateEditorOptions {
    basePath: string;
    value: string;
    language: string;
    uri?: string;
    readOnly?: boolean;
    readOnlyMessage?: string;
    suggestsWords?: boolean;
    onBeforeCreate?: (monaco: MonacoApi) => void;
}

interface CreateDiffEditorOptions {
    basePath: string;
    original: string;
    modified: string;
    language: string;
    readOnlyMessage?: string;
}

interface TextAreaIndent {
    indent(field: HTMLTextAreaElement): void;
    outdent(field: HTMLTextAreaElement): void;
}

interface MarkdownLists {
    onEnter(before: string, after: string): EnterAction | null;
    bindToTextArea(field: HTMLTextAreaElement): void;
    bindToMonaco(editor: MonacoCodeEditor): void;
}

interface EditorStartPosition {
    read(): StartPlace | null;
    goToInMonaco(editor: MonacoCodeEditor, position: StartPlace): void;
    goToInTextArea(field: HTMLTextAreaElement, position: StartPlace): void;
    linkToPreview(link: Element, place: TextPlace): void;
    placeInTextArea(field: HTMLTextAreaElement): TextPlace;
}

// ---- What the scripts offer each other ----

// site.ts
declare var setButtonDisabled: (button: HTMLElement, reason: string | null | undefined) => void;
declare var setButtonBusy: (button: HTMLElement, busy: boolean) => void;
declare var whileBusy: <T>(work: (setProgress: SetProgress) => Promise<T> | T, options?: BusyOptions) => Promise<T>;
declare var isBusy: () => boolean;
declare var isAppAddress: (pathname: string) => boolean;
declare var onClickOf: (id: string, handler: () => void) => void;
declare var textsOf: (id: string) => Texts;
declare var fillIn: (text: string, values: Record<string, string | number>) => string;
declare var showPending: (bar: HTMLElement, status: WorkStatus | null | undefined) => boolean;
declare var formatSize: (bytes: number) => string;
declare var describeProgress: (status: WorkStatus | null | undefined, fraction?: number | null) => string;

// theme.ts
declare var getTheme: () => Theme;
declare var showTheme: (theme: Theme) => void;
declare var toggleTheme: () => void;

// _Toasts.ts
declare var notify: (message: string, kind?: string) => void;
declare var notifyAfterNavigation: (message: string, kind?: string) => void;

// editor.ts
declare var editorZoom: EditorZoom;
declare var createEditor: (element: HTMLElement, options: CreateEditorOptions) => Promise<MonacoCodeEditor>;
declare var createDiffEditor: (element: HTMLElement, options: CreateDiffEditorOptions) => Promise<MonacoDiffEditor>;
declare var textAreaIndent: TextAreaIndent;
declare var markdownLists: MarkdownLists;
declare var editorStartPosition: EditorStartPosition;

// The editor of the page (FileView.ts, ConfigEditorView.ts), which the tests look at too, and the one of the changes of a commit
declare var editor: MonacoCodeEditor | undefined;
declare var changesEditor: MonacoCodeEditor | MonacoDiffEditor | undefined;

// Pages of a file (FileView.ts, BinaryView.ts)
declare var saveFile: () => Promise<void>;
declare var toggleSimpleView: () => void;
declare var indentSimpleView: (outdent: boolean) => void;
declare var toggleMediaFullscreen: () => void;
/** Asked by the rename (_RenameInput.ts) before it goes on: false stops it. */
declare var beforeRename: (() => boolean | undefined) | undefined;

// Markdown (markdown.ts, markdown-print.ts)
declare var markdownDiagramsRendered: Promise<void> | undefined;
declare var printMarkdown: () => Promise<void>;

// The history (_HistoryModal.ts, _CommitSidebar.ts)
declare var openHistoryModal: () => void;
declare var toggleCommitSidebar: () => void;

// shortcuts.ts: whether the key event is one of the element's shortcuts
declare var matchesShortcutOf: (el: Element, e: KeyboardEvent) => boolean;

// Archives and passwords
declare var archiveDownload: ArchiveDownload;
declare var archiveUnzipping: ArchiveUnzipping;
declare var passwordPrompt: PasswordPrompt;

// Uploads
declare var uploadChunks: UploadChunks;
declare var uploadOperation: UploadOperation | undefined;

// ---- Libraries that are loaded by the page, and not built here ----

interface BootstrapModal {
    show(): void;
    hide(): void;
}

declare var bootstrap: {
    Modal: { getOrCreateInstance(element: Element): BootstrapModal };
    Tooltip: new (element: Element, options?: { trigger?: string }) => unknown;
};

/** KaTeX, loaded only by a page that has math. */
declare var katex: {
    render(source: string, element: HTMLElement, options?: { displayMode?: boolean; throwOnError?: boolean }): void;
} | undefined;

/** Mermaid, loaded only by a page that has diagrams. */
declare var mermaid: {
    initialize(options: { startOnLoad?: boolean; securityLevel?: string; theme?: string }): void;
    render(id: string, source: string): Promise<{ svg: string }>;
} | undefined;
