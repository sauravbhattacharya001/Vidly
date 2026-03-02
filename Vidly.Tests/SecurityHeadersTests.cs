using System;
using System.Collections.Specialized;
using System.IO;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Filters;

namespace Vidly.Tests
{
    [TestClass]
    public class SecurityHeadersAttributeTests
    {
        private SecurityHeadersAttribute _filter;

        [TestInitialize]
        public void Setup()
        {
            _filter = new SecurityHeadersAttribute();
        }

        private ResultExecutedContext CreateContext(bool isSecure = false, NameValueCollection existingHeaders = null)
        {
            var headers = existingHeaders ?? new NameValueCollection();
            var response = new FakeHttpResponse(headers);
            var request = new FakeHttpRequest(isSecure);
            var httpContext = new FakeHttpContext(request, response);
            var routeData = new RouteData();
            var controller = new FakeController();
            var controllerContext = new ControllerContext(httpContext, routeData, controller);
            var actionDescriptor = new ReflectedActionDescriptor(
                typeof(FakeController).GetMethod("Index"),
                "Index",
                new ReflectedControllerDescriptor(typeof(FakeController)));

            return new ResultExecutedContext(
                controllerContext,
                new EmptyResult(),
                false,
                null);
        }

        // ---- Security headers are added ----

        [TestMethod]
        public void OnResultExecuted_SetsXContentTypeOptions()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            var response = context.HttpContext.Response;
            Assert.AreEqual("nosniff", response.Headers["X-Content-Type-Options"]);
        }

        [TestMethod]
        public void OnResultExecuted_SetsXFrameOptions()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            Assert.AreEqual("DENY", context.HttpContext.Response.Headers["X-Frame-Options"]);
        }

        [TestMethod]
        public void OnResultExecuted_SetsXXSSProtection()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            Assert.AreEqual("1; mode=block",
                context.HttpContext.Response.Headers["X-XSS-Protection"]);
        }

        [TestMethod]
        public void OnResultExecuted_SetsReferrerPolicy()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            Assert.AreEqual("strict-origin-when-cross-origin",
                context.HttpContext.Response.Headers["Referrer-Policy"]);
        }

        [TestMethod]
        public void OnResultExecuted_SetsPermissionsPolicy()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            var value = context.HttpContext.Response.Headers["Permissions-Policy"];
            Assert.IsNotNull(value);
            Assert.IsTrue(value.Contains("camera=()"), "Should restrict camera.");
            Assert.IsTrue(value.Contains("microphone=()"), "Should restrict microphone.");
            Assert.IsTrue(value.Contains("geolocation=()"), "Should restrict geolocation.");
        }

        [TestMethod]
        public void OnResultExecuted_SetsCSP()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            var csp = context.HttpContext.Response.Headers["Content-Security-Policy"];
            Assert.IsNotNull(csp, "CSP header should be set.");
            Assert.IsTrue(csp.Contains("default-src 'self'"));
            Assert.IsTrue(csp.Contains("frame-ancestors 'none'"));
        }

        // ---- HSTS only on secure connections ----

        [TestMethod]
        public void OnResultExecuted_HTTP_NoHSTS()
        {
            var context = CreateContext(isSecure: false);
            _filter.OnResultExecuted(context);

            Assert.IsNull(context.HttpContext.Response.Headers["Strict-Transport-Security"],
                "HSTS should not be set for non-secure connections.");
        }

        [TestMethod]
        public void OnResultExecuted_HTTPS_SetsHSTS()
        {
            var context = CreateContext(isSecure: true);
            _filter.OnResultExecuted(context);

            var hsts = context.HttpContext.Response.Headers["Strict-Transport-Security"];
            Assert.IsNotNull(hsts, "HSTS should be set for secure connections.");
            Assert.IsTrue(hsts.Contains("max-age=31536000"), "HSTS max-age should be 1 year.");
            Assert.IsTrue(hsts.Contains("includeSubDomains"), "HSTS should include subdomains.");
        }

        // ---- Does not overwrite existing headers ----

        [TestMethod]
        public void OnResultExecuted_ExistingHeader_NotOverwritten()
        {
            var existingHeaders = new NameValueCollection
            {
                { "X-Frame-Options", "SAMEORIGIN" }
            };
            var context = CreateContext(existingHeaders: existingHeaders);

            _filter.OnResultExecuted(context);

            Assert.AreEqual("SAMEORIGIN",
                context.HttpContext.Response.Headers["X-Frame-Options"],
                "Existing X-Frame-Options should not be overwritten.");
        }

        [TestMethod]
        public void OnResultExecuted_ExistingCSP_NotOverwritten()
        {
            var customCSP = "default-src 'none'";
            var existingHeaders = new NameValueCollection
            {
                { "Content-Security-Policy", customCSP }
            };
            var context = CreateContext(existingHeaders: existingHeaders);

            _filter.OnResultExecuted(context);

            Assert.AreEqual(customCSP,
                context.HttpContext.Response.Headers["Content-Security-Policy"],
                "Existing CSP should not be overwritten.");
        }

        // ---- Removes server identity headers ----

        [TestMethod]
        public void OnResultExecuted_RemovesServerHeader()
        {
            var existing = new NameValueCollection { { "Server", "Microsoft-IIS/10.0" } };
            var context = CreateContext(existingHeaders: existing);

            _filter.OnResultExecuted(context);

            Assert.IsNull(context.HttpContext.Response.Headers["Server"],
                "Server header should be removed.");
        }

        [TestMethod]
        public void OnResultExecuted_RemovesXPoweredBy()
        {
            var existing = new NameValueCollection { { "X-Powered-By", "ASP.NET" } };
            var context = CreateContext(existingHeaders: existing);

            _filter.OnResultExecuted(context);

            Assert.IsNull(context.HttpContext.Response.Headers["X-Powered-By"],
                "X-Powered-By header should be removed.");
        }

        [TestMethod]
        public void OnResultExecuted_RemovesXAspNetVersion()
        {
            var existing = new NameValueCollection { { "X-AspNet-Version", "4.5.2" } };
            var context = CreateContext(existingHeaders: existing);

            _filter.OnResultExecuted(context);

            Assert.IsNull(context.HttpContext.Response.Headers["X-AspNet-Version"],
                "X-AspNet-Version header should be removed.");
        }

        [TestMethod]
        public void OnResultExecuted_RemovesXAspNetMvcVersion()
        {
            var existing = new NameValueCollection { { "X-AspNetMvc-Version", "5.2" } };
            var context = CreateContext(existingHeaders: existing);

            _filter.OnResultExecuted(context);

            Assert.IsNull(context.HttpContext.Response.Headers["X-AspNetMvc-Version"],
                "X-AspNetMvc-Version header should be removed.");
        }

        // ---- CSP allows required CDN domains ----

        [TestMethod]
        public void CSP_AllowsBootstrapCDN()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            var csp = context.HttpContext.Response.Headers["Content-Security-Policy"];
            Assert.IsTrue(csp.Contains("cdn.jsdelivr.net"),
                "CSP should allow cdn.jsdelivr.net for Bootstrap.");
        }

        [TestMethod]
        public void CSP_AllowsGoogleFonts()
        {
            var context = CreateContext();
            _filter.OnResultExecuted(context);

            var csp = context.HttpContext.Response.Headers["Content-Security-Policy"];
            Assert.IsTrue(csp.Contains("fonts.googleapis.com"),
                "CSP should allow Google Fonts stylesheets.");
            Assert.IsTrue(csp.Contains("fonts.gstatic.com"),
                "CSP should allow Google Fonts font files.");
        }

        // ---- Fake MVC context types ----

        private class FakeController : Controller
        {
            public ActionResult Index() => new EmptyResult();
        }

        private class FakeHttpContext : HttpContextBase
        {
            private readonly FakeHttpRequest _request;
            private readonly FakeHttpResponse _response;

            public FakeHttpContext(FakeHttpRequest request, FakeHttpResponse response)
            {
                _request = request;
                _response = response;
            }

            public override HttpRequestBase Request => _request;
            public override HttpResponseBase Response => _response;
        }

        private class FakeHttpRequest : HttpRequestBase
        {
            private readonly bool _isSecure;

            public FakeHttpRequest(bool isSecure) => _isSecure = isSecure;

            public override bool IsSecureConnection => _isSecure;
        }

        private class FakeHttpResponse : HttpResponseBase
        {
            private readonly NameValueCollection _headers;

            public FakeHttpResponse(NameValueCollection headers) => _headers = headers;

            public override NameValueCollection Headers => _headers;
        }
    }
}
