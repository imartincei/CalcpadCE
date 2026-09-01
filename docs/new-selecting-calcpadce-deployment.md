# Selecting a CalcpadCE Deployment

## CalcpadCE Desktop App

### Overview

Can be executed and installed like any other desktop application (will be able to be installed on Windows soon). 
It runs locally by bundling the web application in an OS-native webview using Tauri.
It runs the CalcpadCE engine as a local server.

### Pros

- Streamlined for CalcpadCE functionality
- Interface is optimized for ease-of-use
- Smaller install size
- Better file opening from system explorer
- Lightweight and fast
- Best for users who are less comfortable with developer tools

### Cons
- No MacOS support
- No AI, terminal, Git, or extension integrations
- Interface will look different on different operating systems
- Limited window splitting and no multi-monitor support
- Only one app instance can be active at once.

## CalcpadCE for VS Code

### Overview

Runs CalcpadCE inside VS Code via its extension framework.
It runs locally by bundling the web application in VS Code webview panes and runs the CalcpadCE engine as a local server.

### Pros
- Great for developers who are already familiar with VS Code
- Integrates with system terminal, Git, Github Copilot, Claude Code, and other features from VS Code's large extension library.
- Same interface on every operating system
- MacOS support
- Multiple app instances can be active at once, windows can be active on multiple monitors.
- Gives the most options for users who are comfortable with developer tools.

### Cons
- Larger install size if VS Code is not already installed
- Slower performance and more memory usage
- Interface is more cluttered as it supports more than CalcpadCE features
- Window management is more complex, UI mode is less streamlined.

## Shared Features
- Shared CalcpadCE side panel with identical functionality. See [CalcpadCE Panel & Settings](new-calcpad-panel.md).
- Same PDF, Word, and HTML export processes.
- Same CalcpadCE editor, symbol navigation, and linting. See [CalcpadCE Editor](new-calcpadce-editor.md).
- Same preview options and functionality
- Both run the same CalcpadCE calculation engine.
- Both allow offline usage and local filesystem access.
