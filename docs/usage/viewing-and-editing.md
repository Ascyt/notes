# Viewing and editing files

What a file shows depends on its kind.

## Markdown

A Markdown file (`.md`) opens rendered: headings, lists, tables, task lists, code blocks, quotes, links, images and footnotes. HTML written into a note is shown as text, never run. A line break in the text is a line break in the rendered view.

Besides what Markdown always has:

| Written as | Shows |
|---|---|
| ` ```csharp ` … ` ``` ` | Code, highlighted for its language (most common languages; others stay plain) |
| `$a^2$`, and `$$` on the lines before and after a formula | Math in LaTeX, inline or as a block of its own |
| ` ```mermaid ` … ` ``` ` | A [mermaid](https://mermaid.js.org) diagram |
| `> [!NOTE]` (also `TIP`, `IMPORTANT`, `WARNING`, `CAUTION`) at the start of a quote | An alert, colored by its kind |
| `:::warning` … `:::` | A box around what is in it; `note`, `info`, `tip`, `important`, `warning`, `caution` and `danger` are colored |
| `^^^` before an image, `^^^ Caption` after it | The image with a caption |
| `^^ Text` | A footer, in small print |
| `""Title""` | A citation |
| `*[W3C]: World Wide Web Consortium` on a line of its own | Every "W3C" in the note marked as an abbreviation; hovering, tapping or focusing it shows what it stands for |

A link to another site opens in a new tab; a link to the notes stays in the same one.

| Desktop, light | Phone, dark |
|---|---|
| ![A rendered Markdown file](../screenshots/markdown-preview-desktop.png) | ![A rendered Markdown file on a phone](../screenshots/markdown-preview-phone.png) |

The pencil icon (or `e`) opens the editor, the book icon (or `p`) goes back to the rendered view. Without permission to change the file, there is only the rendered view.

The PDF icon (or Ctrl+P) saves the rendered file as a PDF: it opens the browser's print dialog, where "Save as PDF" is chosen as the printer. The PDF has only the text, without the page around it, always in the light theme, with the name of the file and the date at the top of every page and the page number at the bottom; the browser suggests the name of the file as the name of the PDF. Browsers that can't replace their own header and footer with these (Firefox, for instance) print theirs at the edge of the paper instead; a message then says that unchecking "Print headers and footers" in the print dialog gives the best result. Math and diagrams are in it as they are shown, and its text can be selected and searched.

Pointing anywhere on the line of a part of the rendered text (a paragraph, a list item, a heading, a code block, …), tapping it on a phone, or moving the focus into it shows a small pencil to its left. It opens the editor with the cursor where that part is written, scrolled to the middle of the editor.

## The editors

Every text file can be edited in the browser. There are two editors, and the choice between them is kept in the browser:

- The **code editor** (Monaco, the editor of Visual Studio Code), with highlighting, search and everything else of a code editor.
- The **simple editor**, a plain text field, which works better on phones.

| Code editor | Simple editor, on a phone |
|---|---|
| ![The code editor](../screenshots/editor-desktop.png) | ![The simple editor on a phone](../screenshots/simple-editor-phone.png) |

The phone icon in the toolbar switches to the simple editor, the keyboard icon back to the code editor. The floppy disk icon (or Ctrl+S) saves; it is only blue while there is something to save. Leaving the page with unsaved changes asks first.

The magnifying glass icons (or Alt+Plus and Alt+Minus) make the text of the editor larger and smaller; inside the editor, Ctrl+Plus, Ctrl+Minus and Ctrl+scrolling do the same, and Ctrl+0 goes back to the usual size. Both editors share the zoom, and it is kept in the browser.

A file that may be read but not changed opens in the editor as read-only, and the Save icon says why it can't be used.

## Images, video, audio and PDF

Images, videos, audio files and PDFs are shown on the file's page by the browser. The image icon shows the file on its own, as the browser would (`?raw`).

![An image](../screenshots/image-desktop.png)

Other files that aren't text can't be shown, and only be downloaded.

## Downloading

The download icon (or Alt+PageDown) downloads the file. For a directory it downloads a zip of everything in it that may be read.
