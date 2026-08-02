# Working with Files

Input data in CalcpadCE can be saved to disk and reused multiple times.
The supported file formats are "**\*.txt**", "**\*.cpd**" and "**\*.cpdz**". Input forms have to be saved to "**\*.cpd**" and "**\*.cpdz**" files and text scripts to "**\*.txt**" files.
Both "**\*.cpd**" and "**\*.cpdz**" file types are associated with CalcpadCE and can be opened with double click.
The main difference between the two formats is that "**\*.cpd**" is a text file and can be edited while "\***.cpdz**" is binary and can be only executed.
The source code inside is protected from viewing, copying and modification.

## New

You can start a new file by clicking the <img src="./media/image42.png" alt="" height="20"> button.
This will clear the file name and the source code.
If the current file is not saved, you will be prompted to do that.

<img src="./media/image43.png" style="width:3.27639in;height:1.66458in" alt="PromptSave" />

If you answer "**Yes**", the "**File Save**" dialog will appear.
Enter file name and click "**Save**". Thus, you will preserve your data before being cleared.
If you select "**Cancel**" you will interrupt the command and everything will remain unchanged.

## Open

You can open an existing file with the <img src="./media/image44.png" alt="" height="20"> button.
A file selection dialog will appear.
The active file extension is "\*.cpd", by default.
If you search for "\*.txt" or "\*.cpdz" files, select the corresponding type at the bottom of the dialog.
Then find the required file and press "**Open**" or double click on the file.
It will be loaded into CalcpadCE and the file name will be displayed in the title bar.

## Save

You can save the current file by clicking the <img src="./media/image9.png" alt="" height="20"> button.
If the file has not been saved so far, you will be prompted to select path and name.
Otherwise, it will be rewritten at the current location.

## Save As…

If you need to save the current file with a different name, select the "**File/Save As…**" menu command.
A file selection dialog will be displayed.
Select file path and name and click "**Save**"

## Save As Compiled Worksheet…

Compiling produces a "**\*.cpdz**" from the document you are working on.
It is a separate output rather than a rename: the file you have open keeps its own name and stays editable, so you can keep working on the "**\*.cpd**" and re-compile whenever you need a new copy to hand out.

A compiled worksheet is fully portable: everything the document depends on is written into it, so it runs with nothing beside it.

* "**#include**"d files are expanded in place, and macros defined with "**#def**" are applied — the compiled file has neither.
* Every "**#read**" is replaced by the data it imports. `#read M from table.csv` becomes the assignment `M = [1; 3|2; 4]`, hidden so it does not appear in the report, and the matrix keeps the shape the directive asked for (`type=C`, `type=S` and the rest). A read declared as high performance ("**type=R_hp**") becomes an ordinary matrix.
* Images referenced by a relative path are embedded as data, including those referenced by an included file.
* A `<project>`/`<library>` reference (see [Path root tokens](new-includes.md#path-root-tokens-project-and-library)) is always resolved to your own local path, for every reference kind — a compiled worksheet's source is locked, so there is no way for whoever opens it to add a `#ProjectPath`/`#LibraryPath` of their own.

If a referenced file cannot be read — a missing "**\*.csv**", an "**#include**" that does not resolve — compiling stops and reports it, rather than writing a worksheet that fails for whoever receives it. An unsaved document has no folder for relative paths to resolve against, so save it before compiling.

"**#write**" and "**#append**" are outputs, not dependencies, so a relative target is left as it is and still writes next to the compiled file when it runs. An absolute target, however, points at a folder that may not exist on whoever runs the compiled file — so with the "**Write outputs next to the worksheet**" checkbox on the "**Export**" tab checked (the default), it is rewritten to its bare filename instead, landing beside the compiled file the same way a relative one does. Clear the checkbox to keep it exactly as written.

* In **calcpad-desktop**, use "**File/Save As Compiled Worksheet…**", or the "**Save Compiled…**" button on the "**Export**" tab.
* In **VS Code**, run "**CalcpadCE: Save As Compiled Worksheet…**" from the command palette, or use the same "**Save Compiled…**" button on the "**Export**" tab.

Opening a compiled worksheet gives you the input form with the source locked — that is what the format is for.
Values you enter can still be saved back into it: in **calcpad-desktop** it saves like any other file, and in **VS Code** a compiled worksheet opens in its own editor where "**Save**" writes the entered values back.

A filled-in worksheet is still read and handed on, so the report and the exports work from a compiled file as they do from a "**\*.cpd**".
In **VS Code**, "**CalcpadCE: Toggle Report Preview**" opens the report beside the form, and the "**Export**" tab's PDF, HTML and Word buttons render the values you have entered.
The source stays hidden throughout — the report is a rendering, not the code.

If the recipient has to read or edit the calculation rather than just fill it in, export a portable package instead.

## Export Portable Package…

A portable package is the middle ground between a "**\*.cpd**", which only runs on the machine it was written on, and a "**\*.cpdz**", which runs anywhere but cannot be read.
It is a "**\*.zip**" holding the document as text beside a folder of everything it references, with each path rewritten to reach it there:

```
calc.zip
    calc.cpd
    calc.cpd.refs/
        logo.png
        library.cpd
        loads.csv
```

Unzip it anywhere and open the "**\*.cpd**": it renders as it did for its author, and it is still a document — readable, editable, and re-exportable.

* "**#include**" stays an "**#include**", "**#read**" stays a "**#read**" and images stay images. Only their paths change.
* An "**#include**"d file is packed with its own references, which are rewritten as well.
* Images given as a web address or as inline data are left alone: they already resolve anywhere.
* "**#write**" and "**#append**" are outputs, not dependencies, so a relative target is left alone for the same reason as when compiling. An absolute target is rewritten to its bare filename when "**Write outputs next to the worksheet**" on the "**Export**" tab is checked (the default), so the output lands beside wherever the package is unpacked; clear it to keep the target exactly as written.
* A `<project>`/`<library>` reference (see [Path root tokens](new-includes.md#path-root-tokens-project-and-library)) is left exactly as written by default, for the recipient's own `#ProjectPath`/`#LibraryPath` to resolve — useful for a shared library file that shouldn't be duplicated into every package. The **Export** tab has a checkbox for each root, off by default, to bundle it instead: resolved to your own local path and packed like any other absolute reference. The tab also shows the document's declared paths, read-only.

The folder is flat, so if two referenced files share a name — however different their folders are, and however deep in the "**#include**"s they sit — the second and any further one are renamed "**name-1.ext**", "**name-2.ext**" and so on, and every path that pointed at them is rewritten to match.
A reference that cannot be read stops the export, and lists every one.
Two "**#write**"/"**#append**" targets that would collapse onto the same filename once rewritten next to the worksheet stop it too, naming both — rename one, or clear the checkbox.
An unsaved document has no folder for relative paths to resolve against, so save it first.

* In **calcpad-desktop**, use "**File/Export Portable Package…**", or the "**Export Portable…**" button on the "**Export**" tab.
* In **VS Code**, run "**CalcpadCE: Export Portable Package…**" from the command palette or the editor's right-click menu, or use the same "**Export Portable…**" button on the "**Export**" tab.

One thing to know when reading the packaged files: a path inside an "**#include**"d file is written relative to the *document*, not to the included file, because "**#include**"s are expanded into the document before anything resolves.
That is where the original resolved it from too.
