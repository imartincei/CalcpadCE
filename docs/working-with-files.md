# Working with Files

Input data in CalcpadCE can be saved to disk and reused multiple times.
The supported file formats are:

| Format | Contents | Editable |
| ------ | -------- | -------- |
| **\*.cpd** | A CalcpadCE file, saved as plain text | Yes |
| **\*.cpdz** | A [compiled worksheet](new-exports.md#save-as-compiled-worksheet) with everything it depends on built in | No, only the input values |

Because a **\*.cpd** file is plain text, it can also be compared and tracked with version control tools.
Values entered into an [input form](new-ui-mode.md) and other document data are stored in a [metadata footer](new-metadata-comments.md#file-metadata-footer) at the end of the file, which the editor hides by default.

A **\*.cpdz** file opens as its input form, and the code inside the CalcpadCE file cannot be modified.
Values entered into it can still be saved back into the file.
It is created from a **\*.cpd** file with **Save Compiled…** on the **Export** tab, so you keep working on the editable original.

To hand a document to someone who needs to read or edit it, along with every file it includes, reads or shows, export a [portable package](new-exports.md#export-portable-package) instead.

## New, Open and Save

Files are created, opened and saved the same way as in other programs, with **Ctrl+N**, **Ctrl+O** and **Ctrl+S**.
If you close a file with unsaved changes, you will be prompted to save it first.

- **Desktop app** - several files can be open at once in tabs, and the **Files** tab of the side panel browses a folder.
  See [Desktop App → Working with tabs](new-desktop-app.md#working-with-tabs) and [Opening files](new-desktop-app.md#opening-files).
- **VS Code** - files are opened and saved as any other file in VS Code.
  A file with a **\*.cpd** extension is recognized as a CalcpadCE file automatically.
  See [VS Code Extension → Your first document](new-vscode-extension.md#your-first-document).

## Other Files

A CalcpadCE file can use other files:

- `#include` inserts another CalcpadCE file, so shared constants, functions and macros can be kept in one place.
- `#read`, `#write` and `#append` read and write matrix data in text, CSV and Excel files.
- Images are shown with an HTML `<img>` tag.

Their paths can be relative to the CalcpadCE file, or start with `{project}`, `{library}` or `{user}`.
See [Includes and File Reads](new-includes.md) and [Programming → Import/Export of External Data](programming.md#importexport-of-external-data).
