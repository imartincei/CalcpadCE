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

Any images the document references by a relative path are embedded into the compiled file, so it can be distributed on its own without the surrounding folder.

* In **calcpad-desktop**, use "**File/Save As Compiled Worksheet…**", or the "**Save Compiled…**" button on the "**Export**" tab.
* In **VS Code**, run "**CalcpadCE: Save As Compiled Worksheet…**" from the command palette, or use the same "**Save Compiled…**" button on the "**Export**" tab.

Opening a compiled worksheet gives you the input form with the source locked — that is what the format is for.
Values you enter can still be saved back into it: in **calcpad-desktop** it saves like any other file, and in **VS Code** a compiled worksheet opens in its own editor where "**Save**" writes the entered values back.
