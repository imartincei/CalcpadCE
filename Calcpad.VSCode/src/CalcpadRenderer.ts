import * as vscode from 'vscode';
import { ChildProcessWithoutNullStreams, spawn } from 'child_process';
import * as readline from 'readline';
import { resolveCli } from './binary';

export interface InputValue {
  line: number;
  value: string;
}

export interface RenderRequest {
  sourcePath?: string;
  sourceText?: string;
  units?: string;
  inputValues?: InputValue[];
}

interface ServerResponse {
  id: number;
  ok: boolean;
  html?: string;
  error?: string;
}

// Manages a single long-running `Cli --serve` child process and matches
// NDJSON responses back to their requests by id.
export class CalcpadRenderer implements vscode.Disposable {
  private proc: ChildProcessWithoutNullStreams | undefined;
  private rl: readline.Interface | undefined;
  private nextId = 1;
  private readonly pending = new Map<number, { resolve: (html: string) => void; reject: (err: Error) => void }>();
  private readonly output: vscode.OutputChannel;
  private disposed = false;

  constructor(private readonly context: vscode.ExtensionContext) {
    this.output = vscode.window.createOutputChannel('Calcpad Preview');
  }

  async render(request: RenderRequest): Promise<string> {
    const proc = this.ensureProcess();
    const id = this.nextId++;
    const payload = JSON.stringify({ id, ...request });
    return new Promise<string>((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      proc.stdin.write(payload + '\n', (err) => {
        if (err) {
          this.pending.delete(id);
          reject(err);
        }
      });
    });
  }

  private ensureProcess(): ChildProcessWithoutNullStreams {
    if (this.proc && !this.proc.killed) {
      return this.proc;
    }

    const { command, args } = resolveCli(this.context);
    const proc = spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'] });
    this.proc = proc;

    this.rl = readline.createInterface({ input: proc.stdout });
    this.rl.on('line', (line) => this.onLine(line));

    proc.stderr.on('data', (chunk) => this.output.append(chunk.toString()));

    proc.on('exit', (code, signal) => {
      this.output.appendLine(`[calcpad] CLI process exited (code=${code}, signal=${signal}).`);
      this.failAllPending(new Error('Calcpad render process exited.'));
      this.proc = undefined;
      this.rl?.close();
      this.rl = undefined;
    });

    proc.on('error', (err) => {
      this.output.appendLine(`[calcpad] Failed to start CLI: ${err.message}`);
      this.failAllPending(err);
      this.proc = undefined;
    });

    return proc;
  }

  private onLine(line: string): void {
    const trimmed = line.trim();
    if (!trimmed) {
      return;
    }
    let response: ServerResponse;
    try {
      response = JSON.parse(trimmed);
    } catch {
      this.output.appendLine(`[calcpad] Ignoring non-JSON output: ${trimmed.slice(0, 200)}`);
      return;
    }
    const entry = this.pending.get(response.id);
    if (!entry) {
      return;
    }
    this.pending.delete(response.id);
    if (response.ok && response.html !== undefined) {
      entry.resolve(response.html);
    } else {
      entry.reject(new Error(response.error ?? 'Unknown render error.'));
    }
  }

  private failAllPending(err: Error): void {
    for (const { reject } of this.pending.values()) {
      reject(err);
    }
    this.pending.clear();
  }

  dispose(): void {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    this.failAllPending(new Error('Extension deactivated.'));
    this.rl?.close();
    try {
      this.proc?.stdin.end();
    } catch {
      /* ignore */
    }
    this.proc?.kill();
    this.proc = undefined;
    this.output.dispose();
  }
}
