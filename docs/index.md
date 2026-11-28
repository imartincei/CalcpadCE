# About CalcpadCE

CalcpadCE (Community Edition) is free and open-source software for mathematical and engineering calculations.
It combines powerful computational algorithms with HTML reports that render live as you type, and export to PDF and Word.
It is simple and easy to use, but it also includes many advanced features:

- real and complex numbers (rectangular and polar-phasor formats);
- units of measurement (SI, Imperial and USCS);
- vectors and matrices: rectangular, symmetric, column, diagonal, upper/lower triangular;
- custom variables and units;
- built-in library with common math functions;
- vectors and matrix functions:
  - data functions: search, lookup, sort, count, etc.;
  - aggregate functions: min, max, sum, sumsq, srss, average, product, (geometric) mean, etc.;
  - math functions: norm, condition, determinant, rank, trace, transpose, adjugate and cofactor, inverse, factorization (Cholesky, ldlt, lu, qr and svd), eigenvalues/vectors and linear systems of equations;
- custom functions of multiple parameters f(x; y; z; …), with optional parameters and [keyword arguments](new-keyword-arguments.md);
- powerful numerical methods for root and extremum finding, numerical integration, and differentiation;
- finite sum, product, and iteration procedures, Fourier series and FFT;
- modules, macros, and re-usable text strings, with shared [project and library paths](new-includes.md#path-root-tokens-project-and-library);
- reading and writing data from/to text, CSV and Excel files;
- program flow control with conditions and loops;
- "titles" and 'text' comments in quotes;
- support for HTML, CSS and Markdown in comments, and whole [HTML or Markdown blocks](new-parsing-modes.md) for rich formatting;
- function plotting, images, tables, parametric SVG drawings, etc.;
- [input forms](new-ui-mode.md) with text boxes, drop-downs, radio buttons, checkboxes and editable grids;
- professional looking HTML reports for viewing and printing;
- export to PDF, Word (\*.docx) and HTML documents;
- variable substitution and smart rounding of numbers;
- output visibility control and content folding;
- a [linter](new-linter.md) that flags problems as you type;
- support for plain text (\*.txt, \*.cpd) and compiled (\*.cpdz) file formats.

CalcpadCE runs as a [desktop app](new-desktop-app.md) and as a [VS Code extension](new-vscode-extension.md).
Both use the same editor, side panel, and calculation engine.
See [Selecting a CalcpadCE Deployment](new-selecting-calcpadce-deployment.md) to help decide which one suits you.

The calculation engine is developed using the C# programming language.
It automatically parses the input, substitutes the variables, calculates the expressions, and displays the output.
All results are sent to a professional looking HTML report for viewing and printing.

<img src="./media/CalcpadCE Editor.png" alt="CalcpadCE" />
