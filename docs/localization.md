# Localization

Caret is available in **English**, **French** and **Spanish**. People choose their language in **Settings → Language**; *Use Windows display language* (the default) picks French or Spanish automatically on a French or Spanish Windows and falls back to English otherwise. A change applies the next time Caret starts.

## How it works

| Piece | Where |
| --- | --- |
| Caret's own text | `src/Caret.App/Strings/<lang>/AppResources.resw` |
| Text inherited from Typedown (mostly used by the editor: placeholders, footnote tool, tooltips) | `Strings/en/CommonResources.resw`, `DialogResources.resw`, `SettingsResources.resw`. French and Spanish versions of the ones the editor still uses live in `Strings/fr` and `Strings/es` `AppResources.resw`. |
| Loading | `Utilities/Locale.cs` reads the `.resw` files as XML at startup. English is always loaded underneath the chosen language, so a string that isn't translated yet shows in English instead of disappearing. |
| In XAML | `Text="{u:Loc Key=SaveMenuItem_Text}"` (`Utilities/LocExtension.cs`) |
| In code | `Locale.GetString("Untitled")`, or `Locale.Format("SaveChangesPrompt", name)` for text with `{0}` placeholders |

Rules that keep translations working:

- **Never build a sentence out of pieces.** Use one string with `{0}` placeholders, so each language can put the name where its grammar needs it (`"Voulez-vous enregistrer les modifications apportées à {0} ?"`).
- **Plurals are separate strings** (`WordCountOne` / `WordCountMany`). French treats 0 as singular; the code handles that.
- **Give an element its own key when the same English word means different things.** "View" is both a menu (*Affichage*, *Ver*) and an editing mode (*Visuel*, *Visual*).
- Keys and placeholders must match across languages; `Strings/en/AppResources.resw` is the reference.

## Adding a language

1. Copy `Strings/en/AppResources.resw` to `Strings/<code>/AppResources.resw` (two-letter code, e.g. `de`) and translate every `<value>`, keeping `{0}` and `{identifier}` placeholders exactly as they are.
2. Add the editor strings at the end of `fr/AppResources.resw` (from `InputFootnoteDefine` onward) to the new file too, translated.
3. Add the code to `Locale.SupportedLanguages` and a `ComboBoxItem` (language name written in that language) to `LanguageComboBox` in `MainWindow.xaml`, plus its tag to the index list in `LoadSettingsIntoDialog`.
4. Build: the `.resw` is picked up and copied automatically.

## Style

| | French | Spanish |
| --- | --- | --- |
| Address | *vous* | *tú* (Microsoft's current Spanish style) |
| Punctuation | non-breaking space before `: ; ? !` and inside `« »` | opening `¿ ¡`; quotes `« »` |
| Shift key | Maj (`Ctrl+Maj+S`) | Mayús (`Ctrl+Mayús+S`) |
| OK button | OK | Aceptar |
| Page sizes | centimetres | centimetres |
| Terminology source | Microsoft Windows / Office French | Microsoft Windows / Office Spanish (Spain) |

## Glossary

Terms that must stay consistent everywhere, including the Store listing and documentation.

| English | French | Spanish |
| --- | --- | --- |
| Note | Note | Nota |
| File / Folder | Fichier / Dossier | Archivo / Carpeta |
| Save / Save As | Enregistrer / Enregistrer sous | Guardar / Guardar como |
| Open | Ouvrir | Abrir |
| Settings | Paramètres | Configuración |
| Favorites | Favoris | Favoritos |
| Templates | Modèles | Plantillas |
| Trash / Recycle Bin | Corbeille | Papelera / Papelera de reciclaje |
| Heading 1 | Titre 1 | Título 1 |
| Bulleted / Numbered / Task list | Liste à puces / numérotée / de tâches | Lista con viñetas / numerada / de tareas |
| Code block | Bloc de code | Bloque de código |
| Table | Tableau | Tabla |
| Link | Lien | Vínculo |
| Alt text | Texte de remplacement | Texto alternativo |
| Find and Replace | Rechercher et remplacer | Buscar y reemplazar |
| View / Code / Split (editing modes) | Visuel / Code / Fractionné | Visual / Código / Dividido |
| Preview | Aperçu | Vista previa |
| Import / Export | Importer / Exporter | Importar / Exportar |
| Plain text | Texte brut | Texto sin formato |
| Update | Mise à jour | Actualización |
| Convert to Markdown | Convertir en Markdown | Convertir a Markdown |
| Token (AI) | Jeton | Token |
| Slide | Diapositive | Diapositiva |
| KB / MB | Ko / Mo | KB / MB |
| Markdown, MarkItDown, Caret | unchanged | unchanged |

Native-speaker review is welcome: open a pull request against the `.resw` file, or an issue quoting the key and the suggested wording.
