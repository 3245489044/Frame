import { McpGateway } from './gateway';

const gateway = new McpGateway();

// 优雅关闭
process.on('SIGTERM', async () => {
  console.log('[Gateway] Received SIGTERM, shutting down...');
  await gateway.stop();
  process.exit(0);
});

process.on('SIGINT', async () => {
  console.log('[Gateway] Received SIGINT, shutting down...');
  await gateway.stop();
  process.exit(0);
});

// 启动网关
gateway.start().catch((err) => {
  console.error('[Gateway] Failed to start:', err);
  process.exit(1);
});
