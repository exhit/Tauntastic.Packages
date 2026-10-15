using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Checks the saved GitHub token against the GitHub API and reports what GitHub says: whether it is accepted, the rate limit it gets, and when it expires. Also describes the token without revealing it, so you can compare it with the one on GitHub.</summary>
    public static class GitHubTokenTester
    {
        private static UnityWebRequest _request;
        private static bool _polling;
        private static string _testedToken = string.Empty;
        private static string _note = string.Empty;

        /// <summary>True while the test request is running.</summary>
        public static bool IsRunning
        {
            get { return _request != null; }
        }

        /// <summary>The outcome of the last test, or an empty string when none was run.</summary>
        public static string Result { get; private set; }

        /// <summary>True when the last test ended with the token accepted.</summary>
        public static bool LastTestAccepted { get; private set; }

        /// <summary>Raised on the main thread when the test finishes.</summary>
        public static event Action Finished;

        /// <summary>Describes a token without revealing it: length, prefix kind and the last 4 characters, and warns about characters that do not belong in a token.</summary>
        public static string Describe(string token)
        {
            if (string.IsNullOrEmpty(token))
                return "No token is saved.";

            string kind = token.StartsWith("github_pat_", StringComparison.Ordinal) ? "fine-grained token (github_pat_...)"
                : token.StartsWith("ghp_", StringComparison.Ordinal) ? "classic token (ghp_...)"
                : "unknown kind (it should start with github_pat_ or ghp_)";

            string last = token.Length > 4 ? token.Substring(token.Length - 4) : token;
            string text = "Saved token: " + token.Length + " characters, " + kind + ", ends with \"..." + last + "\".";

            foreach (char c in token)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_') || c > 127)
                {
                    text += " It contains a character that does not belong in a token (a space, line break or symbol): paste it again.";
                    break;
                }
            }

            return text;
        }

        /// <summary>Sends one request to the rate limit endpoint with the saved token.</summary>
        public static void Test()
        {
            Start(PackageUpdateSettings.GitHubToken, "Testing the saved token. ");
        }

        /// <summary>
        /// Tests the text on the clipboard instead of the saved token, without saving it, and says whether it is identical to the saved token.
        /// Copy the token with the copy icon on GitHub, then use this: if the clipboard text is accepted but the saved one is not, the saving
        /// went wrong; if both are rejected, GitHub does not accept the token itself.
        /// </summary>
        public static void TestClipboard()
        {
            string clipboard = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
            string saved = PackageUpdateSettings.GitHubToken;

            string comparison;
            if (string.IsNullOrEmpty(clipboard))
            {
                comparison = "The clipboard is empty. ";
            }
            else if (string.IsNullOrEmpty(saved))
            {
                comparison = "No token is saved yet. ";
            }
            else if (string.Equals(clipboard, saved, StringComparison.Ordinal))
            {
                comparison = "The clipboard text is IDENTICAL to the saved token. ";
            }
            else
            {
                int index = 0;
                while (index < clipboard.Length && index < saved.Length && clipboard[index] == saved[index])
                    index++;

                comparison = "The clipboard text DIFFERS from the saved token (" + clipboard.Length + " vs " + saved.Length +
                             " characters, first difference at position " + (index + 1) + "). ";
            }

            Start(clipboard, comparison + "Testing the clipboard text. ");
        }

        private static void Start(string token, string note)
        {
            if (IsRunning)
                return;

            _testedToken = token ?? string.Empty;
            _note = note ?? string.Empty;
            Result = string.Empty;
            LastTestAccepted = false;

            if (string.IsNullOrEmpty(token))
            {
                Result = _note + Describe(token);
                RaiseFinished();
                return;
            }

            _request = UnityWebRequest.Get("https://api.github.com/rate_limit");
            _request.timeout = 15;
            _request.SetRequestHeader("Authorization", "Bearer " + _testedToken);
            _request.SetRequestHeader("Accept", "application/vnd.github+json");
            _request.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
            _request.SendWebRequest();

            if (!_polling)
            {
                _polling = true;
                EditorApplication.update += Poll;
            }
        }

        private static void Poll()
        {
            if (_request == null || !_request.isDone)
                return;

            UnityWebRequest request = _request;
            _request = null;
            EditorApplication.update -= Poll;
            _polling = false;

            string describe = _note + Describe(_testedToken);
            long code = request.responseCode;

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                Result = "Could not reach GitHub: " + request.error + "\n" + describe;
            }
            else if (code == 200)
            {
                LastTestAccepted = true;
                string expiry = request.GetResponseHeader("github-authentication-token-expiration");
                string limit = ReadCoreLimit(request.downloadHandler.text);
                Result = "GitHub accepts this token (HTTP 200)." +
                         (string.IsNullOrEmpty(limit) ? string.Empty : " Rate limit: " + limit + " requests per hour.") +
                         (string.IsNullOrEmpty(expiry) ? " No expiry date reported." : " Expires: " + expiry + ".") +
                         "\n" + describe;
            }
            else
            {
                Result = "GitHub rejected this token (HTTP " + code + "). " + describe;
            }

            request.Dispose();
            RaiseFinished();
        }

        private static string ReadCoreLimit(string json)
        {
            object root;
            if (!SimpleJson.TryParse(json, out root))
                return string.Empty;

            var dictionary = root as System.Collections.Generic.Dictionary<string, object>;
            System.Collections.Generic.Dictionary<string, object> resources = SimpleJson.GetObject(dictionary, "resources");
            System.Collections.Generic.Dictionary<string, object> core = SimpleJson.GetObject(resources, "core");
            object limit;
            if (core != null && core.TryGetValue("limit", out limit) && limit is double)
                return ((int)(double)limit).ToString();
            return string.Empty;
        }

        private static void RaiseFinished()
        {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Action handler = Finished;
            if (handler != null)
                handler();
        }
    }
}
