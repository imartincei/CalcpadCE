# Portable Export Options

Beyond saving a document as "**\*.cpd**", CalcpadCE can produce two self-contained outputs meant to be handed to someone else: a compiled worksheet, which runs anywhere but keeps its source locked, and a portable package, which stays readable and editable.

## Save As Compiled Worksheet…

Compiling produces a "**\*.cpdz**" from the document you are working on.
It is a separate output rather than a rename: the file you have open keeps its own name and stays editable, so you can keep working on the "**\*.cpd**" and re-compile whenever you need a new copy to hand out.

A compiled worksheet is fully portable: everything the document depends on is written into it, so it runs with nothing beside it.

* "**#include**"d files are expanded in place, and macros defined with "**#def**" are applied — the compiled file has neither.
* Every "**#read**" is given the file it names to carry, in place of the path: `#read M from table.csv` becomes `#read M from data:text/csv;base64,MSwzCjIsNAo=`. It stays a read, so everything else about it stays too — the type, the separator, the sheet and the range are untouched, and the compiled worksheet reads them exactly as the original did. An Excel workbook is carried the same way, as its own bytes. What a compiled worksheet may carry is capped at 10 MB, per file and in total, since it is all held in memory to be run; past that the compile stops and says so.
* Images referenced by a relative path are embedded as data, including those referenced by an included file.
* A `{project}`/`{library}` reference (see [Path root tokens](new-includes.md#path-root-tokens-project-and-library)) is resolved to your own local path, for every reference kind — a compiled worksheet's source is locked, so there is no way for whoever opens it to add a `#ProjectPath`/`#LibraryPath` of their own.

If a referenced file cannot be read — a missing "**\*.csv**", an "**#include**" that does not resolve — compiling stops and reports it, rather than writing a worksheet that fails for whoever receives it. An unsaved document has no folder for relative paths to resolve against, so save it before compiling.

"**#write**" and "**#append**" are outputs, not dependencies, so a relative target is left as it is and still writes next to the compiled file when it runs. An absolute target, however, points at a folder that may not exist on whoever runs the compiled file — so it is rewritten to its bare filename, landing beside the compiled file the same way a relative one does.

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
* "**#write**" and "**#append**" are outputs, not dependencies, so a relative target is left alone for the same reason as when compiling. An absolute target is rewritten to its bare filename, so the output lands beside wherever the package is unpacked.
* A `{project}`, `{library}` or `{user}` reference (see [Path root tokens](new-includes.md#path-root-tokens-project-and-library)) is resolved against your own declared roots and packed like any other reference. The token names a folder there is no reason to expect the recipient has, so a package that still depended on one would not be portable — which means the root has to be declared, and its folder has to exist, or the export is refused naming the directive. If what you actually want is for the recipient to resolve a shared library from *their* own folders, hand them the "**\*.cpd**" itself and have them point `#ProjectPath`/`#LibraryPath` at it; that is a document to be shared, not a package to be unpacked.

The folder is flat, so if two referenced files share a name — however different their folders are, and however deep in the "**#include**"s they sit — the second and any further one are renamed "**name-1.ext**", "**name-2.ext**" and so on, and every path that pointed at them is rewritten to match.
A reference that cannot be read stops the export, and lists every one.
Two "**#write**"/"**#append**" targets that would collapse onto the same filename once rewritten next to the worksheet stop it too, naming both — rename one.
An unsaved document has no folder for relative paths to resolve against, so save it first.

* In **calcpad-desktop**, use "**File/Export Portable Package…**", or the "**Export Portable…**" button on the "**Export**" tab.
* In **VS Code**, run "**CalcpadCE: Export Portable Package…**" from the command palette or the editor's right-click menu, or use the same "**Export Portable…**" button on the "**Export**" tab.

One thing to know when reading the packaged files: a path inside an "**#include**"d file is written relative to the *document*, not to the included file, because "**#include**"s are expanded into the document before anything resolves.
That is where the original resolved it from too.
