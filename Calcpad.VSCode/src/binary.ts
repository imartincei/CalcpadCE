import * as path from 'path';
import * as fs from 'fs';
import * as vscode from 'vscode';

export interface CliCommand {
  command: string;
  args: string[];
}

function ridFor(): string {
  switch (process.platform) {
    case 'win32':
      return 'win-x64';
    case 'linux':
      return 'linux-x64';
    case 'darwin':
      return process.arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
    default:
      throw new Error(`Unsupported platform: ${process.platform}`);
  }
}

// Builds the command used to launch the Calcpad CLI in --serve mode.
// Honors the `calcpad.cli.path` override (which may be a native exe or a Cli.dll
// run through `dotnet`); otherwise falls back to the bundled per-RID binary.
export function resolveCli(context: vscode.ExtensionContext): CliCommand {
  const override = vscode.workspace.getConfiguration('calcpad').get<string>('cli.path')?.trim();
  if (override) {
    return toCommand(override);
  }

  const exe = process.platform === 'win32' ? 'Cli.exe' : 'Cli';
  const bundled = path.join(context.extensionPath, 'bin', ridFor(), exe);
  if (fs.existsSync(bundled)) {
    ensureExecutable(bundled);
    return { command: bundled, args: ['--serve'] };
  }

  // Dev fallback (F5 from the monorepo): use the already-built Cli.dll next door
  // so the extension works without a full self-contained publish.
  const devDll = findDevCliDll(context.extensionPath);
  if (devDll) {
    return { command: 'dotnet', args: [devDll, '--serve'] };
  }

  throw new Error(
    `Calcpad CLI not found at ${bundled}. Build it with "npm run publish-cli", ` +
      `run "dotnet build Calcpad.Cli", or set "calcpad.cli.path".`
  );
}

function findDevCliDll(extensionPath: string): string | undefined {
  const base = path.resolve(extensionPath, '..', 'Calcpad.Cli', 'bin');
  for (const config of ['Debug', 'Release']) {
    const dll = path.join(base, config, 'net10.0', 'Cli.dll');
    if (fs.existsSync(dll)) {
      return dll;
    }
  }
  return undefined;
}

function toCommand(p: string): CliCommand {
  if (p.toLowerCase().endsWith('.dll')) {
    return { command: 'dotnet', args: [p, '--serve'] };
  }
  ensureExecutable(p);
  return { command: p, args: ['--serve'] };
}

function ensureExecutable(p: string): void {
  if (process.platform === 'win32') {
    return;
  }
  try {
    fs.accessSync(p, fs.constants.X_OK);
  } catch {
    try {
      fs.chmodSync(p, 0o755);
    } catch {
      /* best effort */
    }
  }
}
