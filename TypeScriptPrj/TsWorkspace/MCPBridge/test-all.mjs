// 测试所有 mcp-proxy 端口
import http from 'http';

const ports = [3100, 3101, 3102, 3103, 3104, 3105, 3106, 3107];

async function testPort(port) {
  return new Promise((resolve) => {
    const postData = JSON.stringify({
      jsonrpc: '2.0',
      id: '1',
      method: 'tools/list'
    });
    const options = {
      hostname: 'localhost',
      port: port,
      path: '/mcp',
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json, text/event-stream',
        'Content-Length': Buffer.byteLength(postData)
      }
    };
    const req = http.request(options, (res) => {
      let data = '';
      res.on('data', (chunk) => { data += chunk.toString(); });
      res.on('end', () => {
        // 解析 SSE data 行
        for (const line of data.split('\n')) {
          if (line.startsWith('data: ')) {
            try {
              const parsed = JSON.parse(line.slice(6));
              const tools = parsed.result?.tools || [];
              resolve({ port, status: res.statusCode, toolCount: tools.length, toolNames: tools.map(t => t.name) });
              return;
            } catch {}
          }
        }
        resolve({ port, status: res.statusCode, toolCount: 0, error: 'No data line found' });
      });
    });
    req.on('error', (e) => resolve({ port, status: 0, toolCount: 0, error: e.message }));
    req.write(postData);
    req.end();
  });
}

async function main() {
  console.log('Testing all MCP ports...\n');
  for (const port of ports) {
    const result = await testPort(port);
    const status = result.status === 200 ? '✅' : '❌';
    console.log(`${status} Port ${port}: HTTP ${result.status}, Tools: ${result.toolCount}`);
    if (result.toolNames?.length) {
      result.toolNames.forEach(n => console.log(`     - ${n}`));
    }
    if (result.error) {
      console.log(`     Error: ${result.error}`);
    }
  }
}

main().catch(console.error);
