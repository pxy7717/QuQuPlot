using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace QuquPlot.Utils
{
    /// <summary>
    /// 实时数据流服务器，监听本地端口，接收外部数据流（如Python脚本），并通过事件传递数据点。
    /// </summary>
    public class RealTimeDataServer : IDisposable
    {
        private readonly int _port;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;

        /// <summary>
        /// 新数据点事件（线程池线程触发）
        /// </summary>
        public event Action<string, double, double>? DataReceived;

        public RealTimeDataServer(int port = 9000)
        {
            _port = port;
        }

        /// <summary>
        /// 启动服务器，开始监听端口
        /// </summary>
        public void Start()
        {
            if (_listener != null) return;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _listenTask = Task.Run(() => ListenLoop(_cts.Token));
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }

        private async Task ListenLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _listener!.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClient(client, token), token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"exception: {ex}");
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string? line;
                while (!token.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
                {
                    bool parsed = false;
                    try
                    {
                        var doc = JsonDocument.Parse(line);
                        if (doc.RootElement.TryGetProperty("curves", out var curvesElem) && curvesElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var curveElem in curvesElem.EnumerateArray())
                            {
                                string? id = null;
                                if (curveElem.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String)
                                    id = idElem.GetString();
                                if (id != null && curveElem.TryGetProperty("points", out var pointsElem) && pointsElem.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var pt in pointsElem.EnumerateArray())
                                    {
                                        if (pt.ValueKind == JsonValueKind.Array && pt.GetArrayLength() == 2)
                                        {
                                            double x = pt[0].GetDouble();
                                            double y = pt[1].GetDouble();
                                            DataReceived?.Invoke(id, x, y);
                                        }
                                    }
                                }
                            }
                            parsed = true;
                        }
                        // 兼容单条曲线格式: {"id":"curve1","points":[[x,y],...]}
                        else if (doc.RootElement.TryGetProperty("id", out var idElem2) && idElem2.ValueKind == JsonValueKind.String &&
                                 doc.RootElement.TryGetProperty("points", out var pointsElem2) && pointsElem2.ValueKind == JsonValueKind.Array)
                        {
                            string id = idElem2.GetString()!;
                            foreach (var pt in pointsElem2.EnumerateArray())
                            {
                                if (pt.ValueKind == JsonValueKind.Array && pt.GetArrayLength() == 2)
                                {
                                    double x = pt[0].GetDouble();
                                    double y = pt[1].GetDouble();
                                    DataReceived?.Invoke(id, x, y);
                                }
                            }
                            parsed = true;
                        }
                    }
                    catch { /* 不是JSON，继续尝试CSV */ }

                    if (!parsed)
                    {
                        // 解析格式: x,y 或 y
                        var parts = line.Split(',');
                        if (parts.Length == 2 &&
                            double.TryParse(parts[0], out double x) &&
                            double.TryParse(parts[1], out double y))
                        {
                            DataReceived?.Invoke("default", x, y);
                        }
                        else if (parts.Length == 1 && double.TryParse(parts[0], out double yOnly))
                        {
                            DataReceived?.Invoke("default", 0, yOnly);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
} 