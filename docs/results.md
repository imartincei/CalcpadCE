# Results

The results appear in the results pane beside the editor.
Open it with the **CalcpadCE Preview** button in VS Code, or toggle it with **Ctrl+P** in the desktop app.
You cannot edit the output content, but you can select and copy it.

By default, the results refresh each time you change the code.
For large documents, you can turn off **Auto-Run Preview** in the [Settings](new-settings.md#editor-features) tab and run the calculation manually with **Ctrl+Alt+X** or the **▶ Run** button.
Turn on **Sync Preview to Cursor Line** to have the results scroll to match the current position in the source code.

The results pane can show the document in **Preview**, **Input**, **Report** and **Unwrapped** modes.
See [How It Works](how-it-works.md).

## Substitution

CalcpadCE can substitute the values of variables in all formulas in the output, just before the answer:

<img src="./media/image39.png" style="width:2.27778in;height:0.43056in" alt="Картина, която съдържа Шрифт, линия, номер, текст Описанието е генерирано автоматично" />

For that purpose, you need to turn on **Substitute Variables** in the [Settings](new-settings.md#math) tab.
It is on by default, and a document can change it with `#settings {"substitute": false}`.
That makes the results easy to review and check.
This is important when calculations have to be checked by supervisors, teachers etc.
This is also an advantage over the spreadsheet software where the actual formulas are hidden in the cells.

If you do not need the substitution, you can turn this option off.
Then the answers will follow directly the calculation formulas:

<img src="./media/image40.png" style="width:1.52083in;height:0.45833in" alt="Картина, която съдържа Шрифт, линия, номер, текст Описанието е генерирано автоматично" />

There is also an option to control the variable substitution behavior inside worksheets.
You can use the following switches for that purpose:

- `#nosub` do not substitute variables (no substitution)
- `#novar` show equations only with substituted values (no variables)
- `#varsub` show equations with variables and substituted values (default)

If an equation gets too long and does not fit on a single line, you can choose the way it looks in the output by using these two switches:

- `#split` the equation is split after the "=" symbol
- `#wrap` the equation is wrapped at the end of the line (default)

## Rounding

Rounding is specified by the number of digits *n* after the decimal point.
It is set with **Decimals** in the [Settings](new-settings.md#math) tab.
The value of *n* can be between "0" and "15".
If you enter "0", all results will be rounded to integers.

However, rounding can come across some potential problems.
If the result is less than 10-*n* and you round it to *n* digits after the decimal point, the result will contain only zeros.
That is why, CalcpadCE incorporates some advanced rules: If the output contains less than *n* significant digits after rounding, it is expanded up to *n* significant digits.
Even then, if the number is too small, it will be difficult to count the zeros after the decimal point.
So, in such cases, the output is converted to floating point format with *n* digits.
When the total number of digits becomes greater than 2*n*, the factional part is being truncated.
In this way, the output becomes easier to read, still providing at least 2*n* significant digits.
You can see several examples below, obtained for *n* = 3.

| Code | Output |
| ---- | ------ |
| `0.000001 * π` | $3.14{×}10^{-6}$ |
| `0.001 * π` | $0.00314$ |
| `0.1 * π` | $0.314$ |
| `1 * π` | $3.142$ |
| `1000 * π` | $3141.59$ |
| `1000000 * π` | $3141593$ |

Rounding affects only the way in which numbers are displayed in the output.
Internally, all numbers are stored with the maximum possible precision.
That is why, if you print the output and try to repeat the calculations with the numbers from the report, you probably will get some little differences.
This is because you use the rounded values instead of the actual ones.

You can override the global rounding inside a worksheet by using the `#Round n` keyword, where *n* is the number of digits after the decimal point (from "0" to "15").
To restore the global rounding, enter `#Round default`.
You can also set it with `#settings {"decimals": n}`, which `#end settings` reverts.
See [Settings → In a document](new-settings.md#in-a-document).

## Formatting

CalcpadCE does not simply calculate formulas.
It also builds a professionally looking report out of your source code.
It uses HTML to format the output.
It is widely recognized and allows you to publish your calculations on the web.
You can select between two different styles for equation formatting: "**professional**" and "**inline**", with the **Format Equations** setting.
The professional style uses division bar, large and small brackets, radical, etc.
Numerator and denominator are displayed one above the other.
The inline style uses slash for displaying division and all symbols are arranged in a single line.
The following formatting rules apply:

- Intervals are maintained automatically.

- Variables are formatted as *italic*.

- Multiplication operator "\*" is replaced with "∙".

- Exponentiation operator "^" is formatted as superscript.

- Underscore "\_" is formatted as subscript.

- Square root function is replaced with radical √‾.

Several examples of formatting in different cases are provided in the table below:

| Code | Output |
| ---- | ------ |
| `x + 3` | *x* + 3 |
| `x - 3` | *x* – 3 |
| `3 * x` | 3∙*x* |
| `(x + 1)/3` | (*x* + 1)/3 or $`\frac{x + 1}{3}`$ |
| `x + 3 * y` | *x* + 3∙*y* |
| `sqr(x+3)` | $\sqrt{x + 3}$ |
| `x_1^3` | $x_1^3$ |
| `sin(x)` | **sin**(*x*) |

HTML formatting makes the report easier to read and check than the respective plain text.
You can also insert additional HTML code inside the comments that will affect the final appearance.

CalcpadCE uses for decimal separator the symbol defined in the regional settings of your operating system.

## Custom Format Strings

You can specify format strings for different parts of your worksheet and even for individual output values.
At worksheet level you can do that by following command:

```calcpad
#format format string
```

To restore the default formatting, add the following line:

```calcpad
#format default
```

To specify a custom format string for an individual output value, add a colon followed by the respective string, e.g.:

```calcpad
x = 12.345:format string
```

If you have units, the format specifier is positioned after the units:

```calcpad
x = 12.345cm:format string
```

There are several types of format strings that you can use:

### Exponential

Engineering or scientific: `En` or `en`.

`n` range: 0-17  
`n` default value: 6

| Code | Output |
| ---- | ------ |
| `123456.789:e` | $1.234568\mathrm{e}{+}005$ |
| `0.00123456:e2` | $1.23\mathrm{e}{-}003$ |
| `123456789:E3` | $1.235{×}10^{+008}$ |

### Fixed-point

Displays always fixed-point: `Fn` or `fn`.

`n` range: 0-17  
`n` default value: 2

| Code | Output |
| ---- | ------ |
| `123.456789:f` | $123.46$ |
| `0.00123456:F5` | $0.00123$ |
| `123:F2` | $123.00$ |

### General

Displays either fixed point or scientific: `Gn` or `gn`

`n` range: 0-17  
`n` default value: 15

| Code | Output |
| ---- | ------ |
| `123.456789:g` | $123.456789$ |
| `0.0012345678:g3` | $0.00123$ |
| `123456m:G3` | $1.23×10^5 \mathrm{m}$<br>(with unit "Meters") |

### Number

Fixed point with digit grouping: `Nn` or `nn`

`n` range: 0-17  
`n` default value: 2

The symbols defined in the regional settings of your operating system are used for thousands and decimal separators.

| Code | Output |
| ---- | ------ |
| `123.456789:n` | $123.46$ |
| `0.0012345678:N3` | $0.001$ |
| `123456:N3` | $123\mathrm{,}456.000$ |

### Currency

Fixed point with currency symbol and digit grouping: `Cn` or `en`

`n` range: 0-17  
`n` default value: 2

The currency symbol defined in the regional settings of your operating system is used.

| Code | Output |
| ---- | ------ |
| `123.456789:C` | $123.46 €$ |
| `0.0012345678:C3` | $0.001 €$ |
| `123456:C` | $123\mathrm{,}456.00 €$ |

### Custom

The following characters can be used to compose a custom formatting:

- `0` - zero placeholder. Displays either a digit or zero if a digit is not available.
- `#` - optional digit placeholder. Displays a digit if available or nothing.
- `.` - decimal separator
- `,` - group separator
- `E`, `e`, `E+`, `e+`, `E-`, `e-` - exponential notation

| Code | Output |
| ---- | ------ |
| `123.456789:000000` | $000123$ |
| `123.45:0.0000` | $123.4500$ |
| `123.45:0.####` | $123.45$ |
| `0.00123456:#.#####` | $.00123$ |
| `1234567:#,#.0` | $1\mathrm{,}234\mathrm{,}567.0$ |
| `1234567:0.00e+00` | $1.23\mathrm{e}{+}06$ |
| `1234567:0.00e-0` | $1.23\mathrm{e}6$ |
| `0.01234:#.##e-000` | $1.23\mathrm{e}{-}002$ |
| `0.12:0.000e-00` | $1.200\mathrm{e}{-}0$ |

## Exporting and Printing

The report can be exported to **PDF**, **Word** (\*.docx) or **HTML** from the **Export** tab of the [CalcpadCE panel](new-calcpad-panel.md#export).
PDF export lets you set the paper size, orientation, margins and the header and footer, and a document can carry its own page setup.
Word export writes the equations as native Word equations, which can be edited inside Word.
An HTML file can be opened on any computer with a web browser, without CalcpadCE.
To print the report, export it to PDF and print the PDF.

See [Exports](new-exports.md) for the details of each format, and for exporting plots and portable copies of a document.

To copy part of the output, select it and press **Ctrl+C**.
If the target program supports HTML, like Word, the formatting will be preserved.
Otherwise, the content will be pasted as plain text.
