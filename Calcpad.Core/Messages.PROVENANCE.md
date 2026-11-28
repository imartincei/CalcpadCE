# .NET message provenance

- `Messages.resx` is the canonical English source.
- `Messages.bg.resx` and `Messages.zh.resx` are community contributions.
- `Calcpad.Cli/Messages.*` and `Calcpad.OpenXml/Messages.*` follow the same rule.
- A future `Messages.pt-BR.resx` is a community contribution.
- The server accepts every frontend locale, but only Bulgarian and Chinese have satellites.
- Other cultures fall back to English; `zh-Hant` falls back through `zh`.
- The 30 keys missing from the Bulgarian and Chinese satellites still fall back to English.
- `Calcpad.Highlighter/Linter/Constants/ErrorCodes.cs` descriptions are English source, not translated yet.
- `Calcpad.Highlighter/Snippets/Data/*.cs` descriptions are English source, not translated yet.
