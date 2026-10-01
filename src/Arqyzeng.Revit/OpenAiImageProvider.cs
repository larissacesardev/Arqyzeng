using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Arqyzeng.Rendering;

internal sealed record ImageEditRequest(byte[] Png, string Prompt, int Width, int Height, string Quality, byte[]? MaskPng = null);
internal sealed record ImageEditResult(byte[] Png, int Width, int Height);

internal interface IImageProvider
{
    Task<ImageEditResult> EditAsync(ImageEditRequest request, string key, CancellationToken cancellation);
}

internal sealed class ImageProviderException : Exception
{
    internal ImageProviderException(string message) : base(message) { }
}

internal sealed class OpenAiImageProvider : IImageProvider
{
    internal const string Model = "gpt-image-2.5-sunburst";
    private readonly HttpClient _http;
    internal OpenAiImageProvider(HttpClient http) => _http = http;

    internal static (int Width, int Height) OutputSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (double)width / height > 3 || (double)height / width > 3)
            throw new ArgumentException("Para IA, use uma proporcao entre 1:3 e 3:1.");
        double scale = 1536.0 / Math.Max(width, height);
        int w = Math.Max(16, (int)Math.Round(width * scale / 16) * 16);
        int h = Math.Max(16, (int)Math.Round(height * scale / 16) * 16);
        return (w, h);
    }

    public async Task<ImageEditResult> EditAsync(ImageEditRequest input, string key, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('\r') || key.Contains('\n'))
            throw new ArgumentException("Insira uma chave API valida no campo protegido.");
        if (string.IsNullOrWhiteSpace(input.Prompt) || input.Png.Length == 0 || input.Png.Length >= 50 * 1024 * 1024)
            throw new ArgumentException("A captura deve ser PNG com menos de 50 MB e ter um prompt.");
        if (input.Width % 16 != 0 || input.Height % 16 != 0 || input.Width < 16 || input.Height < 16 ||
            input.Width > 3840 || input.Height > 3840 || (long)input.Width * input.Height < 655360 ||
            (long)input.Width * input.Height > 8294400 || (double)input.Width / input.Height > 3 ||
            (double)input.Height / input.Width > 3)
            throw new ArgumentException("Dimensoes de IA invalidas.");
        if (input.Quality is not ("low" or "medium" or "high"))
            throw new ArgumentException("Qualidade de IA invalida.");
        var referenceSize = ReadPngSize(input.Png);
        if (referenceSize.Width != input.Width || referenceSize.Height != input.Height)
            throw new ArgumentException("A referencia enviada deve ter as mesmas dimensoes solicitadas para o render.");
        if (input.MaskPng is not null)
        {
            if (input.MaskPng.Length >= 50 * 1024 * 1024 || ReadPngSize(input.MaskPng) != referenceSize ||
                input.MaskPng[25] != 6)
                throw new ArgumentException("A mascara deve ser PNG RGBA, menor que 50 MB e ter as dimensoes da referencia.");
        }
        cancellation.ThrowIfCancellationRequested();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(Model), "model");
        form.Add(new StringContent(input.Prompt), "prompt");
        form.Add(new StringContent($"{input.Width}x{input.Height}"), "size");
        form.Add(new StringContent(input.Quality), "quality");
        form.Add(new StringContent("1"), "n");
        form.Add(new StringContent("png"), "output_format");
        var image = new ByteArrayContent(input.Png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "image[]", "captura.png");
        if (input.MaskPng is not null)
        {
            var mask = new ByteArrayContent(input.MaskPng);
            mask.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(mask, "mask", "mascara.png");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/edits") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        // Uma unica tentativa: repetir automaticamente pode duplicar cobranca.
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Chave API recusada. Confira a chave no painel OpenAI.",
                HttpStatusCode.Forbidden => "Acesso ao modelo recusado. Confira permissoes e verificacao da organizacao no painel OpenAI.",
                HttpStatusCode.TooManyRequests => "Limite ou saldo da API atingido. Confira uso, cobranca e limites no painel OpenAI.",
                HttpStatusCode.BadRequest => "A API recusou os parametros ou o conteudo da captura.",
                HttpStatusCode.NotFound => "Modelo indisponivel para este projeto da API.",
                _ => "O servico de imagens nao concluiu a solicitacao."
            };
            // Nunca mostrar ou registrar corpo remoto, que pode conter dados ou credenciais.
            throw new ImageProviderException(message + $" HTTP {(int)response.StatusCode}. Nenhuma nova tentativa foi enviada.");
        }
        await using var remote = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int count;
        while ((count = await remote.ReadAsync(chunk.AsMemory(), cancellation).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > 80 * 1024 * 1024)
                throw new ImageProviderException("Resposta da API maior que o limite do MVP.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellation).ConfigureAwait(false);
        }
        try
        {
            using var json = JsonDocument.Parse(buffer.ToArray());
            var data = json.RootElement.GetProperty("data");
            if (data.GetArrayLength() != 1) throw new FormatException();
            string encoded = data[0].GetProperty("b64_json").GetString() ?? throw new FormatException();
            byte[] png = Convert.FromBase64String(encoded);
            var size = ReadPngSize(png);
            if (size.Width != input.Width || size.Height != input.Height)
                throw new ImageProviderException("A API devolveu dimensoes diferentes das solicitadas. O resultado nao foi aplicado.");
            return new ImageEditResult(png, size.Width, size.Height);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new ImageProviderException("A API nao devolveu um PNG valido. A captura original foi preservada.");
        }
    }

    internal static (int Width, int Height) ReadPngSize(byte[] png)
    {
        ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(signature) ||
            !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw new FormatException("PNG invalido.");
        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)));
        if (width < 1 || height < 1 || width > 4096 || height > 4096) throw new FormatException("PNG invalido.");
        return (width, height);
    }
}