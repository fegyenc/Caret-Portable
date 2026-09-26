# Caret Portable

Convert Word, Excel, PowerPoint, PDF, Outlook and other files to Markdown with **Microsoft MarkItDown**, one file at a time or a whole folder at once, and read or edit the result in a Markdown editor.

**Nothing to install.** Unzip the folder anywhere you can write to (your Documents, Desktop or a USB drive) and double-click **Caret.exe**. You don't need administrator rights, Caret doesn't add itself to PATH, and it doesn't touch the registry.

## What you need

| | |
| --- | --- |
| **Windows** | Windows 10 (version 1809 or later) or Windows 11, 64-bit. |
| **WebView2** | Built into Windows 11, and installed on almost every managed Windows 10 PC with Microsoft Edge. |
| **Python 3.10 or later** | Installed for your account. The [python.org installer](https://www.python.org/downloads/windows/) works without administrator rights ("Install for me only"). You **don't** need to tick "Add python.exe to PATH". |
| **MarkItDown** | Installed into that Python. Caret can do this for you (see below). |

## First run

1. Open **Convert to Markdown** in the sidebar.
2. The **Converter** panel shows what Caret found:
   - **"Microsoft MarkItDown 0.1.x in Python 3.x"**: you're ready.
   - **"Python … is here, but MarkItDown isn't installed in it yet"**: click **Install MarkItDown**. Caret runs `pip install --user` for your account only, which needs access to pypi.org.
   - **"No Python found"**: install Python for your account, or click **Choose python.exe…** if it's somewhere Caret doesn't look.

Caret finds Python without PATH. It looks in:

- a `python` folder next to `Caret.exe` (see *Sharing with a team* below)
- the Python launcher (`py.exe`)
- `%LOCALAPPDATA%\Programs\Python\Python3xx`
- `C:\Program Files\Python3xx` and `C:\Python3xx`
- Anaconda or Miniconda in your user folder
- Microsoft Store Python
- PATH, as a last resort

Your choice from **Choose python.exe…** is remembered.

If your company blocks pip, install MarkItDown yourself (or ask IT to) with:

```
python -m pip install --user "markitdown[docx,pptx,xlsx,xls,pdf,outlook]"
```

Then click **Detect again**.

## Converting

- **Choose files**: one file, or several at once.
- **Choose folder**: every supported file in the folder **and its subfolders**.
- **Drag and drop** files or folders onto the page.
- **Save to**: *next to the original files*, or *a folder you choose*. With a chosen folder, the subfolder structure of a converted folder is kept.
- **Existing files are never overwritten**: if `report.md` already exists, the new one is `report (2).md`.
- **Copy all for AI** puts every converted file on the clipboard as one text, ready to paste into an assistant.
- **File → Import Document as Markdown…** converts one file and opens it straight in the editor.

Supported formats: Word (.docx), Excel (.xlsx, .xls), PowerPoint (.pptx), PDF, CSV, Outlook (.msg), HTML, EPUB, Jupyter (.ipynb), JSON, XML, RSS, plain text and ZIP archives of those.

Older .doc and .ppt files need to be saved as .docx or .pptx first. Scanned PDFs without a text layer have no text to extract.

## Where things are kept

Everything Caret remembers is in the **`Data` folder next to `Caret.exe`**: settings, recent files, favorites, templates, crash-recovery copies and the editor's browser cache. To move Caret, move the whole folder. To remove it, delete the folder.

If the folder is read-only (for example on a network share), Caret uses `%LOCALAPPDATA%\Caret Portable` instead.

## Sharing with a team

To give colleagues everything in one folder, put a Python with MarkItDown already installed into a folder called `python` next to `Caret.exe`. Caret uses it before anything else, so nobody has to install Python.

The [Windows embeddable package](https://www.python.org/downloads/windows/) is one way to do this. After unzipping it:

1. In `python3xx._pth`, uncomment `import site`.
2. Install pip with `get-pip.py`.
3. Run `python -m pip install "markitdown[docx,pptx,xlsx,xls,pdf,outlook]"`.

## If Caret won't start

- **Nothing happens, or "This app has been blocked by your system administrator".** Your company's application control (AppLocker or Windows Defender Application Control) doesn't allow programs to run from your user folders. Ask IT to allow `Caret.exe`, or to put the folder somewhere that's allowed.
- **"WebView2 Runtime" message.** Ask IT to install the Microsoft Edge WebView2 Runtime (Evergreen). It's a standard Microsoft component.

A diagnostic log is written to `%TEMP%\caret_winui_probe.log`.

## Privacy

Documents are converted on your PC by MarkItDown running in your own Python, and nothing is uploaded. Caret doesn't connect to anything on its own: no update check, no telemetry. The only download it can start is `pip install`, and only when you click **Install MarkItDown**.
