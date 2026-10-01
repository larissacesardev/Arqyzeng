using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Arqyzeng.Rendering;

internal static class ImageProviderChecks
{
    internal static async Task RunAsync()
    {
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        foreach (var size in new[] { (4096, 2304), (1152, 2048), (1024, 1024), (4096, 1755), (4096, 4096), (768, 256) })
        {
            var output = OpenAiImageProvider.OutputSize(size.Item1, size.Item2);
            Require(output.Width % 16 == 0 && output.Height % 16 == 0 && Math.Max(output.Width, output.Height) == 1536 &&
                (long)output.Width * output.Height >= 655360, "Normalizacao de dimensoes incorreta.");
        }
        bool rejected = false;
        try { OpenAiImageProvider.OutputSize(4096, 256); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Proporcao fora dos limites aceita.");
        // Cabecalho sintetico para testar o contrato HTTP; a decodificacao completa e feita pela WPF no plugin.
        byte[] png = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(png.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 1536);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 864);
        string json = JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(png) } } });
        var edit = new ImageEditRequest(png, "PROMPT_DE_TESTE", 1536, 864, "low");
        var success = new FakeHandler(async (request, token) =>
        {
            Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/images/edits", "Endpoint incorreto.");
            Require(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "CHAVE_FICTICIA", "Autenticacao incorreta.");
            string body = await request.Content!.ReadAsStringAsync(token);
            Require(body.Contains("image/png") && body.Contains("image[]") && body.Contains("captura.png") &&
                body.Contains(OpenAiImageProvider.Model) && body.Contains("1536x864") && body.Contains("PROMPT_DE_TESTE"), "Imagem ou parametros ausentes.");
            Require(!body.Contains("mascara.png"), "Print enviou mascara sem solicita-la.");
            Require(!body.Contains("CHAVE_FICTICIA"), "Credencial no corpo do formulario.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        using (var http = new HttpClient(success))
        {
            var result = await new OpenAiImageProvider(http).EditAsync(edit, "CHAVE_FICTICIA", CancellationToken.None);
            Require(result.Width == 1536 && result.Height == 864 && result.Png.SequenceEqual(png), "Resultado incorreto.");
        }
        Require(success.Count == 1, "Mais de uma solicitacao enviada.");
        var mismatched = new FakeHandler((_, _) => throw new InvalidOperationException("Nao deve enviar dimensoes divergentes."));
        using (var http = new HttpClient(mismatched))
        {
            rejected = false;
            try { await new OpenAiImageProvider(http).EditAsync(edit with { Height = 1024 }, "CHAVE_FICTICIA", CancellationToken.None); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && mismatched.Count == 0, "Referencia diferente da saida aceita.");
        }

        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError })
        {
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("SEGREDO_REMOTO") }));
            using var http = new HttpClient(handler);
            rejected = false;
            try { await new OpenAiImageProvider(http).EditAsync(edit, "CHAVE_FICTICIA", CancellationToken.None); }
            catch (ImageProviderException error) { rejected = error.Message.Contains($"HTTP {(int)status}") && !error.Message.Contains("SEGREDO_REMOTO") && !error.Message.Contains("CHAVE_FICTICIA"); }
            Require(rejected && handler.Count == 1, "Erro vazou dados ou repetiu chamada.");
        }
        var invalid = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        using (var http = new HttpClient(invalid))
        {
            rejected = false;
            try { await new OpenAiImageProvider(http).EditAsync(edit, "CHAVE_FICTICIA", CancellationToken.None); }
            catch (ImageProviderException) { rejected = true; }
            Require(rejected, "Resposta sem imagem aceita.");
        }
        foreach (string invalidJson in new[]
        {
            "{\"data\":[{\"b64_json\":\"nao-base64!\"}]}",
            JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(new byte[40]) } } })
        })
        {
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(invalidJson) }));
            using var http = new HttpClient(handler);
            rejected = false;
            try { await new OpenAiImageProvider(http).EditAsync(edit, "CHAVE_FICTICIA", CancellationToken.None); }
            catch (ImageProviderException) { rejected = true; }
            Require(rejected, "Resposta com imagem invalida aceita.");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var never = new FakeHandler((_, _) => throw new InvalidOperationException("Nao deve enviar."));
        using (var http = new HttpClient(never))
        {
            rejected = false;
            try { await new OpenAiImageProvider(http).EditAsync(edit, "CHAVE_FICTICIA", canceled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            Require(rejected && never.Count == 0, "Cancelamento antes do envio ignorado.");
        }
        var pending = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using (var http = new HttpClient(pending))
        using (var cancellation = new CancellationTokenSource())
        {
            Task<ImageEditResult> task = new OpenAiImageProvider(http).EditAsync(edit, "CHAVE_FICTICIA", cancellation.Token);
            cancellation.Cancel(); rejected = false;
            try { await task; } catch (OperationCanceledException) { rejected = true; }
            Require(rejected && pending.Count == 1, "Cancelamento durante a chamada falhou.");
        }
        // Panorama: os dois tamanhos 2:1 passam pelo mesmo contrato HTTP, sem chamada real.
        foreach (int width in new[] { 1536, 2048 })
        {
            byte[] panoramaPng = (byte[])png.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(panoramaPng.AsSpan(16, 4), (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(panoramaPng.AsSpan(20, 4), (uint)(width / 2));
            var panoramaRequest = new ImageEditRequest(panoramaPng, "PANORAMA_EQUIRECTANGULAR", width, width / 2, "low");
            var panoramaHandler = new FakeHandler(async (request, token) =>
            {
                string body = await request.Content!.ReadAsStringAsync(token);
                Require(body.Contains($"{width}x{width / 2}") && body.Contains("PANORAMA_EQUIRECTANGULAR") &&
                    !body.Contains("CHAVE_FICTICIA"), "Panorama: dimensoes ou corpo HTTP incorretos.");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(panoramaPng) } } })) };
            });
            using var http = new HttpClient(panoramaHandler);
            var panoramaResult = await new OpenAiImageProvider(http).EditAsync(panoramaRequest, "CHAVE_FICTICIA", CancellationToken.None);
            Require(panoramaHandler.Count == 1 && panoramaResult.Width == width && panoramaResult.Height * 2 == width,
                "Panorama: chamada repetida ou resultado fora de 2:1.");
        }
        byte[] maskPng = (byte[])png.Clone(); maskPng[25] = 6;
        var maskedHandler = new FakeHandler(async (request, token) => {
            string body = await request.Content!.ReadAsStringAsync(token);
            Require(body.Contains("mascara.png") && body.Contains("mask") && !body.Contains("CHAVE_FICTICIA"), "Mascara ausente ou credencial exposta.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        using (var http = new HttpClient(maskedHandler))
            await new OpenAiImageProvider(http).EditAsync(edit with { MaskPng = maskPng }, "CHAVE_FICTICIA", CancellationToken.None);
        Require(maskedHandler.Count == 1, "Reparo: chamada duplicada.");
        byte[] badMask = (byte[])maskPng.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(badMask.AsSpan(16, 4), 1024);
        var badMaskHandler = new FakeHandler((_, _) => throw new InvalidOperationException("Nao deve enviar mascara invalida."));
        using (var http = new HttpClient(badMaskHandler)) {
            rejected = false;
            try { await new OpenAiImageProvider(http).EditAsync(edit with { MaskPng = badMask }, "CHAVE_FICTICIA", CancellationToken.None); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && badMaskHandler.Count == 0, "Mascara divergente foi enviada.");
        }
        Console.WriteLine("OpenAI: contrato HTTP, dimensoes, erros sem vazamento, tentativa unica e cancelamento aprovados (servico simulado, sem custo).");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;
        internal int Count { get; private set; }
        internal FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) => _reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Count++; return _reply(request, cancellationToken); }
    }
}