// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Project contributors: DVMProject authors
*
*/

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace dvmconsole
{
    /// <summary>
    /// Supplies authenticated HTTP audio to the native URL reader without storing
    /// an unbounded stream or exposing upstream credentials in its URL.
    /// One playback attempt owns the loopback listener and all associated requests.
    /// </summary>
    internal sealed class AuthenticatedAudioRelay : IDisposable
    {
        private static readonly TimeSpan RequestIdleTimeout = TimeSpan.FromSeconds(30);
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly HttpClient client = new HttpClient();
        private readonly CancellationTokenSource cancellation;
        private readonly Uri upstream;
        private readonly string route = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private CancellationTokenRegistration stopListener;
        private HttpResponseMessage firstResponse;
        private Task serverTask;
        private byte[] responseHeaders;
        private int disposed;

        /// <summary>Per-attempt local URL. Never contains the upstream credentials.</summary>
        public string Url { get; private set; }

        private AuthenticatedAudioRelay(Uri upstream, string username, string password, CancellationToken token)
        {
            this.upstream = upstream;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            client.Timeout = RequestIdleTimeout;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password ?? string.Empty}")));
        }

        /// <summary>
        /// Validates authentication before opening the local endpoint. The caller
        /// must dispose this owner after disposing its Media Foundation reader.
        /// </summary>
        public static AuthenticatedAudioRelay Open(string url, string username, string password, CancellationToken token)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Authenticated audio requires an HTTP(S) URL without embedded credentials.");

            var relay = new AuthenticatedAudioRelay(uri, username, password, token);
            try
            {
                relay.firstResponse = relay.RequestAsync(HttpMethod.Get, null, token).GetAwaiter().GetResult();
                relay.responseHeaders = BuildResponseHeaders(relay.firstResponse);
                token.ThrowIfCancellationRequested();
                relay.listener.Start();
                relay.Url = $"http://127.0.0.1:{((IPEndPoint)relay.listener.LocalEndpoint).Port}{relay.route}";
                relay.stopListener = relay.cancellation.Token.Register(() => relay.listener.Stop());
                relay.serverTask = Task.Run(relay.ServeAsync);
                return relay;
            }
            catch
            {
                relay.Dispose();
                throw;
            }
        }

        private async Task<HttpResponseMessage> RequestAsync(HttpMethod method, RangeHeaderValue range, CancellationToken token)
        {
            using var request = new HttpRequestMessage(method, upstream);
            request.Headers.Range = range;
            HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }

        private async Task ServeAsync()
        {
            var connections = new List<Task>();
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    TcpClient connection = await listener.AcceptTcpClientAsync(cancellation.Token).ConfigureAwait(false);
                    connections.RemoveAll(task => task.IsCompleted);
                    // Native readers may reopen/range-read while sniffing. Bound
                    // those connections without retaining completed tasks forever.
                    if (connections.Count >= 4) { connection.Dispose(); continue; }
                    connections.Add(ForwardAsync(connection));
                }
            }
            catch (Exception ex) when (cancellation.IsCancellationRequested &&
                (ex is OperationCanceledException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException)) { }
            finally { await Task.WhenAll(connections).ConfigureAwait(false); }
        }

        private static byte[] BuildResponseHeaders(HttpResponseMessage response)
        {
            var reply = new StringBuilder($"HTTP/1.1 {(int)response.StatusCode} OK\r\nConnection: close\r\n");
            if (response.Content.Headers.ContentType != null) reply.Append($"Content-Type: {response.Content.Headers.ContentType}\r\n");
            if (response.Content.Headers.ContentLength.HasValue) reply.Append($"Content-Length: {response.Content.Headers.ContentLength.Value}\r\n");
            if (response.Content.Headers.ContentRange != null) reply.Append($"Content-Range: {response.Content.Headers.ContentRange}\r\n");
            reply.Append("\r\n");
            return Encoding.ASCII.GetBytes(reply.ToString());
        }

        private async Task ForwardAsync(TcpClient connection)
        {
            using (connection)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
            {
                try
                {
                    timeout.CancelAfter(RequestIdleTimeout);
                    CancellationToken token = timeout.Token;
                    using NetworkStream local = connection.GetStream();
                    var header = new StringBuilder();
                    byte[] single = new byte[1];
                    while (header.Length < 16384)
                    {
                        if (await local.ReadAsync(single, token).ConfigureAwait(false) == 0) return;
                        header.Append((char)single[0]);
                        int count = header.Length;
                        if (count >= 4 && header[count - 4] == '\r' && header[count - 3] == '\n' &&
                            header[count - 2] == '\r' && header[count - 1] == '\n') break;
                    }
                    string[] lines = header.ToString().Split("\r\n");
                    string[] request = lines[0].Split(' ');
                    if (header.Length >= 16384 || request.Length != 3 || request[1] != route ||
                        (request[0] != "GET" && request[0] != "HEAD"))
                    {
                        await local.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token).ConfigureAwait(false);
                        return;
                    }

                    RangeHeaderValue range = null;
                    string rangeLine = lines.FirstOrDefault(line => line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                    if (rangeLine != null && !RangeHeaderValue.TryParse(rangeLine.Substring(6).Trim(), out range)) return;

                    bool head = request[0] == "HEAD";
                    if (head)
                    {
                        // Live sources often implement GET only. Satisfy native
                        // HEAD probes from the already validated response metadata.
                        await local.WriteAsync(responseHeaders, token).ConfigureAwait(false);
                        return;
                    }
                    // HTTP permits a server to ignore Range and return the whole
                    // representation. Reuse the validated GET for the first read.
                    HttpResponseMessage response = Interlocked.Exchange(ref firstResponse, null);
                    timeout.CancelAfter(RequestIdleTimeout);
                    response ??= await RequestAsync(HttpMethod.Get, range, token).ConfigureAwait(false);
                    using (response)
                    {
                        // Only a fixed upstream URL is reachable; do not forward
                        // client credentials, cookies, redirects or arbitrary headers.
                        await local.WriteAsync(BuildResponseHeaders(response), token).ConfigureAwait(false);
                        using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                        byte[] buffer = new byte[32768];
                        while (true)
                        {
                            timeout.CancelAfter(RequestIdleTimeout);
                            int read = await body.ReadAsync(buffer, token).ConfigureAwait(false);
                            if (read == 0) break;
                            await local.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is SocketException || ex is OperationCanceledException || ex is ObjectDisposedException || ex is HttpRequestException)
                {
                    // The native reader can close sniff/range requests early.
                    // End failed responses and let playback retry. Do not retain a
                    // probe's failure and apply it to a later successful connection.
                }
                catch (Exception ex)
                {
                    Log.WriteWarning($"Authenticated audio relay request failed: {ex.GetType().Name}.");
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            cancellation.Cancel();
            listener.Stop();
            try { serverTask?.GetAwaiter().GetResult(); }
            finally
            {
                Interlocked.Exchange(ref firstResponse, null)?.Dispose();
                client.Dispose();
                stopListener.Dispose();
                cancellation.Dispose();
            }
        }
    }
}
