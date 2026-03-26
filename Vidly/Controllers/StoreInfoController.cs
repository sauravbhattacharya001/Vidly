using System;
using System.Web.Mvc;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Controllers
{
    /// <summary>
    /// Store hours and location information for customers.
    /// </summary>
    public class StoreInfoController : Controller
    {
        private readonly StoreInfoService _storeInfoService;

        public StoreInfoController()
            : this(new StoreInfoService())
        {
        }

        public StoreInfoController(StoreInfoService storeInfoService)
        {
            _storeInfoService = storeInfoService
                ?? throw new ArgumentNullException(nameof(storeInfoService));
        }

        /// <summary>
        /// GET: StoreInfo — Lists all store locations with hours.
        /// </summary>
        public ActionResult Index()
        {
            var viewModel = new StoreInfoViewModel
            {
                Stores = _storeInfoService.GetAllStores()
            };

            return View(viewModel);
        }

        /// <summary>
        /// GET: StoreInfo/Details/1 — Shows details for a specific store.
        /// </summary>
        public ActionResult Details(int id)
        {
            var store = _storeInfoService.GetStoreById(id);
            if (store == null)
                return HttpNotFound("Store not found.");

            var viewModel = new StoreInfoViewModel
            {
                Stores = _storeInfoService.GetAllStores(),
                SelectedStore = store
            };

            return View(viewModel);
        }
    }
}
