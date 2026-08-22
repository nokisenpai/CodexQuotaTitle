using System.Text;
using CodexQuotaTitle.Mcp;

if (args.Length > 0 && string.Equals(args[0], "--mcp-helper", StringComparison.Ordinal))
{
    return await McpHelperProgram.RunAsync(args[1..]);
}

var startupContext = McpStartupContext.FromEnvironment();
var processTree = McpProcessTree.Capture(Environment.ProcessId);
var bootstrapLog = McpBootstrapLog.CreateDefault();
bootstrapLog.WriteProcessEntered(startupContext, processTree);

Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var server = new McpStdioServer(
    bootstrapLog,
    Environment.ProcessId,
    startupContext,
    new McpHelperBootstrapper(bootstrapLog, startupContext, processTree));

return await server.RunAsync(Console.In, Console.Out);
