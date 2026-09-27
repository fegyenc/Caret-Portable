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
    //
    // Text files (CSV, TXT, JSON, XML, HTML) arrive with the encoding Caret decided on
    // (MarkItDownFormats.CharsetFor, the same rule as opening Markdown: byte-order mark, strict UTF-8,
    // else Windows' ANSI code page). Left to itself MarkItDown guesses, and it guessed a French
    // Windows-1252 CSV as Windows-1250 ("Été à Noël" came out "Été ŕ Noël"). CSVs separated by
    // semicolons, tabs or pipes (Excel on a French, Polish or Hungarian PC uses ";") are rewritten with
    // commas first, since MarkItDown only splits on commas; the delimiter is chosen like Caret's own
    // converter did: the one that appears most often, outside quotes, on the first line.
    public sealed class MarkItDownWorker : IDisposable
    {
        private const string WorkerScript = """
            import sys, json, csv, io
            out = sys.stdout
            sys.stdout = sys.stderr
            def send(obj):
                out.write(json.dumps(obj) + '\n')
                out.flush()
            try:
                import importlib.metadata as m
                from markitdown import MarkItDown
                md = MarkItDown()
                send({'ready': True, 'version': m.version('markitdown')})
            except Exception as e:
                send({'ready': False, 'error': '%s: %s' % (type(e).__name__, e)})
                sys.exit(1)
            try:
                from markitdown import StreamInfo
            except Exception:
                StreamInfo = None  # MarkItDown before 0.1: no hints, it guesses as before
            def delimiter(text):
                line = text.split('\n', 1)[0]
                def count(d):
                    n, quoted = 0, False
                    for ch in line:
                        if ch == '"':
                            quoted = not quoted
                        elif ch == d and not quoted:
                            n += 1
                    return n
                return max(',;\t|', key=count)
            def convert(src, charset):
                if StreamInfo is None or not charset:
                    return md.convert(src)
                if src.lower().endswith('.csv'):
                    with open(src, 'rb') as f:
                        text = f.read().decode(charset)
                    d = delimiter(text)
                    if d != ',':
                        buf = io.StringIO()
                        csv.writer(buf, lineterminator='\n').writerows(csv.reader(io.StringIO(text, newline=''), delimiter=d))
                        return md.convert(io.BytesIO(buf.getvalue().encode('utf-8')), stream_info=StreamInfo(extension='.csv', charset='utf-8'))
                return md.convert(src, stream_info=StreamInfo(charset=charset))
            for line in sys.stdin:
                line = line.strip()
                if not line:
                    continue
                try:
                    job = json.loads(line)
                    result = convert(job['src'], job.get('charset'))
                    send({'ok': True, 'markdown': result.text_content or ''})
                except Exception as e:
                    send({'ok': False, 'kind': type(e).__name__, 'error': str(e) or type(e).__name__})
            """;

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
            var request = JsonConvert.SerializeObject(new { src = sourcePath, charset = MarkItDownFormats.CharsetFor(sourcePath) }, AsciiJson);
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
