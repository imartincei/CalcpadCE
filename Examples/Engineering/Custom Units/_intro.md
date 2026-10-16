[CalcpadCE](https://calcpad-ce.org) ships with the [SI](https://en.wikipedia.org/wiki/International_System_of_Units) base and derived units built in, but the unit system itself is open: any new dimension can be declared with `#def` and used like a native unit in the rest of the worksheet.

The two pages in this group illustrate the pattern with everyday examples.
[Currency Units](#currency-units) defines USD, EUR and a handful of other currencies along with their cross-rates so that monetary expressions auto-convert and round correctly, while [Information Units](#information-units) sets up bit, byte, KiB, MiB and friends with their binary prefixes, ready for bandwidth and storage calculations.

Each page is self-contained and can be `#include`d into any worksheet that needs the same units, providing a starting template for your own custom unit families.
