// The part of Monaco's API that the scripts use. Monaco itself is loaded by editor.ts (its AMD loader), when a page needs an editor, and
// is served from wwwroot/lib (LibMan). Declared here by hand, so that the build needs no package for it; what a script starts to use is added.

interface MonacoPosition {
    lineNumber: number;
    column: number;
}

interface MonacoRange {
    startLineNumber: number;
    startColumn: number;
    endLineNumber: number;
    endColumn: number;
}

interface MonacoSelection extends MonacoRange {
}

interface MonacoModel {
    getLineContent(lineNumber: number): string;
    getAlternativeVersionId(): number;
    getFullModelRange(): MonacoRange;
}

/** The options of an editor (the ones that are set here). */
interface MonacoEditorOptions {
    theme?: string;
    readOnly?: boolean;
    readOnlyMessage?: { value: string };
    automaticLayout?: boolean;
    wordWrap?: 'on' | 'off';
    fontSize?: number;
    wordBasedSuggestions?: 'off';
    quickSuggestions?: boolean;
    useInlineViewWhenSpaceIsLimited?: boolean;
    originalEditable?: boolean;
}

/** What an editor is made with: its options, and the text it starts with (or a model that has it). */
interface MonacoConstructionOptions extends MonacoEditorOptions {
    value?: string;
    language?: string;
    model?: MonacoModel;
}

interface MonacoEdit {
    range: MonacoRange;
    text: string;
    forceMoveMarkers?: boolean;
}

interface MonacoCodeEditor {
    addCommand(keybinding: number, handler: () => void, context?: string): string | null;
    getOption(option: number): unknown;
    updateOptions(options: MonacoEditorOptions): void;
    getValue(): string;
    setValue(value: string): void;
    focus(): void;
    hasTextFocus(): boolean;
    layout(): void;
    getPosition(): MonacoPosition | null;
    setPosition(position: MonacoPosition): void;
    revealPosition(position: MonacoPosition): void;
    revealPositionInCenter(position: MonacoPosition): void;
    revealPositionInCenterIfOutsideViewport(position: MonacoPosition): void;
    getSelection(): MonacoSelection | null;
    getSelections(): MonacoSelection[] | null;
    getModel(): MonacoModel | null;
    trigger(source: string, handlerId: string, payload: unknown): void;
    pushUndoStop(): boolean;
    executeEdits(source: string, edits: MonacoEdit[], endCursorState?: MonacoSelection[]): boolean;
    onDidChangeModelContent(listener: () => void): unknown;
    getTargetAtClientPoint(clientX: number, clientY: number): { position: MonacoPosition | null } | null;
}

interface MonacoDiffEditor {
    setModel(model: { original: MonacoModel; modified: MonacoModel }): void;
    getOriginalEditor(): MonacoCodeEditor;
    getModifiedEditor(): MonacoCodeEditor;
    updateOptions(options: MonacoEditorOptions): void;
}

interface MonacoApi {
    KeyCode: { Escape: number; Enter: number; KeyZ: number };
    KeyMod: { Alt: number };
    Range: new (startLineNumber: number, startColumn: number, endLineNumber: number, endColumn: number) => MonacoRange;
    Selection: new (startLineNumber: number, startColumn: number, endLineNumber: number, endColumn: number) => MonacoSelection;
    Uri: { parse(value: string): unknown };
    editor: {
        EditorOption: { wordWrap: number };
        setTheme(theme: string): void;
        create(element: HTMLElement, options: MonacoConstructionOptions): MonacoCodeEditor;
        createDiffEditor(element: HTMLElement, options: MonacoEditorOptions): MonacoDiffEditor;
        createModel(value: string, language?: string, uri?: unknown): MonacoModel;
    };
    languages: {
        json: { jsonDefaults: { setDiagnosticsOptions(options: object): void } };
    };
}

declare var monaco: MonacoApi;

/** Monaco's AMD loader (loader.js). */
declare const require: {
    (modules: string[], onLoad: () => void, onError: (error: unknown) => void): void;
    config(options: { paths: Record<string, string> }): void;
};
