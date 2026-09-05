import { spawn, ChildProcess } from 'child_process';
import { McpServerConfig } from './config';

interface ManagedProcess {
  config: McpServerConfig;
  process: ChildProcess;
}

export class McpManager {
  private processes = new Map<string, ManagedProcess>();
  private messageHandlers: Map<string, (data: string) => void> = new Map();

  async startAll(configs: McpServerConfig[]): Promise<void> {
    for (const config of configs) {
      await this.startOne(config);
    }
  }

  async startOne(config: McpServerConfig): Promise<void> {
    const proc = spawn(config.command, config.args, {
      env: { ...process.env, ...config.env },
      stdio: ['pipe', 'pipe', 'pipe'],
    });

    proc.stdout?.on('data', (data: Buffer) => {
      const msg = data.toString();
      const handler = this.messageHandlers.get(config.name);
      if (handler) {
        handler(msg);
      }
    });

    proc.stderr?.on('data', (data: Buffer) => {
      console.error(`[${config.name}] stderr: ${data.toString().trim()}`);
    });

    proc.on('exit', (code: number | null) => {
      console.log(`[${config.name}] exited with code ${code}`);
      // 自动重启
      setTimeout(() => this.startOne(config), 1000);
    });

    proc.on('error', (err: Error) => {
      console.error(`[${config.name}] error: ${err.message}`);
    });

    this.processes.set(config.name, { config, process: proc });
    console.log(`[${config.name}] started on port ${config.port}`);
  }

  setMessageHandler(name: string, handler: (data: string) => void): void {
    this.messageHandlers.set(name, handler);
  }

  sendMessage(name: string, message: string): void {
    const managed = this.processes.get(name);
    if (managed && managed.process.stdin) {
      managed.process.stdin.write(message);
    }
  }

  async stopAll(): Promise<void> {
    for (const [, managed] of this.processes) {
      managed.process.kill();
    }
    this.processes.clear();
    console.log('[McpManager] All processes stopped');
  }

  getProcess(name: string): ChildProcess | undefined {
    return this.processes.get(name)?.process;
  }

  getStatus(): Array<{ name: string; port: number; running: boolean }> {
    const status: Array<{ name: string; port: number; running: boolean }> = [];
    for (const [name, managed] of this.processes) {
      status.push({
        name,
        port: managed.config.port,
        running: managed.process.exitCode === null,
      });
    }
    return status;
  }
}
