# Theoretical Background

> (you can skip this if you find it boring)

How does CalcpadCE actually work?
There is a sophisticated math parser inside, that does most of the job.
First, the source code is scanned, and the sequence of bytes is converted into a list of tokens, using lexical analysis.
Each token is represented by data and type (purpose, role).

Then the parser checks if all tokens are in the correct order.
We need to know if the expression is mathematically correct and can be computed.
Otherwise, a comprehensible error message should be generated.
For example, "3 + a / 5" is a correct expression and "3 a + / 5" is not.
For that purpose, the standard mathematical notation is represented by a formal language with context-free grammar and syntax analysis is used.

Arithmetic expressions are usually written in infix notation.
It means that each operator is located between the respective operands (e.g. "5\*3 + 2"). The problem is that, unlike humans, computers are difficult to understand such expressions.
The main problems are the operator precedence and the use of brackets.
For example, the above expression makes "17", while "5\*(3 + 2)" makes "25". That is why, the expression is converted into different type of notation, called "postfix" or Reverse Polish Notation (RPN). It is very easy for a computer to read this one.
For example, the expression "5\*(3 + 2)" is written in RPN as "5 3 2 + \*". The main advantage is that the order of operations can be clearly specified without the need for brackets.

There is a simple and powerful algorithm for evaluation of expressions written in reverse polish notation (RPN). It is used by almost all calculators.
However, CalcpadCE includes additional functionality for processing parameters, functions, macros, conditional execution, loops, etc.

This was a brief and simple explanation.
If you are more curious about these topics, you can find more information in specialized books, papers or websites.
Wikipedia is a good place to start with:

<https://en.wikipedia.org/wiki/Parsing>

<https://en.wikipedia.org/wiki/Lexical_analysis>

<https://en.wikipedia.org/wiki/Context-free_grammar>

<https://en.wikipedia.org/wiki/Shunting-yard_algorithm>

<https://en.wikipedia.org/wiki/Reverse_Polish_notation>
