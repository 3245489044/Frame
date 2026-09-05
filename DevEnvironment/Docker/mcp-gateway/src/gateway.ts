import http from 'http';
import { McpManager } from './mcp-manager';
import { mcpServers, McpServerConfig } from './config';

interface SseClient {
  id: number;
  res: http.ServerResponse;
}

export class McpGateway {
  private manager = new McpManager();
  private sseClients = new Map<string, Set<SseClient>>();
  private clientIdCounter = 0;

  async start(): Promise<void> {
    await this.manager.startAll(mcpServers);

    for (const config of mcpServers) {
      this.setupMessageHandler(config);
      this.createServer(config);
    }

    console.log(
      `MCP Gateway started: ${mcpServers.map((s) => `${s.name}:${s.port}`).join(', ')}`
    );
  }

  private setupMessageHandler(config: McpServerConfig): void {
    this.manager.setMessageHandler(config.name, (data: string) => {
      this.broadcastToClients(config.name, data);
    });
  }

  private createServer(config: McpServerConfig): void {
    const server = http.createServer((req, res) => {
      // CORS 头
      res.setHeader('Access-Control-Allow-Origin', '*');
      res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
      res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

      if (req.method === 'OPTIONS') {
        res.writeHead(204);
        res.end();
        return;
      }

      if (req.method === 'GET' && req.url === '/mcp') {
        this.handleSseConnection(config.name, res);
      } else if (req.method === 'POST' && req.url === '/mcp') {
        this.handleMcpRequest(config.name, req, res);
      } else {
        res.writeHead(404, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Not found' }));
      }
    });

    server.listen(config.port, '0.0.0.0', () => {
      console.log(`[${config.name}] HTTP server listening on port ${config.port}`);
    });
  }

  private handleSseConnection(name: string, res: http.ServerResponse): void {
    res.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      Connection: 'keep-alive',
    });

    const clientId = ++this.clientIdCounter;
    const client: SseClient = { id: clientId, res };

    if (!this.sseClients.has(name)) {
      this.sseClients.set(name, new Set());
    }
    this.sseClients.get(name)!.add(client);

    // 发送初始连接事件
    res.write(`event: connected\ndata: {"clientId": ${clientId}}\n\n`);

    // 心跳保活
    const keepAlive = setInterval(() => {
      res.write(':keepalive\n\n');
    }, 15000);

    reqOnClose(res, () => {
      clearInterval(keepAlive);
      this.sseClients.get(name)?.delete(client);
      console.log(`[${name}] SSE client ${clientId} disconnected`);
    });
  }

  private handleMcpRequest(
    name: string,
    req: http.IncomingMessage,
    res: http.ServerResponse
  ): void {
    let body = '';
    req.on('data', (chunk: string) => {
      body += chunk;
    });
    req.on('end', () => {
      // 将请求转发到 MCP 子进程的 stdin
      this.manager.sendMessage(name, body);

      // 返回 202 Accepted，实际响应通过 SSE 推送
      res.writeHead(202, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ status: 'accepted' }));
    });
  }

  private broadcastToClients(name: string, data: string): void {
    const clients = this.sseClients.get(name);
    if (!clients) return;

    for (const client of clients) {
      try {
        client.res.write(`data: ${data}\n\n`);
      } catch {
        clients.delete(client);
      }
    }
  }

  async stop(): Promise<void> {
    await this.manager.stopAll();
    console.log('[McpGateway] Gateway stopped');
  }
}

// 辅助函数：监听响应关闭事件
function reqOnClose(res: http.ServerResponse, callback: () => void): void {
  res.on('close', callback);
  res.on('error', callback);
}
