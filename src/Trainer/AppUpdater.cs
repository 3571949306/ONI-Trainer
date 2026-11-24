using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ONITrainer
{
    public sealed class UpdateCheckResult
    {
        public bool Success;
        public string Error;
        public Version CurrentVersion;
        public Version LatestVersion;
        public string LatestTag;
        public string ReleaseName;
        public string ReleaseNotes;
        public string HtmlUrl;
        public string PublishedAt;
        public string AssetName;
        public string AssetDownloadUrl;
        public long AssetSize;

        public bool HasUpdate
        {
            get
            {
                return Success && LatestVersion != null && CurrentVersion != null &&
                       AppUpdater.CompareVersions(CurrentVersion, LatestVersion) < 0;
            }
        }
    }

    public static class AppUpdater
    {
        public const string RepoUrl = "https://github.com/3571949306/ONI-Trainer";
        public const string ReleasesUrl = "https://github.com/3571949306/ONI-Trainer/releases";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/3571949306/ONI-Trainer/releases/latest";

        public static Version GetCurrentVersion()
        {
            try
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return NormalizeVersion(v ?? new Version(2, 0, 0, 0));
            }
            catch
            {
                return new Version(2, 0, 0, 0);
            }
        }

        public static string GetCurrentVersionDisplay()
        {
            return FormatVersion(GetCurrentVersion());
        }

        public static string FormatVersion(Version v)
        {
            Version n = NormalizeVersion(v ?? new Version(0, 0, 0, 0));
            if (n.Revision > 0) return "v" + n.Major + "." + n.Minor + "." + n.Build + "." + n.Revision;
            if (n.Build > 0) return "v" + n.Major + "." + n.Minor + "." + n.Build;
            return "v" + n.Major + "." + n.Minor;
        }

        public static Version NormalizeVersion(Version v)
        {
            if (v == null) return new Version(0, 0, 0, 0);
            int maj = Math.Max(0, v.Major);
            int min = Math.Max(0, v.Minor);
            int bld = v.Build >= 0 ? v.Build : 0;
            int rev = v.Revision >= 0 ? v.Revision : 0;
            return new Version(maj, min, bld, rev);
        }

        public static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string s = tag.Trim();
            while (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
                s = s.Substring(1).Trim();
            int sep = s.IndexOfAny(new char[] { '-', '+' });
            if (sep >= 0) s = s.Substring(0, sep).Trim();
            if (s.Length == 0) return null;

            string[] parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return null;
            int[] nums = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                int val;
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out val) || val < 0)
                    return null;
                nums[i] = val;
            }
            return new Version(nums[0], nums[1], nums[2], nums[3]);
        }

        public static int CompareVersions(Version a, Version b)
        {
            Version na = NormalizeVersion(a);
            Version nb = NormalizeVersion(b);
            return na.CompareTo(nb);
        }

        public static void EnsureTls12()
        {
            try
            {
                SecurityProtocolType proto = (SecurityProtocolType)3072;
                try
                {
                    SecurityProtocolType tls13 = (SecurityProtocolType)12288;
                    ServicePointManager.SecurityProtocol = proto | tls13;
                }
                catch
                {
                    ServicePointManager.SecurityProtocol = proto;
                }
            }
            catch { }
        }

        public static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }

        public static UpdateCheckResult CheckLatest(int timeoutMs)
        {
            Version cur = GetCurrentVersion();
            UpdateCheckResult res = new UpdateCheckResult();
            res.CurrentVersion = cur;
            res.HtmlUrl = ReleasesUrl;

            EnsureTls12();
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
                req.Method = "GET";
                req.UserAgent = "ONI-Trainer/" + cur.ToString();
                req.Accept = "application/vnd.github+json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    return ParseGitHubReleaseJson(json, cur);
                }
            }
            catch (WebException wex)
            {
                HttpWebResponse httpResp = wex.Response as HttpWebResponse;
                if (httpResp != null)
                {
                    HttpStatusCode code = httpResp.StatusCode;
                    httpResp.Close();
                    if (code == HttpStatusCode.NotFound)
                    {
                        res.Success = false;
                        res.Error = "仓库暂无已发布的正式版本。";
                        return res;
                    }
                    if (code == HttpStatusCode.Forbidden || (int)code == 429)
                    {
                        UpdateCheckResult fallback = TryCheckViaRedirect(cur, timeoutMs);
                        if (fallback.Success) return fallback;
                        res.Error = "GitHub API 访问受限，请稍后重试或点击「打开发布页」手动查看。";
                        return res;
                    }
                    UpdateCheckResult fb = TryCheckViaRedirect(cur, timeoutMs);
                    if (fb.Success) return fb;
                    res.Error = "服务器返回 HTTP " + (int)code + "。";
                    return res;
                }

                UpdateCheckResult redir = TryCheckViaRedirect(cur, timeoutMs);
                if (redir.Success) return redir;
                res.Error = "连接更新服务器失败：" + wex.Message;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = "检查更新失败：" + ex.Message;
                return res;
            }
        }

        static UpdateCheckResult TryCheckViaRedirect(Version cur, int timeoutMs)
        {
            UpdateCheckResult res = new UpdateCheckResult();
            res.CurrentVersion = cur;
            res.HtmlUrl = ReleasesUrl;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ReleasesUrl + "/latest");
                req.Method = "GET";
                req.UserAgent = "ONI-Trainer/" + cur.ToString();
                req.AllowAutoRedirect = false;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;

                string loc = null;
                HttpWebResponse resp = null;
                try
                {
                    resp = (HttpWebResponse)req.GetResponse();
                    loc = resp.Headers["Location"];
                }
                catch (WebException we)
                {
                    HttpWebResponse r2 = we.Response as HttpWebResponse;
                    if (r2 != null)
                    {
                        loc = r2.Headers["Location"];
                        r2.Close();
                    }
                }
                finally
                {
                    if (resp != null) resp.Close();
                }

                if (string.IsNullOrEmpty(loc))
                {
                    res.Error = "未获取到版本重定向信息。";
                    return res;
                }

                const string marker = "/releases/tag/";
                int idx = loc.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    res.Error = "仓库暂无已发布的正式版本。";
                    return res;
                }

                string tag = loc.Substring(idx + marker.Length).Trim();
                int q = tag.IndexOfAny(new char[] { '?', '#', '/' });
                if (q >= 0) tag = tag.Substring(0, q);

                Version latest = ParseVersion(tag);
                if (latest == null)
                {
                    res.Error = "无法解析版本标签：" + tag;
                    return res;
                }

                res.Success = true;
                res.LatestTag = tag;
                res.LatestVersion = latest;
                res.ReleaseName = tag;
                res.HtmlUrl = RepoUrl + "/releases/tag/" + Uri.EscapeDataString(tag);
                res.AssetName = "ONI-Trainer.exe";
                res.AssetDownloadUrl = RepoUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/ONI-Trainer.exe";
                res.ReleaseNotes = "（通过 GitHub 发布页回退获取版本号，完整更新说明请点击「打开发布页」查看。）";
                return res;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                return res;
            }
        }

        public static UpdateCheckResult ParseGitHubReleaseJson(string json, Version currentVersion)
        {
            UpdateCheckResult res = new UpdateCheckResult();
            res.CurrentVersion = NormalizeVersion(currentVersion ?? GetCurrentVersion());
            res.HtmlUrl = ReleasesUrl;

            if (string.IsNullOrEmpty(json))
            {
                res.Error = "服务器返回了空响应。";
                return res;
            }

            try
            {
                int i = 0;
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != '{')
                {
                    res.Error = "版本数据格式异常。";
                    return res;
                }
                i++;

                string bestExeName = null;
                string bestExeUrl = null;
                long bestExeSize = 0;

                while (i < json.Length)
                {
                    SkipWs(json, ref i);
                    if (i < json.Length && json[i] == '}') { i++; break; }
                    if (i < json.Length && json[i] == ',') { i++; continue; }
                    if (i >= json.Length || json[i] != '"') break;

                    string key = ReadJsonString(json, ref i);
                    SkipWs(json, ref i);
                    if (i < json.Length && json[i] == ':') i++;
                    SkipWs(json, ref i);

                    if (string.Equals(key, "tag_name", StringComparison.Ordinal))
                        res.LatestTag = ReadNullableJsonString(json, ref i);
                    else if (string.Equals(key, "name", StringComparison.Ordinal))
                        res.ReleaseName = ReadNullableJsonString(json, ref i);
                    else if (string.Equals(key, "html_url", StringComparison.Ordinal))
                    {
                        string u = ReadNullableJsonString(json, ref i);
                        if (!string.IsNullOrEmpty(u)) res.HtmlUrl = u;
                    }
                    else if (string.Equals(key, "body", StringComparison.Ordinal))
                        res.ReleaseNotes = ReadNullableJsonString(json, ref i);
                    else if (string.Equals(key, "published_at", StringComparison.Ordinal))
                        res.PublishedAt = ReadNullableJsonString(json, ref i);
                    else if (string.Equals(key, "assets", StringComparison.Ordinal) && i < json.Length && json[i] == '[')
                        ParseAssetsArray(json, ref i, out bestExeName, out bestExeUrl, out bestExeSize);
                    else
                        SkipJsonValue(json, ref i);
                }

                if (string.IsNullOrEmpty(res.LatestTag))
                {
                    res.Error = "未在返回数据中找到发布版本号（tag_name）。";
                    return res;
                }

                Version latest = ParseVersion(res.LatestTag);
                if (latest == null)
                {
                    res.Error = "无法解析版本号：" + res.LatestTag;
                    return res;
                }

                res.LatestVersion = latest;
                res.AssetName = bestExeName;
                res.AssetDownloadUrl = bestExeUrl;
                res.AssetSize = bestExeSize;
                if (string.IsNullOrEmpty(res.ReleaseName)) res.ReleaseName = res.LatestTag;
                res.Success = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = "解析更新信息失败：" + ex.Message;
                return res;
            }
        }

        static void ParseAssetsArray(string json, ref int i, out string chosenName, out string chosenUrl, out long chosenSize)
        {
            chosenName = null;
            chosenUrl = null;
            chosenSize = 0;

            if (i >= json.Length || json[i] != '[') { SkipJsonValue(json, ref i); return; }
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ']') { i++; break; }
                if (i < json.Length && json[i] == ',') { i++; continue; }
                if (i >= json.Length || json[i] != '{')
                {
                    SkipJsonValue(json, ref i);
                    continue;
                }

                i++;
                string aName = null;
                string aUrl = null;
                long aSize = 0;

                while (i < json.Length)
                {
                    SkipWs(json, ref i);
                    if (i < json.Length && json[i] == '}') { i++; break; }
                    if (i < json.Length && json[i] == ',') { i++; continue; }
                    if (i >= json.Length || json[i] != '"') break;

                    string k = ReadJsonString(json, ref i);
                    SkipWs(json, ref i);
                    if (i < json.Length && json[i] == ':') i++;
                    SkipWs(json, ref i);

                    if (string.Equals(k, "name", StringComparison.Ordinal))
                        aName = ReadNullableJsonString(json, ref i);
                    else if (string.Equals(k, "browser_download_url", StringComparison.Ordinal))
                        aUrl = ReadNullableJsonString(json, ref i);
                    else if (string.Equals(k, "size", StringComparison.Ordinal))
                        aSize = ReadJsonLong(json, ref i);
                    else
                        SkipJsonValue(json, ref i);
                }

                if (!string.IsNullOrEmpty(aName) && !string.IsNullOrEmpty(aUrl) &&
                    aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    bool exactTarget = string.Equals(aName, "ONI-Trainer.exe", StringComparison.OrdinalIgnoreCase);
                    bool haveExact = string.Equals(chosenName, "ONI-Trainer.exe", StringComparison.OrdinalIgnoreCase);
                    if (exactTarget || (!haveExact && chosenUrl == null))
                    {
                        chosenName = aName;
                        chosenUrl = aUrl;
                        chosenSize = aSize;
                    }
                }
            }
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        static string ReadNullableJsonString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '"') return ReadJsonString(s, ref i);
            SkipJsonValue(s, ref i);
            return null;
        }

        static long ReadJsonLong(string s, ref int i)
        {
            SkipWs(s, ref i);
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            long val;
            if (i > start && long.TryParse(s.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
                return val;
            if (i == start) SkipJsonValue(s, ref i);
            return 0;
        }

        static string ReadJsonString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return "";
            i++;
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i < s.Length)
                {
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                ushort code;
                                if (ushort.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        static void SkipJsonValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return;
            char c = s[i];
            if (c == '"')
            {
                ReadJsonString(s, ref i);
                return;
            }
            if (c == '{' || c == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char ch = s[i];
                    if (ch == '"')
                    {
                        ReadJsonString(s, ref i);
                        continue;
                    }
                    if (ch == '{' || ch == '[') depth++;
                    else if (ch == '}' || ch == ']')
                    {
                        depth--;
                        i++;
                        if (depth <= 0) break;
                        continue;
                    }
                    i++;
                }
                return;
            }
            while (i < s.Length)
            {
                char ch = s[i];
                if (ch == ',' || ch == '}' || ch == ']' || ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
                    break;
                i++;
            }
        }

        public static bool DownloadAndStageUpdate(
            string downloadUrl,
            long expectedSize,
            Action<long, long> onProgress,
            Func<bool> isCancelled,
            out string stagedPath,
            out string error)
        {
            stagedPath = null;
            error = null;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                error = "发布页未包含可执行文件下载地址。";
                return false;
            }

            string tmp = Path.Combine(Path.GetTempPath(), "ONI-Trainer-update-" + Process.GetCurrentProcess().Id + ".exe");
            EnsureTls12();
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(downloadUrl);
                req.Method = "GET";
                req.UserAgent = "ONI-Trainer/" + GetCurrentVersion().ToString();
                req.AllowAutoRedirect = true;
                req.Timeout = 15000;
                req.ReadWriteTimeout = 30000;

                long totalWritten = 0;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream net = resp.GetResponseStream())
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = expectedSize > 0 ? expectedSize : resp.ContentLength;
                    byte[] buf = new byte[32768];
                    int read;
                    while ((read = net.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (isCancelled != null && isCancelled())
                        {
                            error = "已取消下载。";
                            try { fs.Close(); } catch { }
                            try { File.Delete(tmp); } catch { }
                            return false;
                        }
                        fs.Write(buf, 0, read);
                        totalWritten += read;
                        if (onProgress != null) onProgress(totalWritten, total);
                    }
                }

                if (totalWritten < 65536 || (expectedSize > 0 && totalWritten != expectedSize))
                {
                    try { File.Delete(tmp); } catch { }
                    error = "下载文件大小校验失败，可能传输不完整。";
                    return false;
                }

                using (FileStream chk = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int b0 = chk.ReadByte();
                    int b1 = chk.ReadByte();
                    if (b0 != 0x4D || b1 != 0x5A)
                    {
                        try { chk.Close(); } catch { }
                        try { File.Delete(tmp); } catch { }
                        error = "下载内容不是有效的 Windows 可执行程序。";
                        return false;
                    }
                }

                stagedPath = tmp;
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                error = "下载更新失败：" + ex.Message;
                return false;
            }
        }

        static string EscapePsSingleQuoted(string s)
        {
            return (s ?? "").Replace("'", "''");
        }

        public static bool LaunchSelfReplaceScript(string stagedExePath, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrEmpty(stagedExePath) || !File.Exists(stagedExePath))
                {
                    error = "更新安装包不存在。";
                    return false;
                }

                string targetExe = Application.ExecutablePath;
                string targetDir = Path.GetDirectoryName(targetExe);
                int pid = Process.GetCurrentProcess().Id;

                string probe = Path.Combine(targetDir, ".oni_update_probe_" + pid);
                try
                {
                    File.WriteAllText(probe, "ok", Encoding.ASCII);
                    File.Delete(probe);
                }
                catch
                {
                    error = "当前目录无写入权限，无法自动替换程序。\r\n请右键「以管理员身份运行」或手动下载新版本覆盖。";
                    return false;
                }

                string psPath = Path.Combine(Path.GetTempPath(), "oni_trainer_apply_update_" + pid + ".ps1");
                StringBuilder ps = new StringBuilder();
                ps.AppendLine("$ErrorActionPreference = 'Stop'");
                ps.AppendLine("$oldPid = " + pid);
                ps.AppendLine("$staged = '" + EscapePsSingleQuoted(stagedExePath) + "'");
                ps.AppendLine("$target = '" + EscapePsSingleQuoted(targetExe) + "'");
                ps.AppendLine("$backup = $target + '.old'");
                ps.AppendLine("for ($i = 0; $i -lt 30; $i++) {");
                ps.AppendLine("    if (-not (Get-Process -Id $oldPid -ErrorAction SilentlyContinue)) { break }");
                ps.AppendLine("    Start-Sleep -Milliseconds 500");
                ps.AppendLine("}");
                ps.AppendLine("Start-Sleep -Milliseconds 250");
                ps.AppendLine("$done = $false");
                ps.AppendLine("for ($t = 0; $t -lt 10 -and -not $done; $t++) {");
                ps.AppendLine("    try {");
                ps.AppendLine("        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue }");
                ps.AppendLine("        if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup -Force }");
                ps.AppendLine("        try {");
                ps.AppendLine("            Copy-Item -LiteralPath $staged -Destination $target -Force");
                ps.AppendLine("            $done = $true");
                ps.AppendLine("        } catch {");
                ps.AppendLine("            if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $target)) {");
                ps.AppendLine("                Move-Item -LiteralPath $backup -Destination $target -Force -ErrorAction SilentlyContinue");
                ps.AppendLine("            }");
                ps.AppendLine("            throw");
                ps.AppendLine("        }");
                ps.AppendLine("    } catch {");
                ps.AppendLine("        Start-Sleep -Milliseconds 300");
                ps.AppendLine("    }");
                ps.AppendLine("}");
                ps.AppendLine("if ($done) {");
                ps.AppendLine("    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue");
                ps.AppendLine("    Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue");
                ps.AppendLine("    Start-Process -FilePath $target");
                ps.AppendLine("}");
                ps.AppendLine("Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue");

                File.WriteAllText(psPath, ps.ToString(), new UTF8Encoding(true));

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + psPath + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                error = "启动自动替换脚本失败：" + ex.Message;
                return false;
            }
        }
    }

    public class UpdateDialog : Form
    {
        const int BG = 0x16171C, CARD = 0x20222A, INPUT = 0x292C36, ACC = 0x00D68F, ACC_HOV = 0x1CE3A0;
        const int BTN = 0x323642, BTN_HOV = 0x3E4352, TXT = 0xECEEF2, SUB = 0x949AA8, WARN = 0xF2B138;

        readonly UpdateCheckResult _info;
        readonly Action _onRequestAppExit;
        readonly Font _fontReg = new Font("Microsoft YaHei UI", 9F);
        readonly Font _fontBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        ProgressBar _progress;
        Label _lblProgress;
        Button _btnUpdate;
        Button _btnCancel;
        Button _btnBrowser;
        volatile bool _downloading;
        volatile bool _cancelled;

        public bool UpdateStarted { get; private set; }

        public UpdateDialog(UpdateCheckResult info, Action onRequestAppExit)
        {
            _info = info;
            _onRequestAppExit = onRequestAppExit;

            Text = "发现新版本  ·  ONI 修改器";
            ClientSize = new Size(500, 390);
            MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = C(BG);
            Font = _fontReg;

            BuildUI();
            DpiHelper.ScaleLayout(this);
        }

        Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        void BuildUI()
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = C(CARD) };
            top.Controls.Add(new Label
            {
                Text = "发现新版本：" + (_info.LatestTag ?? ""),
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = C(ACC),
                Location = new Point(16, 8),
                Size = new Size(468, 24)
            });

            string sizeText = _info.AssetSize > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} MB", _info.AssetSize / (1024.0 * 1024.0))
                : "未知大小";
            string pubText = "";
            if (!string.IsNullOrEmpty(_info.PublishedAt) && _info.PublishedAt.Length >= 10)
                pubText = "  ·  发布于 " + _info.PublishedAt.Substring(0, 10);

            top.Controls.Add(new Label
            {
                Text = "当前版本 " + AppUpdater.FormatVersion(_info.CurrentVersion) + "  →  最新版本 " +
                       (_info.LatestTag ?? "") + pubText + "  ·  " + sizeText,
                Font = _fontReg,
                ForeColor = C(SUB),
                Location = new Point(16, 33),
                Size = new Size(468, 18)
            });

            Controls.Add(new Label
            {
                Text = "更新说明",
                ForeColor = C(TXT),
                Font = _fontBold,
                Location = new Point(16, 68),
                Size = new Size(120, 20)
            });

            string notes = string.IsNullOrEmpty(_info.ReleaseNotes)
                ? "（暂无文字说明，可点击下方「打开发布页」查看详情。）"
                : _info.ReleaseNotes.Replace("\r\n", "\n").Replace("\n", "\r\n");

            TextBox txtNotes = new TextBox
            {
                Text = notes,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = C(INPUT),
                ForeColor = C(TXT),
                BorderStyle = BorderStyle.FixedSingle,
                Font = _fontReg,
                Location = new Point(16, 92),
                Size = new Size(468, 184)
            };

            _progress = new ProgressBar
            {
                Location = new Point(16, 288),
                Size = new Size(468, 16),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            _lblProgress = new Label
            {
                Text = !string.IsNullOrEmpty(_info.AssetDownloadUrl)
                    ? "准备就绪：点击「立即下载并更新」将自动替换并重启修改器。"
                    : "未检测到直接 EXE 下载附件，请点击「打开发布页」手动下载。",
                ForeColor = string.IsNullOrEmpty(_info.AssetDownloadUrl) ? C(WARN) : C(SUB),
                Font = _fontReg,
                Location = new Point(16, 310),
                Size = new Size(468, 20)
            };

            _btnBrowser = new Button
            {
                Text = "打开发布页",
                Location = new Point(16, 342),
                Size = new Size(116, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(BTN),
                ForeColor = C(TXT),
                Font = _fontReg,
                Cursor = Cursors.Hand
            };
            _btnBrowser.FlatAppearance.BorderSize = 0;
            _btnBrowser.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
            _btnBrowser.Click += (s, e) => AppUpdater.OpenUrl(_info.HtmlUrl ?? AppUpdater.ReleasesUrl);

            _btnUpdate = new Button
            {
                Text = "立即下载并更新",
                Location = new Point(236, 342),
                Size = new Size(146, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(ACC),
                ForeColor = C(0x0E231C),
                Font = _fontBold,
                Cursor = Cursors.Hand,
                Enabled = !string.IsNullOrEmpty(_info.AssetDownloadUrl)
            };
            _btnUpdate.FlatAppearance.BorderSize = 0;
            _btnUpdate.FlatAppearance.MouseOverBackColor = C(ACC_HOV);
            _btnUpdate.Click += (s, e) => StartDownload();

            _btnCancel = new Button
            {
                Text = "稍后",
                Location = new Point(390, 342),
                Size = new Size(94, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = C(BTN),
                ForeColor = C(TXT),
                Font = _fontBold,
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderSize = 0;
            _btnCancel.FlatAppearance.MouseOverBackColor = C(BTN_HOV);
            _btnCancel.Click += (s, e) =>
            {
                if (_downloading)
                {
                    _cancelled = true;
                    _btnCancel.Enabled = false;
                    _lblProgress.Text = "正在取消下载…";
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };

            Controls.Add(txtNotes);
            Controls.Add(_progress);
            Controls.Add(_lblProgress);
            Controls.Add(_btnBrowser);
            Controls.Add(_btnUpdate);
            Controls.Add(_btnCancel);
            Controls.Add(top);
        }

        void StartDownload()
        {
            if (_downloading || string.IsNullOrEmpty(_info.AssetDownloadUrl)) return;
            _downloading = true;
            _cancelled = false;
            _btnUpdate.Enabled = false;
            _btnCancel.Text = "取消下载";
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            _lblProgress.ForeColor = C(TXT);
            _lblProgress.Text = "正在连接 GitHub 下载新版本…";

            Thread t = new Thread(() =>
            {
                string staged;
                string err;
                bool ok = AppUpdater.DownloadAndStageUpdate(
                    _info.AssetDownloadUrl,
                    _info.AssetSize,
                    (done, total) =>
                    {
                        if (IsDisposed || !IsHandleCreated) return;
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (IsDisposed) return;
                                if (total > 0)
                                {
                                    int pct = (int)Math.Min(100, Math.Max(0, (done * 100L) / total));
                                    _progress.Style = ProgressBarStyle.Continuous;
                                    _progress.Value = pct;
                                    _lblProgress.Text = string.Format(
                                        CultureInfo.InvariantCulture,
                                        "正在下载：{0:0.00} MB / {1:0.00} MB（{2}%）",
                                        done / (1024.0 * 1024.0), total / (1024.0 * 1024.0), pct);
                                }
                                else
                                {
                                    _progress.Style = ProgressBarStyle.Marquee;
                                    _lblProgress.Text = string.Format(
                                        CultureInfo.InvariantCulture,
                                        "正在下载：{0:0.00} MB…", done / (1024.0 * 1024.0));
                                }
                            }));
                        }
                        catch { }
                    },
                    () => _cancelled,
                    out staged,
                    out err);

                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed) return;
                        _downloading = false;
                        _progress.Style = ProgressBarStyle.Continuous;
                        if (!ok)
                        {
                            _btnUpdate.Enabled = !string.IsNullOrEmpty(_info.AssetDownloadUrl);
                            _btnCancel.Enabled = true;
                            _btnCancel.Text = "关闭";
                            _lblProgress.ForeColor = C(WARN);
                            _lblProgress.Text = err ?? "下载失败。";
                            if (!_cancelled)
                                MessageBox.Show(this, err ?? "下载失败。", "自动更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        string launchErr;
                        if (!AppUpdater.LaunchSelfReplaceScript(staged, out launchErr))
                        {
                            _btnUpdate.Enabled = true;
                            _btnCancel.Enabled = true;
                            _btnCancel.Text = "关闭";
                            _lblProgress.ForeColor = C(WARN);
                            _lblProgress.Text = launchErr ?? "无法启动替换脚本。";
                            MessageBox.Show(this, launchErr ?? "无法启动替换脚本。", "自动更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        UpdateStarted = true;
                        _progress.Value = 100;
                        _lblProgress.ForeColor = C(ACC);
                        _lblProgress.Text = "下载完成，正在退出并替换为新版本…";
                        DialogResult = DialogResult.OK;
                        Close();
                        if (_onRequestAppExit != null)
                            _onRequestAppExit();
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_downloading && !UpdateStarted)
                _cancelled = true;
            base.OnFormClosing(e);
        }
    }
}
