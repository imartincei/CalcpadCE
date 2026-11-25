// Loaded by CalcpadCE via `pi -e`: connects the editor's MCP server and gates tool calls.

const READ_ONLY = new Set([
  "read", "grep", "find", "ls", "codemode", "tool_search",
  "list_mcp_resources", "list_mcp_resource_templates", "read_mcp_resource",
]);

export default function (pi: any) {
  const env = process.env;
  const headers: Record<string, string> = JSON.parse(env.CALCPAD_MCP_HEADERS ?? "{}");
  headers.Authorization = `Bearer ${env.CALCPAD_MCP_TOKEN ?? ""}`;
  // Keep the token out of commands the agent runs.
  delete env.CALCPAD_MCP_TOKEN;
  pi.registerMcpServer("calcpad", {
    url: env.CALCPAD_MCP_URL,
    headers,
    exposure: "direct",
    description: "The worksheets open in the CalcpadCE editor",
  });
  const autoApproved = new Set(
    (env.CALCPAD_AUTO_TOOLS ?? "").split(",").filter(Boolean).map((t) => `mcp__calcpad__${t}`),
  );
  pi.on("tool_call", async (event: any, ctx: any) => {
    if (READ_ONLY.has(event.toolName) || autoApproved.has(event.toolName)) return;
    const allowed = await ctx.ui.confirm(
      "calcpad:approve",
      JSON.stringify({ tool_name: event.toolName, input: event.input }),
    );
    if (!allowed) return { block: true, reason: "The user denied this action." };
  });
}
