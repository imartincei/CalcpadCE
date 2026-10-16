import * as vscode from 'vscode';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';
import { exec } from 'child_process';
import { promisify } from 'util';

const execAsync = promisify(exec);

const FONT_FILES = ['JuliaMono-Regular.ttf', 'JuliaMono-Bold.ttf'];

const WINDOWS_REGISTRY_NAMES: Record<string, string> = {
    'JuliaMono-Regular.ttf': 'JuliaMono Regular (TrueType)',
    'JuliaMono-Bold.ttf': 'JuliaMono Bold (TrueType)',
};

const PROMPTED_KEY = 'juliaMono.promptedForInstall';

function userFontDir(): string {
    if (process.platform === 'win32') {
        return path.join(process.env.LOCALAPPDATA!, 'Microsoft', 'Windows', 'Fonts');
    }
    if (process.platform === 'darwin') {
        return path.join(os.homedir(), 'Library', 'Fonts');
    }
    return path.join(os.homedir(), '.local', 'share', 'fonts');
}

function isJuliaMonoInstalled(): boolean {
    const dir = userFontDir();
    return FONT_FILES.every(f => fs.existsSync(path.join(dir, f)));
}

async function installFonts(sourceDir: string): Promise<void> {
    const destDir = userFontDir();
    fs.mkdirSync(destDir, { recursive: true });

    for (const file of FONT_FILES) {
        const src = path.join(sourceDir, file);
        const dest = path.join(destDir, file);
        fs.copyFileSync(src, dest);

        if (process.platform === 'win32') {
            const regName = WINDOWS_REGISTRY_NAMES[file];
            await execAsync(
                `reg add "HKCU\\Software\\Microsoft\\Windows NT\\CurrentVersion\\Fonts" /v "${regName}" /t REG_SZ /d "${dest}" /f`
            );
        }
    }

    if (process.platform === 'linux') {
        await execAsync('fc-cache -f');
    }
}

export async function installJuliaMonoCommand(context: vscode.ExtensionContext): Promise<void> {
    const sourceDir = path.join(context.extensionPath, 'fonts');

    if (isJuliaMonoInstalled()) {
        vscode.window.showInformationMessage('JuliaMono is already installed.');
        return;
    }

    try {
        await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: 'Installing JuliaMono…' },
            () => installFonts(sourceDir)
        );
    } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to install JuliaMono: ${msg}`);
        return;
    }

    const choice = await vscode.window.showInformationMessage(
        'JuliaMono installed. Reload the window to use it in the editor.',
        'Reload Window'
    );
    if (choice === 'Reload Window') {
        vscode.commands.executeCommand('workbench.action.reloadWindow');
    }
}

export async function maybePromptInstall(context: vscode.ExtensionContext): Promise<void> {
    if (context.globalState.get<boolean>(PROMPTED_KEY)) return;
    if (isJuliaMonoInstalled()) {
        await context.globalState.update(PROMPTED_KEY, true);
        return;
    }

    const choice = await vscode.window.showInformationMessage(
        'CalcpadCE recommends the JuliaMono font for better math symbol rendering in .cpd files. Install now?',
        'Install',
        'Not Now',
        "Don't Ask Again"
    );

    if (choice === 'Install') {
        await context.globalState.update(PROMPTED_KEY, true);
        await installJuliaMonoCommand(context);
    } else if (choice === "Don't Ask Again") {
        await context.globalState.update(PROMPTED_KEY, true);
    }
}
