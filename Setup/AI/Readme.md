## How to develop engineering calculations with CalcpadCE and Claude AI in VS Code

1. Install software:
   - Visual Studio Code - https://code.visualstudio.com/download
   - CalcpadCE for desktop - https://github.com/imartincei/CalcpadCE/releases
   - Node.js - https://nodejs.org/en/download/current

2. Install VS Code extensions
   - CalcpadCE - https://github.com/imartincei/CalcpadCE
   - Claude - https://marketplace.visualstudio.com/items?itemName=anthropic.claude-code

3. Install Claude Code in the terminal 
   - Start VS Code press Ctrl+\` top open the terminal. Then type the following:<br/>
   `npm install -g @anthropic-ai/claude-code`

4. Create a work folder for your CalcpadCE project and place these two files in it:
   - https://github.com/imartincei/CalcpadCE/blob/main/Setup/AI/Work/CALCPAD_CLAUDE_INSTRUCTIONS.txt
   - https://github.com/imartincei/CalcpadCE/blob/main/Setup/AI/Work/CALCPAD_LANGUAGE_REFERENCE_FOR_CLAUDE.md
   - You can also copy the Examples folder from your \Documents\Calcpad\

5. Start creating
   - Open your work folder in VS Code
   - Press Ctrl+\` to open the terminal and type: `claude`
   - Follow the steps to set up Claude in your terminal
   - Give Claude prompts, e.g.: 
     - `Create a CalcpadCE program for analysis of a simply supported beam`
     - `Calculate I-section properties with CalcpadCE`
   - Claude will automatically read the available files with instructions and learn to use CalcpadCE
   - Open the generated file and press Ctrl+Shift+B to run it with CalcpadCE
   - Correct errors if any or ask Claude to do it. Paste error messages from CalcpadCE into Claude console to provide clues.
