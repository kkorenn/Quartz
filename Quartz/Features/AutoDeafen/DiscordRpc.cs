using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using Quartz.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Features.AutoDeafen;

// Minimal Discord RPC client over the local IPC socket (named pipe on
// Windows, unix socket elsewhere), ported from the original
// KorenResourcePack. Authenticates with the user's OAuth access token and
// flips SET_VOICE_SETTINGS deaf on request from a background thread.
internal sealed class DiscordRpc {
    private readonly string clientId;
    private readonly string accessToken;

    private Thread thread;
    private volatile bool running;
    private volatile bool desiredDeaf;
    private volatile bool ready;
    private volatile string status = "idle";
    private Stream stream;
    private readonly object ioLock = new();

    internal string Status => status;
    internal bool Ready => ready;

    internal DiscordRpc(string clientId, string accessToken) {
        this.clientId = clientId;
        this.accessToken = accessToken;
    }

    internal void Start() {
        if(running) {
            return;
        }
        running = true;
        thread = new Thread(Run) { IsBackground = true, Name = "Quartz-DiscordRpc" };
        thread.Start();
    }

    internal void SetDeaf(bool deaf) {
        desiredDeaf = deaf;
    }

    internal void Stop() {
        running = false;
        try { if(ready) { ApplyDeaf(false); } } catch { }
        try { stream?.Dispose(); } catch { }
        stream = null;
        ready = false;
    }

    private void Run() {
        try {
            status = "connecting";
            stream = Connect();
            if(stream == null) {
                status = "discord not found";
                running = false;
                return;
            }

            Handshake();

            if(!TryAuthenticate()) {
                status = "authenticate failed";
                running = false;
                return;
            }

            ready = true;
            status = "ready";

            bool current = false;
            while(running) {
                if(desiredDeaf != current) {
                    ApplyDeaf(desiredDeaf);
                    current = desiredDeaf;
                }
                Thread.Sleep(120);
            }
        } catch(Exception ex) {
            status = "error: " + ex.Message;
            MainCore.Log.Wrn("[AutoDeafen] discord rpc error: " + ex);
        } finally {
            try { stream?.Dispose(); } catch { }
            stream = null;
            ready = false;
        }
    }

    private Stream Connect() {
        bool unix = Environment.OSVersion.Platform == PlatformID.Unix
            || Environment.OSVersion.Platform == PlatformID.MacOSX
            || (int)Environment.OSVersion.Platform == 6;

        for(int i = 0; i < 10; i++) {
            try {
                if(unix) {
                    string dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                        ?? Environment.GetEnvironmentVariable("TMPDIR")
                        ?? Environment.GetEnvironmentVariable("TMP")
                        ?? "/tmp";
                    string path = System.IO.Path.Combine(dir.TrimEnd('/'), "discord-ipc-" + i);
                    if(!File.Exists(path)) {
                        continue;
                    }
                    Socket sock = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    sock.Connect(new UnixDomainSocketEndPoint(path));
                    return new NetworkStream(sock, true);
                }

                NamedPipeClientStream pipe = new(".", "discord-ipc-" + i, PipeDirection.InOut);
                pipe.Connect(2000);
                return pipe;
            } catch { }
        }
        return null;
    }

    private void WriteFrame(int op, string json) {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] header = new byte[8];
        Buffer.BlockCopy(BitConverter.GetBytes(op), 0, header, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, header, 4, 4);
        lock(ioLock) {
            stream.Write(header, 0, 8);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }

    private JObject ReadFrame(out int op) {
        byte[] header = ReadExact(8);
        op = BitConverter.ToInt32(header, 0);
        int len = BitConverter.ToInt32(header, 4);
        byte[] payload = len > 0 ? ReadExact(len) : [];
        string json = Encoding.UTF8.GetString(payload);
        return string.IsNullOrEmpty(json) ? [] : JObject.Parse(json);
    }

    private byte[] ReadExact(int n) {
        byte[] buf = new byte[n];
        int off = 0;
        while(off < n) {
            int r = stream.Read(buf, off, n - off);
            if(r <= 0) {
                throw new IOException("ipc closed");
            }
            off += r;
        }
        return buf;
    }

    private void Handshake() {
        WriteFrame(0, JsonConvert.SerializeObject(new { v = 1, client_id = clientId }));
        ReadFrame(out _);
    }

    private JObject Command(string cmd, object args) {
        string nonce = Guid.NewGuid().ToString();
        WriteFrame(1, JsonConvert.SerializeObject(new { cmd, args, nonce }));
        while(true) {
            JObject msg = ReadFrame(out int op);
            if(op == 3) {
                // PING — answer with PONG and keep waiting for our reply.
                WriteFrame(4, msg.ToString(Formatting.None));
                continue;
            }
            if(msg.Value<string>("nonce") == nonce) {
                return msg;
            }
        }
    }

    private bool TryAuthenticate() {
        if(string.IsNullOrEmpty(accessToken)) {
            return false;
        }
        try {
            JObject r = Command("AUTHENTICATE", new { access_token = accessToken });
            JToken data = r["data"];
            return data != null && data["user"] != null;
        } catch {
            return false;
        }
    }

    private void ApplyDeaf(bool deaf) {
        Command("SET_VOICE_SETTINGS", new { deaf });
    }
}
