## How to develop engineering calculations with CalcpadCE and Claude Code

The CalcpadCE desktop app has a built-in agent chat that needs none of this setup.
Follow these steps to use Claude Code on its own, in VS Code or a terminal.

1. Install software:
   - Visual Studio Code - https://code.visualstudio.com/download
   - CalcpadCE for desktop - https://github.com/imartincei/CalcpadCE/releases
   - Claude Code - https://code.claude.com/docs/en/setup

2. Install VS Code extensions:
   - CalcpadCE - https://github.com/imartincei/CalcpadCE
   - Claude Code - https://marketplace.visualstudio.com/items?itemName=anthropic.claude-code

3. Give Claude Code the CalcpadCE skill.
   Copy the `calcpad-generator` folder from https://github.com/imartincei/CalcpadCE/tree/main/.claude/skills into your personal skills folder: `%USERPROFILE%\.claude\skills\` on Windows, `~/.claude/skills/` on Linux.
   The skill reads the online CalcpadCE documentation when it needs more detail.
   Copy it again after updating CalcpadCE to pick up new rules.

4. Create a work folder for your CalcpadCE project.
   You can also copy the Examples folder from your \Documents\Calcpad\ into it.

5. Start creating:
   - Open your work folder in VS Code
   - Press Ctrl+\` to open the terminal and type: `claude`
   - Give Claude prompts, e.g.:
     - `Create a CalcpadCE file for analysis of a simply supported beam`
     - `Calculate I-section properties with CalcpadCE`
   - Claude uses the skill to write CalcpadCE files and looks up the documentation when it needs to
   - Open the generated file and press Ctrl+Shift+B to run it with CalcpadCE
   - Ask Claude to fix any problems, and paste the messages from the **Problems** panel into Claude to help it
