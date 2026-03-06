using System;
using System.Collections.Concurrent;
using System.Net;
using System.Web.Mvc;

namespace Vidly.Filters
{
    /// <summary>
    /// Action filter that rate-limits requests per client IP using a sliding
    /// window algorithm.  Apply to controllers or actions that handle
    /// sensitive operations (gift card balance checks, coupon validation,
    /// login attempts) to prevent brute-force attacks and enumeration.
    ///
    /// <para><b>Usage:</b></para>
    /// <code>
    ///   [RateLimit(MaxRequests = 10, WindowSeconds = 60)]
    ///   public ActionResult CheckBalance(string code) { ... }
    /// </code>
    ///
    /// <para>
    /// Returns HTTP 429 Too Many Requests when the limit is exceeded,
    /// with a Retry-After header indicating when the client can retry.
    /// </para>
    ///
    /// <para><b>Thread safety:</b> Uses <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// and lock-free atomic operations for safe concurrent access under
    /// IIS thread pool contention.</para>
    /// </summary>
    public class RateLimitAttribute : ActionFilterAttribute
    {
        /// <summary>Maximum requests allowed per window per client IP.</summary>
        public int MaxRequests { get; set; } = 30;

        /// <summary>Sliding window duration in seconds.</summary>
        public int WindowSeconds { get; set; } = 60;

        /// <summary>Custom message returned to rate-limited clients.</summary>
        public string Message { get; set; } = "Too many requests. Please try again later.";

        // Shared state across all instances with the same window configuration.
        // Key: "IP|MaxReq|Window" to isolate limits per attribute config.
        private static readonly ConcurrentDictionary<string, ClientWindow> _windows
            = new ConcurrentDictionary<string, ClientWindow>();

        // Periodic cleanup counter to prevent unbounded memory growth
        private static long _requestCounter;
        private const long CleanupInterval = 1000;

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var clientIp = GetClientIp(filterContext);
            var windowKey = $"{clientIp}|{MaxRequests}|{WindowSeconds}";
            var now = DateTimeOffset.UtcNow;
            var windowSpan = TimeSpan.FromSeconds(WindowSeconds);

            var window = _windows.GetOrAdd(windowKey, _ => new ClientWindow(now));

            lock (window)
            {
                // Slide the window: remove expired entries
                var cutoff = now - windowSpan;
                while (window.Timestamps.Count > 0 && window.Timestamps.Peek() < cutoff)
                {
                    window.Timestamps.Dequeue();
                }

                if (window.Timestamps.Count >= MaxRequests)
                {
                    // Rate limited — calculate retry-after from oldest entry in window
                    var oldest = window.Timestamps.Peek();
                    var retryAfter = (int)Math.Ceiling((oldest + windowSpan - now).TotalSeconds);
                    if (retryAfter < 1) retryAfter = 1;

                    filterContext.HttpContext.Response.StatusCode = 429;
                    filterContext.HttpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
                    filterContext.HttpContext.Response.Headers["X-RateLimit-Limit"] = MaxRequests.ToString();
                    filterContext.HttpContext.Response.Headers["X-RateLimit-Remaining"] = "0";
                    filterContext.Result = new HttpStatusCodeResult(
                        (HttpStatusCode)429, Message);
                    return;
                }

                window.Timestamps.Enqueue(now);
            }

            // Set rate limit headers on successful requests
            var remaining = MaxRequests - _windows.GetOrAdd(windowKey, _ => new ClientWindow(now)).Timestamps.Count;
            filterContext.HttpContext.Response.Headers["X-RateLimit-Limit"] = MaxRequests.ToString();
            filterContext.HttpContext.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, remaining).ToString();

            // Periodic cleanup of expired entries to prevent memory leaks
            var count = System.Threading.Interlocked.Increment(ref _requestCounter);
            if (count % CleanupInterval == 0)
            {
                CleanupExpiredWindows();
            }

            base.OnActionExecuting(filterContext);
        }

        /// <summary>
        /// Extracts the client IP address, respecting X-Forwarded-For
        /// for reverse proxy deployments (Azure App Service, IIS ARR, nginx).
        /// Only trusts the rightmost IP in X-Forwarded-For (closest to the
        /// server) to prevent spoofing via attacker-controlled headers.
        /// </summary>
        private static string GetClientIp(ActionExecutingContext filterContext)
        {
            var xff = filterContext.HttpContext.Request.Headers["X-Forwarded-For"];
            if (!string.IsNullOrEmpty(xff))
            {
                // Take the rightmost entry — it's the one added by the trusted
                // reverse proxy closest to the server.  Left entries can be
                // spoofed by the client.
                var parts = xff.Split(',');
                var ip = parts[parts.Length - 1].Trim();
                if (!string.IsNullOrEmpty(ip))
                    return ip;
            }

            return filterContext.HttpContext.Request.UserHostAddress ?? "unknown";
        }

        /// <summary>
        /// Removes window entries that haven't been updated in 2× the window
        /// duration to prevent unbounded memory growth from unique IPs.
        /// </summary>
        private void CleanupExpiredWindows()
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(WindowSeconds * 2);
            foreach (var kvp in _windows)
            {
                lock (kvp.Value)
                {
                    if (kvp.Value.Timestamps.Count == 0 ||
                        kvp.Value.Timestamps.Peek() < cutoff)
                    {
                        _windows.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Clears all rate limit state.  Intended for testing only.
        /// </summary>
        internal static void ResetAllWindows()
        {
            _windows.Clear();
            _requestCounter = 0;
        }

        /// <summary>
        /// Sliding window state for a single client IP.
        /// </summary>
        private class ClientWindow
        {
            /// <summary>Queue of request timestamps within the current window.</summary>
            public readonly System.Collections.Generic.Queue<DateTimeOffset> Timestamps
                = new System.Collections.Generic.Queue<DateTimeOffset>();

            public ClientWindow(DateTimeOffset firstRequest)
            {
                Timestamps.Enqueue(firstRequest);
            }
        }
    }
}
