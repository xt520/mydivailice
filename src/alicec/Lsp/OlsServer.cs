using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Server;

namespace alicec.Lsp;

internal static class OlsServer
{
    internal static async Task<int> RunAsync(Stream input, Stream output, TextWriter log)
    {
        var ws = new OlsWorkspace();

        var server = await LanguageServer.From(options =>
                options
                    .WithInput(input)
                    .WithOutput(output)
                    .WithServices(services =>
                    {
                        services.AddSingleton(ws);
                    })
                    .WithHandler<OlsTextDocumentHandler>()
                    .WithHandler<OlsDefinitionHandler>()
                    .WithHandler<OlsCompletionHandler>()
            )
            .ConfigureAwait(false);

        await server.WaitForExit.ConfigureAwait(false);
        return 0;
    }

}
