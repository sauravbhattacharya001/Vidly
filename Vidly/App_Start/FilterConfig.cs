using System.Web;
using System.Web.Mvc;
using Vidly.Filters;

namespace Vidly
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new SecurityHeadersAttribute());
        }
    }
}
