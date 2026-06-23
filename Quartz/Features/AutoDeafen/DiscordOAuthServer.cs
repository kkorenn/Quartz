using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Quartz.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Quartz.Features.AutoDeafen;

// Tiny loopback HTTP server that captures the Discord OAuth token, ported
// from the original KorenResourcePack. Discord's implicit grant redirects to
// http://127.0.0.1:5672/ with the token in the URL fragment; the served page
// reads it with JS and POSTs it back to /token, where it's saved into the
// Auto Deafen settings.
//
// rpc.voice.write is gated to a Discord app's owner + up to 50 testers, so
// each user must point the mod at their OWN Discord application's client id.
internal static class DiscordOAuthServer {
    private const string RedirectUri = "http://127.0.0.1:5672/";

    internal static string ClientId => (AutoDeafen.Conf?.DiscordClientId ?? "").Trim();

    private static readonly object gate = new();
    private static readonly List<TcpListener> listeners = [];
    private static Thread thread;
    private static volatile bool running;
    private static string expectedState = "";
    private static Action<string> saveToken;
    private static volatile string status = "oauth off";

    internal static string Status => status;
    internal static bool Running => running;

    internal static bool EnsureStarted(Action<string> tokenSaver) {
        lock(gate) {
            saveToken = tokenSaver;
            if(running) {
                return true;
            }

            StopLocked();
            return StartLocked();
        }
    }

    internal static void Stop() {
        lock(gate) {
            StopLocked();
            status = "oauth off";
        }
    }

    internal static void OpenAuthorizeUrl() {
        string url = AuthorizeUrl();
        if(string.IsNullOrEmpty(url)) {
            return;
        }
        OpenUrl(url);
        status = "waiting for discord";
    }

    internal static string AuthorizeUrl() {
        if(string.IsNullOrEmpty(ClientId)) {
            status = "set client id first";
            return "";
        }

        // CSRF state: a fresh random token the capture page echoes back and the
        // callback handler verifies (returnedState != expectedState -> reject).
        // Guid "N" is hex, so URL-safe without escaping.
        string state = Guid.NewGuid().ToString("N");
        lock(gate) {
            expectedState = state;
        }

        if(!EnsureStarted(AutoDeafen.SaveAccessToken)) {
            lock(gate) {
                expectedState = "";
            }
            return "";
        }

        // Fixed shape; client_id and the CSRF state token are substituted.
        return "https://discord.com/oauth2/authorize?client_id=" + Escape(ClientId) +
            "&response_type=token&redirect_uri=http%3A%2F%2F127.0.0.1%3A5672%2F" +
            "&scope=identify+rpc+rpc.voice.write" +
            "&state=" + state;
    }

    private static bool StartLocked() {
        try {
            Uri uri = new(RedirectUri);
            int port = uri.Port > 0 ? uri.Port : 5672;

            TcpListener v4 = new(IPAddress.Loopback, port);
            v4.Start();
            listeners.Add(v4);

            try {
                TcpListener v6 = new(IPAddress.IPv6Loopback, port);
                try { v6.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true); } catch { }
                v6.Start();
                listeners.Add(v6);
            } catch { }

            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "Quartz-DiscordOAuth" };
            thread.Start();
            status = "oauth listening " + port;
            return true;
        } catch(Exception ex) {
            StopLocked();
            status = "oauth listen failed: " + ex.Message;
            MainCore.Log.Wrn("[AutoDeafen] oauth listen failed: " + ex);
            return false;
        }
    }

    private static void StopLocked() {
        running = false;
        for(int i = 0; i < listeners.Count; i++) {
            try { listeners[i].Stop(); } catch { }
        }
        listeners.Clear();
        thread = null;
    }

    private static void Run() {
        while(running) {
            try {
                for(int i = 0; i < listeners.Count; i++) {
                    TcpListener listener = listeners[i];
                    if(!listener.Pending()) {
                        continue;
                    }
                    using TcpClient client = listener.AcceptTcpClient();
                    Handle(client);
                }
            } catch(Exception ex) {
                if(running) {
                    status = "oauth error: " + ex.Message;
                    MainCore.Log.Wrn("[AutoDeafen] oauth error: " + ex);
                }
            }

            Thread.Sleep(40);
        }
    }

    private static void Handle(TcpClient client) {
        if(client.Client.RemoteEndPoint is IPEndPoint remote && !IPAddress.IsLoopback(remote.Address)) {
            WriteResponse(client, "403 Forbidden", "loopback only");
            return;
        }

        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;

        Request request = ReadRequest(client.GetStream());
        Dictionary<string, string> query = ParseQuery(request.Target);

        if(query.TryGetValue("error", out string error)) {
            status = "oauth denied: " + error;
            WriteResponse(client, "200 OK", "Discord authorization was denied: " + WebUtility.HtmlEncode(error));
            return;
        }

        if(request.Method == "POST" && request.Target.StartsWith("/token", StringComparison.Ordinal)) {
            HandleTokenPost(client, request.Body);
            return;
        }

        WriteResponse(client, "200 OK", CapturePage());
    }

    private static void HandleTokenPost(TcpClient client, string body) {
        try {
            JObject jo = JObject.Parse(body ?? "{}");
            string token = jo["access_token"]?.ToString();
            if(string.IsNullOrEmpty(token)) {
                status = "missing oauth token";
                WriteJson(client, "400 Bad Request", "{\"ok\":false,\"error\":\"missing_access_token\"}");
                return;
            }

            string returnedState = jo["state"]?.ToString();
            string stateToCheck;
            lock(gate) {
                stateToCheck = expectedState;
            }
            // No authorization request means no token is ever accepted. This also
            // closes the short post-success window before the listener shuts down.
            if(string.IsNullOrEmpty(stateToCheck) || returnedState != stateToCheck) {
                status = "oauth state mismatch";
                WriteJson(client, "400 Bad Request", "{\"ok\":false,\"error\":\"state_mismatch\"}");
                return;
            }

            saveToken?.Invoke(token);
            lock(gate) {
                expectedState = "";
            }
            status = "token saved";
            WriteJson(client, "200 OK", "{\"ok\":true}");
            StopAfterResponse();
        } catch(Exception ex) {
            status = "token save failed";
            MainCore.Log.Wrn("[AutoDeafen] token save failed: " + ex);
            WriteJson(client, "500 Internal Server Error", "{\"ok\":false,\"error\":\"token_save_failed\"}");
        }
    }

    private static Request ReadRequest(Stream stream) {
        byte[] buffer = new byte[8192];
        int offset = 0;
        while(offset < buffer.Length) {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if(read <= 0) {
                break;
            }
            offset += read;
            string header = Encoding.UTF8.GetString(buffer, 0, offset);
            int headerEnd = header.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if(headerEnd < 0) {
                continue;
            }

            int contentLength = ContentLength(header);
            int bodyStart = headerEnd + 4;
            int available = offset - bodyStart;
            while(available < contentLength && offset < buffer.Length) {
                read = stream.Read(buffer, offset, Math.Min(buffer.Length - offset, contentLength - available));
                if(read <= 0) {
                    break;
                }
                offset += read;
                available += read;
            }

            string body = contentLength > 0 && bodyStart < offset
                ? Encoding.UTF8.GetString(buffer, bodyStart, Math.Min(contentLength, offset - bodyStart))
                : "";
            return ParseRequest(header, body);
        }

        return ParseRequest(Encoding.UTF8.GetString(buffer, 0, offset), "");
    }

    private static Request ParseRequest(string header, string body) {
        if(string.IsNullOrEmpty(header)) {
            return new Request("GET", "/", body);
        }
        int lineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
        string line = lineEnd >= 0 ? header[..lineEnd] : header;
        string[] parts = line.Split(' ');
        return new Request(parts.Length >= 1 ? parts[0] : "GET", parts.Length >= 2 ? parts[1] : "/", body);
    }

    private static int ContentLength(string header) {
        string[] lines = header.Split(["\r\n"], StringSplitOptions.None);
        for(int i = 0; i < lines.Length; i++) {
            int sep = lines[i].IndexOf(':');
            if(sep < 0) {
                continue;
            }
            string name = lines[i][..sep].Trim();
            if(!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            return int.TryParse(lines[i][(sep + 1)..].Trim(), out int value) ? Math.Max(0, value) : 0;
        }
        return 0;
    }

    private static void StopAfterResponse() {
        Thread stopper = new(() => {
            Thread.Sleep(250);
            lock(gate) {
                StopLocked();
                status = "authorized";
            }
        }) { IsBackground = true, Name = "Quartz-DiscordOAuthStop" };
        stopper.Start();
    }

    private static Dictionary<string, string> ParseQuery(string target) {
        Dictionary<string, string> result = [];
        int q = target.IndexOf('?');
        if(q < 0 || q + 1 >= target.Length) {
            return result;
        }

        string[] pairs = target[(q + 1)..].Split('&');
        for(int i = 0; i < pairs.Length; i++) {
            if(string.IsNullOrEmpty(pairs[i])) {
                continue;
            }
            int eq = pairs[i].IndexOf('=');
            string key = eq >= 0 ? pairs[i][..eq] : pairs[i];
            string val = eq >= 0 ? pairs[i][(eq + 1)..] : "";
            result[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(val);
        }
        return result;
    }

    private static void WriteResponse(TcpClient client, string statusLine, string body) {
        string html = body.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase) >= 0
            ? body
            : "<!doctype html><meta charset=\"utf-8\"><title>ADOFAI Discord OAuth</title>" +
              "<body style=\"font:16px sans-serif;margin:32px\">" + body + "</body>";
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        string header = "HTTP/1.1 " + statusLine + "\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Content-Length: " + bytes.Length + "\r\n" +
            "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        Stream stream = client.GetStream();
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static void WriteJson(TcpClient client, string statusLine, string json) {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        string header = "HTTP/1.1 " + statusLine + "\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Content-Length: " + bytes.Length + "\r\n" +
            "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        Stream stream = client.GetStream();
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static string CapturePage() {
        return @"<!doctype html>
<meta charset=""utf-8"">
<title>ADOFAI Discord OAuth</title>
<body style=""font:16px sans-serif;margin:32px"">
<div id=""msg"">Connecting Discord to ADOFAI...</div>
<script>
(async function () {
  const msg = document.getElementById('msg');
  const params = new URLSearchParams(location.hash.replace(/^#/, ''));
  const accessToken = params.get('access_token');
  const state = params.get('state');
  if (!accessToken) {
    msg.textContent = 'ADOFAI Discord OAuth server is running. Open authorization from the mod settings.';
    return;
  }
  const res = await fetch('/token', {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({access_token: accessToken, state: state})
  });
  msg.textContent = res.ok
    ? 'Discord authorized. You can return to ADOFAI.'
    : 'ADOFAI rejected the Discord token. Try authorizing again from the mod settings.';
  history.replaceState(null, '', location.pathname);
})().catch(function () {
  document.getElementById('msg').textContent = 'Could not pass the token to ADOFAI.';
});
</script>
</body>";
    }

    private static string Escape(string value) => Uri.EscapeDataString(value ?? "");

    internal static void OpenUrl(string url) {
        try {
            Application.OpenURL(url);
            return;
        } catch { }

        try {
            if(Environment.OSVersion.Platform == PlatformID.MacOSX) {
                Process.Start("open", url);
            } else if(Environment.OSVersion.Platform == PlatformID.Unix || (int)Environment.OSVersion.Platform == 6) {
                Process.Start("xdg-open", url);
            } else {
                Process.Start(url);
            }
        } catch { }
    }

    private sealed class Request {
        internal readonly string Method;
        internal readonly string Target;
        internal readonly string Body;

        internal Request(string method, string target, string body) {
            Method = method ?? "GET";
            Target = target ?? "/";
            Body = body ?? "";
        }
    }
}
