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

If a referenced file cannot be read — a missing "**\*.csv**", an "**#include**" that does not resolve — compiling stops and reports it, rather than writing a worksheet that fails for whoever receives it. An unsaved document has no folder for relative paths to resolve against, so save it before compiling.

"**#write**" and "**#append**" are left as they are: they are outputs, not dependencies, and still write next to the compiled file when it runs.

* In **calcpad-desktop**, use "**File/Save As Compiled Worksheet…**", or the "**Save Compiled…**" button on the "**Export**" tab.
* In **VS Code**, run "**CalcpadCE: Save As Compiled Worksheet…**" from the command palette, or use the same "**Save Compiled…**" button on the "**Export**" tab.

Opening a compiled worksheet gives you the input form with the source locked — that is what the format is for.
Values you enter can still be saved back into it: in **calcpad-desktop** it saves like any other file, and in **VS Code** a compiled worksheet opens in its own editor where "**Save**" writes the entered values back.

A filled-in worksheet is still read and handed on, so the report and the exports work from a compiled file as they do from a "**\*.cpd**".
In **VS Code**, "**CalcpadCE: Toggle Report Preview**" opens the report beside the form, and the "**Export**" tab's PDF, HTML and Word buttons render the values you have entered.
The source stays hidden throughout — the report is a rendering, not the code.
