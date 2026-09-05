// 测试 mcp-proxy 的 Streamable HTTP 端点
import http from 'http';

const port = process.argv[2] || 3100;

function testStreamableHTTP() {
  return new Promise((resolve) => {
    console.log(`\n=== 测试 Streamable HTTP http://localhost:${port}/mcp ===`);
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
      console.log('Status:', res.statusCode);
      console.log('Headers:', JSON.stringify(res.headers));
      let data = '';
      res.on('data', (chunk) => {
        data += chunk.toString();
        if (data.length > 5000) {
          console.log('Data (first 5000 chars):', data.substring(0, 5000));
          res.destroy();
          resolve({ status: res.statusCode, data: data.substring(0, 5000) });
        }
      });
      res.on('end', () => {
        console.log('Data (end):', data.substring(0, 5000));
        resolve({ status: res.statusCode, data: data.substring(0, 5000) });
      });
    });
    req.on('error', (e) => {
      console.log('Error:', e.message);
      resolve({ status: 0, error: e.message });
    });
    req.write(postData);
    req.end();
  });
}

async function main() {
  const result = await testStreamableHTTP();
  if (result.data) {
    try {
      const parsed = JSON.parse(result.data);
      console.log('\n=== 解析结果 ===');
      console.log(JSON.stringify(parsed, null, 2).substring(0, 2000));
    } catch {
      console.log('\n=== 原始数据（非 JSON）===');
    }
  }
}

main().catch(console.error);
