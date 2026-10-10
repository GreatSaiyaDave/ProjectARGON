using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WRLDZ.Core
{
    /// <summary>
    /// Desktop Google sign-in. Opens the browser, catches the loopback redirect, and
    /// returns the verified subject. The client id (and, for a "Desktop app" OAuth client, the client
    /// secret Google requires at the token exchange even with PKCE) come from the environment
    /// (WRLDZ_GOOGLE_CLIENT_ID / WRLDZ_GOOGLE_CLIENT_SECRET) or from the local, git-ignored
    /// StreamingAssets/WRLDZ/Auth/google_oauth.json: {"client_id": "...", "client_secret": "..."}.
    /// Never commit that file. Google treats an installed-app secret as non-confidential, but it still
    /// does not belong in the repository.
    /// </summary>
    public static class GoogleSignIn
    {
        public const int Port = 8765;
        public const string MissingClientMessage =
            "Sign in with Google needs a desktop client id in StreamingAssets/WRLDZ/Auth/google_oauth.json.";

        public class Profile
        {
            public string Sub;
            public string Email;
            public string Name;
        }

        const string ClientFileRelative = "WRLDZ/Auth/google_oauth.json";

        static HttpListener _listener;
        static volatile bool _cancel;
        static int _inFlight;

        /// <summary>True while a <see cref="SignIn"/> is waiting on the browser.</summary>
        public static bool InProgress => Volatile.Read(ref _inFlight) != 0;

        public static bool TryLoadClientId(out string clientId, out string error)
        {
            clientId = Environment.GetEnvironmentVariable("WRLDZ_GOOGLE_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                clientId = clientId.Trim();
                error = null;
                return true;
            }

            const string path = ClientFileRelative;
            if (!StreamingFiles.Exists(path))
            {
                error = MissingClientMessage;
                return false;
            }

            try
            {
                var file = JsonUtility.FromJson<ClientFile>(StreamingFiles.ReadAllText(path));
                clientId = file != null ? file.client_id : null;
            }
            catch (Exception ex)
            {
                error = "Could not read the Google client file. " + ex.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                error = MissingClientMessage;
                return false;
            }

            clientId = clientId.Trim();
            error = null;
            return true;
        }

        public static void Cancel()
        {
            _cancel = true;
            try { _listener?.Stop(); }
            catch { /* already closed */ }
        }

        /// <summary>
        /// Optional client secret: WRLDZ_GOOGLE_CLIENT_SECRET, else "client_secret" in the local client file.
        /// Null when neither is set (Web/iOS/Android clients have none). Never logged.
        /// </summary>
        static string LoadClientSecret()
        {
            var secret = Environment.GetEnvironmentVariable("WRLDZ_GOOGLE_CLIENT_SECRET");
            if (!string.IsNullOrWhiteSpace(secret))
                return secret.Trim();
            try
            {
                if (!StreamingFiles.TryReadAllText(ClientFileRelative, out var json)) return null;
                var file = JsonUtility.FromJson<ClientFile>(json);
                return string.IsNullOrWhiteSpace(file?.client_secret) ? null : file.client_secret.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Blocks. Call off the Unity main thread. Only one sign-in runs at a time: a second call while the
        /// first waits on the browser returns null with an error instead of fighting over the loopback port.
        /// </summary>
        public static Profile SignIn(string clientId, out string error)
        {
            error = null;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            {
                error = "Google sign-in is already open in your browser.";
                return null;
            }

            try
            {
                return SignInCore(clientId, out error);
            }
            finally
            {
                Volatile.Write(ref _inFlight, 0);
            }
        }

        static Profile SignInCore(string clientId, out string error)
        {
            error = null;
            _cancel = false;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                error = MissingClientMessage;
                return null;
            }

            var verifier = Base64Url(RandomBytes(32));
            byte[] challengeBytes;
            using (var sha = SHA256.Create())
                challengeBytes = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
            var challenge = Base64Url(challengeBytes);
            var state = Base64Url(RandomBytes(16));
            var redirect = "http://127.0.0.1:" + Port + "/callback";
            var auth = "https://accounts.google.com/o/oauth2/v2/auth"
                       + "?client_id=" + Uri.EscapeDataString(clientId)
                       + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                       + "&response_type=code"
                       + "&scope=" + Uri.EscapeDataString("openid email profile")
                       + "&code_challenge=" + challenge
                       + "&code_challenge_method=S256"
                       + "&state=" + state
                       + "&prompt=select_account";

            HttpListener listener = null;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
                listener.Start();
                _listener = listener;

                Process.Start(new ProcessStartInfo
                {
                    FileName = auth,
                    UseShellExecute = true
                });

                var pending = listener.BeginGetContext(null, null);
                if (!pending.AsyncWaitHandle.WaitOne(120000) || _cancel)
                {
                    error = _cancel ? "Google sign-in cancelled." : "Google sign-in timed out.";
                    return null;
                }

                var ctx = listener.EndGetContext(pending);
                if (_cancel)
                {
                    error = "Google sign-in cancelled.";
                    return null;
                }

                var query = Query(ctx.Request.Url != null ? ctx.Request.Url.Query : "");
                Profile profile = null;
                try
                {
                    if (_cancel)
                        error = "Google sign-in cancelled.";
                    else if (!query.TryGetValue("state", out var got) || got != state)
                        error = "Google sign-in state did not match.";
                    else if (query.TryGetValue("error", out var oauthErr))
                        error = oauthErr == "access_denied"
                            ? "Google sign-in was declined."
                            : "Google sign-in failed (" + oauthErr + ").";
                    else if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                        error = "Google did not return a sign-in code.";
                    else
                    {
                        try
                        {
                            var token = Exchange(clientId, code, verifier, redirect, out error);
                            if (!string.IsNullOrEmpty(token))
                                profile = UserInfo(token, out error);
                        }
                        catch (WebException ex) when (!_cancel)
                        {
                            error = DescribeWebError(ex);
                            Debug.LogWarning("[WRLDZ] Google sign-in: " + error);
                        }
                    }
                }
                finally
                {
                    // The browser page reports the real outcome, written only after validation + exchange.
                    WriteClosePage(ctx, profile != null, error);
                }

                return profile;
            }
            catch (WebException ex)
            {
                if (_cancel)
                {
                    error = "Google sign-in cancelled.";
                    return null;
                }

                error = DescribeWebError(ex);
                Debug.LogWarning("[WRLDZ] Google sign-in: " + error);
                return null;
            }
            catch (Exception ex)
            {
                if (_cancel)
                {
                    error = "Google sign-in cancelled.";
                    return null;
                }

                Debug.LogWarning("[WRLDZ] Google sign-in: " + ex.Message);
                error = "Could not reach Google. " + ex.Message;
                return null;
            }
            finally
            {
                try { listener?.Close(); }
                catch { /* ignore */ }
                if (ReferenceEquals(_listener, listener))
                    _listener = null;
            }
        }

        static string Exchange(string clientId, string code, string verifier, string redirect, out string error)
        {
            error = null;
            var body = "code=" + Uri.EscapeDataString(code)
                       + "&client_id=" + Uri.EscapeDataString(clientId)
                       + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                       + "&grant_type=authorization_code"
                       + "&code_verifier=" + Uri.EscapeDataString(verifier);
            // Desktop OAuth clients must send their secret at the exchange even with PKCE (else 400).
            var secret = LoadClientSecret();
            if (!string.IsNullOrEmpty(secret))
                body += "&client_secret=" + Uri.EscapeDataString(secret);
            var json = Post("https://oauth2.googleapis.com/token", body, null);
            var token = JsonUtility.FromJson<TokenBody>(json);
            if (token == null || string.IsNullOrEmpty(token.access_token))
            {
                error = "Google did not return a token.";
                return null;
            }

            return token.access_token;
        }

        static Profile UserInfo(string accessToken, out string error)
        {
            error = null;
            var json = Post("https://www.googleapis.com/oauth2/v3/userinfo", null, accessToken);
            var info = JsonUtility.FromJson<UserBody>(json);
            if (info == null || string.IsNullOrEmpty(info.sub))
            {
                error = "Google did not return an account id.";
                return null;
            }

            // Only a Google-verified address is kept; it is informational and never used to pick an account.
            var email = info.email_verified ? info.email ?? "" : "";
            return new Profile { Sub = info.sub, Email = email, Name = info.name ?? "" };
        }

        /// <summary>Google's JSON error body ("error" / "error_description") when there is one.</summary>
        static string DescribeWebError(WebException ex)
        {
            try
            {
                if (ex.Response is HttpWebResponse res)
                {
                    using (res)
                    using (var reader = new StreamReader(res.GetResponseStream() ?? Stream.Null))
                    {
                        var body = reader.ReadToEnd();
                        var err = string.IsNullOrEmpty(body) ? null : JsonUtility.FromJson<ErrorBody>(body);
                        if (err != null && !string.IsNullOrEmpty(err.error))
                        {
                            var hint = err.error == "invalid_client" || err.error == "invalid_request"
                                ? " Check the client id / secret in the local google_oauth.json."
                                : "";
                            return $"Google refused sign-in ({(int)res.StatusCode} {err.error}" +
                                   (string.IsNullOrEmpty(err.error_description) ? "" : ": " + err.error_description) +
                                   ")." + hint;
                        }

                        return $"Google refused sign-in (HTTP {(int)res.StatusCode}).";
                    }
                }
            }
            catch
            {
                /* fall through to the transport message */
            }

            return "Could not reach Google. " + ex.Message;
        }

        static string Post(string url, string form, string bearer)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = string.IsNullOrEmpty(form) ? "GET" : "POST";
            req.Timeout = 20000;
            if (!string.IsNullOrEmpty(bearer))
                req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            if (!string.IsNullOrEmpty(form))
            {
                var bytes = Encoding.UTF8.GetBytes(form);
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = bytes.Length;
                using var stream = req.GetRequestStream();
                stream.Write(bytes, 0, bytes.Length);
            }

            using var res = (HttpWebResponse)req.GetResponse();
            using var reader = new StreamReader(res.GetResponseStream() ?? Stream.Null);
            return reader.ReadToEnd();
        }

        static void WriteClosePage(HttpListenerContext ctx, bool ok, string error)
        {
            try
            {
                var msg = ok
                    ? "Signed in. You can close this window and return to Project ARGON."
                    : "Sign-in did not complete: " + WebUtility.HtmlEncode(error ?? "unknown error") +
                      " Return to Project ARGON and try again.";
                var html = "<html><body style=\"font-family:sans-serif;background:#0c1220;color:#faf8f4;padding:2rem\">"
                           + "<p>" + msg + "</p></body></html>";
                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.StatusCode = ok ? 200 : 400;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch
            {
                /* the browser tab may already be gone */
            }
        }

        static Dictionary<string, string> Query(string raw)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw)) return dict;
            var q = raw.StartsWith("?") ? raw.Substring(1) : raw;
            foreach (var part in q.Split('&'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                var cut = part.IndexOf('=');
                if (cut <= 0) continue;
                var key = Uri.UnescapeDataString(part.Substring(0, cut));
                var val = Uri.UnescapeDataString(part.Substring(cut + 1));
                dict[key] = val;
            }

            return dict;
        }

        static byte[] RandomBytes(int count)
        {
            var buf = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(buf);
            return buf;
        }

        static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        [Serializable]
        class ClientFile
        {
            public string client_id;
            public string client_secret;
        }

        [Serializable]
        class ErrorBody
        {
            public string error;
            public string error_description;
        }

        [Serializable]
        class TokenBody
        {
            public string access_token;
        }

        [Serializable]
        class UserBody
        {
            public string sub;
            public string email;
            public bool email_verified;
            public string name;
        }
    }
}
