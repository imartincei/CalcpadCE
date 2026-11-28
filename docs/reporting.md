# Reporting

All calculations are automatically collected into a professionally formatted calculation report.
You can export it to PDF, Word or HTML from the **Export** tab (see [Exports](new-exports.md)).
Besides math expressions, you can add headings, comments, tables and images.

## Headings

A heading is a text, enclosed in double quotes (**"**).
It is bold and larger than the main text, and is rendered as a top-level `<h1>` heading.
Headings are listed in the **TOC** tab of the [CalcpadCE panel](new-calcpad-panel.md#toc), where you can click one to jump to it.

## Text/Comments

Comments are enclosed in single quotes (**'**).
You can skip the closing quote, if it is the last symbol in the line.
Headings and comments can contain any symbols without restrictions.
Everything outside them is assumed to be math expressions.
However, if you put any formulas inside comments, they will not be calculated or formatted.
Since the final output is rendered to an HTML document, you can use HTML and CSS in comments to provide your calculation report with additional formatting.
For longer HTML or Markdown content, you can also switch a whole block of lines out of calculation mode with `#html` or `#markdown`.
See [Parsing Modes](new-parsing-modes.md).

## Formatting with HTML and CSS

CalcpadCE can be used as a development platform for professional engineering programs.
If you are not going to do that, you can skip this chapter.

**HTML** (Hyper Text Markup Language) is a markup language which is created for formatting web pages.
You can change the font type, size and weight, the color of the text and to insert tables, images, etc.
This is performed by adding special elements called "tags".
Each tag is enclosed in angular brackets: "<tag\>".
Some tags are used in pairs - opening "<tag\>" and closing "</tag\>".
The contents is going in between.
For example, if you want to make some text bold, you can use the following tags: <b\>**Bold text**</b\>.
Even if you are not a professional programmer, you can easily learn some basic HTML, to use with CalcpadCE:

| HTML code | Output |
| --------- | ------ |
| `<h1>Heading 1</h1>` | <span style="font-size:1.4em;font-weight:bold;">Heading 1</span> |
| `<h2>Heading 2</h2>` | <span style="font-size:1.32em;font-weight:bold;">Heading 2</span> |
| `<h3>Heading 3</h3>` | <span style="font-size:1.24em;font-weight:bold;">Heading 3</span> |
| `<h4>Heading 4</h4>` | <span style="font-size:1.16em;font-weight:bold;">Heading 4</span> |
| `<h5>Heading 5</h5>` | <span style="font-size:1.08em;font-weight:bold;">Heading 5</span> |
| `<h6>Heading 6</h6>` | **Heading 6** |
| `<hr/>` (horizontal line) | **————————** |
| `<p>Paragraph</p>` | Paragraph |
| `Line<br/>break` | Line<br/>break |
| `<b>Bold</b>` | **Bold** |
| `<i>Italic</i>` | *Italic* |
| `<u>Underlined</u>` | <u>Underlined</u> |
| `<s>Struck through</s>` | <s>Struck through</s> |
| `<span style="color:red;">Red</span>` | <span style="color:red;">Red</span> |
| `x<sup>superscript</sup>` | $x^{superscript}$ |
| `x<sub>subscript</sub>` | $x_{subscript}$ |
| `<span style="font:14pt Times;">Times, 14pt</span>` | <span style="font:14pt Times;">Times, 14pt</span> |

You can put HTML tags only in comments, but you can also make them affect expressions.
For example:

```calcpad
'<span style="color:red;"> as simple as ' 2 + 2 '</span>'
```

will give the following output:

<span style="color:red;">as simple as $2 + 2 = 4$</span>

We simply enclosed the expression with two comments.
The first comment contains the opening tag '<span style="color:red;"\>' and the second - the closing tag '</span\>'.
Everything between the two tags is colored in red.
Make sure not to forget the quotes.
Otherwise, the program will try to parse the HTML code as math expression and will return an error.
The following code: style="color:red" is called "inline CSS" (Cascading Style Sheets).
It is used to format the look of HTML documents.
You can learn more about HTML and CSS from the following links:

<http://www.w3schools.com/html/>

<http://www.w3schools.com/CSS/>

You can also use some of the many free WYSIWYG HTML editors available on the Internet.

To style the whole report, put a `<style>` element in an `#html` block or in `'` comments under `#val`.
The **CSS** section of the **Insert** tab has snippets to start from, such as report fonts and table styles.

### Predefined Classes

Some formatting that is commonly used in engineering design worksheets is predefined as CSS classes and can be inserted by simply assigning the respective class to HTML elements.

**err** - adds red color to text:

`'<span class="err">The check is not satisfied ✖</span>`  
<span class="err">The check is not satisfied ✖</span>

**ok** - adds green color to text:

`'<span class="ok">The check is satisfied ✔</span>`  
<span class="ok">The check is satisfied ✔</span>

**ref** - (right aligned) it is used for references to design codes and equation numbering:

`'<span class="ref">[EN 1992-1-1, §9.2.2]</span>`  
<span class="ref">[EN 1992-1-1, §9.2.2]</span>

**bordered** - adds border to tables:

`'<table class="bordered">...</table>`

**data** - makes the first column left aligned and the others - right aligned:

`'<table class="data">...</table>`

### Content Folding

If you have some long and detailed calculations, you can fold them optionally in the output.
They will be hidden by default, except for the first line, which can be used for the section heading.
All you need to do is to enclose the folding section into an HTML "**div**" element with class "**fold**", as follows:

```html
'<div class="fold">
'<b>Heading</b> (click to unfold)
'<p>Content to be folded</p>
'</div>
```

The result will look as follows:

<div class="fold">
<p><b>Heading</b> (click to unfold)</p>
<p>Content to be folded</p>
</div>

### Images

Before inserting an image into CalcpadCE document, you need to have it already as a file.
You can create it by some image editing software and save it to a \*.png, \*.gif, \*.jpg or \*.svg file.
You can use some freeware programs like Paint, Gimp, InkScape, DraftSight or others.
Then you can insert it using HTML.
All you need to do is to put the following text at the required place, inside a comment:

```calcpad
'<img style="float:right" src="c:/Users/Me/Pictures/Picture1.png" alt="Picture1.png">
```

Of course, instead of "**c:/Users/Me/Pictures/Picture1.png**" you must specify the actual path to your image.
The file can be local, network or on the Internet.
Always use forward slashes "**/**", even if the file is local.
If the image is located in the same folder as the current worksheet, you can specify a relative path as follows: "**./Picture1.png**".
A path can also start with `{project}`, `{library}` or `{user}`, so the same document finds its images on any computer.
See [Path root tokens](new-includes.md#path-root-tokens-project-and-library).
The text **style="float:right;"** aligns the image to the right allowing the text to float at left.
Otherwise, the image will become part of the text flow and will make it split.
Alternatively, to **style="float:right"**, you can use **class="side"** for the same purpose.

You can also insert an image with the **Insert Image** button on the [Insert](new-calcpad-panel.md#insert) tab.
You will be prompted to select a file, and an `<img>` tag with its path is inserted at the cursor.
In the desktop app, you can also right-click an image in the **Files** tab to insert a reference to it with a relative or absolute path.

Local images are embedded automatically when you export the report.
They are also packed into [compiled worksheets and portable packages](new-exports.md#portable-exports), so those still show them on another computer.

## Formatting with Markdown

[Markdown](https://www.markdownguide.org/) is a simple and lightweight markup language for text formatting.
Unlike HTML, it uses individual symbols or short sequences of symbols for tagging.
In CalcpadCE, you can use Markdown in comments optionally, instead of HTML.
Since it requires an additional parsing step, you can switch it on and off by using the following keywords inside your worksheet:

```calcpad
#md on - switches Markdown mode on;  
#md off - switches Markdown mode off.
```

During parsing, the marked text is converted to HTML and then passed for further processing.
Since this is performed line-by-line, block level formatting like lists, tables, etc. are not fully supported.
For those, use a `#markdown` block instead, which renders consecutive lines together.
See [`#markdown` vs `#md on`](new-parsing-modes.md#markdown-vs-md-on).
You can use the following syntax elements:

| Markdown code | HTML code | Output |
| ------------- | --------- | ------ |
| `# Heading 1` | `<h1>Heading 1</h1>` | Heading 1 |
| `## Heading 2` | `<h2>Heading 2</h2>` | Heading 2 |
| `### Heading 3` | `<h3>Heading 3</h3>` | Heading 3 |
| `#### Heading 4` | `<h4>Heading 4</h4>` | Heading 4 |
| `##### Heading 5` | `<h5>Heading 5</h5>` | Heading 5 |
| `###### Heading 6` | `<h6>Heading 6</h6>` | Heading 6 |
| `---` (horizontal line) | `<hr/>` | **———————** |
| `**Bold**` | `<strong>Bold</strong>` | **Bold** |
| `*Italic*` | `<em>Italic</em>` | *Italic* |
| `***Bold Italic***` | `<em><strong>Bold Italic</strong></em>` | ***Bold Italic*** |
| `++Underlined++` | `<ins>Underlined</ins>` | <u>Underlined</u> |
| `~~Struck through~~` | `<del>Struck through</del>` | <s>Struck through</s> |
| `==Highlighted==` | `<mark>Highlighted</mark>` | Highlighted |
| `x^superscript^` | `x<sup>superscript</sup>` | x<sup>superscript</sup> |
| `x~subscript~` | `x<sub>subscript</sub>` | x<sub>subscript</sub> |
| `` `Code` `` | `<code>Code</code>` | `Code` |
| `[Example site](https://example.com)` | `<a href="https://example.com">Example site</a>` | [Example site](https://example.com) |
| `![Image](image.jpg)` | `<img src="image.jpg" alt="Image" />` | |
| `> Blockquote 1`<br/>`>> Blockquote 2` | `<blockquote>Blockquote 1`<br/>`<blockquote>Blockquote 2`<br/>`</blockquote></blockquote>` | |

## Formatting Hotkeys

The editor has hotkeys that insert formatting markup in comments, such as **Ctrl+B** for bold and **Ctrl+1** … **Ctrl+6** for headings.
They insert either HTML or Markdown, depending on the **Comment Format** setting.
By default, Markdown is used below an `#md on` line and HTML elsewhere.
Inside an `#html` or `#markdown` block, they insert that block's format without comment quotes.

See [Formatting hotkeys](new-calcpadce-editor.md#formatting-hotkeys) for the full list, with the markup each one inserts.
