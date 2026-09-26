using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Typedown.WinUI.Services.MarkItDown
{
    public sealed record MarkItDownResult(bool Ok, string Markdown, string Error, string ErrorKind);

    public sealed class MarkItDownStartException : Exception
    {
        public MarkItDownStartException(string message) : base(message) { }
    }

    // One Python process per batch, fed one file at a time over stdin/stdout. Importing MarkItDown
    // and its converters takes a second or two, so a folder of fifty files pays that once, not fifty
    // times. The protocol is one JSON line each way, ASCII-only (json.dumps escapes everything else),
    // so no code page can mangle it. Anything a converter library prints goes to stderr, never into
    // the protocol. Markdown comes back as text, and Caret writes the file itself, so the
    // never-overwrite naming rules stay in one place.
    public sealed class MarkItDownWorker : IDisposable
    {
        private const string WorkerScript =
            "import sys, json\n" +
            "out = sys.stdout\n" +
            "sys.stdout = sys.stderr\n" +
            "def send(obj):\n" +
            "    out.write(json.dumps(obj) + '\\n')\n" +
            "    out.flush()\n" +
            "try:\n" +
            "    import importlib.metadata as m\n" +
            "    from markitdown import MarkItDown\n" +
            "    md = MarkItDown()\n" +
            "    send({'ready': True, 'version': m.version('markitdown')})\n" +
            "except Exception as e:\n" +
            "    send({'ready': False, 'error': '%s: %s' % (type(e).__name__, e)})\n" +
            "    sys.exit(1)\n" +
            "for line in sys.stdin:\n" +
            "    line = line.strip()\n" +
            "    if not line:\n" +
            "        continue\n" +
            "    try:\n" +
            "        job = json.loads(line)\n" +
            "        result = md.convert(job['src'])\n" +
            "        send({'ok': True, 'markdown': result.text_content or ''})\n" +
            "    except Exception as e:\n" +
            "        send({'ok': False, 'kind': type(e).__name__, 'error': str(e) or type(e).__name__})\n";

        private static readonly JsonSerializerSettings AsciiJson = new() { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii };

        private readonly Process process;
        private readonly StringBuilder stderrTail = new();

        public string Version { get; private set; }

        public bool IsAlive => !disposed && !timedOut && !process.HasExited;

        private bool disposed;
        private bool timedOut;

        private MarkItDownWorker(Process process)
        {
            this.process = process;
        }

        public static async Task<MarkItDownWorker> StartAsync(string pythonExe, int startTimeoutMs = 90_000)
        {
            var startInfo = ProcessRunner.CreateStartInfo(pythonExe, new[] { "-u", "-c", WorkerScript });
            startInfo.RedirectStandardInput = true;
            startInfo.StandardInputEncoding = new UTF8Encoding(false);
            var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                process.Dispose();
                throw new MarkItDownStartException(ex.Message);
            }
            var worker = new MarkItDownWorker(process);
            process.ErrorDataReceived += (_, e) => worker.AppendStderr(e.Data);
            process.BeginErrorReadLine();

            var hello = await worker.ReadMessageAsync(startTimeoutMs);
            if (hello == null)
            {
                var detail = worker.StderrTail;
                worker.Dispose();
                throw new MarkItDownStartException(string.IsNullOrWhiteSpace(detail) ? "Python exited before MarkItDown started." : detail);
            }
            if (hello.Value<bool?>("ready") != true)
            {
                worker.Dispose();
                throw new MarkItDownStartException(hello.Value<string>("error") ?? "MarkItDown couldn't start.");
            }
            worker.Version = hello.Value<string>("version");
            return worker;
        }

        // A timed-out or crashed conversion ends this worker (IsAlive turns false); the caller starts
        // a new one for the next file.
        public async Task<MarkItDownResult> ConvertAsync(string sourcePath, int timeoutMs)
        {
            if (!IsAlive) throw new InvalidOperationException("The MarkItDown worker has stopped.");
            stderrTail.Clear();
            var request = JsonConvert.SerializeObject(new { src = sourcePath }, AsciiJson);
            await process.StandardInput.WriteLineAsync(request);
            await process.StandardInput.FlushAsync();
            var reply = await ReadMessageAsync(timeoutMs);
            if (reply == null)
            {
                if (timedOut) throw new TimeoutException();
                var detail = StderrTail;
                throw new MarkItDownStartException(string.IsNullOrWhiteSpace(detail) ? "Python stopped unexpectedly." : detail);
            }
            return reply.Value<bool?>("ok") == true
                ? new MarkItDownResult(true, reply.Value<string>("markdown") ?? "", null, null)
                : new MarkItDownResult(false, null, reply.Value<string>("error"), reply.Value<string>("kind"));
        }

        // null when the process ended or didn't answer in time (in which case it's killed).
        private async Task<JObject> ReadMessageAsync(int timeoutMs)
        {
            var read = process.StandardOutput.ReadLineAsync();
            if (await Task.WhenAny(read, Task.Delay(timeoutMs)) != read)
            {
                timedOut = true;
                Kill();
                return null;
            }
            var line = await read;
            if (line == null) return null;
            try { return JObject.Parse(line); }
            catch (JsonException) { return null; }
        }

        private string StderrTail
        {
            get { lock (stderrTail) return stderrTail.ToString().Trim(); }
        }

        private void AppendStderr(string line)
        {
            if (line == null) return;
            lock (stderrTail)
            {
                stderrTail.AppendLine(line);
                // Only the end matters (the traceback's last lines); keep it bounded.
                if (stderrTail.Length > 8000) stderrTail.Remove(0, stderrTail.Length - 8000);
            }
        }

        private void Kill()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { process.StandardInput.Close(); } catch { }
            try { if (!process.WaitForExit(2000)) Kill(); } catch { }
            process.Dispose();
        }
    }
}
