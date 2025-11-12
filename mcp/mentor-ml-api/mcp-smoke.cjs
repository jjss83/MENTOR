const path = require('path');
const { Client } = require('@modelcontextprotocol/sdk/client/index.js');
const { StdioClientTransport } = require('@modelcontextprotocol/sdk/client/stdio.js');

const boolFromEnv = (value) => {
  if (value === undefined) {
    return undefined;
  }
  const normalized = value.trim().toLowerCase();
  if (['true', '1', 'yes', 'y', 'on'].includes(normalized)) {
    return true;
  }
  if (['false', '0', 'no', 'n', 'off'].includes(normalized)) {
    return false;
  }
  throw new Error(`Unable to parse boolean from value "${value}"`);
};

async function main() {
  const serverPath = path.resolve(__dirname, 'dist', 'index.js');
  const transport = new StdioClientTransport({
    command: 'node',
    args: [serverPath],
    cwd: path.resolve(__dirname, '..', '..'),
    env: {
      MENTOR_ML_API_BASE_URL: process.env.MENTOR_ML_API_BASE_URL || 'http://localhost:5113',
      MENTOR_ML_API_TIMEOUT_MS: process.env.MENTOR_ML_API_TIMEOUT_MS
    },
    stderr: 'pipe'
  });

  const client = new Client({
    name: 'mentor-mcp-smoke',
    version: '0.1.0'
  });

  process.on('SIGINT', async () => {
    await client.close();
    await transport.close();
    process.exit(1);
  });

  await client.connect(transport);
  console.log('Connected to MCP server.');

  const tools = await client.listTools();
  console.log('Available tools:', tools.tools?.map(t => t.name));

  const runId = process.env.MCP_RUN_ID || `mcp-3dball-${Date.now()}`;
  const configPath = process.env.MCP_CONFIG_PATH || 'config/ppo/3DBall.yaml';
  const environmentPath = process.env.MCP_ENVIRONMENT_PATH;
  const noGraphics = boolFromEnv(process.env.MCP_NO_GRAPHICS);

  const request = {
    runId,
    configPath
  };

  if (environmentPath) {
    request.environmentPath = environmentPath;
  }

  if (noGraphics !== undefined) {
    request.noGraphics = noGraphics;
  }

  console.log('Request payload:', request);

  const result = await client.callTool({
    name: 'mentor_run_training',
    arguments: request
  });

  console.log('Tool result:');
  console.log(JSON.stringify(result, null, 2));

  await client.close();
  await transport.close();
}

main().catch(async (err) => {
  console.error('Error running MCP smoke test:', err);
  process.exitCode = 1;
});
