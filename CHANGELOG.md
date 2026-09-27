# Changelog

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
