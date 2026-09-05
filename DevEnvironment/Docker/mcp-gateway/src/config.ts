export interface McpServerConfig {
  name: string;
  port: number;
  command: string;
  args: string[];
  env?: Record<string, string>;
}

export const mcpServers: McpServerConfig[] = [
  {
    name: 'mcp-sequentialthinking-tools',
    port: 3100,
    command: 'npx',
    args: ['-y', 'mcp-sequentialthinking-tools'],
    env: { MAX_HISTORY_SIZE: '1000' },
  },
  {
    name: 'context7-mcp',
    port: 3101,
    command: 'context7-mcp',
    args: [],
  },
  {
    name: 'context-mode',
    port: 3102,
    command: 'node',
    args: ['/usr/local/lib/node_modules/context-mode/start.mjs'],
  },
  {
    name: 'memory',
    port: 3103,
    command: 'mcp-server-memory',
    args: [],
    env: { MEMORY_FILE_PATH: '/opt/mcp-data/memory.json' },
  },
  {
    name: 'filesystem',
    port: 3104,
    command: 'mcp-server-filesystem',
    args: ['/workspace/WorkFlowPrj'],
  },
  {
    name: 'lexis-mcp',
    port: 3105,
    command: 'node',
    args: [
      '/usr/local/lib/node_modules/lexis-mcp/dist/cli/main.js',
      'mcp',
      '--path',
      '/workspace/WorkFlowPrj',
    ],
  },
  {
    name: 'token-savior-recall',
    port: 3106,
    command: 'token-savior',
    args: [],
    env: {
      WORKSPACE_ROOTS: '/workspace/WorkFlowPrj',
      TOKEN_SAVIOR_CLIENT: 'claude-code',
      TOKEN_SAVIOR_PROFILE: 'optimized',
    },
  },
  {
    name: 'csproj-context',
    port: 3107,
    command: 'node',
    args: ['/workspace/WorkFlowPrj/TypeScriptPrj/TsWorkspace/CsprojContext/build/index.js'],
  },
];
