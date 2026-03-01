using System.Web.Mvc;

namespace Vidly.Filters
{
    /// <summary>
    /// Action filter that adds security headers to every HTTP response.
    /// Defense-in-depth layer complementing Web.config &lt;customHeaders&gt;.
    /// Useful when the app is hosted behind a reverse proxy that strips
    /// IIS-level headers or when running under IIS Express during development.
    /// </summary>
    public class SecurityHeadersAttribute : ActionFilterAttribute
    {
        public override void OnResultExecuted(ResultExecutedContext filterContext)
        {
            var response = filterContext.HttpContext.Response;

            // Prevent MIME-type sniffing
            SetHeaderIfMissing(response, "X-Content-Type-Options", "nosniff");

            // Prevent clickjacking
            SetHeaderIfMissing(response, "X-Frame-Options", "DENY");

            // Enable browser XSS filter (legacy browsers)
            SetHeaderIfMissing(response, "X-XSS-Protection", "1; mode=block");

            // Control referrer information leakage
            SetHeaderIfMissing(response, "Referrer-Policy", "strict-origin-when-cross-origin");

            // Restrict browser feature access
            SetHeaderIfMissing(response, "Permissions-Policy", "camera=(), microphone=(), geolocation=()");

            // HTTP Strict Transport Security — force HTTPS for 1 year, include subdomains
            if (filterContext.HttpContext.Request.IsSecureConnection)
            {
                SetHeaderIfMissing(response, "Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            }

            // Content Security Policy — allow self-hosted resources plus CDN for Bootstrap/jQuery
            SetHeaderIfMissing(response, "Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
                "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://fonts.googleapis.com; " +
                "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
                "img-src 'self' data:; " +
                "frame-ancestors 'none'");

            // Prevent browsers from caching sensitive pages (customer data, rentals)
            // Shared/public caches and browser back-button could otherwise expose
            // private data to the next user on a shared computer.
            SetHeaderIfMissing(response, "Cache-Control", "no-store, no-cache, must-revalidate, private");
            SetHeaderIfMissing(response, "Pragma", "no-cache");

            // Remove server identification headers
            response.Headers.Remove("Server");
            response.Headers.Remove("X-Powered-By");
            response.Headers.Remove("X-AspNet-Version");
            response.Headers.Remove("X-AspNetMvc-Version");

            base.OnResultExecuted(filterContext);
        }

        private static void SetHeaderIfMissing(System.Web.HttpResponseBase response, string name, string value)
        {
            if (string.IsNullOrEmpty(response.Headers[name]))
            {
                response.Headers.Set(name, value);
            }
        }
    }
}
