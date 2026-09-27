# Changelog

## 1.4.0

Brings Portable up to Caret 1.4 ([fegyenc/Caret#25](https://github.com/fegyenc/Caret/pull/25) to [#35](https://github.com/fegyenc/Caret/pull/35)).

- **Outlook emails to Markdown.** `.msg` and `.eml` become one clean thread, oldest first, without repeated quotes, signatures and disclaimers, with personal data masked by default (**Mask personal data** on the Convert page). It uses Caret's MarkItDown email plugin, shipped inside the folder: no extra install. Emails have their own place in the sidebar and on the start page.
- **Document tabs.** Several documents per window, `Ctrl+T`, `Ctrl+Tab`, `Ctrl+W` closes the document and keeps the window. Saved documents reopen at the next start.
- **Settings is a page** in the window, with search and categories (`Ctrl+,`).
- **Colour schemes** (Copper, Paper, Sage, Harbor, Graphite) in light and dark, checked for WCAG 2.2 AA contrast; optional Windows accent; colours per area of the window and per tab.
- **Layouts**: Classic, Streamlined and Distraction-free (`F11`); the sidebar on either side, or narrow; `F6` moves between areas; a start page with favourites and recent files.
- **New organization policies** for the default layout, colour scheme, accent and theme.
- Fixes from Caret: UTF-32 big-endian files, "Don't Save" no longer leaves a recovery copy, Find keeps its options, favourites follow renamed files, Explorer Cut pastes as a move.

## 1.0.1

- Markdown files that aren't saved as UTF-8 (for example output of `markitdown file.docx > file.md` in a Command Prompt) open with the right characters. The status bar shows the encoding, and saving converts the file to UTF-8. From Caret ([fegyenc/Caret#24](https://github.com/fegyenc/Caret/pull/24)).
- CSV, TXT, JSON, XML and HTML files are converted with the right encoding instead of MarkItDown's guess, so accented letters in files saved by Excel or older tools come out correctly.
- CSV files separated by semicolons (Excel on French, Polish or Hungarian PCs), tabs or pipes become proper tables.
- Tidier download: `Caret.exe`, `README.md`, `app\` and `Data\`. Unused language resources removed.
- The repository moved to `src/` with Caret names.

## 1.0.0

- First portable version, built from [Caret](https://github.com/fegyenc/Caret): runs from a folder, no installation or administrator rights.
- Every conversion runs through Microsoft MarkItDown in the user's own Python, found without PATH, with *Choose python.exe…* and *Install MarkItDown* (pip `--user`).
- Single files, several files or whole folders with subfolders; output next to the originals or in a chosen folder; nothing is overwritten.
- Settings and the editor cache stay in `Data\` next to `Caret.exe`.

The detailed history, including everything from Typedown and Caret, is in [docs/history.md](docs/history.md).
