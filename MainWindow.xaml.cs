using QRCoder;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Drawing;

namespace WifiDownload
{
    public partial class MainWindow : Window
    {
        private string selectedPath = null;
        private bool selectedIsFolder = false;
        private CancellationTokenSource cts;
        private Task serverTask;
        private HttpListener listener;

        // 使用端口 215（0215）
        private const int DefaultPort = 215;

        public MainWindow()
        {
            InitializeComponent();
            TxtPort.Text = DefaultPort.ToString();
            TxtPreview.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 未开始分享{Environment.NewLine}";
            TxtPreviewRoller.ScrollToEnd();
        }

        private void ApplyFirewallRule(int port)
        {
            try
            {
                string ruleName = $"LAN_Share_Port_{port}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true
                })?.WaitForExit(1000);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}",
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true
                })?.WaitForExit(1000);

                Dispatcher.Invoke(() =>
                {
                    TxtPreview.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 已配置防火墙端口：{Environment.NewLine}";
                    TxtPreviewRoller.ScrollToEnd();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtPreview.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 防火墙配置失败：{ex.Message}{Environment.NewLine}";
                    TxtPreviewRoller.ScrollToEnd();
                });
            }
        }


        private BitmapImage BitmapToImageSource(Bitmap bitmap)
            {
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = ms;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                return img;
            }

    private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog();
            if (ofd.ShowDialog() == true)
            {
                SelectPath(ofd.FileName, false);
            }
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                SelectPath(dlg.SelectedPath, true);
            }
        }

        private void SelectPath(string path, bool isFolder)
        {
            selectedPath = path;
            selectedIsFolder = isFolder || Directory.Exists(path);
            TxtSelected.Text = (selectedIsFolder ? "文件夹：" : "文件：") + path;
        }

        private void DragArea_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
            else
                e.Effects = System.Windows.DragDropEffects.None;
        }

        private void DragArea_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                    SelectPath(files[0], Directory.Exists(files[0]));
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedPath) || (!File.Exists(selectedPath) && !Directory.Exists(selectedPath)))
            {
                System.Windows.MessageBox.Show("请先选择文件或文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse(TxtPort.Text, out int port))
            {
                System.Windows.MessageBox.Show("端口格式不正确", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string localIp = GetLocalIPv4();
            if (string.IsNullOrEmpty(localIp))
            {
                System.Windows.MessageBox.Show("无法获取本机局域网IP", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string baseUrl = $"http://{localIp}:{port}/";


            cts = new CancellationTokenSource();
            serverTask = Task.Run(() => RunHttpServerAsync(localIp, port, cts.Token));
            Dispatcher.Invoke(() =>
            {
                TxtUrl.Text = baseUrl;
                BtnStart.IsEnabled = false;
                BtnSelectFile.IsEnabled = false;
                BtnSelectFolder.IsEnabled = false;
                BtnStop.IsEnabled = true;
                StackBtn.Visibility = Visibility.Collapsed;
                TxtTip.Text = "扫描下方二维码下载你分享的文件";
                TxtSelected.Text = baseUrl;
                using var qrGen = new QRCodeGenerator();
                var data = qrGen.CreateQrCode(TxtUrl.Text, QRCodeGenerator.ECCLevel.Q);
                var qr = new QRCode(data);
                var bmp = qr.GetGraphic(20);
                QrImage.Source = BitmapToImageSource(bmp);
                QrImage.Visibility = Visibility.Visible;
            });

            await Task.Delay(200);
            //System.Windows.MessageBox.Show("分享已启动，局域网内设备打开该链接或扫描二维码访问。", "已开始", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopServer();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtUrl.Text))
                System.Windows.Clipboard.SetText(TxtUrl.Text);
        }

        private void BtnShowQr_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtUrl.Text))
            {
                System.Windows.MessageBox.Show("请先开始分享以生成链接", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new QRWindow(TxtUrl.Text);
            win.Owner = this;
            win.ShowDialog();
        }

        private void BtnOpenLocal_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(selectedPath) && (File.Exists(selectedPath) || Directory.Exists(selectedPath)))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = selectedPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }

        private void StopServer()
        {
            try
            {
                cts?.Cancel();
                listener?.Stop();
                serverTask?.Wait(500);
            }
            catch { }
            finally
            {
                string ruleName = $"LAN_Share_Port_{TxtPort.Text}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
                    Verb = "runas",
                    CreateNoWindow = true,
                    UseShellExecute = true
                })?.WaitForExit(1000);
                Dispatcher.Invoke(() =>
                {
                    TxtPreview.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 已移除防火墙规则{Environment.NewLine}";
                    TxtPreviewRoller.ScrollToEnd();
                });
                cts = null;
                serverTask = null;
                listener = null;


                Dispatcher.Invoke(() =>
                {
                    BtnStart.IsEnabled = true;
                    BtnStop.IsEnabled = false;
                    BtnSelectFile.IsEnabled = true;
                    BtnSelectFolder.IsEnabled = true;
                    TxtUrl.Text = string.Empty;
                    QrImage.Visibility = Visibility.Collapsed;
                    StackBtn.Visibility = Visibility.Visible;
                    TxtTip.Text = "点击下方按钮选择你要分享的文件或文件夹";
                    TxtSelected.Text = selectedPath;
                    TxtPreview.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 已停止{Environment.NewLine}";
                    TxtPreviewRoller.ScrollToEnd();
                });
            }
        }

        private string GetLocalIPv4()
        {
            try
            {
                string host = Dns.GetHostName();
                var addrs = Dns.GetHostEntry(host).AddressList;
                foreach (var a in addrs)
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        return a.ToString();
                }

                using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                udp.Connect("8.8.8.8", 65530);
                if (udp.LocalEndPoint is IPEndPoint ep)
                    return ep.Address.ToString();
            }
            catch { }
            return null;
        }

        private async Task RunHttpServerAsync(string ip, int port, CancellationToken token)
        {
            string prefix = $"http://{ip}:{port}/";
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                ApplyFirewallRule(port);
                listener.Start();
                Dispatcher.Invoke(() =>
                {
                    TxtPreview.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 已开始分享：{selectedPath}{Environment.NewLine}";
                    TxtPreviewRoller.ScrollToEnd();
                }
                );
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => {
                    TxtPreview.Text += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动HTTP服务失败：{ex.Message}{Environment.NewLine}";
                    TxtPreviewRoller.ScrollToEnd();
                    System.Windows.MessageBox.Show("启动 HTTP 服务失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                return;
            }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var ctxTask = listener.GetContextAsync();
                    var completed = await Task.WhenAny(ctxTask, Task.Delay(-1, token));
                    if (completed != ctxTask) break;

                    var ctx = ctxTask.Result;
                    _ = Task.Run(() => HandleRequest(ctx));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine("HTTP server error: " + ex);
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            string rawUrl = ctx.Request.Url.AbsolutePath;
            try
            {
                if (selectedIsFolder)
                {
                    string requested = Uri.UnescapeDataString(rawUrl.TrimStart('/'));

                    if (requested.StartsWith("download/"))
                    {
                        string relPath = requested.Substring("download/".Length);
                        string fullPath = Path.Combine(selectedPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullPath))
                        {
                            await ServeFile(ctx, fullPath); 
                            return;
                        }
                        else
                        {
                            ctx.Response.StatusCode = 404;
                            byte[] notFound = Encoding.UTF8.GetBytes("404 - 未找到");
                            ctx.Response.ContentType = "text/plain; charset=utf-8";
                            await ctx.Response.OutputStream.WriteAsync(notFound, 0, notFound.Length);
                            ctx.Response.Close();
                            return;
                        }
                    }

                    string fullDirPath = string.IsNullOrEmpty(requested) ? selectedPath : Path.Combine(selectedPath, requested.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(fullDirPath))
                    {
                        await ServeDirectoryListing(ctx, fullDirPath, requested);
                    }
                    else if (File.Exists(fullDirPath))
                    {
                        await ServeDownloadPage(ctx, fullDirPath);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 404;
                        byte[] notFound = Encoding.UTF8.GetBytes("404 - 未找到");
                        ctx.Response.ContentType = "text/plain; charset=utf-8";
                        await ctx.Response.OutputStream.WriteAsync(notFound, 0, notFound.Length);
                        ctx.Response.Close();
                    }
                }
                else
                {
                    string requested = Uri.UnescapeDataString(rawUrl.TrimStart('/'));

                    if (requested.StartsWith("download/"))
                    {
                        string relPath = requested.Substring("download/".Length);
                        string fullPath = Path.Combine(selectedPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullPath))
                        {
                            await ServeFile(ctx, fullPath);
                            return;
                        }
                        else
                        {
                            ctx.Response.StatusCode = 404;
                            byte[] notFound = Encoding.UTF8.GetBytes("404 - 未找到");
                            ctx.Response.ContentType = "text/plain; charset=utf-8";
                            await ctx.Response.OutputStream.WriteAsync(notFound, 0, notFound.Length);
                            ctx.Response.Close();
                            return;
                        }
                    }
                    await ServeDownloadPage(ctx, selectedPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("HandleRequest error: " + ex);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }


        private async Task ServeFile(HttpListenerContext ctx, string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                long fileLength = fi.Length;
                string fileName = fi.Name;

                // 告知客户端支持 Range
                ctx.Response.AddHeader("Accept-Ranges", "bytes");

                // 检查 Range 请求头
                string rangeHeader = ctx.Request.Headers["Range"];
                long start = 0;
                long end = fileLength - 1;
                bool isPartial = false;

                if (!string.IsNullOrEmpty(rangeHeader))
                {
                    // 只支持单个范围 "bytes=start-end" 格式
                    var ok = ParseRangeHeader(rangeHeader, fileLength, out start, out end);
                    if (!ok)
                    {
                        // 无效 Range -> 416
                        ctx.Response.StatusCode = 416;
                        ctx.Response.AddHeader("Content-Range", $"bytes */{fileLength}");
                        byte[] invalid = Encoding.UTF8.GetBytes("416 Range Not Satisfiable");
                        ctx.Response.ContentType = "text/plain; charset=utf-8";
                        ctx.Response.ContentLength64 = invalid.Length;
                        await ctx.Response.OutputStream.WriteAsync(invalid, 0, invalid.Length);
                        ctx.Response.Close();
                        return;
                    }
                    isPartial = true;
                }

                long contentLength = end - start + 1;

                // 打开文件流 — 大缓冲 + 异步 + 顺序读取提示
                using var fs = new FileStream(filePath,
                    FileMode.Open, FileAccess.Read, FileShare.Read,
                    8 * 1024 * 1024, // 8MB 内核缓冲
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                // 如果是部分请求，设置 206 和 Content-Range
                if (isPartial)
                {
                    ctx.Response.StatusCode = 206; // Partial Content
                    ctx.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{fileLength}");
                }
                else
                {
                    ctx.Response.StatusCode = 200;
                }

                ctx.Response.ContentType = GetContentTypeByName(fileName);
                ctx.Response.ContentLength64 = contentLength;
                ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(fileName)}\"");

                // 将文件流定位到 start
                fs.Seek(start, SeekOrigin.Begin);

                // 使用 CopyToAsync，高效传输；使用 4MB 缓冲
                const int bufferSize = 4 * 1024 * 1024;
                var buffer = new byte[bufferSize];

                long remaining = contentLength;
                while (remaining > 0)
                {
                    int toRead = remaining > bufferSize ? bufferSize : (int)remaining;
                    int read = await fs.ReadAsync(buffer, 0, toRead);
                    if (read <= 0) break;
                    await ctx.Response.OutputStream.WriteAsync(buffer, 0, read);
                    remaining -= read;
                }

                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ServeFile error: " + ex);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        // 解析 Range header（支持单个区间，返回是否成功）
        private bool ParseRangeHeader(string rangeHeader, long fileLength, out long start, out long end)
        {
            start = 0;
            end = fileLength - 1;

            // 期望格式 "bytes=START-END" 或 "bytes=START-" 或 "bytes=-SUFFIXLEN"
            if (string.IsNullOrEmpty(rangeHeader)) return false;
            rangeHeader = rangeHeader.Trim();

            if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;

            var range = rangeHeader.Substring(6).Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (range.Length == 0) return false;

            // 只处理第一个 range
            var part = range[0].Trim();
            if (part.StartsWith("-"))
            {
                // suffix length: last N bytes
                if (!long.TryParse(part[1..], out long suffixLen)) return false;
                if (suffixLen <= 0) return false;
                if (suffixLen > fileLength) suffixLen = fileLength;
                start = fileLength - suffixLen;
                end = fileLength - 1;
                return true;
            }
            else if (part.Contains('-'))
            {
                var pieces = part.Split('-', 2);
                if (!long.TryParse(pieces[0], out start)) return false;
                if (string.IsNullOrEmpty(pieces[1]))
                {
                    end = fileLength - 1;
                }
                else
                {
                    if (!long.TryParse(pieces[1], out end)) return false;
                }

                if (start < 0) start = 0;
                if (end >= fileLength) end = fileLength - 1;
                if (start > end) return false;
                return true;
            }

            return false;
        }

        private async Task ServeDownloadPage(HttpListenerContext ctx, string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                string fileName = fi.Name;

                var sb = new StringBuilder();
                sb.Append("<html><head><meta charset='utf-8'><title>局域网文件快速分享</title>");
                sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>"); 
                sb.Append("<style>");
                sb.Append("body { font-family: 'Segoe UI', Tahoma, Arial; padding: 20px; background: #f9f9f9; color: #333; margin:0; }");
                sb.Append(".container { max-width: 600px; margin: 40px auto; background: #fff; padding: 30px; border-radius: 12px; box-shadow: 0 4px 16px rgba(0,0,0,0.15); text-align: center; }");
                sb.Append("h1 { font-size: 26px; color: #0366d6; margin-bottom: 10px; }");
                sb.Append("p { font-size: 14px; color: #555; margin-bottom: 20px; }");
                sb.Append("h3 { color: #0366d6; margin-bottom: 25px; font-size: 15px; word-break: break-all; }");
                sb.Append(".download-btn { display: inline-block; padding: 14px 28px; font-size: 18px; color: #fff; background: #0366d6; border-radius: 8px; text-decoration: none; transition: background 0.2s; }");
                sb.Append(".download-btn:hover { background: #024c9c; }");
                sb.Append("</style>");
                sb.Append("</head><body>");
                sb.Append("<div class='container'>");
                sb.Append("<h1>局域网文件快速分享</h1>");
                sb.Append("<p>如果你使用微信扫码，建议点击右上角三个点然后选择使用默认浏览器打开。</p>");
                sb.Append($"<h3>下载文件：{WebUtility.HtmlEncode(fileName)}</h3>");

                string relPath = Path.GetRelativePath(selectedPath, filePath).Replace("\\", "/");
                string urlPath = string.Join("/", relPath.Split('/').Select(p => Uri.EscapeDataString(p)));
                sb.Append($"<a class='download-btn' href='/download/{urlPath}'>点击下载</a>");
                sb.Append("</div></body></html>");

                byte[] pageBytes = Encoding.UTF8.GetBytes(sb.ToString());
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = pageBytes.Length;
                await ctx.Response.OutputStream.WriteAsync(pageBytes, 0, pageBytes.Length);
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ServeDownloadPage error: " + ex);
                        try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                    }
        }



        private async Task ServeDirectoryListing(HttpListenerContext ctx, string dirPath, string requestedRelative)
        {
            var sb = new StringBuilder();
            sb.Append("<html><head><meta charset=\"utf-8\"><title>局域网文件快速分享</title>");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>"); 
            sb.Append("<style>");
            sb.Append("body { font-family: 'Segoe UI', Tahoma, Arial; padding: 20px; background: #f9f9f9; color: #333; margin:0; }");
            sb.Append(".container { max-width: 600px; margin: 30px auto; padding: 20px; background: #fff; border-radius: 12px; box-shadow: 0 4px 16px rgba(0,0,0,0.15); }");
            sb.Append("h1 { font-size: 26px; color: #0366d6; margin-bottom: 8px; text-align: center; }");
            sb.Append("p { font-size: 14px; color: #555; margin-bottom: 20px;}");
            sb.Append("h3 { color: #0366d6; margin-bottom: 20px; font-size: 20px; }");
            sb.Append(".file-list { list-style: none; padding: 0; margin: 0; }");
            sb.Append(".file-list li { background: #fff; margin-bottom: 8px; padding: 12px 16px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); transition: transform 0.1s; }");
            sb.Append(".file-list li:hover { transform: translateY(-2px); box-shadow: 0 4px 8px rgba(0,0,0,0.15); }");
            sb.Append(".file-list a { text-decoration: none; color: #0366d6; font-weight: 500; display: flex; align-items: center; word-break: break-all; }");
            sb.Append(".file-list a span { margin-right: 10px; font-size: 18px; }");
            sb.Append(".breadcrumb { margin-bottom: 20px; font-size: 14px; }");
            sb.Append(".breadcrumb a { text-decoration: none; color: #0366d6; margin-right: 5px; }");
            sb.Append("</style>");
            sb.Append("</head><body>");
            sb.Append("<div class='container'>");
            sb.Append("<h1>局域网文件快速分享</h1>");
            sb.Append("<p>如果你使用微信扫码，建议点击右上角三个点然后选择使用默认浏览器打开。</p>");
            sb.Append("<div class='breadcrumb'>");
            sb.Append($"<a href='/'>主页</a> / {WebUtility.HtmlEncode(requestedRelative)}</div>");
            sb.Append("<h3>目录：" + WebUtility.HtmlEncode(Path.GetFileName(dirPath)) + "</h3>");
            sb.Append("<ul class='file-list'>");

            if (!string.Equals(Path.GetFullPath(dirPath), Path.GetFullPath(selectedPath), StringComparison.OrdinalIgnoreCase))
            {
                string parentRel = string.IsNullOrEmpty(requestedRelative) ? "" : GetParentRelative(requestedRelative);
                sb.Append($"<li><a href='/{WebUtility.UrlEncode(parentRel)}'><span>⬆</span> 上级目录</a></li>");
            }

            foreach (var d in Directory.GetDirectories(dirPath))
            {
                string name = Path.GetFileName(d);
                string rel = string.IsNullOrEmpty(requestedRelative) ? name : (requestedRelative + "/" + name);
                string urlPath = string.Join("/", rel.Split('/').Select(p => Uri.EscapeDataString(p)));
                sb.Append($"<li><a href='/{urlPath}'><span>📁</span> {WebUtility.HtmlEncode(name)}</a></li>");
            }

            foreach (var f in Directory.GetFiles(dirPath))
            {
                string name = Path.GetFileName(f);
                string rel = string.IsNullOrEmpty(requestedRelative) ? name : (requestedRelative + "/" + name);
                string urlPath = string.Join("/", rel.Split('/').Select(p => Uri.EscapeDataString(p)));
                sb.Append($"<li><a href='/{urlPath}'><span>📄</span> {WebUtility.HtmlEncode(name)}</a></li>");
            }


            sb.Append("</ul>");
            sb.Append("</div></body></html>");

            byte[] buf = Encoding.UTF8.GetBytes(sb.ToString());
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
            ctx.Response.Close();
        }



        private string GetParentRelative(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return "";
            var parts = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) return "";
            return string.Join('/', parts, 0, parts.Length - 1);
        }

        private string GetContentTypeByName(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html",
                ".txt" => "text/plain",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream",
            };
        }
    }
}


