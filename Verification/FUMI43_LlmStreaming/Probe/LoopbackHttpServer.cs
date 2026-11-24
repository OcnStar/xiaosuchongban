using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xiaosu.CompanionAI.LLM.Probe
{
    public sealed class LoopbackHttpContext
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public string Method { get; set; } = "GET";
        public string RawTarget { get; set; } = "/";
        public string Path { get; set; } = "/";
        public string QueryString { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] BodyBytes { get; set; } = Array.Empty<byte>();

        public string BodyText => BodyBytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(BodyBytes);

        public LoopbackHttpContext(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public NetworkStream RawStream => _stream;

        public async Task WriteResponseAsync(int statusCode, string contentType, byte[] body)
        {
            body ??= Array.Empty<byte>();
            var sb = new StringBuilder(256);
            sb.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(ReasonPhrase(statusCode)).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                if (body.Length > 0)
                {
                    await _stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                }
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task WriteJsonAsync(int statusCode, string json)
        {
            return WriteResponseAsync(statusCode, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json ?? "{}"));
        }

        public Task WriteHtmlAsync(int statusCode, string html)
        {
            return WriteResponseAsync(statusCode, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html ?? string.Empty));
        }

        public async Task BeginSseAsync(int statusCode = 200)
        {
            string header =
                "HTTP/1.1 " + statusCode + " " + ReasonPhrase(statusCode) + "\r\n" +
                "Content-Type: text/event-stream; charset=utf-8\r\n" +
                "Cache-Control: no-cache, no-transform\r\n" +
                "X-Accel-Buffering: no\r\n" +
                "Connection: close\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(header);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task WriteSseEventAsync(string eventName, string singleLineData)
        {
            var sb = new StringBuilder(128 + (singleLineData?.Length ?? 0));
            if (!string.IsNullOrEmpty(eventName))
            {
                sb.Append("event: ").Append(eventName).Append('\n');
            }
            sb.Append("data: ").Append(singleLineData ?? "{}").Append("\n\n");
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            await WriteRawBytesAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        public async Task WriteRawBytesAsync(byte[] bytes, int offset, int count)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(bytes, offset, count).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void AbortConnectionImmediately()
        {
            try
            {
                _client.LingerState = new LingerOption(true, 0);
                _client.Close();
            }
            catch
            {
            }
        }

        private static string ReasonPhrase(int code)
        {
            return code switch
            {
                200 => "OK",
                204 => "No Content",
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                429 => "Too Many Requests",
                500 => "Internal Server Error",
                502 => "Bad Gateway",
                _ => "OK"
            };
        }
    }

    /// <summary>
    /// Loopback-only (127.0.0.1 and [::1]) async HTTP/1.1 + SSE server.
    /// Never binds 0.0.0.0 or external LAN interfaces.
    /// </summary>
    public sealed class LoopbackHttpServer : IDisposable
    {
        private readonly Func<LoopbackHttpContext, CancellationToken, Task> _handler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private TcpListener _v4Listener;
        private TcpListener _v6Listener;
        private int _port;
        private bool _disposed;

        public int Port => _port;

        public LoopbackHttpServer(int port, Func<LoopbackHttpContext, CancellationToken, Task> handler)
        {
            _port = port;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Start()
        {
            _v4Listener = new TcpListener(IPAddress.Loopback, _port);
            _v4Listener.Start();
            _port = ((IPEndPoint)_v4Listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync(_v4Listener, _cts.Token);

            try
            {
                _v6Listener = new TcpListener(IPAddress.IPv6Loopback, _port);
                _v6Listener.Start();
                _ = AcceptLoopAsync(_v6Listener, _cts.Token);
            }
            catch
            {
                // IPv6 loopback may be unavailable or disabled; IPv4 loopback remains active.
                _v6Listener = null;
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, ct));
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken serverCt)
        {
            using (client)
            {
                client.NoDelay = true;
                try
                {
                    using var stream = client.GetStream();
                    var ctx = await ReadHttpRequestAsync(client, stream, serverCt).ConfigureAwait(false);
                    if (ctx == null)
                    {
                        return;
                    }
                    await _handler(ctx, serverCt).ConfigureAwait(false);
                }
                catch
                {
                    // Client disconnects and intentional mid-stream aborts are expected.
                }
            }
        }

        private static async Task<LoopbackHttpContext> ReadHttpRequestAsync(
            TcpClient client,
            NetworkStream stream,
            CancellationToken ct)
        {
            const int MaxHeaderBytes = 65536;
            const int MaxBodyBytes = 2 * 1024 * 1024;

            using var headerBuf = new MemoryStream(2048);
            byte[] scratch = new byte[4096];
            int headerEndIndex = -1;

            while (headerEndIndex < 0)
            {
                if (headerBuf.Length > MaxHeaderBytes)
                {
                    return null;
                }
                int n = await stream.ReadAsync(scratch, 0, scratch.Length, ct).ConfigureAwait(false);
                if (n <= 0)
                {
                    return null;
                }
                headerBuf.Write(scratch, 0, n);
                headerEndIndex = FindDoubleCrlf(headerBuf.GetBuffer(), (int)headerBuf.Length);
            }

            byte[] allRead = headerBuf.GetBuffer();
            int totalRead = (int)headerBuf.Length;
            string headerText = Encoding.ASCII.GetString(allRead, 0, headerEndIndex);
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                return null;
            }

            string[] reqParts = lines[0].Split(' ');
            if (reqParts.Length < 2)
            {
                return null;
            }

            var ctx = new LoopbackHttpContext(client, stream)
            {
                Method = reqParts[0].Trim().ToUpperInvariant(),
                RawTarget = reqParts[1].Trim()
            };

            int qIdx = ctx.RawTarget.IndexOf('?');
            if (qIdx >= 0)
            {
                ctx.Path = ctx.RawTarget.Substring(0, qIdx);
                ctx.QueryString = ctx.RawTarget.Substring(qIdx + 1);
            }
            else
            {
                ctx.Path = ctx.RawTarget;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string hKey = lines[i].Substring(0, colon).Trim();
                    string hVal = lines[i].Substring(colon + 1).Trim();
                    ctx.Headers[hKey] = hVal;
                }
            }

            int bodyStart = headerEndIndex + 4;
            int alreadyBufferedBody = totalRead - bodyStart;

            if (ctx.Headers.TryGetValue("Content-Length", out string clStr) &&
                int.TryParse(clStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int contentLength) &&
                contentLength > 0)
            {
                if (contentLength > MaxBodyBytes)
                {
                    return null;
                }
                byte[] body = new byte[contentLength];
                int copied = Math.Min(alreadyBufferedBody, contentLength);
                if (copied > 0)
                {
                    Buffer.BlockCopy(allRead, bodyStart, body, 0, copied);
                }
                while (copied < contentLength)
                {
                    int n = await stream.ReadAsync(body, copied, contentLength - copied, ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    copied += n;
                }
                ctx.BodyBytes = body;
            }
            else if (ctx.Headers.TryGetValue("Transfer-Encoding", out string te) &&
                     te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                using var leftover = new MemoryStream();
                if (alreadyBufferedBody > 0)
                {
                    leftover.Write(allRead, bodyStart, alreadyBufferedBody);
                }
                ctx.BodyBytes = await ReadChunkedBodyAsync(stream, leftover.ToArray(), MaxBodyBytes, ct).ConfigureAwait(false);
            }

            return ctx;
        }

        private static async Task<byte[]> ReadChunkedBodyAsync(
            NetworkStream stream,
            byte[] prefetched,
            int maxBytes,
            CancellationToken ct)
        {
            using var raw = new MemoryStream();
            if (prefetched.Length > 0) raw.Write(prefetched, 0, prefetched.Length);

            using var decoded = new MemoryStream();
            byte[] buf = new byte[2048];
            int cursor = 0;

            while (true)
            {
                int crlf = FindSingleCrlf(raw.GetBuffer(), cursor, (int)raw.Length);
                while (crlf < 0)
                {
                    int n = await stream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                    if (n <= 0) return decoded.ToArray();
                    raw.Write(buf, 0, n);
                    crlf = FindSingleCrlf(raw.GetBuffer(), cursor, (int)raw.Length);
                }

                string sizeHex = Encoding.ASCII.GetString(raw.GetBuffer(), cursor, crlf - cursor).Split(';')[0].Trim();
                if (!int.TryParse(sizeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int chunkSize))
                {
                    break;
                }
                cursor = crlf + 2;
                if (chunkSize == 0)
                {
                    break;
                }
                if (decoded.Length + chunkSize > maxBytes)
                {
                    break;
                }
                while ((int)raw.Length - cursor < chunkSize + 2)
                {
                    int n = await stream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    raw.Write(buf, 0, n);
                }
                int avail = Math.Min(chunkSize, (int)raw.Length - cursor);
                if (avail > 0)
                {
                    decoded.Write(raw.GetBuffer(), cursor, avail);
                }
                cursor += chunkSize + 2;
            }
            return decoded.ToArray();
        }

        private static int FindDoubleCrlf(byte[] buf, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n')
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindSingleCrlf(byte[] buf, int start, int length)
        {
            for (int i = start; i <= length - 2; i++)
            {
                if (buf[i] == '\r' && buf[i + 1] == '\n')
                {
                    return i;
                }
            }
            return -1;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            try { _v4Listener?.Stop(); } catch { }
            try { _v6Listener?.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
